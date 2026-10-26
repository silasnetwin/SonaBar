using System.Globalization;

namespace SonaBar.Core;

/// <summary>A snippet trigger found at the end of the text typed so far.</summary>
public sealed record TypedSnippetMatch(Snippet Snippet, string Trigger);

/// <summary>
/// Pure trigger matching for opt-in typed snippet expansion. The keyboard hook owns
/// the character buffer; keeping matching here makes the boundary rules testable
/// without sending synthetic keyboard input.
/// </summary>
public static class TypedSnippetMatcher
{
    public static IReadOnlyList<string> Triggers(Snippet? snippet)
    {
        if (snippet?.Trigger is not string value) return [];
        return value
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(IsEligible)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Single-character and control-character triggers are too easy to fire by accident.
    /// Surrogate pairs and combining marks are rejected because a target's Backspace
    /// may delete a grapheme cluster instead of one UTF-16 code unit.
    /// </summary>
    public static bool IsEligible(string trigger) =>
        trigger.Length >= 2 &&
        trigger.Any(character => !char.IsWhiteSpace(character)) &&
        !trigger.Any(char.IsControl) &&
        !trigger.Any(character => char.IsSurrogate(character) ||
            char.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark);

    public static bool IsDelimiter(char value) =>
        value is ' ' or '\t' or '\r' or '\n' or '.' or ',' or '!' or '?' or ';' or ':';

    /// <summary>
    /// Returns the longest trigger at the end of <paramref name="buffer"/> that starts
    /// at a word boundary. Internal spaces and punctuation stay in the buffer so
    /// multi-word and punctuation triggers can match; the completing delimiter is
    /// not part of the buffer.
    /// </summary>
    public static TypedSnippetMatch? Match(string? buffer, IEnumerable<Snippet>? snippets)
    {
        if (string.IsNullOrEmpty(buffer) || snippets is null) return null;
        TypedSnippetMatch? best = null;
        foreach (var snippet in snippets)
        {
            if (snippet is null) continue;
            foreach (var trigger in Triggers(snippet))
            {
                if (trigger.Length > buffer.Length ||
                    !buffer.EndsWith(trigger, StringComparison.OrdinalIgnoreCase)) continue;
                var start = buffer.Length - trigger.Length;
                if (start > 0 && (char.IsLetterOrDigit(buffer[start - 1]) || buffer[start - 1] == '_')) continue;
                if (best is null || trigger.Length > best.Trigger.Length) best = new(snippet, trigger);
            }
        }
        return best;
    }
}
