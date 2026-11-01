namespace FullTilt.Core;

public sealed record PiePreset(string Id, string Name, InputAction[] Actions, Control DefaultControl)
{
    public PieMenu Create() => new(Guid.NewGuid(), Name, (InputAction[])Actions.Clone()) { SegmentCount = Actions.Length, PresetId = Id };
}
public static class PiePresets
{
    public const int MigrationVersion = 1;
    public static IReadOnlyList<PiePreset> All { get; } = Array.AsReadOnly<PiePreset>([
        new("media", "Media", [InputAction.PlayPause, InputAction.NextTrack, InputAction.Mute, InputAction.PreviousTrack], Control.LT),
        new("browsing", "Browsing", [InputAction.Back, InputAction.PreviousTab, InputAction.NewTab, InputAction.Forward, InputAction.NextTab, InputAction.ReopenTab], Control.LB),
        new("windows", "Windows", [InputAction.TaskView, InputAction.MinimizeWindow, InputAction.SnapLeft, InputAction.RestoreWindow, InputAction.MaximizeWindow, InputAction.SnapRight], Control.RT),
        new("editing", "Editing", [InputAction.Copy, InputAction.Undo, InputAction.Paste, InputAction.SelectAll], Control.RB)
    ]);
    public static PiePreset? Find(string? id) => All.FirstOrDefault(p => p.Id == id);
    public static AppSettings Initialize(AppSettings settings, bool bind = false)
    {
        if (settings.PiePresetMigration >= MigrationVersion) return settings;
        var menus = PieMenu.Validate(settings.PieMenus); var bindings = new Dictionary<Control, Guid>(settings.PieBindings);
        foreach (var preset in All)
        {
            var menu = menus.FirstOrDefault(m => m.PresetId == preset.Id);
            if (menu is null) { menu = preset.Create(); menus.Add(menu); }
            if (bind) bindings[preset.DefaultControl] = menu.Id;
        }
        return (settings with { PieMenus = menus, PieBindings = bindings, PiePresetMigration = MigrationVersion }).Validated();
    }
}

// Draft data is never sent to the runtime or store until Apply merges it with the latest settings.
public sealed class PieDraft
{
    private readonly PieMenu _original;
    private readonly Control? _originalOpener;
    private readonly double _originalSize;
    public double SizePercent { get; private set; }
    public PieMenu Menu { get; private set; }
    public Control? OpenWith { get; set; }
    public Control? RemovedOpener => OpenWith != _originalOpener ? _originalOpener : null;
    public bool IsNew { get; }
    public bool Deleted { get; set; }
    public bool Dirty => IsNew || Deleted || OpenWith != _originalOpener || Menu.Name != _original.Name || SizePercent != _originalSize || Menu.SegmentCount != _original.SegmentCount || !Menu.Slots.SequenceEqual(_original.Slots) || !Menu.DPadActions.SequenceEqual(_original.DPadActions) ||
        KeyboardChord.Fingerprint(Menu.RecordedSlots) != KeyboardChord.Fingerprint(_original.RecordedSlots) || KeyboardChord.Fingerprint(Menu.RecordedDPad) != KeyboardChord.Fingerprint(_original.RecordedDPad);
    public PieDraft(PieMenu menu, AppSettings settings, bool isNew = false)
    {
        _original = menu.Snapshot();
        Menu = _original.Snapshot();
        IsNew = isNew;
        SizePercent = _originalSize = PieMenu.ValidSize(settings.PieMenuSizePercent);
        _originalOpener = settings.PieBindings.Where(p => p.Value == menu.Id).OrderBy(p => p.Key).Select(p => (Control?)p.Key).FirstOrDefault();
        OpenWith = _originalOpener;
    }
    public void Rename(string name) => Menu = Menu with { Name = name };
    public void SetSize(double percent) => SizePercent = PieMenu.ValidSize(percent);
    public void Assign(int slot, InputAction action) => Menu = Menu.Assign(slot, action);
    public void AssignDPad(int direction, InputAction? action) => Menu = Menu.AssignDPad(direction, action);
    public void Record(int index, KeyboardChord chord, bool dpad = false) => Menu = Menu.Record(index, chord, dpad);
    public void Swap(int first, int second) => Menu = Menu.Swap(first, second);
    public InputAction[] RemovedByResize(int count) => count >= 2 && count < Menu.SegmentCount ? Menu.Slots.Skip(count).ToArray() : [];
    public void Resize(int count) => Menu = Menu.Resize(count);
    public void Restore()
    {
        if (FactoryDefaults.Menu(Menu) is { } baseline)
        {
            Menu = baseline.Snapshot() with { Id = Menu.Id, Name = Menu.Name };
            return;
        }
        var preset = PiePresets.Find(Menu.PresetId);
        Menu = Menu with { Slots = preset is null ? new InputAction[Menu.SegmentCount] : (InputAction[])preset.Actions.Clone(), SegmentCount = preset?.Actions.Length ?? Menu.SegmentCount, DPadActions = new InputAction?[4], RecordedSlots = [], RecordedDPad = [] };
    }
    public string? BindingConflict(AppSettings latest)
    {
        if (OpenWith is not { } control || OpenWith == _originalOpener) return null;
        if (latest.PieBindings.TryGetValue(control, out var id)) return id == Menu.Id ? null : latest.PieMenus.FirstOrDefault(m => m.Id == id)?.Name ?? "another menu";
        if (latest.RecordedBindings.TryGetValue(control, out var recorded)) return recorded.Label;
        var action = latest.Mappings.GetValueOrDefault(control);
        return action == InputAction.None ? null : ActionCatalog.Get(action).Label;
    }
    public AppSettings Apply(AppSettings latest)
    {
        if (SizePercent != _originalSize) latest = latest with { PieMenuSizePercent = SizePercent };
        if (Deleted) return PieMenu.Delete(latest, Menu.Id).Validated();
        var menus = latest.PieMenus.Where(m => m.Id != Menu.Id).ToList();
        var index = latest.PieMenus.FindIndex(m => m.Id == Menu.Id);
        menus.Insert(index < 0 ? menus.Count : index, Menu.Snapshot() with { Name = PieMenu.CleanName(Menu.Name) });
        var bindings = new Dictionary<Control, Guid>(latest.PieBindings); var mappings = new Dictionary<Control, InputAction>(latest.Mappings);
        if (OpenWith != _originalOpener)
        {
            if (_originalOpener is { } previous && bindings.GetValueOrDefault(previous) == Menu.Id) { bindings.Remove(previous); mappings[previous] = InputAction.None; }
            if (OpenWith is { } next) { bindings[next] = Menu.Id; mappings[next] = InputAction.None; }
        }
        return (latest with { PieMenus = menus, PieBindings = bindings, Mappings = mappings }).Validated();
    }
}
