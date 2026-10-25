using SonaBar.Core;
using Xunit;
namespace SonaBar.Tests;
public class CoreTests
{
    [Fact] public void NewProfilesUseTheSingleCtrlShiftSpaceDictationShortcut(){var settings=new AppSettings();Assert.Equal(new Hotkey(6,32),settings.PlainHotkey);}
    [Fact] public void NewProfilesStartWithWindowsByDefault(){Assert.True(new AppSettings().LaunchAtLogin);}
    [Theory]
    [InlineData("  Hello  world ! \r\n\n\nNext ","Hello world!\n\nNext")]
    [InlineData("[BLANK_AUDIO]","")]
    [InlineData("I said no, no, no.","I said no, no, no.")]
    [InlineData("Costs 1.234,56 €", "Costs 1.234,56 €")]
    public void CleanupPreservesMeaning(string input,string expected)=>Assert.Equal(expected,TextProcessing.Clean(input));
    [Fact] public void SnippetsPreserveWhitespaceAndExpandVariables()
    {
        var snippet=new Snippet("Signature","insert signature|my signature","Mara\n\tSilasNet\n{clipboard}");
        Assert.Equal("Mara\n\tSilasNet\nabc",TextProcessing.Expand("My signature.",[snippet],DateTime.Now,()=>"abc"));
        Assert.Equal("Please add my signature",TextProcessing.Expand("Please add my signature",[snippet],DateTime.Now,()=>throw new Exception()));
    }
    [Fact] public void InvalidTransitionIsRejected(){var state=new OperationMachine();Assert.Throws<InvalidOperationException>(()=>state.Move(OperationState.Inserting));Assert.Equal(OperationState.Idle,state.State);}
    [Fact] public void DictationCanBeCancelledAtEveryStage()
    {
        foreach(var stopAt in new[]{OperationState.Recording,OperationState.Transcribing,OperationState.Enhancing,OperationState.Inserting})
        {
            var state=new OperationMachine();foreach(var next in new[]{OperationState.Recording,OperationState.Transcribing,OperationState.Enhancing,OperationState.Inserting}){state.Move(next);if(next==stopAt)break;}state.Move(OperationState.Cancelled);state.Move(OperationState.Idle);Assert.Equal(OperationState.Idle,state.State);
        }
    }
    [Fact] public void ProfilesAreCaseInsensitiveAndNeverDefaultToEnter(){var settings=new AppSettings();Assert.False(settings.ProfileFor("cmd.exe").PressEnter);settings.Profiles.Add(new("Code.exe",InsertionStrategy.Unicode,false,"Keep code"));Assert.Equal(InsertionStrategy.Unicode,settings.ProfileFor("CODE.EXE").Strategy);}
    [Fact] public void DuplicateHotkeysAreRejected(){var settings=new AppSettings();settings.EditHotkey=settings.PlainHotkey;Assert.Throws<InvalidDataException>(settings.Validate);}
    [Fact] public void InvalidLanguageIsRejected(){var settings=new AppSettings{Language="not-a-language"};Assert.Throws<InvalidDataException>(settings.Validate);}
    [Theory][InlineData("auto")][InlineData("de")][InlineData("en")][InlineData("fr")][InlineData("es")][InlineData("it")][InlineData("pt")][InlineData("nl")][InlineData("pl")]
    public void RequiredLanguagesAvailable(string language)=>Assert.True(Languages.All.ContainsKey(language));
    [Fact] public void SubtitleTimestampsUseCorrectSeparators(){var s=new TranscriptSegment(TimeSpan.FromMilliseconds(1500),TimeSpan.FromSeconds(3),"Hello");Assert.Contains("00:00:01,500 --> 00:00:03,000",TextProcessing.Subtitle([s],false));Assert.StartsWith("WEBVTT\n\n",TextProcessing.Subtitle([s],true));Assert.Contains("00:00:01.500",TextProcessing.Subtitle([s],true));}
}
