using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using FullTilt.Windows;

namespace FullTilt;

// A lightly blended, clipped background blur stays beneath sharp WPF artwork.
internal sealed class PieBackdropWindow : Window
{
    private nint _handle;
    internal PieBackdrop? Backdrop { get; private set; }
    internal bool TransitionsDisabled { get; private set; }
    public PieBackdropWindow()
    {
        Title = "FullTilt pie backdrop";
        Width = Height = 440;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true;
        Focusable = false; IsHitTestVisible = false;
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            TransitionsDisabled = PieOverlay.MakeNonInteractive(_handle);
            Backdrop = PieBackdrop.TryCreate(_handle);
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) Backdrop?.SetVisible(false); };
        Closed += (_, _) => { Backdrop?.Dispose(); Backdrop = null; };
    }
    internal void ShowAt(int x, int y, int size)
    {
        new WindowInteropHelper(this).EnsureHandle();
        try
        {
            Position(x, y, size);
            Backdrop?.Resize(size);
            Backdrop?.SetVisible(true);
            Show();
            Position(x, y, size);
        }
        catch (COMException) { Hide(); Backdrop?.Dispose(); Backdrop = null; }
    }
    private void Position(int x, int y, int size)
    {
        if (!NativeMethods.SetWindowPos(_handle, -1, x, y, size, size, 0x10)) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }
}
