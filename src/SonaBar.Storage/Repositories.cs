using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text.Json.Serialization;
using SonaBar.Core;

namespace SonaBar.Storage;

public sealed class SettingsStore
{
    private readonly string path;
    public string? RecoveryMessage { get; private set; }
    /// <summary>
    /// Enum values are written as readable names. Reading still accepts the plain numbers an
    /// earlier version wrote, so an edited settings file never loses the whole configuration.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public SettingsStore(string? directory = null)
    {
        Directory.CreateDirectory(directory ?? Brand.DataRoot);
        path = Path.Combine(directory ?? Brand.DataRoot, "settings.json");
    }
    public AppSettings Load()
    {
        if (!File.Exists(path)) return new();
        try
        {
            var json=File.ReadAllText(path);var value=JsonSerializer.Deserialize<AppSettings>(json,SerializerOptions)??new();
            using var document=JsonDocument.Parse(json);
            if(!document.RootElement.TryGetProperty("HistoryRetentionDays",out _))value.HistoryRetentionDays=0;
            // Older settings files predate the tray-first default. Preserve an
            // explicit choice, but give profiles without this setting the new default.
            if(!document.RootElement.TryGetProperty("LaunchAtLogin",out _))value.LaunchAtLogin=true;
            if(!document.RootElement.TryGetProperty("SchemaVersion",out var version)||version.GetInt32()<2)
            {
                value.Quality=SpeechQuality.Balanced;value.UseCustomSpeechModel=false;value.Sounds="Off";value.KeepSpeechLoaded=true;value.KeepEnhancementLoaded=true;
            }
            // Version 3 removes the second dictation shortcut. PlainHotkey is deliberately
            // preserved so existing users retain their custom shortcut.
            if(!document.RootElement.TryGetProperty("SchemaVersion",out version)||version.GetInt32()<3)value.SchemaVersion=3;
            // Version 4 adds the automatic quality choice. An existing installation already
            // shows a deliberate profile, so that choice is kept instead of being replaced
            // by a hardware guess. New installations start with the automatic choice.
            if(!document.RootElement.TryGetProperty("AutoQuality",out _))value.AutoQuality=false;
            if(!document.RootElement.TryGetProperty("SchemaVersion",out version)||version.GetInt32()<4)value.SchemaVersion=4;
            value.Validate();return value;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or FormatException)
        {
            File.Move(path, path + ".recovered-" + DateTime.UtcNow.Ticks);
            RecoveryMessage = "Settings were damaged. Defaults have been restored and the old file has been preserved.";
            return new();
        }
    }
    public void Save(AppSettings settings)
    {
        settings.Validate();
        File.WriteAllText(path + ".new", JsonSerializer.Serialize(settings, SerializerOptions));
        File.Move(path + ".new", path, true);
    }
}
public sealed class HistoryRepository
{
    private readonly string connectionString;
    public HistoryRepository(string? directory = null)
    {
        Directory.CreateDirectory(directory ?? Brand.DataRoot);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory ?? Brand.DataRoot, "sonabar.db"), Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS history(id INTEGER PRIMARY KEY, stamp TEXT NOT NULL, original TEXT NOT NULL, enhanced TEXT NOT NULL, duration REAL NOT NULL, language TEXT NOT NULL, application TEXT NOT NULL, model TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS statistics(language TEXT NOT NULL, application TEXT NOT NULL, model TEXT NOT NULL, dictations INTEGER NOT NULL, duration REAL NOT NULL, words INTEGER NOT NULL, PRIMARY KEY(language,application,model));
            """;
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var db = new SqliteConnection(connectionString); db.Open(); return db; }
    public void Add(HistoryEntry entry, bool storeText,int retentionDays=0)
    {
        using var db = Open(); using var transaction = db.BeginTransaction();
        using var cmd = db.CreateCommand(); cmd.Transaction = transaction;
        cmd.Parameters.AddWithValue("$stamp", entry.Timestamp.ToUniversalTime().ToString("O")); cmd.Parameters.AddWithValue("$original", entry.Original); cmd.Parameters.AddWithValue("$enhanced", entry.Enhanced);
        cmd.Parameters.AddWithValue("$duration", entry.Duration); cmd.Parameters.AddWithValue("$language", entry.Language); cmd.Parameters.AddWithValue("$application", entry.Application); cmd.Parameters.AddWithValue("$model", entry.Model); cmd.Parameters.AddWithValue("$words", entry.WordCount);
        if (storeText)
        {
            cmd.CommandText = "INSERT INTO history(stamp,original,enhanced,duration,language,application,model) VALUES($stamp,$original,$enhanced,$duration,$language,$application,$model)"; cmd.ExecuteNonQuery();
        }
        cmd.CommandText = "INSERT INTO statistics VALUES($language,$application,$model,1,$duration,$words) ON CONFLICT(language,application,model) DO UPDATE SET dictations=dictations+1,duration=duration+$duration,words=words+$words";
        cmd.ExecuteNonQuery(); transaction.Commit();
        if(retentionDays>0)Prune(retentionDays);
    }
    public int Prune(int days,DateTimeOffset? now=null)
    {
        if(days<=0)return 0;
        using var db=Open();using var command=db.CreateCommand();
        command.CommandText="PRAGMA secure_delete=ON; DELETE FROM history WHERE stamp < $cutoff";
        command.Parameters.AddWithValue("$cutoff",(now??DateTimeOffset.UtcNow).AddDays(-days).ToUniversalTime().ToString("O"));
        return command.ExecuteNonQuery();
    }
    public IReadOnlyList<HistoryEntry> Search(string query = "", DateTimeOffset? after = null, DateTimeOffset? before = null, string language = "", string application = "")
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT * FROM history WHERE (original LIKE $q ESCAPE '~' OR enhanced LIKE $q ESCAPE '~') AND stamp >= $after AND stamp <= $before AND ($lang='' OR language=$lang) AND ($app='' OR application=$app) ORDER BY id DESC LIMIT 1000";
        cmd.Parameters.AddWithValue("$q", "%" + query.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_") + "%");
        cmd.Parameters.AddWithValue("$after", after?.ToUniversalTime().ToString("O") ?? ""); cmd.Parameters.AddWithValue("$before", before?.ToUniversalTime().ToString("O") ?? "9999"); cmd.Parameters.AddWithValue("$lang", language); cmd.Parameters.AddWithValue("$app", application);
        using var reader = cmd.ExecuteReader(); var result = new List<HistoryEntry>();
        while (reader.Read()) result.Add(new(reader.GetInt64(0), DateTimeOffset.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetDouble(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));
        return result;
    }
    public void Delete(long id) { using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "DELETE FROM history WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery(); }
    public void ClearHistory() { using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA secure_delete=ON; DELETE FROM history; PRAGMA wal_checkpoint(TRUNCATE); VACUUM;"; cmd.ExecuteNonQuery(); }
    public void ResetStatistics() { using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "DELETE FROM statistics"; cmd.ExecuteNonQuery(); }
    public IReadOnlyList<Statistic> Statistics()
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT language,application,model,dictations,duration,words FROM statistics"; using var r = cmd.ExecuteReader(); var result = new List<Statistic>();
        while (r.Read()) result.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetDouble(4), r.GetInt64(5))); return result;
    }
}
public sealed record Statistic(string Language, string Application, string Model, long Dictations, double Duration, long Words);
public static class SafeLog
{
    public static void WriteTiming(string values,bool delivered)
    {
        try { Directory.CreateDirectory(Path.Combine(Brand.DataRoot, "Logs")); File.AppendAllText(Path.Combine(Brand.DataRoot, "Logs", "timings.log"), $"{DateTime.UtcNow:O} delivered={delivered} {values}\n"); } catch (IOException) { }
    }
    public static void Write(string operation, Exception exception)
    {
        // Only allow structured identifiers and exception types: native exceptions can embed user content.
        try { Directory.CreateDirectory(Path.Combine(Brand.DataRoot, "Logs")); File.AppendAllText(Path.Combine(Brand.DataRoot, "Logs", "events.log"), $"{DateTime.UtcNow:O} {new string(operation.Where(char.IsLetterOrDigit).ToArray())} {exception.GetType().Name} 0x{exception.HResult:X8}\n"); } catch (IOException) { }
    }
    /// <summary>Writes diagnostic metadata only. Callers must never pass dictated or selected text.</summary>
    public static void WriteDiagnostic(string eventName, params (string Key, object? Value)[] fields)
    {
        try
        {
            var name = new string(eventName.Where(char.IsLetterOrDigit).ToArray());
            var values = string.Join(" ", fields.Select(field =>
            {
                var key = new string(field.Key.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
                var value = (field.Value?.ToString() ?? "null").Replace('\r', ' ').Replace('\n', ' ');
                value = new string(value.Where(c => !char.IsControl(c)).ToArray());
                return $"{key}={value[..Math.Min(value.Length, 240)]}";
            }));
            Directory.CreateDirectory(Path.Combine(Brand.DataRoot, "Logs"));
            File.AppendAllText(Path.Combine(Brand.DataRoot, "Logs", "diagnostics.log"), $"{DateTime.UtcNow:O} event={name} {values}\n");
        }
        catch (IOException) { }
    }
}
