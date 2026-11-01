using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FullTilt.Core;
using Control = FullTilt.Core.Control;

namespace FullTilt;

internal sealed class EditorSession(SettingsViewModel model)
{
    private sealed class TabState
    {
        public Dictionary<string, string> Saved = [], Draft = [];
        public readonly EditHistory History = new();
        public Dictionary<string, string>? Gesture;
    }
    private readonly Dictionary<Guid, Dictionary<EditTab, TabState>> _profiles = [];
    private readonly Dictionary<EditTab, AppSettings> _views = [];
    private BindingRow[]? _bindingRows;
    private bool _replaying;
    public event Action? Changed;
    private void Signal() { _views.Clear(); _bindingRows = null; Changed?.Invoke(); }
    public Func<Control[], EditTab?>? ResolveBindingConflict { get; set; }
    private TabState State(EditTab tab)
    {
        if (!_profiles.TryGetValue(model.ActiveProfileId, out var profile)) _profiles[model.ActiveProfileId] = profile = [];
        if (!profile.TryGetValue(tab, out var state))
        {
            profile[tab] = state = new(); state.Saved = EditFields.Read(tab, model.Settings); state.Draft = new(state.Saved);
        }
        return state;
    }
    public AppSettings View(EditTab tab)
    {
        if (!_views.TryGetValue(tab, out var view)) _views[tab] = view = EditFields.Apply(tab, State(tab).Draft, model.Settings);
        return view;
    }
    public bool Dirty(EditTab tab) => !EditFields.Equal(State(tab).Saved, State(tab).Draft);
    public bool HasDrafts => Dirty(EditTab.Keybindings) || Dirty(EditTab.PieMenus);
    public int Count(EditTab tab)
    {
        if (tab != EditTab.PieMenus) return EditFields.Differences(State(tab).Saved, State(tab).Draft);
        var view = View(tab); var saved = model.Settings;
        var count = view.PieMenuSizePercent == saved.PieMenuSizePercent ? 0 : 1;
        count += Enum.GetValues<Control>().Count(c => State(tab).Saved.GetValueOrDefault("Binding/" + c) != State(tab).Draft.GetValueOrDefault("Binding/" + c));
        foreach (var id in saved.PieMenus.Select(m => m.Id).Union(view.PieMenus.Select(m => m.Id)))
        {
            var a = saved.PieMenus.FirstOrDefault(m => m.Id == id); var b = view.PieMenus.FirstOrDefault(m => m.Id == id);
            if (a is null || b is null) { count++; continue; }
            if (a.Name != b.Name) count++;
            if (a.SegmentCount != b.SegmentCount) count++;
            count += Enumerable.Range(0, Math.Min(a.Slots.Length, b.Slots.Length)).Count(i => a.Slots[i] != b.Slots[i] || !KeyboardChord.Same(a.RecordedSlots.GetValueOrDefault(i), b.RecordedSlots.GetValueOrDefault(i)));
            count += Enumerable.Range(0, 4).Count(i => a.DPadActions[i] != b.DPadActions[i] || !KeyboardChord.Same(a.RecordedDPad.GetValueOrDefault(i), b.RecordedDPad.GetValueOrDefault(i)));
        }
        return count;
    }
    public bool CanUndo(EditTab tab) => State(tab).History.CanUndo;
    public bool CanRedo(EditTab tab) => State(tab).History.CanRedo;
    public void Begin(EditTab tab) => State(tab).Gesture ??= new(State(tab).Draft);
    public void End(EditTab tab)
    {
        var state = State(tab);
        if (state.Gesture is { } before) { state.History.Record(before, state.Draft); state.Gesture = null; Signal(); }
    }
    public void Edit(EditTab tab, AppSettings next)
    {
        var state = State(tab); var fields = EditFields.Read(tab, next);
        if (EditFields.Equal(state.Draft, fields)) return;
        if (state.Gesture is null) state.History.Record(state.Draft, fields);
        state.Draft = fields; Signal();
    }
    public void SavedChanged(AppSettings previous, AppSettings next, bool movement = false)
    {
        // Initialize histories against the outgoing configuration before replacing it.
        if (previous.ActiveProfileId == next.ActiveProfileId)
        {
            foreach (var tab in Enum.GetValues<EditTab>())
            {
                var state = State(tab); var latest = EditFields.Read(tab, next);
                if (tab == EditTab.Movement)
                {
                    if (movement && !_replaying && state.Gesture is null) state.History.Record(EditFields.Read(tab, previous), latest);
                    state.Draft = latest;
                }
                else state.Draft = EditFields.Merge(state.Saved, state.Draft, latest);
                state.Saved = latest;
            }
        }
    }
    public void RefreshProfile()
    {
        foreach (var tab in Enum.GetValues<EditTab>())
        {
            var state = State(tab); var latest = EditFields.Read(tab, model.Settings);
            state.Draft = EditFields.Merge(state.Saved, state.Draft, latest); state.Saved = latest;
        }
        Signal();
    }
    public void Replay(EditTab tab, bool undo)
    {
        End(tab); var state = State(tab);
        if (undo ? !state.History.CanUndo : !state.History.CanRedo) return;
        var fields = undo ? state.History.Undo(state.Draft) : state.History.Redo(state.Draft);
        if (tab == EditTab.Movement)
        {
            _replaying = true;
            try { model.ApplyMovementHistory(EditFields.Apply(tab, fields, model.Settings)); }
            finally { _replaying = false; }
        }
        else { state.Draft = fields; Signal(); }
    }
    public void Discard(EditTab tab) { End(tab); Edit(tab, EditFields.Apply(tab, State(tab).Saved, model.Settings)); }
    public bool Save(params EditTab[] tabs)
    {
        foreach (var tab in tabs) End(tab);
        EditTab? bindingWinner = null;
        if (tabs.Contains(EditTab.Keybindings) && tabs.Contains(EditTab.PieMenus))
        {
            var key = State(EditTab.Keybindings); var pie = State(EditTab.PieMenus);
            var conflicts = Enum.GetValues<Control>().Where(c =>
                key.Draft.GetValueOrDefault("Binding/" + c) != key.Saved.GetValueOrDefault("Binding/" + c) &&
                pie.Draft.GetValueOrDefault("Binding/" + c) != pie.Saved.GetValueOrDefault("Binding/" + c) &&
                key.Draft.GetValueOrDefault("Binding/" + c) != pie.Draft.GetValueOrDefault("Binding/" + c)).ToArray();
            if (conflicts.Length != 0)
            {
                bindingWinner = ResolveBindingConflict?.Invoke(conflicts);
                if (bindingWinner is null) { model.Warning = "These drafts assign the same activation button differently. Choose which tab's binding to save, or save each tab separately."; return false; }
            }
        }
        var next = model.Settings;
        foreach (var tab in tabs.OrderBy(tab => tab == bindingWinner ? 1 : 0))
        {
            var state = State(tab);
            next = EditFields.Apply(tab, EditFields.Merge(state.Saved, state.Draft, EditFields.Read(tab, next)), next);
        }
        if (!model.CommitEditor(next)) return false;
        foreach (var tab in tabs) { var state = State(tab); state.Saved = EditFields.Read(tab, model.Settings); state.Draft = new(state.Saved); }
        Signal(); return true;
    }
    public BindingRow[] BindingRows()
    {
        if (_bindingRows is not null) return _bindingRows;
        var view = View(EditTab.Keybindings);
        return _bindingRows = model.Bindings.Concat(model.DPadBindings).Select(row => new BindingRow(row.Control, view.Mappings[row.Control],
            view.PieBindings.TryGetValue(row.Control, out var id) ? id : null, view.PieMenus, (control, choice) =>
            {
                var current = View(EditTab.Keybindings);
                var mappings = new Dictionary<Control, InputAction>(current.Mappings) { [control] = choice is InputAction action ? action : InputAction.None };
                var bindings = new Dictionary<Control, Guid>(current.PieBindings);
                var recorded = new Dictionary<Control, KeyboardChord>(current.RecordedBindings); recorded.Remove(control);
                if (choice is KeyboardChord chord) recorded[control] = chord;
                if (choice is Guid menu) bindings[control] = menu; else bindings.Remove(control);
                Edit(EditTab.Keybindings, current with { Mappings = mappings, PieBindings = bindings, RecordedBindings = recorded });
            }, view.RecordedBindings.GetValueOrDefault(row.Control))).ToArray();
    }
    public bool BindingDirty(Control control) => State(EditTab.Keybindings).Draft.GetValueOrDefault("Binding/" + control) != State(EditTab.Keybindings).Saved.GetValueOrDefault("Binding/" + control);
    public bool RoleDirty => View(EditTab.Keybindings).CursorStick != model.Settings.CursorStick;
    public static string BindingLabel(AppSettings settings, Control control) => settings.PieBindings.TryGetValue(control, out var id)
        ? (settings.PieMenus.FirstOrDefault(m => m.Id == id)?.Name ?? "Deleted menu") + " · hold" : settings.RecordedBindings.TryGetValue(control, out var chord) ? chord.Label : ActionCatalog.Get(settings.Mappings[control]).Label;

