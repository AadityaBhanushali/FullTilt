using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows;

namespace FullTilt;

public partial class MainWindow : Window
{
    private readonly SettingsViewModel _model;
    public bool AllowClose { get; set; }
    public event Action? HiddenToTray;
    internal event Action<string>? FileAction;
    internal void InvokeFileAction(string command) { if (command == "tour") StartTour(); else FileAction?.Invoke(command); }
    internal Action<bool>? ShowFileMenuForTour { get; set; }
    internal Func<System.Windows.Media.Visual, Rect>? FileMenuBounds { get; set; }
    private OnboardingTour? _tour;
    private AppPreferencesViewModel? _tourPreferences;
    internal bool TourActive => _tour is { IsOpen: true };
    internal OnboardingTour? Tour => _tour;
    internal void StartTour()
    {
        if (TourActive || !IsVisible || WindowState == WindowState.Minimized || System.Windows.Documents.AdornerLayer.GetAdornerLayer((UIElement)Content) is not { } layer) return;
        if (!TryResolvePieDraft()) return;
        _model.StopShortcutRecording();
        var tour = _tour = new OnboardingTour(this, _model, completed => { if (completed) _tourPreferences?.CompleteOnboarding(); });
        tour.Start(layer);
    }
    private void TryAutoTour()
    {
        if (_tourPreferences?.OnboardingPending == true && !TourActive && IsVisible && WindowState != WindowState.Minimized)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { if (_tourPreferences.OnboardingPending) StartTour(); }));
    }
    public MainWindow(SettingsViewModel model, AppPreferencesViewModel? preferences = null)
    {
        InitializeComponent();
        var appContent = (UIElement)Content; Content = null;
        var viewport = new ResponsiveViewport { Child = appContent }; Content = viewport;
        HeaderVersion.Text = " " + AppPreferencesViewModel.ReleaseVersion;
        DataContext = _model = model;
        SettingsPage.SizeChanged += (_, _) => SettingsSubtitle.Visibility = SettingsPage.ActualHeight < 550 ? Visibility.Collapsed : Visibility.Visible;
        preferences ??= new AppPreferencesViewModel();
        _tourPreferences = preferences;
        ContentRendered += (_, _) => TryAutoTour();
        AppPreferencesHost.DataContext = ThemePicker.DataContext = AppVersionText.DataContext = PreferencesPath.DataContext = preferences;
        var movement = new MovementEditor(model, (Style)FindResource("SettingsSwitch"));
        MovementSections.Content = movement;
        foreach (var (name, control) in movement.DiagnosticControls) RegisterName(name, control);
        MovementScroll.ScrollChanged += (_, _) => { if (MovementScroll.ViewportHeight > 0) movement.SetAvailableHeight(MovementScroll.ViewportHeight); };
        ThemedChrome.Apply(this, true);
        PieEditorHost.Content = new PieEditor(model);
        var keybindings = new KeybindingsEditor(model);
        KeybindingsHost.Content = keybindings;
        var hero = new ControllerHero(model, control => { SettingsTabs.SelectedIndex = 1; keybindings.SelectControl(control); },
            stick => { SettingsTabs.SelectedIndex = 1; keybindings.SelectJoystick(stick); });
        ControllerHeroHost.Content = hero;
        Workspace.SizeChanged += (_, _) => keybindings.SetAvailableHeight(Workspace.ActualHeight);
        keybindings.ControlSelected += hero.Select;
        keybindings.JoystickSelected += hero.SelectJoystick;
        void UpdatePieChrome()
        {
            var showController = SettingsTabs.SelectedIndex is not (0 or 1 or 2 or 4);
            keybindings.SetAvailableHeight(Workspace.ActualHeight);
            var wide = viewport.ActualWidth >= 1450 && viewport.ActualHeight >= 850;
            HeaderLogo.Width = wide ? 234 : 184; HeaderLogo.Height = wide ? 64 : 44;
            AppLayout.Margin = wide ? new Thickness(24, 18, 24, 16) : new Thickness(10, 8, 10, 8);
            BrandHeader.Margin = new Thickness(4, 2, 4, wide ? 22 : 8);
            ProfileColumn.Width = new GridLength(620);
            Grid.SetColumn(ProfileControls, 1); Grid.SetRow(ProfileControls, 0);
            Grid.SetColumnSpan(ProfileControls, 1);
            ProfileControls.Margin = new Thickness(24, 0, 0, 0);
            // The retained hidden legacy assignment page has its own hero. Visible
            // Keybindings always uses the complete ControllerBindingsBoard instead.
            var legacyHero = showController && viewport.ActualWidth >= 900;
            HeroRegion.Visibility = legacyHero ? Visibility.Visible : Visibility.Collapsed;
            HeroColumn.Width = new GridLength(legacyHero ? Math.Max(0, (Workspace.ActualWidth - 104) * 1.35 / 2.35) : 0);
            SettingsTabs.ApplyTemplate();
            if (SettingsTabs.Template.FindName("HeroSpacer", SettingsTabs) is ColumnDefinition spacer) spacer.Width = HeroColumn.Width;
        }
        Workspace.SizeChanged += (_, _) => UpdatePieChrome();
        SettingsTabs.SelectionChanged += (_, e) => { if (e.Source == SettingsTabs) { if (SettingsTabs.SelectedIndex != 4) _model.StopShortcutRecording(); UpdatePieChrome(); QueueTabTransition(); } };
        Deactivated += (_, _) => _model.StopShortcutRecording();
        ((FrameworkElement)Content).SizeChanged += (_, _) => UpdatePieChrome();
        _ = new MainWindowPlacement(this);
        _lastVisibleState = preferences.Maximized ? WindowState.Maximized : WindowState.Normal;
        WindowState = _lastVisibleState;
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) return;
            _lastVisibleState = WindowState;
            if (IsVisible) preferences.Maximized = WindowState == WindowState.Maximized;
        };
    }
    private int _transitionRequest;
    private void QueueTabTransition()
    {
        var request = ++_transitionRequest;
        // Resolve any draft-navigation cancellation first; animate only the final tab.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (request != _transitionRequest || !IsVisible ||
                SettingsTabs.Template.FindName("PART_SelectedContentHost", SettingsTabs) is not ContentPresenter host) return;
            host.BeginAnimation(OpacityProperty, null);
            host.RenderTransform = System.Windows.Media.Transform.Identity;
            if (!SystemParameters.ClientAreaAnimation) return;
            var duration = TimeSpan.FromMilliseconds(140);
            host.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(.84, 1, duration)
                { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
            var move = new System.Windows.Media.TranslateTransform(); host.RenderTransform = move;
            move.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new System.Windows.Media.Animation.DoubleAnimation(5, 0, duration)
                { EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }, FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
        }));
    }
    private WindowState _lastVisibleState = WindowState.Normal;
    public void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = _lastVisibleState;
        TryAutoTour();
    }
    private bool _profileChanging;
    public Func<string?>? RequestProfileName { get; set; }
    public Func<string, string?>? RequestRenameProfile { get; set; }
    public Func<string, MessageBoxButton, MessageBoxResult>? ConfirmProfile { get; set; }
    private MessageBoxResult Confirm(string message, MessageBoxButton buttons) => ConfirmProfile?.Invoke(message, buttons) ?? ThemedDialog.Ask(this, message, "FullTilt profiles", buttons);
    private bool ResolveProfileEdits()
    {
        _model.StopShortcutRecording();
        if (!TryResolvePieDraft()) return false;
        if (!_model.HasPauseDraft) return true;
        var answer = ThemedDialog.Ask(this, "Apply the edited pause shortcut before continuing? No discards the shortcut draft.", "Pause shortcut", MessageBoxButton.YesNoCancel);
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.Yes) { if (!_model.CanApplyPauseShortcut) return false; _model.ApplyPauseShortcut(); }
        else _model.DiscardPauseShortcutDraft();
        return true;
    }
    private void Profile_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_profileChanging || _model is null || ProfilePicker.SelectedValue is not Guid id || id == _model.ActiveProfileId) return;
        _profileChanging = true;
        try { if (ResolveProfileEdits()) _model.SwitchProfile(id); }
        finally { RefreshProfilePicker(); _profileChanging = false; }
    }
    private void RefreshProfilePicker()
    {
        ProfilePicker.GetBindingExpression(System.Windows.Controls.ItemsControl.ItemsSourceProperty)?.UpdateTarget();
        ProfilePicker.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedValueProperty)?.UpdateTarget();
    }
    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        _profileChanging = true;
        try { if (ResolveProfileEdits()) _model.SaveProfile(); }
        finally { RefreshProfilePicker(); _profileChanging = false; }
    }
    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!ResolveProfileEdits()) return;
        var name = RequestProfileName is not null ? RequestProfileName() : PromptProfileName(false);
        if (name is null) return;
        _profileChanging = true;
        try { _model.CreateProfile(name); }
        finally { RefreshProfilePicker(); _profileChanging = false; }
    }
    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        var current = _model.Profiles.Single(p => p.Id == _model.ActiveProfileId);
        var name = RequestRenameProfile is not null ? RequestRenameProfile(current.Name) : PromptProfileName(true);
        if (name is null) return;
        _profileChanging = true;
        try { _model.RenameProfile(name); }
        finally { RefreshProfilePicker(); _profileChanging = false; }
    }
    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!_model.CanDeleteProfile) return;
        var current = _model.Profiles.Single(p => p.Id == _model.ActiveProfileId);
        var fallback = _model.Profiles.First(p => p.Id != current.Id);
        if (Confirm("Delete ‘" + current.Name + "’? FullTilt will switch to ‘" + fallback.Name + "’. This cannot be undone.", MessageBoxButton.YesNo) != MessageBoxResult.Yes || !ResolveProfileEdits()) return;
        _profileChanging = true;
        try { _model.DeleteProfile(); }
        finally { RefreshProfilePicker(); _profileChanging = false; }
    }
    private string? PromptProfileName(bool rename)
    {
        var current = _model.Profiles.Single(p => p.Id == _model.ActiveProfileId);
        var dialog = new ThemedDialog(rename ? "Rename profile" : "New profile", "Choose a unique name for this FullTilt setup.", MessageBoxButton.OKCancel,
            rename ? current.Name : "", name =>
            {
                name = FullTilt.Core.ProfileLibrary.CleanName(name);
                if (name.Length is 0 or > 80) return "Enter a name with 1–80 characters.";
                return _model.Profiles.Any(p => (!rename || p.Id != current.Id) && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ? "A profile with that name already exists." : null;
            }) { Owner = this };
        dialog.ShowDialog(); return dialog.Result == MessageBoxResult.OK ? dialog.NameInput!.Text : null;
    }
    private void ApplyPauseShortcut_Click(object sender, RoutedEventArgs e) => _model.ApplyPauseShortcut();
    private void RecordShortcut_Click(object sender, RoutedEventArgs e) => _model.RecordPauseShortcut();
    private void CancelShortcut_Click(object sender, RoutedEventArgs e) => _model.DiscardPauseShortcutDraft();
    private void AddShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        _model.StopShortcutRecording();
        if (sender is Button { ContextMenu: { } menu } button) { menu.DataContext = _model; menu.PlacementTarget = button; menu.IsOpen = true; }
    }
    private void CopySettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(PreferencesPath.Text); }
        catch (System.Runtime.InteropServices.ExternalException) { ThemedDialog.Ask(this, "The clipboard is busy. Try again.", "FullTilt", MessageBoxButton.OK); }
    }
    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!System.IO.Directory.Exists(PreferencesPath.Text)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PreferencesPath.Text) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { ThemedDialog.Ask(this, "Windows could not open the settings folder.", "FullTilt", MessageBoxButton.OK); }
    }
    private void Pause_Click(object sender, RoutedEventArgs e) => _model.TogglePause();
    private void Reset_Click(object sender, RoutedEventArgs e) { if (TryResolvePieDraft() && ThemedDialog.Ask(this, "Reset the current profile’s controller settings and pie menus? Other profiles and app preferences will be kept.", "Reset current profile", MessageBoxButton.YesNo) == MessageBoxResult.Yes) _model.ResetDefaults(); }
    public bool TryResolvePieDraft() => ((PieEditor)PieEditorHost.Content).TryResolvePending();
    private void Release_Click(object sender, RoutedEventArgs e) => _model.ReleaseCancel();
    private void Number_LostFocus(object sender, RoutedEventArgs e)
    {
        // Invalid drafts never reach settings; leaving the field restores its last accepted value.
        if (sender is System.Windows.Controls.TextBox field && System.Windows.Controls.Validation.GetHasError(field))
            field.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateTarget();
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        _model.StopShortcutRecording();
        if (!AllowClose) { e.Cancel = true; if (TryResolvePieDraft()) { Hide(); HiddenToTray?.Invoke(); } }
        base.OnClosing(e);
    }
}
