using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using FullTilt.Core;
using FullTilt.Windows;

namespace FullTilt;

internal static class WindowChromeDiagnostics
{
    public static string Verify(string output)
    {
        // Create only hidden test HWNDs; never show, activate or move a user's window.
        var foreground = NativeForeground();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var model = new SettingsViewModel(new(), new SettingsStore(Path.Combine(output, "window-test-settings")), null, null);
            var window = new MainWindow(model) { AllowClose = true, ShowActivated = false };
            try
            {
                if (window.WindowStyle != WindowStyle.SingleBorderWindow || window.ResizeMode != ResizeMode.CanResize ||
                    window.AllowsTransparency || WindowChrome.GetWindowChrome(window) is not { CaptionHeight: 40, UseAeroCaptionButtons: false })
                    throw new InvalidOperationException("Main window must use themed chrome with native resizing.");
                // Before handle creation, exercise WPF state transitions without showing any window.
                window.WindowState = WindowState.Maximized;
                window.WindowState = WindowState.Normal;
                var handle = new WindowInteropHelper(window).EnsureHandle();
                const long required = 0x00C00000L | 0x00080000L | 0x00040000L | 0x00020000L | 0x00010000L;
                var style = (long)NativeMethods.GetWindowLongPtr(handle, -16);
                if ((style & required) != required) throw new InvalidOperationException("Missing native caption/system menu/resize/minimize/maximize styles.");
                if (((long)NativeMethods.GetWindowLongPtr(handle, -20) & StateIndicator.NonInteractiveStyles) != 0)
                    throw new InvalidOperationException("Overlay-only noninteractive styles leaked into the main window.");
                if (window.IsVisible) throw new InvalidOperationException("Diagnostic window must stay hidden.");
            }
            finally { window.Close(); }
        }
        if (NativeForeground() != foreground) throw new InvalidOperationException("Window diagnostics changed foreground.");
        return "PASS: native caption/system menu/resize/minimize/maximize styles on two independently created hidden main windows; WPF maximize/normal property transitions; themed caption with no overlay styles. No live show/drag/DPI transitions tested. Physical multi-monitor and taskbar/tray behavior require manual validation.";
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint NativeForeground();
}
