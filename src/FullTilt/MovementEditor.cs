using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;


namespace FullTilt;

public sealed class MovementEditor : UserControl
{
    private readonly SettingsViewModel _model;
    private readonly Grid _cards = new(), _deadRows = new();
    private readonly List<MovementValueRow> _values = [];
    private readonly Dictionary<Button, double> _presets = [];
    private double _availableHeight = 760;
    private bool? _compact;
    private bool _dpadClosing;
    private double _deadClosedHeight;
    private bool? _deadClosedCompact;
    private readonly DispatcherTimer _dpadClose = new() { Interval = TimeSpan.FromMilliseconds(170) };
    private void Density(bool compact)
    {
        if (_compact == compact) return; _compact = compact;
        foreach (var card in _cards.Children.OfType<Border>())
        {
            card.Padding = new Thickness(compact ? 8 : 20);
            foreach (var hint in Elements(card).OfType<TextBlock>().Where(t => Equals(t.Tag, "Movement help"))) { hint.ToolTip = hint.Text; hint.Visibility = compact ? Visibility.Collapsed : Visibility.Visible; }
        }
        foreach (var row in _values) row.SetCompact(compact);
        var customize = (Button)DiagnosticControls["CustomizeCurveButton"]; customize.MinHeight = compact ? 30 : 38; customize.Margin = new Thickness(0, compact ? 2 : 8, 0, 0);
        var segments = (Panel)customize.Parent; foreach (var selector in segments.Children.OfType<System.Windows.Controls.Primitives.UniformGrid>()) selector.Margin = compact ? new Thickness(0, 6, 0, 4) : new Thickness(0, 14, 0, 8);
        ((CheckBox)DiagnosticControls["ReverseScrollSwitch"]).Margin = compact ? new Thickness(0, 2, 0, 2) : new Thickness(0, 12, 0, 6);
    }
    private static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is FrameworkElement element) yield return element; foreach (var descendant in Elements(child)) yield return descendant; }
    }
    public Dictionary<string, object> DiagnosticControls { get; } = [];
    public Border CursorCard { get; }
    public Border ScrollCard { get; }
    public Border ResponseCard { get; }
    public Border DeadZoneCard { get; }
    public Func<bool> ConfirmReset { get; set; }
    public MovementEditor(SettingsViewModel model, Style switchStyle)
    {
        _model = model; DataContext = model; FontWeight = FontWeights.Normal;
        ConfirmReset = () => ThemedDialog.Ask(Window.GetWindow(this), "Reset movement speeds, stick roles, scrolling, response and dead zones for this profile? Button bindings, pie menus and other settings are kept.", "Reset movement", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); Content = root;
        var heading = new DockPanel { Margin = new Thickness(6, 0, 6, 18) };
        var history = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        history.ToolTip = "Movement only · Changes apply immediately";
        var undo = EditChrome.Button("Undo", () => model.Edits.Replay(FullTilt.Core.EditTab.Movement, true));
        var redo = EditChrome.Button("Redo", () => model.Edits.Replay(FullTilt.Core.EditTab.Movement, false));
        history.Children.Add(undo); history.Children.Add(redo); DockPanel.SetDock(history, Dock.Right);
        void UpdateHistory() { undo.IsEnabled = model.Edits.CanUndo(FullTilt.Core.EditTab.Movement); redo.IsEnabled = model.Edits.CanRedo(FullTilt.Core.EditTab.Movement); }
        model.Edits.Changed += UpdateHistory; UpdateHistory(); model.Edits.TrackGestures(this, FullTilt.Core.EditTab.Movement);
        var reset = ActionButton("Reset movement", "ResetMovement"); DockPanel.SetDock(reset, Dock.Right); reset.VerticalAlignment = VerticalAlignment.Center; reset.Click += (_, _) => { if (ConfirmReset()) model.ResetMovement(); }; DiagnosticControls["ResetMovementButton"] = reset; heading.Children.Add(reset);
        heading.Children.Add(history); DiagnosticControls["UndoButton"] = undo; DiagnosticControls["RedoButton"] = redo;
        var title = new StackPanel(); title.Children.Add(Text("Movement", 28, true)); title.Children.Add(Help("Tune cursor speed, scrolling and stick response.")); heading.Children.Add(title); root.Children.Add(heading);
        _cards.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); _cards.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 4; i++) _cards.RowDefinitions.Add(new()); Grid.SetRow(_cards, 1); root.Children.Add(_cards);
        var cursor = Rows(5, true); cursor.Children.Add(CardHeading("Cursor speed", "Control pointer movement with a stick.", "Movement"));
        var stick = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) }; stick.ColumnDefinitions.Add(new() { Width = new GridLength(160) }); stick.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); stick.RowDefinitions.Add(new() { Height = GridLength.Auto }); stick.RowDefinitions.Add(new() { Height = GridLength.Auto });
        stick.Children.Add(Text("Cursor stick", 14, true)); var picker = new ComboBox { ItemsSource = model.CursorStickOptions, DisplayMemberPath = "Label", SelectedValuePath = "Value" }; picker.SetBinding(ComboBox.SelectedValueProperty, new Binding(nameof(model.CursorStick)) { Mode = BindingMode.TwoWay }); Grid.SetColumn(picker, 1); stick.Children.Add(picker); DiagnosticControls["StickPicker"] = picker;
        var role = Help(""); role.SetBinding(TextBlock.TextProperty, new Binding(nameof(model.StickRoleHelp))); role.Margin = new Thickness(0, 6, 0, 0); Grid.SetRow(role, 1); Grid.SetColumn(role, 1); stick.Children.Add(role); Add(cursor, stick, 1);
        Add(cursor, Value("CursorNumber", "Normal cursor speed", "Your everyday pointer speed.", "px/s", nameof(model.CursorSpeed), () => model.CursorSpeed, v => model.CursorSpeed = v, () => (1, FullTilt.Core.AppSettings.MaximumCursorSpeed, 1, 4000)), 2);
        Add(cursor, Value("PrecisionNumber", "Precision cursor speed", "Independent speed while Precision is active.", "px/s", nameof(model.PrecisionCursorSpeed), () => model.PrecisionCursorSpeed, v => model.PrecisionCursorSpeed = v, () => (1, FullTilt.Core.AppSettings.MaximumCursorSpeed, 1, 4000)), 3);
        Add(cursor, Value("SpeedBoostNumber", "Boosted cursor speed", "While Speed Boost is active. Precision takes priority.", "px/s", nameof(model.BoostedCursorSpeed), () => model.BoostedCursorSpeed, v => model.BoostedCursorSpeed = v, () => (1, FullTilt.Core.AppSettings.MaximumCursorSpeed, 1, 4000)), 4);
        CursorCard = Card(cursor); _cards.Children.Add(CursorCard);
        var scrolling = Rows(5, true); scrolling.RowDefinitions[3].Height = GridLength.Auto; scrolling.RowDefinitions[4].Height = GridLength.Auto;
        scrolling.Children.Add(CardHeading("Scrolling", "Controlled by the other stick.", "MiddleClick"));
        Add(scrolling, Value("ScrollNumber", "Normal scroll speed", "For everyday scrolling.", "steps/s", nameof(model.ScrollSpeed), () => model.ScrollSpeed, v => model.ScrollSpeed = v, () => (1, 30, 1, 30)), 1);
        Add(scrolling, Value("ScrollBoostNumber", "Boosted scroll speed", "While Speed Boost is active.", "steps/s", nameof(model.BoostedScrollSpeed), () => model.BoostedScrollSpeed, v => model.BoostedScrollSpeed = v, () => (model.ScrollSpeed, model.ScrollSpeed * 10, 1, model.ScrollSpeed * 10)), 2);
        var reverse = new CheckBox { Content = "Reverse scroll direction", Style = switchStyle, ToolTip = "Flip both horizontal and vertical stick scrolling." }; reverse.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(model.ReverseScroll)) { Mode = BindingMode.TwoWay }); Add(scrolling, reverse, 3); DiagnosticControls["ReverseScrollSwitch"] = reverse;
        Add(scrolling, Help("Mouse-wheel steps per second."), 4); ScrollCard = Card(scrolling); _cards.Children.Add(ScrollCard);
        var response = new StackPanel(); response.Children.Add(CardHeading("Cursor response", "Changes how stick movement is translated to cursor speed.", "ResponseCurve"));
        var segments = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(0, 14, 0, 8) };
        foreach (var (label, curve) in new[] { ("Linear", 1d), ("Balanced", 1.8), ("Fine control", 2.6) })
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(8, 7, 8, 7) }; button.Click += (_, _) => model.ResponseCurve = curve; segments.Children.Add(button); _presets[button] = curve; DiagnosticControls[label.Replace(" ", "") + "ResponseButton"] = button;
        }
        response.Children.Add(segments); response.Children.Add(Help("Linear follows stick travel directly. Higher curves soften movement near the center."));
        var curveRow = Value("CurveNumber", "Response curve", "100% is linear; up to 300% gives finer center control.", "%", nameof(model.ResponseCurvePercent), () => model.ResponseCurvePercent, v => model.ResponseCurvePercent = v, () => (100, 300, 1, 300)); curveRow.Visibility = Visibility.Collapsed;
        var customize = new Button { Content = "Customize curve  →", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(0, 5, 0, 5), BorderThickness = new Thickness(0), Background = Brushes.Transparent }; customize.SetResourceReference(Control.ForegroundProperty, "Accent"); customize.Click += (_, _) => { curveRow.Visibility = curveRow.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; UpdateLayoutMode(); if (curveRow.Visibility == Visibility.Visible) UiMotion.Reveal(curveRow); }; response.Children.Add(customize); response.Children.Add(curveRow); DiagnosticControls["CustomizeCurveButton"] = customize;
        ResponseCard = Card(response); _cards.Children.Add(ResponseCard);
        var dead = new Grid(); dead.RowDefinitions.Add(new() { Height = GridLength.Auto }); dead.RowDefinitions.Add(new() { Height = GridLength.Auto }); dead.RowDefinitions.Add(new() { Height = GridLength.Auto }); dead.Children.Add(CardHeading("Dead zones", "Increase only if a stick drifts at rest.", "CenterCursor"));
        _deadRows.ColumnDefinitions.Add(new()); _deadRows.ColumnDefinitions.Add(new()); _deadRows.RowDefinitions.Add(new() { Height = GridLength.Auto }); _deadRows.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var leftZone = Value("LeftDeadZoneNumber", "Left stick dead zone", "Physical left stick drift.", "%", nameof(model.LeftDeadZonePercent), () => model.LeftDeadZonePercent, v => model.LeftDeadZonePercent = v, () => (5, 45, 1, 45)); var rightZone = Value("RightDeadZoneNumber", "Right stick dead zone", "Physical right stick drift.", "%", nameof(model.RightDeadZonePercent), () => model.RightDeadZonePercent, v => model.RightDeadZonePercent = v, () => (5, 45, 1, 45)); _deadRows.Children.Add(leftZone); _deadRows.Children.Add(rightZone); Add(dead, _deadRows, 1);
        var dpad = new StackPanel(); dpad.Children.Add(Value("TapNumber", "D-pad tap distance", "Fresh presses nudge the pointer.", "px", nameof(model.DPadTapDistance), () => model.DPadTapDistance, v => model.DPadTapDistance = v, () => (1, 50, 1, 50))); dpad.Children.Add(Value("DPadSpeedNumber", "D-pad held speed", "Holding moves continuously in cursor mode.", "px/s", nameof(model.PrecisionSpeed), () => model.PrecisionSpeed, v => model.PrecisionSpeed = v, () => (10, 2000, 1, 2000))); dpad.SetBinding(IsEnabledProperty, new Binding(nameof(model.IsDPadCursorMode)));
        var advanced = new Expander { Header = "D-pad movement", Content = dpad, Margin = new Thickness(0, 10, 0, 0) };
        _dpadClose.Tick += (_, _) => { _dpadClose.Stop(); _dpadClosing = false; UpdateLayoutMode(); };
        void DpadStateChanged()
        {
            if (advanced.IsExpanded && !_dpadClosing) { _deadClosedHeight = _cards.RowDefinitions[2].ActualHeight; _deadClosedCompact = _compact; }
            _dpadClose.Stop(); _dpadClosing = !advanced.IsExpanded && UiMotion.Enabled;
            if (_dpadClosing) _dpadClose.Start(); UpdateLayoutMode();
        }
        advanced.Expanded += (_, _) => DpadStateChanged(); advanced.Collapsed += (_, _) => DpadStateChanged();
        // Expanding downward must not ask the outer viewport to bring the entire
        // enlarged card into view. Keyboard navigation into its fields still scrolls normally.
        advanced.RequestBringIntoView += (_, e) => { if (ReferenceEquals(e.TargetObject, advanced)) e.Handled = true; };
        Add(dead, advanced, 2); DiagnosticControls["DPadMovementExpander"] = advanced;
        DeadZoneCard = Card(dead); _cards.Children.Add(DeadZoneCard);
        Unloaded += (_, _) => { _dpadClose.Stop(); _dpadClosing = false; };
        Loaded += (_, _) => { _dpadClosing = false; UpdateLayoutMode(); };
        SizeChanged += (_, _) => UpdateLayoutMode(); model.PropertyChanged += Changed; Refresh();
    }
    private MovementValueRow Value(string key, string title, string detail, string unit, string property, Func<double> get, Action<double> set, Func<(double, double, double, double)> range)
    {
        var row = new MovementValueRow(_model, title, detail, unit, property, get, set, range); _values.Add(row); DiagnosticControls[key] = row.Number; DiagnosticControls[key.Replace("Number", "Slider")] = row.Slider; return row;
    }
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsViewModel.Settings)) Refresh(); }
    private void Refresh()
    {
        foreach (var row in _values) row.Refresh();
        foreach (var (button, curve) in _presets) { button.SetResourceReference(Control.BackgroundProperty, Math.Abs(_model.ResponseCurve - curve) < .001 ? "ThemeEAF9CA" : "ThemeFFFFFF"); button.SetResourceReference(Control.BorderBrushProperty, Math.Abs(_model.ResponseCurve - curve) < .001 ? "Accent" : "ThemeE0E5DC"); }
    }
    public void SetAvailableHeight(double height) { _availableHeight = height; UpdateLayoutMode(); }
    private void UpdateLayoutMode()
    {
        var wide = ActualWidth >= 980;
        var advanced = ((Expander)DiagnosticControls["DPadMovementExpander"]).IsExpanded || _dpadClosing;
        var custom = _values.Single(v => v.Title == "Response curve").Visibility == Visibility.Visible;
        var compact = wide && _availableHeight < 760; Density(compact);
        var dpad = (FrameworkElement)((Expander)DiagnosticControls["DPadMovementExpander"]).Content;
        dpad.Measure(new Size(Math.Max(1, ActualWidth - 40), double.PositiveInfinity));
        var expandedHeight = advanced ? dpad.DesiredSize.Height : 0;
        // Reserve each card's usable content height before distributing extra
        // space. Star rows must never compress labels or hide an action.
        Height = Math.Max(wide ? compact ? 580 : 840 : 1400, _availableHeight - 12) + expandedHeight + (custom ? compact ? 50 : 85 : 0);
        _cards.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star); _cards.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        foreach (var card in _cards.Children.OfType<Border>()) { Grid.SetRowSpan(card, 1); Grid.SetColumnSpan(card, 1); card.Margin = new Thickness(6); }
        if (wide)
        {
            _cards.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star); _cards.RowDefinitions[0].MinHeight = compact ? 200 : 290; _cards.RowDefinitions[1].Height = new GridLength(compact ? custom ? 205 : 155 : custom ? 325 : 240); _cards.RowDefinitions[2].Height = GridLength.Auto;
            var baseHeight = compact ? 150 : 220;
            _cards.RowDefinitions[2].MinHeight = (advanced && _deadClosedCompact == compact ? Math.Max(baseHeight, _deadClosedHeight) : baseHeight) + expandedHeight;
            _cards.RowDefinitions[3].Height = new GridLength(0);
            Grid.SetRow(CursorCard, 0); Grid.SetColumn(CursorCard, 0); Grid.SetRowSpan(CursorCard, 2); Grid.SetRow(ScrollCard, 0); Grid.SetColumn(ScrollCard, 1); Grid.SetRow(ResponseCard, 1); Grid.SetColumn(ResponseCard, 1); Grid.SetRow(DeadZoneCard, 2); Grid.SetColumn(DeadZoneCard, 0); Grid.SetColumnSpan(DeadZoneCard, 2);
        }
        else
        {
            _cards.RowDefinitions[0].MinHeight = 460;
            var heights = new[] { 460d, 330, custom ? 325 : 240, advanced ? 490 : 290 };
            for (var i = 0; i < 4; i++) { _cards.RowDefinitions[i].Height = i == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(heights[i]); var card = (Border)_cards.Children[i]; Grid.SetRow(card, i); Grid.SetColumn(card, 0); }
        }
        _deadRows.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star); _deadRows.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        var right = (UIElement)_deadRows.Children[1]; Grid.SetColumn(right, wide ? 1 : 0); Grid.SetRow(right, wide ? 0 : 1);
    }
    private static Grid Rows(int count, bool stretch) { var grid = new Grid(); for (var i = 0; i < count; i++) grid.RowDefinitions.Add(new() { Height = i == 0 || !stretch ? GridLength.Auto : new GridLength(1, GridUnitType.Star) }); return grid; }
    private static void Add(Grid grid, UIElement element, int row) { Grid.SetRow(element, row); grid.Children.Add(element); }
    private static TextBlock Text(string value, double size, bool bold = false) => new() { Text = value, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
    private static TextBlock Help(string value) { var text = Text(value, 12); text.Tag = "Movement help"; text.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); return text; }
    private static UIElement CardHeading(string title, string detail, string icon)
    {
        var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 6) }; var badge = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Padding = new Thickness(6), Margin = new Thickness(0, 0, 14, 0), Child = new UiIcon { Kind = icon } }; badge.SetResourceReference(Border.BackgroundProperty, "ThemeFCFDFB"); panel.Children.Add(badge); var text = new StackPanel(); text.Children.Add(Text(title, 20, true)); text.Children.Add(Help(detail)); panel.Children.Add(text); return panel;
    }
    private static Border Card(UIElement child) { var card = new Border { Child = child, Padding = new Thickness(20), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10) }; card.SetResourceReference(Border.BackgroundProperty, "ThemeFFFFFF"); card.SetResourceReference(Border.BorderBrushProperty, "ThemeE7EBE3"); return card; }
    private static Button ActionButton(string label, string icon) { var content = new StackPanel { Orientation = Orientation.Horizontal }; content.Children.Add(new UiIcon { Kind = icon, Width = 20, Height = 20, Margin = new Thickness(0, 0, 10, 0) }); content.Children.Add(Text(label, 14, true)); return new Button { Content = content }; }
}
