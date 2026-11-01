using FullTilt.Core;
using Microsoft.Win32;

namespace FullTilt.Windows;

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public string? Read() { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue("FullTilt") as string; }
    public void Write(string? command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (command is null) key.DeleteValue("FullTilt", false);
        else key.SetValue("FullTilt", command, RegistryValueKind.String);
    }
}
