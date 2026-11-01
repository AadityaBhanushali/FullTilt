using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FullTilt.Core;
using FullTilt.Windows;

namespace FullTilt;

internal static class PieSwitchDiagnostics
{
    public static string Verify(string output)
    {
        var foreground = NativeMethods.GetForegroundWindow(); var windowsBefore = Application.Current.Windows.Count;
        var menus = new[] { PiePresets.Find("media")!.Create(), PiePresets.Find("editing")!.Create(), PiePresets.Find("windows")!.Create(), PieMenu.Create("Two sectors", 2), PieMenu.Create("Eight sectors", 8) };
        var latencies = new List<double>(); var firstShows = 0; var displays = 0;
        var dispatcher = Dispatcher.CurrentDispatcher;
        var platform = new PieOverlayPlatform(dispatcher, (window, snapshot, current) =>
        {
            displays++;
            if (!window.IsVisible)
                window.IsVisibleChanged += (_, args) =>
                {
                    if (!(bool)args.NewValue) return;
                    firstShows++;
                    if (window.Diagram.Menu?.Id != snapshot.Menu.Id || !Texts(window.Diagram).Any(t => t.Text == snapshot.Menu.Name) ||
                        ((Canvas)window.Diagram.Child).HasAnimatedProperties)
                        throw new InvalidOperationException("First visible artwork was stale or animated.");
                };
            window.DisplayAt(snapshot, -30000, -30000, (int)PieMenu.Diameter(snapshot.SizePercent), current);
        });
        PieBackdropWindow? backdrop = null; nint backdropHandle = 0;
        void Flush() => dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        PiePresentation Frame(PieMenu menu, int selected = 0, double size = 100) => new(menu, new(1, -30000, -30000), selected, false) { SizePercent = size };
        void Check(PiePresentation frame, PieOverlay? previous)
        {
            var art = platform.ArtworkWindow ?? throw new InvalidOperationException("Requested pie did not produce artwork.");
            if (!art.IsVisible || art.Diagram.Menu?.Id != frame.Menu.Id || art.Diagram.Menu.Name != frame.Menu.Name || ReferenceEquals(previous, art) || platform.IsHidden)
                throw new InvalidOperationException("Reopen/switch reused a stale artwork surface.");
            if (previous?.IsVisible == true) throw new InvalidOperationException("Previous artwork survived switching.");
            backdrop ??= platform.SharedBackdrop; backdropHandle = backdropHandle == 0 ? new WindowInteropHelper(backdrop!).Handle : backdropHandle;
            if (!ReferenceEquals(backdrop, platform.SharedBackdrop) || new WindowInteropHelper(backdrop!).Handle != backdropHandle || art.Owner != backdrop)
                    throw new InvalidOperationException("Switch recreated the shared transparent owner or broke ownership.");
            foreach (var window in new Window[] { art, backdrop! })
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (((long)NativeMethods.GetWindowLongPtr(handle, -20) & StateIndicator.NonInteractiveStyles) != StateIndicator.NonInteractiveStyles || window.ShowActivated || window.ShowInTaskbar || window.IsHitTestVisible ||
                    !(window is PieOverlay overlay ? overlay.TransitionsDisabled : ((PieBackdropWindow)window).TransitionsDisabled))
                    throw new InvalidOperationException("Overlay interaction or per-window transition protection is missing.");
            }
        }
        try
        {
            PieOverlay? previous = null;
            for (var cycle = 0; cycle < 240; cycle++)
            {
                if (cycle is 0 or 120) ThemePalette.Apply(cycle == 120);
                var frame = Frame(menus[cycle % menus.Length], cycle % menus[cycle % menus.Length].SegmentCount, cycle % 3 == 0 ? 150 : 100);
                var clock = Stopwatch.StartNew(); platform.Present(frame); Flush(); latencies.Add(clock.Elapsed.TotalMilliseconds); Check(frame, previous);
                previous = platform.ArtworkWindow;
                // Selection changes stay on the same live surface, without an entrance animation.
                platform.Present(frame with { Selected = (frame.Selected + 1) % frame.Menu.SegmentCount }); Flush();
                if (!ReferenceEquals(previous, platform.ArtworkWindow)) throw new InvalidOperationException("Selection change recreated a live surface.");
                platform.Present(null); Flush();
                if (!platform.IsHidden || previous!.IsVisible || backdrop!.IsVisible || backdrop.Backdrop?.Geometry.Visible == true)
                    throw new InvalidOperationException("Dismissal was acknowledged before all artwork/owner hid.");
            }
            var beforeBurst = displays;
            for (var i = 0; i < 1000; i++) { platform.Present(Frame(menus[i % menus.Length])); platform.Present(null); }
            var latest = Frame(menus[1]); platform.Present(latest); Flush(); Check(latest, previous);
            if (displays != beforeBurst + 1) throw new InvalidOperationException("Superseded queued menus were presented instead of coalescing.");
            previous = platform.ArtworkWindow;
            // A coalesced hide/reopen of the same identity is still a new session.
            platform.Present(null); platform.Present(latest with { Selected = 2 }); Flush(); Check(latest, previous);
            previous = platform.ArtworkWindow;
            platform.Present(Frame(menus[2])); Flush(); Check(Frame(menus[2]), previous); // Defensive visible identity switch.
            platform.Present(null); Flush();
            // Application shutdown may close owned artwork before platform disposal.
            platform.Present(Frame(menus[0])); Flush();
            platform.ArtworkWindow!.Close(); platform.Dispose();
        }
        finally { platform.Dispose(); ThemePalette.Apply(false); }
        if (backdrop?.IsVisible == true || backdrop?.Backdrop is not null || Application.Current.Windows.Count != windowsBefore || NativeMethods.GetForegroundWindow() != foreground)
            throw new InvalidOperationException($"Switch stress cleanup: owner visible={backdrop?.IsVisible}, windows={Application.Current.Windows.Count}/{windowsBefore}, foreground unchanged={NativeMethods.GetForegroundWindow() == foreground}.");

