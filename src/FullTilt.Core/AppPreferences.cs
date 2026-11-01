using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace FullTilt.Core;

public sealed record AppPreferences
{
    public int Version { get; init; } = 1;
    public string Theme { get; init; } = "Dark";
    public bool Maximized { get; init; } = true;
    public bool AutomaticallyCheckForUpdates { get; init; } = true;
    public string? AcknowledgedRelease { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Additional { get; init; }
}

// App-wide portable preferences are separate from controller profile snapshots.
public sealed class AppPreferencesStore(string directory)
{
    public string FilePath { get; } = Path.Combine(directory, "app-preferences.json");
    public string? Warning { get; private set; }
    private bool _blocked;
    public AppPreferences Load()
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            var bytes = File.ReadAllBytes(FilePath);
            var root = JsonNode.Parse(bytes) as JsonObject ?? throw new JsonException("Invalid preferences.");
            // Recover only malformed new optional fields, retaining unrelated valid preferences.
            var repaired = false;
            if (root.TryGetPropertyValue("AutomaticallyCheckForUpdates", out var automatic) && (automatic is not JsonValue automaticValue || !automaticValue.TryGetValue<bool>(out _)))
            { root.Remove("AutomaticallyCheckForUpdates"); repaired = true; }
            if (root.TryGetPropertyValue("AcknowledgedRelease", out var acknowledged) && acknowledged is not null && (acknowledged is not JsonValue acknowledgedValue || !acknowledgedValue.TryGetValue<string>(out _)))
            { root.Remove("AcknowledgedRelease"); repaired = true; }
            if (repaired)
            {
                try { File.Copy(FilePath, FilePath + ".recovered-" + Guid.NewGuid().ToString("N") + ".bak", false); Warning = "Invalid update preferences were backed up and reset. Other app preferences were retained."; }
                catch (Exception backup) when (backup is IOException or UnauthorizedAccessException) { _blocked = true; Warning = "Could not preserve app preferences; saving is disabled. " + backup.Message; }
            }
            var result = root.Deserialize<AppPreferences>() ?? throw new JsonException("Empty preferences.");
            if (result.Version != 1) { _blocked = true; Warning = "Preferences were created by another version; saving is disabled."; return new(); }
            return result with { Theme = result.Theme is "Light" or "Dark" or "System default" ? result.Theme : "Dark" };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            try { File.Copy(FilePath, FilePath + ".invalid-" + Guid.NewGuid().ToString("N") + ".bak", false); Warning = "Unreadable app preferences were backed up. Dark theme is in use."; }
            catch (Exception backup) when (backup is IOException or UnauthorizedAccessException) { _blocked = true; Warning = "Could not preserve app preferences; saving is disabled. " + backup.Message; }
            return new();
        }
    }
    public string? Save(AppPreferences preferences)
    {
        if (_blocked) return Warning;
        var temporary = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(preferences, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak", true);
            else File.Move(temporary, FilePath);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "Could not save app preferences. " + ex.Message; }
    }
}

public interface IStartupRegistration
{
    string? Read();
    void Write(string? command);
}
public static class StartupCommand
{
    public static string ForExecutable(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Contains('"')) throw new ArgumentException("A full executable path is required.", nameof(path));
        return "\"" + path + "\" --startup";
    }
}
