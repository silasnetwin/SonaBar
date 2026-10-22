using Voxora.Audio;
using Voxora.Core;
using Voxora.WindowsIntegration;
using Xunit;

namespace Voxora.Tests;

/// <summary>Covers the hardware detection, the automatic profile choice and the voice activity pass.</summary>
public class HardwareAndVoiceTests
{
    [Fact]
    public void DetectionReadsTheRunningMachineWithoutFailing()
    {
        var profile = HardwareDetector.Detect();
        Assert.True(profile.LogicalProcessors >= 1);
        Assert.True(profile.TotalMemoryBytes > 0, "Total memory should be readable on Windows.");
        Assert.True(profile.AvailableMemoryBytes > 0, "Available memory should be readable on Windows.");
        Assert.True(profile.AvailableMemoryBytes <= profile.TotalMemoryBytes);
        Assert.False(string.IsNullOrWhiteSpace(profile.OperatingSystem));
    }

    [Fact]
    public void DetectionSurvivesAnUnusableDataDirectory()
    {
        var profile = HardwareDetector.Detect("Z:\\does\\not\\exist\\path");
        Assert.True(profile.LogicalProcessors >= 1);
    }

    [Theory]
    [InlineData(2L * 1024 * 1024 * 1024, 8, SpeechQuality.Fast)]
    [InlineData(4L * 1024 * 1024 * 1024, 8, SpeechQuality.Balanced)]
    [InlineData(16L * 1024 * 1024 * 1024, 24, SpeechQuality.Balanced)]
    [InlineData(16L * 1024 * 1024 * 1024, 2, SpeechQuality.Fast)]
    public void RecommendationFollowsTotalMemoryAndProcessorCount(long total, int processors, SpeechQuality expected)
    {
        var profile = Profile(total, processors);
        Assert.Equal(expected, HardwareRecommendations.RecommendQuality(profile));
        Assert.Equal(SpeechProfiles.ModelFor(expected), HardwareRecommendations.RecommendModel(profile));
    }

    [Fact]
    public void ABusyComputerDoesNotChangeTheProfileFromOneMinuteToTheNext()
    {
        var idle = Profile(16L * 1024 * 1024 * 1024, 16) with { AvailableMemoryBytes = 9L * 1024 * 1024 * 1024 };
        var busy = idle with { AvailableMemoryBytes = 2L * 1024 * 1024 * 1024 };
        Assert.Equal(HardwareRecommendations.RecommendQuality(idle), HardwareRecommendations.RecommendQuality(busy));
    }

    [Fact]
    public void AComputerUnderRealMemoryPressureFallsBackToTheFastProfile()
    {
        var pressed = Profile(16L * 1024 * 1024 * 1024, 16) with { AvailableMemoryBytes = 512L * 1024 * 1024 };
        Assert.Equal(SpeechQuality.Fast, HardwareRecommendations.RecommendQuality(pressed));
    }

    [Fact]
    public void TheStrongestProfileIsNeverChosenAutomatically()
    {
        var strong = Profile(64L * 1024 * 1024 * 1024, 32) with { AvailableMemoryBytes = 40L * 1024 * 1024 * 1024 };
        Assert.NotEqual(SpeechQuality.Maximum, HardwareRecommendations.RecommendQuality(strong));
        Assert.Contains("Maximum", HardwareRecommendations.Explain(strong));
    }

    [Fact]
    public void RecommendationExplainsItselfWithoutTechnicalNames()
    {
        var explanation = HardwareRecommendations.Explain(Profile(16L * 1024 * 1024 * 1024, 16));
        Assert.Contains("GB", explanation);
        Assert.DoesNotContain("CUDA", explanation);
        Assert.DoesNotContain("VRAM", explanation);
    }

    [Fact]
    public void AccelerationTextNeverClaimsAccelerationThatIsMissing()
    {
        var withoutRuntime = Profile(8L * 1024 * 1024 * 1024, 8) with { GraphicsAdapter = "Test GPU", VulkanRuntime = false, CudaRuntime = false };
        Assert.Contains("processor", HardwareRecommendations.AccelerationText(withoutRuntime));
        var withRuntime = withoutRuntime with { CudaRuntime = true };
        Assert.Contains("CUDA", HardwareRecommendations.AccelerationText(withRuntime));
    }

    [Fact]
    public void DiskSpaceIsCheckedBeforeAModelIsOffered()
    {
        var profile = Profile(8L * 1024 * 1024 * 1024, 8) with { FreeDiskBytes = 100 * 1024 * 1024 };
        Assert.False(profile.HasRoomFor(500L * 1024 * 1024));
        Assert.True((profile with { FreeDiskBytes = 50L * 1024 * 1024 * 1024 }).HasRoomFor(500L * 1024 * 1024));
    }

    private static HardwareProfile Profile(long total, int processors) => new(
        OperatingSystem: "Windows",
        LogicalProcessors: processors,
        TotalMemoryBytes: total,
        AvailableMemoryBytes: total / 2,
        GraphicsAdapter: null,
        GraphicsMemoryBytes: 0,
        DiscreteGraphics: false,
        SoftwareAdapter: false,
        VulkanRuntime: false,
        CudaRuntime: false,
        FreeDiskBytes: 100L * 1024 * 1024 * 1024);

    private static float[] Build(params (double Duration, double Amplitude)[] parts)
    {
        const int rate = 16000;
        var total = parts.Sum(part => (int)(part.Duration * rate));
        var samples = new float[total];
        var offset = 0;
        foreach (var (duration, amplitude) in parts)
        {
            var count = (int)(duration * rate);
            for (var index = 0; index < count; index++)
                samples[offset + index] = (float)(amplitude * Math.Sin(2 * Math.PI * 220 * index / rate));
            offset += count;
        }
        return samples;
    }

