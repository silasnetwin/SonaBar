using System.Globalization;
using System.Text.RegularExpressions;

namespace SonaBar.Core;

public static class SpeechProfiles
{
    public static string ModelFor(SpeechQuality quality) => quality switch { SpeechQuality.Fast => "base", SpeechQuality.Maximum => "large-v3-turbo", _ => "small" };
    public static string Description(SpeechQuality quality) => quality switch
    {
        SpeechQuality.Fast => "Quick everyday notes · smallest download",
        SpeechQuality.Maximum => "Strongest recognition for accents and difficult speech",
        _ => "Recommended · better recognition of natural everyday speech"
    };
}
public enum SpokenAction { None, Translate, ChatGptPrompt, Lowercase, Uppercase, RemoveFillers, Bullets, ProfessionalEmail, Friendly, SpellingOnly, Summarize }
public sealed record SpokenRequest(string Content, SpokenAction Action, string? Language = null);

/// <summary>Describes how a spoken command is carried out.</summary>
public static class SpokenActions
{
    /// <summary>Commands that are carried out without the writing model.</summary>
    public static bool IsDeterministic(SpokenAction action) => action is SpokenAction.Lowercase or SpokenAction.Uppercase;

    /// <summary>Commands that keep working when no writing model is installed.</summary>
    public static bool HasDeterministicFallback(SpokenAction action) => action is SpokenAction.RemoveFillers;

    /// <summary>The instruction handed to the local writing model.</summary>
    public static string Instruction(SpokenAction action)
    {
        var specific = action switch
        {
            SpokenAction.Bullets => "Turn the source into a bullet list. Keep exactly the same information, names, numbers and requests. Do not invent, merge or drop items.",
            SpokenAction.ProfessionalEmail => "Rewrite the source as a professional email. Keep the same facts, names, numbers, requests and commitments. Do not invent a subject line, a greeting, a closing or a signature that the source does not contain. Output only the email body.",
            SpokenAction.Friendly => "Rewrite the source in a warmer, friendlier tone. Keep the same facts, names, numbers and requests. Do not add information.",
            SpokenAction.SpellingOnly => "Correct spelling only. Do not change grammar, word order, wording, tone or style. Keep the same facts, names and numbers. Output only the corrected text.",
            SpokenAction.Summarize => "Summarize the source in fewer words. Keep every essential fact, name, date, number and request. Do not invent new information and do not answer the source.",
            SpokenAction.RemoveFillers => "Remove hesitation fillers that carry no meaning, such as 'ähm', 'uhm', 'also' used as a stall and repeated filler words. Preserve words that carry meaning, including 'also', 'quasi', 'sozusagen' and 'naja'. Keep the facts, names, numbers and intent unchanged.",
            _ => ""
        };
        if (string.IsNullOrEmpty(specific)) return "";
        return specific + " Preserve all facts, numbers, names, negations and intent. Return only the revised text.";
    }

