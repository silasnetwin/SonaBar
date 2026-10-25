using System.Text.RegularExpressions;
namespace SonaBar.Core;

public static class SpeechCleanup
{
    private const string Number = @"(?:\d{1,3}(?::\d{2})?|null|eins|ein|eine|zwei|drei|vier|fünf|sechs|sieben|acht|neun|zehn|elf|zwölf|dreizehn|vierzehn|fünfzehn|sechzehn|siebzehn|achtzehn|neunzehn|zwanzig)";
    private const string Marker = @"\s*[,;–—-]?\s*(?:(?:ähm?|ey|ei|oh)\s*[,;]?\s*)?(?:ich\s+mein(?:e|te)?|nein|sorry)\s*[,;]?\s*";
    private const string Day = @"(?:Montag|Dienstag|Mittwoch|Donnerstag|Freitag|Samstag|Sonntag)";
    public static string Correct(string text)
    {
        // Do not rewrite quoted examples, code, or explanations of correction markers.
        if (text.IndexOfAny(['"', '„', '“', '`']) >= 0 || Regex.IsMatch(text, @"\b(?:Beispiel|Wort|Ausdruck|phrase)\b", RegexOptions.IgnoreCase)) return text;
        text = Regex.Replace(text, @"\b(?<prefix>um\s+)" + Number + @"(?:\s+Uhr)?" + Marker + @"(?:um\s+)?(?<new>" + Number + @"(?:\s+Uhr)?)(?![\p{L}\d])", m => m.Groups["prefix"].Value + m.Groups["new"].Value, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(?<prefix>am\s+)" + Day + Marker + @"(?:am\s+)?(?<new>" + Day + @")(?=\s*(?:[.!?,;]|$|um\s))", m => m.Groups["prefix"].Value + m.Groups["new"].Value, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(?<prefix>(?:ich\s+)?(?:brauche|benötige|bestelle)\s+)" + Number + Marker + @"(?<new>" + Number + @")(?=\s+(?:Stück|Euro|Kilogramm|Gramm|Liter|Meter|Minuten|Stunden)\b)", m => m.Groups["prefix"].Value + m.Groups["new"].Value, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(?<prefix>(?:mein\s+Name\s+ist|ich\s+heiße)\s+)[\p{L}]+" + Marker + @"(?<new>[\p{L}]+)(?=\s*(?:[.!?]|$))", m => m.Groups["prefix"].Value + m.Groups["new"].Value, RegexOptions.IgnoreCase);
        return text;
    }

    public static string RemoveUnambiguousFillers(string text)
    {
        if (text.IndexOfAny(['"', '„', '“', '`']) >= 0) return text;
        text = Regex.Replace(text, @"^(?:ähm?|uhm?|erm)\s*[,;]\s*", "", RegexOptions.IgnoreCase);
        return Regex.Replace(text, @",\s*(?:ähm?|uhm?|erm)\s*,\s*", ", ", RegexOptions.IgnoreCase);
    }
}
