using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Data;
using System.Windows;
using System.Windows.Controls;
using FullTilt.Core;
using Control = FullTilt.Core.Control;

namespace FullTilt;

public sealed record JoystickTarget(CursorStick Stick, string Label);

public partial class KeybindingsEditor : UserControl
{
    public static readonly DependencyProperty IsCurrentCategoryProperty = DependencyProperty.RegisterAttached(
        "IsCurrentCategory", typeof(bool), typeof(KeybindingsEditor), new PropertyMetadata(false));
    public static bool GetIsCurrentCategory(DependencyObject element) => (bool)element.GetValue(IsCurrentCategoryProperty);
    public static void SetIsCurrentCategory(DependencyObject element, bool value) => element.SetValue(IsCurrentCategoryProperty, value);
    private readonly SettingsViewModel _model;
    private bool _refreshing;
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private string? _currentCategory;
    private BindingChoice[] _visible = [];
    private bool _movementChoices;
    private string _paletteQuery = "";
    private readonly Dictionary<(bool Movement, string Query), double> _scrollPositions = [];
    private readonly ControllerBindingsBoard _board;
    public Control SelectedControl { get; private set; } = Control.LT;
    public CursorStick? SelectedJoystick { get; private set; }
    public event Action<CursorStick>? JoystickSelected;
    private static readonly JoystickTarget[] Joysticks = [new(CursorStick.Left, "Left joystick travel"), new(CursorStick.Right, "Right joystick travel")];
    public event Action<Control>? ControlSelected;
    internal Func<string, KeyboardChord?> RecordKeys { get; set; }
    public KeybindingsEditor(SettingsViewModel model)
    {
        InitializeComponent(); _model = model;
        Actions.IsSynchronizedWithCurrentItem = false;
        RecordKeys = target => KeyboardRecorder.Ask(model, Window.GetWindow(this), target);
        _board = new ControllerBindingsBoard(model, SelectControl, SelectJoystick);
        ControllerBoardHost.Content = _board;
        ControlSelected += _board.Select; JoystickSelected += _board.SelectJoystick;
        SizeChanged += (_, _) => UpdateLayoutMode();
        _model.PropertyChanged += (_, e) => { if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName is nameof(SettingsViewModel.DPadMode) or nameof(SettingsViewModel.Settings)) Refresh(); };
        model.Edits.Changed += Refresh;
        Refresh();
    }
    public void SetAvailableHeight(double height) { EditorLayout.Height = Math.Max(1, height - 32); UpdateLayoutMode(); }
    private void UpdateLayoutMode()
    {
        ControllerBoardHost.Visibility = Visibility.Visible;
        BoardColumn.Width = new GridLength(1, GridUnitType.Star);
        AssignmentColumn.Width = new GridLength(Math.Clamp(ActualWidth * .26, 275, 440));
        ControlPicker.Visibility = Visibility.Collapsed;
        AssignmentPane.BorderThickness = new Thickness(1, 0, 0, 0);
        AssignmentPane.Padding = new Thickness(ActualWidth < 1200 ? 12 : 20, 0, 0, 0);
        ControllerBoardHost.Margin = new Thickness(0, 4, ActualWidth < 1200 ? 12 : 24, 0);
    }
    public void SelectControl(Control control)
    {
        SelectedJoystick = null; SelectedControl = control; Refresh();
    }
    public void SelectJoystick(CursorStick stick)
    {
        SelectedJoystick = stick; Refresh();
    }
    private void Refresh()
    {
        if (_refreshing || _model is null) return;
        _refreshing = true;
        try
        {
            var view = _model.Edits.View(EditTab.Keybindings);
            var rows = _model.Edits.BindingRows();
            var row = rows.FirstOrDefault(r => r.Control == SelectedControl) ?? rows.First();
            SelectedControl = row.Control;
            ControlPicker.ItemsSource = rows.Cast<object>().Concat(Joysticks).ToArray();
            ControlPicker.SelectedItem = SelectedJoystick is { } selectedStick ? Joysticks.Single(j => j.Stick == selectedStick) : row;
            Heading.Text = SelectedJoystick is null ? "Assign action" : "Assign movement";
            TargetBadge.Text = SelectedJoystick is { } stick ? stick == CursorStick.Left ? "L" : "R" : row.Control.ToString();
            TargetName.Text = SelectedJoystick is { } travel ? Joysticks.Single(j => j.Stick == travel).Label : row.Control switch
            {
                Control.LT => "Left trigger", Control.RT => "Right trigger", Control.LB => "Left shoulder", Control.RB => "Right shoulder",
                Control.L3 => "Left stick click", Control.R3 => "Right stick click", _ => row.Label
            };
            var movementOnly = SelectedJoystick is null && row.Control >= Control.DPadUp && view.DPadMode != DPadMode.AssignedActions;
            Actions.IsEnabled = !movementOnly;
            RecordKeysButton.Visibility = SelectedJoystick is null ? Visibility.Visible : Visibility.Collapsed;
            RecordKeysButton.IsEnabled = !movementOnly;
            ModeHint.Visibility = movementOnly ? Visibility.Visible : Visibility.Collapsed;
            var choices = SelectedJoystick is { } joystick
                ? new[] { new BindingChoice(joystick, "Cursor Movement", "Joystick movement", "Move the cursor with this joystick."), new BindingChoice(joystick == CursorStick.Left ? CursorStick.Right : CursorStick.Left, "Scrolling", "Joystick movement", "Scroll with this joystick.") }
                : row.Options.Cast<BindingChoice>().ToArray();
            var query = Search.Text.Trim();
            var visible = choices.Where(c => query.Length == 0 || (c.Label + " " + c.Category).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            var current = choices.FirstOrDefault(c => Equals(c.Value, SelectedJoystick is null ? row.Selection : view.CursorStick));
            _currentCategory = null; // The palette is independent of the target's assignment.
            var dirty = SelectedJoystick is null ? _model.Edits.BindingDirty(row.Control) : _model.Edits.RoleDirty;
            CurrentAssignment.Text = (dirty ? "" : "Current: ") + (row.Selection is KeyboardChord chord && SelectedJoystick is null ? chord.Label : current?.Label ?? "None");
            AssignmentStatus.Background = dirty ? EditChrome.Cherry : ThemePalette.Get("#EDF0E9");
            CurrentAssignment.ToolTip = dirty ? "Saved: " + (SelectedJoystick is null ? EditorSession.BindingLabel(_model.Settings, row.Control) : _model.Settings.CursorStick.ToString()) : null;
            TargetHelp.Text = SelectedJoystick is null ? row.Description : "Changing this role swaps cursor and scrolling sticks. L3/R3 click bindings and physical dead zones stay unchanged.";
            if (!_visible.SequenceEqual(visible))
            {
                if (PaletteScroll() is { } previous) _scrollPositions[(_movementChoices, _paletteQuery)] = previous.VerticalOffset;
                _movementChoices = SelectedJoystick is not null; _paletteQuery = query;
                var offset = _scrollPositions.GetValueOrDefault((_movementChoices, query));
                _visible = visible;
                var grouped = new ListCollectionView(visible);
                grouped.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BindingChoice.Category)));
                Actions.ItemsSource = grouped;
                Actions.SelectedItem = null;
                // Hidden/offscreen windows do not raise Loaded.
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    if (!ReferenceEquals(Actions.ItemsSource, grouped)) return;
                    ApplyCategoryState(); PaletteScroll()?.ScrollToVerticalOffset(offset);
                }));
            }
            ActionHeading.Text = query.Length == 0 ? "Actions" : $"{visible.Length} matching actions";
            ActionHeading.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _refreshing = false; }
        if (SelectedJoystick is { } selected) JoystickSelected?.Invoke(selected);
        else ControlSelected?.Invoke(SelectedControl);
    }
    private void ControlChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing) return;
        if (ControlPicker.SelectedItem is JoystickTarget joystick) { SelectJoystick(joystick.Stick); return; }
        if (ControlPicker.SelectedItem is not BindingRow row) return;
        SelectedJoystick = null;
        SelectControl(row.Control);
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) => Refresh();
    private void RecordKeys_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedJoystick is not null || !RecordKeysButton.IsEnabled) return;
        var row = _model.Edits.BindingRows().Single(r => r.Control == SelectedControl);
        if (RecordKeys(row.Label) is { } chord) row.Selection = chord;
        Refresh();
    }
    private void CategoryLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander category && category.Tag is string name)
        {
            SetIsCurrentCategory(category, name == _currentCategory);
            category.IsExpanded = Search.Text.Trim().Length > 0 || _expanded.Contains(name);
        }
    }
    private void ApplyCategoryState()
    {
        Actions.UpdateLayout();
        void Visit(DependencyObject root)
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is Expander category) CategoryLoaded(category, new RoutedEventArgs());
                Visit(child);
            }
        }
        Visit(Actions);
    }
    private ScrollViewer? PaletteScroll()
    {
        ScrollViewer? Find(DependencyObject root)
        {
            if (root is ScrollViewer viewer) return viewer;
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
                if (Find(System.Windows.Media.VisualTreeHelper.GetChild(root, i)) is { } found) return found;
            return null;
        }
        return Find(Actions);
    }
    private void CategoryExpanded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander category && category.Tag is string name && Search.Text.Trim().Length == 0) _expanded.Add(name);
    }
    private void CategoryCollapsed(object sender, RoutedEventArgs e)
    {
        if (sender is Expander category && category.Tag is string name && Search.Text.Trim().Length == 0) _expanded.Remove(name);
    }
    private void ActionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || Actions.SelectedItem is not BindingChoice choice) return;
        // Re-arm the same row for another target without selecting or scrolling to
        // any target's current assignment during Refresh.
        if (SelectedJoystick is not null && choice.Value is CursorStick stick) { _model.Edits.Edit(EditTab.Keybindings, _model.Edits.View(EditTab.Keybindings) with { CursorStick = stick }); ClearPaletteSelection(); Refresh(); return; }
        var row = _model.Edits.BindingRows().Single(r => r.Control == SelectedControl);
        if (row.Control >= Control.DPadUp && !_model.IsDPadActionMode) return;
        row.Selection = choice.Value; // Existing persistence, ownership cleanup and pie binding path.
        ClearPaletteSelection();
        Refresh();
    }
    private void ClearPaletteSelection() { _refreshing = true; Actions.SelectedItem = null; _refreshing = false; }
}
