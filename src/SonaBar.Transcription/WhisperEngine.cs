using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.Wave;
using SonaBar.Core;

namespace SonaBar.Transcription;

/// <summary>Direct, pinned whisper.cpp v1.9.4 adapter. No managed Whisper runtime is loaded.</summary>
public sealed class WhisperEngine : ITranscriptionEngine
{
    private static readonly JsonSerializerOptions nativeJson = new() { PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim gate = new(1, 1);
    private IntPtr context;
    private string? loadedPath;
    private static bool preferGpu = true;
    private bool forceCpu;
    public event Action<string>? BackendNotice;
    public TranscriptionEngineCapabilities Capabilities => new(false, true, true, Languages.All.Keys.ToArray(), "whisper.cpp v1.9.4");
    public static void ConfigureBackend(string backend) => preferGpu = backend == "GPU / accelerated";

    public async Task WarmAsync(string modelPath, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { await Task.Run(() => Load(modelPath), cancellationToken); }
        finally { gate.Release(); }
    }

    public async Task<TranscriptionResult> TranscribeAsync(string waveFile, string modelPath, string language, string prompt, bool keepLoaded, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested(); Load(modelPath);
                var samples = Read16kMonoWave(waveFile, cancellationToken);
                using var registration = cancellationToken.Register(() => Native.Cancel(context));
                // Four threads leave modern desktop CPUs mostly idle during recognition.
                // Reserve one logical processor for the shell/capture path, but allow enough
                // parallelism for a dictation to complete as soon as recording stops.
                var response = Native.Transcribe(context, samples, language, prompt, RecognitionThreadCount(Environment.ProcessorCount));
                cancellationToken.ThrowIfCancellationRequested();
                return ParseNativeResult(response, language);
            }, CancellationToken.None);
        }
        finally { if (!keepLoaded) Unload(); gate.Release(); }
    }

    public async Task UnloadAsync(CancellationToken cancellationToken) { await gate.WaitAsync(cancellationToken); try { Unload(); } finally { gate.Release(); } }
    private void Load(string modelPath)
    {
        if (loadedPath == modelPath && context != IntPtr.Zero) return;
        Unload();
        try { context = Native.Load(modelPath, preferGpu && !forceCpu); }
        catch (Exception) when (preferGpu && !forceCpu) { forceCpu = true; context = Native.Load(modelPath, false); BackendNotice?.Invoke("GPU acceleration failed. Switched to CPU."); }
        loadedPath = modelPath;
    }
    private void Unload() { if (context != IntPtr.Zero) Native.Unload(context); context = IntPtr.Zero; loadedPath = null; }
    private static float[] Read16kMonoWave(string path, CancellationToken token)
    {
        using var reader = new AudioFileReader(path);
        if (reader.WaveFormat.SampleRate != 16_000 || reader.WaveFormat.Channels != 1) throw new InvalidDataException("SonaBar speech input must be a 16 kHz mono WAV file.");
        var samples = new List<float>((int)Math.Min(reader.Length / Math.Max(1, reader.WaveFormat.BlockAlign), 16_000 * 60)); var buffer = new float[16_384]; int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); samples.AddRange(buffer.AsSpan(0, read).ToArray()); }
        return samples.ToArray();
    }
    public void Dispose() { Unload(); gate.Dispose(); }
    internal static int RecognitionThreadCount(int logicalProcessorCount) => Math.Clamp(logicalProcessorCount - 1, 1, 16);
    internal static TranscriptionResult ParseNativeResult(string response, string requestedLanguage)
    {
        var native = JsonSerializer.Deserialize<NativeResult>(response, nativeJson) ?? throw new InvalidDataException("whisper.cpp returned an invalid transcription result.");
        if (native.Segments is null) throw new InvalidDataException("whisper.cpp returned no segment list.");
        var segments = native.Segments.Select(x => new TranscriptSegment(TimeSpan.FromMilliseconds(x.StartMs), TimeSpan.FromMilliseconds(x.EndMs), TextProcessing.Clean(x.Text))).Where(x => x.Text.Length > 0).ToList();
        return new TranscriptionResult(TextProcessing.Clean(string.Join(" ", segments.Select(x => x.Text))), string.IsNullOrWhiteSpace(native.Language) ? requestedLanguage : native.Language, segments.LastOrDefault()?.End ?? TimeSpan.Zero, segments);
    }
    private sealed record NativeResult(string Language, List<NativeSegment> Segments);
    private sealed record NativeSegment(long StartMs, long EndMs, string Text);
    private static class Native
    {
        private const string Library = "SonaBarWhisperNative";
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sf_whisper_load")] private static extern IntPtr LoadNative([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.I1)] bool gpu, out IntPtr error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sf_whisper_unload")] internal static extern void Unload(IntPtr context);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sf_whisper_cancel")] internal static extern void Cancel(IntPtr context);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sf_whisper_transcribe")] private static extern int TranscribeNative(IntPtr context, float[] samples, int count, [MarshalAs(UnmanagedType.LPUTF8Str)] string language, [MarshalAs(UnmanagedType.LPUTF8Str)] string prompt, int threads, out IntPtr result, out IntPtr error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sf_whisper_free_string")] private static extern void FreeString(IntPtr value);
        internal static IntPtr Load(string path, bool gpu) { var result = LoadNative(path, gpu, out var error); try { if (result == IntPtr.Zero) throw new InvalidOperationException(Read(error) ?? "whisper.cpp could not load the selected model."); return result; } finally { if (error != IntPtr.Zero) FreeString(error); } }
        internal static string Transcribe(IntPtr context, float[] samples, string language, string prompt, int threads) { var status = TranscribeNative(context, samples, samples.Length, language, prompt, threads, out var result, out var error); try { if (status == 1) throw new OperationCanceledException(); if (status != 0) throw new InvalidOperationException(Read(error) ?? "whisper.cpp could not transcribe this audio."); return Read(result) ?? throw new InvalidDataException("whisper.cpp returned no transcription."); } finally { if (result != IntPtr.Zero) FreeString(result); if (error != IntPtr.Zero) FreeString(error); } }
        private static string? Read(IntPtr value) => value == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(value);
    }
}
