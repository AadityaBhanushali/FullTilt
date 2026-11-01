using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace FullTilt.Windows;

public sealed record TouchKeyboardReply(bool Visible = false, string? Error = null);

// Only invoked in --touch-keyboard-helper before normal app startup; caller enforces a process timeout.
public static class TouchKeyboardNative
{
    public static TouchKeyboardReply Run(string command)
    {
        // The helper must also expire if its parent exits before the parent's cancellation continuation runs.
        using var watchdog = new Timer(_ => Environment.Exit(124), null, TouchKeyboardProcessBackend.HelperTimeoutMilliseconds, Timeout.Infinite);
        try
        {
            switch (command)
            {
                case "observe": return new(Observe());
                case "ensure": EnsureStarted(); return new();
                case "toggle": Toggle(); return new();
                default: return new(Error: "Unknown keyboard helper command.");
            }
        }
        catch (Exception ex) when (ex is COMException or Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        { return new(Error: $"Windows touch-keyboard component failed (0x{ex.HResult:X8}). Try the taskbar keyboard button and check Windows input services."); }
    }
    private static bool Observe()
    {
        // Documented desktop API: empty rectangle means hidden. Do not infer visibility from processes/titles.
        var instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new("D5120AA3-46BA-44C5-822D-CA8092C1FC72"), true)!)!;
        try
        {
            var pane = (IFrameworkInputPane)instance;
            Marshal.ThrowExceptionForHR(pane.Location(out var bounds));
            return bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
        }
        finally { Marshal.FinalReleaseComObject(instance); }
    }
    private static void EnsureStarted()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "microsoft shared", "ink", "TabTip.exe");
        if (!File.Exists(path)) throw new FileNotFoundException("Windows touch keyboard is unavailable.");
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName("TabTip"))
            using (process)
            {
                if (process.SessionId != current.SessionId) continue;
                var handle = NativeMethods.OpenProcess(0x1000, false, (uint)process.Id);
                if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    var name = new StringBuilder(32768); var length = name.Capacity;
                    if (!NativeMethods.QueryFullProcessImageName(handle, 0, name, ref length)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (string.Equals(name.ToString(), path, StringComparison.OrdinalIgnoreCase)) return;
                }
                finally { NativeMethods.CloseHandle(handle); }
            }
        using var started = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
    }
    private static void Toggle()
    {
        // UNDOCUMENTED Windows shell interface. May break across Windows versions; never fall back to OSK.
        // Fresh COM instance each call survives shell restarts. No cached application HWND or focus restoration.
        var clsid = new Guid("4CE576FA-83DC-4F88-951C-9D0782B4E376");
        var iid = typeof(ITipInvocation).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, 0, 4, ref iid, out var instance));
        try { Marshal.ThrowExceptionForHR(((ITipInvocation)instance).Toggle(NativeMethods.GetDesktopWindow())); }
        finally { Marshal.FinalReleaseComObject(instance); }
    }
    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object instance);
    [ComImport, Guid("37C994E7-432B-4834-A2F7-DCE1F13B834B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation { [PreserveSig] int Toggle(nint window); }
    [ComImport, Guid("5752238B-24F0-495A-82F1-2FD593056796"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFrameworkInputPane
    {
        [PreserveSig] int Advise(nint window, nint handler, out uint cookie);
        [PreserveSig] int AdviseWithHWND(nint window, nint handler, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int Location(out NativeMethods.Rect bounds);
    }
}
