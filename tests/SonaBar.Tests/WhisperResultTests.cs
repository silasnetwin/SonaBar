using SonaBar.Transcription;
using Xunit;

namespace SonaBar.Tests;

public sealed class WhisperResultTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 3)]
    [InlineData(24, 16)]
    public void RecognitionThreadCountUsesAvailableCpuWithoutOversubscribing(int logicalProcessorCount, int expected)
    {
        Assert.Equal(expected, WhisperEngine.RecognitionThreadCount(logicalProcessorCount));
    }

    [Fact]
    public void NativeLowercaseJsonProducesTranscript()
    {
        var result = WhisperEngine.ParseNativeResult("""{"language":"en","segments":[{"startMs":0,"endMs":820,"text":" Hello world."}]}""", "auto");
        Assert.Equal("Hello world.", result.Text);
        Assert.Equal("en", result.Language);
        Assert.Single(result.Segments);
    }

    [Fact]
    public void NativeResultWithoutSegmentsReportsExactProblem()
    {
        var error = Assert.Throws<InvalidDataException>(() => WhisperEngine.ParseNativeResult("""{"language":"en"}""", "auto"));
        Assert.Contains("segment list", error.Message);
    }
}
