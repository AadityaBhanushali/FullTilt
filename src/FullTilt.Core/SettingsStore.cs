using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace FullTilt.Core;

public sealed record SettingsLoadResult(AppSettings Settings, string? Warning);

public sealed class SettingsStore(string directory)
{
    private readonly object _gate = new();
    private bool _preservationFailed;
    private bool _futureVersion;
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new ActionIdJsonConverter(), new JsonStringEnumConverter() }
    };

    public static (SettingsStore Store, string? Warning) OpenDefault(string executableDirectory, string localAppData)
    {
        var store = new SettingsStore(Path.Combine(executableDirectory, "Profiles"));
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(store.DirectoryPath);
            // A portable library always wins. Never fall back to another write location.
            if (Exists(store.FilePath)) return (store, null);
            var adjacent = Path.Combine(executableDirectory, "settings.json");
            var legacy = Exists(adjacent) ? adjacent : Path.Combine(localAppData, "FullTilt", "settings.json");
            if (Exists(legacy))
            {
                // Copy exact bytes once; leave legacy files untouched, including on failure.
                temporary = Path.Combine(store.DirectoryPath, $".import-{Guid.NewGuid():N}.tmp");
                using (var source = new FileStream(legacy, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    source.CopyTo(target);
                    target.Flush(true);
                }
                File.Move(temporary, store.FilePath, false);
            }
            return (store, null);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            store._preservationFailed = true;
            return (store, $"Portable Profiles folder could not be initialized. Saving is disabled; existing settings are preserved. {ex.Message}");
        }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); } catch (Exception ex) when (IsFileError(ex)) { }
        }
    }

    private static bool Exists(string path)
    {
        // File.Exists hides access errors, which must not be treated as a fresh installation.
        try { _ = File.GetAttributes(path); return true; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return false; }
    }

    public SettingsLoadResult Load()
    {
        lock (_gate)
        {
            try
            {
                // Reading directly distinguishes a missing file from denied access.
                var json = File.ReadAllText(FilePath);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Settings must be an object.");
                var version = 1;
                var hasTapDistance = false;
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Name.Equals("Version", StringComparison.OrdinalIgnoreCase))
                    {
                        if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out version)) throw new JsonException("Invalid settings version.");
                    }
                    if (property.Name.Equals("DPadTapDistance", StringComparison.OrdinalIgnoreCase)) hasTapDistance = true;
                }
                // Recognize future versions before parsing their potentially unknown action IDs.
                if (version > AppSettings.CurrentVersion)
                {
                    _futureVersion = true;
                    return new(new(), "Settings are from a newer FullTilt version. Defaults loaded; the existing file will not be overwritten.");
                }
                if (version < 1) throw new JsonException("Invalid settings version.");
                var settings = ReadConfiguration(document.RootElement, version);
                var (profileSettings, recoveredProfiles) = ReadProfiles(document.RootElement, settings);
                recoveredProfiles |= InvalidOptionalData(document.RootElement) || (Field(document.RootElement, "Profiles").ValueKind == JsonValueKind.Array &&
                    Field(document.RootElement, "Profiles").EnumerateArray().Any(p => InvalidOptionalData(Field(p, "Configuration"))));
                settings = profileSettings;
                if (!hasTapDistance) settings = settings with { DPadTapDistance = 1 };
                if (version < AppSettings.CurrentVersion)
                {
                    var recovery = FilePath + $".v{version}-backup";
                    try
                    {
                        // A prior migration backup can belong to an older revision of this file.
                        if (File.Exists(recovery) && !File.ReadAllBytes(recovery).AsSpan().SequenceEqual(File.ReadAllBytes(FilePath)))
                            recovery += "-" + Guid.NewGuid().ToString("N");
                        if (!File.Exists(recovery)) File.Copy(FilePath, recovery, false);
                    }
                    catch (Exception ex) when (IsFileError(ex))
                    {
                        _preservationFailed = true;
                        return new(settings.Validated(), "Existing settings loaded, but migration backup could not be saved. Saving is disabled to preserve the original.");
                    }
                }
                string? warning = null;
                if (recoveredProfiles)
                {
                    var recovery = FilePath + ".profiles-backup-" + Guid.NewGuid().ToString("N");
                    try { File.Copy(FilePath, recovery, false); warning = "Some optional settings or profile data was invalid. Valid settings retained; original preserved at " + recovery; }
                    catch (Exception ex) when (IsFileError(ex)) { _preservationFailed = true; warning = "Invalid profile data could not be backed up. Saving is disabled to preserve the original."; }
                }
                return new(ProfileLibrary.Initialize(PiePresets.Initialize(settings.Validated())), warning);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return new(ProfileLibrary.Initialize(AppSettings.FreshInstallation()), null);
            }
            catch (JsonException)
            {
                var recovery = FilePath + $".corrupt-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
                try
                {
                    File.Copy(FilePath, recovery, false);
                    return new(new(), $"Unreadable settings preserved at {recovery}. Defaults loaded.");
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    _preservationFailed = true;
                    return new(new(), "Settings are malformed and could not be backed up. Defaults loaded; saving is disabled to preserve the original.");
                }
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                _preservationFailed = true;
                return new(new(), $"Settings could not be read. Defaults loaded; saving is disabled to preserve the original. {ex.Message}");
            }
        }
    }

    public string? Save(AppSettings settings)
    {
        lock (_gate)
        {
            if (_preservationFailed || _futureVersion) return "Settings saving is disabled to preserve the existing file. Changes apply for this session.";
            var temporary = FilePath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    JsonSerializer.Serialize(stream, ProfileLibrary.Checkpoint(settings), JsonOptions);
                    stream.Flush(true);
                }
                // Same-directory replacement is atomic; readers see the old file or the complete new file.
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath);
                return null;
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                return $"Settings could not be saved. Changes apply for this session. {ex.Message}";
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception ex) when (IsFileError(ex)) { /* Do not mask the save result. */ }
            }
        }
    }

    private static JsonElement Field(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object
        ? root.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value : default;
    private static AppSettings ReadConfiguration(JsonElement source, int version)
    {
        var root = JsonNode.Parse(source.GetRawText())!.AsObject();
        foreach (var key in root.Select(p => p.Key).Where(k => new[] { "Profiles", "ActiveProfileId", "PauseShortcut", "PieMenus", "PieBindings", "PiePresetMigration", "ScrollBoostMultiplier", "ReverseScroll", "PieMenuSizePercent", "RecordedBindings", "PrecisionCursorSpeed", "BoostedCursorSpeed" }.Contains(k, StringComparer.OrdinalIgnoreCase)).ToArray()) root.Remove(key);
        var settings = JsonSerializer.Deserialize<AppSettings>(root.ToJsonString(), JsonOptions) ?? throw new JsonException("Empty configuration.");
        settings = settings with { Version = version };
        var boost = Field(source, "ScrollBoostMultiplier"); var reverse = Field(source, "ReverseScroll");
        if (boost.ValueKind == JsonValueKind.Number && boost.TryGetDouble(out var multiplier) && double.IsFinite(multiplier)) settings = settings with { ScrollBoostMultiplier = multiplier };
        if (reverse.ValueKind is JsonValueKind.True or JsonValueKind.False) settings = settings with { ReverseScroll = reverse.GetBoolean() };
        var shortcut = Field(source, "PauseShortcut");
        if (shortcut.ValueKind != JsonValueKind.Undefined)
            try { settings = settings with { PauseShortcut = shortcut.Deserialize<ControllerButtons>(JsonOptions) }; }
            catch (JsonException) { settings = settings with { PauseShortcut = PauseShortcutRules.Default }; }
        settings = ReadPies(source, settings, version);
        settings = settings with { RecordedBindings = ReadRecords(Field(source, "RecordedBindings"), ControlKey) };
        var size = Field(source, "PieMenuSizePercent");
        if (size.ValueKind == JsonValueKind.Number && size.TryGetDouble(out var percent) && double.IsFinite(percent)) settings = settings with { PieMenuSizePercent = PieMenu.ValidSize(percent) };
        else if (version < 14) settings = settings with { PieMenuSizePercent = settings.PieMenus.FirstOrDefault(m => m.SizePercent != 100)?.SizePercent ?? 100 };
        var precision = Field(source, "PrecisionCursorSpeed"); var cursorBoost = Field(source, "BoostedCursorSpeed");
        if (version >= 17 && precision.ValueKind == JsonValueKind.Number && precision.TryGetDouble(out var absolutePrecision) && double.IsFinite(absolutePrecision)) settings = settings with { PrecisionCursorSpeed = absolutePrecision };
        if (version >= 17 && cursorBoost.ValueKind == JsonValueKind.Number && cursorBoost.TryGetDouble(out var absoluteBoost) && double.IsFinite(absoluteBoost)) settings = settings with { BoostedCursorSpeed = absoluteBoost };
        return settings;
    }
    private static bool InvalidOptionalData(JsonElement source)
    {
        var boost = Field(source, "ScrollBoostMultiplier"); var reverse = Field(source, "ReverseScroll");
        var menus = Field(source, "PieMenus");
        var globalSize = Field(source, "PieMenuSizePercent");
        var invalidGlobalSize = globalSize.ValueKind != JsonValueKind.Undefined && (globalSize.ValueKind != JsonValueKind.Number || !globalSize.TryGetDouble(out var globalPercent) || !double.IsFinite(globalPercent) || globalPercent is < 100 or > 200);
        var invalidSize = menus.ValueKind == JsonValueKind.Array && menus.EnumerateArray().Any(menu =>
        {
            var size = Field(menu, "SizePercent");
            var segments = Field(menu, "SegmentCount");
            var count = segments.ValueKind == JsonValueKind.Number && segments.TryGetInt32(out var segmentCount) ? PieGeometry.Count(segmentCount) : 8;
            var dpad = Field(menu, "DPadActions");
            var invalidDpad = dpad.ValueKind != JsonValueKind.Undefined && (dpad.ValueKind != JsonValueKind.Array || dpad.GetArrayLength() != 4 || dpad.EnumerateArray().Any(entry => entry.ValueKind != JsonValueKind.Null && !TryAction(entry, out _)));
            return invalidDpad || InvalidRecords(Field(menu, "RecordedSlots"), key => int.TryParse(key, out var i) && i >= 0 && i < count) ||
                InvalidRecords(Field(menu, "RecordedDPad"), key => int.TryParse(key, out var i) && i is >= 0 and < 4) ||
                (size.ValueKind != JsonValueKind.Undefined && (size.ValueKind != JsonValueKind.Number || !size.TryGetDouble(out var percent) || !double.IsFinite(percent) || percent is < 100 or > 200));
        });
        var invalidCursor = new[] { "PrecisionCursorSpeed", "BoostedCursorSpeed" }.Any(name =>
        {
            var value = Field(source, name);
            return value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) &&
                (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var speed) || !double.IsFinite(speed) || speed < 1 || speed > AppSettings.MaximumCursorSpeed);
        });
        return invalidCursor || InvalidRecords(Field(source, "RecordedBindings"), key => ControlKey(key) is not null) || invalidGlobalSize || invalidSize || (boost.ValueKind != JsonValueKind.Undefined && (boost.ValueKind != JsonValueKind.Number || !boost.TryGetDouble(out var value) || !double.IsFinite(value))) ||
            (reverse.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.True or JsonValueKind.False));
    }
    private static (AppSettings Settings, bool Recovered) ReadProfiles(JsonElement root, AppSettings settings)
    {
        var field = Field(root, "Profiles");
        if (field.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return (settings, false);
        var profiles = new List<FullTiltProfile>(); var recovered = field.ValueKind != JsonValueKind.Array;
        var ids = new HashSet<Guid>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (field.ValueKind == JsonValueKind.Array)
            foreach (var item in field.EnumerateArray())
            {
                var id = Field(item, "Id"); var name = Field(item, "Name"); var configuration = Field(item, "Configuration");
                if (id.ValueKind != JsonValueKind.String || !Guid.TryParse(id.GetString(), out var guid) || guid == Guid.Empty ||
                    name.ValueKind != JsonValueKind.String || configuration.ValueKind != JsonValueKind.Object)
                { recovered = true; continue; }
                var clean = ProfileLibrary.CleanName(name.GetString());
                if (clean.Length is 0 or > 80 || ids.Contains(guid) || names.Contains(clean)) { recovered = true; continue; }
                try
                {
                    var versionField = Field(configuration, "Version");
                    var version = versionField.ValueKind == JsonValueKind.Number && versionField.TryGetInt32(out var parsed) ? parsed : AppSettings.CurrentVersion;
                    if (version > AppSettings.CurrentVersion || version < 1) { recovered = true; continue; }
                    var snapshot = ReadConfiguration(configuration, version);
                    profiles.Add(new(guid, clean, ProfileLibrary.Snapshot(snapshot))); ids.Add(guid); names.Add(clean);
                }
                catch (JsonException) { recovered = true; }
            }
        var active = Field(root, "ActiveProfileId");
        Guid? selected = active.ValueKind == JsonValueKind.String && Guid.TryParse(active.GetString(), out var parsedId) ? parsedId : null;
        if (!profiles.Any(p => p.Id == selected)) recovered = true;
        return (settings with { Profiles = profiles, ActiveProfileId = selected }, recovered);
    }

    private static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;
    private static AppSettings ReadPies(JsonElement root, AppSettings settings, int version)
    {
        static JsonElement Field(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value : default;
        var menus = new List<PieMenu>();
        var collection = Field(root, "PieMenus");
        if (collection.ValueKind == JsonValueKind.Array)
            foreach (var item in collection.EnumerateArray())
            {
                var id = Field(item, "Id");
                if (id.ValueKind != JsonValueKind.String || !Guid.TryParse(id.GetString(), out var guid)) continue;
                var name = Field(item, "Name");
                var countField = Field(item, "SegmentCount");
                var count = version >= 5 && countField.ValueKind == JsonValueKind.Number && countField.TryGetInt32(out var parsedCount) ? PieGeometry.Count(parsedCount) : 8;
                var slots = new InputAction[count]; var list = Field(item, "Slots");
                if (list.ValueKind == JsonValueKind.Array)
                {
                    var i = 0;
                    foreach (var entry in list.EnumerateArray())
                    {
                        if (i == count) break;
                        InputAction action = InputAction.None;
                        if (entry.ValueKind == JsonValueKind.String && ActionCatalog.TryFromId(entry.GetString()!, out var definition)) action = definition!.Action;
                        else if (entry.ValueKind == JsonValueKind.Number && entry.TryGetInt32(out var number)) action = (InputAction)number;
                        slots[i++] = ActionCatalog.SupportsPie(action) ? action : InputAction.None;
                    }
                }
                var preset = Field(item, "PresetId");
                var sizeField = Field(item, "SizePercent");
                var size = sizeField.ValueKind == JsonValueKind.Number && sizeField.TryGetDouble(out var percent) ? PieMenu.ValidSize(percent) : 100;
                var dpad = new InputAction?[4]; var directions = Field(item, "DPadActions");
                if (directions.ValueKind == JsonValueKind.Array)
                {
                    var index = 0;
                    foreach (var entry in directions.EnumerateArray())
                    { if (index == 4) break; if (TryAction(entry, out var action)) dpad[index] = action; index++; }
                }
                menus.Add(new(guid, name.ValueKind == JsonValueKind.String ? name.GetString()! : "Unnamed menu", slots) { SegmentCount = count, PresetId = version >= 5 && preset.ValueKind == JsonValueKind.String ? PiePresets.Find(preset.GetString())?.Id : null, SizePercent = size, DPadActions = dpad,
                    RecordedSlots = ReadRecords(Field(item, "RecordedSlots"), key => int.TryParse(key, out var i) && i >= 0 && i < count ? i : (int?)null),
                    RecordedDPad = ReadRecords(Field(item, "RecordedDPad"), key => int.TryParse(key, out var i) && i is >= 0 and < 4 ? i : (int?)null) });
            }
        var bindings = new Dictionary<Control, Guid>(); var map = Field(root, "PieBindings");
        if (map.ValueKind == JsonValueKind.Object)
            foreach (var pair in map.EnumerateObject())
                if (Enum.TryParse<Control>(pair.Name, true, out var control) && Enum.IsDefined(control))
                    bindings[control] = pair.Value.ValueKind == JsonValueKind.String && Guid.TryParse(pair.Value.GetString(), out var guid) ? guid : Guid.Empty;
        var marker = Field(root, "PiePresetMigration");
        var migration = version >= 5 && marker.ValueKind == JsonValueKind.Number && marker.TryGetInt32(out var value) ? Math.Max(0, value) : 0;
        return settings with { PieMenus = menus, PieBindings = bindings, PiePresetMigration = migration };
    }
    private static bool TryAction(JsonElement entry, out InputAction action)
    {
        action = InputAction.None;
        if (entry.ValueKind == JsonValueKind.String && ActionCatalog.TryFromId(entry.GetString()!, out var definition)) { action = definition!.Action; return true; }
        if (entry.ValueKind == JsonValueKind.Number && entry.TryGetInt32(out var number) && ActionCatalog.Contains((InputAction)number)) { action = (InputAction)number; return true; }
        return false;
    }
    private static Control? ControlKey(string text) => Enum.TryParse<Control>(text, true, out var control) && Enum.IsDefined(control) ? control : null;
    private static KeyboardChord? ReadChord(JsonElement value)
    {
        try { return value.Deserialize<KeyboardChord>(JsonOptions)?.Validated(); }
        catch (JsonException) { return null; }
    }
    private static Dictionary<T, KeyboardChord> ReadRecords<T>(JsonElement value, Func<string, T?> key) where T : struct
    {
        var result = new Dictionary<T, KeyboardChord>();
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var entry in value.EnumerateObject())
                if (key(entry.Name) is { } id && ReadChord(entry.Value) is { } chord) result[id] = chord;
        return result;
    }
    private static bool InvalidRecords(JsonElement value, Func<string, bool> validKey) => value.ValueKind != JsonValueKind.Undefined &&
        (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any(p => !validKey(p.Name) || ReadChord(p.Value) is null));
}
