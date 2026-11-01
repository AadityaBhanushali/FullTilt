using System.ComponentModel;

namespace FullTilt.Core;

public readonly record struct KeyTransition(ushort Key, bool Down)
{
    public ushort ScanCode { get; init; }
    public bool? Extended { get; init; }
    public bool IsExtended => Extended ?? Key is 0x5B or 0x5C or 0x21 or 0x22 or 0x23 or 0x24 or 0x2C or 0x2D or 0x2E or 0x25 or 0x26 or 0x27 or 0x28 or 0xA3 or 0xA5;
}
public interface IKeyInput
{
    bool IsDown(ushort key);
    // Submit an ordered batch. Return the successfully injected prefix, even on partial failure.
    int Send(ReadOnlySpan<KeyTransition> transitions);
}

// Worker-confined. Complete shortcut batches cannot interleave with FullTilt mouse input or other commands.
// Physical state is observed, never changed to force a named shortcut to win.
public sealed class KeyboardOwnership(IKeyInput input)
{
    public const ushort Ctrl = 0xA2, Shift = 0xA0, Alt = 0xA4, Win = 0x5B;
    private readonly bool[] _owned = new bool[256];
    private readonly Dictionary<(ushort Key, bool Extended), KeyTransition> _releases = [];
    private Dictionary<(ushort Key, bool Extended), KeyboardKey> _held = [];
    private readonly HashSet<(ushort Key, bool Extended)> _borrowed = [];
    private readonly HashSet<ushort> _latched = [];
    private readonly HashSet<ushort> _releasedInFrame = [];
    private bool _inFrame;
    public bool IsOwned(ushort key) => _owned[key];
    // SendInput is queued: a key we just released can still appear down in native state.
    // Keep that observation separate from physical ownership for this worker transaction.
    public void BeginFrame() { _inFrame = true; _releasedInFrame.Clear(); }
    private static ushort[] ModifierFamily(ushort key) => new KeyboardKey(key).Modifier switch
    {
        1 => [0x11, Ctrl, 0xA3], 2 => [0x10, Shift, 0xA1],
        3 => [0x12, Alt, 0xA5], 4 => [Win, 0x5C], _ => []
    };
    private bool ObservedDown(ushort key) => !(_inFrame && _releasedInFrame.Contains(key)) && input.IsDown(key);
    private bool ModifierDown(ushort key)
    {
        var family = ModifierFamily(key);
        return family.Length == 0 ? ObservedDown(key) : family.Any(k => _owned[k]) || ExternallyDown(new(key));
    }
    public void SetLatch(StickyModifier modifier, bool active)
    {
        var key = modifier switch { StickyModifier.Ctrl => Ctrl, StickyModifier.Shift => Shift, StickyModifier.Alt => Alt, _ => throw new ArgumentOutOfRangeException(nameof(modifier)) };
        var changed = active ? _latched.Add(key) : _latched.Remove(key);
        if (!changed && (!active || _owned[key] || !AllowsLatch(key))) return;
        ReconcileHolds();
    }
    private bool AllowsLatch(ushort key) => _held.Count == 0 || _held.Values.Any(k => k.Modifier == new KeyboardKey(key).Modifier);
    public void SetHolds(IReadOnlyList<KeyboardChord> chords)
    {
        var next = new Dictionary<(ushort, bool), KeyboardKey>();
        foreach (var chord in chords)
            if (chord.Validated() is { } valid)
                foreach (var key in valid.Keys) next.TryAdd((key.VirtualKey, key.Extended), key);
        _borrowed.RemoveWhere(key => !next.ContainsKey(key));
        _held = next;
        ReconcileHolds();
    }
    private void ReconcileHolds()
    {
        var wanted = new Dictionary<(ushort Key, bool Extended), KeyboardKey>(_held);
        foreach (var key in _latched)
            if (AllowsLatch(key))
                wanted.TryAdd((key, new KeyTransition(key, true).IsExtended), new(key, Extended: new KeyTransition(key, true).IsExtended));
        var events = new List<KeyTransition>();
        foreach (var release in _releases.Values.Reverse().OrderBy(k => new KeyboardKey(k.Key).Modifier != 0 ? 1 : 0))
            if (!wanted.ContainsKey((release.Key, release.IsExtended))) events.Add(release);
        foreach (var pair in wanted.OrderBy(p => p.Value.Modifier == 0 ? 5 : p.Value.Modifier))
        {
            if (_releases.ContainsKey(pair.Key) || _borrowed.Contains(pair.Key)) continue;
            if (ExternallyDown(pair.Value))
            {
                if (_held.ContainsKey(pair.Key)) _borrowed.Add(pair.Key);
                continue;
            }
            events.Add(_held.ContainsKey(pair.Key)
                ? new(pair.Value.VirtualKey, true) { ScanCode = pair.Value.ScanCode, Extended = pair.Value.Extended }
                : new(pair.Value.VirtualKey, true));
        }
        if (events.Count != 0) Send(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(events));
    }
    private bool ExternallyDown(KeyboardKey key)
    {
        var family = ModifierFamily(key.VirtualKey);
        if (family.Length == 0) return !_owned[key.VirtualKey] && ObservedDown(key.VirtualKey);
        // Generic modifier state includes FullTilt's own left/right key. Do not mistake
        // that ownership for a physical key when another held binding shares the family.
        var anyOwned = family.Any(k => _owned[k]);
        var recentlyReleased = _inFrame && family.Any(k => _releasedInFrame.Contains(k));
        return family.Any(k => !_owned[k] && ObservedDown(k) && (k is not (0x10 or 0x11 or 0x12) || !anyOwned && !recentlyReleased));
    }
    public void Shortcut(ushort key, params ushort[] modifiers)
    {
        // An already-held action key cannot supply a fresh edge without releasing someone else's key.
        if (_owned[key] || ObservedDown(key)) return;
        var events = new List<KeyTransition>(12);
        var suspended = new List<ushort>(2);
        foreach (var sticky in new[] { Ctrl, Shift, Alt })
            if (_owned[sticky] && _latched.Contains(sticky) && !_held.ContainsKey((sticky, false)) && !modifiers.Contains(sticky)) { events.Add(new(sticky, false)); suspended.Add(sticky); }
        var borrowed = new List<ushort>(3);
        foreach (var modifier in modifiers)
            if (!_owned[modifier] && !ModifierDown(modifier)) { events.Add(new(modifier, true)); borrowed.Add(modifier); }
        events.Add(new(key, true)); events.Add(new(key, false));
        for (var i = borrowed.Count - 1; i >= 0; i--) events.Add(new(borrowed[i], false));
        foreach (var sticky in suspended) events.Add(new(sticky, true));
        Send(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(events));
    }
    public void Play(KeyboardChord chord)
    {
        if (chord.Validated() is not { } valid) return;
        if (valid.Keys.Any(k => k.Modifier == 0 && (_owned[k.VirtualKey] || ObservedDown(k.VirtualKey)))) return;
        var events = new List<KeyTransition>(); var borrowed = new List<KeyboardKey>(); var suspended = new List<ushort>();
        foreach (var sticky in new[] { Ctrl, Shift, Alt })
            if (_owned[sticky] && _latched.Contains(sticky) && !_held.ContainsKey((sticky, false)) && !valid.Keys.Any(k => k.Modifier == new KeyboardKey(sticky).Modifier))
            { events.Add(new(sticky, false)); suspended.Add(sticky); }
        foreach (var key in valid.Keys)
            if (!_owned[key.VirtualKey] && !ModifierDown(key.VirtualKey))
            { events.Add(new(key.VirtualKey, true) { ScanCode = key.ScanCode, Extended = key.Extended }); borrowed.Add(key); }
        for (var i = borrowed.Count - 1; i >= 0; i--)
            events.Add(new(borrowed[i].VirtualKey, false) { ScanCode = borrowed[i].ScanCode, Extended = borrowed[i].Extended });
        foreach (var sticky in suspended) events.Add(new(sticky, true));
        Send(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(events));
    }
    private void Send(ReadOnlySpan<KeyTransition> events)
    {
        var sent = input.Send(events);
        if (sent < 0 || sent > events.Length) throw new InvalidOperationException("Invalid keyboard adapter result.");
        foreach (var transition in events[..sent])
        {
            var id = (transition.Key, transition.IsExtended);
            if (transition.Down) _releases[id] = transition with { Down = false }; else _releases.Remove(id);
            if (transition.Down) _releasedInFrame.Remove(transition.Key); else _releasedInFrame.Add(transition.Key);
            _owned[transition.Key] = _releases.Keys.Any(k => k.Key == transition.Key);
        }
        if (sent != events.Length)
            throw new Win32Exception("Keyboard input was blocked or partially sent. FullTilt paused; return to a normal desktop window and resume.");
    }
    public void ReleaseAll()
    {
        _held.Clear(); _borrowed.Clear(); _latched.Clear();
        Exception? failure = null;
        // Release action keys before modifiers when recovering a partially submitted shortcut.
        foreach (var release in _releases.Values.OrderBy(k => new KeyboardKey(k.Key).Modifier != 0 ? 1 : 0).ToArray())
                try { Send([release]); }
                catch (Win32Exception ex) { failure = ex; }
        if (failure is not null) throw failure;
    }
}
