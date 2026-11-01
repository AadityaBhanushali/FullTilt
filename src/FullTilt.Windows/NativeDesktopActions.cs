namespace FullTilt.Windows;

internal sealed class NativeDesktopActions(Action<bool> navigate, Action<int, int> scroll, Action<int, int> moveTo) : IDesktopActions
{
    private readonly TouchKeyboardController _keyboard = new(new TouchKeyboardProcessBackend());
    private readonly WindowActions _windows = new(new NativeWindowPlatform());
    public string? Error => _keyboard.Error;
    public void CancelPending() => _keyboard.CancelPending();
    public void Window(WindowCommand command) => _windows.Execute(command);
    public void Navigate(bool forward) => navigate(forward);
    public void Scroll(int horizontal, int vertical) => scroll(horizontal, vertical);
    public void CenterCursor()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == 0) return;
        var monitor = NativeMethods.MonitorFromWindow(foreground, 0);
        if (monitor == 0) return;
        var info = new NativeMethods.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        var center = CursorCentering.Center(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
        moveTo(center.X, center.Y);
    }
    public void ToggleKeyboard() => _keyboard.Request();
    public void Dispose() => _keyboard.Dispose();
}