    [Fact]
    public void SilenceAtBothEndsIsRemovedAndTheWordsStay()
    {
        var samples = Build((1.0, 0.0), (2.0, 0.1), (1.0, 0.0));
        var plan = SilenceTrim.Plan(samples, 16000);
        Assert.True(plan.Trimmed);
        Assert.True(plan.StartSample > 0, "Leading silence should be removed.");
        Assert.True(plan.EndSample < samples.Length, "Trailing silence should be removed.");
        // The spoken part plus the safety margin on both sides has to survive.
        Assert.InRange(plan.LengthSamples, (int)(2.2 * 16000), (int)(2.6 * 16000));
    }

    [Fact]
    public void APauseInsideTheSentenceIsNeverCutAway()
    {
        var samples = Build((1.0, 0.1), (1.0, 0.0), (1.0, 0.1), (1.0, 0.0));
        var plan = SilenceTrim.Plan(samples, 16000);
        Assert.True(plan.Trimmed);
        // From the start of the first sound to the end of the second one, plus the margins.
        Assert.InRange(plan.LengthSamples, (int)(3.2 * 16000), (int)(3.6 * 16000));
    }

    [Fact]
    public void ATrimThatWouldRemoveAlmostEverythingIsRefused()
    {
        var samples = Build((0.1, 0.1), (9.9, 0.0));
        var plan = SilenceTrim.Plan(samples, 16000);
        Assert.False(plan.Trimmed);
        Assert.Equal(samples.Length, plan.LengthSamples);
    }

    [Fact]
    public void ASilentRecordingIsLeftUntouched()
    {
        var samples = Build((3.0, 0.0));
        var plan = SilenceTrim.Plan(samples, 16000);
        Assert.False(plan.Trimmed);
        Assert.Equal(samples.Length, plan.LengthSamples);
    }

    [Fact]
    public void ARecordingWithoutLeadingSilenceOnlyLosesItsTail()
    {
        var samples = Build((2.0, 0.1), (1.0, 0.0));
        var plan = SilenceTrim.Plan(samples, 16000);
        Assert.True(plan.Trimmed);
        Assert.Equal(0, plan.StartSample);
        Assert.InRange(plan.LengthSamples, (int)(2.0 * 16000), (int)(2.4 * 16000));
    }

    [Fact]
    public void TrimmingARealWaveFileKeepsTheWordsAndRemovesTheEdges()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SineFlow-trim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.wav");
            var trimmed = Path.Combine(directory, "trimmed.wav");
            WriteWave(source, Build((1.0, 0.0), (2.0, 0.1), (1.0, 0.0)));
            Assert.True(AudioFiles.TrimSilence(source, trimmed));
            Assert.True(File.Exists(trimmed));
            using var reader = new NAudio.Wave.WaveFileReader(trimmed);
            Assert.InRange(reader.TotalTime.TotalSeconds, 2.2, 2.6);
            Assert.True(reader.TotalTime.TotalSeconds < 4.0, "The trimmed file has to be shorter than the source.");
        }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } }
    }

    [Fact]
    public void TheTrimmedFileKeepsTheSameSoundAsTheSource()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SineFlow-trim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.wav");
            var trimmed = Path.Combine(directory, "trimmed.wav");
            WriteWave(source, Build((1.0, 0.0), (2.0, 0.1), (1.0, 0.0)));
            Assert.True(AudioFiles.TrimSilence(source, trimmed));

            var original = Read(source);
            var result = Read(trimmed);
            Assert.NotEmpty(result);
            Assert.True(result.Count < original.Count, "The trimmed file has to be shorter than the source.");
            // The kept samples have to be a continuous piece of the source recording.
            var offset = -1;
            for (var start = 0; start + result.Count <= original.Count && offset < 0; start++)
            {
                var matches = true;
                for (var index = 0; index < result.Count && matches; index += 97)
                    matches = Math.Abs(original[start + index] - result[index]) < 0.002f;
                if (matches) offset = start;
            }
            Assert.True(offset >= 0, "The trimmed file has to contain the original recording.");
            for (var index = 0; index < result.Count; index++)
                Assert.True(Math.Abs(original[offset + index] - result[index]) < 0.002f, $"Sample {index} changed from {original[offset + index]} to {result[index]}.");
        }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } }
    }

    [Fact]
    public void ARecordingWithBrokenSamplesStillProducesAUsableFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SineFlow-trim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source.wav");
            var trimmed = Path.Combine(directory, "trimmed.wav");
            var samples = Build((1.0, 0.0), (2.0, 0.1), (1.0, 0.0));
            samples[500] = float.NaN;
            samples[900] = float.PositiveInfinity;
            WriteWave(source, samples);
            Assert.True(AudioFiles.TrimSilence(source, trimmed));
            var analysis = AudioFiles.Analyze(trimmed, CancellationToken.None);
            Assert.True(double.IsFinite(analysis.Rms));
            Assert.True(analysis.Rms > 0.01);
        }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } }
    }

    private static List<float> Read(string path)
    {
        var values = new List<float>();
        using var reader = new NAudio.Wave.AudioFileReader(path);
        var buffer = new float[16384];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            for (var index = 0; index < read; index++) values.Add(buffer[index]);
        return values;
    }

    private static void WriteWave(string path, float[] samples)
    {
        // The same 16 bit path the recorder uses, so the test covers the real file format.
        NAudio.Wave.WaveFileWriter.CreateWaveFile16(path, new TestSampleProvider(samples));
    }

    private sealed class TestSampleProvider(float[] samples) : NAudio.Wave.ISampleProvider
    {
        public NAudio.Wave.WaveFormat WaveFormat { get; } = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(16000, 1);
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
