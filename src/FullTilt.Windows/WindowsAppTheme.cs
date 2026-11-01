using Microsoft.Win32;
using System.Security;

namespace FullTilt.Windows;

public static class WindowsAppTheme
{
    // Follow Windows' app color preference (which may differ from its taskbar mode).
    public static bool IsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { return false; }
    }
}
