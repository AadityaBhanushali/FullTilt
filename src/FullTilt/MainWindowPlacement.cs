using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using FullTilt.Core;
using FullTilt.Windows;

namespace FullTilt;

// Let WPF WindowChrome/Windows own hit testing, maximize and DPI scaling.
// Correct only normal-window placement, after native processing has completed.
internal sealed class MainWindowPlacement
{
    private readonly Window _window;
    private nint _handle;
    private HwndSource? _source;
    private bool _queued, _moving, _closed;

    public MainWindowPlacement(Window window)
    {
        _window = window;
        window.SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(window).Handle;
            _source = HwndSource.FromHwnd(_handle);
            _source?.AddHook(Hook);
            FitCurrentMonitor();
            Queue();
        };
        window.Loaded += (_, _) => Queue();
        window.IsVisibleChanged += (_, _) => Queue();
        window.StateChanged += (_, _) => Queue();
        window.SizeChanged += (_, _) => Queue();
        window.Closed += (_, _) => { _closed = true; _source?.RemoveHook(Hook); };
    }
    private nint Hook(nint hwnd, int message, nint wparam, nint lparam, ref bool handled)
    {
        if (message == 0x0214 && _window.WindowState == WindowState.Normal && NativeMethods.GetWindowRect(hwnd, out var current)) // WM_SIZING
        {
            Marshal.StructureToPtr(current, lparam, false); handled = true; return 1;
        }
        if (message == 0x0231) _moving = true; // WM_ENTERSIZEMOVE
        if (message == 0x0232) { _moving = false; Queue(); } // WM_EXITSIZEMOVE
        if (message is 0x02E0 or 0x007E or 0x001A or 0x0047) Queue(); // DPI, display, work-area and native position changes
        // Keep native movement/maximize and WM_DPICHANGED processing intact.
        return 0;
    }
    private void Queue()
    {
        if (_queued || _closed) return;
        _queued = true;
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _queued = false;
            if (_closed || _moving || !_window.IsVisible || _window.WindowState == WindowState.Minimized || _handle == 0) return;
            FitCurrentMonitor();
        }));
    }
    private void FitCurrentMonitor()
    {
        var monitor = NativeMethods.MonitorFromWindow(_handle, 2); // nearest, including disconnected monitor recovery
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info) || !NativeMethods.GetWindowRect(_handle, out var rect)) return;
        var work = new WindowBounds(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        if (work.Width <= 0 || work.Height <= 0) return;
        var scale = Math.Max(96, NativeMethods.GetDpiForWindow(_handle)) / 96d;
        var current = new WindowBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        var fitted = WindowModes.Windowed(current, work, scale);
        _window.MinWidth = fitted.Width / scale; _window.MinHeight = fitted.Height / scale;
        if (_window.WindowState != WindowState.Normal) return;
        if (fitted != current)
            NativeMethods.SetWindowPos(_handle, 0, fitted.X, fitted.Y, fitted.Width, fitted.Height, 0x0014); // NOZORDER | NOACTIVATE
    }
}
