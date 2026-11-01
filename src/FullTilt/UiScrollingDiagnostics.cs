using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Threading;

namespace FullTilt;

// Offscreen routed-event checks: no SendInput, live windows or user settings.
internal static class UiScrollingDiagnostics
{
    private static ScrollViewer Viewer()
    {
        var content = new StackPanel();
        for (var i = 0; i < 200; i++) content.Children.Add(new TextBlock { Text = "Item " + i, Height = 24 });
        var viewer = new ScrollViewer { Content = content, Width = 240, Height = 140 };
        Layout(viewer); return viewer;
    }
    private static void Layout(FrameworkElement root)
    {
        var width = double.IsFinite(root.Width) ? root.Width : root.ActualWidth;
        var height = double.IsFinite(root.Height) ? root.Height : root.ActualHeight;
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        root.UpdateLayout();
    }
    private static void Wheel(UIElement source, int delta, bool controller = true)
    {
        MouseWheelEventArgs e = controller ? new UiWheelScrolling.ControllerWheelEventArgs(delta) : new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta);
        e.RoutedEvent = Mouse.PreviewMouseWheelEvent;
        source.RaiseEvent(e);
        if (!e.Handled) { e.RoutedEvent = Mouse.MouseWheelEvent; source.RaiseEvent(e); }
    }
    public static string Baseline()
    {
        var viewer = Viewer(); Wheel(viewer, -120, false); Layout(viewer); var one = viewer.VerticalOffset;
        viewer.ScrollToTop(); Layout(viewer);
        for (var i = 0; i < 15; i++) { Wheel(viewer, -8, false); Layout(viewer); }
        var small = viewer.VerticalOffset;
        if (one <= 0 || Math.Abs(small - one * 15) > .01) throw new InvalidOperationException($"Unexpected WPF wheel baseline: {one}, {small}");
        return $"WPF baseline: one -120 event scrolls {one}; fifteen -8 events scroll {small} (same total wheel delta, 15x movement).";
    }
    public static string Verify(ComboBox combo)
    {
        UiWheelScrolling.Initialize(); // Repeated initialization must not register twice.
        var single = Viewer(); Wheel(single, -120); Layout(single); var step = single.VerticalOffset;
        var small = Viewer();
        for (var i = 0; i < 15; i++)
        {
            Wheel(small, -8); Layout(small);
            Equal(step * (i + 1) / 15, small.VerticalOffset, "Every packet produces proportional movement without notch waits");
        }
        Wheel(small, -240); Layout(small); Equal(step * 3, small.VerticalOffset, "Multi-notch distance");
        Wheel(small, 120); Layout(small); Equal(step * 2, small.VerticalOffset, "Reverse direction");
        var fractional = Viewer(); Wheel(fractional, -70); Wheel(fractional, 20); Wheel(fractional, -69); Layout(fractional);
        Equal(step * 119 / 120, fractional.VerticalOffset, "Packets queued before a frame preserve fractions and cancel opposing intent");
        Wheel(fractional, -1); Layout(fractional); Equal(step, fractional.VerticalOffset, "Exact total notch distance");
        var other = Viewer(); Wheel(other, -60); Layout(other); Equal(step / 2, other.VerticalOffset, "Separate viewer offsets");
        Wheel(other, -60); other.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); Layout(other);
        Equal(step / 2, other.VerticalOffset, "Unloading cancels pending work");
        var native = Viewer(); Wheel(native, -120, false); Layout(native); Equal(step, native.VerticalOffset, "Native physical wheel path unchanged");
        Wheel(native, 120, false); Layout(native); Equal(0, native.VerticalOffset, "Native physical wheel reverses normally");
        var trackpad = Viewer();
        for (var i = 0; i < 15; i++) { Wheel(trackpad, -8, false); Layout(trackpad); Equal(step * (i + 1) / 15, trackpad.VerticalOffset, "Native fractional trackpad distance"); }
        Wheel(trackpad, 60, false); Layout(trackpad); Equal(step / 2, trackpad.VerticalOffset, "Trackpad reversal");
        Wheel(trackpad, 60, false); Layout(trackpad); Equal(0, trackpad.VerticalOffset, "Trackpad returns to origin");
        Wheel(native, 120); Layout(native); Wheel(native, -8); Layout(native); Equal(step / 15, native.VerticalOffset, "No overscroll debt at boundary");
        VerifyThumb(native, native);

        var list = new ListBox { Width = 220, Height = 100 };
        for (var i = 0; i < 100; i++) list.Items.Add("Action " + i);
        var outerContent = new StackPanel(); outerContent.Children.Add(list); outerContent.Children.Add(new Border { Height = 2000 });
        var outer = new ScrollViewer { Content = outerContent, Width = 260, Height = 180 }; Layout(outer);
        var inner = Descendants<ScrollViewer>(list).First();
        Wheel((UIElement)list.ItemContainerGenerator.ContainerFromIndex(0), -120); Layout(outer); var listStep = inner.VerticalOffset;
        if (listStep <= 0) throw new InvalidOperationException("ListBox did not scroll.");
        inner.ScrollToTop(); Layout(outer);
        for (var i = 0; i < 15; i++) { Wheel((UIElement)list.ItemContainerGenerator.ContainerFromIndex(0), -8); Layout(outer); }
        Equal(listStep, inner.VerticalOffset, "Pixel ListBox keeps proportional distance");
        Equal(0, outer.VerticalOffset, "Nested viewer does not handle the same wheel twice");
        inner.ScrollToTop(); Layout(outer);
        for (var i = 0; i < 15; i++) { Wheel((UIElement)list.ItemContainerGenerator.ContainerFromIndex(0), -8, false); Layout(outer); }
        Equal(listStep, inner.VerticalOffset, "Native trackpad list distance"); Equal(0, outer.VerticalOffset, "Trackpad nested event consumed once");

        // Build the real ComboBox popup content offscreen; no popup HWND or focus change.
        var selected = combo.SelectedIndex;
        var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
        var popupContent = (FrameworkElement)popup.Child; var originalWidth = popupContent.Width; var originalHeight = popupContent.Height; popupContent.Width = 240; popupContent.Height = 160; Layout(popupContent);
        var popupViewer = Descendants<ScrollViewer>(popupContent).First();
        Wheel(popupViewer, -120); Layout(popupContent); var popupStep = popupViewer.VerticalOffset;
        if (popupStep <= 0) throw new InvalidOperationException("ComboBox popup did not scroll.");
        popupViewer.ScrollToTop(); Layout(popupContent);
        for (var i = 0; i < 15; i++) { Wheel(popupViewer, -8); Layout(popupContent); }
        Equal(popupStep, popupViewer.VerticalOffset, "Separate popup tree preserves proportional distance");
        popupViewer.ScrollToTop(); Layout(popupContent);
        for (var i = 0; i < 15; i++) { Wheel(popupViewer, -8, false); Layout(popupContent); }
        Equal(popupStep, popupViewer.VerticalOffset, "Native fractional trackpad popup distance");
        VerifyThumb(popupViewer, popupContent);
        popupContent.Width = originalWidth; popupContent.Height = originalHeight;
        if (combo.SelectedIndex != selected || popup.IsOpen) throw new InvalidOperationException("Scrolling changed a binding or opened a live popup.");
        return $"PASS: controller and native fractional trackpad scrolling ({step} pixels/notch), immediate proportional packets, frame batching, direction/bounds, unchanged ordinary native wheel, nested ListBox and real assignment popup. Native Thumb drag routed events move scrollbars; proportional wheel is suppressed during drag. No live input or capture simulated.";
    }
    private static void VerifyThumb(ScrollViewer viewer, FrameworkElement root)
    {
        viewer.ScrollToTop(); Layout(root);
        var bar = Descendants<ScrollBar>(viewer).First(b => b.Orientation == Orientation.Vertical);
        var thumb = Descendants<Thumb>(bar).First();
        var capture = Mouse.Captured;
        Wheel(viewer, -8); // Pending controller work must not fight a newly started drag.
        thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        Layout(root); Equal(0, viewer.VerticalOffset, "Drag start cancels pending wheel work");
        thumb.RaiseEvent(new DragDeltaEventArgs(0, 20) { RoutedEvent = Thumb.DragDeltaEvent }); Layout(root);
        var offset = viewer.VerticalOffset;
        if (offset <= 0) throw new InvalidOperationException("Native ScrollBar did not process Thumb DragDelta.");
        Wheel(viewer, -120); Layout(root); Equal(offset, viewer.VerticalOffset, "Controller wheel cannot fight thumb dragging");
        thumb.RaiseEvent(new DragCompletedEventArgs(0, 20, false) { RoutedEvent = Thumb.DragCompletedEvent }); Layout(root);
        Wheel(viewer, 8); Layout(root);
        if (viewer.VerticalOffset >= offset) throw new InvalidOperationException("Wheel did not resume after drag.");
        if (!ReferenceEquals(capture, Mouse.Captured)) throw new InvalidOperationException("Scrolling modified capture.");
    }
    public static string VerifyPie(PieEditor editor, ListBox actions, FrameworkElement root)
    {
        Layout(root);
        var viewer = Descendants<ScrollViewer>(actions).First();
        viewer.ScrollToTop(); Layout(root); Wheel(viewer, -8); Layout(root);
        if (viewer.VerticalOffset <= 0) throw new InvalidOperationException("Pie action list did not scroll incrementally.");
        viewer.ScrollToTop(); Layout(root); Wheel(viewer, -120, false); Layout(root); var step = viewer.VerticalOffset;
        viewer.ScrollToTop(); Layout(root);
        for (var i = 0; i < 15; i++) { Wheel(viewer, -8, false); Layout(root); }
        Equal(step, viewer.VerticalOffset, "Real Pie action list trackpad distance matches wheel notch");
        var bar = Descendants<ScrollBar>(viewer).First(b => b.Orientation == Orientation.Vertical);
        var thumb = Descendants<Thumb>(bar).First();
        void Press(UIElement source) => source.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
        var item = (ListBoxItem)actions.ItemContainerGenerator.ContainerFromIndex(0);
        Press(item); if (!editor.ActionDragArmed) throw new InvalidOperationException("Action row no longer arms drag assignment.");
        Press(thumb); if (editor.ActionDragArmed) throw new InvalidOperationException("Scrollbar press armed action drag-and-drop.");
        VerifyThumb(viewer, root);
        return "PASS: real Pie action rows arm assignment dragging; native scrollbar thumb presses do not. Pie list proportional scrolling and native ScrollBar DragDelta verified offscreen. Physical capture/drag feel remains a manual check.";
    }
    internal static string VerifyActions(ListBox actions, FrameworkElement root)
    {
        // Ensure content actually overflows in both window modes. A tall maximized
        // viewport can legitimately fit the collapsed category headers completely.
        var expanded = Descendants<Expander>(actions).Select(e => (Element: e, Expanded: e.IsExpanded)).ToArray();
        foreach (var state in expanded) state.Element.IsExpanded = true;
        Layout(root); var viewer = Descendants<ScrollViewer>(actions).First();
        viewer.ScrollToTop(); Layout(root); Wheel(viewer, -120, false); Layout(root); var step = viewer.VerticalOffset;
        viewer.ScrollToTop(); Layout(root);
        for (var i = 0; i < 15; i++) { Wheel(viewer, -8, false); Layout(root); }
        if (step <= 0) throw new InvalidOperationException("Keybindings actions did not scroll.");
        Equal(step, viewer.VerticalOffset, "Keybindings fractional trackpad distance matches native wheel");
        foreach (var state in expanded) state.Element.IsExpanded = state.Expanded;
        return "PASS: real Keybindings action list native fractional trackpad packets preserve one-notch distance, without changing wheel/controller speed.";
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Equal(double expected, double actual, string label)
    {
        if (Math.Abs(expected - actual) > .01) throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
    }
}
