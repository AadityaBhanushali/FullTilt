using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FullTilt.Windows;
using Microsoft.Win32;

namespace FullTilt;

public partial class StateIndicator : Window
{
    private string _text = "";
    private nint _handle;
    private bool _positioning;
    private bool _failed;
    public event Action<string>? Failed;
    public const long NonInteractiveStyles = 0x08000000L | 0x20L | 0x80L; // NOACTIVATE | TRANSPARENT | TOOLWINDOW
    public StateIndicator()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            var existing = NativeMethods.GetWindowLongPtr(_handle, -20);
            Marshal.SetLastPInvokeError(0);
            var previous = NativeMethods.SetWindowLongPtr(_handle, -20, (nint)((long)existing | NonInteractiveStyles));
            if (previous == 0 && Marshal.GetLastPInvokeError() != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            HwndSource.FromHwnd(_handle)?.AddHook(WindowHook);
        };
        SizeChanged += (_, _) => TryPositionInCorner();
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Closed += (_, _) => SystemEvents.DisplaySettingsChanged -= DisplayChanged;
    }
    public void UpdateState(string text, bool enabled)
    {
        if (_failed) return;
        if (!enabled || string.IsNullOrEmpty(text)) { if (IsVisible) Hide(); return; }
        if (_text != text) { _text = text; StateText.Text = text; }
        if (IsVisible) return;
        new WindowInteropHelper(this).EnsureHandle();
        TryPositionInCorner();
        if (_failed) return;
        Show(); // ShowActivated=false plus WS_EX_NOACTIVATE; never calls Activate/Focus.
    }
    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(TryPositionInCorner);
    private nint WindowHook(nint hwnd, int message, nint wparam, nint lparam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return 3; } // WM_MOUSEACTIVATE -> MA_NOACTIVATE
        if (message == 0x0084) { handled = true; return -1; } // WM_NCHITTEST -> HTTRANSPARENT
        if (message == 0x02E0) Dispatcher.BeginInvoke(TryPositionInCorner); // WM_DPICHANGED
        return 0;
    }
    private void TryPositionInCorner()
    {
        if (_failed) return;
        try { PositionInCorner(); }
        catch (Win32Exception ex)
        {
            _failed = true;
            if (IsVisible) Hide();
            Failed?.Invoke("State indicator is unavailable. Settings still show active modes. " + ex.Message);
        }
    }
    private void PositionInCorner()
    {
        if (_handle == 0 || _positioning) return;
        _positioning = true;
        try
        {
            var monitor = NativeMethods.MonitorFromPoint(default, 1); // Primary display contains physical (0,0).
            var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            var scale = Math.Max(96, NativeMethods.GetDpiForWindow(_handle)) / 96.0;
            var content = (FrameworkElement)Content;
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var width = (int)Math.Ceiling(content.DesiredSize.Width * scale);
            var height = (int)Math.Ceiling(content.DesiredSize.Height * scale);
            var margin = (int)Math.Round(16 * scale);
            if (!NativeMethods.SetWindowPos(_handle, -1, info.Work.Right - width - margin, info.Work.Bottom - height - margin, width, height, 0x10))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        finally { _positioning = false; }
    }
}
