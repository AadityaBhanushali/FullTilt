using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows.Data;
using FullTilt.Core;
using FullTilt.Windows;

namespace FullTilt;

public sealed class ControllerOption(int slot, string label) : INotifyPropertyChanged
{
    public int Slot { get; } = slot;
    public string Label { get; private set; } = label;
    public void UpdateLabel(string label) { Label = label; PropertyChanged?.Invoke(this, new(nameof(Label))); }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record BindingChoice(object Value, string Label, string Category, string Description);
public sealed class BindingRow : INotifyPropertyChanged
{
    private object _selection;
    private readonly Control _control;
    private readonly Action<Control, object> _changed;
    public BindingRow(Control control, InputAction action, Guid? menu, IReadOnlyList<PieMenu> menus, Action<Control, object> changed, KeyboardChord? recorded = null)
    {
        _control = control; _selection = recorded is not null ? recorded : menu is { } id ? id : action; _changed = changed;
        var choices = ActionCatalog.PickerEntries.Select(a => new BindingChoice(a.Action, a.Label, a.Category, a.Description)).ToList();
        choices.AddRange(menus.Select(m => new BindingChoice(m.Id, m.Name + " · hold", "Pie menus", "Aim before or while holding the menu button, then release. Hold the stick centered over 100 ms to cancel. Menu: " + m.Name)));
        Options = new ListCollectionView(choices);
        Options.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BindingChoice.Category)));
    }
    public string Label => _control switch { Control.L3 => "L3 · left stick click", Control.R3 => "R3 · right stick click", Control.DPadUp => "D-pad Up", Control.DPadDown => "D-pad Down", Control.DPadLeft => "D-pad Left", Control.DPadRight => "D-pad Right", _ => _control.ToString() };
    public Control Control => _control;
    public string AccessibilityName => Label + " action";
    public string Description => _selection is KeyboardChord ? "Hold the recorded keys while the controller button is pressed. Release it to let go." : Options.Cast<BindingChoice>().FirstOrDefault(c => Equals(c.Value, _selection))?.Description ?? "Unassigned";
    public ICollectionView Options { get; }
    public object Selection
    {
        get => _selection;
        set
        {
            if (value is null || Equals(value, _selection) || !(value is KeyboardChord chord && chord.Validated() is not null) && !Options.Cast<BindingChoice>().Any(c => Equals(c.Value, value))) return;
            _selection = value; PropertyChanged?.Invoke(this, new(nameof(Selection))); PropertyChanged?.Invoke(this, new(nameof(Action))); PropertyChanged?.Invoke(this, new(nameof(Description))); _changed(_control, value);
        }
    }
    public InputAction Action { get => _selection is InputAction action ? action : InputAction.None; set => Selection = value; }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private AppSettings _settings;
    private readonly SettingsStore _store;
    private readonly ControllerService? _service;
    private readonly DispatcherTimer _debounce;
    private Task<string?> _saveTask = Task.FromResult<string?>(null);
    private bool _dirty, _disposed;
    private int _connectedMask = -1;
    private string _saveStatus = "Changes save automatically";
    private string? _warning;
    private string _title = "Looking for a controller";
    private string _detail = "Connect an XInput controller to begin.";
    private bool _paused;
    private ControllerStatus _lastStatus = ControllerStatus.Initial;
    public ObservableCollection<PauseButtonOption> PauseButtons { get; } = [];
    private bool _recordingShortcut;
    private bool _capturedShortcut;
    public bool RecordingShortcut => _recordingShortcut;
    public bool CanRecordShortcut => _service is not null && _lastStatus.ActiveSlot >= 0;
    public string RecordingHint => !_recordingShortcut ? "Choose buttons or record a controller combination." : _capturedShortcut ? "Captured — review the buttons, then save or cancel." : "Release all buttons, hold 2–4 together, then release to review. Input is suspended while recording.";
    public void RecordPauseShortcut()
    {
        if (!CanRecordShortcut) return;
        _recordingShortcut = true; _capturedShortcut = false;
        _service!.BeginShortcutCapture(); Notify(nameof(RecordingShortcut)); Notify(nameof(RecordingHint)); Notify(nameof(CanApplyPauseShortcut));
    }
    public void StopShortcutRecording()
    {
        if (!_recordingShortcut) return;
        _service?.EndShortcutCapture(); _recordingShortcut = false; _capturedShortcut = false;
        Notify(nameof(RecordingShortcut)); Notify(nameof(RecordingHint)); Notify(nameof(CanApplyPauseShortcut));
    }
    public string PauseShortcutText => PauseShortcutRules.Format(_settings.PauseShortcut);
    public string[] CurrentPauseButtons => PauseShortcutRules.Buttons.Where(button => (_settings.PauseShortcut & button) != 0).Select(PauseShortcutRules.Label).ToArray();
    private ControllerButtons DraftPauseShortcut => PauseButtons.Where(b => b.Selected).Aggregate(ControllerButtons.None, (mask, b) => mask | b.Button);
    public bool CanApplyPauseShortcut => (!_recordingShortcut || _capturedShortcut) && PauseShortcutRules.IsValid(DraftPauseShortcut);
    public string PauseShortcutHint => CanApplyPauseShortcut ? $"{PauseButtons.Count(button => button.Selected)} buttons · Ready to save. Individual actions stay assigned. Release all buttons before toggling again." : "Choose 2–4 distinct buttons. Opposite D-pad directions cannot be combined.";
    public void ApplyPauseShortcut()
    {
        if (!CanApplyPauseShortcut) return;
        Change(_settings with { PauseShortcut = DraftPauseShortcut });
        StopShortcutRecording();
        Notify(nameof(PauseShortcutText)); Notify(nameof(CurrentPauseButtons)); UpdateStatus(_lastStatus);
    }
    private void RefreshPauseButtons()
    {
        PauseButtons.Clear();
        foreach (var button in PauseShortcutRules.Buttons)
            PauseButtons.Add(new(button, (_settings.PauseShortcut & button) != 0, () => { Notify(nameof(CanApplyPauseShortcut)); Notify(nameof(PauseShortcutHint)); }));
        Notify(nameof(PauseShortcutText)); Notify(nameof(CurrentPauseButtons)); Notify(nameof(CanApplyPauseShortcut)); Notify(nameof(PauseShortcutHint));
    }
    public AppSettings Settings => _settings;
    internal EditorSession Edits { get; }
    public IReadOnlyList<FullTiltProfile> Profiles => _settings.Profiles!;
    public Guid ActiveProfileId => _settings.ActiveProfileId!.Value;
    public void DiscardPauseShortcutDraft() { StopShortcutRecording(); RefreshPauseButtons(); }
    public bool HasPauseDraft => DraftPauseShortcut != _settings.PauseShortcut;
    public bool SaveProfile() => CommitProfile(ProfileLibrary.Checkpoint(_settings), false);
    public bool CreateProfile(string name)
    {
        try { return CommitProfile(ProfileLibrary.Create(_settings, name), true); }
        catch (ArgumentException ex) { Warning = ex.Message; return false; }
    }
    public bool CanDeleteProfile => Profiles.Count > 1;
    public bool RenameProfile(string name)
    {
        try { return CommitProfile(ProfileLibrary.Rename(_settings, ActiveProfileId, name), false); }
        catch (ArgumentException ex) { Warning = ex.Message; return false; }
    }
    public bool DeleteProfile()
    {
        try { return CommitProfile(ProfileLibrary.Delete(_settings, ActiveProfileId), true); }
        catch (ArgumentException ex) { Warning = ex.Message; return false; }
    }
    public bool SwitchProfile(Guid id)
    {
        if (id == ActiveProfileId) return true;
        try { return CommitProfile(ProfileLibrary.Switch(_settings, id), true); }
        catch (ArgumentException ex) { Warning = ex.Message; return false; }
    }
    private bool CommitProfile(AppSettings next, bool load)
    {
        // Finish queued saves before atomically writing the outgoing snapshot, selected
        // profile and incoming configuration together. Failed writes leave runtime intact.
        _debounce.Stop(); _saveTask.GetAwaiter().GetResult();
        next = ProfileLibrary.Checkpoint(next);
        var error = _store.Save(next);
        if (error is not null) { Warning = "Profile change was not applied. " + error; if (_dirty) _debounce.Start(); return false; }
        Edits.SavedChanged(_settings, next);
        _settings = next; _dirty = false; SaveStatus = "Profile saved";
        Edits.RefreshProfile();
        if (load)
        {
            _service?.Configure(_settings); // Profile identity forces ownership cleanup and neutral rearm.
            RefreshPauseButtons(); RebuildBindings(); MenusChanged?.Invoke(); UpdateStatus(_lastStatus);
        }
        Notify(string.Empty);
        return true;
    }
    public ObservableCollection<BindingRow> Bindings { get; } = [];
    public ObservableCollection<BindingRow> DPadBindings { get; } = [];
    public IReadOnlyList<Choice<CursorStick>> CursorStickOptions { get; } = [new(CursorStick.Left, "Left stick"), new(CursorStick.Right, "Right stick")];
    public IReadOnlyList<Choice<DPadMode>> DPadModeOptions { get; } = [new(DPadMode.CursorMovement, "Cursor movement"), new(DPadMode.AssignedActions, "Assigned actions")];
    public ObservableCollection<ControllerOption> ControllerOptions { get; } = [];
    public string SaveStatus { get => _saveStatus; private set { _saveStatus = value; Notify(); } }
    public string? Warning { get => _warning; set { _warning = value; Notify(); } }
    public string StatusTitle => _title;
    public string StatusDetail => _detail;
    public string PauseLabel => _paused ? "_Resume" : "_Pause";
    public bool Paused => _paused;
    internal bool KeyboardRecordingReady => _service?.KeyboardRecordingReady ?? true;
    internal void BeginKeyboardRecording() { StopShortcutRecording(); _service?.BeginKeyboardRecording(); }
    internal void EndKeyboardRecording() => _service?.EndKeyboardRecording();
    public string OverlayStateText { get; private set; } = "";
    public string ActiveStateText { get; private set; } = "No active modes";
    public bool IsDPadCursorMode => _settings.DPadMode == DPadMode.CursorMovement;
    public bool IsDPadActionMode => !IsDPadCursorMode;
    public string DPadHelp => IsDPadCursorMode ? "Fresh presses nudge; holding moves continuously." : "D-pad movement is off. Edit its actions in Keybindings.";
    public string StickRoleHelp => _settings.CursorStick == CursorStick.Left ? "Right stick scrolls. L3/R3 remain physical stick clicks." : "Left stick scrolls. L3/R3 remain physical stick clicks.";

    public SettingsViewModel(AppSettings settings, SettingsStore store, ControllerService? service, string? warning)
    {
        _settings = ProfileLibrary.Initialize(settings); _store = store; _service = service; _warning = warning;
        Edits = new(this);
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _debounce.Tick += (_, _) => QueueSave();
        RefreshPauseButtons(); RebuildBindings(); UpdateStatus(ControllerStatus.Initial);
    }
    public double CursorSpeed { get => _settings.CursorSpeed; set => Change(_settings with { CursorSpeed = WholeNumbers.Round(value) }); }
    public double PrecisionSpeed { get => _settings.PrecisionSpeed; set => Change(_settings with { PrecisionSpeed = Math.Round(value) }); }
    public double DPadTapDistance { get => _settings.DPadTapDistance; set => Change(_settings with { DPadTapDistance = value }); }
    public double SpeedBoostMultiplier
    {
        get => _settings.SpeedBoostMultiplier;
        set { Change(_settings with { SpeedBoostMultiplier = value }); Notify(nameof(SpeedBoostSlider)); }
    }
    // A separate slider projection keeps coercion at 4x from overwriting advanced manual values.
    public double SpeedBoostSlider
    {
        get => Math.Clamp(SpeedBoostMultiplier, 1, 4);
        set => SpeedBoostMultiplier = Math.Round(value, 1);
    }
    public double PrecisionPercent { get => _settings.PrecisionPercent; set => Change(_settings with { PrecisionPercent = value }); }
    public CursorStick CursorStick { get => _settings.CursorStick; set { Change(_settings with { CursorStick = value }); Notify(nameof(StickRoleHelp)); } }
    public DPadMode DPadMode { get => _settings.DPadMode; set { Change(_settings with { DPadMode = value }); Notify(nameof(IsDPadCursorMode)); Notify(nameof(IsDPadActionMode)); Notify(nameof(DPadHelp)); } }
    public bool ShowStateIndicator { get => _settings.ShowStateIndicator; set => Change(_settings with { ShowStateIndicator = value }); }
    public double ScrollSpeed { get => _settings.ScrollSpeed; set => Change(_settings with { ScrollSpeed = Math.Round(value, 1) }); }
    public double ResponseCurve { get => _settings.ResponseCurve; set => Change(_settings with { ResponseCurve = Math.Round(value, 2) }); }
    public double ResponseCurvePercent { get => ResponseCurve * 100; set => ResponseCurve = WholeNumbers.Round(value) / 100; }
    public double LeftDeadZone { get => _settings.LeftDeadZone; set => Change(_settings with { LeftDeadZone = Math.Round(value, 2) }); }
    public double RightDeadZone { get => _settings.RightDeadZone; set => Change(_settings with { RightDeadZone = Math.Round(value, 2) }); }
    public double PrecisionCursorSpeed { get => _settings.PrecisionCursorSpeed!.Value; set => Change(_settings with { PrecisionCursorSpeed = WholeNumbers.Round(value) }); }
    public double BoostedCursorSpeed { get => _settings.BoostedCursorSpeed!.Value; set => Change(_settings with { BoostedCursorSpeed = WholeNumbers.Round(value) }); }
    public double BoostedScrollSpeed { get => ScrollSpeed * _settings.ScrollBoostMultiplier; set => Change(_settings with { ScrollBoostMultiplier = value / ScrollSpeed }); }
    public bool ReverseScroll { get => _settings.ReverseScroll; set => Change(_settings with { ReverseScroll = value }); }
    public double LeftDeadZonePercent { get => LeftDeadZone * 100; set => LeftDeadZone = value / 100; }
    public double RightDeadZonePercent { get => RightDeadZone * 100; set => RightDeadZone = value / 100; }
    public void ResetMovement()
    {
        var defaults = FactoryDefaults.Create();
        Change(_settings with { PrecisionCursorSpeed = defaults.PrecisionCursorSpeed, BoostedCursorSpeed = defaults.BoostedCursorSpeed, CursorSpeed = defaults.CursorSpeed, PrecisionPercent = defaults.PrecisionPercent,
            SpeedBoostMultiplier = defaults.SpeedBoostMultiplier, ScrollSpeed = defaults.ScrollSpeed,
            ScrollBoostMultiplier = defaults.ScrollBoostMultiplier, ReverseScroll = defaults.ReverseScroll,
            CursorStick = defaults.CursorStick, ResponseCurve = defaults.ResponseCurve,
            LeftDeadZone = defaults.LeftDeadZone, RightDeadZone = defaults.RightDeadZone,
            PrecisionSpeed = defaults.PrecisionSpeed, DPadTapDistance = defaults.DPadTapDistance, DPadMode = defaults.DPadMode });
        Notify(string.Empty);
    }
    public int ControllerSlot { get => _settings.ControllerSlot; set { if (value is >= -1 and <= 3) Change(_settings with { ControllerSlot = value }); } }

    private void Change(AppSettings next, [CallerMemberName] string? property = null)
    {
        if (_settings == next) return;
        next = next.Validated();
        Edits.SavedChanged(_settings, next, movement: true);
        _settings = next;
        Edits.RefreshProfile();
        _service?.Configure(_settings);
        Notify(property); Notify(nameof(Settings));
        foreach (var projection in new[] { nameof(PrecisionCursorSpeed), nameof(BoostedCursorSpeed), nameof(BoostedScrollSpeed), nameof(LeftDeadZonePercent), nameof(RightDeadZonePercent), nameof(ResponseCurvePercent), nameof(StickRoleHelp) }) Notify(projection);
        _dirty = true;
        SaveStatus = "Saving changes…";
        _debounce.Stop(); _debounce.Start();
    }
    private void RebuildBindings()
    {
        Bindings.Clear();
        DPadBindings.Clear();
        foreach (var control in new[] { Control.A, Control.B, Control.X, Control.Y, Control.LB, Control.RB, Control.LT, Control.RT, Control.L3, Control.R3, Control.DPadUp, Control.DPadDown, Control.DPadLeft, Control.DPadRight })
            (control >= Control.DPadUp ? DPadBindings : Bindings).Add(new(control, _settings.Mappings[control], _settings.PieBindings.TryGetValue(control, out var menu) ? menu : null, _settings.PieMenus, (key, selection) =>
            {
                var mappings = new Dictionary<Control, InputAction>(_settings.Mappings) { [key] = selection is InputAction action ? action : InputAction.None };
                var pies = new Dictionary<Control, Guid>(_settings.PieBindings);
                var recorded = new Dictionary<Control, KeyboardChord>(_settings.RecordedBindings); recorded.Remove(key);
                if (selection is KeyboardChord chord) recorded[key] = chord;
                if (selection is Guid id) pies[key] = id; else pies.Remove(key);
                Change(_settings with { Mappings = mappings, PieBindings = pies, RecordedBindings = recorded });
            }, _settings.RecordedBindings.GetValueOrDefault(control)));
    }
    public event Action? MenusChanged;
    public void SetMenus(AppSettings next)
    {
        Change(next); RebuildBindings(); MenusChanged?.Invoke();
    }
    public bool ApplyPie(PieDraft draft)
        => CommitEditor(draft.Apply(_settings));
    internal void ApplyMovementHistory(AppSettings next) { Change(next, string.Empty); }
    internal bool CommitEditor(AppSettings next)
    {
        // Serialize this explicit atomic save after any pending movement/assignment save.
        _debounce.Stop(); _saveTask.GetAwaiter().GetResult();
        var error = _store.Save(next);
        if (error is not null)
        {
            Warning = error.Replace("Changes apply for this session.", "Draft changes remain unapplied in the editor.");
            if (_dirty) _debounce.Start();
            return false;
        }
        Edits.SavedChanged(_settings, next);
        _settings = next; _dirty = false; _service?.Configure(_settings);
        Edits.RefreshProfile();
        SaveStatus = "All changes saved"; RebuildBindings(); MenusChanged?.Invoke();
        return true;
    }
    public void ResetDefaults()
    {
        StopShortcutRecording();
        Change(AppSettings.FreshInstallation() with { TrayHintShown = _settings.TrayHintShown, Profiles = _settings.Profiles, ActiveProfileId = _settings.ActiveProfileId, PieMenuSizePercent = _settings.PieMenuSizePercent });
        RefreshPauseButtons(); RebuildBindings(); MenusChanged?.Invoke(); UpdateStatus(_lastStatus); Notify(string.Empty);
    }
    public void MarkTrayHintShown() => Change(_settings with { TrayHintShown = true });
    public void TogglePause() => _service?.SetPaused(!_paused);
    public void ReleaseCancel() => _service?.ResetInput();

    public void UpdateStatus(ControllerStatus status)
    {
        if (_recordingShortcut && _service?.ShortcutCapture.Active == false) StopShortcutRecording();
        if (_recordingShortcut && !_capturedShortcut && _service?.ShortcutCapture is { Complete: true } captured)
        {
            foreach (var option in PauseButtons) option.Selected = (captured.Buttons & option.Button) != 0;
            _capturedShortcut = true; Notify(nameof(RecordingHint)); Notify(nameof(CanApplyPauseShortcut)); Notify(nameof(PauseShortcutHint));
        }
        Notify(nameof(CanRecordShortcut));
        _lastStatus = status;
        _paused = status.Paused;
        var labels = new List<string>();
        if (status.PrecisionActive) labels.Add("Precision");
        if (status.SpeedBoostActive) labels.Add("Speed Boost");
        if (status.DragLocked) labels.Add("Drag locked");
        if (status.CtrlActive) labels.Add("Ctrl active");
        if (status.ShiftActive) labels.Add("Shift active");
        if (status.AltActive) labels.Add("Alt active");
        if (status.Paused) labels.Add("Paused");
        ActiveStateText = labels.Count == 0 ? "No active modes" : string.Join(" · ", labels);
        OverlayStateText = string.Join(" · ", labels.Where(label => label != "Speed Boost"));
        Notify(nameof(OverlayStateText)); Notify(nameof(ActiveStateText));
        _title = status.Paused ? "Controller mouse paused" : status.ActiveSlot < 0 ? "Waiting for a controller" : $"Controller {status.ActiveSlot + 1} connected";
        _detail = status.Error ?? (status.Paused ? $"Press {PauseShortcutText} or Resume to continue." : status.ActiveSlot < 0 ? "Connect an XInput controller. All four slots are scanned." : status.AwaitingNeutral ? "Release held buttons and center the sticks to arm them." : $"Ready · {PauseShortcutText} to pause");
        if (_connectedMask != status.ConnectedMask)
        {
            _connectedMask = status.ConnectedMask;
            // Keep item identity stable so refresh cannot clear SelectedValue.
            if (ControllerOptions.Count == 0)
            {
                ControllerOptions.Add(new(-1, "Automatic · first available"));
                for (var i = 0; i < 4; i++) ControllerOptions.Add(new(i, $"Controller {i + 1}"));
            }
            for (var i = 0; i < 4; i++)
                ControllerOptions[i + 1].UpdateLabel($"Controller {i + 1} · {((_connectedMask & (1 << i)) != 0 ? "connected" : "disconnected")}");
            Notify(nameof(ControllerSlot));
        }
        Notify(nameof(StatusTitle)); Notify(nameof(StatusDetail)); Notify(nameof(PauseLabel)); Notify(nameof(Paused));
    }

    private async void QueueSave()
    {
        _debounce.Stop();
        if (!_dirty) return;
        _dirty = false;
        var snapshot = _settings;
        // Serialize saves in submission order so an older write cannot overwrite a newer snapshot.
        _saveTask = _saveTask.ContinueWith(_ => _store.Save(snapshot), TaskScheduler.Default);
        var result = await _saveTask;
        if (_disposed) return;
        if (result is not null) Warning = result;
        if (!_dirty) SaveStatus = result is null ? "All changes saved" : "Changes apply for this session";
    }
    public void Dispose()
    {
        if (_disposed) return;
        StopShortcutRecording();
        _disposed = true; _debounce.Stop();
        _saveTask.GetAwaiter().GetResult();
        if (_dirty) _store.Save(_settings);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
}

public sealed class PauseButtonOption(ControllerButtons button, bool selected, Action changed) : INotifyPropertyChanged
{
    public ControllerButtons Button { get; } = button;
    public string Label => PauseShortcutRules.Label(Button);
    private bool _selected = selected;
    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); changed(); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
