using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FullTilt.Core;
using FullTilt.Windows;

namespace FullTilt;

public sealed class PieOverlay : Window
{
    public PieDiagram Diagram { get; } = new();
    internal PieDPadStrip ShortcutStrip { get; } = new();
    private readonly Grid _layout = new() { Width = 440, Height = 440 };
    private readonly Viewbox _view = new() { Stretch = Stretch.Uniform };
    private double _heightRatio = 1;
    private nint _handle;
    internal PieBackdropWindow? BackdropWindow { get; private set; }
    internal bool TransitionsDisabled { get; private set; }
    internal bool IsClosed { get; private set; }
    private readonly bool _ownsBackdrop;
    public PieOverlay() : this(null) { }
    internal PieOverlay(PieBackdropWindow? backdrop)
    {
        BackdropWindow = backdrop; _ownsBackdrop = backdrop is null;
        Title = "FullTilt pie menu"; Width = Height = 440;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true;
        Focusable = false; IsHitTestVisible = false;
        _layout.RowDefinitions.Add(new() { Height = new GridLength(440) }); _layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Diagram.Width = Diagram.Height = 440; _layout.Children.Add(Diagram);
        ShortcutStrip.Height = 36; ShortcutStrip.Margin = new Thickness(0, 4, 0, 0); Grid.SetRow(ShortcutStrip, 1); _layout.Children.Add(ShortcutStrip);
        _view.Child = _layout; Content = _view;
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) BackdropWindow?.Hide();
        };
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            TransitionsDisabled = MakeNonInteractive(_handle);
        };
        Closed += (_, _) => { ShortcutStrip.Stop(); IsClosed = true; if (_ownsBackdrop) BackdropWindow?.Close(); BackdropWindow = null; };
    }
    internal static bool MakeNonInteractive(nint handle)
    {
        var style = NativeMethods.GetWindowLongPtr(handle, -20);
        Marshal.SetLastPInvokeError(0);
        if (NativeMethods.SetWindowLongPtr(handle, -20, (nint)((long)style | StateIndicator.NonInteractiveStyles)) == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        HwndSource.FromHwnd(handle)!.AddHook(Hook);
        var disabled = 1;
        return DwmSetWindowAttribute(handle, 3, ref disabled, sizeof(int)) >= 0; // Overlay-only show/hide transitions.
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    private static nint Hook(nint window, int message, nint wparam, nint lparam, ref bool handled)
    {
        if (message == 0x21) { handled = true; return 3; }
        if (message == 0x84) { handled = true; return -1; }
        return 0;
    }
    public void Display(PiePresentation presentation) => Display(presentation, null);
    internal void Display(PiePresentation presentation, Func<bool>? stillCurrent)
    {
        _heightRatio = PieDPadStrip.HasHints(presentation.Menu) ? PieDPadStrip.HeightRatio : 1;
        new WindowInteropHelper(this).EnsureHandle();
        if (!IsVisible)
        {
            var target = presentation.Target;
            var monitor = NativeMethods.MonitorFromPoint(new() { X = target.X, Y = target.Y }, 2);
            var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            // Move the hidden HWND onto the captured monitor before querying its per-window DPI.
            Position(Math.Clamp(target.X, info.Work.Left, info.Work.Right - 1), Math.Clamp(target.Y, info.Work.Top, info.Work.Bottom - 1), 1);
            var placement = PiePlacement.Fit(target.X, target.Y, info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom, NativeMethods.GetDpiForWindow(_handle), presentation.SizePercent, _heightRatio);
            Position(placement.X, placement.Y, placement.Size);
            DisplayAt(presentation, placement.X, placement.Y, placement.Size, stillCurrent);
        }
        else if (stillCurrent?.Invoke() != false) SetPresentation(presentation);
    }
    internal void DisplayAt(PiePresentation presentation, int x, int y, int size, Func<bool>? stillCurrent = null)
    {
        SetPresentation(presentation);
        var dpi = VisualTreeHelper.GetDpi(Diagram);
        var bounds = new Size(size / dpi.DpiScaleX, size * _heightRatio / dpi.DpiScaleY);
        _view.Measure(bounds); _view.Arrange(new Rect(bounds)); _view.UpdateLayout();
        if (stillCurrent?.Invoke() != false) ShowAt(x, y, size);
    }
    internal void ShowAt(int x, int y, int size)
    {
        new WindowInteropHelper(this).EnsureHandle();
        BackdropWindow ??= new PieBackdropWindow();
        try
        {
            BackdropWindow.ShowAt(x, y, size);
            Owner = BackdropWindow; // Owned artwork stays above the initialized background HWND.
            Position(x, y, size);
            Show();
            Position(x, y, size); // WPF Show must not restore an old DIP position.
        }
        catch { Dismiss(); throw; }
    }
    public void Dismiss()
    {
        ShortcutStrip.Stop();
        Hide();
        BackdropWindow?.Hide();
    }
    private void Position(int x, int y, int size)
    {
        if (!NativeMethods.SetWindowPos(_handle, -1, x, y, size, (int)Math.Ceiling(size * _heightRatio), 0x10)) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
    private void SetPresentation(PiePresentation presentation)
    {
        var hints = PieDPadStrip.HasHints(presentation.Menu);
        _heightRatio = hints ? PieDPadStrip.HeightRatio : 1; _layout.Height = 440 * _heightRatio;
        ShortcutStrip.Visibility = hints ? Visibility.Visible : Visibility.Collapsed;
        ShortcutStrip.Render(presentation.Menu, presentation.DPadPressed);
        Diagram.Render(presentation.Menu, presentation.Selected, presentation.MouseHeld, desktop: true);
    }
}

public sealed class PieOverlayPlatform : IPiePlatform, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action<PieOverlay, PiePresentation, Func<bool>> _display;
    public PieOverlayPlatform(Dispatcher dispatcher) : this(dispatcher, (window, presentation, current) => window.Display(presentation, current)) { }
    internal PieOverlayPlatform(Dispatcher dispatcher, Action<PieOverlay, PiePresentation, Func<bool>> display)
    { _dispatcher = dispatcher; _display = display; }
    private readonly object _gate = new();
    private PiePresentation? _latest;
    private bool _queued, _disposed;
    private volatile bool _hidden = true;
    private volatile string? _error;
    private PieOverlay? _window;
    private PieBackdropWindow? _backdrop;
    private long _revision, _generation, _windowGeneration = -1;
    internal PieOverlay? ArtworkWindow => _window;
    internal PieBackdropWindow? SharedBackdrop => _backdrop;
    public string? Error => _error;
    public nint Foreground => _error is null ? NativeMethods.GetForegroundWindow() : 0;
    public bool IsHidden => _hidden;
    public PieTarget Capture()
    {
        if (!NativeMethods.GetCursorPos(out var point)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        return new(Foreground, point.X, point.Y);
    }
    public void Present(PiePresentation? presentation)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (presentation is not null && (_latest is null || _latest.Menu.Id != presentation.Menu.Id)) _generation++;
            _revision++;
            _latest = presentation;
            if (presentation is not null) _hidden = false;
            if (_queued) return;
            _queued = true;
            _dispatcher.BeginInvoke(DispatcherPriority.Send, Apply);
        }
    }
    private void Apply()
    {
        PiePresentation? snapshot;
        long generation, revision;
        lock (_gate)
        {
            _queued = false;
            if (_disposed) return;
            snapshot = _latest;
            generation = _generation; revision = _revision;
        }
        // No worker waits for WPF layout or rendering while holding the request lock.
        try
        {
            if (snapshot is null)
            {
                CloseArtwork();
                lock (_gate) { if (_latest is null) _hidden = true; }
            }
            else if (_error is null)
            {
                if (_window is null || generation != _windowGeneration)
                {
                    // A hidden layered HWND retains its last presented pixels. Never
                    // expose that surface in a later session, even if Hide was coalesced.
                    CloseArtwork();
                    _backdrop ??= new PieBackdropWindow();
                    _window = new PieOverlay(_backdrop); _windowGeneration = generation;
                }
                bool Current() { lock (_gate) return !_disposed && _revision == revision && _latest is not null; }
                _display(_window, snapshot, Current);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _error = "Pie overlay unavailable: " + ex.Message;
            CloseArtwork(); _backdrop?.Hide(); _hidden = true;
        }
    }
    public void Dispose()
    {
        lock (_gate) { _disposed = true; _latest = null; _revision++; }
        CloseArtwork(); _backdrop?.Close(); _backdrop = null; _hidden = true;
    }
    private void CloseArtwork()
    {
        var window = _window; _window = null;
        if (window is not null && !window.IsClosed) { window.Dismiss(); window.Close(); }
    }
}
