using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FullTilt.Core;
using FullTilt.Windows;

namespace FullTilt;

internal static class PieBackdropDiagnostics
{
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);
    public static string Verify(nint hiddenWindow, PieOverlay overlay, string output)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var styles = NativeMethods.GetWindowLongPtr(hiddenWindow, -20);
        // Both owned diagnostic HWNDs stay outside the virtual desktop. Exercise
        // actual show/hide/render without input workers or real target windows.
        foreach (var size in new[] { 440, 660, 880, 440 })
        {
            overlay.ShowAt(-30000, -30000, size);
            overlay.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var host = overlay.BackdropWindow ?? throw new InvalidOperationException("Backdrop owner missing.");
            var hostHandle = new WindowInteropHelper(host).Handle;
            var hostStyles = (long)NativeMethods.GetWindowLongPtr(hostHandle, -20);
            foreach (var handle in new[] { hiddenWindow, hostHandle })
                if (!GetWindowRect(handle, out var rect) || rect.Left != -30000 || rect.Top != -30000 ||
                    rect.Right - rect.Left != size || rect.Bottom - rect.Top != size)
                    throw new InvalidOperationException($"Backdrop/artwork physical placement differs: requested (-30000,-30000,{size},{size}); actual ({rect.Left},{rect.Top},{rect.Right - rect.Left},{rect.Bottom - rect.Top}); DPI={NativeMethods.GetDpiForWindow(handle)}; work area={SystemParameters.WorkArea}; host={handle == hostHandle}.");
            const long layered = 0x80000;
            if ((hostStyles & (StateIndicator.NonInteractiveStyles | layered)) != (StateIndicator.NonInteractiveStyles | layered) ||
                host.ShowActivated || host.ShowInTaskbar || host.IsHitTestVisible || overlay.Owner != host)
                throw new InvalidOperationException("Backdrop interaction/ownership changed.");
            if (host.Content is not null || host.Background is not SolidColorBrush background || background.Color.A != 0 ||
                !host.AllowsTransparency || overlay.Opacity != 1)
                throw new InvalidOperationException("Desktop glass acquired an opaque backing or faded its text.");
            var blur = host.Backdrop ?? throw new InvalidOperationException("Light native background blur unavailable on this diagnostic machine.");
            var (center, radius, visible) = blur.Geometry;
            if (!visible || Math.Abs(center.X - size / 2f) > .01 || center.X != center.Y ||
                Math.Abs(radius.X - size * 175f / 400f) > .01 || radius.X != radius.Y || blur.Opacity != PieBackdrop.BackgroundBlurOpacity)
                throw new InvalidOperationException("Light blur strength/clip escaped the ring-free wedge boundary.");
            try { blur.Resize(0); throw new InvalidOperationException("Zero-sized blur clip accepted."); } catch (ArgumentOutOfRangeException) { }
            overlay.Dismiss();
            if (host.IsVisible || overlay.IsVisible || blur.Geometry.Visible)
                throw new InvalidOperationException("Pie dismissal left the background visible.");
        }
        if (NativeMethods.GetWindowLongPtr(hiddenWindow, -20) != styles || NativeMethods.GetForegroundWindow() != foreground)
            throw new InvalidOperationException("Backdrop changed interaction styles or foreground.");
        VerifyMaterials(overlay, output);
        var ownedHost = overlay.BackdropWindow!;
        overlay.Close();
        if (ownedHost.IsVisible || ownedHost.Backdrop is not null)
            throw new InvalidOperationException("Backdrop composition resources survived close.");
        return "PASS: 4.8 desktop ring removed; neutral wedge/center glass darkened 30% against black, gray and white references in dark theme only; light theme uses the original pale glass. Glass alpha/corner/ring absence checked at 100/200%; text opaque and selected lime unchanged. Strip/editor materials unchanged. Actual native compositor uses a 65% background-blur blend clipped to the 175/400 wedge radius, with show/dismiss, invalid size and disposal checks at 100/150/200%. White text-page, black and colored-background WPF material composites rendered from actual overlay content; these images exclude native blur. Non-activating/click-through styles and foreground preserved. HWNDs stayed outside the virtual desktop; no input worker, desktop capture or live settings. Actual GPU blur/desktop appearance remains manual acceptance.\n";
    }
    private static void VerifyMaterials(PieOverlay overlay, string output)
    {
        var menu = PiePresets.Find("browsing")!.Create().AssignDPad(0, InputAction.ScrollUp).AssignDPad(1, InputAction.ScrollDown)
            .AssignDPad(2, InputAction.PreviousTab).AssignDPad(3, InputAction.NextTab);
        try
        {
            foreach (var dark in new[] { false, true })
            {
                ThemePalette.Apply(dark);
                var diagram = new PieDiagram(); diagram.Render(menu, -1, desktop: true);
                var canvas = (Canvas)diagram.Child;
                var strip = new PieDPadStrip(); strip.Render(menu, 0);
                var material = (LinearGradientBrush)((Border)strip.Child).Background;
                var paths = canvas.Children.OfType<System.Windows.Shapes.Path>().ToArray();
                if (paths.Length != menu.SegmentCount || paths.Any(p => p.Effect is not null) || canvas.Children.OfType<System.Windows.Shapes.Ellipse>().Count() != 1)
                    throw new InvalidOperationException("Desktop ring or stacked shading returned.");
                foreach (var glass in paths.Select(p => (LinearGradientBrush)p.Fill).Append((LinearGradientBrush)canvas.Children.OfType<System.Windows.Shapes.Ellipse>().Single().Fill))
                    for (var stop = 0; stop < glass.GradientStops.Count; stop++)
                    {
                        var before = material.GradientStops[stop].Color; var after = glass.GradientStops[stop].Color;
                        if (glass.GradientStops[stop].Offset != material.GradientStops[stop].Offset) throw new InvalidOperationException("Glass gradient moved.");
                        foreach (var channel in new[] { (before.R, after.R), (before.G, after.G), (before.B, after.B) })
                            foreach (var backdrop in new[] { 0, 127, 255 })
                            {
                                var oldPixel = channel.Item1 * before.A / 255d + backdrop * (1 - before.A / 255d);
                                var newPixel = channel.Item2 * after.A / 255d + backdrop * (1 - after.A / 255d);
                                if (Math.Abs(newPixel - oldPixel * (dark ? .7 : 1)) > 1.1) throw new InvalidOperationException("Desktop tint is not 30% darker.");
                            }
                    }
                // Existing brushes must update in place through both theme transitions.
                ThemePalette.Apply(!dark); ThemePalette.Apply(dark);
                var restored = (LinearGradientBrush)paths[0].Fill;
                if (!dark && restored.GradientStops[0].Color != material.GradientStops[0].Color)
                    throw new InvalidOperationException("Light glass retained darkening after a live theme switch.");
                foreach (var scale in new[] { 1, 2 })
                {
                    var bitmap = Render(diagram, 400, 400, scale);
                    foreach (var point in new[] { new Point(200, 42), new Point(200, 180) })
                    {
                        var alpha = Pixel(bitmap, (int)point.X * scale, (int)point.Y * scale)[3];
                        if (alpha < (dark ? 170 : 140) || alpha > (dark ? 211 : 190)) throw new InvalidOperationException($"Glass became opaque/too faint: {point}, alpha {alpha}.");
                    }
                    if (Pixel(bitmap, 0, 0)[3] != 0) throw new InvalidOperationException("Pie corners are no longer transparent.");
                    if (Pixel(bitmap, 200 * scale, 15 * scale)[3] != 0) throw new InvalidOperationException("Removed outer ring still paints pixels.");
                    // Opaque label/center strokes must remain separate from the glass.
                    var ink = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(ink, bitmap.PixelWidth * 4, 0);
                    if (!Enumerable.Range(0, ink.Length / 4).Any(i => ink[i * 4 + 3] == 255)) throw new InvalidOperationException("Glass faded text/icons too.");
                }
                diagram.Render(menu, 0, desktop: true);
                if (Pixel(Render(diagram, 400, 400, 1), 200, 42)[3] < 200) throw new InvalidOperationException("Selected lime lost its contrast.");
                diagram.Render(menu, -1, editable: true);
                if (((Canvas)diagram.Child).Children.OfType<System.Windows.Shapes.Path>().Count() != menu.SegmentCount * 2)
                    throw new InvalidOperationException("Desktop glass changed the editor's layering.");
                var frame = new PiePresentation(menu, new(1, -30000, -30000), -1, false);
                overlay.DisplayAt(frame, -30000, -30000, 440);
                var artwork = (FrameworkElement)overlay.Content;
                artwork.Measure(new Size(440, 480)); artwork.Arrange(new Rect(0, 0, 440, 480)); artwork.UpdateLayout();
                foreach (var kind in new[] { "white", "black", "color" })
                {
                    var scene = new DrawingVisual();
                    using (var draw = scene.RenderOpen())
                    {
                        draw.DrawRectangle(kind == "black" ? Brushes.Black : Brushes.White, null, new Rect(0, 0, 440, 480));
                        for (var row = 0; row < 22; row++)
                        {
                            if (kind == "color") draw.DrawRectangle(row % 2 == 0 ? Brushes.CornflowerBlue : Brushes.Coral, null, new Rect(0, row * 24, 440, 24));
                            var line = new FormattedText("Background text visible through the pie · 0123456789", System.Globalization.CultureInfo.InvariantCulture,
                                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, kind == "black" ? Brushes.White : Brushes.Black, 1);
                            draw.DrawText(line, new Point(6, row * 24 + 2));
                        }
                    }
                    var composed = new RenderTargetBitmap(440, 480, 96, 96, PixelFormats.Pbgra32);
                    composed.Render(scene); composed.Render(artwork);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(composed));
                    using var file = File.Create(Path.Combine(output, $"pie-glass-{(dark ? "dark" : "light")}-{kind}.png")); encoder.Save(file);
                }
                overlay.Dismiss(); strip.Stop();
            }
        }
        finally { ThemePalette.Apply(false); }
    }
    private static RenderTargetBitmap Render(FrameworkElement element, int width, int height, int scale)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width * scale, height * scale, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element); return bitmap;
    }
    private static byte[] Pixel(RenderTargetBitmap bitmap, int x, int y)
    {
        var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0); return pixel;
    }
}