    // Group slider mouse/keyboard gestures and complete numeric/name edits. Ordinary
    // buttons and selectors remain one operation per click.
    public void TrackGestures(FrameworkElement root, EditTab tab)
    {
        DependencyObject? Ancestor(object source, Type type)
        {
            var node = source as DependencyObject;
            while (node is not null && node != root) { if (type.IsInstanceOfType(node)) return node; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node); }
            return null;
        }
        var mouseGesture = false;
        root.PreviewMouseDown += (_, e) => { if (Ancestor(e.OriginalSource, typeof(Slider)) is not null) { Begin(tab); mouseGesture = true; } };
        root.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler((_, _) => { if (mouseGesture) { mouseGesture = false; End(tab); } }), true);
        root.AddHandler(UIElement.LostMouseCaptureEvent, new MouseEventHandler((_, e) => { if (mouseGesture && Ancestor(e.OriginalSource, typeof(Slider)) is not null) { mouseGesture = false; End(tab); } }), true);
        root.PreviewKeyDown += (_, e) => { if (Ancestor(e.OriginalSource, typeof(Slider)) is not null && e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown) Begin(tab); };
        root.PreviewKeyUp += (_, e) => { if (Ancestor(e.OriginalSource, typeof(Slider)) is not null) End(tab); };
        root.GotKeyboardFocus += (_, e) => { if (Ancestor(e.OriginalSource, typeof(TextBox)) is TextBox box && box.Name != "Search") Begin(tab); };
        root.LostKeyboardFocus += (_, e) => { if (Ancestor(e.OriginalSource, typeof(TextBox)) is not null) End(tab); };
        root.Unloaded += (_, _) => End(tab);
    }
}

internal static class EditChrome
{
    public static Brush Cherry => ThemePalette.Get("#F9E2E7");
    public static Brush CherryInk => ThemePalette.Get("#C54F65");
    public static Button Button(string label, Action click, bool primary = false)
    {
        var button = new Button { Content = label, Padding = new Thickness(12, 7, 12, 7), MinHeight = 34, Margin = new Thickness(4, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        Decorate(button, label);
        if (primary) button.Background = ThemePalette.Get("#ECFBCB");
        button.Click += (_, _) => click(); return button;
    }
    public static void Decorate(Button button, string label)
    {
        if (label is not ("Undo" or "Redo")) return;
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new UiIcon { Kind = label, Width = 14, Height = 14, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }); button.Content = content;
    }
}
