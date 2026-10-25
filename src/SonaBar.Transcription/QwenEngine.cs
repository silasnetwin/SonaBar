using System.Text.Json;
using SonaBar.Core;

namespace SonaBar.Transcription;

/// <summary>
/// Qwen3-ASR runs in an authenticated, private Python sidecar because its native Transformers
/// runtime is Python-only. No network listener is created and the model directory is opened
/// offline.
/// </summary>
public sealed class QwenEngine : ITranscriptionEngine
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private QwenSidecar? sidecar;
    private string? sidecarModelPath;
    private int disposed;

    public static bool RuntimeAvailable => QwenSidecar.RuntimeAvailable;

    public TranscriptionEngineCapabilities Capabilities =>
        new(false, true, true, QwenLanguages.Codes, "Qwen3-ASR 1.7B via Transformers 5.20.2 (CPU)");

    public async Task WarmAsync(string modelPath, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var client = await EnsureStartedAsync(modelPath, cancellationToken).ConfigureAwait(false);
            var id = Guid.NewGuid().ToString("N");
            var response = await client.WarmAsync(id, cancellationToken).ConfigureAwait(false);
            if (GetString(response, "op") != "warmed") throw Failure(response);
        }
        catch
        {
            await ResetAsync().ConfigureAwait(false);
            throw;
        }
        finally { gate.Release(); }
    }

    public async Task<TranscriptionResult> TranscribeAsync(string waveFile, string modelPath, string language, string prompt, bool keepLoaded, CancellationToken cancellationToken)
    {
        if (!QwenLanguages.IsSupported(language))
            throw new NotSupportedException("Qwen3-ASR does not support the selected language. Choose Automatic or a language listed for this model.");
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var client = await EnsureStartedAsync(modelPath, cancellationToken).ConfigureAwait(false);
            var normalized = QwenLanguages.Normalize(language);
            var id = Guid.NewGuid().ToString("N");
            var response = await client.TranscribeAsync(id, Path.GetFullPath(waveFile), normalized, prompt ?? "", 1024, cancellationToken).ConfigureAwait(false);
            var op = GetString(response, "op");
            if (op == "cancelled" || (op == "error" && GetBoolean(response, "cancelled"))) throw new OperationCanceledException("Qwen3-ASR transcription was canceled.");
            if (op != "result") throw Failure(response);
            var text = GetString(response, "text") ?? "";
            var detected = GetString(response, "languageCode");
            if (string.IsNullOrWhiteSpace(detected) || detected == "auto") detected = normalized == "auto" ? language : normalized;
            if (string.Equals(detected, "fil", StringComparison.OrdinalIgnoreCase)) detected = "tl";
            var duration = GetDouble(response, "durationSeconds");
            var clean = TextProcessing.Clean(text);
            var segments = string.IsNullOrWhiteSpace(clean) ? [] : new[] { new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(duration), clean) };
            return new TranscriptionResult(clean, detected, TimeSpan.FromSeconds(duration), segments);
        }
        finally
        {
            try { if (!keepLoaded) await ResetAsync().ConfigureAwait(false); }
            finally { gate.Release(); }
        }
    }

    private async Task<QwenSidecar> EnsureStartedAsync(string modelPath, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(modelPath);
        if (sidecar is not null && sidecar.IsRunning && string.Equals(sidecarModelPath, fullPath, StringComparison.OrdinalIgnoreCase)) return sidecar;
        await ResetAsync().ConfigureAwait(false);
        sidecar = await QwenSidecar.StartAsync(fullPath, cancellationToken).ConfigureAwait(false);
        sidecarModelPath = fullPath;
        return sidecar;
    }

    private async Task ResetAsync()
    {
        var current = sidecar;
        sidecar = null;
        sidecarModelPath = null;
        if (current is null) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await current.UnloadAsync(timeout.Token).ConfigureAwait(false);
        }
        catch { }
        finally { await current.DisposeAsync().ConfigureAwait(false); }
    }

    public async Task UnloadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await ResetAsync().ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    public void Dispose()
    {
        // The router and the service container both own the engine, so disposal must be safe
        // more than once. Waiting on a disposed semaphore would otherwise abort shutdown.
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        gate.Wait();
        try { ResetAsync().GetAwaiter().GetResult(); }
        finally { gate.Release(); gate.Dispose(); }
    }

    private static InvalidOperationException Failure(JsonElement response)
    {
        var message = GetString(response, "message");
        return new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "Qwen3-ASR could not complete transcription." : message);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBoolean(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static double GetDouble(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
}
