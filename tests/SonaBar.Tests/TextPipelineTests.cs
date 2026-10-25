using SonaBar.Core;
using Xunit;
namespace SonaBar.Tests;

public class TextPipelineTests
{
    [Fact]
    public void MultipleProtectedTimesSurviveTranslationWithoutAmPmGuesses()
    {
        var protectedText = ProtectedTranslation.Create("Um 10 Uhr und um 11:30 Uhr.", "Chinese");
        Assert.DoesNotContain("10", protectedText.Text); Assert.DoesNotContain("11:30", protectedText.Text);
        var output = string.Join("，", protectedText.Tokens.Keys);
        Assert.Equal("10点，11:30", protectedText.Restore(output));
        Assert.Throws<InvalidDataException>(() => protectedText.Restore("lost details"));
    }
    [Fact]
    public void AutomaticCorrectionCannotReplaceAShortMessageWithDifferentContent()
    {
        Assert.False(OutputPipeline.IsConservativeEdit("Danke Anna!", "Bis morgen!"));
        Assert.False(OutputPipeline.IsConservativeEdit("Ich komme nicht morgen.", "Ich komme morgen."));
        Assert.True(OutputPipeline.IsConservativeEdit("hallo anna danke", "Hallo Anna, danke!"));
    }
    [Theory]
    [InlineData("Hallo Mara, um 11 Uhr.", "你好 Steve，11点。", false)]
    [InlineData("Hallo Mara, um 11 Uhr.", "你好 Mara，11点。", true)]
    [InlineData("Morgen um 11 Uhr.", "Demain matin à 11 heures.", false)]
    [InlineData("Morgen um 11 Uhr.", "Demain à 11 heures.", true)]
    [InlineData("Morgen um 11 Uhr.", "Mañana a las 11.", true)]
    [InlineData("Ich bin Mara und bin 16.", "I am Mara and I am 17.", false)]
    public void TransformationGuardsRejectChangedDetails(string source, string result, bool expected) => Assert.Equal(expected, OutputPipeline.PreservesProtectedDetails(source, result));
    [Theory]
    [InlineData("Hallo, ich bin Mara. Ich möchte diesen Text in Englisch haben.", SpokenAction.Translate, "English", "Hallo, ich bin Mara")]
    [InlineData("Ich bin 16 und gehe in die 11. Klasse, übersetze das auf Französisch.", SpokenAction.Translate, "French", "Ich bin 16 und gehe in die 11. Klasse")]
    [InlineData("Hello Anna. Please translate this into German.", SpokenAction.Translate, "German", "Hello Anna")]
    [InlineData("Wir treffen uns morgen um elf. Übersetze das ins Englische.", SpokenAction.Translate, "English", "Wir treffen uns morgen um elf")]
    [InlineData("Mara arbeitet an SonaBar. Übersetz das ins Französische.", SpokenAction.Translate, "French", "Mara arbeitet an SonaBar")]
    [InlineData("Schreibe eine App. Mach mir daraus einen Prompt für ChatGPT.", SpokenAction.ChatGptPrompt, null, "Schreibe eine App")]
    [InlineData("I need help planning my trip. Turn this into a ChatGPT prompt.", SpokenAction.ChatGptPrompt, null, "I need help planning my trip")]
    public void ExplicitTrailingInstructionsAreSeparated(string text, SpokenAction action, string? language, string content)
    {
        var result = SpokenInstructions.Parse(text); Assert.Equal(action, result.Action); Assert.Equal(language, result.Language); Assert.Equal(content, result.Content);
    }
    [Theory]
    [InlineData("Wir treffen uns morgen um elf und das hätte ich gerne auf Englisch.", "English", "Wir treffen uns morgen um elf")]
    [InlineData("Hallo Anna. Das Ganze jetzt auf Französisch.", "French", "Hallo Anna")]
    [InlineData("Mara arbeitet an SonaBar. Mach das bitte auf Englisch.", "English", "Mara arbeitet an SonaBar")]
    [InlineData("Die Rechnung kostet 149,95 Euro. Das bitte auf Englisch.", "English", "Die Rechnung kostet 149,95 Euro")]
    [InlineData("Hallo Anna. Kannst du das auf Englisch übersetzen?", "English", "Hallo Anna")]
    [InlineData("Hello Anna. Put that in French.", "French", "Hello Anna")]
    [InlineData("Hello Anna. Translate this into Spanish.", "Spanish", "Hello Anna")]
    [InlineData("Hello Anna. Give me that in English.", "English", "Hello Anna")]
    public void NaturalTrailingTranslationRequestsAreSeparated(string text, string language, string content)
    {
        var result = SpokenInstructions.Parse(text);
        Assert.Equal(SpokenAction.Translate, result.Action); Assert.Equal(language, result.Language); Assert.Equal(content, result.Content);
    }
    [Fact]
    public void NaturalLongGermanTranslationRequestKeepsOnlyTheDictatedContent()
    {
        var text = "Hallo, ich bin der Mara. Ich habe hier auf meinem Tisch eine Uhr liegen, eine Tastatur, ein Armband, meine Powerbank, einen Teller und eine Trinkflasche und das hätte ich jetzt gerne auf Englisch übersetzt.";
        var result = SpokenInstructions.Parse(text);
        Assert.Equal(SpokenAction.Translate, result.Action); Assert.Equal("English", result.Language);
        Assert.Contains("Mara", result.Content); Assert.Contains("Tastatur", result.Content); Assert.Contains("Trinkflasche", result.Content);
        Assert.DoesNotContain("hätte ich jetzt gerne", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Englisch übersetzt", result.Content, StringComparison.OrdinalIgnoreCase);
    }
    [Theory]
    [InlineData("Ich möchte Englisch lernen und einen Text schreiben.")]
    [InlineData("Er sagte, ich möchte diesen Text in Englisch haben, aber das war gestern.")]
    [InlineData("ChatGPT ist heute das Thema.")]
    [InlineData("Bitte übersetze das auf Klingonisch.")]
    [InlineData("In Englisch haben wir morgen Unterricht.")]
    [InlineData("Ich lerne Englisch.")]
    [InlineData("Die Übersetzung ins Englische funktioniert.")]
    [InlineData("Anna kann Französisch.")]
    [InlineData("Wir sprechen über englische Grammatik.")]
    public void OrdinaryTextIsNeverStripped(string text) { var result = SpokenInstructions.Parse(text); Assert.Equal(SpokenAction.None, result.Action); Assert.Equal(text, result.Content); }
    [Theory]
    [InlineData("Morgen möchte ich um 10 Uhr, ey ich meine um 11 Uhr.", "Morgen möchte ich um 11 Uhr.")]
    [InlineData("Morgen um 10 Uhr, Ei ich meine um 11 Uhr.", "Morgen um 11 Uhr.")]
    [InlineData("Treffen wir uns um 10:30, ich meinte um 11:45?", "Treffen wir uns um 11:45?")]
    [InlineData("Tomorrow at 10 am, no I mean at 11 am.", "Tomorrow at 11 am.")]
    [InlineData("Ich meine, morgen wird es schön.", "Ich meine, morgen wird es schön.")]
    public void ExplicitTimeCorrectionKeepsOnlyCorrectedTime(string input, string expected) => Assert.Equal(expected, OutputPipeline.CorrectExplicitSelfCorrections(input));
    [Theory]
    [InlineData("am Dienstag – nein Mittwoch", "am Mittwoch")]
    [InlineData("ich brauche fünf, sorry sechs Stück", "ich brauche sechs Stück")]
    [InlineData("mein Name ist Simon, ich meine Mara", "mein Name ist Mara")]
    public void ConservativeSpeechCleanupHandlesClearReplacementPatterns(string input, string expected) => Assert.Equal(expected, SpeechCleanup.Correct(input));
    [Theory]
    [InlineData("Ähm, also das passt.", "also das passt.")]
    [InlineData("Das ist, ähm, wichtig.", "Das ist, wichtig.")]
    [InlineData("Also, das ist wichtig.", "Also, das ist wichtig.")]
    [InlineData("Er sagte: \"ähm, das passt\".", "Er sagte: \"ähm, das passt\".")]
    public void FillerRemovalOnlyRemovesUnambiguousInterjections(string input, string expected) => Assert.Equal(expected, SpeechCleanup.RemoveUnambiguousFillers(input));
    [Theory]
    [InlineData("outlook.exe", "", WritingSurface.Email)]
    [InlineData("discord.exe", "", WritingSurface.Chat)]
    [InlineData("chrome.exe", "Inbox - Gmail - Google Chrome", WritingSurface.Email)]
    [InlineData("chrome.exe", "A Gmail guide - Google Chrome", WritingSurface.Neutral)]
    [InlineData("pwsh.exe", "", WritingSurface.Terminal)]
    public void ApplicationContextUsesOnlyExplicitReliableSignals(string executable, string title, WritingSurface expected) => Assert.Equal(expected, WritingContext.Detect(executable, title));
    [Fact]
    public void VocabularyPromptPrioritizesUserWordsAndStaysBounded()
    {
        var prompt = VocabularyPrompt.Build(["SonaBar", "SilasNet", "Mara", new string('x', 900)]);
        Assert.StartsWith("Mara", prompt); Assert.Contains("SonaBar", prompt); Assert.DoesNotContain(new string('x', 900), prompt);
    }
    [Theory]
    [InlineData(OutputCasing.Normal, "Hallo, ÄÖÜ und Straße!")]
    [InlineData(OutputCasing.Lowercase, "hallo, äöü und straße!")]
    [InlineData(OutputCasing.Uppercase, "HALLO, ÄÖÜ UND STRASSE!")]
    public void CasingIsUnicodeAndMutuallyExclusive(OutputCasing mode, string expected) => Assert.Equal(expected, OutputPipeline.Format("Hallo, ÄÖÜ und Straße!", mode));
    [Fact]
    public void DictionaryCorrectionsRespectWordBoundaries()
    {
        Assert.Equal("Mara und Maraberg spielen GeckoLib.", OutputPipeline.ApplyVocabulary("sielas und Maraberg spielen gecko lip.", [new("sielas", "Mara"), new("gecko lip", "GeckoLib")]));
    }
    [Fact] public void BalancedUsesSmallModelAndAdvancedChoiceIsExplicit() { var settings = new AppSettings(); Assert.Equal("small", settings.EffectiveSpeechModel); settings.SpeechModel = "tiny"; Assert.Equal("small", settings.EffectiveSpeechModel); settings.UseCustomSpeechModel = true; Assert.Equal("tiny", settings.EffectiveSpeechModel); }
    [Fact] public void TranslationInstructionsDoNotRequestRewriting() { var instruction = OutputPipeline.EditingInstruction(new(), "Use formal language", new("Hallo", SpokenAction.Translate, "Chinese")); Assert.Contains("Chinese", instruction); Assert.DoesNotContain("Use formal language", instruction); }
    [Fact] public void TranslationProtectsPersonalWordsAndDecimalValues()
    {
        var protectedText=ProtectedTranslation.Create("Mara bezahlt 149,95 Euro um 11 Uhr.","English",["Mara"]);
        Assert.DoesNotContain("Mara",protectedText.Text,StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("149,95",protectedText.Text);
        var restored=protectedText.Restore(string.Join(" ",protectedText.Tokens.Keys));
        Assert.Contains("Mara",restored);Assert.Contains("149,95",restored);Assert.Contains("11 o’clock",restored);
    }
    [Fact] public void RewriteCatalogHasEveryRequestedStyle() { foreach (var id in new[] { "grammar", "professional", "serious", "friendly", "casual", "funny", "romantic", "shorten", "expand", "simple", "direct", "polite", "prompt" }) Assert.Contains(RewriteActions.All, x => x.Id == id); }
}
