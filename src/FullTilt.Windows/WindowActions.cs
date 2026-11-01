using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FullTilt.Windows;

public interface IWindowPlatform
{
    nint ForegroundRoot();
    bool IsUsable(nint window);
    bool Show(nint window, int command);
    bool Close(nint window);
}
public sealed class WindowActions(IWindowPlatform platform)
{
    public void Execute(WindowCommand command)
    {
        var window = platform.ForegroundRoot();
        if (window == 0 || !platform.IsUsable(window)) return;
        var accepted = command == WindowCommand.Close ? platform.Close(window) : platform.Show(window, ShowCommand(command));
        if (!accepted) throw new Win32Exception("Window command was not accepted. This window may not support it or Windows may restrict access.");
    }
    public static int ShowCommand(WindowCommand command) => command switch
    {
        WindowCommand.Minimize => 6, WindowCommand.Maximize => 3, WindowCommand.Restore => 9,
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };
}
internal sealed class NativeWindowPlatform : IWindowPlatform
{
    // GA_ROOT preserves a foreground modal dialog's normal close instead of closing its owner.
    public nint ForegroundRoot() => NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), 2);
    public bool IsUsable(nint window) => NativeMethods.IsWindow(window) && NativeMethods.IsWindowVisible(window)
        && window != NativeMethods.GetShellWindow() && window != NativeMethods.GetDesktopWindow();
    public bool Show(nint window, int command) => NativeMethods.ShowWindowAsync(window, command);
    public bool Close(nint window) => NativeMethods.PostMessage(window, 0x10, 0, 0);
}
