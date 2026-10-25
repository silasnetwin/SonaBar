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
