namespace FullTilt.Core;

public sealed record PieMenu(Guid Id, string Name, InputAction[] Slots)
{
    public int SegmentCount { get; init; } = 8;
    public string? PresetId { get; init; }
    public double SizePercent { get; init; } = 100;
    // Up, Down, Left, Right. Null preserves the legacy suppression while a pie is open.
    public InputAction?[] DPadActions { get; init; } = new InputAction?[4];
    public Dictionary<int, KeyboardChord> RecordedSlots { get; init; } = [];
    public Dictionary<int, KeyboardChord> RecordedDPad { get; init; } = [];
    public PieMenu Record(int index, KeyboardChord chord, bool dpad = false)
    {
        if (chord.Validated() is not { } valid || index < 0 || index >= (dpad ? 4 : SegmentCount)) return this;
        var menu = dpad ? AssignDPad(index, InputAction.None) : Assign(index, InputAction.None);
        var records = new Dictionary<int, KeyboardChord>(dpad ? menu.RecordedDPad : menu.RecordedSlots) { [index] = valid };
        return dpad ? menu with { RecordedDPad = records } : menu with { RecordedSlots = records };
    }
    public string SlotLabel(int index) => RecordedSlots.TryGetValue(index, out var chord) ? chord.Label : ActionCatalog.Get(Slots[index]).Label;
    public string DPadLabel(int index) => RecordedDPad.TryGetValue(index, out var chord) ? chord.Label : DPadActions[index] is { } action ? ActionCatalog.Get(action).Label : "No override";
    public static readonly string[] DPadDirections = ["Up", "Down", "Left", "Right"];
    public PieMenu AssignDPad(int direction, InputAction? action)
    {
        if (direction is < 0 or > 3 || (action is { } value && !ActionCatalog.Contains(value))) return this;
        var actions = (InputAction?[])DPadActions.Clone(); actions[direction] = action;
        var recorded = new Dictionary<int, KeyboardChord>(RecordedDPad); recorded.Remove(direction);
        return this with { DPadActions = actions, RecordedDPad = recorded };
    }
    public PieMenu Snapshot() => this with { Slots = (InputAction[])Slots.Clone(), DPadActions = (InputAction?[])DPadActions.Clone(),
        RecordedSlots = KeyboardChord.Clean(RecordedSlots, i => i >= 0 && i < SegmentCount), RecordedDPad = KeyboardChord.Clean(RecordedDPad, i => i is >= 0 and < 4) };
    public static double ValidSize(double value) => double.IsFinite(value) ? Math.Clamp(value, 100, 200) : 100;
    public static double Diameter(double sizePercent) => 440 * ValidSize(sizePercent) / 100;
    public static readonly string[] Directions = ["Up", "Up-right", "Right", "Down-right", "Down", "Down-left", "Left", "Up-left"];
    public static PieMenu Create(string name = "New menu", int count = 8) => new(Guid.NewGuid(), name, new InputAction[PieGeometry.Count(count)]) { SegmentCount = PieGeometry.Count(count) };
    public PieMenu Resize(int count)
    {
        if (count is < 2 or > 8) return this;
        var slots = new InputAction[count]; Array.Copy(Slots, slots, Math.Min(Slots.Length, count));
        return this with { SegmentCount = count, Slots = slots, RecordedSlots = KeyboardChord.Clean(RecordedSlots, i => i >= 0 && i < count) };
    }
    public PieMenu Rename(string name) => this with { Name = CleanName(name) };
    public PieMenu Duplicate() => Snapshot() with { Id = Guid.NewGuid(), Name = CleanName(Name + " copy") };
    public PieMenu Assign(int slot, InputAction action)
    {
        if (slot < 0 || slot >= SegmentCount || !ActionCatalog.SupportsPie(action)) return this;
        var slots = (InputAction[])Slots.Clone(); slots[slot] = action;
        var recorded = new Dictionary<int, KeyboardChord>(RecordedSlots); recorded.Remove(slot);
        return this with { Slots = slots, RecordedSlots = recorded };
    }
    public PieMenu Swap(int first, int second)
    {
        if (first < 0 || first >= SegmentCount || second < 0 || second >= SegmentCount) return this;
        var slots = (InputAction[])Slots.Clone(); (slots[first], slots[second]) = (slots[second], slots[first]);
        var recorded = new Dictionary<int, KeyboardChord>(RecordedSlots);
        recorded.Remove(first); recorded.Remove(second);
        if (RecordedSlots.TryGetValue(first, out var a)) recorded[second] = a;
        if (RecordedSlots.TryGetValue(second, out var b)) recorded[first] = b;
        return this with { Slots = slots, RecordedSlots = recorded };
    }
    public static string CleanName(string? name)
    {
        var clean = new string((name ?? "").Where(c => !char.IsControl(c)).Take(100).ToArray()).Trim();
        return clean.Length == 0 ? "Unnamed menu" : clean;
    }
    public static List<PieMenu> Validate(IEnumerable<PieMenu>? menus)
    {
        var result = new List<PieMenu>(); var ids = new HashSet<Guid>();
        foreach (var menu in menus ?? [])
        {
            if (menu is null || menu.Id == Guid.Empty || !ids.Add(menu.Id)) continue;
            var count = PieGeometry.Count(menu.SegmentCount);
            var slots = new InputAction[count];
            for (var i = 0; i < Math.Min(count, menu.Slots?.Length ?? 0); i++)
                if (ActionCatalog.SupportsPie(menu.Slots![i])) slots[i] = menu.Slots[i];
            var dpad = new InputAction?[4];
            for (var i = 0; i < Math.Min(4, menu.DPadActions?.Length ?? 0); i++)
                if (menu.DPadActions![i] is { } action && ActionCatalog.Contains(action)) dpad[i] = action;
            result.Add(new(menu.Id, CleanName(menu.Name), slots) { SegmentCount = count, PresetId = PiePresets.Find(menu.PresetId)?.Id, SizePercent = ValidSize(menu.SizePercent), DPadActions = dpad,
                RecordedSlots = KeyboardChord.Clean(menu.RecordedSlots, i => i >= 0 && i < count && slots[i] == InputAction.None),
                RecordedDPad = KeyboardChord.Clean(menu.RecordedDPad, i => i is >= 0 and < 4 && dpad[i] == InputAction.None) });
        }
        return result;
    }
    public static AppSettings Delete(AppSettings settings, Guid id) => settings with
    {
        PieMenus = settings.PieMenus.Where(m => m.Id != id).ToList(),
        PieBindings = settings.PieBindings.Where(p => p.Value != id).ToDictionary(),
        Mappings = settings.Mappings.ToDictionary(p => p.Key, p => settings.PieBindings.GetValueOrDefault(p.Key) == id ? InputAction.None : p.Value)
    };
}

