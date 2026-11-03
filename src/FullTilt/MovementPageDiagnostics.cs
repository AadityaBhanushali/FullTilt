using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FullTilt.Core;
using Control = FullTilt.Core.Control;

namespace FullTilt;

internal static class MovementPageDiagnostics
{
    public static string Verify(string output)
    {
        using var model = new SettingsViewModel(new AppSettings { Mappings = new(AppSettings.DefaultMappings()) { [Control.L3] = InputAction.Undo, [Control.R3] = InputAction.ToggleKeyboard } }, new SettingsStore(Path.Combine(output, "movement-page-isolated")), null, null);
        var window = new MainWindow(model) { AllowClose = true }; ((TabControl)window.FindName("SettingsTabs")).SelectedIndex = 0;
        var editor = (MovementEditor)((ContentControl)window.FindName("MovementSections")).Content;
        var root = (FrameworkElement)window.Content;
        void Layout(double width = 1700, double height = 1000) { root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); }
        void Render(string name, double width = 1700, double height = 1000)
        {
            Layout(width, height); var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(output, name + ".png")); encoder.Save(stream);
        }
        void Number(string name, string value)
        {
            var field = (TextBox)editor.DiagnosticControls[name]; field.SetCurrentValue(TextBox.TextProperty, value); field.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            if (Validation.GetHasError(field)) throw new InvalidOperationException("Valid movement entry was rejected: " + name);
            field.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent)); Layout();
        }
        void Click(string name) => ((Button)editor.DiagnosticControls[name]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Number("CursorNumber", "642"); Number("PrecisionNumber", "128.4"); Number("SpeedBoostNumber", "2311.2"); Number("ScrollNumber", "13.4"); Number("ScrollBoostNumber", "40.2"); Number("LeftDeadZoneNumber", "5"); Number("RightDeadZoneNumber", "5");
        if (model.PrecisionCursorSpeed != 128 || model.BoostedCursorSpeed != 2311 || Math.Abs(model.Settings.ScrollBoostMultiplier - 40d / 13) > .00001) throw new InvalidOperationException("Independent rounded speeds did not apply.");
        var boostBefore = model.SpeedBoostMultiplier; var scrollBoostBefore = model.Settings.ScrollBoostMultiplier;
        Number("CursorNumber", "1000"); Number("ScrollNumber", "10");
        if (model.SpeedBoostMultiplier != boostBefore || model.Settings.ScrollBoostMultiplier != scrollBoostBefore || model.BoostedCursorSpeed != 2311 || model.PrecisionCursorSpeed != 128) throw new InvalidOperationException("Normal speed refresh changed a stored multiplier or percentage.");
        Number("SpeedBoostNumber", "80000"); Number("PrecisionNumber", "9000");
        if (model.BoostedCursorSpeed != 80000 || model.PrecisionCursorSpeed != 9000 || model.CursorSpeed != 1000) throw new InvalidOperationException("Absolute speeds still depend on normal speed or a 10x limit.");
        Number("CursorNumber", "642"); Number("ScrollNumber", "13.4");
        var reverse = (CheckBox)editor.DiagnosticControls["ReverseScrollSwitch"]; reverse.SetCurrentValue(CheckBox.IsCheckedProperty, true); if (!model.ReverseScroll) throw new InvalidOperationException("Reverse switch is not bound."); reverse.SetCurrentValue(CheckBox.IsCheckedProperty, false);
        ThemePalette.Apply(true); Render("movement-reference-dark"); ThemePalette.Apply(false); Render("movement-reference-light");
        foreach (var (name, expected) in new[] { ("LinearResponseButton", 1d), ("BalancedResponseButton", 1.8), ("FinecontrolResponseButton", 2.6) }) { Click(name); if (model.ResponseCurve != expected) throw new InvalidOperationException("Response preset failed: " + name); }
        var curve = model.ResponseCurve; Click("CustomizeCurveButton"); Layout(); if (model.ResponseCurve != curve) throw new InvalidOperationException("Customize curve changed the stored curve before editing."); Number("CurveNumber", "173");
        ((Expander)editor.DiagnosticControls["DPadMovementExpander"]).IsExpanded = true; Layout();
        foreach (var name in new[] { "CursorNumber", "PrecisionNumber", "SpeedBoostNumber", "ScrollNumber", "ScrollBoostNumber", "LeftDeadZoneNumber", "RightDeadZoneNumber", "CurveNumber", "TapNumber", "DPadSpeedNumber" })
        {
            var number = (TextBox)editor.DiagnosticControls[name]; var row = Descendants<MovementValueRow>(editor).Single(r => ReferenceEquals(r.Number, number));
            var original = double.Parse(number.Text, System.Globalization.CultureInfo.CurrentCulture);
            var increase = Descendants<Button>(row).Single(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Increase ", StringComparison.Ordinal)); var decrease = Descendants<Button>(row).Single(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Decrease ", StringComparison.Ordinal));
            increase.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (double.Parse(number.Text, System.Globalization.CultureInfo.CurrentCulture) <= original) throw new InvalidOperationException("Numeric increase failed: " + name);
            decrease.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var beforeSlider = model.Settings; row.Slider.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty, row.Slider.Minimum + (row.Slider.Maximum - row.Slider.Minimum) * .65);
            if (model.Settings == beforeSlider || Math.Abs(row.Slider.Value - double.Parse(number.Text, System.Globalization.CultureInfo.CurrentCulture)) > .001) throw new InvalidOperationException("Slider/number synchronization failed: " + name);
            var accepted = model.Settings;
            foreach (var invalid in new[] { "NaN", "Infinity", "no", "-1", "2147483648" })
            {
                number.SetCurrentValue(TextBox.TextProperty, invalid); number.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                if (!Validation.GetHasError(number) || model.Settings != accepted) throw new InvalidOperationException("Invalid numeric draft changed settings: " + name);
                number.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent)); if (Validation.GetHasError(number)) throw new InvalidOperationException("Invalid numeric draft did not recover: " + name);
            }
        }
        editor.ConfirmReset = () => false; var beforeReset = model.Settings; Click("ResetMovementButton"); if (model.Settings != beforeReset) throw new InvalidOperationException("Canceled movement reset changed settings.");
        var profileId = model.ActiveProfileId; var menus = model.Settings.PieMenus; var chord = model.Settings.PauseShortcut; var indicator = model.ShowStateIndicator;
        editor.ConfirmReset = () => true; Click("ResetMovementButton");
        if (model.CursorSpeed != FactoryDefaults.Create().CursorSpeed || model.ResponseCurve != FactoryDefaults.Create().ResponseCurve || model.PrecisionCursorSpeed != FactoryDefaults.Create().PrecisionCursorSpeed || model.BoostedCursorSpeed != FactoryDefaults.Create().BoostedCursorSpeed || model.PrecisionPercent != FactoryDefaults.Create().PrecisionPercent || model.ScrollSpeed != FactoryDefaults.Create().ScrollSpeed || model.ReverseScroll || model.ActiveProfileId != profileId || model.Settings.PauseShortcut != chord || model.ShowStateIndicator != indicator || !model.Settings.PieMenus.SequenceEqual(menus) || model.Settings.Mappings[Control.L3] != InputAction.Undo || model.Settings.Mappings[Control.R3] != InputAction.ToggleKeyboard) throw new InvalidOperationException("Movement reset changed unrelated settings or failed to reset movement.");
        Click("CustomizeCurveButton"); ((Expander)editor.DiagnosticControls["DPadMovementExpander"]).IsExpanded = false;
        foreach (var size in new[] { new Size(1920, 1080), new Size(1390, 900), new Size(1080, 760), new Size(864, 640), new Size(760, 620) })
        {
            Render($"movement-reference-{size.Width}x{size.Height}", size.Width, size.Height);
            foreach (var row in Descendants<MovementValueRow>(editor).Where(r => r.IsVisible || r.Visibility == Visibility.Visible))
            {
                // Collapsed ancestor content is not arranged until opened; visible cards use real native bounds.
                if (row.ActualWidth == 0 || row.ActualHeight == 0) continue;
                var parent = (FrameworkElement)row.Parent; var bounds = row.TransformToAncestor(parent).TransformBounds(new Rect(row.RenderSize));
                if (row.Title is "Response curve" or "D-pad tap distance" or "D-pad held speed") continue;
                if (bounds.Left < -1 || bounds.Right > parent.ActualWidth + 1 || bounds.Top < -1 || bounds.Bottom > parent.ActualHeight + 1 || row.ActualHeight + row.Margin.Top + row.Margin.Bottom + 1 < row.DesiredSize.Height) throw new InvalidOperationException("Movement value row is clipped at " + size + ": " + row.Title);
                var fieldBounds = row.Number.TransformToAncestor(row).TransformBounds(new Rect(row.Number.RenderSize));
                if (fieldBounds.Top < -1 || fieldBounds.Bottom > row.ActualHeight + 1) throw new InvalidOperationException("Movement numeric field is clipped at " + size + ": " + row.Title);
            }
            var customize = (Button)editor.DiagnosticControls["CustomizeCurveButton"];
            var actionBounds = customize.TransformToAncestor(editor.ResponseCard).TransformBounds(new Rect(customize.RenderSize));
            if (customize.ActualHeight < 25 || actionBounds.Bottom > editor.ResponseCard.ActualHeight - editor.ResponseCard.Padding.Bottom + 1) throw new InvalidOperationException("Customize curve is clipped at " + size);
        }
        var advanced = (Expander)editor.DiagnosticControls["DPadMovementExpander"];
        var viewport = (ScrollViewer)window.FindName("MovementScroll");
        void Wait(int milliseconds) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) }; timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
        foreach (var animated in new[] { false, true })
        foreach (var size in new[] { new Size(1194, 672), new Size(1536, 864), new Size(1920, 1032) })
        {
            UiMotion.DiagnosticAnimationOverride = animated; advanced.IsExpanded = false; Wait(200); Layout(size.Width, size.Height); viewport.ScrollToTop(); Layout(size.Width, size.Height);
            var beforeSettings = model.Settings; var cardTop = editor.DeadZoneCard.TranslatePoint(new Point(), editor).Y; var offset = viewport.VerticalOffset;
            advanced.IsExpanded = true; Layout(size.Width, size.Height); advanced.BringIntoView();
            foreach (var delay in new[] { 0, 65, 200 })
            {
                if (delay > 0) Wait(delay); Layout(size.Width, size.Height);
                var header = advanced.TransformToAncestor(editor.DeadZoneCard).TransformBounds(new Rect(advanced.RenderSize));
                foreach (var name in new[] { "LeftDeadZoneNumber", "RightDeadZoneNumber" })
                {
                    var field = (TextBox)editor.DiagnosticControls[name]; var bounds = field.TransformToAncestor(editor.DeadZoneCard).TransformBounds(new Rect(field.RenderSize));
                    if (bounds.Bottom > header.Top - 1) throw new InvalidOperationException("D-pad expansion overlaps dead-zone controls: " + size + " animation=" + animated);
                }
                if (Math.Abs(editor.DeadZoneCard.TranslatePoint(new Point(), editor).Y - cardTop) > 1 || Math.Abs(viewport.VerticalOffset - offset) > 1) throw new InvalidOperationException("D-pad expansion shifted existing cards or jumped scroll position: " + size + " animation=" + animated + " card=" + cardTop + "→" + editor.DeadZoneCard.TranslatePoint(new Point(), editor).Y + " scroll=" + offset + "→" + viewport.VerticalOffset);
            }
            foreach (var name in new[] { "TapNumber", "DPadSpeedNumber" })
            {
                var field = (TextBox)editor.DiagnosticControls[name]; var bounds = field.TransformToAncestor(editor.DeadZoneCard).TransformBounds(new Rect(field.RenderSize));
                if (bounds.Bottom > editor.DeadZoneCard.ActualHeight - editor.DeadZoneCard.Padding.Bottom + 1 || field.ActualHeight < 30) throw new InvalidOperationException("Expanded D-pad field is clipped: " + size);
            }
            if (!ReferenceEquals(beforeSettings, model.Settings)) throw new InvalidOperationException("Expansion changed movement settings.");
            viewport.ScrollToBottom(); Layout(size.Width, size.Height); Render($"dpad-expanded-{size.Width}x{size.Height}-{(animated ? "motion" : "static")}", size.Width, size.Height);
            advanced.IsExpanded = false;
            foreach (var delay in new[] { 0, 65, 200 })
            {
                if (delay > 0) Wait(delay); Layout(size.Width, size.Height);
                if (Math.Abs(editor.DeadZoneCard.TranslatePoint(new Point(), editor).Y - cardTop) > 1) throw new InvalidOperationException("D-pad collapse shifted the existing cards: " + size);
                var header = advanced.TransformToAncestor(editor.DeadZoneCard).TransformBounds(new Rect(advanced.RenderSize));
                foreach (var name in new[] { "LeftDeadZoneNumber", "RightDeadZoneNumber" })
                {
                    var field = (TextBox)editor.DiagnosticControls[name]; var bounds = field.TransformToAncestor(editor.DeadZoneCard).TransformBounds(new Rect(field.RenderSize));
                    if (bounds.Bottom > header.Top - 1) throw new InvalidOperationException("D-pad collapse overlaps dead-zone controls: " + size);
                }
            }
        }
        UiMotion.DiagnosticAnimationOverride = false;
        window.Close(); ThemePalette.Apply(false);
        return "PASS: all ten numeric fields, up/down buttons and sliders; valid/invalid numeric drafts; independent absolute cursor speeds above the old 10x limit, preserved speeds when normal changes and unchanged scroll ratios; reverse switch; three response presets/customize; movement-only reset/cancel preserves mappings, menus, shortcut, profile and indicator; light/dark and full/restored/compact renders. Isolated settings only; no live controller or input injection.";
    }
    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) { var child = VisualTreeHelper.GetChild(node, i); if (child is T match) yield return match; foreach (var item in Descendants<T>(child)) yield return item; }
    }
}
