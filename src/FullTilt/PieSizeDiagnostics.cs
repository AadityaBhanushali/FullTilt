using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FullTilt.Core;

namespace FullTilt;

internal static class PieSizeDiagnostics
{
    public static string Verify(string output)
    {
        using var model = new SettingsViewModel(PiePresets.Initialize(new(), bind: true), new SettingsStore(Path.Combine(output, "pie-size-isolated")), null, null);
        var editor = new PieEditor(model); editor.Confirm = (_, _) => MessageBoxResult.Yes;
        void Layout(double width = 1500, double height = 850) { editor.Measure(new Size(width, height)); editor.Arrange(new Rect(0, 0, width, height)); editor.UpdateLayout(); }
        void Render(string name, FrameworkElement element, double width, double height)
        {
            element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(output, name + ".png")); encoder.Save(stream);
        }
        Layout(); var saved = model.Settings.PieMenuSizePercent; var first = editor.Draft!.Menu.Id; var previewSize = editor.Diagram.ActualWidth;
        if (previewSize <= 1 || previewSize > editor.PreviewPane.ColumnDefinitions[0].ActualWidth || editor.Diagram.ActualHeight != previewSize) throw new InvalidOperationException("Initial preview does not fit the editor pane.");
        editor.SizeSlider.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty, 200d); Layout(); var large = editor.Diagram.ActualWidth;
        if (editor.Draft.SizePercent != 200 || !model.Edits.Dirty(EditTab.PieMenus) || model.Settings.PieMenuSizePercent != saved || editor.SizeNumber.Text != "200") throw new InvalidOperationException("Pie size slider did not edit a detached global draft.");
        editor.SizeNumber.SetCurrentValue(TextBox.TextProperty, "100"); editor.SizeNumber.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); Layout();
        if (Math.Abs(editor.Diagram.ActualWidth - large) > .1 || Math.Abs(large - previewSize) > .1 || editor.SizeSlider.Value != 100) throw new InvalidOperationException("Desktop size slider changed the fixed preview.");
        foreach (var invalid in new[] { "NaN", "Infinity", "no", "99", "201" })
        {
            editor.SizeNumber.SetCurrentValue(TextBox.TextProperty, invalid); editor.SizeNumber.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            if (!Validation.GetHasError(editor.SizeNumber) || editor.Draft.SizePercent != 100) throw new InvalidOperationException("Invalid pie size changed draft.");
            editor.SizeNumber.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        }
        editor.SizeSlider.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty, 150d); if (!editor.TryResolvePending() || model.Settings.PieMenuSizePercent != 150 || editor.Draft!.Dirty) throw new InvalidOperationException("Pending size did not save with confirmation.");
        editor.SizeSlider.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty, 175d);
        var frame = new DispatcherFrame(); var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        wait.Tick += (_, _) => { wait.Stop(); frame.Continue = false; }; wait.Start(); Dispatcher.PushFrame(frame);
        if (!model.Edits.Dirty(EditTab.PieMenus) || model.Settings.PieMenuSizePercent != 150 || new SettingsStore(Path.Combine(output, "pie-size-isolated")).Load().Settings.PieMenuSizePercent != 150) throw new InvalidOperationException("Size draft leaked through a timer.");
        if (!editor.Apply() || model.Settings.PieMenuSizePercent != 175) throw new InvalidOperationException("Explicit Save did not persist global size.");
        var second = model.Settings.PieMenus[1]; editor.SelectMenu(second.Id); if (editor.MenuSizePercent != 175) throw new InvalidOperationException("Global size changed between menus."); editor.SelectMenu(first);
        editor.SearchBox.Text = "Scroll"; Layout(); var character = editor.SearchBox.GetRectFromCharacterIndex(0);
        if (character.IsEmpty || character.X < 34 || character.X > 55 || character.Height < 5 || character.Bottom > editor.SearchBox.ActualHeight) throw new InvalidOperationException("Pie action search text is outside its viewport.");
        if (editor.Draft!.Dirty || editor.SelectedAction != editor.Draft.Menu.Slots[0]) throw new InvalidOperationException("Pie search changed a saved draft or selected an unrelated filtered action."); editor.SearchBox.Clear();
        if (editor.SelectedAction != editor.Draft.Menu.Slots[0]) throw new InvalidOperationException("Pie action highlight differs from the selected segment after refreshing the list.");
        foreach (var dark in new[] { false, true })
        {
            ThemePalette.Apply(dark); editor.Refresh(); Layout(); previewSize = editor.Diagram.ActualWidth; foreach (var percent in new[] { 100d, 150, 200, 100 })
            {
                editor.SizeSlider.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty, percent); Layout();
                if (Math.Abs(editor.Diagram.ActualWidth - previewSize) > .1 || editor.Diagram.ActualHeight != editor.Diagram.ActualWidth) throw new InvalidOperationException("Global size changed the in-app preview at " + percent + "%.");
                Render($"pie-size-editor-{(dark ? "dark" : "light")}-{percent}", editor, 1500, 850);
            }
            for (var count = 2; count <= 8; count++) foreach (var percent in new[] { 100d, 150, 200 })
            {
                var menu = PieMenu.Create("Centered size", count) with { SizePercent = percent }; menu = menu with { Slots = Enumerable.Repeat(InputAction.PreviousTrack, count).ToArray() };
                var diagram = new PieDiagram(); diagram.Render(menu, 0, editable: true, opener: "LB"); var diameter = 440 * percent / 100;
                diagram.Measure(new Size(diameter, diameter)); diagram.Arrange(new Rect(0, 0, diameter, diameter)); diagram.UpdateLayout();
                var canvas = (Canvas)diagram.Child;
                foreach (var button in canvas.Children.OfType<Button>())
                {
                    var contents = (StackPanel)button.Content; var contentBounds = contents.TransformToAncestor(canvas).TransformBounds(new Rect(contents.RenderSize));
                    var x = Canvas.GetLeft(button) + button.Width / 2; var y = Canvas.GetTop(button) + button.Height / 2;
                    if (Math.Abs(contentBounds.X + contentBounds.Width / 2 - x) > .1 || Math.Abs(contentBounds.Y + contentBounds.Height / 2 - y) > .1) throw new InvalidOperationException("Pie icon/text group is off center: " + count + " segments, " + percent + "%");
                    foreach (FrameworkElement child in contents.Children)
                    {
                        var bounds = child.TransformToAncestor(canvas).TransformBounds(new Rect(child.RenderSize));
                        var scaled = child.TransformToAncestor(diagram).TransformBounds(new Rect(child.RenderSize));
                        if (Math.Abs(bounds.X + bounds.Width / 2 - x) > .1 || Math.Abs(scaled.Width - bounds.Width * diameter / 400) > .1 || Math.Abs(scaled.Height - bounds.Height * diameter / 400) > .1) throw new InvalidOperationException("Pie icon or label is off center or does not scale uniformly.");
                    }
                }
                if (count is 4 or 8) Render($"pie-size-{(dark ? "dark" : "light")}-{count}-{percent}", diagram, diameter, diameter);
            }
        }
        editor.Discard(); foreach (var size in new[] { new Size(1100, 760), new Size(850, 620), new Size(630, 580) })
        {
            Render($"pie-size-editor-{size.Width}x{size.Height}", editor, size.Width, size.Height);
            var fixedSize = editor.Diagram.ActualWidth;
            editor.SizeSlider.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty, 200d); Layout(size.Width, size.Height);
            if (Math.Abs(editor.Diagram.ActualWidth - fixedSize) > .1) throw new InvalidOperationException("Desktop size changed a compact preview.");
            editor.Discard();
        }
        var window = new MainWindow(model) { AllowClose = true }; ((TabControl)window.FindName("SettingsTabs")).SelectedIndex = 2;
        ThemePalette.Apply(true); Render("pie-size-page-dark", (FrameworkElement)window.Content, 1700, 1000);
        ThemePalette.Apply(false); Render("pie-size-page-light", (FrameworkElement)window.Content, 1700, 1000); window.Close();
        ThemePalette.Apply(false);
        return "PASS: detached global desktop size, slider/numeric synchronization, invalid drafts, explicit Save and shared value between menus; fixed reference preview at 100/150/200% in both themes and full/restored/compact layouts; 2–8 desktop segment layouts keep icons and labels uniformly scaled and centered. Isolated settings and offscreen artwork only; no live controller/input.";
    }
}
