using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Voxora.Core;
using System.Diagnostics;

namespace Voxora.Audio;

public sealed class AudioCaptureService : IAudioCaptureService, IMMNotificationClient
{
    private readonly MMDeviceEnumerator devices = new();
    private readonly List<(WasapiCapture Capture, WaveFileWriter Writer, string Path, TaskCompletionSource Done)> active = [];
    private string? destination;
    private volatile bool stopping;
    private Task? stopTask;
    public bool IsCapturing=>active.Count!=0;
    public event Action<float>? Level;
    public event Action<string>? Faulted;
    public event Action? DevicesChanged;
    public AudioCaptureService() => devices.RegisterEndpointNotificationCallback(this);
    public IReadOnlyList<(string Id, string Name)> Microphones() => devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).Select(d => { using (d) return (d.ID, d.FriendlyName); }).ToList();
    public void Start(string path, CaptureMode mode, string? microphoneId)
    {
        if (active.Count != 0) throw new InvalidOperationException("Recording is already running.");
        destination = path; stopping = false;stopTask=null;
        try
        {
            if (mode is CaptureMode.Microphone or CaptureMode.Both)
            {
                var device = microphoneId is null ? devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications) : devices.GetDevice(microphoneId);
                using (device) Attach(new WasapiCapture(device), path + ".mic.wav");
            }
            if (mode is CaptureMode.System or CaptureMode.Both) Attach(new WasapiLoopbackCapture(), path + ".system.wav");
            foreach (var item in active) item.Capture.StartRecording();
        }
        catch { DisposeActive(); throw new InvalidOperationException("No microphone found or the audio device is unavailable. Check Windows microphone privacy settings and choose an input device."); }
    }
    private void Attach(WasapiCapture capture, string path)
    {
        WaveFileWriter writer;
        try{writer=new WaveFileWriter(path,capture.WaveFormat);}catch{capture.Dispose();throw;}
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        active.Add((capture, writer, path, done));
        capture.DataAvailable += (_, e) =>
        {
            lock (writer)
            {
                if (done.Task.IsCompleted) return;
                writer.Write(e.Buffer, 0, e.BytesRecorded);
                float peak = 0; double squareSum=0; var samples=0;
                if (capture.WaveFormat.BitsPerSample == 32)
                {
                    var floating=capture.WaveFormat.Encoding==WaveFormatEncoding.IeeeFloat||capture.WaveFormat is WaveFormatExtensible extensible&&extensible.SubFormat==new Guid("00000003-0000-0010-8000-00aa00389b71");
                    for (int i = 0; i + 3 < e.BytesRecorded; i += 4){var sample=floating?BitConverter.ToSingle(e.Buffer,i):BitConverter.ToInt32(e.Buffer,i)/2147483648f;if(float.IsFinite(sample)){peak=Math.Max(peak,Math.Abs(sample));squareSum+=(double)sample*sample;samples++;}}
                }
                else if (capture.WaveFormat.BitsPerSample == 16)
                    for (int i = 0; i + 1 < e.BytesRecorded; i += 2) { var sample=BitConverter.ToInt16(e.Buffer, i) / 32768f; peak = Math.Max(peak, Math.Abs(sample)); squareSum+=(double)sample*sample;samples++; }
                // RMS reflects sustained speech rather than a single click. The square-root
                // response makes quiet natural speech visible without letting a peak pin the UI.
                var rms=samples==0?0:Math.Sqrt(squareSum/samples);
                var level=Math.Clamp((float)(Math.Sqrt(rms)*2.2),0,1);
                Level?.Invoke(level);
            }
        };
        capture.RecordingStopped += (_, e) =>
        {
            lock (writer) { writer.Flush(); done.TrySetResult(); }
            if (e.Exception is not null && !stopping) Faulted?.Invoke("Recording device was disconnected. Your captured audio can still be recovered.");
        };
    }
    public Task StopAsync()=>stopTask??=StopCoreAsync();
    private async Task StopCoreAsync()
    {
        if (active.Count == 0) return;
        stopping = true;
        foreach (var item in active) item.Capture.StopRecording();
        var captured=active.ToArray();var finalPath=destination;
        try{await Task.WhenAll(captured.Select(i => i.Done.Task)).WaitAsync(TimeSpan.FromSeconds(8));}
        finally{foreach (var item in captured) { item.Capture.Dispose(); item.Writer.Dispose(); }active.Clear();}
        var paths = captured.Select(i => i.Path).ToArray();
        try
        {
            await Task.Run(() =>
            {
                var readers = paths.Select(p => new AudioFileReader(p)).ToArray();
                try
                {
                    var sources = readers.Select(r => AudioFiles.Mono16k(r)).ToArray();
                    ISampleProvider mixed = sources.Length == 1 ? sources[0] : new MixingSampleProvider(sources);
                    WaveFileWriter.CreateWaveFile16(finalPath!, mixed);
                }
                finally { foreach (var r in readers) r.Dispose(); }
            });
        }
        finally { foreach (var p in paths) File.Delete(p); }
    }
    private void DisposeActive()
    {
        stopping = true;
        foreach (var item in active) { item.Capture.Dispose(); lock(item.Writer){item.Done.TrySetResult();item.Writer.Dispose();} File.Delete(item.Path); }
        active.Clear();
    }
    public void Dispose() { DisposeActive(); devices.UnregisterEndpointNotificationCallback(this); devices.Dispose(); }
    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => DevicesChanged?.Invoke();
    public void OnDeviceAdded(string pwstrDeviceId) => DevicesChanged?.Invoke();
    public void OnDeviceRemoved(string deviceId) => DevicesChanged?.Invoke();
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => DevicesChanged?.Invoke();
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
}
public static class AudioFiles
{
    public static (TimeSpan Duration,double Rms,float Peak) Analyze(string wave,CancellationToken token)
    {
        using var reader=new AudioFileReader(wave);var buffer=new float[16384];long count=0;double squareSum=0;float peak=0;int read;
        while((read=reader.Read(buffer,0,buffer.Length))>0)
        {
            token.ThrowIfCancellationRequested();
            for(int i=0;i<read;i++)
            {
                var sample=buffer[i];
                // A device that delivers a value that is not a number must not turn the
                // measurement into a number that is not a number.
                if(!float.IsFinite(sample))continue;
                squareSum+=(double)sample*sample;peak=Math.Max(peak,Math.Abs(sample));
            }
            count+=read;
        }
        return(reader.TotalTime,count==0?0:Math.Sqrt(squareSum/count),peak);
    }
    public static ISampleProvider Mono16k(ISampleProvider input)
    {
        if (input.WaveFormat.Channels == 2) input = new StereoToMonoSampleProvider(input);
        else if (input.WaveFormat.Channels != 1) throw new InvalidDataException("This audio stream has unsupported channel routing. Convert it with the bundled decoder.");
        return input.WaveFormat.SampleRate == 16000 ? input : new SpeechResampler(input);
    }
    public static async Task ConvertAsync(string input, string output, CancellationToken token)
    {
        var ffmpeg = Path.Combine(AppContext.BaseDirectory, "native", "ffmpeg.exe");
        if (!File.Exists(ffmpeg)) throw new FileNotFoundException("The bundled media decoder is missing. Reinstall SineFlow.");
        var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "-nostdin", "-v", "error", "-y", "-i", input, "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", output }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync(token); var stdout = process.StandardOutput.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); await Task.WhenAll(error, stdout); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        if (process.ExitCode != 0) throw new InvalidDataException("This media file could not be decoded. It may be damaged, encrypted, or have no audio track.");
    }
    public static TimeSpan Duration(string wave) { using var reader = new WaveFileReader(wave); return reader.TotalTime; }

    /// <summary>
    /// Removes leading and trailing silence from a recording. The file is streamed twice so a
    /// long recording never has to be held in memory, and an internal pause is never touched.
    /// </summary>
    public static bool TrimSilence(string source, string destination, CancellationToken token = default)
    {
        const int rate = 16000;
        var frame = SilenceTrim.FrameSize(rate);
        var levels = new List<double>();
        long totalSamples = 0;
        using (var reader = new AudioFileReader(source))
        {
            var provider = Mono16k(reader);
            var buffer = new float[16384];
            var squareSum = 0d;
            var inFrame = 0;
            int read;
            while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                for (var index = 0; index < read; index++)
                {
                    var sample = buffer[index];
                    // A broken capture can deliver a value that is not a number. It carries no
                    // sound and must not poison the level measurement.
                    if (!float.IsFinite(sample)) sample = 0f;
                    squareSum += (double)sample * sample;
                    inFrame++;
                    totalSamples++;
                    if (inFrame == frame)
                    {
                        levels.Add(Math.Sqrt(squareSum / frame));
                        squareSum = 0;
                        inFrame = 0;
                    }
                }
            }
            if (inFrame > 0) levels.Add(Math.Sqrt(squareSum / inFrame));
        }
        var plan = SilenceTrim.PlanFromLevels(levels, rate, (int)Math.Min(totalSamples, int.MaxValue));
        if (!plan.Trimmed) { File.Copy(source, destination, true); return false; }

        var kept = new List<float>(plan.LengthSamples);
        using (var reader = new AudioFileReader(source))
        {
            var provider = Mono16k(reader);
            var buffer = new float[16384];
            long written = 0;
            int read;
            while (written < plan.StartSample && (read = provider.Read(buffer, 0, buffer.Length)) > 0) written += read;
            written = 0;
            while (written < plan.LengthSamples && (read = provider.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (var index = 0; index < read && written < plan.LengthSamples; index++, written++)
                {
                    var sample = buffer[index];
                    kept.Add(float.IsFinite(sample) ? sample : 0f);
                }
            }
        }
        // The result is written through the same 16 bit path the recorder uses, so the
        // trimmed file has exactly the format the speech engine expects.
        WaveFileWriter.CreateWaveFile16(destination, new FloatSampleProvider(kept.ToArray(), rate));
        return true;
    }

    /// <summary>Feeds a buffer of mono samples to the wave writer.</summary>
    private sealed class FloatSampleProvider(float[] samples, int rate) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(rate, 1);
        private int position;
        public int Read(float[] buffer, int offset, int count)
        {
            var available = Math.Min(count, samples.Length - position);
            if (available <= 0) return 0;
            Array.Copy(samples, position, buffer, offset, available);
            position += available;
            return available;
        }
    }
}
// Sinc conversion retains the upper speech band; the generic two-filter interpolator attenuates sibilants.
internal sealed class SpeechResampler : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly NAudio.Dsp.WdlResampler resampler=new();
    public WaveFormat WaveFormat {get;}=WaveFormat.CreateIeeeFloatWaveFormat(16000,1);
    public SpeechResampler(ISampleProvider source)
    {
        this.source=source;resampler.SetMode(false,0,true,64,32);resampler.SetFeedMode(false);resampler.SetRates(source.WaveFormat.SampleRate,16000);
    }
    public int Read(float[] buffer,int offset,int count)
    {
        var required=resampler.ResamplePrepare(count,1,out var input,out var start);
        var available=source.Read(input,start,required);
        return resampler.ResampleOut(buffer,offset,available,count,1);
    }
}
public sealed class AudioDucker : IDisposable
{
    private MMDevice? device;
    private float previous;
    private readonly string recoveryPath = Path.Combine(Brand.DataRoot, "volume-recovery.txt");
    public void Recover()
    {
        if (!File.Exists(recoveryPath)) return;
        var lines = File.ReadAllLines(recoveryPath);
        try
        {
            using var enumerator = new MMDeviceEnumerator(); using var endpoint = enumerator.GetDevice(lines[0]);
            endpoint.AudioEndpointVolume.MasterVolumeLevelScalar = float.Parse(lines[1], System.Globalization.CultureInfo.InvariantCulture);
        }
        finally { File.Delete(recoveryPath); }
    }
    public void Begin()
    {
        if (device is not null) return;
        using var enumerator = new MMDeviceEnumerator(); device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        previous = device.AudioEndpointVolume.MasterVolumeLevelScalar;
        File.WriteAllLines(recoveryPath, [device.ID, previous.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        device.AudioEndpointVolume.MasterVolumeLevelScalar = previous * .25f;
    }
    public void Dispose()
    {
        if (device is null) return;
        try { device.AudioEndpointVolume.MasterVolumeLevelScalar = previous; File.Delete(recoveryPath); }
        finally { device.Dispose(); device = null; }
    }
}
