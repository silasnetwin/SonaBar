using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Voxora.Transcription;

/// <summary>
/// Owns the private Python process that runs Qwen3-ASR. The pipe name is random per launch and
/// both directions prove knowledge of a per-launch secret before any model work starts.
/// </summary>
internal sealed class QwenSidecar : IAsyncDisposable
{
    private readonly NamedPipeServerStream pipe;
    private readonly Process process;
    private readonly string secret;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pending = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource lifetime = new();
    private readonly StringBuilder diagnostics = new();
    private readonly object diagnosticLock = new();
    private Task reader = Task.CompletedTask;
    private int disposed;
    private int stopped;

    public static bool RuntimeAvailable => ResolvePythonPath() is not null && ResolveScriptPath() is not null;
    public bool IsRunning
    {
        get
        {
            if (Volatile.Read(ref stopped) != 0 || Volatile.Read(ref disposed) != 0) return false;
            try { return !process.HasExited; } catch { return false; }
        }
    }

    public static string? ResolvePythonPath()
    {
        var configured = Environment.GetEnvironmentVariable("SINEFLOW_QWEN_PYTHON");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);
        var packaged = Path.Combine(AppContext.BaseDirectory, "qwen", "python.exe");
        return File.Exists(packaged) ? packaged : null;
    }

    public static string? ResolveScriptPath()
    {
        var configured = Environment.GetEnvironmentVariable("SINEFLOW_QWEN_SIDECAR");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);
        var packaged = Path.Combine(AppContext.BaseDirectory, "qwen", "qwen_sidecar.py");
        return File.Exists(packaged) ? packaged : null;
    }

    private QwenSidecar(NamedPipeServerStream pipe, Process process, string secret)
    {
        this.pipe = pipe;
        this.process = process;
        this.secret = secret;
    }

    public static async Task<QwenSidecar> StartAsync(string modelPath, CancellationToken cancellationToken)
    {
        var python = ResolvePythonPath() ?? throw new InvalidOperationException("The embedded Qwen runtime is missing. Reinstall SineFlow or run the Qwen packaging step.");
        var script = ResolveScriptPath() ?? throw new InvalidOperationException("The Qwen sidecar script is missing. Reinstall SineFlow.");
        if (!Directory.Exists(modelPath)) throw new DirectoryNotFoundException($"The Qwen model directory was not found: {modelPath}");
        var pipeName = "SineFlow.Qwen." + Guid.NewGuid().ToString("N");
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 65536, 65536);
        var start = new ProcessStartInfo
        {
            FileName = python,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(script)!
        };
        start.ArgumentList.Add("-I");
        start.ArgumentList.Add("-X");
        start.ArgumentList.Add("utf8");
        start.ArgumentList.Add(script);
        start.Environment["SINEFLOW_QWEN_PIPE"] = pipeName;
        start.Environment["SINEFLOW_QWEN_SECRET"] = secret;
        start.Environment["SINEFLOW_QWEN_MODEL"] = Path.GetFullPath(modelPath);
        start.Environment["SINEFLOW_QWEN_THREADS"] = Math.Clamp(Environment.ProcessorCount, 1, 16).ToString(CultureInfo.InvariantCulture);
        start.Environment["SINEFLOW_QWEN_DTYPE"] = Environment.GetEnvironmentVariable("SINEFLOW_QWEN_DTYPE") ?? "bfloat16";
        start.Environment["HF_HUB_OFFLINE"] = "1";
        start.Environment["TRANSFORMERS_OFFLINE"] = "1";
        start.Environment["PYTHONUNBUFFERED"] = "1";
        start.Environment["PYTHONNOUSERSITE"] = "1";
        start.Environment["TOKENIZERS_PARALLELISM"] = "false";
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var sidecar = new QwenSidecar(server, process, secret);
        process.OutputDataReceived += (_, args) => sidecar.AppendDiagnostic(args.Data);
        process.ErrorDataReceived += (_, args) => sidecar.AppendDiagnostic(args.Data);
        try
        {
            if (!process.Start()) throw new InvalidOperationException("The Qwen sidecar did not start.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connect.CancelAfter(TimeSpan.FromSeconds(30));
            try { await server.WaitForConnectionAsync(connect.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The Qwen sidecar did not connect in time." + sidecar.DiagnosticSuffix());
            }
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshake.CancelAfter(TimeSpan.FromSeconds(15));
            var hello = await ReadJsonAsync(server, handshake.Token).ConfigureAwait(false);
            var op = GetString(hello, "op");
            var nonce = GetString(hello, "nonce");
            if (op != "hello" || string.IsNullOrWhiteSpace(nonce) || nonce.Length > 512)
                throw new InvalidDataException("The Qwen sidecar sent an invalid handshake.");
            await QwenProtocol.WriteFrameAsync(server, QwenProtocol.Encode(new
            {
                op = "auth",
                proof = QwenProtocol.CreateProof(secret, "client", nonce)
            }), handshake.Token).ConfigureAwait(false);
            var ready = await ReadJsonAsync(server, handshake.Token).ConfigureAwait(false);
            if (GetString(ready, "op") != "ready" || !QwenProtocol.VerifyProof(secret, "server", nonce, GetString(ready, "proof")))
                throw new InvalidDataException("The Qwen sidecar failed authentication.");
            sidecar.reader = sidecar.ReadLoopAsync();
            return sidecar;
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            try { server.Dispose(); } catch { }
            process.Dispose();
            throw;
        }
    }

    public async Task<JsonElement> WarmAsync(string id, CancellationToken cancellationToken) =>
        await RequestAsync(id, new { op = "warm", id }, cancellationToken).ConfigureAwait(false);

    public async Task<JsonElement> TranscribeAsync(string id, string waveFile, string language, string prompt, int maxNewTokens, CancellationToken cancellationToken) =>
        await RequestAsync(id, new { op = "transcribe", id, wave = waveFile, language, prompt, maxNewTokens }, cancellationToken).ConfigureAwait(false);

    public async Task UnloadAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref disposed) != 0 || Volatile.Read(ref stopped) != 0) return;
        var id = "unload-" + Guid.NewGuid().ToString("N");
        try { await RequestAsync(id, new { op = "unload", id }, cancellationToken).ConfigureAwait(false); }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { }
    }

    private async Task<JsonElement> RequestAsync(string id, object request, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref disposed) != 0 || Volatile.Read(ref stopped) != 0) throw new InvalidOperationException("The Qwen sidecar is not running.");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pending.TryAdd(id, completion)) throw new InvalidOperationException("A Qwen request with that id is already running.");
        using var registration = cancellationToken.Register(() => _ = CancelAsync(id));
        try
        {
            await SendAsync(request, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
            if (finished != completion.Task) Stop();
            throw;
        }
        finally { pending.TryRemove(id, out _); }
    }

    private async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await QwenProtocol.WriteFrameAsync(pipe, QwenProtocol.Encode(message), cancellationToken).ConfigureAwait(false); }
        finally { writeGate.Release(); }
    }

    private async Task CancelAsync(string id)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await SendAsync(new { op = "cancel", id }, timeout.Token).ConfigureAwait(false);
        }
        catch { }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var payload = await QwenProtocol.ReadFrameAsync(pipe, lifetime.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                var op = GetString(root, "op");
                var id = GetString(root, "id");
                if (op is "progress" or "log") continue;
                if (id is not null && pending.TryRemove(id, out var completion)) completion.TrySetResult(root.Clone());
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            FailAll(new IOException("The Qwen sidecar stopped unexpectedly." + DiagnosticSuffix(), error));
            Stop();
        }
    }

    private void FailAll(Exception error)
    {
        foreach (var pair in pending)
            if (pending.TryRemove(pair.Key, out var completion)) completion.TrySetException(error);
    }

    private void Stop()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        try { lifetime.Cancel(); } catch { }
        try { if (!process.HasExited) process.Kill(true); } catch { }
    }

    private void AppendDiagnostic(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (diagnosticLock)
        {
            if (diagnostics.Length > 12_000) diagnostics.Remove(0, diagnostics.Length - 8_000);
            diagnostics.AppendLine(line.Trim());
        }
    }

    private string DiagnosticSuffix()
    {
        lock (diagnosticLock) return diagnostics.Length == 0 ? "" : " Details: " + diagnostics.ToString().Trim();
    }

    private static async Task<JsonElement> ReadJsonAsync(Stream stream, CancellationToken cancellationToken)
    {
        var payload = await QwenProtocol.ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await SendAsync(new { op = "shutdown" }, timeout.Token).ConfigureAwait(false);
        }
        catch { }
        Stop();
        try { await reader.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
        try { pipe.Dispose(); } catch { }
        try { process.Dispose(); } catch { }
        writeGate.Dispose();
        lifetime.Dispose();
    }
}
