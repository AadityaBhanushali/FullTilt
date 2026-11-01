using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FullTilt;

internal static class UiChromeDiagnostics
{
    internal static void Verify(ContextMenu menu, SettingsViewModel model, string output)
    {
        menu.DataContext = model;
        var saved = model.Settings.PauseShortcut;
        foreach (var dark in new[] { false, true })
        {
            ThemePalette.Apply(dark);
            menu.Measure(new Size(250, 460)); menu.Arrange(new Rect(0, 0, 250, 460)); menu.UpdateLayout();
            var item = (MenuItem)menu.ItemContainerGenerator.ContainerFromIndex(0);
            item.ApplyTemplate();
            var selected = model.PauseButtons[0].Selected;
            typeof(MenuItem).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(item, null);
            if (model.PauseButtons[0].Selected == selected || model.Settings.PauseShortcut != saved || !item.StaysOpenOnClick) throw new InvalidOperationException("Shortcut menu did not edit only its manual draft.");
            var tick = (TextBlock)item.Template.FindName("Tick", item);
            if ((tick.Visibility == Visibility.Visible) != item.IsChecked) throw new InvalidOperationException("Shortcut checkmark differs from its selection.");
            typeof(MenuItem).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(item, null);
            var viewer = Find<ScrollViewer>(menu);
            var bar = Find<ScrollBar>(viewer); bar.ApplyTemplate();
            if (bar.Template.FindName("PART_Track", bar) is not Track track || track.Thumb is null || bar.ActualWidth > 14.1) throw new InvalidOperationException("Themed scrollbar lost its native track or compact width.");
            var bitmap = new RenderTargetBitmap(250, 460, 96, 96, PixelFormats.Pbgra32); bitmap.Render(menu);
            using var stream = File.Create(Path.Combine(output, "shortcut-menu-" + (dark ? "dark" : "light") + ".png")); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
        }
        ThemePalette.Apply(true);
    }
    private static T Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            try { return Find<T>(child); } catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException("Missing themed " + typeof(T).Name);
    }
}
