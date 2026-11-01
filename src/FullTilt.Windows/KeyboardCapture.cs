using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using FullTilt.Core;

namespace FullTilt.Windows;

// Installed only by the foreground recorder. Captured input never reaches other applications.
public sealed class KeyboardCapture : IDisposable
{
    private delegate nint Hook(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct KeyData { public uint Key, Scan, Flags, Time; public nuint Extra; }
    private readonly Hook _callback;
    private readonly Func<bool> _foreground;
    private readonly Action<KeyboardRecording> _changed;
    private readonly KeyboardRecordingFilter _filter;
    private nint _handle;
    private bool _disposed;
    // A failed unhook must leave the unmanaged callback alive, but inert.
    private static readonly List<Hook> Retained = [];
    public KeyboardCapture(Func<bool> foreground, Action<KeyboardRecording> changed)
    {
        _foreground = foreground; _changed = changed;
        var held = Enumerable.Range(8, 247).Where(k => k is not (0x10 or 0x11 or 0x12) && (GetAsyncKeyState(k) & 0x8000) != 0)
            .Select(k => new KeyboardKey((ushort)k, Extended: k is 0xA3 or 0xA5 or 0x5B or 0x5C or >= 0x21 and <= 0x2E)).ToArray();
        _filter = new(held); _callback = Receive;
        _handle = SetWindowsHookEx(13, _callback, GetModuleHandle(null), 0);
        if (_handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start keyboard recording.");
        _changed(_filter.Recording);
    }
    private nint Receive(int code, nint message, nint pointer)
    {
        if (code < 0 || _disposed || !_foreground()) return CallNextHookEx(0, code, message, pointer);
        var data = Marshal.PtrToStructure<KeyData>(pointer);
        if ((data.Flags & 0x10) != 0) return CallNextHookEx(0, code, message, pointer);
        var down = message == 0x100 || message == 0x104;
        var up = message == 0x101 || message == 0x105;
        if (!down && !up) return CallNextHookEx(0, code, message, pointer);
        var key = (ushort)data.Key; var extended = (data.Flags & 1) != 0;
        var suppress = false;
        if (!_filter.Recording.Complete)
        {
            var name = new StringBuilder(80);
            GetKeyNameText((int)((data.Scan << 16) | (extended ? 1u << 24 : 0)), name, name.Capacity);
            suppress = _filter.Update(new(key, (ushort)data.Scan, extended, name.Length == 0 ? null : name.ToString()), down);
            _changed(_filter.Recording);
        }
        return suppress ? 1 : CallNextHookEx(0, code, message, pointer);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != 0 && !UnhookWindowsHookEx(_handle)) lock (Retained) Retained.Add(_callback);
        _handle = 0;
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, Hook callback, nint module, uint thread);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetKeyNameText(int key, StringBuilder name, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
