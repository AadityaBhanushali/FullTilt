using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Threading;
using FullTilt.Core;
using Control = FullTilt.Core.Control;

namespace FullTilt;

public sealed record PieMenuOption(Guid Id, string Label, bool Unsaved = false) { public string Marker => Unsaved ? "●" : ""; }
public sealed record PieControlOption(Control? Control, string Label);

public sealed class PieEditor : UserControl
{
    private readonly SettingsViewModel _model;
    private readonly ListBox _menus = new() { DisplayMemberPath = "Label" };
    private readonly TextBox _name = new() { MaxLength = 100, FontSize = 18, FontWeight = FontWeights.SemiBold };
    private readonly ComboBox _count = new() { ItemsSource = Enumerable.Range(2, 7).ToArray(), Width = 110, ItemStringFormat = "{0} segments", Margin = new Thickness(6, 0, 0, 0) };
    private readonly ComboBox _openWith = new() { DisplayMemberPath = "Label", MinWidth = 80 };
    private readonly TextBox _search = new();
    internal TextBox SearchBox => _search;
    internal InputAction? SelectedAction => _draft is null || (_direction >= 0 ? _draft.Menu.RecordedDPad : _draft.Menu.RecordedSlots).ContainsKey(_direction >= 0 ? _direction : _slot)
        ? null : _direction >= 0 ? _draft.Menu.DPadActions[_direction] : _draft.Menu.Slots[_slot];
    public static readonly DependencyProperty IsDPadTargetProperty = DependencyProperty.Register(nameof(IsDPadTarget), typeof(bool), typeof(PieEditor), new PropertyMetadata(false));
    public bool IsDPadTarget => (bool)GetValue(IsDPadTargetProperty);
    private readonly ListBox _actions = new();
    private readonly TextBlock _selection = Text("Choose a segment", 13);
    private readonly TextBlock _bindingHelp = Text("", 12);
    private readonly TextBlock _status = Text("", 12);
    private readonly Grid _center = new();
    private readonly Grid _preview = new();
    internal PieDPadEditor DPadEditor { get; } = new();
    private readonly Button _removeOverride = Button("Remove override");
    internal Button RecordKeysButton { get; } = Button("Record key / shortcut");
    internal Func<string, KeyboardChord?> RecordKeys { get; set; }
    private int _direction = -1;
    private FrameworkElement _dragSurface = null!;
    internal Slider SizeSlider { get; } = new() { Minimum = 100, Maximum = 200, SmallChange = 1, LargeChange = 10, TickFrequency = 1, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true };
    internal TextBox SizeNumber { get; } = new() { Width = 76, VerticalContentAlignment = VerticalAlignment.Center };
    public static readonly DependencyProperty MenuSizePercentProperty = DependencyProperty.Register(nameof(MenuSizePercent), typeof(double), typeof(PieEditor), new PropertyMetadata(100d, SizeChangedValue, (_, value) => WholeNumbers.Round(PieMenu.ValidSize((double)value))));
    public double MenuSizePercent { get => (double)GetValue(MenuSizePercentProperty); set => SetValue(MenuSizePercentProperty, value); }
    private static void SizeChangedValue(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var editor = (PieEditor)owner;
        if (!editor._refreshing && editor._draft is { Deleted: false }) { editor._draft.SetSize((double)args.NewValue); editor.RefreshList(); editor.RefreshPreview(); editor.QueueSave(); }
        editor.UpdatePreviewSize();
    }
    private readonly Button _restore = Button("Restore defaults");
    private readonly Button _undo = Button("Undo"), _redo = Button("Redo"), _discard = Button("Discard"), _save = Button("Save changes");
    private string? _saveError;
    private PieDraft? _draft;
    private int _slot;
    private bool _showSlotHighlight;
    private Window? _highlightWindow;
    private bool _refreshing, _applying;
    private Point _dragStart;
    private ActionDefinition? _pendingAction, _dragAction;
    internal bool ActionDragArmed => _dragAction is not null;
    internal Action<FrameworkElement, InputAction, IDataObject>? DiagnosticDragRunner { get; set; }
    internal Grid PreviewPane => _preview;
    public PieDiagram Diagram { get; } = new() { Margin = new Thickness(0, 5, 0, 5) };
    public PieDraft? Draft => _draft;
    // Dialog seam also permits unattended WPF diagnostics without showing real prompts.
    public Func<string, MessageBoxButton, MessageBoxResult> Confirm { get; set; }

