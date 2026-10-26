using SonaBar.Transcription;
using Xunit;

namespace SonaBar.Tests;

public sealed class ParakeetHotwordTests
{
    [Fact]
    public void HotwordsAreTrimmedDeduplicatedAndKeepOrder()
    {
        var words = ParakeetEngine.NormalizeHotwords([" Mara ", "", "mara", "SilasNet", null, "  "]);
        Assert.Equal(new[] { "Mara", "SilasNet" }, words);
    }

    [Fact]
    public void HotwordsAreLimitedToTheNativeContextBudget()
    {
        var words = ParakeetEngine.NormalizeHotwords(Enumerable.Range(0, 100).Select(index => "term-" + index));
        Assert.Equal(64, words.Count);
        Assert.Equal("term-0", words[0]);
        Assert.Equal("term-63", words[words.Count - 1]);
    }
}