public static class PieGeometry
{
    public static int Count(int count) => count is >= 2 and <= 8 ? count : 8;
    public static double Width(int count) => 360.0 / Count(count);
    // Clockwise from Up, except two segments face Right/Left.
    public static double Center(int count, int index) => (count == 2 ? 90 : 0) + index * Width(count);
    public static int Sector(int count, double angle) => (int)Math.Floor(((angle - Center(count, 0) + Width(count) / 2 + 720) % 360) / Width(count)) % Count(count);
    public static int HitTest(int count, double x, double y) => x * x + y * y < (77.0 / 197) * (77.0 / 197) || x * x + y * y > 1 ? -1 : Sector(count, (Math.Atan2(x, -y) * 180 / Math.PI + 360) % 360);
    public static string Label(int count, int index) => PieMenu.Directions[(int)Math.Round(Center(count, index) / 45) % 8];
}

public sealed class PieSelection(int count = 8)
{
    public const double NeutralThreshold = .22;
    public const double SelectionThreshold = .55;
    public const double BoundaryHysteresisDegrees = 6;
    public const double ReleaseToleranceSeconds = .100;
    private double _neutralSeconds;
    public int Selected { get; private set; } = -1;
    public void Update(short x, short y, double elapsedSeconds = 0)
    {
        var dx = MotionMath.Normalize(x); var dy = MotionMath.Normalize(y);
        var magnitude = Math.Sqrt(dx * dx + dy * dy);
        // Keep the highlighted choice briefly as the stick springs back. This is
        // release tolerance, never a dwell before selecting or executing an action.
        if (magnitude <= NeutralThreshold)
        {
            if (double.IsFinite(elapsedSeconds) && elapsedSeconds > 0) _neutralSeconds += elapsedSeconds;
            if (_neutralSeconds > ReleaseToleranceSeconds + 1e-9) Selected = -1;
            return;
        }
        _neutralSeconds = 0;
        // Pre-positioned sticks select on the opening sample. The existing radial
        // and angular hysteresis still prevent weak movement/drift from selecting.
        if (magnitude < SelectionThreshold) return;
        var angle = (Math.Atan2(dx, dy) * 180 / Math.PI + 360) % 360;
        if (Selected >= 0)
        {
            var distance = Math.Abs((angle - PieGeometry.Center(count, Selected) + 540) % 360 - 180);
            if (distance <= PieGeometry.Width(count) / 2 + BoundaryHysteresisDegrees) return;
        }
        Selected = PieGeometry.Sector(count, angle);
    }
}

public readonly record struct PieTarget(nint Foreground, int X, int Y);
public sealed record PiePresentation(PieMenu Menu, PieTarget Target, int Selected, bool MouseHeld)
{
    public double SizePercent { get; init; } = 100;
    public ControllerButtons DPadPressed { get; init; }
}
// Worker-side adapter; presentation is asynchronous. Dispatch waits for acknowledged hiding.
public interface IPiePlatform
{
    PieTarget Capture();
    nint Foreground { get; }
    void Present(PiePresentation? presentation);
    bool IsHidden { get; }
    string? Error => null;
}

public readonly record struct PiePlacement(int X, int Y, int Size)
{
    // All inputs and output are physical pixels; DPI only scales the desired DIP diameter.
    public static PiePlacement Fit(int x, int y, int left, int top, int right, int bottom, double dpi, double sizePercent = 100, double heightRatio = 1)
    {
        heightRatio = double.IsFinite(heightRatio) ? Math.Max(1, heightRatio) : 1;
        var size = Math.Max(1, Math.Min((int)Math.Round(PieMenu.Diameter(sizePercent) * Math.Clamp(dpi / 96, .5, 4)), Math.Min(right - left, (int)Math.Floor((bottom - top) / heightRatio))));
        return new(Math.Clamp(x - size / 2, left, Math.Max(left, right - size)), Math.Clamp(y - size / 2, top, Math.Max(top, bottom - (int)Math.Ceiling(size * heightRatio))), size);
    }
}
