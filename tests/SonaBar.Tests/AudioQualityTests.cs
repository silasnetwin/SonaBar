using NAudio.Wave;
using SonaBar.Audio;
using Xunit;
namespace SonaBar.Tests;

public class AudioQualityTests
{
    [Fact]
    public void ResamplingPreservesSpeechBandAndDuration()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        try
        {
            var tone = new NAudio.Wave.SampleProviders.SignalGenerator(48000, 2) { Frequency = 5000, Gain = .25, Type = NAudio.Wave.SampleProviders.SignalGeneratorType.Sin };
            var source = new NAudio.Wave.SampleProviders.OffsetSampleProvider(tone) { Take = TimeSpan.FromSeconds(1) };
            WaveFileWriter.CreateWaveFile16(path, AudioFiles.Mono16k(source));
            using var reader = new WaveFileReader(path); Assert.Equal(16000, reader.WaveFormat.SampleRate); Assert.Equal(1, reader.WaveFormat.Channels);
            var signal = AudioFiles.Analyze(path, default); Assert.InRange(signal.Duration.TotalSeconds, .99, 1.01); Assert.InRange(signal.Rms, .14, .20);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void SilentShortRecordingIsRecognizableWithoutLoadingAModel()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        try { using (var writer = new WaveFileWriter(path, new WaveFormat(16000, 16, 1))) writer.Write(new byte[6400], 0, 6400); var signal = AudioFiles.Analyze(path, default); Assert.Equal(0, signal.Rms); Assert.Equal(.2, signal.Duration.TotalSeconds, 3); }
        finally { File.Delete(path); }
    }
}
