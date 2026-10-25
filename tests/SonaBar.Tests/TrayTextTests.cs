using SonaBar.App;
using Xunit;

namespace SonaBar.Tests;

/// <summary>
/// The tray tooltip is limited to 63 characters by Windows. The previous
/// truncation used a hard-coded prefix length that was one character too long,
/// so every status update could throw an ArgumentOutOfRangeException.
/// </summary>
public sealed class TrayTextTests
{
    [Fact]
    public void ShortStatusKeepsTheFullPrefixAndMessage()
    {
        Assert.Equal("SonaBar · Ready to listen", TrayText.ForStatus("Ready to listen"));
    }

    [Fact]
    public void EmptyStatusStillProducesAValidTooltip()
    {
        Assert.Equal("SonaBar · ", TrayText.ForStatus(""));
    }

    [Fact]
    public void LongStatusIsTruncatedToTheWindowsLimit()
    {
        var text = TrayText.ForStatus(new string('x', 100));
        Assert.Equal(TrayText.MaxLength, text.Length);
        Assert.StartsWith("SonaBar · ", text);
    }
}
