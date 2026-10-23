using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
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
        foreach (var m in Catalog) Validate(m);
    }
    private static void Validate(ModelDefinition model)
    {
        if (model.Sha256.Length != 64 || !model.Sha256.All(Uri.IsHexDigit) || model.Bytes <= 0 || new Uri(model.Url).Scheme != "https")
            throw new InvalidDataException("Invalid model catalog.");
        if (!model.IsBundle) return;
        if (model.Files.Sum(file => file.Bytes) != model.Bytes) throw new InvalidDataException("Invalid model catalog bundle size.");
        foreach (var file in model.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Path) || Path.IsPathRooted(file.Path) || file.Path.Split('/', '\\').Any(part => part is "" or "." or ".."))
                throw new InvalidDataException("Invalid model catalog bundle path.");
            if (file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit) || file.Bytes <= 0 || new Uri(file.Url).Scheme != "https")
                throw new InvalidDataException("Invalid model catalog bundle file.");
        }
    }
    private static string ExtensionFor(string engine) => engine switch { "Whisper" => ".bin", "ONNX" => ".onnx", "Qwen3-ASR" => "", _ => ".gguf" };
    public string PathFor(ModelDefinition model) => Path.Combine(root, model.Id + (model.IsBundle ? "" : ExtensionFor(model.Engine)));
    private string FilePathFor(ModelDefinition model, ModelFile file)
    {
        var directory = Path.GetFullPath(PathFor(model)) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(directory, file.Path));
        if (!path.StartsWith(directory, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid model bundle path.");
        return path;
    }
    private static string BundleFingerprint(ModelDefinition model) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", model.Files.Select(file => $"{file.Path}\t{file.Sha256.ToLowerInvariant()}\t{file.Bytes}"))))).ToLowerInvariant();
    public bool IsInstalled(ModelDefinition model)
    {
        try
        {
            if (model.IsBundle) return BundleInstalled(model);
            var path = PathFor(model);
            return File.Exists(path) && new FileInfo(path).Length == model.Bytes && File.Exists(path + ".verified") && File.ReadAllText(path+".verified").Trim().Equals(model.Sha256,StringComparison.OrdinalIgnoreCase);
        }
        catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
    }
    private bool BundleInstalled(ModelDefinition model)
    {
        var directory = PathFor(model);
        if (!Directory.Exists(directory)) return false;
        var marker = Path.Combine(directory, ".verified");
        if (!File.Exists(marker) || !File.ReadAllText(marker).Trim().Equals(BundleFingerprint(model), StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var file in model.Files)
        {
            var path = FilePathFor(model, file);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Bytes) return false;
        }
        return true;
    }
    public ModelDefinition? ResolveSpeech(string selected) => Catalog.Where(m => (m.Engine is "Whisper" or "NeMo-Speech.cpp" or "Qwen3-ASR") && IsInstalled(m)).OrderByDescending(m => m.Id == selected).ThenBy(m => m.Bytes).FirstOrDefault();
    public async Task<bool> VerifyAsync(ModelDefinition model, CancellationToken token)
    {
        if (model.IsBundle)
        {
            var directory = PathFor(model);
            var marker = Path.Combine(directory, ".verified");
            foreach (var file in model.Files)
            {
                token.ThrowIfCancellationRequested();
                var path = FilePathFor(model, file);
                if (!File.Exists(path) || new FileInfo(path).Length != file.Bytes) { File.Delete(marker); return false; }
                await using var stream = File.OpenRead(path);
                if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) { File.Delete(marker); return false; }
            }
            await File.WriteAllTextAsync(marker, BundleFingerprint(model), token);
            return true;
        }
        var filePath = PathFor(model);
        if (!File.Exists(filePath)) return false;
        await using var fileStream = File.OpenRead(filePath);
        var valid = Convert.ToHexString(await SHA256.HashDataAsync(fileStream, token)).Equals(model.Sha256, StringComparison.OrdinalIgnoreCase);
        if (valid) await File.WriteAllTextAsync(filePath + ".verified", model.Sha256, token);
        else File.Delete(filePath + ".verified");
        return valid;
    }
    public async Task EnsureVerifiedAsync(ModelDefinition model,CancellationToken token)
    {
        if (model.IsBundle)
        {
            var directory = PathFor(model);
            if (!Directory.Exists(directory)) throw new InvalidDataException("The model is not installed. Download it again in Models.");
            var bundleMarker = new FileInfo(Path.Combine(directory, ".verified"));
            var newest = model.Files.Max(file => new FileInfo(FilePathFor(model, file)).LastWriteTimeUtc);
            if (BundleInstalled(model) && bundleMarker.LastWriteTimeUtc >= newest) return;
            if (!await VerifyAsync(model, token)) throw new InvalidDataException("The model is damaged. Delete and download it again in Models.");
            return;
        }
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
        try
        {
            var path = PathFor(model);
            if (model.IsBundle)
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else File.Delete(path);
            }
            else foreach (var suffix in new[] { "", ".partial", ".verified" }) File.Delete(path + suffix);
        }
        finally { gate.Release(); }
    }
    public async Task DownloadAsync(ModelDefinition model, IProgress<DownloadProgress>? progress, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (model.IsBundle) await DownloadBundleAsync(model, progress, token);
            else await DownloadSingleAsync(model, progress, token);
        }
        catch(OperationCanceledException error)when(!token.IsCancellationRequested){throw new IOException("The model download stopped responding. Check your connection and choose Resume.",error);}
        finally { gate.Release(); }
    }
    private async Task DownloadSingleAsync(ModelDefinition model, IProgress<DownloadProgress>? progress, CancellationToken token)
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
    private async Task DownloadBundleAsync(ModelDefinition model, IProgress<DownloadProgress>? progress, CancellationToken token)
    {
        var directory = PathFor(model);
        Directory.CreateDirectory(directory);
        var remaining = model.Files.Sum(file =>
        {
            var target = FilePathFor(model, file);
            var partial = target + ".partial";
            var present = (File.Exists(target) ? new FileInfo(target).Length : 0) + (File.Exists(partial) ? new FileInfo(partial).Length : 0);
            return Math.Max(0, file.Bytes - Math.Min(file.Bytes, present));
        });
        if (new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace < remaining + 64 * 1024 * 1024) throw new IOException("Not enough free disk space for this model.");
        long completed = 0;
        foreach (var file in model.Files)
        {
            token.ThrowIfCancellationRequested();
            var target = FilePathFor(model, file);
            var partial = target + ".partial";
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target))
            {
                if (new FileInfo(target).Length == file.Bytes) { completed += file.Bytes; progress?.Report(new(completed, model.Bytes, "Downloading")); continue; }
                File.Delete(target);
            }
            long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            if (offset > file.Bytes) { File.Delete(partial); offset = 0; }
            if (offset < file.Bytes)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, file.Url);
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
                    if (offset + read > file.Bytes) throw new IOException("Model download exceeded its expected size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token); offset += read;
                    if(reported.ElapsedMilliseconds>=100){progress?.Report(new(completed + offset, model.Bytes, "Downloading " + file.Path));reported.Restart();}
                }
            }
            if (new FileInfo(partial).Length != file.Bytes) throw new IOException("Download interrupted. Choose Resume to continue.");
            progress?.Report(new(completed + file.Bytes, model.Bytes, "Verifying " + file.Path));
            await using (var stream = File.OpenRead(partial))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    stream.Close(); File.Delete(partial); throw new IOException("Model verification failed. The damaged download was removed; please retry.");
                }
            }
            File.Move(partial, target, true);
            completed += file.Bytes;
            progress?.Report(new(completed, model.Bytes, "Downloading"));
        }
        progress?.Report(new(model.Bytes, model.Bytes, "Verifying SHA-256"));
        foreach (var file in model.Files)
        {
            token.ThrowIfCancellationRequested();
            var target = FilePathFor(model, file);
            await using var stream = File.OpenRead(target);
            if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                stream.Close(); File.Delete(target); File.Delete(Path.Combine(directory, ".verified"));
                throw new IOException("Model verification failed. The damaged download was removed; please retry.");
            }
        }
        await File.WriteAllTextAsync(Path.Combine(directory, ".verified"), BundleFingerprint(model), token);
        progress?.Report(new(model.Bytes, model.Bytes, "Ready offline"));
    }
    // Do not dispose the semaphore while a cancelled download is unwinding its finally block.
    public void Dispose() { http.Dispose(); }
}
