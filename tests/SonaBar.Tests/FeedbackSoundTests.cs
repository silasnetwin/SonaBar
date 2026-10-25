using SonaBar.Audio;
using SonaBar.Core;
using Xunit;

namespace SonaBar.Tests;

public sealed class FeedbackSoundTests
{
    [Theory]
    [InlineData("Off", FeedbackSound.Started, false)]
    [InlineData("Off", FeedbackSound.Finished, false)]
    [InlineData("Off", FeedbackSound.Error, false)]
    [InlineData("Minimal", FeedbackSound.Started, false)]
    [InlineData("Minimal", FeedbackSound.Finished, true)]
    [InlineData("Minimal", FeedbackSound.Error, false)]
    [InlineData("Standard", FeedbackSound.Started, true)]
    [InlineData("Standard", FeedbackSound.Finished, true)]
    [InlineData("Standard", FeedbackSound.Error, true)]
    public void SoundModePlaysOnlyTheConfiguredCues(string mode, FeedbackSound sound, bool expected)
        => Assert.Equal(expected, FeedbackSounds.ShouldPlay(mode, sound));

    [Fact]
    public void FeedbackFilesKeepStableNames()
    {
        Assert.Equal("start.wav", FeedbackSounds.FileNameFor(FeedbackSound.Started));
        Assert.Equal("done.wav", FeedbackSounds.FileNameFor(FeedbackSound.Finished));
        Assert.Equal("error.wav", FeedbackSounds.FileNameFor(FeedbackSound.Error));
    }

    [Fact]
    public void InvalidSoundModeFallsBackToOff()
    {
        var settings = new AppSettings { Sounds = "Loud" };
        settings.Validate();
        Assert.Equal("Off", settings.Sounds);
    }
}
