using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Control = System.Windows.Controls.Control;

namespace FullTilt;

// In-window overlay: dims everything except the focused area, never covers the desktop or takes foreground focus.
// It only reads the UI; tab selection is restored when it closes.
internal sealed class OnboardingTour : Adorner
{
    private static readonly TimeSpan Glide = TimeSpan.FromMilliseconds(260), Enter = TimeSpan.FromMilliseconds(180);
    private static readonly SolidColorBrush Lime = new(Color.FromRgb(200, 243, 91)), Forest = new(Color.FromRgb(24, 37, 27));
    private readonly MainWindow _window;
    private readonly TourContext _context;
    private readonly IReadOnlyList<TourStep> _steps = TourSteps.Create();
    private readonly Action<bool> _finished;
    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly Rectangle _blocker = new() { Fill = Brushes.Transparent };
    private readonly Path _dim = new() { IsHitTestVisible = false }, _ring = new() { IsHitTestVisible = false, Stroke = Lime, StrokeThickness = 2 };
    private readonly RectangleGeometry _full = new();
    private readonly GeometryGroup _holes = new();
    private readonly Canvas _lines = new() { IsHitTestVisible = false };
    private readonly Border _bubble = new() { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(24, 22, 24, 18), Width = 400, Focusable = false };
    private readonly Path _arrowFill = new() { Fill = Brushes.Transparent, IsHitTestVisible = false }, _arrowEdge = new() { StrokeThickness = 1, IsHitTestVisible = false };
    private readonly BrandLogo _logo = new() { Width = 168, Height = 46, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };
    private readonly TextBlock _title = new() { FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _body = new() { Margin = new Thickness(0, 10, 0, 18) };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _count = new() { FontSize = 13, MinWidth = 56, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(12, 0, 0, 0) };
    private readonly Button _skip, _back, _next;
    private readonly List<Border> _dotItems = [];
    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly int _startTab;
    private int _index = -1;
    private bool _closing;
    private string _signature = "";
    private IInputElement? _previousFocus;

    internal bool IsOpen => !_closing;
    internal int Index => _index;
    internal int Count => _steps.Count;
    internal Rect? BubbleBounds { get; private set; }
    internal IReadOnlyList<Rect> HoleBounds { get; private set; } = [];

    // finished(completed): true after Done or Skip, so first-run users are not shown it again.
    internal OnboardingTour(MainWindow window, SettingsViewModel model, Action<bool> finished, UIElement? host = null) : base(host ?? (UIElement)window.Content)
    {
        _window = window; _context = new TourContext(window, model) { Root = host ?? window.Content as DependencyObject }; _finished = finished; _startTab = window.SettingsTabs.SelectedIndex;
        _skip = Link("Skip tour", () => Close(true)); _back = Pill("Back", () => Go(-1)); _next = Pill("Next", () => { if (_index == _steps.Count - 1) Close(true); else Go(1); }, true);
        _bubble.SetResourceReference(Border.BackgroundProperty, "ThemeFFFFFF"); _bubble.SetResourceReference(Border.BorderBrushProperty, "ThemeE0E5DC");
        _bubble.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = .35, Direction = 270 };
        _title.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); _count.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _arrowFill.SetResourceReference(Shape.FillProperty, "ThemeFFFFFF"); _arrowEdge.SetResourceReference(Shape.StrokeProperty, "ThemeE0E5DC");
        for (var i = 0; i < _steps.Count; i++)
        {
            var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 6, 0) };
            _dotItems.Add(dot); _dots.Children.Add(dot);
        }
        var footer = new Grid(); footer.ColumnDefinitions.Add(new()); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footer.RowDefinitions.Add(new() { Height = GridLength.Auto }); footer.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var progress = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) }; progress.Children.Add(_dots); progress.Children.Add(_count);
        Grid.SetColumnSpan(progress, 2); footer.Children.Add(progress);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; actions.Children.Add(_back); actions.Children.Add(_next);
        Grid.SetRow(_skip, 1); Grid.SetRow(actions, 1); Grid.SetColumn(actions, 1); footer.Children.Add(_skip); footer.Children.Add(actions);
        var stack = new StackPanel(); stack.Children.Add(_logo); stack.Children.Add(_title); stack.Children.Add(_body); stack.Children.Add(footer);
        _bubble.Child = stack;
        AutomationProperties.SetLiveSetting(_bubble, AutomationLiveSetting.Polite);
        _holes.FillRule = FillRule.Nonzero;
        _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, _full, _holes); _ring.Data = _holes;
        _canvas.Children.Add(_blocker); _canvas.Children.Add(_dim); _canvas.Children.Add(_ring); _canvas.Children.Add(_lines);
        _canvas.Children.Add(_arrowFill); _canvas.Children.Add(_arrowEdge); _canvas.Children.Add(_bubble);
        AddVisualChild(_canvas); AddLogicalChild(_canvas);
        _settle.Tick += (_, _) => { _settle.Stop(); Refresh(false); };
        window.PreviewKeyDown += OnKey; window.LayoutUpdated += OnLayout;
        IsHitTestVisible = true;
    }
    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _canvas;
    protected override System.Collections.IEnumerator LogicalChildren => new[] { _canvas }.GetEnumerator();
    protected override Size MeasureOverride(Size constraint) { var size = AdornedElement.RenderSize; _canvas.Measure(size); return size; }
    protected override Size ArrangeOverride(Size size) { _canvas.Arrange(new Rect(size)); return size; }

    internal void Start(AdornerLayer layer)
    {
        layer.Add(this); _previousFocus = Keyboard.FocusedElement;
        _dim.Fill = new SolidColorBrush(Color.FromArgb((byte)(ThemePalette.IsDark ? 168 : 128), 0, 0, 0));
        if (UiMotion.Enabled) _dim.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Enter));
        if (UiMotion.Enabled) _ring.BeginAnimation(OpacityProperty, new DoubleAnimation(1, .55, TimeSpan.FromMilliseconds(950)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Show(0)));
    }
    internal void Go(int delta) { var next = Math.Clamp(_index + delta, 0, _steps.Count - 1); if (next != _index) Show(next); }
    internal void GoTo(int index) => Show(Math.Clamp(index, 0, _steps.Count - 1));

    internal void Close(bool completed)
    {
        if (_closing) return;
        _closing = true; _settle.Stop();
        _window.PreviewKeyDown -= OnKey; _window.LayoutUpdated -= OnLayout;
        _window.ShowFileMenuForTour?.Invoke(false);
        if (_window.SettingsTabs.SelectedIndex != _startTab) _window.SettingsTabs.SelectedIndex = _startTab;
        void Remove()
        {
            (VisualTreeHelper.GetParent(this) as AdornerLayer)?.Remove(this);
            if (_previousFocus is IInputElement { } previous && previous != _next) Keyboard.Focus(previous);
            _finished(completed);
        }
        if (!UiMotion.Enabled) { Remove(); return; }
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160)); fade.Completed += (_, _) => Remove();
        _canvas.BeginAnimation(OpacityProperty, fade);
    }

    private void Show(int index)
    {
        if (_closing) return;
        var step = _steps[index]; _index = index; _signature = "";
        if (step.Tab is { } tab && _window.SettingsTabs.SelectedIndex != tab) _window.SettingsTabs.SelectedIndex = tab;
        _window.ShowFileMenuForTour?.Invoke(step.FileMenu);
        AdornedElement.UpdateLayout();
        var (title, body) = step.Text(_context);
        _title.Text = title; _logo.Visibility = step.Welcome ? Visibility.Visible : Visibility.Collapsed; _title.FontSize = step.Welcome ? 26 : 22;
        FillBody(body);
        _count.Text = $"{index + 1} of {_steps.Count}";
        _back.Visibility = index == 0 ? Visibility.Collapsed : Visibility.Visible;
        _skip.Visibility = index == _steps.Count - 1 ? Visibility.Collapsed : Visibility.Visible;
        _next.Content = index == 0 ? "Start tour" : index == _steps.Count - 1 ? "Done" : "Next";
        AutomationProperties.SetName(_next, (string)_next.Content);
        AutomationProperties.SetName(_bubble, $"Tour step {index + 1} of {_steps.Count}: {title}");
        for (var i = 0; i < _dotItems.Count; i++) SetDot(_dotItems[i], i == index, i < index);
        Refresh(true);
        _settle.Stop(); _settle.Start(); // The tab fade offsets content by a few pixels; re-measure once it settles.
        _next.Focus();
    }
    private void SetDot(Border dot, bool active, bool seen)
    {
        if (active) dot.Background = Lime; else dot.SetResourceReference(Border.BackgroundProperty, seen ? "Muted" : "ThemeE0E5DC");
        var width = active ? 18d : 6d;
        if (UiMotion.Enabled) dot.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }); else { dot.BeginAnimation(WidthProperty, null); dot.Width = width; }
    }
    private void FillBody(string text)
    {
        _body.Children.Clear();
        foreach (var paragraph in text.Split('\n'))
        {
            var callout = paragraph.StartsWith("!!", StringComparison.Ordinal);
            var block = new TextBlock { FontSize = 15, LineHeight = 22, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, callout ? 0 : 6) };
            var parts = (callout ? paragraph[2..] : paragraph).Split("**");
            for (var i = 0; i < parts.Length; i++) block.Inlines.Add(new Run(parts[i]) { FontWeight = i % 2 == 1 || callout ? FontWeights.SemiBold : FontWeights.Normal });
            if (callout)
            {
                block.Foreground = ThemePalette.Get("#161B17");
                _body.Children.Add(new Border { Background = ThemePalette.Get("#ECFBCB"), BorderBrush = ThemePalette.Get("#84BC3B"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 4, 0, 10), Child = block });
            }
            else { block.SetResourceReference(TextBlock.ForegroundProperty, "Ink"); _body.Children.Add(block); }
        }
    }

    private void OnLayout(object? sender, EventArgs e) { if (!_closing && _index >= 0 && Signature() != _signature) Refresh(false); }
    private string Signature() => $"{_canvas.ActualWidth:0}x{_canvas.ActualHeight:0}|" + string.Join(";", Resolve().Select(r => r.rect.ToString()));

    private List<(Rect rect, FrameworkElement? connect, FrameworkElement? first)> Resolve()
    {
        var result = new List<(Rect, FrameworkElement?, FrameworkElement?)>();
        var step = _steps[_index];
        if (step.Spots is null || AdornedElement is not FrameworkElement root || root.ActualWidth < 1) return result;
        IReadOnlyList<TourSpot> spots;
        try { spots = step.Spots(_context); } catch (InvalidOperationException) { return result; }
        foreach (var spot in spots)
        {
            Rect? union = null;
            foreach (var element in spot.Elements)
            {
                if (!TourContext.Shown(element) || element.ActualWidth < 1) continue;
                element.BringIntoView();
                var bounds = element.TransformToVisual(root).TransformBounds(new Rect(element.RenderSize));
                union = union is { } current ? Rect.Union(current, bounds) : bounds;
            }
            if (union is { } found) result.Add((found, spot.ConnectTo, spot.Elements[0]));
        }
        return result;
    }

    private void Refresh(bool animate)
    {
        if (_closing || _index < 0 || AdornedElement is not FrameworkElement root) return;
        var step = _steps[_index];
        var bounds = new Rect(0, 0, Math.Max(1, root.ActualWidth), Math.Max(1, root.ActualHeight));
        _canvas.Width = _blocker.Width = bounds.Width; _canvas.Height = _blocker.Height = bounds.Height; _full.Rect = bounds;
        var spots = Resolve(); _signature = Signature();
        var rects = spots.Select(s => Rect.Inflate(s.rect, 7, 7)).Select(r => Rect.Intersect(r, bounds)).Where(r => !r.IsEmpty).ToList();
        // The page's navigation tab is lit too, so people can see where they are; it never steers bubble placement.
        var tab = step.Tab is { } index && index < _window.SettingsTabs.Items.Count && _window.SettingsTabs.ItemContainerGenerator.ContainerFromIndex(index) is TabItem item && TourContext.Shown(item) && item.ActualWidth > 0
            ? Rect.Intersect(Rect.Inflate(item.TransformToVisual(root).TransformBounds(new Rect(item.RenderSize)), 3, 3), bounds) : Rect.Empty;
        var lit = tab.IsEmpty ? rects : [.. rects, tab];
        HoleBounds = lit;
        SetHoles(lit, animate && UiMotion.Enabled);
        Connect(spots, animate && UiMotion.Enabled);
        _bubble.Measure(new Size(Math.Min(400, Math.Max(240, bounds.Width - 24)), double.PositiveInfinity));
        var size = new Size(_bubble.Width, _bubble.DesiredSize.Height);
        var anchor = step.Anchor?.Invoke(_context) is { } host && TourContext.Shown(host) ? host.TransformToVisual(root).TransformBounds(new Rect(host.RenderSize)) : (Rect?)null;
        var avoid = new List<Rect>(); if (!tab.IsEmpty) avoid.Add(tab);
        if (step.FileMenu && _window.FileMenuBounds?.Invoke(root) is { } menu) avoid.Add(menu);
        var (place, arrow) = Place(step, size, bounds, rects, anchor, avoid);
        BubbleBounds = place;
        var moved = _bubble.RenderTransform as TranslateTransform ?? new TranslateTransform(); _bubble.RenderTransform = moved;
        Canvas.SetLeft(_bubble, place.X); Canvas.SetTop(_bubble, place.Y);
        DrawArrow(arrow, place);
        if (animate && UiMotion.Enabled)
        {
            _bubble.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Enter));
            moved.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, Enter) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        else { _bubble.BeginAnimation(OpacityProperty, null); moved.BeginAnimation(TranslateTransform.YProperty, null); moved.Y = 0; _bubble.Opacity = 1; }
    }

    private void SetHoles(List<Rect> rects, bool animate)
    {
        while (_holes.Children.Count > rects.Count) _holes.Children.RemoveAt(_holes.Children.Count - 1);
        for (var i = 0; i < rects.Count; i++)
        {
            var radius = Math.Min(12, Math.Min(rects[i].Width, rects[i].Height) / 2);
            RectangleGeometry geometry;
            if (i < _holes.Children.Count) geometry = (RectangleGeometry)_holes.Children[i];
            else { var center = new Point(rects[i].X + rects[i].Width / 2, rects[i].Y + rects[i].Height / 2); geometry = new RectangleGeometry(new Rect(center, new Size(0, 0))); _holes.Children.Add(geometry); }
            geometry.RadiusX = geometry.RadiusY = radius;
            var from = geometry.Rect;
            geometry.BeginAnimation(RectangleGeometry.RectProperty, null); geometry.Rect = rects[i];
            if (animate && from != rects[i]) geometry.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(from, rects[i], Glide) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }
    private void Connect(List<(Rect rect, FrameworkElement? connect, FrameworkElement? first)> spots, bool animate)
    {
        _lines.Children.Clear();
        if (AdornedElement is not FrameworkElement root) return;
        foreach (var spot in spots)
        {
            if (spot.connect is not { } dot || !TourContext.Shown(dot)) continue;
            var target = dot.TransformToVisual(root).TransformBounds(new Rect(dot.RenderSize)); var center = new Point(target.X + target.Width / 2, target.Y + target.Height / 2);
            var onLeft = spot.rect.X + spot.rect.Width / 2 < center.X; var start = new Point(onLeft ? spot.rect.Right : spot.rect.X, spot.rect.Y + spot.rect.Height / 2);
            var line = new Line { X1 = start.X, Y1 = start.Y, X2 = center.X, Y2 = center.Y, Stroke = Lime, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            _lines.Children.Add(line);
            _lines.Children.Add(new Ellipse { Width = 34, Height = 34, Stroke = Lime, StrokeThickness = 2, Margin = new Thickness(center.X - 17, center.Y - 17, 0, 0) });
            if (animate)
            {
                line.BeginAnimation(Line.X2Property, new DoubleAnimation(start.X, center.X, Glide) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                line.BeginAnimation(Line.Y2Property, new DoubleAnimation(start.Y, center.Y, Glide) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }
    }

    // Prefers the step's sides, never covers a highlighted area, and always stays inside the window.
    private static (Rect bubble, Rect? target) Place(TourStep step, Size size, Rect bounds, List<Rect> holes, Rect? anchor, List<Rect> avoid)
    {
        const double Gap = 20, Edge = 12;
        var inner = Rect.Inflate(bounds, -Edge, -Edge);
        Rect? union = holes.Count == 0 ? null : holes.Aggregate(Rect.Union);
        Rect Clamp(Rect rect) => new(Math.Clamp(rect.X, inner.X, Math.Max(inner.X, inner.Right - rect.Width)), Math.Clamp(rect.Y, inner.Y, Math.Max(inner.Y, inner.Bottom - rect.Height)), rect.Width, rect.Height);
        bool Fits(Rect rect) => inner.Contains(rect) && holes.All(h => !h.IntersectsWith(Rect.Inflate(rect, 4, 4))) && avoid.All(a => !a.IntersectsWith(Rect.Inflate(rect, 4, 4)));
        // Slide right past anything that would hide the bubble, such as the open File menu.
        Rect RightOf(double x, double y)
        {
            foreach (var other in avoid)
                if (other.Right + Gap > x && other.Left < x + size.Width && other.Top < y + size.Height && other.Bottom > y) x = other.Right + Gap;
            return new Rect(x, y, size.Width, size.Height);
        }
        Rect? Candidate(TourSide side)
        {
            if (side == TourSide.Center) return Clamp(new Rect(bounds.X + (bounds.Width - size.Width) / 2, bounds.Y + (bounds.Height - size.Height) / 2, size.Width, size.Height));
            if (side == TourSide.AnchorBottom)
                return anchor is { } host ? Clamp(new Rect(host.X + (host.Width - size.Width) / 2, host.Bottom - size.Height - Edge, size.Width, size.Height)) : null;
            if (union is not { } area) return null;
            var middle = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);
            return side switch
            {
                TourSide.Right => Clamp(RightOf(area.Right + Gap, middle.Y - size.Height / 2)),
                TourSide.Left => Clamp(new Rect(area.X - Gap - size.Width, middle.Y - size.Height / 2, size.Width, size.Height)),
                TourSide.Below => Clamp(new Rect(middle.X - size.Width / 2, area.Bottom + Gap, size.Width, size.Height)),
                _ => Clamp(new Rect(middle.X - size.Width / 2, area.Y - Gap - size.Height, size.Width, size.Height)),
            };
        }
        foreach (var side in step.Prefer.Concat([TourSide.Right, TourSide.Left, TourSide.Below, TourSide.Above, TourSide.AnchorBottom, TourSide.Center]))
            if (Candidate(side) is { } rect && Fits(rect)) return (rect, side is TourSide.Center or TourSide.AnchorBottom ? null : Nearest(holes, rect) is { } near && Distance(rect, near) < 80 ? near : null);
        return (Candidate(TourSide.Center)!.Value, null);
    }
    private static double Distance(Rect a, Rect b) => Math.Max(Math.Max(b.X - a.Right, a.X - b.Right), Math.Max(b.Y - a.Bottom, a.Y - b.Bottom));
    private static Rect? Nearest(List<Rect> holes, Rect bubble)
    {
        if (holes.Count == 0) return null;
        var center = new Point(bubble.X + bubble.Width / 2, bubble.Y + bubble.Height / 2);
        return holes.OrderBy(h => Math.Abs(h.X + h.Width / 2 - center.X) + Math.Abs(h.Y + h.Height / 2 - center.Y)).First();
    }
    private void DrawArrow(Rect? target, Rect bubble)
    {
        _arrowFill.Data = _arrowEdge.Data = Geometry.Empty;
        if (target is not { } hole) return;
        // Point from the bubble edge facing the highlighted area.
        Point tip, a, b; const double Size = 9;
        if (bubble.X >= hole.Right - 1) { var y = Math.Clamp(hole.Y + hole.Height / 2, bubble.Y + 22, bubble.Bottom - 22); tip = new(bubble.X - Size, y); a = new(bubble.X + 1, y - Size); b = new(bubble.X + 1, y + Size); }
        else if (bubble.Right <= hole.X + 1) { var y = Math.Clamp(hole.Y + hole.Height / 2, bubble.Y + 22, bubble.Bottom - 22); tip = new(bubble.Right + Size, y); a = new(bubble.Right - 1, y - Size); b = new(bubble.Right - 1, y + Size); }
        else if (bubble.Y >= hole.Bottom - 1) { var x = Math.Clamp(hole.X + hole.Width / 2, bubble.X + 28, bubble.Right - 28); tip = new(x, bubble.Y - Size); a = new(x - Size, bubble.Y + 1); b = new(x + Size, bubble.Y + 1); }
        else { var x = Math.Clamp(hole.X + hole.Width / 2, bubble.X + 28, bubble.Right - 28); tip = new(x, bubble.Bottom + Size); a = new(x - Size, bubble.Bottom - 1); b = new(x + Size, bubble.Bottom - 1); }
        _arrowFill.Data = new PathGeometry([new PathFigure(a, [new LineSegment(tip, false), new LineSegment(b, false)], true)]);
        _arrowEdge.Data = new PathGeometry([new PathFigure(a, [new LineSegment(tip, true), new LineSegment(b, true)], false)]);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_closing) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { e.Handled = true; Close(true); }
        else if (key == Key.Right) { e.Handled = true; if (_index == _steps.Count - 1) Close(true); else Go(1); }
        else if (key == Key.Left) { e.Handled = true; Go(-1); }
        else if (key == Key.Tab)
        {
            e.Handled = true; var focus = Keyboard.FocusedElement as DependencyObject;
            var buttons = new[] { _skip, _back, _next }.Where(b => b.Visibility == Visibility.Visible).ToList();
            var current = buttons.FindIndex(b => b == focus);
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
            buttons[((current < 0 ? (step > 0 ? -1 : 0) : current) + step + buttons.Count) % buttons.Count].Focus();
        }
        else if (key is Key.Enter or Key.Space && Keyboard.FocusedElement is not Button) { e.Handled = true; _next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    }

    private Button Link(string text, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(2, 6, 8, 6), MinHeight = 0, Cursor = Cursors.Hand, Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        button.SetResourceReference(Control.ForegroundProperty, "Muted"); AutomationProperties.SetName(button, text);
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = Factory() };
        button.MouseEnter += (_, _) => button.SetResourceReference(Control.ForegroundProperty, "Ink"); button.MouseLeave += (_, _) => button.SetResourceReference(Control.ForegroundProperty, "Muted");
        button.Click += (_, _) => click(); return button;
        static FrameworkElementFactory Factory() { var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty)); var content = new FrameworkElementFactory(typeof(ContentPresenter)); border.AppendChild(content); return border; }
    }
    private Button Pill(string text, Action click, bool primary = false)
    {
        var button = new Button { Content = text, MinWidth = primary ? 96 : 72, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(18, 9, 18, 9), FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal };
        if (primary) { button.Background = Lime; button.Foreground = Forest; }
        AutomationProperties.SetName(button, text); UiMotion.SetInteractive(button, true); button.Click += (_, _) => click(); return button;
    }
}
