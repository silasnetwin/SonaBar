using SonaBar.Core;
using Xunit;

namespace SonaBar.Tests;

public class TypedSnippetTests
{
    [Fact]
    public void TypedExpansionIsOptIn() => Assert.False(new AppSettings().ExpandTypedSnippets);

    [Fact]
    public void TypedTriggerMatchesOnlyAtAWordBoundary()
    {
        var snippet = new Snippet("Signature", "sig", "Mara");
        Assert.Equal("sig", TypedSnippetMatcher.Match("Please sig", [snippet])?.Trigger);
        Assert.Equal("sig", TypedSnippetMatcher.Match("sig", [snippet])?.Trigger);
        Assert.Null(TypedSnippetMatcher.Match("Pleaseasig", [snippet]));
        Assert.Null(TypedSnippetMatcher.Match("Please sig", []));
    }

    [Fact]
    public void LongestTriggerAlternativeWins()
    {
        var snippet = new Snippet("BRB", "brb|be right back", "Be right back.");
        Assert.Equal("be right back", TypedSnippetMatcher.Match("I will be right back", [snippet])?.Trigger);
        Assert.Equal("brb", TypedSnippetMatcher.Match("I will brb", [snippet])?.Trigger);
        Assert.Equal("e.g.", TypedSnippetMatcher.Match("Use e.g.", [new Snippet("Example", "e.g.", "for example")])?.Trigger);
    }

    [Fact]
    public void TypedMatchingIsCaseInsensitive()
    {
        var snippet = new Snippet("Signature", "sig", "Mara");
        Assert.Equal("sig", TypedSnippetMatcher.Match("Please SIG", [snippet])?.Trigger);
    }

    [Fact]
    public void SingleCharacterAndControlCharacterTriggersAreIgnored()
    {
        Assert.Null(TypedSnippetMatcher.Match("a", [new Snippet("One", "a", "x")]));
        Assert.Empty(TypedSnippetMatcher.Triggers(new Snippet("Control", "a\tb", "x")));
        Assert.Empty(TypedSnippetMatcher.Triggers(null));
    }

    [Fact]
    public void NullSnippetEntriesDoNotBreakMatching()
    {
        var snippet = new Snippet("Signature", "sig", "Mara");
        Assert.Equal("sig", TypedSnippetMatcher.Match("Please sig", [null!, snippet])?.Trigger);
    }

    [Fact]
    public void TypedExpansionUsesVariablesWithoutRematchingTheTrigger()
    {
        var now = new DateTime(2026, 10, 25, 14, 30, 0);
        var snippet = new Snippet("Signature", "my sig", "{date}|{time}|{clipboard}");
        Assert.Equal($"{now.ToShortDateString()}|{now.ToShortTimeString()}|abc", TextProcessing.ExpandSnippet(snippet, now, () => "abc"));
        Assert.Equal("literal", TextProcessing.ExpandSnippet(new Snippet("Literal", "lit", "literal"), now, () => throw new InvalidOperationException()));
        Assert.Equal("", TextProcessing.ExpandSnippet(new Snippet("Empty", "empty", null!), now, () => ""));
    }

    [Fact]
    public void DelimitersCoverTypingAndSentenceEndings()
    {
        foreach (var delimiter in " \t\r\n.,!?;:") Assert.True(TypedSnippetMatcher.IsDelimiter(delimiter));
        Assert.False(TypedSnippetMatcher.IsDelimiter('a'));
    }
}
