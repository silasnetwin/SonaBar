using System.Text.RegularExpressions;

namespace Voxora.Core;

public enum WritingSurface { Neutral, Email, Chat, Prompt, Code, Terminal }
public static class WritingContext
{
    public static WritingSurface Detect(string executable, string windowTitle = "")
    {
        var app = executable.ToLowerInvariant();
        if (app is "outlook.exe" or "olk.exe" or "hxoutlook.exe" or "thunderbird.exe") return WritingSurface.Email;
        if (app is "discord.exe" or "slack.exe" or "teams.exe" or "ms-teams.exe") return WritingSurface.Chat;
        if (app is "chatgpt.exe" or "codex.exe") return WritingSurface.Prompt;
        if (app is "code.exe" or "idea64.exe" or "rider64.exe") return WritingSurface.Code;
        if (app is "windowsterminal.exe" or "cmd.exe" or "powershell.exe" or "pwsh.exe") return WritingSurface.Terminal;
        if (app is "chrome.exe" or "msedge.exe" or "firefox.exe")
        {
            // Only an explicit service suffix, never arbitrary words in a page/document title.
            var match = Regex.Match(windowTitle, @"(?:^|[-–—])\s*(?<service>Gmail|Outlook|ChatGPT|Discord)\s*[-–—]\s*(?:Google Chrome|Microsoft Edge|Mozilla Firefox)\s*$", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups["service"].Value.ToLowerInvariant() switch { "gmail" or "outlook" => WritingSurface.Email, "chatgpt" => WritingSurface.Prompt, "discord" => WritingSurface.Chat, _ => WritingSurface.Neutral };
        }
        return WritingSurface.Neutral;
    }

    public static string Instruction(WritingSurface surface) => surface switch
    {
        WritingSurface.Email => "Format as an email: put the stated greeting, body and stated sign-off in separate paragraphs. Never add a greeting, recipient, subject or signature that was not spoken.",
        WritingSurface.Chat => "Keep conversational wording and short paragraphs. Do not add letter-style greetings or sign-offs.",
        WritingSurface.Prompt => "Structure explicitly spoken tasks and constraints clearly. Do not answer the request or invent roles, requirements, examples or sub-tasks.",
        WritingSurface.Code => "Preserve identifiers, commands, code, capitalization of technical names and English terms exactly.",
        WritingSurface.Terminal => "Return the text unchanged. Never generate or alter commands.",
        _ => "Use neutral text formatting. Preserve the original language and wording."
    };
}

public static class VocabularyPrompt
{
    public static string Build(IEnumerable<string> words)
    {
        // Whisper has a bounded prompt context. Explicit user terms precede built-in names.
        var selected = words.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x is "SineFlow" or "SNine Studios" ? 1 : 0);
        var result = new List<string>(); var length = 0;
        foreach (var word in selected) { if (word.Length > 120 || length + word.Length + 2 > 700) continue; result.Add(word); length += word.Length + 2; }
        return string.Join(", ", result);
    }
}
