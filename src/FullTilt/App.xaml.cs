using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FullTilt.Core;
using FullTilt.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace FullTilt;

public partial class App : Application
{
    private Mutex? _mutex;
    private bool _ownsMutex, _exiting;
    private EventWaitHandle? _openEvent;
    private RegisteredWaitHandle? _openRegistration;
    private ControllerService? _service;
    private SettingsViewModel? _model;
    private MainWindow? _window;
    private AppPreferencesViewModel? _preferences;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _trayPause;
    private Icon? _icon;
    private DispatcherTimer? _statusTimer;
    private StateIndicator? _indicator;
    private PieOverlayPlatform? _pies;
    private LaunchFeatures? _launch;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--touch-keyboard-helper")
        {
            var reply = TouchKeyboardNative.Run(e.Args[1]);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(reply));
            Shutdown(reply.Error is null ? 0 : 1);
            return;
        }
        ThemePalette.Apply(false);
        if (e.Args.Contains("--smoke-test"))
        {
            UiMotion.DiagnosticAnimationOverride = false;
            RunSmokeTest(e.Args);
            return;
        }
        UiWheelScrolling.Initialize();
        _mutex = new Mutex(false, @"Local\FullTilt.Desktop.v1");
        try { _ownsMutex = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsMutex = true; }
        _openEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\FullTilt.Open.v1");
        if (!_ownsMutex) { _openEvent.Set(); Shutdown(); return; }

        var (store, locationWarning) = SettingsStore.OpenDefault(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var loaded = store.Load();
        _pies = new PieOverlayPlatform(Dispatcher);
        _service = new ControllerService(loaded.Settings, piePlatform: _pies);
        if (e.Args.Contains("--paused")) _service.SetPaused(true);
        _model = new SettingsViewModel(loaded.Settings, store, _service, string.Join(" ", new[] { locationWarning, loaded.Warning }.Where(s => s is not null)));
        var preferences = new AppPreferencesViewModel(new AppPreferencesStore(store.DirectoryPath), new WindowsStartupRegistration(), Environment.ProcessPath, WindowsAppTheme.IsDark);
        _preferences = preferences;
        preferences.ApplyTheme();
        _window = new MainWindow(_model, preferences);
        MainWindow = _window;
        _window.HiddenToTray += ExplainTrayOnce;
        CreateTray();
        _indicator = new StateIndicator();
        _indicator.Failed += message => _model.Warning = message;
        _openRegistration = ThreadPool.RegisterWaitForSingleObject(_openEvent, (_, _) => Dispatcher.BeginInvoke(ShowSettings), null, -1, false);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        DispatcherUnhandledException += OnUiException;
        SessionEnding += (_, _) => ExitApplication();
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _statusTimer.Tick += (_, _) =>
        {
            var status = _service.Status;
            _model.UpdateStatus(status);
            try { _indicator?.UpdateState(_model.OverlayStateText, _model.ShowStateIndicator); }
            catch (Win32Exception ex)
            {
                _indicator?.Close(); _indicator = null;
                _model.Warning = "State indicator is unavailable. Settings still show active modes. " + ex.Message;
            }
            if (_trayPause is not null) _trayPause.Text = status.Paused ? "Resume" : "Pause";
            if (_tray is not null) _tray.Text = status.Paused ? "FullTilt · Paused" : status.ActiveSlot < 0 ? "FullTilt · Waiting for controller" : $"FullTilt · Controller {status.ActiveSlot + 1}";
        };
        _statusTimer.Start();
        if (!e.Args.Contains("--startup")) _window.Show();
        _service.Start();
        _launch = new LaunchFeatures(_window, preferences, message => _model.Warning = message);
        _window.FileAction += command => { if (command == "exit") ExitApplication(); };
        _launch.Start();
    }

    private void CreateTray()
    {
        using var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/FullTilt.ico")).Stream;
        using var original = new Icon(stream);
        _icon = (Icon)original.Clone();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open FullTilt", null, (_, _) => Dispatcher.BeginInvoke(ShowSettings));
        _trayPause = new Forms.ToolStripMenuItem("Pause", null, (_, _) => Dispatcher.BeginInvoke(() => _model?.TogglePause()));
        menu.Items.Add(_trayPause);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.BeginInvoke(ExitApplication));
        _tray = new Forms.NotifyIcon { Icon = _icon, Text = "FullTilt", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowSettings);
    }
    private void ExplainTrayOnce()
    {
        if (_model is null || _model.Settings.TrayHintShown) return;
        _tray?.ShowBalloonTip(6000, "FullTilt is still running", $"Open FullTilt from the system tray. {_model.PauseShortcutText} pauses or resumes. Choose Exit in the tray menu to quit.", Forms.ToolTipIcon.Info);
        _model.MarkTrayHintShown();
    }
    private void ShowSettings()
    {
        if (_window is null || _exiting) return;
        _window.RestoreFromTray();
        _window.Activate();
        // Foreground restrictions can deny this request; the visible window remains available.
        _ = NativeMethods.SetForegroundWindow(new WindowInteropHelper(_window).Handle);
    }
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!_exiting) Dispatcher.BeginInvoke(new Action(() => { if (!_exiting) _preferences?.SystemThemeChanged(); }));
    }
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        _service?.ResetInput();
        if (e.Mode == PowerModes.Suspend) _service?.SetPaused(true);
    }
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        _service?.ResetInput();
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff) _service?.SetPaused(true);
    }
    private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // End emulation before presenting any unexpected UI error.
        _service?.Dispose();
        e.Handled = true;
        ThemedDialog.Ask(null, "FullTilt stopped safely after an unexpected error.\n\n" + e.Exception.Message, "FullTilt", MessageBoxButton.OK);
        ExitApplication();
    }
    private void ExitApplication()
    {
        if (_exiting) return;
        if (_window is not null && !_window.TryResolvePieDraft()) return;
        _exiting = true;
        _launch?.Dispose();
        _service?.Dispose();
        if (_window is not null) { _window.AllowClose = true; _window.Close(); }
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _statusTimer?.Stop();
        _launch?.Dispose();
        _indicator?.Close();
        _openRegistration?.Unregister(null);
        _service?.Dispose();
        _pies?.Dispose();
        _model?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); }
        _icon?.Dispose();
        _openEvent?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void RunSmokeTest(string[] args)
    {
        // This branch never constructs an input service, input sink, tray icon or instance signal.
        // Render the real WPF tree offscreen and exercise bindings without live input or user settings.
        var index = Array.IndexOf(args, "--smoke-test");
        var output = index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : Path.Combine(AppContext.BaseDirectory, "diagnostics");
        try
        {
            Directory.CreateDirectory(output);
            ThemedDialog.RejectDiagnosticPrompts = true;
            File.WriteAllText(Path.Combine(output, "public-launch.txt"), PublicLaunchDiagnostics.Verify(output));
            if (args.Contains("--public-launch-only")) { Shutdown(0); return; }
            File.WriteAllText(Path.Combine(output, "onboarding-tour.txt"), TourDiagnostics.Verify(output));
            if (args.Contains("--tour-only")) { Shutdown(0); return; }
            File.WriteAllText(Path.Combine(output, "launch-features.txt"), LaunchDiagnostics.Verify(output));
            if (args.Contains("--launch-only")) { Shutdown(0); return; }
            File.WriteAllText(Path.Combine(output, "independent-actions.txt"), IndependentActionsDiagnostics.Verify(output));
            if (args.Contains("--independent-actions-only"))
            {
                File.WriteAllText(Path.Combine(output, "keybindings-regressions.txt"), KeybindingsRegressionDiagnostics.Verify(output));
                File.WriteAllText(Path.Combine(output, "app-preferences.txt"), AppPreferencesDiagnostics.Verify(output));
                Shutdown(0); return;
            }
            if (args.Contains("--whole-numbers-only"))
            {
                File.WriteAllText(Path.Combine(output, "whole-numbers.txt"), WholeNumberDiagnostics.Verify(output));
                File.WriteAllText(Path.Combine(output, "movement-reference.txt"), MovementPageDiagnostics.Verify(output));
                File.WriteAllText(Path.Combine(output, "pie-size.txt"), PieSizeDiagnostics.Verify(output)); Shutdown(0); return;
            }
            File.WriteAllText(Path.Combine(output, "recorded-keyboard.txt"), RecordedKeyboardDiagnostics.Verify(output));
            if (args.Contains("--recorded-keyboard-only")) { Shutdown(0); return; }
            File.WriteAllText(Path.Combine(output, "context-dpad.txt"), PieDPadDiagnostics.Verify(output));
            if (args.Contains("--context-dpad-only")) { Shutdown(0); return; }
            PieSwitchDiagnostics.Verify(output);
            if (args.Contains("--pie-switch-only")) { Shutdown(0); return; }
            File.WriteAllText(Path.Combine(output, "edit-workflow.txt"), EditWorkflowDiagnostics.Verify(output));
            if (args.Contains("--edit-workflow-only")) { Shutdown(0); return; }
            File.WriteAllText(Path.Combine(output, "window-chrome.txt"), WindowChromeDiagnostics.Verify(output));
            File.WriteAllText(Path.Combine(output, "ui-scrolling.txt"), UiScrollingDiagnostics.Baseline());
            UiWheelScrolling.Initialize();
            var diagnosticStore = new SettingsStore(Path.Combine(output, "settings"));
            using var model = new SettingsViewModel(new(), diagnosticStore, null, null);
            var window = new MainWindow(model) { AllowClose = true };
            ((System.Windows.Controls.TabControl)window.FindName("SettingsTabs")).SelectedIndex = 0;
            model.UpdateStatus(new(0, 1, true, false, null));
            model.CursorSpeed = 1500;
            if (model.Settings.CursorSpeed != 1500) throw new InvalidOperationException("Slider binding model failed.");
            model.Bindings[4].Action = InputAction.SnapLeft;
            if (model.Settings.Mappings[Control.LB] != InputAction.SnapLeft) throw new InvalidOperationException("Mapping model failed.");
            model.ResetDefaults();
            model.CursorSpeed = 1200;
            window.ApplyTemplate();
            var root = (FrameworkElement)window.Content;
            root.Measure(new System.Windows.Size(864, 840));
            root.Arrange(new Rect(0, 0, 864, 840));
            root.UpdateLayout();
            var picker = (System.Windows.Controls.ComboBox)window.FindName("ControllerPicker");
            picker.SelectedValue = 2;
            if (model.ControllerSlot != 2) throw new InvalidOperationException("Controller selection binding failed.");
            model.UpdateStatus(new(2, 15, true, false, null));
            if (!Equals(picker.SelectedValue, 2)) throw new InvalidOperationException("Controller refresh changed the selected slot.");
            model.ResetDefaults();
            model.CursorSpeed = 1200;
            var tap = (System.Windows.Controls.TextBox)window.FindName("TapNumber");
            tap.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, "23");
            tap.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)!.UpdateSource();
            if (model.DPadTapDistance != 23) throw new InvalidOperationException("Tap numeric binding failed.");
            tap.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, "NaN");
            tap.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)!.UpdateSource();
            if (!System.Windows.Controls.Validation.GetHasError(tap) || model.DPadTapDistance != 23) throw new InvalidOperationException("Invalid numeric input was accepted.");
            tap.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)!.UpdateTarget();
            var precision = (System.Windows.Controls.TextBox)window.FindName("PrecisionNumber");
            precision.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, "300");
            precision.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)!.UpdateSource();
            if (model.PrecisionCursorSpeed != 300) throw new InvalidOperationException("Precision numeric binding failed.");
            var boostNumber = (System.Windows.Controls.TextBox)window.FindName("SpeedBoostNumber");
            var boostSlider = (System.Windows.Controls.Slider)window.FindName("SpeedBoostSlider");
            var boostBinding = boostNumber.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)!;
            foreach (var value in new[] { 1.35, 1.75, 2.5, 3.25, 6.125 })
            {
                boostNumber.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, (value * model.CursorSpeed).ToString(System.Globalization.CultureInfo.CurrentCulture)); boostBinding.UpdateSource(); root.UpdateLayout();
                if (model.BoostedCursorSpeed != value * model.CursorSpeed || Math.Abs(boostSlider.Value - Math.Min(4000, value * model.CursorSpeed)) > .000001) throw new InvalidOperationException("Independent boost entry or slider projection changed its speed.");
            }
            foreach (var invalid in new[] { "", "no", "0", "-1", "NaN", "Infinity", "2147483648" })
            {
                boostNumber.SetCurrentValue(System.Windows.Controls.TextBox.TextProperty, invalid); boostBinding.UpdateSource();
                if (!System.Windows.Controls.Validation.GetHasError(boostNumber) || model.BoostedCursorSpeed != 7350) throw new InvalidOperationException("Invalid boost entry changed cursor settings.");
                boostNumber.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
                if (System.Windows.Controls.Validation.GetHasError(boostNumber)) throw new InvalidOperationException("Invalid boost text did not recover on leaving the field.");
            }
            var boostStore = new SettingsStore(Path.Combine(output, "boost-roundtrip"));
            if (boostStore.Save(model.Settings) is not null || boostStore.Load().Settings.BoostedCursorSpeed != 7350) throw new InvalidOperationException("Manual boost entry did not persist.");
            boostSlider.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty, 2.7 * model.CursorSpeed); root.UpdateLayout();
            if (model.BoostedCursorSpeed != 3240 || double.Parse(boostNumber.Text, System.Globalization.CultureInfo.CurrentCulture) != 2.7 * model.CursorSpeed) throw new InvalidOperationException("Boost slider did not synchronize number field.");
            model.UpdateStatus(new(0, 1, false, false, null, SpeedBoostActive: true));
            if (model.OverlayStateText != "" || model.ActiveStateText != "Speed Boost") throw new InvalidOperationException("Boost must stay in app status but not the overlay.");
            var allStates = new ControllerStatus(0, 1, true, false, null, PrecisionActive: true, DragLocked: true, CtrlActive: true, ShiftActive: true, AltActive: true, SpeedBoostActive: true);
            model.UpdateStatus(allStates);
            const string otherStates = "Precision · Drag locked · Ctrl active · Shift active · Alt active · Paused";
            if (model.OverlayStateText != otherStates || !model.ActiveStateText.Contains("Speed Boost")) throw new InvalidOperationException("Boost filtering changed another overlay state.");
            model.UpdateStatus(allStates with { SpeedBoostActive = false });
            if (model.OverlayStateText != otherStates) throw new InvalidOperationException("Releasing boost changed other overlay states.");
            model.UpdateStatus(new(0, 1, false, false, null));
            if (model.OverlayStateText != "" || model.ActiveStateText != "No active modes") throw new InvalidOperationException("Idle status failed to clear.");
            var movementScroll = Descendants<System.Windows.Controls.ScrollViewer>(root).First(scroll => ReferenceEquals(scroll.Content, window.FindName("MovementSections")));
            movementScroll.ScrollToBottom(); root.UpdateLayout();
            RenderDiagnostic(root, window.Background, output, "speedboost-movement.png", 864, 840);
            RenderDiagnostic(root, window.Background, output, "speedboost-movement-150.png", 744, 580, 1.5);
            movementScroll.ScrollToTop();
            model.ResetDefaults();
            model.CursorSpeed = 1200;
            if (model.SpeedBoostMultiplier != FactoryDefaults.Create().SpeedBoostMultiplier) throw new InvalidOperationException("Boost default reset failed.");
            if (model.DPadTapDistance != FactoryDefaults.Create().DPadTapDistance || model.PrecisionPercent != FactoryDefaults.Create().PrecisionPercent) throw new InvalidOperationException("Reset defaults failed.");
            model.UpdateStatus(new(0, 1, false, false, null, true, true, true, true));
            if (!model.OverlayStateText.Contains("Ctrl active") || !model.OverlayStateText.Contains("Shift active"))
                throw new InvalidOperationException("Sticky modifier status is missing.");
            root.UpdateLayout();
            RenderDiagnostic(root, window.Background, output, "settings-window.png", 864, 840);
            RenderDiagnostic(root, window.Background, output, "movement-150.png", 864, 840, 1.5);
            RenderDiagnostic(root, window.Background, output, "movement-compact.png", 744, 580);
            var tabs = (System.Windows.Controls.TabControl)window.FindName("SettingsTabs");
            tabs.SelectedIndex = 1;
            root.Measure(new System.Windows.Size(864, 840)); root.Arrange(new Rect(0, 0, 864, 840)); root.UpdateLayout();
            var keyEditor = (KeybindingsEditor)((System.Windows.Controls.ContentControl)window.FindName("KeybindingsHost")).Content;
            keyEditor.SelectControl(FullTilt.Core.Control.A);
            var keyActions = (System.Windows.Controls.ListBox)keyEditor.FindName("Actions");
            var keySearch = (System.Windows.Controls.TextBox)keyEditor.FindName("Search");
            var heroView = (ControllerBindingsBoard)((System.Windows.Controls.ContentControl)keyEditor.FindName("ControllerBoardHost")).Content;
            heroView.ApplyTemplate();
            heroView.Measure(new System.Windows.Size(1000, 900)); heroView.Arrange(new Rect(0, 0, 1000, 900)); heroView.UpdateLayout();
            heroView.VerifyJoystickTargets();
            foreach (var shoulder in new[] { FullTilt.Core.Control.LB, FullTilt.Core.Control.RB, FullTilt.Core.Control.LT, FullTilt.Core.Control.RT, FullTilt.Core.Control.DPadUp, FullTilt.Core.Control.DPadDown, FullTilt.Core.Control.DPadLeft, FullTilt.Core.Control.DPadRight, FullTilt.Core.Control.L3, FullTilt.Core.Control.R3 })
            {
                var beforeClick = model.Settings;
                var button = Descendants<System.Windows.Controls.Button>(heroView).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Select " + shoulder + " keybinding");
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var target = Descendants<System.Windows.Controls.Button>(heroView).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Press " + shoulder + " diagram target");
                target.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (keyEditor.SelectedControl != shoulder || !ReferenceEquals(beforeClick, model.Settings)) throw new InvalidOperationException("Shoulder hotspot changed settings or selected the wrong control.");
            }
            keyEditor.SelectControl(FullTilt.Core.Control.A);
            // Travel uses only the existing role swap. Clicks retain the ordinary catalog.
            model.Bindings.Single(r => r.Control == Control.L3).Selection = InputAction.Backspace;
            model.Bindings.Single(r => r.Control == Control.R3).Selection = InputAction.Enter;
            var clickMappings = model.Settings.Mappings;
            var clickPies = model.Settings.PieBindings;
            foreach (var stick in new[] { CursorStick.Left, CursorStick.Right })
            {
                var beforeTravelSelection = model.Settings;
                Descendants<System.Windows.Controls.Button>(heroView).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Select " + stick + " joystick travel").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Descendants<System.Windows.Controls.Button>(heroView).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Press " + stick + " joystick travel target").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (keyEditor.SelectedJoystick != stick || !ReferenceEquals(beforeTravelSelection, model.Settings) || keyActions.Items.Count != 2) throw new InvalidOperationException("Travel targets must select movement roles without changing settings.");
                foreach (var role in new[] { "Cursor Movement", "Scrolling" })
                {
                    keyActions.SelectedItem = keyActions.Items.Cast<BindingChoice>().Single(c => c.Label == role);
                    model.Edits.Save(EditTab.Keybindings);
                    var expected = role == "Cursor Movement" ? stick : stick == CursorStick.Left ? CursorStick.Right : CursorStick.Left;
                    if (model.CursorStick != expected || !clickMappings.SequenceEqual(model.Settings.Mappings) || !clickPies.SequenceEqual(model.Settings.PieBindings)) throw new InvalidOperationException("Joystick role change modified click or pie bindings.");
                    if (!model.SaveProfile() || diagnosticStore.Load().Settings.CursorStick != expected) throw new InvalidOperationException("Joystick role did not persist through the existing settings path.");
                }
                RenderDiagnostic(root, window.Background, output, "joystick-travel-" + stick + ".png", 1390, 900);
            }
            foreach (var click in new[] { Control.L3, Control.R3 })
            {
                keyEditor.SelectControl(click);
                if (keyEditor.SelectedJoystick is not null || !keyActions.Items.Cast<BindingChoice>().Any(c => Equals(c.Value, InputAction.Backspace))) throw new InvalidOperationException("Stick clicks lost ordinary action choices.");
            }
            model.ResetDefaults();
            model.CursorSpeed = 1200; keyEditor.SelectControl(FullTilt.Core.Control.A);
            if (keyActions.Items.Groups?.Count != keyActions.Items.Cast<BindingChoice>().Select(c => c.Category).Distinct().Count()) throw new InvalidOperationException("Keybinding categories are missing.");
            var settingsBeforeSearch = model.Settings;
            keySearch.Text = "backspace";
            if (keyActions.Items.Count != 1 || !ReferenceEquals(settingsBeforeSearch, model.Settings)) throw new InvalidOperationException("Keybindings search changed settings or failed filtering.");
            keyActions.SelectedIndex = 0; model.Edits.Save(EditTab.Keybindings);
            if (model.Settings.Mappings[FullTilt.Core.Control.A] != InputAction.Backspace) throw new InvalidOperationException("New action cards failed to use existing binding path.");
            keySearch.Text = "";
            var beforeSelectingControl = model.Settings;
            keyEditor.SelectControl(FullTilt.Core.Control.RT);
            if (!ReferenceEquals(beforeSelectingControl, model.Settings)) throw new InvalidOperationException("Selecting a control changed its binding.");
            foreach (var action in ActionCatalog.PickerEntries)
            {
                File.WriteAllText(Path.Combine(output, "diagnostic-progress.txt"), "Keybindings catalog: " + action.Action);
                keyActions.SelectedItem = keyActions.Items.Cast<BindingChoice>().Single(c => Equals(c.Value, action.Action) && c.Category == action.Category); model.Edits.Save(EditTab.Keybindings);
                if (model.Settings.Mappings[FullTilt.Core.Control.RT] != action.Action || model.Settings.PieBindings.ContainsKey(FullTilt.Core.Control.RT)) throw new InvalidOperationException("Action card assignment failed: " + action.Label);
            }
            var menuChoice = keyActions.Items.Cast<BindingChoice>().First(c => c.Value is Guid);
            keyActions.SelectedItem = menuChoice; model.Edits.Save(EditTab.Keybindings);
            if (model.Settings.PieBindings[FullTilt.Core.Control.RT] != (Guid)menuChoice.Value) throw new InvalidOperationException("Action cards cannot assign pie menus.");
            model.DPadMode = DPadMode.AssignedActions;
            keyEditor.SelectControl(FullTilt.Core.Control.DPadUp);
            keyActions.SelectedItem = keyActions.Items.Cast<BindingChoice>().Single(c => Equals(c.Value, InputAction.ArrowUp)); model.Edits.Save(EditTab.Keybindings);
            if (model.Settings.Mappings[FullTilt.Core.Control.DPadUp] != InputAction.ArrowUp) throw new InvalidOperationException("D-pad action cards failed.");
            model.ResetDefaults();
            model.CursorSpeed = 1200; keyEditor.SelectControl(FullTilt.Core.Control.A);
            var leftClick = keyActions.Items.Cast<BindingChoice>().Single(c => Equals(c.Value, InputAction.LeftClick));
            keyActions.SelectedItem = leftClick;
            RenderDiagnostic(root, window.Background, output, "keybindings-3.5.png", 1390, 900);
            var currentCategory = Descendants<System.Windows.Controls.Expander>(keyActions).Single(c => Equals(c.Header, "Pointer & Click"));
            if (currentCategory.IsExpanded || keyActions.SelectedItem is not null) throw new InvalidOperationException("Assigning an action changed the independent palette category or selection.");
            var beforeCategories = model.Settings;
            currentCategory.IsExpanded = false;
            keySearch.Text = "backspace";
            RenderDiagnostic(root, window.Background, output, "keybindings-4.0-search.png", 1390, 900);
            if (keyActions.Items.Count != 1 || !Descendants<System.Windows.Controls.Expander>(keyActions).All(c => c.IsExpanded) || !ReferenceEquals(beforeCategories, model.Settings)) throw new InvalidOperationException("Category search did not reveal read-only matches.");
            keySearch.Text = "";
            RenderDiagnostic(root, window.Background, output, "keybindings-4.0-categories.png", 1390, 900);
            if (Descendants<System.Windows.Controls.Expander>(keyActions).Single(c => Equals(c.Header, "Pointer & Click")).IsExpanded) throw new InvalidOperationException("Search lost the user's collapsed category state.");
            model.Bindings.Single(r => r.Control == Control.LT).Selection = InputAction.SpeedBoostHold;
            model.Bindings.Single(r => r.Control == Control.L3).Selection = InputAction.ToggleKeyboard;
            model.Bindings.Single(r => r.Control == Control.R3).Selection = InputAction.Undo;
            model.DPadMode = DPadMode.AssignedActions;
            foreach (var (control, action) in new[] { (Control.DPadUp, InputAction.ArrowUp), (Control.DPadLeft, InputAction.ArrowLeft), (Control.DPadDown, InputAction.ArrowDown), (Control.DPadRight, InputAction.ArrowRight) })
                model.DPadBindings.Single(r => r.Control == control).Selection = action;
            foreach (var actionCategory in Descendants<System.Windows.Controls.Expander>(keyActions)) actionCategory.IsExpanded = false;
            keyEditor.SelectControl(Control.LT);
            RenderDiagnostic(root, window.Background, output, "keybindings-4.0-reference.png", 1570, 980);
            RenderDiagnostic(root, window.Background, output, "keybindings-4.0-150.png", 1390, 900, 1.5);
            if (Descendants<System.Windows.Controls.Expander>(keyActions).Any(c => c.IsExpanded) || keyActions.SelectedItem is not null || ((System.Windows.Controls.TextBlock)keyEditor.FindName("CurrentAssignment")).Text != "Current: Speed Boost While Held") throw new InvalidOperationException("Target details failed to update independently of the collapsed palette.");
            var assignmentRows = Descendants<System.Windows.Controls.Button>(heroView).Where(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Select ", StringComparison.Ordinal)).ToArray();
            if (assignmentRows.Length != 16 || assignmentRows.Select(System.Windows.Controls.Canvas.GetLeft).Distinct().Count() != 2) throw new InvalidOperationException("Diagram assignments are not aligned into left/right columns.");
            RenderDiagnostic(root, window.Background, output, "keybindings-3.5-compact.png", 744, 650);
            if (((FrameworkElement)keyEditor.FindName("ControllerBoardHost")).Visibility != Visibility.Visible || ((FrameworkElement)keyEditor.FindName("ControlPicker")).Visibility != Visibility.Collapsed) throw new InvalidOperationException("Keybindings must retain the controller and every selectable target.");
            model.ResetDefaults();
            model.CursorSpeed = 1200; keyEditor.SelectControl(Control.A);
            tabs.SelectedIndex = 3;
            root.Measure(new System.Windows.Size(864, 840)); root.Arrange(new Rect(0, 0, 864, 840)); root.UpdateLayout();
            if (!Descendants<FrameworkElement>(root).Any(e => e.Name == "BindingDetails") || Descendants<FrameworkElement>(root).Any(e => e.Name == "KeybindingsHost" || e.Name == "ControllerCard")) throw new InvalidOperationException("Button Assignment must contain only the full mapping editor.");
            var bindingBoxes = Descendants<System.Windows.Controls.ComboBox>(root).Where(box => box.DataContext is BindingRow).ToArray();
            var expectedBindings = model.IsDPadActionMode ? 14 : 10;
            if (bindingBoxes.Length != expectedBindings) throw new InvalidOperationException($"Expected {expectedBindings} physical binding choices; found {bindingBoxes.Length}.");
            var l3 = bindingBoxes.Single(box => ((BindingRow)box.DataContext).Control == Control.L3);
            l3.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedValueProperty, InputAction.DragLock);
            if (model.Settings.Mappings[Control.L3] != InputAction.DragLock) throw new InvalidOperationException("Categorized L3 binding failed.");
            foreach (var definition in ActionCatalog.All)
            {
                l3.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedValueProperty, definition.Action);
                if (model.Settings.Mappings[Control.L3] != definition.Action) throw new InvalidOperationException("Action picker rejected " + definition.Id);
            }
            l3.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedValueProperty, InputAction.ToggleKeyboard);
            model.UpdateStatus(new(0, 15, false, false, null, AltActive: true));
            if (model.OverlayStateText != "Alt active") throw new InvalidOperationException("Sticky Alt indicator missing.");
            tabs.SelectedIndex = 4; root.UpdateLayout();
            File.WriteAllText(Path.Combine(output, "update3.2.txt"), Update32Diagnostics.VerifyShortcut(model,
                Descendants<System.Windows.Controls.CheckBox>(root).ToArray(),
                Descendants<System.Windows.Controls.Button>(root).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Apply pause shortcut"), output)
                + "\n" + Update32Diagnostics.VerifyCaret(boostNumber.Template));
            root.UpdateLayout();
            var beforeRefresh = model.Settings;
            model.UpdateStatus(new(0, 15, false, false, null, true, true, true, true));
            tabs.SelectedIndex = 0; tabs.SelectedIndex = 1; tabs.SelectedIndex = 2; tabs.SelectedIndex = 4; tabs.SelectedIndex = 3; root.UpdateLayout();
            if (!ReferenceEquals(beforeRefresh, model.Settings)) throw new InvalidOperationException("Status or tab refresh changed settings.");
            if (!l3.IsTextSearchEnabled || l3.MaxDropDownHeight > 390) throw new InvalidOperationException("Action picker navigation or height limit missing.");
            // Render the actual popup content without opening its HWND or changing desktop focus.
            l3.ApplyTemplate();
            File.AppendAllText(Path.Combine(output, "ui-scrolling.txt"), "\n" + UiScrollingDiagnostics.Verify(l3));
            tabs.SelectedIndex = 1; root.UpdateLayout();
            File.AppendAllText(Path.Combine(output, "ui-scrolling.txt"), "\n" + UiScrollingDiagnostics.VerifyActions(keyActions, root));
            tabs.SelectedIndex = 3; root.UpdateLayout();
            var popup = (System.Windows.Controls.Primitives.Popup)l3.Template.FindName("PART_Popup", l3);
            var popupContent = (FrameworkElement)popup.Child;
            RenderDiagnostic(popupContent, System.Windows.Media.Brushes.White, output, "action-picker.png", 310, 390);
            var actionScroll = Descendants<System.Windows.Controls.ScrollViewer>(popupContent).First();
            if (actionScroll.ScrollableHeight <= 0 || ((BindingRow)l3.DataContext).Options.Groups?.Count != ActionCatalog.Categories.Count + 1)
                throw new InvalidOperationException("Expanded action picker needs action categories plus pie menus.");
            actionScroll.ScrollToVerticalOffset(actionScroll.ScrollableHeight * .4);
            popupContent.UpdateLayout();
            RenderDiagnostic(popupContent, System.Windows.Media.Brushes.White, output, "action-picker-middle.png", 310, 390);
            actionScroll.ScrollToEnd(); popupContent.UpdateLayout();
            RenderDiagnostic(popupContent, System.Windows.Media.Brushes.White, output, "action-picker-end.png", 310, 390);
            model.Bindings.Single(row => row.Control == Control.LB).Action = InputAction.StickyCtrl;
            model.Bindings.Single(row => row.Control == Control.RB).Action = InputAction.StickyShift;
            model.Bindings.Single(row => row.Control == Control.R3).Action = InputAction.Copy;
            model.DPadMode = DPadMode.AssignedActions;
            model.DPadBindings.Single(row => row.Control == Control.DPadDown).Action = InputAction.ReleaseCancel;
            model.DPadBindings.Single(row => row.Control == Control.DPadUp).Action = InputAction.VolumeUp;
            model.DPadBindings.Single(row => row.Control == Control.DPadLeft).Action = InputAction.PreviousTrack;
            model.DPadBindings.Single(row => row.Control == Control.DPadRight).Action = InputAction.PlayPause;
            RenderDiagnostic(root, window.Background, output, "button-assignments.png", 864, 840);
            RenderDiagnostic(root, window.Background, output, "assignments-150.png", 864, 840, 1.5);
            if (Descendants<System.Windows.Controls.ComboBox>(root).Count(box => box.DataContext is BindingRow && box.IsVisible) != 0)
            {
                // Offscreen controls have no PresentationSource and are not actually visible.
                throw new InvalidOperationException("Diagnostic unexpectedly showed a settings window.");
            }
            RenderDiagnostic(root, window.Background, output, "assignments-compact.png", 744, 580);
            var assignmentScroll = Descendants<System.Windows.Controls.ScrollViewer>(root).First(view => view.Content is System.Windows.Controls.Border);
            assignmentScroll.ScrollToEnd(); root.UpdateLayout();
            RenderDiagnostic(root, window.Background, output, "assignments-help-compact.png", 744, 580);
            tabs.SelectedIndex = 0;
            RenderDiagnostic(root, window.Background, output, "movement-actions-mode.png", 864, 840);
            if (tap.IsEnabled) throw new InvalidOperationException("D-pad numeric controls should be disabled in action mode.");

            tabs.SelectedIndex = 2;
            var editor = (PieEditor)((System.Windows.Controls.ContentControl)window.FindName("PieEditorHost")).Content;
            editor.Discard();
            editor.Confirm = (_, _) => MessageBoxResult.Yes;
            RenderDiagnostic(root, window.Background, output, "pie-editor-initial.png", 864, 840);
            EditorNewMenu();
            void EditorNewMenu()
            {
                var button = Descendants<System.Windows.Controls.Button>(editor).Single(b => Equals(b.Content, "New pie menu"));
                if (button.ContextMenu is not null || !Descendants<System.Windows.Controls.Button>(editor).Any(b => Equals(b.Content, "Save changes")) || !Descendants<System.Windows.Controls.Button>(editor).Any(b => Equals(b.Content, "Discard"))) throw new InvalidOperationException("Pie editor exposes preset creation or is missing Save/Discard.");
                button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (editor.Draft!.Dirty || editor.Draft.Menu.SegmentCount != 4 || editor.Draft.OpenWith is not null || editor.Draft.Menu.PresetId is not null || editor.Draft.Menu.Slots.Any(a => a != InputAction.None) || model.Settings.PieMenus.Any(m => m.Id == editor.Draft.Menu.Id)) throw new InvalidOperationException("New menu is not an empty detached four-segment menu.");
                if (!editor.Apply()) throw new InvalidOperationException("New menu could not be saved.");
            }
            RenderDiagnostic(root, window.Background, output, "pie-editor-empty.png", 864, 840);
            void EditorButton(string name) => Descendants<System.Windows.Controls.Button>(editor).Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            T EditorControl<T>(string name) where T : DependencyObject => Descendants<T>(editor).Single(control => System.Windows.Automation.AutomationProperties.GetName(control) == name);
            var nameBox = EditorControl<System.Windows.Controls.TextBox>("Menu name");
            var actionList = EditorControl<System.Windows.Controls.ListBox>("Supported pie actions");
            var countBox = EditorControl<System.Windows.Controls.ComboBox>("Segment count");
            var bindingBox = EditorControl<System.Windows.Controls.ComboBox>("Open with controller binding");
            File.AppendAllText(Path.Combine(output, "ui-scrolling.txt"), "\n" + UiScrollingDiagnostics.VerifyPie(editor, actionList, root));
            var categories = Descendants<System.Windows.Controls.Expander>(actionList).ToArray();
            if (categories.Length < 4 || !categories.Any(c => Equals(c.Header, "Window Controls"))) throw new InvalidOperationException("Pie action categories missing.");
            var category = categories.First(); var expandedHeight = category.ActualHeight;
            var draftBeforeCategory = editor.Draft!.Menu;
            category.IsExpanded = false; root.UpdateLayout();
            if (category.ActualHeight >= expandedHeight || !ReferenceEquals(draftBeforeCategory, editor.Draft.Menu)) throw new InvalidOperationException("Category collapse failed or modified assignments.");
            category.IsExpanded = true; root.UpdateLayout();
            var pieSearch = EditorControl<System.Windows.Controls.TextBox>("Search pie actions");
            var pieActionScroll = Descendants<System.Windows.Controls.ScrollViewer>(actionList).First();
            pieActionScroll.ScrollToEnd(); root.UpdateLayout();
            pieSearch.Text = "copy"; root.UpdateLayout();
            if (actionList.Items.Count != 1 || pieActionScroll.VerticalOffset != 0 || !ReferenceEquals(draftBeforeCategory, editor.Draft.Menu)) throw new InvalidOperationException("Pie action search failed after scrolling or changed its draft.");
            RenderDiagnostic(root, window.Background, output, "pie-search-copy.png", 1390, 900);
            pieSearch.Text = "Media";
            if (actionList.Items.Count == 0 || actionList.Items.Cast<ActionDefinition>().Any(a => a.Category != "Media") || !ReferenceEquals(draftBeforeCategory, editor.Draft.Menu)) throw new InvalidOperationException("Category search changed the draft or returned unrelated actions.");
            pieSearch.Text = "";
            nameBox.Text = "Desktop tools";
            actionList.SelectedItem = ActionCatalog.Get(InputAction.Copy); editor.Apply();
            if (editor.Draft!.Dirty || editor.Draft.Menu.Slots[0] != InputAction.Copy || model.Settings.PieMenus.Single(m => m.Id == editor.Draft.Menu.Id).Name != "Desktop tools") throw new InvalidOperationException("Action and rename did not save after Apply.");
            countBox.SelectedItem = 8;
            editor.Diagram.TryDrop(7, new DataObject("FullTilt.PieAction", InputAction.Paste)); editor.Apply();
            if (editor.Draft!.Dirty || editor.SelectedAction != InputAction.Paste || model.Settings.PieMenus.Single(m => m.Id == editor.Draft.Menu.Id).Slots[7] != InputAction.Paste) throw new InvalidOperationException("Save reset the selected segment or failed to save its action.");
            string? resizePrompt = null;
            editor.Confirm = (message, _) => { resizePrompt = message; return MessageBoxResult.No; };
            countBox.SelectedItem = 4;
            if (editor.Draft.Menu.SegmentCount != 8 || resizePrompt?.Contains("Paste") != true) throw new InvalidOperationException("Resize did not disclose removed assignments or respect cancellation.");
            editor.Confirm = (_, _) => MessageBoxResult.Yes; countBox.SelectedItem = 4;
            editor.Confirm = (_, _) => MessageBoxResult.No;
            bindingBox.SelectedItem = bindingBox.Items.Cast<PieControlOption>().Single(option => option.Control == Control.A);
            if (editor.Draft.OpenWith is not null || model.Settings.PieBindings.ContainsKey(Control.A)) throw new InvalidOperationException("Binding conflict cancellation changed saved settings.");
            editor.Confirm = (_, _) => MessageBoxResult.Yes;
            bindingBox.SelectedItem = bindingBox.Items.Cast<PieControlOption>().Single(option => option.Control == Control.A);
            var menuId = editor.Draft!.Menu.Id; editor.Apply();
            if (model.Settings.PieBindings[Control.A] != menuId || model.Settings.PieMenus.Single(m => m.Id == menuId).Slots[0] != InputAction.Copy) throw new InvalidOperationException("Menu and binding were not saved together.");
            if (model.Bindings.Single(row => row.Control == Control.A).Selection is not Guid selectedMenu || selectedMenu != menuId) throw new InvalidOperationException("Open with uses a different binding system.");
            nameBox.Text = "Automatic rename";
            editor.Confirm = (_, _) => MessageBoxResult.Yes;
            if (!window.TryResolvePieDraft() || !editor.SelectMenu(model.Settings.PieMenus.First(m => m.Id != menuId).Id) || model.Settings.PieMenus.Single(m => m.Id == menuId).Name != "Automatic rename") throw new InvalidOperationException("Navigation did not flush pending rename.");
            editor.SelectMenu(menuId);
            editor.Confirm = (_, _) => MessageBoxResult.Yes;
            EditorButton("Rename"); EditorButton("Clone"); editor.Apply();
            if (editor.Draft!.Dirty || editor.Draft.Menu.Id == menuId || !model.Settings.PieMenus.Any(m => m.Id == editor.Draft.Menu.Id)) throw new InvalidOperationException("Clone was not saved automatically.");
            editor.SelectMenu(menuId); EditorButton("Delete"); editor.Apply();
            if (model.Settings.PieMenus.Any(m => m.Id == menuId) || model.Settings.PieBindings.ContainsKey(Control.A)) throw new InvalidOperationException("Confirmed deletion was not saved or left a binding.");
            editor.BeginNew("media");
            actionList.SelectedItem = ActionCatalog.Get(InputAction.Copy); countBox.SelectedItem = 8;
            EditorButton("Restore defaults");
            if (editor.Draft!.Menu.SegmentCount != FactoryDefaults.Create().PieMenus[0].SegmentCount || editor.Draft.Menu.Slots[0] != InputAction.PlayPause) throw new InvalidOperationException("Preset restore failed.");
            RenderDiagnostic(root, window.Background, output, "pie-editor-populated.png", 864, 840);
            RenderDiagnostic(root, window.Background, output, "pie-editor-laptop.png", 864, 680);
            RenderDiagnostic(root, window.Background, output, "pie-editor-compact.png", 744, 580);
            RenderDiagnostic(root, window.Background, output, "pie-editor-150.png", 864, 680, 1.5);
            if (Descendants<System.Windows.Controls.ScrollViewer>(editor).Any(scroll => scroll.ScrollableWidth > 1)) throw new InvalidOperationException("Pie editor needs horizontal scrolling.");
            if (Descendants<FrameworkElement>(root).Any(e => e.Name == "SharedSettingsFooter" || e.Name == "ControllerCard")) throw new InvalidOperationException("Settings controls leaked into Pie Menus.");
            editor.Discard(); editor.BeginNew("windows");
            RenderDiagnostic(root, window.Background, output, "pie-editor-3.7-wide.png", 1600, 960);
            if (Descendants<System.Windows.Controls.ListBox>(editor).Any(list => list.ActualHeight < 80)) throw new InvalidOperationException("Wide pie lists have no usable viewport.");
            var iconSheet = new System.Windows.Controls.WrapPanel { Width = 1120 };
            foreach (var action in ActionCatalog.All)
            {
                var cell = new System.Windows.Controls.StackPanel { Width = 140, Height = 90 };
                cell.Children.Add(new UiIcon { Kind = action.Action.ToString(), Width = 32, Height = 32, Margin = new Thickness(0, 8, 0, 7) });
                cell.Children.Add(new System.Windows.Controls.TextBlock { Text = action.Label, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = ThemePalette.Get("#161B17") });
                iconSheet.Children.Add(cell);
            }
            RenderDiagnostic(iconSheet, ThemePalette.Get("#FFFFFF"), output, "action-icons-light.png", 1120, (int)Math.Ceiling(ActionCatalog.All.Count / 8.0) * 90);
            if (Descendants<UiIcon>(iconSheet).Any(icon => icon.UsesFallback)) throw new InvalidOperationException("An action has no specific icon.");
            ThemePalette.Apply(true);
            RenderDiagnostic(iconSheet, ThemePalette.Get("#FFFFFF"), output, "action-icons-dark.png", 1120, (int)Math.Ceiling(ActionCatalog.All.Count / 8.0) * 90);
            ThemePalette.Apply(false);
            var pieOverlay = new PieOverlay();
            var pieForeground = NativeMethods.GetForegroundWindow();
            var pieHandle = new WindowInteropHelper(pieOverlay).EnsureHandle();
            var pieStyles = (long)NativeMethods.GetWindowLongPtr(pieHandle, -20);
            if ((pieStyles & StateIndicator.NonInteractiveStyles) != StateIndicator.NonInteractiveStyles || pieOverlay.ShowActivated || pieOverlay.ShowInTaskbar || pieOverlay.IsHitTestVisible || pieOverlay.IsVisible) throw new InvalidOperationException("Pie overlay non-interactive styles missing.");
            if (NativeMethods.GetForegroundWindow() != pieForeground) throw new InvalidOperationException("Hidden pie HWND changed foreground.");
            File.WriteAllText(Path.Combine(output, "pie-transparency.txt"), PieBackdropDiagnostics.Verify(pieHandle, pieOverlay, output));
            var namePreview = new PieDiagram();
            foreach (var menuName in new[] { "Keyboard", "A very long custom menu name that must remain inside the center" })
            {
                namePreview.Render(PieMenu.Create(menuName, 8), -1);
                RenderDiagnostic(namePreview, window.Background, output, menuName == "Keyboard" ? "pie-name.png" : "pie-name-long.png", 440, 440);
                var nameLabel = Descendants<System.Windows.Controls.TextBlock>(namePreview).Single(text => text.Text == menuName);
                if (nameLabel.ActualHeight > 74 || nameLabel.ActualWidth > 86) throw new InvalidOperationException("Pie name escaped its center bounds.");
            }
            foreach (var count in Enumerable.Range(2, 7))
            {
                var menu = PieMenu.Create("Geometry " + count, count);
                for (var slot = 0; slot < count; slot++) menu = menu.Assign(slot, slot % 2 == 0 ? InputAction.MinimizeWindow : InputAction.ToggleKeyboard);
                pieOverlay.Diagram.Render(menu, 0);
                if (pieOverlay.Diagram.HitSlot(new System.Windows.Point(200, 200)) != -1 || pieOverlay.Diagram.HitSlot(new System.Windows.Point(200, 1)) != -1) throw new InvalidOperationException("Pie center/halo is selectable.");
                for (var slot = 0; slot < count; slot++)
                {
                    var angle = PieGeometry.Center(count, slot) * Math.PI / 180;
                    foreach (var radius in new[] { 66.0, 122.0, 170.0 })
                        if (pieOverlay.Diagram.HitSlot(new System.Windows.Point(200 + Math.Sin(angle) * radius, 200 - Math.Cos(angle) * radius)) != slot) throw new InvalidOperationException("Rounded pie hit target does not match visible wedge.");
                    var gap = (PieGeometry.Center(count, slot) + PieGeometry.Width(count) / 2) * Math.PI / 180;
                    if (pieOverlay.Diagram.HitSlot(new System.Windows.Point(200 + Math.Sin(gap) * 122, 200 - Math.Cos(gap) * 122)) != -1) throw new InvalidOperationException("Pie gap is selectable.");
                }
                RenderDiagnostic(pieOverlay.Diagram, System.Windows.Media.Brushes.Transparent, output, "pie-overlay-" + count + ".png", 440, 440);
            }
            pieOverlay.Diagram.Render(editor.Draft.Menu, 0, mouseHeld: true);
            RenderDiagnostic(pieOverlay.Diagram, System.Windows.Media.Brushes.Transparent, output, "pie-overlay-drag.png", 440, 440, 1.5);
            ThemePalette.Apply(true);
            pieOverlay.Diagram.Render(editor.Draft.Menu, 0);
            RenderDiagnostic(pieOverlay.Diagram, System.Windows.Media.Brushes.Transparent, output, "pie-overlay-dark.png", 440, 440);
            ThemePalette.Apply(false);
            pieOverlay.Close();
            editor.Discard(); editor.BeginNew(null); nameBox.Text = "Long named menu for editing and switching between application windows";
            for (var slot = 0; slot < 4; slot++) editor.Diagram.TryDrop(slot, new DataObject("FullTilt.PieAction", InputAction.MinimizeWindow));
            RenderDiagnostic(root, window.Background, output, "pie-editor-long-labels.png", 744, 580, 1.5);
            var beforeInvalid = editor.Draft!.Menu;
            if (editor.Diagram.TryDrop(0, new DataObject("FullTilt.PieAction", InputAction.PrecisionHold)) || !ReferenceEquals(beforeInvalid, editor.Draft.Menu)) throw new InvalidOperationException("Invalid drop modified the draft.");
            EditorButton("Clear all actions"); if (editor.Draft.Menu.Slots.Any(action => action != InputAction.None)) throw new InvalidOperationException("Custom clear failed.");
            editor.Discard();

            var protectedStore = new SettingsStore(Path.Combine(output, "protected-settings"));
            Directory.CreateDirectory(protectedStore.DirectoryPath);
            const string futureSettings = "{\"Version\":99}";
            File.WriteAllText(protectedStore.FilePath, futureSettings);
            var protectedLoad = protectedStore.Load();
            using (var protectedModel = new SettingsViewModel(protectedLoad.Settings, protectedStore, null, protectedLoad.Warning))
            {
                var protectedDraft = new PieDraft(PieMenu.Create("Unsaved", 4), protectedModel.Settings, true);
                protectedDraft.Assign(0, InputAction.Copy); protectedDraft.OpenWith = FullTilt.Core.Control.A;
                if (protectedModel.ApplyPie(protectedDraft) || protectedModel.Settings.PieMenus.Count != 0 || !protectedDraft.Dirty || File.ReadAllText(protectedStore.FilePath) != futureSettings)
                    throw new InvalidOperationException("Failed Apply must retain the draft and preserve runtime settings and the file.");
                var failedEditor = new PieEditor(protectedModel);
                failedEditor.Confirm = (_, _) => MessageBoxResult.Yes;
                if (!failedEditor.BeginNew(null) || failedEditor.Apply() || !protectedModel.Edits.Dirty(EditTab.PieMenus) || !failedEditor.SelectMenu(Guid.NewGuid()) || failedEditor.TryResolvePending() || File.ReadAllText(protectedStore.FilePath) != futureSettings) throw new InvalidOperationException("Failed Save lost edits, allowed navigation, or changed protected settings.");
                RenderDiagnostic(failedEditor, window.Background, output, "pie-autosave-failure.png", 1500, 850);
                if (!Descendants<System.Windows.Controls.TextBlock>(failedEditor).Any(t => t.Text.StartsWith("Could not save."))) throw new InvalidOperationException("Save failure is not visible.");
            }

            var indicator = new StateIndicator();
            var foreground = NativeMethods.GetForegroundWindow();
            var handle = new WindowInteropHelper(indicator).EnsureHandle(); // Hidden HWND only; never show or inject.
            var styles = (long)NativeMethods.GetWindowLongPtr(handle, -20);
            if ((styles & StateIndicator.NonInteractiveStyles) != StateIndicator.NonInteractiveStyles || indicator.ShowActivated || indicator.IsVisible)
                throw new InvalidOperationException("Indicator must be hidden, non-activating and click-through.");
            if (NativeMethods.GetForegroundWindow() != foreground) throw new InvalidOperationException("Hidden indicator changed foreground focus.");
            indicator.StateText.Text = "Precision · Drag locked · Ctrl active · Shift active · Paused";
            RenderDiagnostic((FrameworkElement)indicator.Content, System.Windows.Media.Brushes.Transparent, output, "state-indicator.png", 480, 42);
            indicator.Close();
            tabs.SelectedIndex = 4;
            root.Measure(new System.Windows.Size(864, 840)); root.Arrange(new Rect(0, 0, 864, 840)); root.UpdateLayout();
            var shortcutCard = (FrameworkElement)window.FindName("PauseShortcutCard");
            if (!((FrameworkElement)window.FindName("SettingsSections")).IsAncestorOf(shortcutCard) || Descendants<System.Windows.Controls.Expander>(root).Any()) throw new InvalidOperationException("Settings must show all sections without expanders.");
            root.UpdateLayout();
            RenderDiagnostic(root, window.Background, output, "pause-shortcut.png", 864, 840);
            RenderDiagnostic(root, window.Background, output, "settings-tab-compact.png", 744, 580);
            if (tabs.Items.Count != 5 || !Descendants<FrameworkElement>(root).Any(e => e.Name == "ControllerCard") || !Descendants<FrameworkElement>(root).Any(e => e.Name == "SharedSettingsFooter")) throw new InvalidOperationException("Settings tab is missing controls.");
            RenderDiagnostic(root, window.Background, output, "settings-3.6-light.png", 1390, 980);
            File.WriteAllText(Path.Combine(output, "app-preferences.txt"), AppPreferencesDiagnostics.Verify(output));
            ((AppPreferencesViewModel)((FrameworkElement)window.FindName("AppPreferencesHost")).DataContext).Theme = "Dark";
            RenderDiagnostic(root, window.Background, output, "settings-3.6-dark.png", 1390, 980);
            tabs.SelectedIndex = 1; RenderDiagnostic(root, window.Background, output, "keybindings-3.6-dark.png", 1390, 980);
            tabs.SelectedIndex = 0; RenderDiagnostic(root, window.Background, output, "movement-3.6-dark.png", 864, 840);
            tabs.SelectedIndex = 2; RenderDiagnostic(root, window.Background, output, "pie-editor-3.7-dark.png", 1390, 980);
            tabs.SelectedIndex = 4;
            var themedPrompt = new ThemedDialog("Pie menu changes", "Replace this occupied controller binding? Other bindings will be preserved.", MessageBoxButton.YesNoCancel);
            RenderDiagnostic((FrameworkElement)themedPrompt.Content, themedPrompt.Background, output, "themed-confirmation.png", 480, 260);
            Descendants<System.Windows.Controls.Button>((DependencyObject)themedPrompt.Content).Single(b => Equals(b.Content, "Cancel")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (themedPrompt.Result != MessageBoxResult.Cancel) throw new InvalidOperationException("Themed dialog canceled incorrectly.");
            themedPrompt.Close();
            var themedName = new ThemedDialog("Rename profile", "Choose a unique name for this FullTilt setup.", MessageBoxButton.OKCancel, "Drawing", name => string.IsNullOrWhiteSpace(name) ? "Enter a name." : null);
            RenderDiagnostic((FrameworkElement)themedName.Content, themedName.Background, output, "themed-profile-name.png", 480, 300);
            var renameButton = Descendants<System.Windows.Controls.Button>((DependencyObject)themedName.Content).Single(b => Equals(b.Content, "Rename"));
            themedName.NameInput!.Text = "";
            if (renameButton.IsEnabled) throw new InvalidOperationException("Empty profile name can be accepted.");
            themedName.NameInput.Text = "New name";
            renameButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            if (!renameButton.IsEnabled || themedName.Result != MessageBoxResult.OK) throw new InvalidOperationException("Valid profile name rejected.");
            themedName.Close();
            ThemePalette.Apply(false);
            if (((System.Windows.Controls.TabItem)tabs.Items[3]).Visibility != Visibility.Collapsed || tabs.Items.Cast<System.Windows.Controls.TabItem>().Count(tab => tab.Visibility == Visibility.Visible) != 4) throw new InvalidOperationException("Button Assignment must remain hidden while four navigation tabs remain available.");
            // Visible pages expand beside the same far-left navigation; the retained hidden assignment page is checked only by diagnostics.
            double? ordinaryWidth = null;
            foreach (var pageIndex in new[] { 3, 0, 1, 4, 2 })
            {
                tabs.SelectedIndex = pageIndex;
                RenderDiagnostic(root, window.Background, output, "panel-layout-" + pageIndex + ".png", 1390, 900);
                var heroRegion = (FrameworkElement)window.FindName("HeroRegion");
                var fullWidth = pageIndex is 0 or 1 or 2 or 4;
                if ((heroRegion.Visibility == Visibility.Visible) != !fullWidth) throw new InvalidOperationException("Controller artwork visibility differs from panel layout.");
                if (!fullWidth)
                {
                    var contentHost = (System.Windows.Controls.ContentPresenter)tabs.Template.FindName("PART_SelectedContentHost", tabs);
                    ordinaryWidth ??= contentHost.ActualWidth;
                    if (contentHost.TranslatePoint(new System.Windows.Point(0,0), root).X < heroRegion.TranslatePoint(new System.Windows.Point(heroRegion.ActualWidth,0), root).X)
                        throw new InvalidOperationException("Controller overlaps the right content panel.");
                    if (Math.Abs(contentHost.ActualWidth - ordinaryWidth.Value) > 1) throw new InvalidOperationException("Ordinary pages do not share the same right panel.");
                }
                else
                {
                    var host = (System.Windows.Controls.ContentPresenter)tabs.Template.FindName("PART_SelectedContentHost", tabs);
                    if (host.ActualWidth <= ordinaryWidth!.Value || host.TranslatePoint(new System.Windows.Point(0, 0), root).X > tabs.TranslatePoint(new System.Windows.Point(140, 0), root).X) throw new InvalidOperationException("Settings/Pie Menus lost full content width beside the left navigation.");
                }
            }
            foreach (var pageIndex in new[] { 0, 3, 4 })
            {
                tabs.SelectedIndex = pageIndex;
                RenderDiagnostic(root, window.Background, output, "panel-narrow-" + pageIndex + ".png", 1080, 760);
                if (Descendants<System.Windows.Controls.ScrollViewer>(tabs).Any(scroll => scroll.ScrollableWidth > 1)) throw new InvalidOperationException("Right panel requires horizontal scrolling.");
            }
            File.WriteAllText(Path.Combine(output, "profiles.txt"), ProfileDiagnostics.Verify(output, boostNumber.Template));
            File.WriteAllText(Path.Combine(output, "keybindings-regressions.txt"), KeybindingsRegressionDiagnostics.Verify(output));
            File.WriteAllText(Path.Combine(output, "settings-4.1.txt"), SettingsPageDiagnostics.Verify(output));
            File.WriteAllText(Path.Combine(output, "movement-reference.txt"), MovementPageDiagnostics.Verify(output));
            File.WriteAllText(Path.Combine(output, "whole-numbers.txt"), WholeNumberDiagnostics.Verify(output));
            File.WriteAllText(Path.Combine(output, "pie-size.txt"), PieSizeDiagnostics.Verify(output));
            File.WriteAllText(Path.Combine(output, "motion.txt"), MotionDiagnostics.Verify(output));
            File.WriteAllText(Path.Combine(output, "display-layout.txt"), DisplayLayoutDiagnostics.Verify(output));
            if (WindowsInputSink.NativeInputSize != 40) throw new InvalidOperationException("Unexpected x64 INPUT layout.");
            File.WriteAllText(Path.Combine(output, "smoke-test.txt"), $"PASS: FullTilt {typeof(App).Assembly.GetName().Version}\nArchitecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}\nFour visible navigation tabs, retained hidden Button Assignment and separate Settings controls, all {ActionCatalog.All.Count} action selections, action/pie groups, tab/status preservation, numeric validation, defaults and disabled D-pad controls checked.\nMovement and assignment tabs rendered offscreen at normal/compact sizes and 150%; closed picker content rendered at three scroll positions.\nHidden indicator HWND styles and unchanged foreground checked; all five indicator states rendered offscreen.\nPie categories: expand/collapse and search preserve drafts; visible Rename/Clone/Delete exercised. Pie drafts: empty one-click creation without presets and explicit Save/Discard; inline rename, actions, binding conflicts, navigation/close flush, resize confirmation, clone/delete, defaults/clear and invalid drops checked. All 2–8 segment counts rendered. Empty/populated/long-label editor and overlay states rendered; hidden pie HWND styles checked. Live focus and controller interaction remain manual checks.\nSpeed Boost: whole numeric/manual and slider synchronization, invalid-input recovery, advanced-value persistence and overlay exclusion checked; other indicators preserved.\nKeybindings: search is read-only; shoulder/trigger, individual D-pad and stick callouts/targets, grouped control selection, all {ActionCatalog.All.Count} actions, pie bindings and D-pad cards verified through existing settings handlers.\nNative INPUT size: {WindowsInputSink.NativeInputSize}\nLive input emulation: NEVER STARTED\n");
            Shutdown(0);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(output, "smoke-test-error.txt"), ex.ToString()); } catch (IOException) { }
            Shutdown(1);
        }
    }

    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void RenderDiagnostic(FrameworkElement root, System.Windows.Media.Brush backgroundBrush, string output, string name, int width, int height, double scale = 1)
    {
        root.Measure(new System.Windows.Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.Background); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(backgroundBrush, null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name)); encoder.Save(file);
    }
}