    /// <summary>Short examples shown in the settings so the commands are discoverable.</summary>
    public static IReadOnlyList<(string Spoken, string Effect)> Examples { get; } = [
        ("Übersetze den Text auf Englisch.", "Translates the dictation."),
        ("Mach daraus eine professionelle E-Mail.", "Rewrites the dictation as a professional email."),
        ("Formuliere das freundlicher.", "Rewrites the dictation in a friendlier tone."),
        ("Korrigiere nur die Rechtschreibung.", "Corrects spelling without touching style."),
        ("Entferne die Füllwörter.", "Removes hesitation fillers."),
        ("Mach daraus Stichpunkte.", "Turns the dictation into a bullet list."),
        ("Fasse das kurz zusammen.", "Shortens the dictation."),
        ("Alles lowercase.", "Writes the text in lower case, without a model."),
        ("Alles in Großbuchstaben.", "Writes the text in upper case, without a model.")
    ];
}
public static partial class SpokenInstructions
{
    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["englisch"] = "English",
        ["english"] = "English",
        ["französisch"] = "French",
        ["franzoesisch"] = "French",
        ["french"] = "French",
        ["deutsch"] = "German",
        ["german"] = "German",
        ["spanisch"] = "Spanish",
        ["spanish"] = "Spanish",
        ["italienisch"] = "Italian",
        ["italian"] = "Italian",
        ["portugiesisch"] = "Portuguese",
        ["portuguese"] = "Portuguese",
        ["chinesisch"] = "Chinese",
        ["chinese"] = "Chinese",
        ["mandarin"] = "Chinese",
        ["japanisch"] = "Japanese",
        ["japanese"] = "Japanese",
        ["koreanisch"] = "Korean",
        ["korean"] = "Korean",
        ["arabisch"] = "Arabic",
        ["arabic"] = "Arabic",
        ["hindi"] = "Hindi",
        ["russisch"] = "Russian",
        ["russian"] = "Russian",
        ["ukrainisch"] = "Ukrainian",
        ["ukrainian"] = "Ukrainian",
        ["türkisch"] = "Turkish",
        ["turkish"] = "Turkish",
        ["polnisch"] = "Polish",
        ["polish"] = "Polish",
        ["niederländisch"] = "Dutch",
        ["dutch"] = "Dutch",
        ["schwedisch"] = "Swedish",
        ["swedish"] = "Swedish"
    };
    private static readonly Dictionary<string, string> InflectedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["englische"]="English", ["englischen"]="English", ["englischem"]="English",
        ["französische"]="French", ["französischen"]="French", ["französischem"]="French",
        ["deutsche"]="German", ["deutschen"]="German", ["deutschem"]="German",
        ["spanische"]="Spanish", ["spanischen"]="Spanish", ["spanischem"]="Spanish",
        ["italienische"]="Italian", ["italienischen"]="Italian", ["italienischem"]="Italian",
        ["portugiesische"]="Portuguese", ["portugiesischen"]="Portuguese",
        ["chinesische"]="Chinese", ["chinesischen"]="Chinese",
        ["japanische"]="Japanese", ["japanischen"]="Japanese",
        ["koreanische"]="Korean", ["koreanischen"]="Korean",
        ["russische"]="Russian", ["russischen"]="Russian",
        ["türkische"]="Turkish", ["türkischen"]="Turkish",
        ["polnische"]="Polish", ["polnischen"]="Polish",
        ["niederländische"]="Dutch", ["niederländischen"]="Dutch"
    };
    private static bool TryLanguage(string value,out string language) => Languages.TryGetValue(value.Trim(),out language!)||InflectedLanguages.TryGetValue(value.Trim(),out language!);
    // Commands are intentionally accepted only at the end of a dictation. This prevents a normal
    // sentence about English, translations, or grammar from being treated as an instruction.
    public static SpokenRequest Parse(string text)
    {
        var prompt = Regex.Match(text, @"(?:[,.;!?]\s*|\s+)(?:bitte\s+)?(?:mach(?:e)?\s+(?:mir\s+)?(?:daraus|aus\s+(?:diesem|dem)\s+text)\s+(?:bitte\s+)?(?:einen?\s+)?(?:guten?\s+|optimierten?\s+)?prompt\s+f[uü]r\s+chat\s*gpt|(?:turn|make)\s+(?:this|that)\s+into\s+(?:a\s+)?(?:chat\s*gpt\s+prompt|prompt\s+for\s+chat\s*gpt))\s*(?:bitte|please)?[.!?]*\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (prompt.Success && prompt.Index > 0) return new(text[..prompt.Index].Trim(), SpokenAction.ChatGptPrompt);
        foreach(var pattern in TranslationCommandPatterns)
        {
            var translation=pattern.Match(text);
            if(!translation.Success||translation.Index<=0||!TryLanguage(translation.Groups["language"].Value,out var language))continue;
            var content=ContentBeforeCommand(text,translation.Index);
            if(!string.IsNullOrWhiteSpace(content))return new(content,SpokenAction.Translate,language);
        }
        // Formatting commands are accepted only as a trailing instruction, and always with an
        // explicit verb or the word "alles". A sentence that merely mentions a language, a
        // formatting term or a filler word keeps its content.
        foreach(var (pattern,action) in FormattingCommandPatterns)
        {
            var command=pattern.Match(text);
            if(!command.Success||command.Index<=0)continue;
            var content=ContentBeforeCommand(text,command.Index);
            if(!string.IsNullOrWhiteSpace(content))return new(content,action);
        }
        return new(text, SpokenAction.None);
    }
    private static string ContentBeforeCommand(string text,int commandStart)
    {
        var content=text[..commandStart].TrimEnd();
        // Natural German and English dictation often links the final instruction with “und/and”.
        // It is part of the instruction, never of the text to translate.
        content=Regex.Replace(content,@"(?:\s*,)?\s+(?:und|and)\s*$","",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        return content.TrimEnd(' ', ',', ';', ':');
    }
    private static readonly Regex[] TranslationCommandPatterns =
    [
        // “Übersetze das bitte ins Englische.”
        new(@"(?:[,.;!?;:]\s*|\s+)(?:bitte\s+)?[uü]bersetz(?:e|t)?\s+(?:(?:das|dies(?:en|e)?|den\s+text|alles|es)\s+)?(?:bitte\s+|doch\s+|mal\s+)?(?:auf|in|ins)\s+(?<language>[\p{L}]+)(?:\s+(?:bitte|doch|mal))?\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),
        // “... und das hätte ich jetzt gerne auf Englisch übersetzt.”
        new(@"(?:[,.;!?;:]\s*|\s+und\s+)(?:(?:das|dies(?:en|e)?(?:\s+text)?|den\s+text|alles|es|das\s+ganze)\s+)?(?:ich\s+)?(?:hätte|m[oö]chte|will)\s+(?:ich\s+)?(?:(?:das|dies(?:en|e)?(?:\s+text)?|den\s+text|alles|es)\s+)?(?:(?:jetzt|bitte|doch|mal|gerne)\s+)*(?:auf|in|ins)\s+(?<language>[\p{L}]+)(?:\s+(?:[uü]bersetzt|[uü]bersetzen|haben))?\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),
        // “Mach das bitte auf Englisch.” The explicit object and final language make this
        // conservative even when the preceding sentence itself mentions a language.
        new(@"(?:[,.;!?;:]\s*|\s+)(?:mach(?:e)?|gib)\s+(?:mir\s+)?(?:das|dies(?:en|e)?(?:\s+text)?|den\s+text|alles|es|das\s+ganze)\s+(?:(?:bitte|jetzt|doch|mal|gerne)\s+)*(?:auf|in|ins)\s+(?<language>[\p{L}]+)(?:\s+(?:[uü]bersetzt|[uü]bersetzen))?\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),
        // “Kannst du das auf Englisch übersetzen?” Must run before the shorter
        // “Das …” rule so the request itself is never retained as source content.
        new(@"(?:[,.;!?;:]\s*|\s+)(?:kannst\s+(?:du\s+)?)?(?:das|dies(?:en|e)?(?:\s+text)?|den\s+text|alles|es)\s+(?:bitte\s+)?(?:auf|in|ins)\s+(?<language>[\p{L}]+)\s+[uü]bersetzen\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),
        // “Das Ganze jetzt auf Französisch.” / “Das bitte ins Spanische übersetzen.”
        new(@"(?:[,.;!?;:]\s*|\s+)(?:das|dies(?:en|e)?(?:\s+text)?|den\s+text|alles|es|das\s+ganze)(?:(?:\s+(?:bitte|jetzt|doch|mal|gerne))*)\s+(?:auf|in|ins)\s+(?<language>[\p{L}]+)(?:\s+(?:[uü]bersetzt|[uü]bersetzen))?\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),
        // “Mach daraus eine englische Version.”
        new(@"(?:[,.;!?;:]\s*|\s+)(?:mach(?:e)?|gib)\s+(?:mir\s+)?daraus\s+(?:bitte\s+)?(?:eine\s+)?(?<language>[\p{L}]+)\s+version\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),
        // English requests: “Translate that to German.” / “Put that in French.”
        new(@"(?:[,.;!?;:]\s*|\s+)(?:please\s+)?translate\s+(?:this(?:\s+text)?|that)\s+(?:into|to)\s+(?<language>[\p{L}]+)\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),
        new(@"(?:[,.;!?;:]\s*|\s+)(?:put|give)\s+(?:me\s+)?(?:this|that)\s+(?:in|to)\s+(?<language>[\p{L}]+)\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)
    ];
    // Trailing formatting instructions. Every pattern names an explicit object or the word
    // "alles", so ordinary sentences about formatting are never treated as a command.
    private static readonly (Regex Pattern, SpokenAction Action)[] FormattingCommandPatterns =
    [
        (new(@"(?:[,.;!?;:]\s*|\s+)(?:schreib|schreibe|mach|mache|formuliere)\s+(?:das\s+|dies(?:en|e)?\s+|den\s+text\s+|den\s+|alles\s+|alles\s+bitte\s+)?(?:bitte\s+)?(?:in\s+)?großbuchstaben\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),SpokenAction.Uppercase),
        (new(@"(?:[,.;!?;:]\s*|\s+)alles\s+(?:bitte\s+)?(?:in\s+)?(?:großbuchstaben|uppercase)\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),SpokenAction.Uppercase),
        (new(@"(?:[,.;!?;:]\s*|\s+)(?:schreib|schreibe|mach|mache|formuliere)\s+(?:das\s+|dies(?:en|e)?\s+|den\s+text\s+|den\s+|alles\s+)?(?:bitte\s+)?(?:in\s+)?kleinbuchstaben\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),SpokenAction.Lowercase),
        (new(@"(?:[,.;!?;:]\s*|\s+)alles\s+(?:bitte\s+)?(?:in\s+)?(?:kleinbuchstaben|lowercase)\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),SpokenAction.Lowercase),
        (new(@"(?:[,.;!?;:]\s*|\s+)(?:mach|mache|schreib|schreibe|formuliere)\s+(?:das\s+|dies(?:en|e)?\s+|daraus\s+|aus\s+(?:diesem|dem)\s+text\s+)?(?:bitte\s+)?(?:zu\s+|als\s+|in\s+)?(?:eine\s+)?(?:stichpunkte|stichpunktliste|stichpunkten)\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),SpokenAction.Bullets),
        (new(@"(?:[,.;!?;:]\s*|\s+)(?:in|als)\s+(?:bitte\s+)?stichpunkten\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),SpokenAction.Bullets),
        (new(@"(?:[,.;!?;:]\s*|\s+)(?:mach|mache|schreib|schreibe|formuliere)\s+(?:daraus\s+)?(?:bitte\s+)?(?:eine\s+|einen\s+)?(?:professionelle(?:r)?\s+)?e-?mail\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),SpokenAction.ProfessionalEmail),
        (new(@"(?:[,.;!?;:]\s*|\s+)(?:formuliere|mach|mache|schreib|schreibe)\s+(?:das\s+|dies(?:en|e)?\s+|den\s+text\s+)?(?:bitte\s+)?freundlicher\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),SpokenAction.Friendly),
        (new(@"(?:[,.;!?;:]\s*|\s+)freundlicher\s+formulier(?:e|en)\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),SpokenAction.Friendly),
        (new(@"(?:[,.;!?;:]\s*|\s+)(?:korrigiere|korrigier|berichtige)\s+(?:bitte\s+)?(?:nur\s+)?(?:die\s+)?rechtschreibung\s*(?:[.!?]|\s+und\s+nichts\s+weitere[nr]?\s*[.!?])\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),SpokenAction.SpellingOnly),
        (new(@"(?:[,.;!?;:]\s*|\s+)(?:nur\s+)?rechtschreibung\s+(?:korrigier(?:e|en)|bearbeit(?:e|en)|prüf(?:e|en))\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),SpokenAction.SpellingOnly),
        (new(@"(?:[,.;!?;:]\s*|\s+)(?:fasse|fass)\s+(?:das\s+|dies(?:en|e)?\s+|den\s+text\s+)?(?:bitte\s+)?kurz\s+zusammen\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),SpokenAction.Summarize),
        (new(@"(?:[,.;!?;:]\s*|\s+)(?:fasse|fass)\s+(?:das\s+|dies(?:en|e)?\s+|den\s+text\s+)?(?:bitte\s+)?zusammen\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),SpokenAction.Summarize),
        (new(@"(?:[,.;!?;:]\s*|\s+)(?:entferne|entfern|streiche|lass\s+weg)\s+(?:bitte\s+)?(?:die\s+|alle\s+|das\s+)?(?:füllwörter|fuellwoerter|fillerwörter|fillerwoerter)\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),SpokenAction.RemoveFillers),
        (new(@"(?:[,.;!?;:]\s*|\s+)ohne\s+füllwörter\s*[.!?]*\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant),SpokenAction.RemoveFillers)
    ];
}
public static class OutputPipeline
{
    // A deterministic safety net, not a claim of semantic equivalence. Unverifiable edits keep the source.
    public static bool PreservesProtectedDetails(string source, string result, IEnumerable<string>? vocabulary = null)
    {
        if (string.IsNullOrWhiteSpace(result)) return false;
        static string[] Numbers(string value) => Regex.Matches(value, @"\d+(?:[,:]\d+)*").Select(m => m.Value).Order().ToArray();
        if (!Numbers(source).SequenceEqual(Numbers(result))) return false;
        var protectedNames = (vocabulary ?? []).Where(word => !string.IsNullOrWhiteSpace(word) && source.Contains(word, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (Match match in Regex.Matches(source, @"\b(?:ich bin|ich heiße|mein Name ist|Hallo|Hi|I am|my name is)\s*,?\s*(?<name>[A-ZÄÖÜ][\p{L}-]+)", RegexOptions.CultureInvariant))
            protectedNames.Add(match.Groups["name"].Value);
        if (protectedNames.Any(name => !result.Contains(name, StringComparison.OrdinalIgnoreCase))) return false;
        // A bare clock time must not gain an AM/PM interpretation during translation.
        if (Regex.IsMatch(source, @"\d{1,2}(?::\d{2})?\s*Uhr", RegexOptions.IgnoreCase) &&
           !Regex.IsMatch(source, @"\b(morgens|vormittags|nachmittags|abends|nachts|morning|afternoon|evening|am|pm)\b", RegexOptions.IgnoreCase) &&
           Regex.IsMatch(result, @"\b(morning|afternoon|evening|matin|après-midi|soir|tarde|noche|mattina|pomeriggio)\b|de la mañana|\d\s*(?:a\.?m\.?|p\.?m\.?)\b|上午|下午|早上|晚上", RegexOptions.IgnoreCase)) return false;
        return true;
    }
    private static readonly string[] Negations = ["nicht", "kein", "keine", "keinen", "keinem", "keiner", "niemals", "nie", "not", "never", "without"];
    /// <summary>True when a negation present in the source disappeared from the result.</summary>
    public static bool PreservesNegations(string source, string result)
    {
        var originalWords = Regex.Matches(source.ToLowerInvariant(), @"[\p{L}]+").Select(m => m.Value).ToHashSet();
        var outputWords = Regex.Matches(result.ToLowerInvariant(), @"[\p{L}]+").Select(m => m.Value).ToHashSet();
        return !Negations.Any(word => originalWords.Contains(word) && !outputWords.Contains(word));
    }
    /// <summary>True when the result contains a number that the source did not contain.</summary>
    public static bool IntroducesNewDetails(string source, string result)
    {
        var sourceNumbers = Regex.Matches(source, @"\d+(?:[.,:]\d+)*").Select(m => m.Value).ToHashSet();
        var resultNumbers = Regex.Matches(result, @"\d+(?:[.,:]\d+)*").Select(m => m.Value).ToHashSet();
        return resultNumbers.Any(number => !sourceNumbers.Contains(number));
    }
    /// <summary>
    /// Decides whether an explicit spoken edit may replace the dictated text. A summary is
    /// allowed to leave details out, but never to add them or to drop a negation.
    /// </summary>
    public static bool AcceptsSpokenEdit(SpokenAction action, string source, string result)
    {
        if (string.IsNullOrWhiteSpace(result)) return false;
        if (action == SpokenAction.Summarize)
            return !IntroducesNewDetails(source, result) && PreservesNegations(source, result);
        return PreservesProtectedDetails(source, result);
    }
    public static bool IsConservativeEdit(string source, string result)
    {
        if (!PreservesProtectedDetails(source, result)) return false;
        var numbers = Regex.Matches(source, @"\d+(?:[.,:]\d+)*").Select(m => m.Value).Order().ToArray();
        var outputNumbers = Regex.Matches(result, @"\d+(?:[.,:]\d+)*").Select(m => m.Value).Order().ToArray();
        if (!numbers.SequenceEqual(outputNumbers)) return false;
        if (!PreservesNegations(source, result)) return false;
        var originalWords = Regex.Matches(source.ToLowerInvariant(), @"[\p{L}]+").Select(m => m.Value).ToHashSet();
        var outputWords = Regex.Matches(result.ToLowerInvariant(), @"[\p{L}]+").Select(m => m.Value).ToHashSet();
        return outputWords.Intersect(originalWords).Count() >= originalWords.Count * .65 && result.Length <= source.Length * 2.2;
    }
    public static string Format(string text, OutputCasing casing) => casing switch { OutputCasing.Lowercase => text.Replace("İ", "i\u0307", StringComparison.Ordinal).ToLowerInvariant(), OutputCasing.Uppercase => text.Replace("ß", "SS", StringComparison.Ordinal).ToUpperInvariant(), _ => text };
    public static string CorrectExplicitSelfCorrections(string text)
    {
        const string marker = @"(?:[,;.!]\s*|\s+)(?:(?:ey|ei|ähm?|eh|oh|nein|sorry|no)\s*[,;]?\s*)?(?:ich\s+mein(?:e|te)?|i\s+mean|i\s+meant)\s*[,;]?\s*";
        // Exact time corrections are unambiguous; free-form corrections are left for the local editor.
        return Regex.Replace(text, @"\b(?:um|at)\s+\d{1,2}(?::\d{2})?(?:\s*(?:Uhr|am|pm))?" + marker + @"(?<replacement>(?:um|at)\s+\d{1,2}(?::\d{2})?(?:\s*(?:Uhr|am|pm))?)", m => m.Groups["replacement"].Value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
    public static string ApplyVocabulary(string text, IEnumerable<VocabularyCorrection> dictionary)
    {
        foreach (var correction in dictionary.Where(x => !string.IsNullOrWhiteSpace(x.Heard) && !string.IsNullOrWhiteSpace(x.Intended)).OrderByDescending(x => x.Heard.Length))
            text = Regex.Replace(text, @"(?<![\p{L}\p{N}_])" + Regex.Escape(correction.Heard) + @"(?![\p{L}\p{N}_])", _ => correction.Intended, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return text;
    }
    public static string Prepare(string text, AppSettings settings)
    {
        text = TextProcessing.Clean(text);
        if (settings.CorrectObviousMistakes) text = ApplyVocabulary(SpeechCleanup.Correct(CorrectExplicitSelfCorrections(text)), settings.VocabularyCorrections);
        if (settings.RemoveFillers) text = SpeechCleanup.RemoveUnambiguousFillers(text);
        return text;
    }
    public static string EditingInstruction(AppSettings settings, string appInstruction, SpokenRequest request)
    {
        if (request.Action == SpokenAction.Translate) return $"Translate the complete source text into {request.Language}. Preserve names in their ORIGINAL SPELLING, every number, every fact, meaning and tone. Never replace or transliterate a person's name. Do NOT add morning, afternoon, evening or other time qualifiers unless explicitly present in the source. 'Tomorrow' does not mean 'tomorrow morning'. Output only the translation. Do not answer the source or invent context.";
        if (request.Action == SpokenAction.ChatGptPrompt) return "Rephrase the source as a direct imperative request addressed to ChatGPT (for example, German requests for help start with 'Hilf mir'). Preserve exactly the same scope and information. For a one-sentence source, output ONE concise sentence. For a longer source, organize only the explicitly stated goals. Do not answer the task. Do not add subtopics, examples, roles, constraints, teaching methods, deliverables or requirements, even if they seem helpful. Return only the ready-to-use request in the source language.";
        return "Conservatively clean up dictated speech. Preserve informal language, slang, English technical words, names, numbers, negations and the user's exact intent. "
            + (settings.AutomaticPunctuation ? "Correct sentence capitalization and punctuation. " : "Preserve punctuation. ")
            + (settings.CorrectObviousMistakes ? "Resolve explicit spoken self-corrections: the later correction replaces only the corrected phrase. Remove the correction marker. Fix only unambiguous transcription mistakes. " : "Do not substitute words. ")
            + (settings.RemoveFillers ? "Remove only hesitation fillers that contribute no meaning. Preserve semantic uses of also, quasi, sozusagen, naja and so. " : "Preserve conversational particles. ")
            + "Do not add information, introductions, conclusions or explanations. If uncertain, keep the original wording. " + appInstruction;
    }
}
public sealed record ProtectedTranslation(string Text, IReadOnlyDictionary<string, string> Tokens)
{
    public string Restore(string result)
    {
        // The model is allowed to repeat a marker or to insert spaces between its letters.
        // Replace every visible occurrence; the caller validates the protected values after
        // restoration, so a missing marker is reported there instead of failing this pass.
        foreach (var pair in Tokens)
        {
            var pattern = TokenPattern(pair.Key);
            result = Regex.Replace(result, pattern, _ => pair.Value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        return result;
    }
    private static string TokenPattern(string token) =>
        @"(?<![\p{L}\p{N}_])" + string.Join(@"\s*", token.Select(character => Regex.Escape(character.ToString()))) + @"(?![\p{L}\p{N}_])";
    public static ProtectedTranslation Create(string source, string language, IEnumerable<string>? customWords = null)
    {
        var tokens = new Dictionary<string, string>();
        string Token(string value)
        {
            var token = "SONABARLITERAL" + new string('X', tokens.Count + 1);
            while (source.Contains(token, StringComparison.OrdinalIgnoreCase) || tokens.ContainsKey(token)) token += "X";
            tokens[token] = value;
            return token;
        }
        // Hide ambiguous clock times and literal values from the model so it cannot alter a
        // schedule, price, or dictionary term while translating the surrounding sentence.
        var text = Regex.Replace(source, @"\b(?<time>\d{1,2}(?::\d{2})?)\s*Uhr\b", match =>
        {
            var time = match.Groups["time"].Value;
            return Token(time.Contains(':') ? time : language switch { "Chinese" => time + "点", "Japanese" => time + "時", "Korean" => time + "시", "German" => time + " Uhr", "French" => time + " heures", "English" => time + " o’clock", _ => time });
        }, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(?<![\p{L}\p{N}])\d+(?:[,.]\d+)*(?![\p{L}\p{N}])", match => Token(match.Value), RegexOptions.CultureInvariant);
        foreach(var word in (customWords??[]).Where(x=>!string.IsNullOrWhiteSpace(x)).OrderByDescending(x=>x.Length))
            text=Regex.Replace(text,@"(?<![\p{L}\p{N}_])"+Regex.Escape(word)+@"(?![\p{L}\p{N}_])",match=>Token(match.Value),RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        return new(text, tokens);
    }
}
public sealed record RewriteAction(string Id, string Label, string Instruction)
{
    public override string ToString() => Label;
}
public static class RewriteActions
{
    public static IReadOnlyList<RewriteAction> All { get; } = [
        new("grammar","Fix spelling & grammar","Correct spelling, sentence capitalization, punctuation and grammar. Preserve wording when already correct."),
        new("improve","Improve writing","Improve clarity and flow without changing the message."),
        new("professional","Professional","Use a professional tone."),new("serious","More serious","Use a serious, matter-of-fact tone."),
        new("friendly","Friendlier","Use a warm, friendly tone."),new("casual","Casual","Use natural, casual everyday language."),
        new("funny","More playful","Use a lightly humorous tone without inventing facts or adding jokes that change the point."),new("romantic","More romantic","Use an affectionate, romantic tone while preserving the stated relationship and intent."),
        new("shorten","Shorten","Make it concise, retaining all essential facts, names, dates and requests."),new("expand","Expand","Explain the same points more fully. Do not invent examples, facts or commitments."),
        new("simple","Simpler","Use simple, easy-to-understand language."),new("direct","More direct","Be clear and direct, retaining politeness."),new("polite","More polite","Use courteous language without changing requests or commitments."),
        new("bullets","Bullet list","Format the same information as a bullet list."),new("tasks","To-do list","Format stated tasks as a to-do list. Do not invent tasks."),
        new("prompt","ChatGPT prompt","Turn the stated goal into a clear ChatGPT prompt. Include only supplied facts and constraints. Do not answer the prompt."),
        new("translate","Translate","Translate into the selected target language."),new("custom","Custom instruction","")
    ];
}