        // Supersede during preparation: a released/changed request must never show late.
        var rejectedShows = 0;
        PieOverlayPlatform? superseded = null;
        superseded = new(dispatcher, (window, snapshot, current) =>
        {
            window.IsVisibleChanged += (_, args) => { if ((bool)args.NewValue) rejectedShows++; };
            superseded!.Present(null);
            window.DisplayAt(snapshot, -30000, -30000, 440, current);
        });
        using (superseded) { superseded.Present(Frame(menus[0])); Flush(); if (rejectedShows != 0 || !superseded.IsHidden) throw new InvalidOperationException("Released request produced a late popup."); }
        latencies.Sort();
        var report = $"PASS: {firstShows} fresh first-visible artwork surfaces in 240 alternating/reopen cycles, both themes, 2/4/6/8 sectors and 100/150% size; correct menu before visibility, no runtime crossfade; stable shared light-blur owner HWND; live selection reuse; 2000 superseded requests coalesced to latest; coalesced same-menu reopen and visible identity switch; release during preparation prevented; hidden acknowledgment, native transition/no-activate/click-through flags, cleanup and unchanged foreground.\nCPU/UI request-to-show timing outside the virtual desktop: median {latencies[latencies.Count / 2]:0.00} ms, p95 {latencies[(int)(latencies.Count * .95)]:0.00} ms, max {latencies[^1]:0.00} ms. These are diagnostic preparation timings, not physical controller/display latency. No input worker, live settings or desktop capture. Visible monitor/GPU/controller acceptance remains manual.\n";
        File.WriteAllText(Path.Combine(output, "pie-switch-stress.txt"), report); return report;
    }
    private static IEnumerable<TextBlock> Texts(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is TextBlock text) yield return text; foreach (var nested in Texts(child)) yield return nested; }
    }
}
