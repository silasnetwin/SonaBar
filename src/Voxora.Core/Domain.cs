using System.Text.RegularExpressions;

namespace Voxora.Core;

public static class Brand
{
    public const string Name = "SineFlow", Publisher = "SNine Studios", Version = "0.1.3";
    public static string LegacyDataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Publisher, "Voxora");
    public static string DataRoot => Environment.GetEnvironmentVariable("SINEFLOW_DATA_ROOT") is string root&&!string.IsNullOrWhiteSpace(root)?Path.GetFullPath(root):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Publisher, Name);
    public static void MigrateLegacyData()
    {
        if (Environment.GetEnvironmentVariable("SINEFLOW_DATA_ROOT") is not null) return;
        if (!Directory.Exists(DataRoot) && Directory.Exists(LegacyDataRoot))
        {
            try { Directory.Move(LegacyDataRoot, DataRoot); } catch (IOException) { return; }
        }
        var legacyDatabase = Path.Combine(DataRoot, "voxora.db");
        var database = Path.Combine(DataRoot, "sineflow.db");
        if (File.Exists(legacyDatabase) && !File.Exists(database))
        {
            try { File.Move(legacyDatabase, database); } catch (IOException) { }
        }
    }
}
public enum OperationState { Idle, Recording, Transcribing, Enhancing, Inserting, Cancelled, Failed }
public sealed class OperationMachine
{
    public OperationState State { get; private set; }
    public event Action<OperationState>? Changed;
    public void Move(OperationState next)
    {
        var valid = next is OperationState.Cancelled or OperationState.Failed || (State, next) switch
        {
            (OperationState.Idle, OperationState.Recording or OperationState.Transcribing or OperationState.Enhancing) => true,
            (OperationState.Recording, OperationState.Transcribing) => true,
            (OperationState.Transcribing, OperationState.Enhancing or OperationState.Inserting or OperationState.Idle) => true,
            (OperationState.Enhancing, OperationState.Inserting or OperationState.Idle) => true,
            (OperationState.Inserting or OperationState.Cancelled or OperationState.Failed, OperationState.Idle) => true,
            _ => false
        };
        if (!valid) throw new InvalidOperationException($"Cannot move from {State} to {next}.");
        State = next; Changed?.Invoke(next);
    }
}
public enum ClipboardMode { Preserve, Replace, Ask }
public enum CaptureMode { Microphone, System, Both }
public enum InsertionStrategy { Automatic, Clipboard, Unicode }
public enum SpeechQuality { Fast, Balanced, Maximum }
public enum OutputCasing { Normal, Lowercase, Uppercase }
public sealed record Hotkey(uint Modifiers, uint Key)
{
    public override string ToString() => $"{((Modifiers & 2) != 0 ? "Ctrl + " : "")}{((Modifiers & 4) != 0 ? "Shift + " : "")}{((Modifiers & 1) != 0 ? "Alt + " : "")}{((Modifiers & 8) != 0 ? "Win + " : "")}{(Key == 32 ? "Space" : ((char)Key).ToString())}";
}
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 4;
    public bool Onboarded { get; set; }
    public string Language { get; set; } = "auto";
    public string SpeechModel { get; set; } = "small";
    public SpeechQuality Quality {get;set;} = SpeechQuality.Balanced;
    /// <summary>When set, the speech profile follows the detected hardware instead of a manual choice.</summary>
    public bool AutoQuality { get; set; } = true;
    public bool UseCustomSpeechModel {get;set;}
    public OutputCasing OutputCasing {get;set;}
    public bool AutomaticPunctuation {get;set;} = true;
    public bool CorrectObviousMistakes {get;set;} = true;
    public bool SpokenInstructions {get;set;} = true;
    public bool ShowQuickActions {get;set;} = true;
    public bool SelectionActions { get; set; } = true;
    public bool RemoveFillers { get; set; }
    public bool AppAwareFormatting { get; set; } = true;
    public int HistoryRetentionDays { get; set; } = 30;
    public List<VocabularyCorrection> VocabularyCorrections {get;set;} = [];
    public string EffectiveSpeechModel => UseCustomSpeechModel ? SpeechModel : SpeechProfiles.ModelFor(Quality);
    public string Theme { get; set; } = "Light";
    public string Backend { get; set; } = "Automatic";
    public string? MicrophoneId { get; set; }
    public Hotkey PlainHotkey { get; set; } = new(6, 32);
    public Hotkey EditHotkey { get; set; } = new(6, 69);
    public bool PushToTalk { get; set; }
    public ClipboardMode Clipboard { get; set; } = ClipboardMode.Preserve;
    public bool StoreHistory { get; set; }
    public bool DuckAudio { get; set; }
    public string Sounds { get; set; } = "Off";
    public bool KeepSpeechLoaded { get; set; } = true;
    public bool KeepEnhancementLoaded { get; set; } = true;
    // SineFlow is designed to wait quietly in the tray so its global shortcut is
    // always available. People can turn this off in General settings.
    public bool LaunchAtLogin { get; set; } = true;
    public bool AutomaticUpdates { get; set; } = true;
    public bool DebugLogging { get; set; }
    public double OverlayLeft { get; set; } = -1;
    public double OverlayTop { get; set; } = -1;
    public int? OverlayPixelLeft {get;set;}
    public int? OverlayPixelTop {get;set;}
    public List<string> CustomWords { get; set; } = ["SineFlow", "SNine Studios"];
    public List<Snippet> Snippets { get; set; } = [];
    public List<ApplicationProfile> Profiles { get; set; } = [];
    public ApplicationProfile ProfileFor(string executable) => Profiles.FirstOrDefault(p => p.Executable.Equals(executable, StringComparison.OrdinalIgnoreCase)) ?? new(executable, InsertionStrategy.Automatic, false, executable.ToLowerInvariant() switch
    {
        "outlook.exe" => "Format as a clear email, retaining the original language. Do not invent a subject or signature.",
        "discord.exe" => "Keep a natural, casual conversational message. Avoid formal sign-offs.",
        "teams.exe" or "ms-teams.exe" => "Use professional but conversational workplace language.",
        "code.exe" or "idea64.exe" or "rider64.exe" => "Preserve programming identifiers, technical terminology and code exactly.",
        "windowsterminal.exe" or "cmd.exe" or "powershell.exe" or "pwsh.exe" => "Return the original input unchanged. Do not generate or modify commands.",
        _ => "Preserve the user's intent and language."
    });
    public void Validate()
    {
        if (Language is null||!Languages.All.ContainsKey(Language)) throw new InvalidDataException("Choose a supported language.");
        if(PlainHotkey is null||EditHotkey is null||CustomWords is null||VocabularyCorrections is null||Snippets is null||Profiles is null)throw new InvalidDataException("Settings contain missing values.");
        if(!Enum.IsDefined(Clipboard)||Theme is not ("System" or "Light" or "Dark")||Backend is not ("Automatic" or "CPU" or "GPU / accelerated"))throw new InvalidDataException("Invalid app preferences.");
        if(!double.IsFinite(OverlayLeft)||!double.IsFinite(OverlayTop))throw new InvalidDataException("Invalid overlay position.");
        if(!Enum.IsDefined(Quality)||!Enum.IsDefined(OutputCasing))throw new InvalidDataException("Invalid transcription preferences.");
        if(HistoryRetentionDays is <0 or >3650)throw new InvalidDataException("Choose a history retention period between 0 and 3650 days.");
        if (new[] { PlainHotkey, EditHotkey }.Distinct().Count() != 2) throw new InvalidDataException("Each action needs a different shortcut.");
        if (new[] { PlainHotkey, EditHotkey }.Any(h => h.Modifiers == 0 || h.Key < 8 || h.Key > 254)) throw new InvalidDataException("Shortcuts need a modifier and a valid key.");
    }
}
public sealed record ApplicationProfile(string Executable, InsertionStrategy Strategy, bool PressEnter, string Instruction);
public sealed record Snippet(string Name, string Trigger, string Content);
public sealed record VocabularyCorrection(string Heard,string Intended);
public sealed record TranscriptSegment(TimeSpan Start, TimeSpan End, string Text, string Speaker = "");
public sealed record TranscriptionResult(string Text, string Language, TimeSpan Duration, IReadOnlyList<TranscriptSegment> Segments);
public sealed record TranscriptionEngineCapabilities(bool SupportsStreaming, bool SupportsBatch, bool SupportsCancellation, IReadOnlyCollection<string> Languages, string Runtime);
public sealed record HistoryEntry(long Id, DateTimeOffset Timestamp, string Original, string Enhanced, double Duration, string Language, string Application, string Model)
{
    public string Text => string.IsNullOrWhiteSpace(Enhanced) ? Original : Enhanced;
    public int WordCount => Regex.Matches(Text, @"\S+").Count;
    public override string ToString() => $"{Timestamp.LocalDateTime:g}  ·  {Application}\n{Text}";
}
public sealed record ModelFile(string Path, string Url, string Sha256, long Bytes);
public sealed record ModelDefinition(string Id, string Name, string Engine, string Url, string Sha256, long Bytes, string Hardware, string License)
{
    /// <summary>A model made of several files is stored in a directory named after its Id.</summary>
    public IReadOnlyList<ModelFile> Files { get; init; } = [];
    public bool IsBundle => Files.Count != 0;
    public long TotalBytes => IsBundle ? Files.Sum(file => file.Bytes) : Bytes;
}
public sealed record DownloadProgress(long Received, long Total, string Stage)
{
    public double Percent => Total > 0 ? Received * 100d / Total : 0;
}
public interface ITranscriptionEngine : IDisposable
{
    Task<TranscriptionResult> TranscribeAsync(string waveFile, string modelPath, string language, string prompt, bool keepLoaded, CancellationToken cancellationToken);
    Task WarmAsync(string modelPath, CancellationToken cancellationToken) => Task.CompletedTask;
    Task UnloadAsync(CancellationToken cancellationToken) { Dispose(); return Task.CompletedTask; }
    TranscriptionEngineCapabilities Capabilities => new(false, true, true, Languages.All.Keys.ToArray(), "Unknown");
}
public interface IEnhancementEngine : IDisposable
{
    Task<string> EnhanceAsync(string text, string instruction, string modelPath, bool keepLoaded, CancellationToken cancellationToken);
}
public interface IAudioCaptureService : IDisposable
{
    event Action<float>? Level;
    event Action<string>? Faulted;
    void Start(string path, CaptureMode mode, string? microphoneId);
    Task StopAsync();
}
public interface IModelManager
{
    IReadOnlyList<ModelDefinition> Catalog { get; }
    string PathFor(ModelDefinition model);
    bool IsInstalled(ModelDefinition model);
    Task DownloadAsync(ModelDefinition model, IProgress<DownloadProgress>? progress, CancellationToken token);
}
public static class Languages
{
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        ["auto"]="Automatic", ["en"]="English", ["de"]="German", ["fr"]="French", ["es"]="Spanish", ["it"]="Italian", ["pt"]="Portuguese", ["nl"]="Dutch", ["pl"]="Polish", ["ja"]="Japanese", ["zh"]="Chinese", ["ko"]="Korean", ["uk"]="Ukrainian", ["ru"]="Russian", ["ar"]="Arabic", ["hi"]="Hindi", ["tr"]="Turkish", ["sv"]="Swedish", ["da"]="Danish", ["no"]="Norwegian", ["fi"]="Finnish", ["cs"]="Czech", ["el"]="Greek", ["he"]="Hebrew", ["hu"]="Hungarian", ["ro"]="Romanian", ["vi"]="Vietnamese", ["id"]="Indonesian", ["th"]="Thai", ["ca"]="Catalan", ["sk"]="Slovak", ["bg"]="Bulgarian", ["hr"]="Croatian", ["sr"]="Serbian", ["sl"]="Slovenian", ["et"]="Estonian", ["lv"]="Latvian", ["lt"]="Lithuanian", ["fa"]="Persian", ["ur"]="Urdu", ["bn"]="Bengali", ["ta"]="Tamil", ["te"]="Telugu", ["ml"]="Malayalam", ["mr"]="Marathi", ["gu"]="Gujarati", ["kn"]="Kannada", ["pa"]="Punjabi", ["sw"]="Swahili", ["af"]="Afrikaans", ["is"]="Icelandic", ["cy"]="Welsh", ["ga"]="Irish", ["mt"]="Maltese", ["eu"]="Basque", ["gl"]="Galician", ["sq"]="Albanian", ["mk"]="Macedonian", ["bs"]="Bosnian", ["hy"]="Armenian", ["ka"]="Georgian", ["az"]="Azerbaijani", ["kk"]="Kazakh", ["uz"]="Uzbek", ["ne"]="Nepali", ["si"]="Sinhala", ["km"]="Khmer", ["lo"]="Lao", ["my"]="Myanmar", ["ms"]="Malay", ["tl"]="Tagalog", ["mn"]="Mongolian", ["bo"]="Tibetan", ["la"]="Latin", ["yi"]="Yiddish", ["sa"]="Sanskrit", ["ps"]="Pashto", ["tg"]="Tajik", ["tk"]="Turkmen", ["tt"]="Tatar", ["ba"]="Bashkir", ["be"]="Belarusian", ["br"]="Breton", ["fo"]="Faroese", ["lb"]="Luxembourgish", ["nn"]="Nynorsk", ["oc"]="Occitan", ["sd"]="Sindhi", ["am"]="Amharic", ["as"]="Assamese", ["ha"]="Hausa", ["jw"]="Javanese", ["su"]="Sundanese", ["mg"]="Malagasy", ["mi"]="Maori", ["sn"]="Shona", ["so"]="Somali", ["yo"]="Yoruba", ["ht"]="Haitian Creole", ["ln"]="Lingala", ["haw"]="Hawaiian", ["yue"]="Cantonese"
    };
}
public static class TextProcessing
{
    public static string Clean(string text)
    {
        text = text.Replace("\r\n", "\n").Trim();
        text = Regex.Replace(text, @"\[(?:BLANK_AUDIO|SILENCE|Music)\]", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @" +([,.;!?])", "$1");
        text = Regex.Replace(text, @" +\n", "\n");
        return Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
    }
    public static string Expand(string text, IEnumerable<Snippet> snippets, DateTime now, Func<string> clipboard)
    {
        var snippet = snippets.FirstOrDefault(s => s.Trigger.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(t => t.Equals(text.Trim().TrimEnd('.', '!', '?'), StringComparison.OrdinalIgnoreCase)));
        return snippet is null ? text : snippet.Content.Replace("{date}", now.ToShortDateString()).Replace("{time}", now.ToShortTimeString()).Replace("{clipboard}", snippet.Content.Contains("{clipboard}") ? clipboard() : "");
    }
    public static string Subtitle(IEnumerable<TranscriptSegment> segments, bool vtt)
    {
        var lines = segments.Select((s, i) => $"{i + 1}\n{Stamp(s.Start, vtt)} --> {Stamp(s.End, vtt)}\n{s.Text.Trim()}\n");
        return (vtt ? "WEBVTT\n\n" : "") + string.Join("\n", lines);
    }
    private static string Stamp(TimeSpan t, bool vtt) => $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}{(vtt ? '.' : ',')}{t.Milliseconds:000}";
}
