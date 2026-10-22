using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Voxora.Core;

namespace Voxora.Models;

public sealed class ModelManager : IModelManager, IDisposable
{
    private readonly string root;
    private readonly HttpClient http;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string,(long Length,DateTime LastWriteUtc)> verifiedThisSession=[];
    private readonly object verificationLock=new();
    public IReadOnlyList<ModelDefinition> Catalog { get; }
    public ModelManager(string? root = null, HttpMessageHandler? handler = null)
    {
        this.root = root ?? Path.Combine(Brand.DataRoot, "Models");
        Directory.CreateDirectory(this.root);
        http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = Timeout.InfiniteTimeSpan;
        using var stream = typeof(ModelManager).Assembly.GetManifestResourceStream("SineFlow.Models.catalog.json")!;
        Catalog = JsonSerializer.Deserialize<List<ModelDefinition>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        foreach (var m in Catalog)
            if (m.Sha256.Length != 64 || !m.Sha256.All(Uri.IsHexDigit) || m.Bytes <= 0 || new Uri(m.Url).Scheme != "https") throw new InvalidDataException("Invalid model catalog.");
    }
    public string PathFor(ModelDefinition model) => Path.Combine(root, model.Id + (model.Engine == "Whisper" ? ".bin" : model.Engine == "ONNX" ? ".onnx" : ".gguf"));
    public bool IsInstalled(ModelDefinition model)
    {
        try{return File.Exists(PathFor(model)) && new FileInfo(PathFor(model)).Length == model.Bytes && File.Exists(PathFor(model) + ".verified") && File.ReadAllText(PathFor(model)+".verified").Trim().Equals(model.Sha256,StringComparison.OrdinalIgnoreCase);}
        catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
    }
    public ModelDefinition? ResolveSpeech(string selected) => Catalog.Where(m => m.Engine is "Whisper" or "NeMo-Speech.cpp" && IsInstalled(m)).OrderByDescending(m => m.Id == selected).ThenBy(m => m.Bytes).FirstOrDefault();
    public async Task<bool> VerifyAsync(ModelDefinition model, CancellationToken token)
    {
        if (!File.Exists(PathFor(model))) return false;
        await using var stream = File.OpenRead(PathFor(model));
        var valid = Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(model.Sha256, StringComparison.OrdinalIgnoreCase);
        if (!valid) File.Delete(PathFor(model) + ".verified");
        return valid;
    }
    public async Task EnsureVerifiedAsync(ModelDefinition model,CancellationToken token)
    {
        var path=PathFor(model);
        var file=new FileInfo(path);
        var stamp=(file.Length,file.LastWriteTimeUtc);
        lock(verificationLock)if(verifiedThisSession.TryGetValue(model.Id,out var previous)&&previous==stamp)return;
        // A model is hashed before it is promoted from .partial and the signed catalog hash is
        // recorded beside it. Re-hashing several gigabytes at first dictation makes the normal
        // interaction feel broken, while IsInstalled already treats a missing/stale marker as
        // untrusted. Keep the stronger full hash for imports without that marker.
        var marker=new FileInfo(path+".verified");
        if(IsInstalled(model)&&marker.LastWriteTimeUtc>=stamp.LastWriteTimeUtc) { lock(verificationLock)verifiedThisSession[model.Id]=stamp; return; }
        if(!await VerifyAsync(model,token))throw new InvalidDataException("The model is damaged. Delete and download it again in Models.");
        lock(verificationLock)verifiedThisSession[model.Id]=stamp;
    }
    public void Delete(ModelDefinition model)
    {
        if (!gate.Wait(0)) throw new InvalidOperationException("Pause the download before deleting a model.");
        try { foreach (var suffix in new[] { "", ".partial", ".verified" }) File.Delete(PathFor(model) + suffix); }
        finally { gate.Release(); }
    }
    public async Task DownloadAsync(ModelDefinition model, IProgress<DownloadProgress>? progress, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var path = PathFor(model); var partial = path + ".partial";
            long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            if (offset > model.Bytes) { File.Delete(partial); offset = 0; }
            if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < model.Bytes - offset + 64 * 1024 * 1024) throw new IOException("Not enough free disk space for this model.");
            if (offset < model.Bytes)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, model.Url);
                if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.EnsureSuccessStatusCode();
                if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new IOException("Insecure model redirect rejected.");
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    if (response.Content.Headers.ContentRange?.From != offset) throw new IOException("The download server returned an invalid byte range.");
                }
                else offset = 0;
                await using var output = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 131072, true);
                await using var input = await response.Content.ReadAsStreamAsync(token);
                var buffer = new byte[131072]; int read;var reported=System.Diagnostics.Stopwatch.StartNew();
                while (true)
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(45));
                    read=await input.ReadAsync(buffer,timeout.Token);if(read==0)break;
                    if (offset + read > model.Bytes) throw new IOException("Model download exceeded its expected size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token); offset += read;
                    if(reported.ElapsedMilliseconds>=100){progress?.Report(new(offset, model.Bytes, "Downloading"));reported.Restart();}
                }
            }
            if (new FileInfo(partial).Length != model.Bytes) throw new IOException("Download interrupted. Choose Resume to continue.");
            progress?.Report(new(model.Bytes, model.Bytes, "Verifying SHA-256"));
            await using (var stream = File.OpenRead(partial))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                if (!hash.Equals(model.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    stream.Close(); File.Delete(partial); throw new IOException("Model verification failed. The damaged download was removed; please retry.");
                }
            }
            File.Move(partial, path, true);
            await File.WriteAllTextAsync(path + ".verified", model.Sha256, token);
            var installed=new FileInfo(path); lock(verificationLock)verifiedThisSession[model.Id]=(installed.Length,installed.LastWriteTimeUtc);
            progress?.Report(new(model.Bytes, model.Bytes, "Ready offline"));
        }
        catch(OperationCanceledException error)when(!token.IsCancellationRequested){throw new IOException("The model download stopped responding. Check your connection and choose Resume.",error);}
        finally { gate.Release(); }
    }
    // Do not dispose the semaphore while a cancelled download is unwinding its finally block.
    public void Dispose() { http.Dispose(); }
}
