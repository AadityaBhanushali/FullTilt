using System.Text.Json.Serialization;

namespace FullTilt.Core;

public readonly record struct KeyboardKey(ushort VirtualKey, ushort ScanCode = 0, bool Extended = false, string? Name = null)
{
    [JsonIgnore] public int Modifier => VirtualKey switch { 0x11 or 0xA2 or 0xA3 => 1, 0x10 or 0xA0 or 0xA1 => 2, 0x12 or 0xA4 or 0xA5 => 3, 0x5B or 0x5C => 4, _ => 0 };
    [JsonIgnore] public bool Valid => VirtualKey is >= 8 and < 255 && ScanCode <= 255;
    [JsonIgnore] public string Label => !string.IsNullOrWhiteSpace(Name) ? Name : VirtualKey switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)VirtualKey).ToString(),
        >= 0x70 and <= 0x87 => "F" + (VirtualKey - 0x6F),
        >= 0x60 and <= 0x69 => "Num " + (VirtualKey - 0x60),
        0xA2 or 0x11 => "Ctrl", 0xA3 => "Right Ctrl", 0xA0 or 0x10 => "Shift", 0xA1 => "Right Shift",
        0xA4 or 0x12 => "Alt", 0xA5 => "Right Alt", 0x5B => "Win", 0x5C => "Right Win",
        0x08 => "Backspace", 0x09 => "Tab", 0x0D => Extended ? "Num Enter" : "Enter", 0x1B => "Esc",
        0x20 => "Space", 0x21 => "Page Up", 0x22 => "Page Down", 0x23 => "End", 0x24 => "Home",
        0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down", 0x2D => "Insert", 0x2E => "Delete",
        0x13 => "Pause", 0x14 => "Caps Lock", 0x2C => "Print Screen", 0x90 => "Num Lock", 0x91 => "Scroll Lock",
        _ => "Key " + VirtualKey.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)
    };
}

public sealed record KeyboardChord(KeyboardKey[] Keys)
{
    [JsonIgnore] public string Label => string.Join(" + ", Keys.Select(k => k.Label));
    [JsonIgnore] public string Signature => string.Join("/", Keys.Select(k => $"{k.VirtualKey}:{k.ScanCode}:{k.Extended}:{k.Name}"));
    public KeyboardChord? Validated()
    {
        if (Keys is null || Keys.Length is < 1 or > 256 || Keys.Any(k => !k.Valid)) return null;
        var keys = Keys.DistinctBy(k => (k.VirtualKey, k.Extended)).Select(k => k with
        { Name = k.Name is null ? null : new string(k.Name.Where(c => !char.IsControl(c)).Take(40).ToArray()).Trim() })
            .OrderBy(k => k.Modifier == 0 ? 5 : k.Modifier).ToArray();
        // Windows' secure attention sequence cannot be injected as an ordinary shortcut.
        if (keys.Any(k => k.Modifier == 1) && keys.Any(k => k.Modifier == 3) && keys.Any(k => k.VirtualKey == 0x2E)) return null;
        return new(keys);
    }
    public static bool Same(KeyboardChord? a, KeyboardChord? b) => a?.Signature == b?.Signature;
    public static string Fingerprint<T>(Dictionary<T, KeyboardChord> values) where T : notnull =>
        string.Join("|", values.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value.Signature));
    public static Dictionary<T, KeyboardChord> Clean<T>(Dictionary<T, KeyboardChord>? values, Func<T, bool> valid) where T : notnull =>
        (values ?? []).Where(p => valid(p.Key) && p.Value?.Validated() is not null).OrderBy(p => p.Key).ToDictionary(p => p.Key, p => p.Value.Validated()!);
}

// A single connected key gesture, never a macro or repeat timer. No live state is persisted.
public sealed class KeyboardRecording
{
    private readonly HashSet<(ushort, bool)> _held = [];
    private readonly HashSet<ushort> _entry;
    private readonly List<KeyboardKey> _keys = [];
    private bool _armed;
    public KeyboardRecording(IEnumerable<KeyboardKey>? initiallyHeld = null)
    {
        // GetAsyncKeyState does not distinguish the two Enter locations. Entry gating uses VK only.
        _entry = (initiallyHeld ?? []).Where(k => k.VirtualKey != 0x13).Select(k => k.VirtualKey).ToHashSet();
        _armed = _entry.Count == 0;
    }
    public bool WaitingForRelease => !_armed;
    public string Preview => string.Join(" + ", _keys.DistinctBy(k => (k.VirtualKey, k.Extended)).Select(k => k.Label));
    public bool Complete { get; private set; }
    public KeyboardChord? Result { get; private set; }
    public void Update(KeyboardKey key, bool down)
    {
        if (Complete || !key.Valid) return;
        if (!_armed)
        {
            if (down && key.VirtualKey != 0x13) _entry.Add(key.VirtualKey); else _entry.Remove(key.VirtualKey);
            if (_entry.Count == 0) _armed = true;
            return;
        }
        var id = (key.VirtualKey, key.Extended);
        if (down) { if (_held.Add(id) && _armed) _keys.Add(key); if (key.VirtualKey == 0x13) _held.Remove(id); }
        else _held.Remove(id);
        if (_held.Count != 0) return;
        if (_keys.Count == 0) return;
        Complete = true; Result = new KeyboardChord(_keys.ToArray()).Validated();
    }
}

// Only keys already delivered before recording may pass through during the entry-release gate.
// This prevents a fresh shortcut from escaping while the user releases an initially held modifier.
public sealed class KeyboardRecordingFilter
{
    private readonly HashSet<ushort> _entry;
    private readonly HashSet<(ushort, bool)> _captured = [];
    public KeyboardRecording Recording { get; }
    public KeyboardRecordingFilter(IEnumerable<KeyboardKey> initiallyHeld)
    {
        var keys = initiallyHeld.ToArray(); _entry = keys.Select(k => k.VirtualKey).ToHashSet(); Recording = new(keys);
    }
    public bool Update(KeyboardKey key, bool down)
    {
        if (Recording.Complete || !key.Valid) return false;
        var id = (key.VirtualKey, key.Extended);
        var suppress = _captured.Contains(id) || down && !_entry.Contains(key.VirtualKey);
        if (suppress && down) _captured.Add(id);
        if (!down) { _captured.Remove(id); _entry.Remove(key.VirtualKey); }
        Recording.Update(key, down); return suppress;
    }
}
