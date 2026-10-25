using SonaBar.Core;
using SonaBar.Storage;
using Xunit;
namespace SonaBar.Tests;
public sealed class StorageTests : IDisposable
{
    private readonly string directory=Path.Combine(Path.GetTempPath(),"SonaBar-tests-"+Guid.NewGuid());
    [Theory][InlineData("{\"Language\":null}")][InlineData("{\"PlainHotkey\":null}")][InlineData("{\"SchemaVersion\":\"wrong\"}")][InlineData("[]")]
    public void StructurallyInvalidSettingsRecoverWithoutCrashing(string json){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"settings.json"),json);var store=new SettingsStore(directory);Assert.Equal(SpeechQuality.Balanced,store.Load().Quality);Assert.NotNull(store.RecoveryMessage);}
    [Fact] public void SettingsRoundTrip(){var store=new SettingsStore(directory);var settings=new AppSettings{Language="de",StoreHistory=false};store.Save(settings);var actual=store.Load();Assert.Equal("de",actual.Language);Assert.False(actual.StoreHistory);}
    [Fact] public void OldSettingsMigrateWithoutLosingUserData(){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"settings.json"),"{\"Language\":\"de\",\"SpeechModel\":\"base\",\"CustomWords\":[\"Mara\"],\"StoreHistory\":false}");var settings=new SettingsStore(directory).Load();Assert.Equal("small",settings.EffectiveSpeechModel);Assert.Equal("de",settings.Language);Assert.Equal("Mara",Assert.Single(settings.CustomWords));Assert.False(settings.StoreHistory);Assert.Equal("Off",settings.Sounds);}
    [Fact] public void V2MigrationRemovesLegacySecondShortcutWithoutChangingTheUsersDictationShortcut(){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"settings.json"),"{\"SchemaVersion\":2,\"PlainHotkey\":{\"Modifiers\":2,\"Key\":65},\"EnhancedHotkey\":{\"Modifiers\":6,\"Key\":32}}");var settings=new SettingsStore(directory).Load();Assert.Equal(new Hotkey(2,65),settings.PlainHotkey);Assert.Equal(4,settings.SchemaVersion);}
    [Fact] public void ExistingInstallationsKeepTheirChosenProfileInsteadOfAGuess()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"settings.json"),"{\"SchemaVersion\":3,\"Quality\":\"Maximum\",\"AutoQuality\":false}");
        var settings=new SettingsStore(directory).Load();
        Assert.False(settings.AutoQuality);
        Assert.Equal(SpeechQuality.Maximum,settings.Quality);
    }
    [Fact] public void NewInstallationsStartWithTheAutomaticProfile()
    {
        Directory.CreateDirectory(directory);
        var settings=new SettingsStore(directory).Load();
        Assert.True(settings.AutoQuality);
    }
    [Fact] public void AnOlderSettingsFileWithoutTheAutomaticFlagKeepsTheExplicitProfile()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"settings.json"),"{\"SchemaVersion\":3,\"Quality\":\"Fast\"}");
        var settings=new SettingsStore(directory).Load();
        Assert.False(settings.AutoQuality);
        Assert.Equal(SpeechQuality.Fast,settings.Quality);
    }
    [Fact] public void AHandEditedSettingsFileKeepsEveryValueInsteadOfBeingReset()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"settings.json"),"{\"SchemaVersion\":4,\"Language\":\"de\",\"Quality\":\"Maximum\",\"OutputCasing\":\"Uppercase\",\"Clipboard\":\"Replace\"}");
        var store=new SettingsStore(directory);var settings=store.Load();
        Assert.Null(store.RecoveryMessage);
        Assert.Equal("de",settings.Language);
        Assert.Equal(SpeechQuality.Maximum,settings.Quality);
        Assert.Equal(OutputCasing.Uppercase,settings.OutputCasing);
        Assert.Equal(ClipboardMode.Replace,settings.Clipboard);
    }
    [Fact] public void EnumValuesWrittenAsNumbersAreStillUnderstood()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"settings.json"),"{\"SchemaVersion\":3,\"Quality\":0,\"OutputCasing\":2}");
        var store=new SettingsStore(directory);var settings=store.Load();
        Assert.Null(store.RecoveryMessage);
        Assert.Equal(SpeechQuality.Fast,settings.Quality);
        Assert.Equal(OutputCasing.Uppercase,settings.OutputCasing);
    }
    [Fact] public void CorruptSettingsArePreserved(){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"settings.json"),"broken");var store=new SettingsStore(directory);Assert.Equal("auto",store.Load().Language);Assert.NotNull(store.RecoveryMessage);Assert.Single(Directory.GetFiles(directory,"*.recovered-*"));}
    [Fact] public void PrivacyModeStoresOnlyStatistics(){var db=new HistoryRepository(directory);db.Add(Entry("sensitive transcript"),false);Assert.Empty(db.Search());Assert.Equal(1,db.Statistics().Single().Dictations);}
    [Fact] public void HistoryDeletionPreservesLifetimeStatistics(){var db=new HistoryRepository(directory);db.Add(Entry("hello there"),true);Assert.Single(db.Search("hello"));db.ClearHistory();Assert.Empty(db.Search());Assert.Equal(2,db.Statistics().Single().Words);db.ResetStatistics();Assert.Empty(db.Statistics());}
    [Fact] public void SearchEscapesSqlWildcards(){var db=new HistoryRepository(directory);db.Add(Entry("100% complete"),true);db.Add(Entry("1000 complete"),true);Assert.Single(db.Search("100%"));}
    [Fact]
    public void RetentionPrunesOnlyExpiredTranscriptText()
    {
        var db = new HistoryRepository(directory);
        db.Add(Entry("old") with { Timestamp = DateTimeOffset.UtcNow.AddDays(-31) }, true);
        db.Add(Entry("new"), true);
        Assert.Equal(1, db.Prune(30));
        Assert.Single(db.Search());
        Assert.Equal(2, db.Statistics().Single().Dictations);
    }
    private static HistoryEntry Entry(string text)=>new(0,DateTimeOffset.UtcNow,text,"",3,"en","Notepad.exe","base");
    public void Dispose(){Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(directory))Directory.Delete(directory,true);}
}
