using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace FullTilt;

// Small vector symbols share the app's ink; no font-dependent icon glyphs.
public sealed class UiIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(UiIcon), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(nameof(Ink), typeof(Brush), typeof(UiIcon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush? Ink { get => (Brush?)GetValue(InkProperty); set => SetValue(InkProperty, value); }
    internal bool UsesFallback { get; private set; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); UsesFallback = false;
        var ink = Ink ?? ThemePalette.Get("#161B17");
        var pen = new Pen(ink, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.PushTransform(new ScaleTransform(ActualWidth / 32, ActualHeight / 32));
        void Path(string data, bool fill = false) => dc.DrawGeometry(fill ? ink : null, pen, Geometry.Parse(data));
        void Key(string label)
        {
            dc.DrawRoundedRectangle(null, pen, new Rect(2, 5, 28, 22), 4, 4);
            var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), label.Length > 4 ? 6.5 : label.Length > 2 ? 8 : 13, ink, 1);
            if (text.Width > 24) text.SetFontSize(6.5 * 24 / text.Width);
            dc.DrawText(text, new Point(16 - text.Width / 2, 16 - text.Height / 2));
        }
        switch (Kind)
        {
            case "Settings":
                Path("M5,3 L5,9 M5,17 L5,29 M16,3 L16,18 M16,26 L16,29 M27,3 L27,7 M27,15 L27,29");
                dc.DrawRoundedRectangle(null, pen, new Rect(2, 9, 6, 8), 2, 2);
                dc.DrawRoundedRectangle(null, pen, new Rect(13, 18, 6, 8), 2, 2);
                dc.DrawRoundedRectangle(null, pen, new Rect(24, 7, 6, 8), 2, 2);
                break;
            case "Keybindings":
                foreach (var x in new[] { 4, 18 }) foreach (var y in new[] { 4, 18 }) dc.DrawRoundedRectangle(ink, null, new Rect(x, y, 10, 10), 2, 2);
                break;
            case "Movement": Path("M7,3 L25,20 L17,20 L21,28 L17,30 L13,22 L7,28 Z"); break;
            case "ResponseCurve": Path("M2,17 L8,17 L12,6 L16,26 L20,13 L24,17 L30,17"); break;
            case "ResetMovement": Path("M26,9 A12,12 0 1 0 28,21 M26,3 L26,10 L19,10"); break;
            case "Pie": Path("M14,3 A12,12 0 1 0 28,18 L14,18 Z", true); Path("M18,2 L18,14 L30,14 A12,12 0 0 0 18,2 Z"); break;
            case "TaskView": dc.DrawRoundedRectangle(null, pen, new Rect(3, 5, 18, 23), 2, 2); Path("M25,10 L30,10 L30,24 L25,24"); break;
            case "CloseWindow": case "ReleaseCancel": Path("M7,7 L25,25 M25,7 L7,25"); break;
            case "MinimizeWindow": dc.DrawEllipse(null, pen, new Point(16, 16), 12, 12); Path("M10,16 L22,16"); break;
            case "MaximizeWindow": dc.DrawRoundedRectangle(null, pen, new Rect(5, 5, 22, 22), 2, 2); break;
            case "RestoreWindow": case "Copy": Path("M10,6 L10,3 L28,3 L28,23 L25,23"); dc.DrawRoundedRectangle(null, pen, new Rect(4, 9, 18, 20), 2, 2); break;
            case "SnapLeft": case "SnapRight":
                dc.DrawRoundedRectangle(null, pen, new Rect(Kind == "SnapLeft" ? 17 : 2, 5, 13, 22), 2, 2);
                Path(Kind == "SnapLeft" ? "M13,16 L2,16 L7,11 M2,16 L7,21" : "M19,16 L30,16 L25,11 M30,16 L25,21"); break;
            case "Mute": case "VolumeUp": case "VolumeDown":
                Path("M3,12 L9,12 L17,5 L17,27 L9,20 L3,20 Z");
                Path(Kind == "Mute" ? "M22,12 L29,20 M29,12 L22,20" : Kind == "VolumeUp" ? "M21,16 L30,16 M25,11 L25,21" : "M21,16 L30,16"); break;
            case "NewTab": case "CloseTab": case "ReopenTab":
                dc.DrawRoundedRectangle(null, pen, new Rect(2, 4, 28, 24), 2, 2); Path("M2,10 L30,10");
                Path(Kind == "CloseTab" ? "M12,15 L21,24 M21,15 L12,24" : Kind == "ReopenTab" ? "M12,14 L7,19 L12,24 M7,19 L22,19 Q26,19 26,25" : "M11,19 L22,19 M16,14 L16,24"); break;
            case "LeftClick": case "RightClick": case "MiddleClick": case "DoubleClick":
                dc.DrawRoundedRectangle(null, pen, new Rect(7, 2, 18, 28), 8, 8); Path("M7,13 L25,13 M16,2 L16,13");
                dc.DrawEllipse(ink, null, new Point(Kind == "RightClick" ? 21 : Kind == "MiddleClick" ? 16 : 11, 8), 2.1, 2.1);
                if (Kind == "DoubleClick") { Path("M1,4 L4,7 M1,11 L4,11"); }
                break;
            case "DragLock": dc.DrawRoundedRectangle(null, pen, new Rect(5, 13, 22, 16), 2, 2); Path("M10,13 L10,8 A6,6 0 0 1 22,8 L22,13 M16,20 L16,24"); break;
            case "PlayPause": Path("M5,5 L18,16 L5,27 Z M24,5 L24,27 M29,5 L29,27"); break;
            case "NextTrack": Path("M4,5 L23,16 L4,27 Z M27,5 L27,27"); break;
            case "PreviousTrack": Path("M28,5 L9,16 L28,27 Z M5,5 L5,27"); break;
            case "None": Path("M9,16 L23,16 M16,9 L16,23"); break;
            case "Paste": Path("M10,6 L5,6 L5,29 L27,29 L27,6 L22,6"); dc.DrawRoundedRectangle(null, pen, new Rect(10, 2, 12, 7), 2, 2); break;
            case "Undo": Path("M12,6 L4,13 L12,20 M4,13 L20,13 Q28,13 28,22 L28,27"); break;
            case "Redo": Path("M20,6 L28,13 L20,20 M28,13 L12,13 Q4,13 4,22 L4,27"); break;
            case "Back": Path("M27,16 L5,16 L13,8 M5,16 L13,24"); break;
            case "Forward": Path("M5,16 L27,16 L19,8 M27,16 L19,24"); break;
            case "PreviousTab": case "NextTab":
                dc.DrawRoundedRectangle(null, pen, new Rect(2, 4, 28, 24), 2, 2); Path("M2,10 L30,10");
                Path(Kind == "PreviousTab" ? "M25,19 L8,19 L13,14 M8,19 L13,24" : "M7,19 L24,19 L19,14 M24,19 L19,24"); break;
            case "Enter":
                dc.DrawRoundedRectangle(null, pen, new Rect(2, 5, 28, 22), 4, 4);
                Path("M24,10 L24,17 L9,17 M14,12 L9,17 L14,22"); break;
            case "Escape": case "Esc": Key("Esc"); break;
            case "Delete": Key("Del"); break;
            case "Space": Key("␣"); break;
            case "Backspace": Path("M11,6 L29,6 L29,26 L11,26 L2,16 Z M16,11 L24,21 M24,11 L16,21"); break;
            case "ArrowUp": case "ArrowDown": case "ArrowLeft": case "ArrowRight":
                dc.DrawRoundedRectangle(null, pen, new Rect(2, 3, 28, 26), 4, 4);
                Path(Kind switch { "ArrowUp" => "M16,24 L16,8 L10,14 M16,8 L22,14", "ArrowDown" => "M16,8 L16,24 L10,18 M16,24 L22,18", "ArrowLeft" => "M24,16 L8,16 L14,10 M8,16 L14,22", _ => "M8,16 L24,16 L18,10 M24,16 L18,22" }); break;
            case "StickyCtrl": Key("Ctrl"); break;
            case "StickyShift": Key("Shift"); break;
            case "StickyAlt": Key("Alt"); break;
            case "SelectAll": Key("Ctrl+A"); break;
            case "Rename": Key("F2"); break;
            case "Cut":
                dc.DrawEllipse(null, pen, new Point(7, 24), 4, 4); dc.DrawEllipse(null, pen, new Point(25, 24), 4, 4);
                Path("M10,21 L26,3 M22,21 L6,3"); break;
            case "PreviousApp":
                dc.DrawRoundedRectangle(null, pen, new Rect(10, 3, 19, 19), 2, 2);
                dc.DrawRoundedRectangle(null, pen, new Rect(2, 11, 19, 19), 2, 2); Path("M6,21 L17,21 L13,17 M17,21 L13,25"); break;
            case "PrecisionHold": case "PrecisionToggle":
                dc.DrawEllipse(null, pen, new Point(16, 16), 8, 8); Path("M16,2 L16,10 M16,22 L16,30 M2,16 L10,16 M22,16 L30,16"); break;
            case "SpeedBoostHold": case "SpeedBoostToggle": Path("M6,5 L16,16 L6,27 M17,5 L27,16 L17,27"); break;
            case "ToggleKeyboard":
                dc.DrawRoundedRectangle(null, pen, new Rect(2, 6, 28, 20), 3, 3);
                foreach (var x in new[] { 7, 13, 19, 25 }) Path($"M{x},12 L{x},13 M{x},17 L{x},18");
                Path("M10,22 L22,22"); break;
            case "PageUp": Key("PgUp"); break;
            case "PageDown": Key("PgDn"); break;
            case "JumpTop": Key("C+Home"); break;
            case "JumpBottom": Key("C+End"); break;
            case "Tab": Key("Tab"); break;
            case "PreviousField": Key("S+Tab"); break;
            case "Home": Key("Home"); break;
            case "End": Key("End"); break;
            case "ClipboardHistory": Key("W+V"); break;
            case "EmojiSymbols": Key("W+."); break;
            case "VoiceTyping": Key("W+H"); break;
            case "Save": Key("C+S"); break;
            case "SaveAs": Key("C+S+S"); break;
            case "Find": Key("C+F"); break;
            case "OpenItem": Key("Enter"); break;
            case "DeleteItem": Key("Del"); break;
            case "NewFolder": Key("C+S+N"); break;
            case "Properties": Key("A+Enter"); break;
            case "ContextMenu": Key("S+F10"); break;
            case "SnapTop": Key("W+A+Up"); break;
            case "SnapBottom": Key("W+A+Down"); break;
            case "NextApp": Key("A+S+Tab"); break;
            case "ShowDesktop": Key("W+D"); break;
            case "StartMenu": Key("Win"); break;
            case "WindowsSearch": Key("W+S"); break;
            case "NextDesktop": Key("W+C+Right"); break;
            case "PreviousDesktop": Key("W+C+Left"); break;
            case "CreateDesktop": Key("W+C+D"); break;
            case "CloseDesktop": Key("W+C+F4"); break;
            case "FileExplorer": Key("W+E"); break;
            case "QuickSettings": Key("W+A"); break;
            case "Notifications": Key("W+N"); break;
            case "Refresh": Key("F5"); break;
            case "FocusAddressBar": Key("C+L"); break;
            case "FindOnPage": Key("C+F"); break;
            case "BrowserZoomIn": Key("C++"); break;
            case "BrowserZoomOut": Key("C+-"); break;
            case "ResetZoom": Key("C+0"); break;
            case "OpenDownloads": Key("C+J"); break;
            case "TakeScreenshot": Key("W+PrtSc"); break;
            case "SnippingTool": Key("W+S+S"); break;
            case "ScreenRecording": Key("W+S+R"); break;
            case "Magnifier": Key("W++"); break;
            case "MagnifierZoomIn": Key("W++"); break;
            case "MagnifierZoomOut": Key("W+-"); break;
            case "CloseMagnifier": Key("W+Esc"); break;
            case "CenterCursor": dc.DrawEllipse(null, pen, new Point(16,16), 5,5); Path("M16,2 L16,8 M16,24 L16,30 M2,16 L8,16 M24,16 L30,16"); break;
            case "ReleaseModifiers": Key("Ctrl"); Path("M3,2 L29,30"); break;
            case "ScrollUp": case "ScrollDown": case "ScrollLeft": case "ScrollRight":
                Path(Kind switch { "ScrollUp" => "M8,24 L16,16 L24,24 M8,14 L16,6 L24,14", "ScrollDown" => "M8,8 L16,16 L24,8 M8,18 L16,26 L24,18", "ScrollLeft" => "M24,8 L16,16 L24,24 M14,8 L6,16 L14,24", _ => "M8,8 L16,16 L8,24 M18,8 L26,16 L18,24" }); break;
            default: UsesFallback = true; Key("?"); break;
        }
        dc.Pop();
    }
}
