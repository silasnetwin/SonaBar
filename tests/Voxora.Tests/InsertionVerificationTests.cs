using Voxora.WindowsIntegration;
using Xunit;

namespace Voxora.Tests;

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
}
