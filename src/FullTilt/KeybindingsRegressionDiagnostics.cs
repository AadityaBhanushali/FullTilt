using System;
using System.Collections.Generic;
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

internal static class KeybindingsRegressionDiagnostics
{
    public static string Verify(string output)
    {
        using var model = new SettingsViewModel(new(), new SettingsStore(Path.Combine(output, "keybindings-regression-settings")), null, null);
        model.Bindings.Single(r => r.Control == Control.LT).Selection = InputAction.SpeedBoostHold;
        model.Bindings.Single(r => r.Control == Control.R3).Selection = InputAction.Enter;
        var editor = new KeybindingsEditor(model);
        editor.SetAvailableHeight(760);
        var root = new Grid(); root.SetResourceReference(Panel.BackgroundProperty, "ThemeFFFFFF"); root.Children.Add(editor);
        var actions = (ListBox)editor.FindName("Actions");
        var search = (TextBox)editor.FindName("Search");
        var placeholder = (TextBlock)editor.FindName("SearchPlaceholder");
        void Layout()
        {
            root.Measure(new Size(1300, 760)); root.Arrange(new Rect(0, 0, 1300, 760)); root.UpdateLayout();
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.Background); root.UpdateLayout();
        }
        var before = model.Settings;
        Layout();
        foreach (var category in Descendants<Expander>(actions)) category.IsExpanded = true;
        Layout();
        var list = actions.ItemsSource;
        var categories = Descendants<Expander>(actions).ToArray();
        var viewer = Descendants<ScrollViewer>(actions).First();
        viewer.ScrollToVerticalOffset(350); Layout(); var offset = viewer.VerticalOffset;
        foreach (var control in new[] { Control.LT, Control.R3, Control.Y, Control.LT, Control.X, Control.R3, Control.A })
        {
            editor.SelectControl(control); Layout();
            if (!ReferenceEquals(list, actions.ItemsSource) || !categories.SequenceEqual(Descendants<Expander>(actions)) ||
                categories.Any(c => !c.IsExpanded || KeybindingsEditor.GetIsCurrentCategory(c)) || actions.SelectedItem is not null || Math.Abs(viewer.VerticalOffset - offset) > .1)
                throw new InvalidOperationException("Target change reset the independent action palette: " + control);
        }
        editor.SelectJoystick(CursorStick.Right); Layout();
        if (Descendants<Expander>(actions).Any(KeybindingsEditor.GetIsCurrentCategory) || actions.SelectedItem is not null) throw new InvalidOperationException("Movement selection focused an assigned action.");
        editor.SelectControl(Control.Y); Layout();
        if (Math.Abs(viewer.VerticalOffset - offset) > .1 || Descendants<Expander>(actions).Any(c => !c.IsExpanded)) throw new InvalidOperationException("Returning from joystick travel lost the action browsing position.");
        try
        {
            foreach (var dark in new[] { false, true })
            {
                ThemePalette.Apply(dark); Layout();
                search.SelectAll(); search.SelectedText = "backspace"; search.CaretIndex = search.Text.Length; Layout();
                var filtered = actions.ItemsSource;
                editor.SelectControl(Control.X); editor.SelectControl(Control.Y); Layout();
                if (!ReferenceEquals(filtered, actions.ItemsSource) || search.Text != "backspace") throw new InvalidOperationException("Target change reset search.");
                if (actions.Items.Count != 1 || placeholder.Visibility != Visibility.Collapsed || search.Text != "backspace") throw new InvalidOperationException("Typing did not show the search query and filtered result.");
                CheckTextGeometry(search);
                RenderSearch(search, output, dark ? "search-typed-dark.png" : "search-typed-light.png");
                search.CaretIndex = 3; Layout(); CheckTextGeometry(search);
                search.Select(0, search.Text.Length); Layout();
                RenderSearch(search, output, dark ? "search-selected-dark.png" : "search-selected-light.png", 1.5);
                search.SelectedText = new string('w', 80); search.CaretIndex = search.Text.Length; Layout();
                search.ScrollToHorizontalOffset(double.MaxValue); Layout();
                var caret = search.GetRectFromCharacterIndex(search.CaretIndex);
                if (caret.IsEmpty || caret.X < 34 || caret.Right > search.ActualWidth - 2 || caret.Y < 0 || caret.Bottom > search.ActualHeight) throw new InvalidOperationException($"Long query caret is outside the text viewport: {caret}, field={search.ActualWidth}x{search.ActualHeight}, offset={search.HorizontalOffset}.");
                search.Clear(); Layout();
                if (placeholder.Visibility != Visibility.Visible || actions.Items.Count < ActionCatalog.All.Count) throw new InvalidOperationException("Clearing search lost its placeholder or action choices.");
            }
        }
        finally { ThemePalette.Apply(false); }
        if (!ReferenceEquals(before, model.Settings)) throw new InvalidOperationException("Selection/search diagnostics modified assignments.");
        return "PASS: target changes preserve the action-list instance, category instances/expansion, scroll offset and search without selecting or focusing the assigned action; movement/click switches stay distinct.\nPASS: typed query, character/caret bounds, glyph pixels, text selection, long-query scrolling and clear checked in light/dark and rendered at 100%/150%. Isolated settings; no live input or target window.\n";
    }
    private static void CheckTextGeometry(TextBox search)
    {
        var first = search.GetRectFromCharacterIndex(0);
        var caret = search.GetRectFromCharacterIndex(search.CaretIndex);
        var host = (ScrollViewer)search.Template.FindName("PART_ContentHost", search);
        var origin = host.TranslatePoint(new Point(), search);
        if (first.IsEmpty || caret.IsEmpty || first.X < 39 || first.X > 46 || first.Height < search.FontSize ||
            first.Y < origin.Y - 1 || first.Bottom > origin.Y + host.ActualHeight + 1 || Math.Abs(first.Y + first.Height / 2 - search.ActualHeight / 2) > 3)
            throw new InvalidOperationException($"Search text/caret is misplaced or clipped: glyph={first}, caret={caret}, host={origin}/{host.ActualWidth}x{host.ActualHeight}.");
    }
    private static void RenderSearch(TextBox search, string output, string name, double scale = 1)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(search.ActualWidth * scale), (int)Math.Ceiling(search.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(search);
        var first = search.GetRectFromCharacterIndex(0); var last = search.GetRectFromCharacterIndex(search.Text.Length);
        var stride = bitmap.PixelWidth * 4; var pixels = new byte[stride * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, stride, 0);
        var ink = ((SolidColorBrush)search.Foreground).Color;
        var visibleInk = 0;
        for (var y = Math.Max(0, (int)(first.Y * scale)); y < Math.Min(bitmap.PixelHeight, (int)Math.Ceiling(first.Bottom * scale)); y++)
            for (var x = Math.Max(0, (int)(first.X * scale)); x < Math.Min(bitmap.PixelWidth, (int)Math.Ceiling(last.X * scale)); x++)
            {
                var offset = y * stride + x * 4;
                if (Math.Abs(pixels[offset] - ink.B) + Math.Abs(pixels[offset + 1] - ink.G) + Math.Abs(pixels[offset + 2] - ink.R) < 100) visibleInk++;
            }
        if (visibleInk < 20) throw new InvalidOperationException("Typed search glyphs are not visible in the rendered text bounds.");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name)); encoder.Save(file);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var foundChild in Descendants<T>(child)) yield return foundChild;
        }
    }
}