    public PieEditor(SettingsViewModel model)
    {
        _model = model; FontWeight = FontWeights.Normal;
        RecordKeys = target => KeyboardRecorder.Ask(model, Window.GetWindow(this), target, holdable: _direction >= 0);
        Confirm = (message, buttons) => ThemedDialog.Ask(Window.GetWindow(this), message, "Editor changes", buttons,
            buttons == MessageBoxButton.YesNoCancel ? "Save" : "Yes", buttons == MessageBoxButton.YesNoCancel ? "Discard" : "No");
        _search.Name = "Search";
        _model.Edits.ResolveBindingConflict = controls =>
        {
            var answer = ThemedDialog.Ask(Window.GetWindow(this), "Both drafts assign " + string.Join(", ", controls) + " differently. Choose the binding to keep. All other edits will be saved.",
                "Shared activation bindings", MessageBoxButton.YesNoCancel, "Use Keybindings", "Use Pie Menus");
            return answer == MessageBoxResult.Yes ? EditTab.Keybindings : answer == MessageBoxResult.No ? EditTab.PieMenus : null;
        };
        var root = new Grid();
        Rows(root, new GridLength(1, GridUnitType.Star), GridLength.Auto);
        var heading = new StackPanel { Margin = new Thickness(4, 0, 0, 16) };
        heading.Children.Add(Text("Pie Menus", 24, true));
        var subtitle = Text("Build quick controller shortcuts.", 12); subtitle.Margin = new Thickness(0, 4, 0, 0); heading.Children.Add(subtitle);
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(200) });
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(320) });
        for (var i = 0; i < 3; i++) columns.RowDefinitions.Add(new());
        Add(root, columns, 0);
        void LayoutColumns()
        {
            var wide = ActualWidth >= 1000;
            columns.ColumnDefinitions[0].Width = wide ? new GridLength(Math.Clamp(ActualWidth * .17, 200, 280)) : new GridLength(1, GridUnitType.Star);
            columns.ColumnDefinitions[1].Width = new GridLength(wide ? 1 : 0, GridUnitType.Star);
            columns.ColumnDefinitions[2].Width = new GridLength(wide ? Math.Clamp(ActualWidth * .28, 320, 440) : 0);
            for (var i = 0; i < 3; i++) columns.RowDefinitions[i].Height = wide ? new GridLength(i == 0 ? 1 : 0, GridUnitType.Star) : new GridLength(i == 0 ? 300 : i == 1 ? 680 : 500);
            for (var i = 0; i < columns.Children.Count; i++) { var card = (FrameworkElement)columns.Children[i]; Grid.SetColumn(card, wide ? i : 0); Grid.SetRow(card, wide ? 0 : i); card.Margin = new Thickness(wide && i > 0 ? 8 : 0, !wide && i > 0 ? 10 : 0, 0, 0); }
            if (root.Parent is ScrollViewer viewport) root.Height = Math.Max(wide ? (ActualWidth < 1250 ? 780 : 540) : 1540, viewport.ActualHeight);
        }
        SizeChanged += (_, _) => LayoutColumns();
        var left = new Grid(); Rows(left, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
        Add(left, heading, 0); Add(left, Text("My menus", 20, true), 1);
        _menus.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "PieListItem");
        _menus.BorderThickness = new Thickness(0); _menus.Background = Brushes.Transparent; _menus.Margin = new Thickness(0, 14, 0, 12);
        _menus.HorizontalContentAlignment = HorizontalAlignment.Stretch; ScrollViewer.SetHorizontalScrollBarVisibility(_menus, ScrollBarVisibility.Disabled);
        var menuText = new FrameworkElementFactory(typeof(TextBlock)); menuText.SetBinding(TextBlock.TextProperty, new Binding("Label")); menuText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); menuText.SetValue(FrameworkElement.MarginProperty, new Thickness(3, 12, 3, 12)); menuText.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        var menuRow = new FrameworkElementFactory(typeof(DockPanel));
        var marker = new FrameworkElementFactory(typeof(TextBlock)); marker.SetBinding(TextBlock.TextProperty, new Binding(nameof(PieMenuOption.Marker))); marker.SetValue(TextBlock.ForegroundProperty, EditChrome.CherryInk); marker.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); marker.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 4, 0)); menuRow.AppendChild(marker); menuRow.AppendChild(menuText);
        _menus.DisplayMemberPath = ""; _menus.ItemTemplate = new DataTemplate { VisualTree = menuRow }; Add(left, _menus, 2);
        var management = new StackPanel();
        var add = Button("New pie menu"); management.Children.Add(add);
        add.Click += (_, _) => BeginNew(null);
        var operations = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var rename = Button("Rename"); var clone = Button("Clone"); var delete = Button("Delete");
        rename.Click += (_, _) => { _name.Focus(); _name.SelectAll(); };
        clone.Click += (_, _) => Duplicate(); delete.Click += (_, _) => Delete();
        foreach (var button in new[] { rename, clone, delete }) { button.FontSize = 12; button.Padding = new Thickness(8, 6, 8, 6); button.Margin = new Thickness(0, 0, 4, 4); operations.Children.Add(button); }
        management.Children.Add(operations); Add(left, management, 3); Card(columns, left, 0);

        Rows(_center, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
        Diagram.HorizontalAlignment = HorizontalAlignment.Center; Diagram.VerticalAlignment = VerticalAlignment.Center;
        _preview.ColumnDefinitions.Add(new() { Width = new GridLength(1.15, GridUnitType.Star) });
        _preview.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _preview.Children.Add(Diagram); Grid.SetColumn(DPadEditor, 1); DPadEditor.Margin = new Thickness(4, 12, 0, 12); _preview.Children.Add(DPadEditor);
        _preview.SizeChanged += (_, _) => UpdatePreviewSize(); Add(_center, Section(_preview), 1);
        DPadEditor.SelectedDirection += SelectDPad;
        DPadEditor.Dropped += (direction, action) => { _direction = direction; if (action is KeyboardChord chord) RecordSelected(chord); else AssignSelected((InputAction)action); FilterActions(); };
        Card(columns, _center, 1);
        _openWith.ItemsSource = new[] { new PieControlOption(null, "Unassigned") }.Concat(Enum.GetValues<Control>().Select(c => new PieControlOption(c, c >= Control.DPadUp ? c + " · action mode" : c + " (Hold)"))).ToArray();

        var configuration = new Grid { Margin = new Thickness(0, 0, 0, 12) }; Rows(configuration, GridLength.Auto, GridLength.Auto);
        _name.FontSize = 20; Add(configuration, _name, 0);
        var choices = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var width in new[] { 1d, .8, 1.4 }) choices.ColumnDefinitions.Add(new() { Width = new GridLength(width, GridUnitType.Star) });
        for (var i = 0; i < 3; i++) choices.RowDefinitions.Add(new() { Height = GridLength.Auto });
        choices.SizeChanged += (_, _) =>
        {
            var inline = choices.ActualWidth >= 500;
            for (var i = 0; i < 3; i++) { choices.ColumnDefinitions[i].Width = new GridLength(inline ? new[] { 1d, .8, 1.4 }[i] : i == 0 ? 1 : 0, GridUnitType.Star); var item = choices.Children[i]; Grid.SetColumn(item, inline ? i : 0); Grid.SetRow(item, inline ? 0 : i); }
        };
        var binding = new Grid { Margin = new Thickness(0, 10, 0, 0) }; Rows(binding, GridLength.Auto, GridLength.Auto);
        var bindingLabel = Text("Activate with", 12, true); bindingLabel.Margin = new Thickness(0, 0, 0, 5); binding.Children.Add(bindingLabel); Add(binding, _openWith, 1); choices.Children.Add(binding);
        _bindingHelp.Margin = new Thickness(0, 6, 0, 8); binding.RowDefinitions.Add(new() { Height = GridLength.Auto }); Add(binding, _bindingHelp, 2);
        var segments = new StackPanel { Margin = new Thickness(12, 10, 12, 0) }; segments.Children.Add(Text("Segments", 12, true)); _count.Width = double.NaN; _count.Margin = new Thickness(0, 5, 0, 0); segments.Children.Add(_count); Grid.SetColumn(segments, 1); choices.Children.Add(segments);
        var size = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        size.Children.Add(Text("Menu size · global", 12, true));
        var sizeValues = new Grid { Margin = new Thickness(0, 5, 0, 0) }; sizeValues.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); sizeValues.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        SizeSlider.Style = MovementValueRow.CreateSliderStyle(draftAware: true); SizeSlider.SetResourceReference(System.Windows.Controls.Control.FocusVisualStyleProperty, "FocusRing");
        SizeSlider.ValueChanged += (_, _) =>
        {
            var whole = WholeNumbers.Round(SizeSlider.Value);
            if (SizeSlider.Value != whole) SizeSlider.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty, whole);
        };
        SizeSlider.Margin = new Thickness(0, 0, 8, 0); SizeSlider.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new Binding(nameof(MenuSizePercent)) { Source = this, Mode = BindingMode.TwoWay }); sizeValues.Children.Add(SizeSlider);
        var sizeBinding = new Binding(nameof(MenuSizePercent)) { Source = this, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, Converter = WholeNumbers.Converter, StringFormat = "{0:0}" }; sizeBinding.ValidationRules.Add(new SizeRule()); SizeNumber.SetBinding(TextBox.TextProperty, sizeBinding);
        SizeNumber.LostFocus += (_, _) => SizeNumber.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        SizeNumber.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { SizeNumber.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget(); e.Handled = true; } };
        var percent = new DockPanel(); var suffix = Text("%", 12); suffix.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(suffix, Dock.Right); percent.Children.Add(suffix); percent.Children.Add(SizeNumber); Grid.SetColumn(percent, 1); sizeValues.Children.Add(percent); size.Children.Add(sizeValues);
        var limits = new DockPanel(); var maximum = Text("200%", 12); DockPanel.SetDock(maximum, Dock.Right); limits.Children.Add(maximum); limits.Children.Add(Text("100%", 12)); size.Children.Add(limits); size.Children.Add(Text("All desktop pies, across profiles. Editor fits its pane. Applies on Save changes.", 12)); Grid.SetColumn(size, 2); choices.Children.Add(size);
        Add(configuration, choices, 1); Add(_center, Section(configuration), 0);
        SizeSlider.ToolTip = SizeNumber.ToolTip = "Global desktop menu size, 100 to 200 percent. Editor preview fits its pane independently. Applies on Save changes."; AutomationProperties.SetName(SizeSlider, "Global desktop pie menu size slider"); AutomationProperties.SetName(SizeNumber, "Global desktop pie menu size percent");
        var right = new Grid(); Rows(right, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star)); Add(right, Text("Assign action", 22, true), 0);
        _selection.Margin = new Thickness(0, 12, 0, 12); _selection.FontWeight = FontWeights.SemiBold;
        var selectionPanel = new StackPanel(); selectionPanel.Children.Add(_selection); selectionPanel.Children.Add(_removeOverride); Add(right, selectionPanel, 1);
        RecordKeysButton.Margin = new Thickness(0, 6, 0, 10); RecordKeysButton.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(RecordKeysButton, "Record key or shortcut for selected pie target");
        selectionPanel.Children.Add(RecordKeysButton);
        RecordKeysButton.Click += (_, _) => { if (_draft is { Deleted: false } && RecordKeys(_draft.Menu.Name + " · " + (_direction >= 0 ? "D-pad " + PieMenu.DPadDirections[_direction] : "Segment " + (_slot + 1))) is { } chord) RecordSelected(chord); };
        _removeOverride.Click += (_, _) => { if (_draft is not null && _direction >= 0) { _draft.AssignDPad(_direction, null); CaptureEdit(); } };
        _search.Padding = new Thickness(8, 0, 8, 0); _search.MinHeight = 42; _search.VerticalContentAlignment = VerticalAlignment.Center; _search.SetResourceReference(TextBox.CaretBrushProperty, "Ink");
        _search.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(SearchTemplate);
        var searchBox = new Grid { Margin = new Thickness(0, 0, 0, 8) }; searchBox.Children.Add(_search);
        var searchHint = Text("Search actions…", 13); searchHint.Margin = new Thickness(43, 0, 0, 0); searchHint.VerticalAlignment = VerticalAlignment.Center; searchHint.IsHitTestVisible = false; searchHint.Foreground = ThemePalette.Get("#808080"); searchBox.Children.Add(searchHint);
        void UpdateSearchHint() => searchHint.Visibility = _search.Text.Length == 0 && !_search.IsKeyboardFocusWithin ? Visibility.Visible : Visibility.Collapsed;
        _search.TextChanged += (_, _) => UpdateSearchHint();
        _search.IsKeyboardFocusWithinChanged += (_, _) => UpdateSearchHint();
        _search.ToolTip = "Search actions or categories"; Add(right, searchBox, 2); Add(right, _actions, 3); Card(columns, right, 2);
        ScrollViewer.SetHorizontalScrollBarVisibility(_actions, ScrollBarVisibility.Disabled); _actions.IsSynchronizedWithCurrentItem = false; _actions.BorderThickness = new Thickness(0); _actions.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var available = new MultiBinding { Converter = new PieActionAvailability() };
        available.Bindings.Add(new Binding("Action"));
        available.Bindings.Add(new Binding(nameof(IsDPadTarget)) { Source = this });
        var rowStyle = new Style(typeof(ListBoxItem), TryFindResource(typeof(ListBoxItem)) as Style);
        rowStyle.Setters.Add(new Setter(IsEnabledProperty, available));
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(OpacityProperty, .45)); rowStyle.Triggers.Add(disabled);
        _actions.ItemContainerStyle = rowStyle;
        _actions.ToolTip = "Actions that require a physical hold are available for D-pad directions. Use their toggle action on a pie segment.";
        var actionText = new FrameworkElementFactory(typeof(TextBlock)); actionText.SetBinding(TextBlock.TextProperty, new Binding("Label")); actionText.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); actionText.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 7, 4, 7)); _actions.ItemTemplate = new DataTemplate { VisualTree = actionText };
        var expander = new FrameworkElementFactory(typeof(Expander)); expander.SetResourceReference(FrameworkElement.StyleProperty, "PieCategory"); expander.SetBinding(HeaderedContentControl.HeaderProperty, new Binding("Name")); expander.SetValue(Expander.IsExpandedProperty, true); expander.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 6));
        var items = new FrameworkElementFactory(typeof(ItemsPresenter)); expander.AppendChild(items);
        var groupStyle = new Style(typeof(GroupItem)); groupStyle.Setters.Add(new Setter(System.Windows.Controls.Control.TemplateProperty, new ControlTemplate(typeof(GroupItem)) { VisualTree = expander }));
        _actions.GroupStyle.Add(new GroupStyle { ContainerStyle = groupStyle });

        var footer = new Grid { Margin = new Thickness(4, 10, 0, 0) }; footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footer.RowDefinitions.Add(new() { Height = GridLength.Auto }); footer.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _status.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(_status);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetColumn(buttons, 1); footer.Children.Add(buttons);
        foreach (var button in new[] { _undo, _redo, _restore, _discard, _save }) { button.Margin = new Thickness(6, 0, 0, 0); buttons.Children.Add(button); }
        _save.Background = ThemePalette.Get("#ECFBCB");
        EditChrome.Decorate(_undo, "Undo"); EditChrome.Decorate(_redo, "Redo");
        _undo.Click += (_, _) => _model.Edits.Replay(EditTab.PieMenus, true);
        _redo.Click += (_, _) => _model.Edits.Replay(EditTab.PieMenus, false);
        _discard.Click += (_, _) => Discard();
        _save.Click += (_, _) => Apply();
        footer.SizeChanged += (_, _) =>
        {
            var inline = footer.ActualWidth >= 660;
            Grid.SetColumn(buttons, inline ? 1 : 0); Grid.SetRow(buttons, inline ? 0 : 1);
            buttons.Margin = new Thickness(0, inline ? 0 : 6, 0, 0);
            footer.ColumnDefinitions[1].Width = inline ? GridLength.Auto : new GridLength(0);
        };
        Add(_center, footer, 2);
        var viewport = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        viewport.SizeChanged += (_, _) => LayoutColumns();
        _dragSurface = viewport; Content = new System.Windows.Documents.AdornerDecorator { Child = viewport };
        Diagram.DragRequested += StartDrag;
        PreviewMouseLeftButtonDown += PreviewPointerDown;

        _menus.SelectionChanged += (_, _) => { if (!_refreshing && _menus.SelectedItem is PieMenuOption option) SelectMenu(option.Id); };
        _model.Edits.TrackGestures(this, EditTab.PieMenus);
        _model.Edits.Changed += OnMenusChanged;
        _name.TextChanged += (_, _) => { if (!_refreshing && _draft is not null) { _draft.Rename(_name.Text); RefreshList(); RefreshPreview(); QueueSave(); } };
        _count.SelectionChanged += (_, _) => { if (!_refreshing && _draft is not null && _count.SelectedItem is int count) Resize(count); };
        _openWith.SelectionChanged += (_, _) => { if (!_refreshing && _draft is not null && _openWith.SelectedItem is PieControlOption option) { var previous = _draft.OpenWith; _draft.OpenWith = option.Control; if (!ConfirmBinding()) { _draft.OpenWith = previous; Refresh(); } else CaptureEdit(); } };
        _search.TextChanged += (_, _) => FilterActions();
        _actions.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _draft is { Deleted: false } && _actions.SelectedItem is ActionDefinition action)
            {
                if (Mouse.LeftButton == MouseButtonState.Pressed) _pendingAction = action;
                else AssignSelected(action.Action);
            }
        };
        _actions.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_pendingAction is { } action && _draft is { Deleted: false }) AssignSelected(action.Action);
            _pendingAction = null; _dragAction = null;
        };
        _restore.Click += (_, _) => { if (_draft is not null) { _draft.Restore(); _slot = Math.Min(_slot, _draft.Menu.SegmentCount - 1); CaptureEdit(); } };
        Diagram.SelectedSlot += slot => { _slot = slot; _direction = -1; _showSlotHighlight = true; FilterActions(); };
        Diagram.Dropped += (slot, data) => { if (_draft is { Deleted: false }) { _slot = slot; _direction = -1; _showSlotHighlight = true; if (data is InputAction action) _draft.Assign(slot, action); else if (data is PieSlotDrag from) _draft.Swap(from.Slot, slot); CaptureEdit(); FilterActions(); } };
        _actions.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _pendingAction = null; _dragAction = null;
            // A scrollbar is inside the ListBox too. Only a press on an actual
            // action row may start action drag-and-drop; never steal a Thumb drag.
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(_actions, source) is ListBoxItem { Content: ActionDefinition action })
            { if (_direction < 0 && !ActionCatalog.SupportsPie(action.Action)) return; _pendingAction = _dragAction = action; _dragStart = e.GetPosition(_actions); }
        };
        _actions.PreviewMouseMove += (_, e) =>
        {
            ContinueActionDrag(e.GetPosition(_actions), e.LeftButton == MouseButtonState.Pressed, Mouse.Captured is System.Windows.Controls.Primitives.Thumb);
        };
        _actions.LostMouseCapture += (_, _) => ActionCaptureLost(Mouse.LeftButton == MouseButtonState.Pressed);
        AutomationProperties.SetName(_menus, "Saved pie menus"); AutomationProperties.SetName(_name, "Menu name"); AutomationProperties.SetName(_count, "Segment count"); AutomationProperties.SetName(_openWith, "Open with controller binding"); AutomationProperties.SetName(_search, "Search pie actions"); AutomationProperties.SetName(_actions, "Supported pie actions"); AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        Loaded += (_, _) => { _model.MenusChanged -= OnMenusChanged; _model.MenusChanged += OnMenusChanged; AttachHighlightWindow(Window.GetWindow(this)); OnMenusChanged(); };
        Unloaded += (_, _) => { _model.MenusChanged -= OnMenusChanged; AttachHighlightWindow(null); ClearSlotHighlight(); };
        Load(_model.Settings.PieMenus.FirstOrDefault()?.Id); FilterActions();
    }
    private void AttachHighlightWindow(Window? window)
    {
        if (ReferenceEquals(_highlightWindow, window)) return;
        if (_highlightWindow is not null) { _highlightWindow.PreviewMouseLeftButtonDown -= PreviewPointerDown; _highlightWindow.Deactivated -= PreviewDeactivated; }
        _highlightWindow = window;
        if (window is not null) { window.PreviewMouseLeftButtonDown += PreviewPointerDown; window.Deactivated += PreviewDeactivated; }
    }
    private void PreviewDeactivated(object? sender, EventArgs e) => ClearSlotHighlight();
    private void PreviewPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (!_showSlotHighlight) return;
        if (e.OriginalSource is Visual source && Diagram.IsAncestorOf(source) && Diagram.HitSlot(e.GetPosition((Canvas)Diagram.Child)) >= 0) return;
        ClearSlotHighlight();
    }
    private void ClearSlotHighlight()
    {
        if (!_showSlotHighlight) return;
        _showSlotHighlight = false;
        // Update only the wheel: rebuilding the D-pad or action palette here
        // would remove the control receiving the outside click.
        RenderDiagram();
    }
    public bool TryResolvePending()
    {
        CaptureEdit();
        if (!_model.Edits.HasDrafts) return true;
        var answer = Confirm("Save the Keybindings and Pie Menus drafts before continuing? Discard restores the last saved configuration.", MessageBoxButton.YesNoCancel);
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.No) { _model.Edits.Discard(EditTab.Keybindings); _model.Edits.Discard(EditTab.PieMenus); return true; }
        return _model.Edits.Save(EditTab.Keybindings, EditTab.PieMenus);
    }
    private void StartDrag(FrameworkElement source, InputAction action, IDataObject data)
    {
        if (DiagnosticDragRunner is { } run) run(source, action, data);
        else PieDragVisual.Run(_dragSurface, source, action, data, Diagram, DPadEditor);
    }
    internal void ActionCaptureLost(bool leftPressed) { if (!leftPressed) { _pendingAction = null; _dragAction = null; } }
    internal void ContinueActionDrag(Point point, bool leftPressed, bool scrollbarCaptured = false)
    {
        if (!leftPressed) { _pendingAction = null; _dragAction = null; return; }
        if (_dragAction is { } action && !scrollbarCaptured &&
            (Math.Abs(point.X - _dragStart.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(point.Y - _dragStart.Y) >= SystemParameters.MinimumVerticalDragDistance))
        {
            _pendingAction = null; _dragAction = null;
            StartDrag(_actions, action.Action, PieDragData.ForAction(action.Action));
        }
    }
    private void QueueSave() => CaptureEdit();
    private bool ConfirmBinding()
    {
        var conflict = _draft?.BindingConflict(_model.Edits.View(EditTab.PieMenus));
        return conflict is null || _draft!.Deleted || Confirm("This will replace " + _draft.OpenWith + "’s existing ‘" + conflict + "’ binding. Other bindings will be preserved. Continue?", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
    }
    public bool SelectMenu(Guid id)
    {
        if (_draft?.Menu.Id == id) return true;
        Load(id); FilterActions(); return true;
    }
    internal void SelectDPad(int direction) { if (direction is < 0 or > 3) return; _showSlotHighlight = false; _direction = direction; FilterActions(); }
    internal void AssignSelected(InputAction action)
    {
        if (_draft is not { Deleted: false }) return;
        if (_direction < 0 && !ActionCatalog.SupportsPie(action)) return;
        _refreshing = true; _actions.SelectedItem = null; _refreshing = false;
        if (_direction >= 0) _draft.AssignDPad(_direction, action); else _draft.Assign(_slot, action);
        CaptureEdit();
    }
    internal void RecordSelected(KeyboardChord chord)
    {
        if (_draft is not { Deleted: false }) return;
        _draft.Record(_direction >= 0 ? _direction : _slot, chord, _direction >= 0); CaptureEdit();
    }
    public bool BeginNew(string? presetId)
    {
        _draft = new(PiePresets.Find(presetId)?.Create() ?? PieMenu.Create("New pie menu", 4), _model.Edits.View(EditTab.PieMenus), true); _slot = 0; _direction = -1; _showSlotHighlight = false; CaptureEdit(); FilterActions(); return true;
    }
    public bool Apply()
    {
        CaptureEdit();
        if (_model.Edits.Save(EditTab.PieMenus)) { _saveError = null; Refresh(); return true; }
        _saveError = "Could not save. Edits retained. " + _model.Warning; Refresh(); return false;
    }
    private void CaptureEdit()
    {
        if (_draft is null || !_draft.Dirty) return;
        _applying = true;
        var id = _draft.Menu.Id;
        try { _model.Edits.Edit(EditTab.PieMenus, _draft.Apply(_model.Edits.View(EditTab.PieMenus))); }
        finally { _applying = false; }
        _saveError = null; Load(id, preserveSlot: true);
    }
    public void Discard() { _saveError = null; _model.Edits.Discard(EditTab.PieMenus); }
    private void Duplicate()
    {
        if (_draft is null) return;
        _draft = new(_draft.Menu.Duplicate(), _model.Edits.View(EditTab.PieMenus), true); _slot = 0; _showSlotHighlight = false; CaptureEdit();
    }
    private void Delete()
    {
        if (_draft is null) return;
        var affected = _model.Edits.View(EditTab.PieMenus).PieBindings.Where(p => p.Value == _draft.Menu.Id).Select(p => p.Key.ToString()).ToArray();
        if (_draft is null) return;
        if (Confirm("Delete ‘" + _draft.Menu.Name + "’? This removes it" + (affected.Length == 0 ? "." : " and clears these bindings: " + string.Join(", ", affected) + "."), MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _draft.Deleted = true; CaptureEdit();
    }
    private void Resize(int count)
    {
        if (_draft is null || count == _draft.Menu.SegmentCount) return;
        if (count < _draft.Menu.SegmentCount)
        {
            var removed = Enumerable.Range(count, _draft.Menu.SegmentCount - count).Select(i => "Segment " + (i + 1) + ": " + _draft.Menu.SlotLabel(i));
            if (Confirm("Reducing to " + count + " segments removes these assignments:\n" + string.Join("\n", removed) + "\nContinue?", MessageBoxButton.YesNo) != MessageBoxResult.Yes) { Refresh(); return; }
        }
        _draft.Resize(count); _slot = Math.Min(_slot, count - 1); CaptureEdit();
    }
    private void OnMenusChanged() { if (!_applying) Load(_draft?.Menu.Id, preserveSlot: true); }
    private void Load(Guid? id, bool preserveSlot = false)
    {
        if (!preserveSlot) _direction = -1;
        var view = _model.Edits.View(EditTab.PieMenus);
        var menu = view.PieMenus.FirstOrDefault(m => m.Id == id) ?? view.PieMenus.FirstOrDefault();
        if (!preserveSlot || menu?.Id != _draft?.Menu.Id) _showSlotHighlight = false;
        _draft = menu is null ? null : new(menu, view); _slot = preserveSlot ? Math.Min(_slot, (menu?.SegmentCount ?? 1) - 1) : 0; Refresh(true);
    }
    public void Refresh(bool preserveName = false)
    {
        _refreshing = true;
        if (!preserveName || !_name.IsKeyboardFocusWithin || _draft is null) _name.Text = _draft?.Menu.Name ?? "Create your first menu";
        _count.SelectedItem = _draft?.Menu.SegmentCount ?? 4;
        SetCurrentValue(MenuSizePercentProperty, _draft?.SizePercent ?? _model.Settings.PieMenuSizePercent);
        UpdatePreviewSize();
        SizeNumber.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        _openWith.SelectedItem = _openWith.Items.Cast<PieControlOption>().First(o => o.Control == _draft?.OpenWith);
        _center.IsEnabled = _name.IsEnabled = _count.IsEnabled = _openWith.IsEnabled = _actions.IsEnabled = _draft is { Deleted: false };
        _refreshing = false; RefreshList(); RefreshPreview();
    }
    private void RefreshList()
    {
        _refreshing = true;
        var options = _model.Edits.View(EditTab.PieMenus).PieMenus.Select(m => new PieMenuOption(m.Id, m.Name, MenuChanged(m))).ToList();
        _menus.ItemsSource = options; _menus.SelectedItem = options.FirstOrDefault(m => m.Id == _draft?.Menu.Id); _refreshing = false;
    }
    private void RenderDiagram()
    {
        var menu = _draft?.Menu;
        Diagram.Render(menu, menu is null || _direction >= 0 || !_showSlotHighlight ? -1 : _slot, editable: _draft is { Deleted: false }, opener: _draft?.OpenWith?.ToString(), saved: _model.Settings.PieMenus.FirstOrDefault(m => m.Id == menu?.Id), showDraft: true);
    }
    private void RefreshPreview()
    {
        var menu = _draft?.Menu;
        RenderDiagram();
        DPadEditor.Render(menu, _model.Settings.PieMenus.FirstOrDefault(m => m.Id == menu?.Id), _direction);
        var selectedAction = menu is null ? (InputAction?)null : _direction >= 0 ? menu.DPadActions[_direction] : menu.Slots[_slot];
        var recorded = menu is null ? null : (_direction >= 0 ? menu.RecordedDPad : menu.RecordedSlots).GetValueOrDefault(_direction >= 0 ? _direction : _slot);
        _selection.Text = menu is null ? "Create a menu to begin" : "Selected: " + menu.Name + " · " + (_direction >= 0 ? "D-pad " + PieMenu.DPadDirections[_direction] : "Segment " + (_slot + 1)) + "\n" + (recorded?.Label ?? PieDPadEditor.Label(selectedAction));
        RecordKeysButton.IsEnabled = _draft is { Deleted: false };
        _removeOverride.Visibility = _direction >= 0 ? Visibility.Visible : Visibility.Collapsed;
        _removeOverride.IsEnabled = selectedAction is not null;
        SetValue(IsDPadTargetProperty, _direction >= 0);
        _restore.IsEnabled = _draft is { Deleted: false };
        _restore.Content = menu is null || FactoryDefaults.Menu(menu) is null && PiePresets.Find(menu.PresetId) is null ? "Clear all actions" : "Restore defaults";
        AutomationProperties.SetName(_restore, (string)_restore.Content);
        _status.Text = _saveError ?? (_model.Edits.Dirty(EditTab.PieMenus) ? $"● {_model.Edits.Count(EditTab.PieMenus)} unsaved changes · Pie Menus only · Apply with Save" : "Pie Menus only · Changes take effect when you save");
        _status.Foreground = _model.Edits.Dirty(EditTab.PieMenus) ? EditChrome.CherryInk : ThemePalette.Get("#161B17");
        _undo.IsEnabled = _model.Edits.CanUndo(EditTab.PieMenus); _redo.IsEnabled = _model.Edits.CanRedo(EditTab.PieMenus);
        _save.IsEnabled = _discard.IsEnabled = _model.Edits.Dirty(EditTab.PieMenus);
        var saved = _model.Settings.PieMenus.FirstOrDefault(m => m.Id == menu?.Id);
        Highlight(_name, menu is not null && (saved is null || saved.Name != menu.Name), saved?.Name ?? "No menu");
        Highlight(_count, menu is not null && (saved is null || saved.SegmentCount != menu.SegmentCount), saved?.SegmentCount.ToString() ?? "No menu");
        var savedOpener = menu is null ? (Control?)null : _model.Settings.PieBindings.Where(p => p.Value == menu.Id).Select(p => (Control?)p.Key).FirstOrDefault();
        Highlight(_openWith, _draft is not null && _draft.OpenWith != savedOpener, savedOpener?.ToString() ?? "Unassigned");
        var sizeDirty = _draft is not null && _draft.SizePercent != _model.Settings.PieMenuSizePercent;
        Highlight(SizeNumber, sizeDirty, WholeNumbers.Format(_model.Settings.PieMenuSizePercent) + "%");
        SizeSlider.Background = sizeDirty ? EditChrome.Cherry : Brushes.Transparent;
        SizeSlider.Foreground = sizeDirty ? EditChrome.CherryInk : ThemePalette.Get("#BAEF6C");
        var extra = menu is null ? [] : _model.Edits.View(EditTab.PieMenus).PieBindings.Where(p => p.Value == menu.Id && p.Key != _draft!.OpenWith && p.Key != _draft.RemovedOpener).Select(p => p.Key.ToString()).ToArray();
        var conflict = _draft?.BindingConflict(_model.Edits.View(EditTab.PieMenus));
        _bindingHelp.Text = conflict is not null ? "Replacing " + _draft!.OpenWith + ": " + conflict + "." : extra.Length > 0 ? "Additional bindings kept: " + string.Join(", ", extra) : "Aim + hold → release. Shared with Keybindings.";
        if (_draft?.OpenWith >= Control.DPadUp && _model.Settings.DPadMode != DPadMode.AssignedActions) _bindingHelp.Text += " Enable D-pad assigned-actions mode to use this binding.";
        if (_draft?.OpenWith >= Control.DPadUp) _bindingHelp.Text += " The opening direction stays reserved until release.";
    }
    private bool MenuChanged(PieMenu menu)
    {
        var saved = _model.Settings.PieMenus.FirstOrDefault(m => m.Id == menu.Id);
        return saved is null || saved.Name != menu.Name || !saved.Slots.SequenceEqual(menu.Slots) || !saved.DPadActions.SequenceEqual(menu.DPadActions) ||
            KeyboardChord.Fingerprint(saved.RecordedSlots) != KeyboardChord.Fingerprint(menu.RecordedSlots) || KeyboardChord.Fingerprint(saved.RecordedDPad) != KeyboardChord.Fingerprint(menu.RecordedDPad) ||
            !_model.Settings.PieBindings.Where(p => p.Value == menu.Id).OrderBy(p => p.Key).SequenceEqual(_model.Edits.View(EditTab.PieMenus).PieBindings.Where(p => p.Value == menu.Id).OrderBy(p => p.Key));
    }
    private static void Highlight(System.Windows.Controls.Control control, bool changed, string saved)
    {
        if (changed) control.Background = EditChrome.Cherry; else control.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        control.ToolTip = changed ? "Unsaved · Saved: " + saved : null;
    }
    private void FilterActions()
    {
        // Target changes update only the target details. Search alone changes this list.
        var entries = ActionCatalog.PickerEntries.Where(a => a.Label.Contains(_search.Text, StringComparison.OrdinalIgnoreCase) || a.Category.Contains(_search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (_actions.Items.Cast<ActionDefinition>().SequenceEqual(entries)) { RefreshPreview(); return; }
        var view = new ListCollectionView(entries);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ActionDefinition.Category))); _refreshing = true; _actions.ItemsSource = view; _refreshing = false; RefreshPreview();
    }
    private static readonly Brush Accent = ThemePalette.Get("#427B0E");
    private void UpdatePreviewSize()
    {
        // Fit the editor's own pane; global Menu size still affects desktop pies only.
        var diameter = Math.Max(1, Math.Min(_preview.ActualWidth * 1.15 / 2.15, _preview.ActualHeight) - 20);
        Diagram.Width = Diagram.Height = diameter;
    }
    private sealed class SizeRule : ValidationRule
    {
        public override ValidationResult Validate(object value, System.Globalization.CultureInfo cultureInfo) =>
            double.TryParse(value?.ToString(), System.Globalization.NumberStyles.Float, cultureInfo, out var number) && double.IsFinite(number) && number is >= 100 and <= 200
            ? ValidationResult.ValidResult : new(false, "Enter a size from 100 to 200 percent.");
    }
    private sealed class PieActionAvailability : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            values[0] is InputAction action && (values[1] is true || ActionCatalog.SupportsPie(action));
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
    private static TextBlock Text(string value, double size, bool bold = false) => new() { Text = value, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Foreground = ThemePalette.Get("#161B17") };
    private static Button Button(string text) { var button = new Button { Content = text, Padding = new Thickness(10, 8, 10, 8), MinHeight = 38 }; AutomationProperties.SetName(button, text); return button; }
    private static void Rows(Grid grid, params GridLength[] heights) { foreach (var height in heights) grid.RowDefinitions.Add(new() { Height = height }); }
    private static void Add(Grid grid, UIElement element, int row) { Grid.SetRow(element, row); grid.Children.Add(element); }
    private static void Card(Grid grid, UIElement child, int column) { var card = new Border { Child = child, Background = ThemePalette.Get("#FFFFFF"), BorderBrush = ThemePalette.Get("#DCE6E5"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Margin = new Thickness(column == 0 ? 0 : 8, 0, 0, 0) }; Grid.SetColumn(card, column); grid.Children.Add(card); }
    private static Border Section(UIElement child) => new() { Child = child, BorderBrush = ThemePalette.Get("#DCE6E5"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8) };
    private const string SearchTemplate = """
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="TextBox">
        <Border x:Name="SearchFrame" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="12">
        <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="34"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
        <TextBlock Text="⌕" FontSize="23" HorizontalAlignment="Center" VerticalAlignment="Center" IsHitTestVisible="False" Foreground="{DynamicResource Muted}"/>
        <ScrollViewer x:Name="PART_ContentHost" Grid.Column="1" VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/></Grid></Border>
        <ControlTemplate.Triggers><Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="SearchFrame" Property="BorderBrush" Value="{DynamicResource Theme84BC3B}"/></Trigger></ControlTemplate.Triggers>
        </ControlTemplate>
        """;
}
