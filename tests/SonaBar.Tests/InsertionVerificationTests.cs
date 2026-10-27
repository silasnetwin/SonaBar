using SonaBar.Core;
using SonaBar.WindowsIntegration;
using Xunit;

namespace SonaBar.Tests;

public sealed class InsertionVerificationTests
{
    [Fact]
    public void NewTextIsConfirmedWhenItWasNotPresentBefore()
    {
        Assert.True(TextInsertionService.ConfirmedChange("Hello world", "world", "Hello"));
    }

    [Fact]
    public void RepeatedIdenticalTextStillNeedsAnotherOccurrence()
    {
        Assert.False(TextInsertionService.ConfirmedChange("Hello world", "world", "Hello world"));
        Assert.True(TextInsertionService.ConfirmedChange("Hello world world", "world", "Hello world"));
    }

    [Fact]
    public void WindowsAndUnixLineEndingsAreEquivalent()
    {
        Assert.True(TextInsertionService.ConfirmedChange("Hello\r\nworld", "Hello\nworld", ""));
    }

    [Fact]
    public void WhitespaceNormalizationDoesNotHideARealInsertion()
    {
        Assert.True(TextInsertionService.ConfirmedChange("Hello   world", "Hello world", ""));
        Assert.False(TextInsertionService.ConfirmedChange("Hello   world", "Hello world", "Hello   world"));
    }

    [Fact]
    public void PartialInsertionIsDetectedBeforeAFallbackCanDuplicateText()
    {
        Assert.True(TextInsertionService.ContainsPartialInsertion("Hello wor", "Hello world", ""));
        Assert.False(TextInsertionService.ContainsPartialInsertion("Hello", "Hello world", "Hello"));
        Assert.False(TextInsertionService.ContainsPartialInsertion("Unrelated", "Hello world", ""));
    }

    [Fact]
    public void PartialInsertionCoverageMeasuresTheVisiblePrefix()
    {
        Assert.Equal(9, TextInsertionService.InsertedCoverage("Hello wor", "Hello world", ""));
        Assert.True(TextInsertionService.InsertedCoverage("Hello world", "Hello world", "") >= TextInsertionService.MinimumCoverage("Hello world"));
        Assert.Equal(0, TextInsertionService.InsertedCoverage("Unrelated", "Hello world", ""));
    }

    [Fact]
    public void RichComposersPreferClipboardPaste()
    {
        Assert.True(TextInsertionService.PreferClipboardForTarget("DiscordCanary.exe", "markup__ editor__ slateTextArea"));
        Assert.True(TextInsertionService.PreferClipboardForTarget("OpenCode.exe", ""));
        Assert.True(TextInsertionService.PreferClipboardForTarget("chrome.exe", "cm-editor"));
        Assert.False(TextInsertionService.PreferClipboardForTarget("notepad.exe", "Edit"));
    }

    [Fact]
    public void ChangedClipboardDeliveryIsAcceptedOnlyWithAVisiblePrefix()
    {
        var text = "Hello world. This is a long dictation sentence.";
        var normalized = "Hello world. This is a long dictation sentence";
        var coverage = TextInsertionService.InsertedCoverage(normalized, text, "");
        Assert.True(TextInsertionService.CanAssumeChangedClipboardDelivery(true, coverage, text));
        Assert.False(TextInsertionService.CanAssumeChangedClipboardDelivery(false, coverage, text));
        Assert.False(TextInsertionService.CanAssumeChangedClipboardDelivery(true, 10, text));
    }

    [Fact]
    public void BrowserPageSelectionIsNotTreatedAsAFieldSelection()
    {
        Assert.False(TextInsertionService.SelectionBelongsToContent("Page heading", ""));
        Assert.False(TextInsertionService.SelectionBelongsToContent("Page heading", null));
        Assert.True(TextInsertionService.SelectionBelongsToContent("selected", "before selected after"));
    }

    [Fact]
    public void CompatibilityModeAcceptsOpaqueGameTargets()
    {
        Assert.True(TextInsertionService.CanAssumeOpaqueDelivery(InsertionStrategy.Compatibility, false));
        Assert.True(TextInsertionService.CanAssumeOpaqueDelivery(InsertionStrategy.Automatic, true));
        Assert.False(TextInsertionService.CanAssumeOpaqueDelivery(InsertionStrategy.Automatic, false));
        Assert.False(TextInsertionService.CanAssumeOpaqueDelivery(InsertionStrategy.Clipboard, false));
    }

    [Fact]
    public void RobloxUsesCompatibilityInsertionByDefault()
    {
        var settings = new AppSettings();
        var profile = settings.ProfileFor("RobloxPlayerBeta.exe");
        Assert.Equal(InsertionStrategy.Compatibility, profile.Strategy);
    }
}
