using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using FullTilt.Windows;

namespace FullTilt;

// FullTilt controller and fractional high-resolution wheel packets use proportional
// offsets. Ordinary native wheel notches and mouse capture remain WPF-owned.
internal static class UiWheelScrolling
{
    private sealed class ScrollState { public double Pending; public bool Scheduled, Dragging; }
    private static readonly ConditionalWeakTable<ScrollViewer, ScrollState> States = new();
    private static bool _initialized;

    // Offscreen diagnostics supply the same origin metadata without injecting input.
    internal sealed class ControllerWheelEventArgs(int delta) : MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta);

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        foreach (var type in new[] { typeof(ScrollViewer), typeof(ComboBox) })
            EventManager.RegisterClassHandler(type, Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel));
        // Pixel offsets are required even in lists whose default panel scrolls in items.
        // Configure during layout, before interaction; keep native ScrollBar/Thumb templates.
        EventManager.RegisterClassHandler(typeof(ScrollViewer), FrameworkElement.SizeChangedEvent,
            new SizeChangedEventHandler((sender, _) => ((ScrollViewer)sender).SetCurrentValue(ScrollViewer.CanContentScrollProperty, false)));
        EventManager.RegisterClassHandler(typeof(ScrollViewer), FrameworkElement.UnloadedEvent,
            new RoutedEventHandler((sender, _) => { if (States.TryGetValue((ScrollViewer)sender, out var state)) state.Pending = 0; }));
        EventManager.RegisterClassHandler(typeof(Thumb), Thumb.DragStartedEvent, new DragStartedEventHandler((sender, _) => Drag((Thumb)sender, true)), true);
        EventManager.RegisterClassHandler(typeof(Thumb), Thumb.DragCompletedEvent, new DragCompletedEventHandler((sender, _) => Drag((Thumb)sender, false)), true);
    }
    private static void Drag(Thumb thumb, bool active)
    {
        if (Ancestor<ScrollBar>(thumb) is null || Ancestor<ScrollViewer>(thumb) is not { } viewer) return;
        var state = States.GetOrCreateValue(viewer);
        state.Pending = 0;
        state.Dragging = active;
        // Observe only: never mark drag events handled or change mouse capture.
    }
    private static bool Dragging(ScrollState state) => state.Dragging ||
        (Mouse.Captured is DependencyObject captured && Ancestor<Thumb>(captured) is not null);

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0) return;
        var controller = e is ControllerWheelEventArgs || unchecked((nuint)NativeMethods.GetMessageExtraInfo()) == WindowsInputSink.WheelInputTag;
        if (!controller && e.Delta % 120 == 0) return; // Keep ordinary mouse-wheel behavior.
        var target = e.OriginalSource as DependencyObject;
        while (target is not null && target is not (ScrollViewer or ComboBox)) target = Parent(target);
        if (!ReferenceEquals(target, sender)) return; // Only innermost consumer, once.
        e.Handled = true; // Never replay tiny packets as native full wheel steps.
        if (sender is not ScrollViewer viewer) return; // Do not change a closed assignment ComboBox.
        var state = States.GetOrCreateValue(viewer);
        if (Dragging(state)) { state.Pending = 0; return; }
        const double notchDistance = 48; // WPF physical ScrollViewer wheel step: three 16-DIP lines.
        state.Pending -= e.Delta / 120d * notchDistance;
        if (state.Scheduled) return;
        state.Scheduled = true;
        viewer.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            state.Scheduled = false;
            var distance = state.Pending; state.Pending = 0;
            if (distance == 0 || Dragging(state)) return;
            viewer.ScrollToVerticalOffset(Math.Clamp(viewer.VerticalOffset + distance, 0, viewer.ScrollableHeight));
        }));
    }
    private static T? Ancestor<T>(DependencyObject? value) where T : DependencyObject
    {
        while (value is not null) { if (value is T found) return found; value = Parent(value); }
        return null;
    }
    private static DependencyObject? Parent(DependencyObject value) => value switch
    {
        Visual or Visual3D => VisualTreeHelper.GetParent(value),
        FrameworkContentElement content => content.Parent,
        _ => LogicalTreeHelper.GetParent(value)
    };
}
