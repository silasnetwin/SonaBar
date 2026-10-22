using Voxora.Core;
using Xunit;

namespace Voxora.Tests;

/// <summary>Covers the spoken formatting commands and the rules that keep ordinary speech intact.</summary>
public class SpokenCommandTests
{
    [Theory]
    [InlineData("Der Termin ist morgen. Alles lowercase.", SpokenAction.Lowercase, "Der Termin ist morgen")]
    [InlineData("Der Termin ist morgen. Alles in Großbuchstaben.", SpokenAction.Uppercase, "Der Termin ist morgen")]
    [InlineData("Hallo Anna. Mach das in Kleinbuchstaben.", SpokenAction.Lowercase, "Hallo Anna")]
    [InlineData("Hallo Anna. Schreib das bitte in Großbuchstaben.", SpokenAction.Uppercase, "Hallo Anna")]
    [InlineData("Hallo Anna. Mach daraus Stichpunkte.", SpokenAction.Bullets, "Hallo Anna")]
    [InlineData("Hallo Anna. Mach das bitte zu Stichpunkten.", SpokenAction.Bullets, "Hallo Anna")]
    [InlineData("Hallo Anna. Mach daraus eine professionelle E-Mail.", SpokenAction.ProfessionalEmail, "Hallo Anna")]
    [InlineData("Hallo Anna. Formuliere das freundlicher.", SpokenAction.Friendly, "Hallo Anna")]
    [InlineData("Hallo Anna. Korrigiere nur die Rechtschreibung.", SpokenAction.SpellingOnly, "Hallo Anna")]
    [InlineData("Hallo Anna. Entferne die Füllwörter.", SpokenAction.RemoveFillers, "Hallo Anna")]
    [InlineData("Hallo Anna. Fasse das kurz zusammen.", SpokenAction.Summarize, "Hallo Anna")]
    public void TrailingFormattingCommandsAreSeparatedFromTheDictation(string text, SpokenAction action, string content)
    {
        var request = SpokenInstructions.Parse(text);
        Assert.Equal(action, request.Action);
        Assert.Equal(content, request.Content);
    }

    [Theory]
    [InlineData("Ich schreibe das in Großbuchstaben, weil es eine Überschrift ist.")]
    [InlineData("Wir sprechen über Stichpunkte und Großbuchstaben.")]
    [InlineData("Die Füllwörter waren viele.")]
    [InlineData("Ich möchte alles lowercase schreiben.")]
    [InlineData("Bitte korrigiere die Rechtschreibung im Bericht.")]
    [InlineData("Er meinte, wir sollten das zusammenfassen.")]
    [InlineData("Meine E-Mail-Adresse ist mara@example.com.")]
    [InlineData("Das ist ein professionelles Layout.")]
    public void OrdinaryTextIsNeverTurnedIntoACommand(string text)
    {
        var request = SpokenInstructions.Parse(text);
        Assert.Equal(SpokenAction.None, request.Action);
        Assert.Equal(text, request.Content);
    }

    [Fact]
    public void CasingCommandsAreServedWithoutTheWritingModel()
    {
        Assert.True(SpokenActions.IsDeterministic(SpokenAction.Lowercase));
        Assert.True(SpokenActions.IsDeterministic(SpokenAction.Uppercase));
        Assert.False(SpokenActions.IsDeterministic(SpokenAction.Translate));
        Assert.False(SpokenActions.IsDeterministic(SpokenAction.Bullets));
        Assert.True(SpokenActions.HasDeterministicFallback(SpokenAction.RemoveFillers));
    }

    [Fact]
    public void CasingCommandsProduceTheRequestedCase()
    {
        var lower = SpokenInstructions.Parse("Hallo Anna. Alles lowercase.");
        var upper = SpokenInstructions.Parse("Hallo Anna. Alles in Großbuchstaben.");
        Assert.Equal("hallo anna", OutputPipeline.Format(lower.Content, OutputCasing.Lowercase));
        Assert.Equal("HALLO ANNA", OutputPipeline.Format(upper.Content, OutputCasing.Uppercase));
    }

    [Fact]
    public void EveryFormattingCommandHasAnInstruction()
    {
        foreach (var action in new[] { SpokenAction.Bullets, SpokenAction.ProfessionalEmail, SpokenAction.Friendly, SpokenAction.SpellingOnly, SpokenAction.Summarize, SpokenAction.RemoveFillers })
        {
            var instruction = SpokenActions.Instruction(action);
            Assert.False(string.IsNullOrWhiteSpace(instruction));
            Assert.Contains("Preserve", instruction, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ExamplesAreShownForEverySupportedCommand()
    {
        Assert.True(SpokenActions.Examples.Count >= 9);
        Assert.All(SpokenActions.Examples, example => Assert.False(string.IsNullOrWhiteSpace(example.Spoken)));
    }

    [Fact]
    public void ASummaryMayLeaveDetailsOutButNeverAddsThem()
    {
        Assert.True(OutputPipeline.AcceptsSpokenEdit(SpokenAction.Summarize, "Morgen um 11 Uhr treffen wir uns.", "Wir treffen uns morgen."));
        Assert.False(OutputPipeline.AcceptsSpokenEdit(SpokenAction.Summarize, "Morgen um 11 Uhr treffen wir uns.", "Wir treffen uns morgen um 15 Uhr."));
        Assert.False(OutputPipeline.AcceptsSpokenEdit(SpokenAction.Summarize, "Ich komme nicht.", "Ich komme."));
    }

    [Fact]
    public void OtherEditsStillHaveToKeepEveryProtectedDetail()
    {
        Assert.False(OutputPipeline.AcceptsSpokenEdit(SpokenAction.Friendly, "Ich bin 16 und gehe in die 11. Klasse.", "Ich bin 17 und gehe in die 11. Klasse."));
        Assert.True(OutputPipeline.AcceptsSpokenEdit(SpokenAction.Friendly, "Ich bin 16 und gehe in die 11. Klasse.", "Ich bin 16 und gehe in die 11. Klasse, das macht Spaß."));
    }
}
