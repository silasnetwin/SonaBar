using SonaBar.App;
using SonaBar.Core;
using SonaBar.WindowsIntegration;
using Xunit;

namespace SonaBar.Tests;

/// <summary>
/// Covers the states that replaced the endless "getting ready": a missing model is its own
/// state, work in progress is distinguishable from being ready, and the inspection of the
/// focused field can never run without a limit.
/// </summary>
public sealed class StartupAndThemeTests
{
    [Theory]
    [InlineData(ReadinessStage.Starting, true)]
    [InlineData(ReadinessStage.CheckingModels, true)]
    [InlineData(ReadinessStage.PreparingModel, true)]
    [InlineData(ReadinessStage.Transcribing, true)]
    [InlineData(ReadinessStage.Inserting, true)]
    [InlineData(ReadinessStage.Ready, false)]
    [InlineData(ReadinessStage.NeedsModel, false)]
    [InlineData(ReadinessStage.Idle, false)]
    [InlineData(ReadinessStage.Error, false)]
    public void OnlyWorkInProgressCountsAsBusy(ReadinessStage stage, bool busy) =>
        Assert.Equal(busy, new Readiness(stage).IsBusy);

    [Fact]
    public void EveryStageCarriesALabelTheUserCanRead()
    {
        foreach (var stage in Enum.GetValues<ReadinessStage>().Where(x => x != ReadinessStage.Idle))
            Assert.False(string.IsNullOrWhiteSpace(new Readiness(stage).Label), $"{stage} has no label");
    }

    [Fact]
    public void MissingModelExplainsWhatToDoInsteadOfClaimingReadiness()
    {
        var readiness = new Readiness(ReadinessStage.NeedsModel, "Download a speech model in Models to start dictating.");
        Assert.False(readiness.IsBusy);
        Assert.NotEqual("Ready to listen", readiness.Label);
        Assert.Contains("Download", readiness.Detail);
    }

    [Fact]
    public void StageLabelDescribesTheStateAndKeepsTheExplanationSeparate()
    {
        var readiness = new Readiness(ReadinessStage.NeedsModel, "Download a model.");
        Assert.Equal("No speech model yet", readiness.Label);
        Assert.Equal("Download a model.", readiness.Detail);
        Assert.Equal("Ready to listen", new Readiness(ReadinessStage.Ready).Label);
        Assert.Equal("", new Readiness(ReadinessStage.Ready).Detail);
    }

    [Fact]
    public void FocusedFieldInspectionHasAHardLimit()
    {
        Assert.InRange(TextInsertionService.CaptureTimeout, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void OwnWindowsAreRecognisedSoTheyAreNeverInspectedFromTheHelperThread()
    {
        // A field inside SonaBar is described by this process; asking the automation helper
        // thread for it made both threads wait for each other and froze the window.
        Assert.False(TextInsertionService.IsOwnWindow(nint.Zero));
        Assert.True(TextInsertionService.IsOwnProcess((uint)Environment.ProcessId));
        Assert.False(TextInsertionService.IsOwnProcess((uint)Environment.ProcessId + 1));
    }

    [Fact]
    public void StuckAutomationCallIsAbandonedInsteadOfHangingTheCaller()
    {
        using var probe = new AutomationProbe();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var late = probe.TryRun(() => { Thread.Sleep(4000); return "late"; }, TimeSpan.FromMilliseconds(150));
        clock.Stop();
        Assert.Null(late);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"the call blocked for {clock.Elapsed}");
        // A fresh worker takes over, so one busy application cannot disable the feature.
        Assert.Equal("ok", probe.TryRun(() => "ok", TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void AutomationProbeReturnsTheResultOfAQuickCall()
    {
        using var probe = new AutomationProbe();
        Assert.Equal("focused", probe.TryRun(() => "focused", TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void DarkThemeUsesNeutralSurfacesAndABlueAccent()
    {
        var dark = Palette.Dark;
        var accent = Parse(dark.Accent);
        Assert.True(accent.B > accent.R, "the accent must be blue rather than green");
        foreach (var surface in new[] { dark.AppBackground, dark.Surface, dark.SurfaceElevated, dark.Sidebar })
        {
            var colour = Parse(surface);
            Assert.True(Math.Max(colour.R, Math.Max(colour.G, colour.B)) < 80, $"{surface} is too bright for a dark surface");
            Assert.True(Math.Abs(colour.G - colour.R) < 12 && Math.Abs(colour.G - colour.B) < 12, $"{surface} is not a neutral grey");
        }
    }

    [Fact]
    public void LightThemeKeepsReadableContrastOnItsSurfaces()
    {
        var light = Palette.Light;
        var text = Parse(light.TextPrimary);
        var surface = Parse(light.SurfaceElevated);
        Assert.True(Luminance(text) < 0.2, "body text must be dark in the light theme");
        Assert.True(Luminance(surface) > 0.8, "cards must be light in the light theme");
        Assert.True(ContrastRatio(text, surface) >= 7, "body text must reach the enhanced contrast level");
    }

    [Fact]
    public void EveryResourceKeyTheWindowStylesUseExistsInThePalette()
    {
        var keys = Palette.Dark.Entries.Select(x => x.Key).ToHashSet();
        foreach (var required in new[]
        {
            "Surface", "SurfaceElevated", "Card", "Sidebar", "BackgroundElevated",
            "Border", "Line", "BorderStrong", "TextPrimary", "TextSecondary", "TextTertiary", "Ink", "Muted",
            "Accent", "AccentInk", "AccentSoft", "Action", "ActionInk", "Hover", "SurfaceHover",
            "Error", "Warning", "Success", "OverlaySurface", "OverlayInk", "OverlayAccent", "OverlayError"
        })
            Assert.Contains(required, keys);
    }

    [Fact]
    public void OverlaySurfaceIsOpaqueSoRoundedCornersCannotShowBlack()
    {
        foreach (var palette in new[] { Palette.Dark, Palette.Light })
        {
            var colour = Parse(palette.OverlaySurface);
            Assert.Equal(255, colour.A);
        }
    }

    private static System.Windows.Media.Color Parse(string hex) =>
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);

    private static double Luminance(System.Windows.Media.Color colour)
    {
        static double Channel(byte value)
        {
            var v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(colour.R) + 0.7152 * Channel(colour.G) + 0.0722 * Channel(colour.B);
    }

    private static double ContrastRatio(System.Windows.Media.Color a, System.Windows.Media.Color b)
    {
        var first = Luminance(a);
        var second = Luminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }
}
