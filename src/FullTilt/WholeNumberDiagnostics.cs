using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FullTilt.Core;

namespace FullTilt;

internal static class WholeNumberDiagnostics
{
    internal static string Verify(string output)
    {
        using var model = new SettingsViewModel(PiePresets.Initialize(new(), bind: true) with
        { CursorSpeed = 642, PrecisionPercent = 20, SpeedBoostMultiplier = 3.6, PrecisionCursorSpeed = 128.4, BoostedCursorSpeed = 2311.2, ScrollSpeed = 13.4, ResponseCurve = 1.73, PieMenuSizePercent = 128.9 },
            new SettingsStore(Path.Combine(output, "whole-number-isolated")), null, null);
        var before = model.Settings;
        var window = new MainWindow(model) { AllowClose = true };
        var movement = (MovementEditor)((ContentControl)window.FindName("MovementSections")).Content;
        var pie = (PieEditor)((ContentControl)window.FindName("PieEditorHost")).Content;
        var root = (FrameworkElement)window.Content;
        void Layout() { root.Measure(new Size(1700, 1000)); root.Arrange(new Rect(0, 0, 1700, 1000)); root.UpdateLayout(); }
        static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Whole(string text) => Assert(double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) && number == Math.Truncate(number), "Fractional numeric display: " + text);
        void Enter(TextBox field, string text)
        {
            field.SetCurrentValue(TextBox.TextProperty, text); field.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert(!Validation.GetHasError(field), "Rounded numeric entry rejected: " + text);
            field.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent)); Layout(); Whole(field.Text);
        }
        try
        {
            Layout();
            Assert(model.Settings == before && !model.Edits.Dirty(EditTab.PieMenus), "Viewing rounded legacy values changed saved settings/drafts.");
            Assert(pie.SizeNumber.Text == "129" && pie.SizeSlider.Value == 129 && !pie.Draft!.Dirty, "Legacy fractional menu size was not represented as a clean whole number.");
            var tabs = (TabControl)window.FindName("SettingsTabs"); tabs.SelectedIndex = 0;
            ((Button)movement.DiagnosticControls["CustomizeCurveButton"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Expander)movement.DiagnosticControls["DPadMovementExpander"]).IsExpanded = true; Layout();
            foreach (var key in new[] { "Cursor", "Precision", "SpeedBoost", "Scroll", "ScrollBoost", "LeftDeadZone", "RightDeadZone", "Curve", "Tap", "DPadSpeed" })
            {
                var field = (TextBox)movement.DiagnosticControls[key + "Number"];
                var slider = (Slider)movement.DiagnosticControls[key + "Slider"];
                Whole(field.Text); Assert(slider.Value == Math.Truncate(slider.Value) && slider.SmallChange >= 1 && slider.SmallChange == Math.Truncate(slider.SmallChange) && slider.IsSnapToTickEnabled, "Fractional slider/step: " + key);
                slider.SetCurrentValue(RangeBase.ValueProperty, slider.Minimum + (slider.Maximum - slider.Minimum) * .4137); Layout();
                Whole(field.Text); Assert(slider.Value == Math.Truncate(slider.Value), "Slider retained a fractional value: " + key);
            }
            Enter((TextBox)movement.DiagnosticControls["CursorNumber"], "700.5"); Assert(model.CursorSpeed == 701, "Half-up cursor rounding failed.");
            Enter((TextBox)movement.DiagnosticControls["ScrollNumber"], "13.4"); Assert(model.ScrollSpeed == 13, "Scroll rounding down failed.");
            Enter((TextBox)movement.DiagnosticControls["CurveNumber"], "173.5"); Assert(model.ResponseCurve == 1.74, "Whole-percent curve lost fine control.");
            Enter((TextBox)movement.DiagnosticControls["LeftDeadZoneNumber"], "18.5"); Assert(model.LeftDeadZone == .19, "Whole-percent dead zone failed.");
            var boost = model.BoostedCursorSpeed; var precision = model.PrecisionCursorSpeed; model.CursorSpeed = 713;
            Assert(model.BoostedCursorSpeed == boost && model.PrecisionCursorSpeed == precision, "Normal speed changed independent speeds."); Layout();
            Whole(((TextBox)movement.DiagnosticControls["SpeedBoostNumber"]).Text);
            tabs.SelectedIndex = 2; Layout();
            Enter(pie.SizeNumber, "129.5"); Assert(pie.MenuSizePercent == 130 && pie.Draft!.SizePercent == 130 && model.Settings.PieMenuSizePercent == 128.9, "Rounded menu size escaped its draft.");
            pie.SizeSlider.SetCurrentValue(RangeBase.ValueProperty, 130.2); Layout();
            Assert(pie.SizeSlider.Value == 130 && pie.SizeNumber.Text == "130", "Coerced pie slider kept a fractional thumb.");
            model.Edits.Replay(EditTab.PieMenus, true); Layout(); Whole(pie.SizeNumber.Text);
            model.Edits.Replay(EditTab.PieMenus, false); Layout(); Whole(pie.SizeNumber.Text);
            pie.Discard(); Layout(); Assert(!pie.Draft!.Dirty && pie.SizeNumber.Text == "129", "Discard fabricated a rounded legacy draft.");
            Enter(pie.SizeNumber, "152.7"); Assert(pie.Apply() && model.Settings.PieMenuSizePercent == 153, "Rounded pie size did not save.");
            foreach (var culture in new[] { CultureInfo.GetCultureInfo("en-US"), CultureInfo.GetCultureInfo("fr-FR") })
                Assert(Equals(WholeNumbers.Converter.ConvertBack(128.5.ToString(culture), typeof(double), null!, culture), 129d), "Culture-aware whole rounding failed.");
            foreach (var dark in new[] { false, true })
            {
                ThemePalette.Apply(dark); tabs.SelectedIndex = 0; Layout();
                var image = new RenderTargetBitmap(1700, 1000, 96, 96, PixelFormats.Pbgra32); image.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var file = File.Create(Path.Combine(output, "whole-settings-" + (dark ? "dark" : "light") + ".png")); encoder.Save(file);
            }
        }
        finally { pie.Discard(); window.Close(); ThemePalette.Apply(false); }
        return "PASS: every movement numeric field/slider uses whole displayed values and steps, fractional input rounds nearest with halves up, response/dead-zone percentages retain fine internal precision, independent cursor speeds survive refresh, legacy fractional settings are not modified by viewing, global pie size rounds in its detached draft with Save/Discard/Undo/Redo and no false cherry, culture-aware rounding. Isolated stores/offscreen WPF only; no live input/settings.";
    }
}
