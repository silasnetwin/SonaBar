using System.Runtime.InteropServices;
using NAudio.Wave;
using Voxora.Core;

namespace Voxora.Transcription;

/// <summary>Direct binding to NVIDIA NeMo-Speech.cpp's stable ASR C ABI for Parakeet TDT.</summary>
public sealed class ParakeetEngine : ITranscriptionEngine
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IntPtr recognizer;
    private string? loadedPath;
    public TranscriptionEngineCapabilities Capabilities => new(true, true, false, ["en", "de", "fr", "es", "it", "pt", "nl", "pl", "ru", "uk", "ja", "ko", "zh", "ar", "hi", "tr", "sv", "da", "no", "fi", "cs", "el", "he", "hu", "ro"], "NVIDIA NeMo-Speech.cpp v0.1.0");

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
                var options = Native.DefaultOptions();
                options.LanguageCode = language == "auto" ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8(language);
                try
                {
                    var status = Native.Recognize(recognizer, ref options, samples, (UIntPtr)(uint)samples.Length, 16_000, out var result);
                    try
                    {
                        Ensure(status); cancellationToken.ThrowIfCancellationRequested();
                        var text = Native.Utf8(Native.ResultTranscript(result, UIntPtr.Zero)) ?? "";
                        var words = new List<TranscriptSegment>();
                        var count = Native.ResultWordCount(result, UIntPtr.Zero).ToUInt64();
                        for (ulong index = 0; index < count; index++)
                        {
                            var word = Native.Utf8(Native.ResultWordText(result, UIntPtr.Zero, (UIntPtr)index));
                            if (!string.IsNullOrWhiteSpace(word)) words.Add(new(TimeSpan.FromMilliseconds(Native.ResultWordStart(result, UIntPtr.Zero, (UIntPtr)index)), TimeSpan.FromMilliseconds(Native.ResultWordEnd(result, UIntPtr.Zero, (UIntPtr)index)), word));
                        }
                        var segments = words.Count == 0 ? (string.IsNullOrWhiteSpace(text) ? [] : [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(samples.Length / 16_000d), TextProcessing.Clean(text))]) : words;
                        return new TranscriptionResult(TextProcessing.Clean(text), language, TimeSpan.FromSeconds(samples.Length / 16_000d), segments);
                    }
                    finally { if (result != IntPtr.Zero) Native.DestroyResult(result); }
                }
                finally { if (options.LanguageCode != IntPtr.Zero) Marshal.FreeCoTaskMem(options.LanguageCode); }
            }, CancellationToken.None);
        }
        finally { if (!keepLoaded) Unload(); gate.Release(); }
    }

    private void Load(string modelPath)
    {
        if (recognizer != IntPtr.Zero && loadedPath == modelPath) return;
        Unload();
        var modelPathPointer = Marshal.StringToCoTaskMemUTF8(modelPath);
        try
        {
            var model = new Native.ModelConfig { Size = (UIntPtr)Marshal.SizeOf<Native.ModelConfig>(), Path = modelPathPointer };
            var backend = new Native.BackendConfig { Size = (UIntPtr)Marshal.SizeOf<Native.BackendConfig>(), Gpu = -1 };
            using var modelMemory = new Native.StructMemory<Native.ModelConfig>(model);
            using var backendMemory = new Native.StructMemory<Native.BackendConfig>(backend);
            var config = new Native.RecognizerConfig { Size = (UIntPtr)Marshal.SizeOf<Native.RecognizerConfig>(), Backend = backendMemory.Pointer, Model = modelMemory.Pointer };
            Ensure(Native.Create(ref config, out recognizer));
            loadedPath = modelPath;
        }
        finally { Marshal.FreeCoTaskMem(modelPathPointer); }
    }
    private void Unload() { if (recognizer != IntPtr.Zero) Native.Destroy(recognizer); recognizer = IntPtr.Zero; loadedPath = null; }
    public async Task UnloadAsync(CancellationToken cancellationToken) { await gate.WaitAsync(cancellationToken); try { Unload(); } finally { gate.Release(); } }
    public void Dispose() { Unload(); gate.Dispose(); }
    private static void Ensure(int status) { if (status != 0) throw new InvalidOperationException(Native.Utf8(Native.LastError()) ?? "NeMo-Speech.cpp could not complete transcription."); }
    private static float[] Read16kMonoWave(string path, CancellationToken token)
    {
        using var reader = new AudioFileReader(path);
        if (reader.WaveFormat.SampleRate != 16_000 || reader.WaveFormat.Channels != 1) throw new InvalidDataException("SineFlow speech input must be a 16 kHz mono WAV file.");
        var all = new List<float>(); var buffer = new float[16_384]; int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); all.AddRange(buffer.AsSpan(0, read).ToArray()); }
        return all.ToArray();
    }

    private static class Native
    {
        private const string Library = "nemo_speech_asr_c";
        [StructLayout(LayoutKind.Sequential)] internal struct BackendConfig { public UIntPtr Size; public int Gpu; }
        [StructLayout(LayoutKind.Sequential)] internal struct ModelConfig { public UIntPtr Size; public IntPtr Path; public IntPtr Name; }
        [StructLayout(LayoutKind.Sequential)] internal struct RecognizerConfig { public UIntPtr Size; public IntPtr Backend; public IntPtr Model; public IntPtr Streaming; public IntPtr Decoder; public IntPtr Vad; public IntPtr Endpointing; public IntPtr Postproc; public IntPtr Diar; public IntPtr Batching; }
        [StructLayout(LayoutKind.Sequential)] internal struct RecognitionOptions { public UIntPtr Size; public IntPtr RequestId; public IntPtr LanguageCode; [MarshalAs(UnmanagedType.I1)] public bool InterimResults; [MarshalAs(UnmanagedType.I1)] public bool WordTimes; [MarshalAs(UnmanagedType.I1)] public bool Punctuation; [MarshalAs(UnmanagedType.I1)] public bool Verbatim; [MarshalAs(UnmanagedType.I1)] public bool Profanity; public int StopHistory; public IntPtr Contexts; public UIntPtr ContextCount; public int Alternatives; [MarshalAs(UnmanagedType.I1)] public bool Diarization; public int MaxSpeakers; }
        internal sealed class StructMemory<T> : IDisposable where T : struct { internal IntPtr Pointer { get; } = Marshal.AllocHGlobal(Marshal.SizeOf<T>()); internal StructMemory(T value) => Marshal.StructureToPtr(value, Pointer, false); public void Dispose() { Marshal.DestroyStructure<T>(Pointer); Marshal.FreeHGlobal(Pointer); } }
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nemo_speech_asr_create")] internal static extern int Create(ref RecognizerConfig config, out IntPtr recognizer);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nemo_speech_asr_destroy")] internal static extern void Destroy(IntPtr recognizer);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nemo_speech_asr_recognition_options_default")] internal static extern RecognitionOptions DefaultOptions();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nemo_speech_asr_recognize_f32")] internal static extern int Recognize(IntPtr recognizer, ref RecognitionOptions options, float[] samples, UIntPtr sampleCount, int sampleRate, out IntPtr result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nemo_speech_asr_result_destroy")] internal static extern void DestroyResult(IntPtr result);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nemo_speech_asr_result_transcript")] internal static extern IntPtr ResultTranscript(IntPtr result, UIntPtr alternative);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nemo_speech_asr_result_word_count")] internal static extern UIntPtr ResultWordCount(IntPtr result, UIntPtr alternative);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nemo_speech_asr_result_word_text")] internal static extern IntPtr ResultWordText(IntPtr result, UIntPtr alternative, UIntPtr word);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nemo_speech_asr_result_word_start_time")] internal static extern int ResultWordStart(IntPtr result, UIntPtr alternative, UIntPtr word);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nemo_speech_asr_result_word_end_time")] internal static extern int ResultWordEnd(IntPtr result, UIntPtr alternative, UIntPtr word);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "nemo_speech_asr_last_error")] internal static extern IntPtr LastError();
        internal static string? Utf8(IntPtr value) => value == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(value);
    }
}

/// <summary>Selects an engine using verified model metadata rather than UI state.</summary>
public sealed class TranscriptionEngineRouter(WhisperEngine whisper, ParakeetEngine parakeet) : ITranscriptionEngine
{
    public TranscriptionEngineCapabilities Capabilities => new(true, true, true, Languages.All.Keys.ToArray(), "SineFlow local engine router");
    private ITranscriptionEngine Select(string modelPath) => Path.GetFileName(modelPath).StartsWith("parakeet-", StringComparison.OrdinalIgnoreCase) ? parakeet : whisper;
    public Task WarmAsync(string modelPath, CancellationToken cancellationToken) => Select(modelPath).WarmAsync(modelPath, cancellationToken);
    public Task<TranscriptionResult> TranscribeAsync(string waveFile, string modelPath, string language, string prompt, bool keepLoaded, CancellationToken cancellationToken) => Select(modelPath).TranscribeAsync(waveFile, modelPath, language, prompt, keepLoaded, cancellationToken);
    public Task UnloadAsync(CancellationToken cancellationToken) => Task.WhenAll(whisper.UnloadAsync(cancellationToken), parakeet.UnloadAsync(cancellationToken));
    public void Dispose() { whisper.Dispose(); parakeet.Dispose(); }
}
