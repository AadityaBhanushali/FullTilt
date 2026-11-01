using System.Numerics;
using System.Runtime.InteropServices;
using Windows.System;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace FullTilt.Windows;

// Accent-policy blur ignores a layered window's HRGN. Clip the composition visual
// itself so live blur cannot fill the rectangular corners outside the pie.
public sealed class PieBackdrop : IDisposable
{
    public const float BackgroundBlurOpacity = .65f;
    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        void CreateDesktopWindowTarget(nint window, [MarshalAs(UnmanagedType.Bool)] bool topmost, out nint target);
        void EnsureOnThread(uint threadId);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct QueueOptions { public int Size, ThreadType, ApartmentType; }
    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(QueueOptions options, out nint controller);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    // Retain our queue until the UI thread exits. Never shut down a borrowed queue.
    [ThreadStatic] private static DispatcherQueueController? _queue;
    private readonly nint _window;
    private Compositor? _compositor;
    private DesktopWindowTarget? _target;
    private CompositionBackdropBrush? _brush;
    private CompositionEllipseGeometry? _circle;
    private CompositionGeometricClip? _clip;
    private SpriteVisual? _visual;
    private bool _disposed;

    private PieBackdrop(nint window) => _window = window;
    public static PieBackdrop? TryCreate(nint window)
    {
        if (window == 0) return null;
        var result = new PieBackdrop(window);
        try
        {
            if (DispatcherQueue.GetForCurrentThread() is null)
            {
                Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(new QueueOptions { Size = 12, ThreadType = 2, ApartmentType = 2 }, out var queue));
                try { _queue = MarshalInterface<DispatcherQueueController>.FromAbi(queue); }
                finally { Marshal.Release(queue); }
            }
            int enabled = 1;
            Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(window, 17, ref enabled, sizeof(int))); // DWMWA_USE_HOSTBACKDROPBRUSH, Windows 11
            result._compositor = new Compositor();
            result._compositor.As<ICompositorDesktopInterop>().CreateDesktopWindowTarget(window, true, out var target);
            try { result._target = MarshalInterface<DesktopWindowTarget>.FromAbi(target); }
            finally { Marshal.Release(target); }
            result._brush = result._compositor.CreateHostBackdropBrush();
            result._circle = result._compositor.CreateEllipseGeometry();
            result._clip = result._compositor.CreateGeometricClip(result._circle);
            result._visual = result._compositor.CreateSpriteVisual();
            result._visual.Brush = result._brush;
            // Favor blurred background detail while keeping the surface translucent.
            result._visual.Opacity = BackgroundBlurOpacity;
            result._visual.Clip = result._clip;
            result._visual.IsVisible = false;
            result._target.Root = result._visual;
            return result;
        }
        catch (Exception ex) when (ex is COMException or TypeLoadException or DllNotFoundException or EntryPointNotFoundException)
        {
            result.Dispose();
            return null; // Retain translucent WPF; never fall back to rectangular accent blur.
        }
    }
    public void Resize(int pixels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(pixels, 1);
        _visual!.Size = new Vector2(pixels);
        _circle!.Center = new Vector2(pixels / 2f);
        _circle.Radius = new Vector2(pixels * 175f / 400f); // Wedge boundary; never restore the removed outer ring.
    }
    public void SetVisible(bool visible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _visual!.IsVisible = visible;
    }
    // Inspect the real composition geometry rather than duplicate its calculation.
    public (Vector2 Center, Vector2 Radius, bool Visible) Geometry => (_circle!.Center, _circle.Radius, _visual!.IsVisible);
    public float Opacity => _visual!.Opacity;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_target is not null) _target.Root = null;
        _visual?.Dispose(); _clip?.Dispose(); _circle?.Dispose(); _brush?.Dispose();
        _target?.Dispose(); _compositor?.Dispose();
        int disabled = 0;
        _ = DwmSetWindowAttribute(_window, 17, ref disabled, sizeof(int));
    }
}
