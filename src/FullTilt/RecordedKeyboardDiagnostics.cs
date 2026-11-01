using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FullTilt.Core;
using Control = FullTilt.Core.Control;

namespace FullTilt;

internal static class RecordedKeyboardDiagnostics
{
    internal static string Verify(string output)
    {
        var settings = PiePresets.Initialize(new(), bind: true);
        var store = new SettingsStore(Path.Combine(output, "recorded-keyboard-isolated"));
        using var model = new SettingsViewModel(settings, store, null, null);
        VerifyClosing(model);
        VerifyHoldHelp(model, output);
        var chord = new KeyboardChord([new(0xA2), new(0xA0), new(0x70)]);
        var keys = new KeybindingsEditor(model) { RecordKeys = _ => chord }; keys.SelectControl(Control.A);
        var button = (Button)keys.FindName("RecordKeysButton"); Click(button);
        Assert(model.Settings.RecordedBindings.Count == 0 && model.Edits.BindingDirty(Control.A), "Recording escaped keybinding draft.");
        Assert(((TextBlock)keys.FindName("CurrentAssignment")).Text == "Ctrl + Shift + F1", "Recording label should contain only the draft shortcut.");
        model.Edits.Replay(EditTab.Keybindings, true); Assert(model.Edits.View(EditTab.Keybindings).RecordedBindings.Count == 0, "Recording Undo failed.");
        model.Edits.Replay(EditTab.Keybindings, false); model.Edits.Discard(EditTab.Keybindings); model.Edits.Replay(EditTab.Keybindings, true);
        Assert(model.Edits.Save(EditTab.Keybindings) && model.Settings.RecordedBindings[Control.A].Label == chord.Label, "Recording Save failed.");
        keys.RecordKeys = _ => null; Click(button); Assert(!model.Edits.Dirty(EditTab.Keybindings), "Recorder Cancel changed a binding.");
        keys.SelectJoystick(CursorStick.Left); Assert(button.Visibility == Visibility.Collapsed, "Joystick travel offers a keyboard binding.");
        keys.SelectControl(Control.DPadUp); Assert(!button.IsEnabled, "Cursor mode exposes D-pad recording."); keys.SelectControl(Control.A);
        var editor = new PieEditor(model) { RecordKeys = _ => chord }; Layout(editor, 1500, 850);
        editor.SelectDPad(2); Click(editor.RecordKeysButton); Layout(editor, 1500, 850);
        Assert(model.Edits.Count(EditTab.PieMenus) == 1 && model.Settings.PieMenus[0].RecordedDPad.Count == 0, "Direction draft count or separation failed.");
        Assert(Descendants<Button>(editor.DPadEditor).Count(b => ReferenceEquals(b.Background, EditChrome.Cherry)) == 1, "Recording highlights unchanged directions.");
        Assert(editor.SelectedAction is null && Descendants<TextBlock>(editor).Any(t => t.Text.Contains("Selected: Media · D-pad Left\nCtrl + Shift + F1")), "Selected recorded direction not identified.");
        var first = editor.Draft!.Menu.Id; editor.SelectMenu(model.Settings.PieMenus[1].Id); Assert(editor.Draft!.Menu.RecordedDPad.Count == 0, "Recording leaked across menus.");
        editor.SelectMenu(first); editor.SelectDPad(2); Assert(editor.Apply(), "Direction recording Save failed.");
        Assert(Descendants<Button>(editor.DPadEditor).All(b => !ReferenceEquals(b.Background, EditChrome.Cherry)), "Saved recording retains draft colors.");
        editor.SelectMenu(model.Settings.PieMenus[1].Id); Click(editor.RecordKeysButton); // first segment
        Assert(editor.Draft!.Menu.RecordedSlots[0].Label == chord.Label, "Segment recording failed.");
        Assert(editor.DPadEditor.TryDrop(0, PieDragData.ForSlot(editor.Draft.Menu.Id, 0)) && editor.Draft.Menu.RecordedDPad[0].Label == chord.Label, "Recorded segment cannot be dropped on a D-pad direction.");
        Assert(editor.Draft.Menu.RecordedSlots[0].Label == chord.Label, "D-pad copy changed recorded source.");
        Assert(editor.Diagram.TryDrop(1, PieDragData.ForSlot(editor.Draft.Menu.Id, 0)) && editor.Draft.Menu.RecordedSlots.ContainsKey(1), "Recorded segment swap failed.");
        foreach (var dark in new[] { false, true })
        {
            ThemePalette.Apply(dark); editor.Refresh(); Layout(editor, 1500, 850);
            Assert(Descendants<ListBox>(editor).Max(l => l.ActualHeight) > 400, "Record button collapsed the action list.");
            Render(editor, output, "recorded-pie-" + (dark ? "dark" : "light"), 1500, 850);
            keys.SetAvailableHeight(850); Render(keys, output, "recorded-keybindings-" + (dark ? "dark" : "light"), 1450, 820);
            var dialog = new KeyboardRecorder(model, "Media · D-pad Left", diagnostic: true);
            var state = new KeyboardRecording(); state.Update(new(0xA2), true); state.Update(new(0xA0), true); state.Update(new(0x70), true);
            dialog.Changed(state); Render((FrameworkElement)dialog.Content, output, "keyboard-recorder-listening-" + (dark ? "dark" : "light"), 490, 330);
            state.Update(new(0x70), false); state.Update(new(0xA0), false); state.Update(new(0xA2), false); dialog.Changed(state);
            Assert(dialog.Recording?.Label == chord.Label, "Recorder review differs from captured keys.");
            Render((FrameworkElement)dialog.Content, output, "keyboard-recorder-review-" + (dark ? "dark" : "light"), 490, 330); dialog.Close();
            var strip = new PieDPadStrip(); strip.Render(editor.Draft.Menu, ControllerButtons.DPadUp);
            Assert(Descendants<TextBlock>(strip).Any(t => t.Text == chord.Label), "Recorded strip label missing.");
            Render(strip, output, "recorded-strip-" + (dark ? "dark" : "light"), 880, 72);
        }
        var loaded = new SettingsStore(store.DirectoryPath).Load().Settings;
        Assert(loaded.RecordedBindings[Control.A].Label == chord.Label && loaded.PieMenus[0].RecordedDPad[2].Label == chord.Label, "Editor recordings did not persist.");
        var blockedStore = new SettingsStore(Path.Combine(output, "recorded-keyboard-failed-save")); Directory.CreateDirectory(blockedStore.DirectoryPath);
        File.WriteAllText(blockedStore.FilePath, "{\"Version\":99}"); _ = blockedStore.Load();
        using var blocked = new SettingsViewModel(settings, blockedStore, null, null);
        blocked.Edits.BindingRows().Single(r => r.Control == Control.A).Selection = chord;
        Assert(!blocked.Edits.Save(EditTab.Keybindings) && blocked.Edits.View(EditTab.Keybindings).RecordedBindings.ContainsKey(Control.A) && blocked.Settings.RecordedBindings.Count == 0, "Failed save lost recording draft or changed runtime.");
        ThemePalette.Apply(false);
        return "PASS: 4.7 held-key/chord help for controller bindings/D-pad and tap help for pie segments; Caps Lock recording uses detached Save/Discard/Undo/Redo. Hidden recorder HWND close/deactivation reentry for Use recording, Cancel, title-bar close and focus loss, cancelled close, late callbacks and repeated close requests; Esc recording preserved on acceptance. Existing Record buttons for controller bindings, pie segments and D-pad directions; injected recorder result/cancel; selected target and labels; one-direction cherry; menu isolation; recorded segment swaps and D-pad drops; persisted recordings and retained drafts on failed save. Light/dark editor, recording/review and hint-strip renders. Isolated stores and core key events only: no keyboard hook, live service or synthetic input used. Physical key capture and target-application acceptance remain manual.";
    }
    private static void VerifyHoldHelp(SettingsViewModel model, string output)
    {
        var keys = new KeybindingsEditor(model) { RecordKeys = _ => new([new(0x14)]) }; keys.SelectControl(Control.B);
        Click((Button)keys.FindName("RecordKeysButton"));
        Assert(model.Edits.View(EditTab.Keybindings).RecordedBindings[Control.B].Label == "Caps Lock" && !model.Settings.RecordedBindings.ContainsKey(Control.B), "Caps Lock recording escaped its draft.");
        Assert(((TextBlock)keys.FindName("TargetHelp")).Text.Contains("Hold the recorded keys"), "Recorded key help still describes a tap.");
        model.Edits.Replay(EditTab.Keybindings, true); Assert(!model.Edits.View(EditTab.Keybindings).RecordedBindings.ContainsKey(Control.B), "Caps Lock Undo failed.");
        model.Edits.Replay(EditTab.Keybindings, false); model.Edits.Discard(EditTab.Keybindings);
        Assert(!model.Settings.RecordedBindings.ContainsKey(Control.B), "Caps Lock Discard changed runtime.");
        foreach (var holdable in new[] { true, false })
        {
            var dialog = new KeyboardRecorder(model, holdable ? "B" : "Media · Segment 1", diagnostic: true, holdable: holdable);
            var text = Descendants<TextBlock>((DependencyObject)dialog.Content).Select(t => t.Text).ToArray();
            Assert(text.Any(t => t.Contains(holdable ? "Keys stay down" : "Pie segments tap")), "Recorder help has the wrong playback scope.");
            Render((FrameworkElement)dialog.Content, output, holdable ? "held-key-recorder" : "pie-tap-recorder", 490, 330); dialog.Close();
        }
    }
    private static void VerifyClosing(SettingsViewModel model)
    {
        var foreground = FullTilt.Windows.NativeMethods.GetForegroundWindow();
        var saved = model.Settings;
        foreach (var path in new[] { "Use recording", "Cancel", "Close", "Focus loss" })
        {
            var dialog = new KeyboardRecorder(model, "B", diagnostic: true) { ShowActivated = false };
            var state = new KeyboardRecording(); state.Update(new(0x1B), true); state.Update(new(0x1B), false); dialog.Changed(state);
            var buttons = Descendants<Button>((DependencyObject)dialog.Content).ToArray();
            // A hidden HWND enters WPF's real native closing path without showing or installing a hook.
            _ = new WindowInteropHelper(dialog).EnsureHandle();
            var closing = 0; var closed = 0;
            dialog.Closing += (_, _) =>
            {
                closing++;
                dialog.CancelOnDeactivation(); // Same path as deactivation during WPF modal teardown.
            };
            dialog.Closed += (_, _) => { closed++; dialog.CancelOnDeactivation(); };
            if (path == "Focus loss") dialog.CancelOnDeactivation();
            else Click(buttons.Single(b => Equals(b.Content, path) || path == "Close" && System.Windows.Automation.AutomationProperties.GetName(b) == "Close"));
            Assert(closing == 1 && closed == 1, "Recorder close was reentrant: " + path);
            Assert(path == "Use recording" ? dialog.Result?.Label == "Esc" : dialog.Result is null, "Recorder close altered acceptance/cancellation: " + path);
            var before = dialog.Recording;
            var late = new KeyboardRecording(); late.Update(new(65), true); late.Update(new(65), false);
            dialog.Changed(late); dialog.CancelOnDeactivation();
            Assert(ReferenceEquals(before, dialog.Recording), "Late recording callback changed a closed recorder.");
        }
        var cancelled = new KeyboardRecorder(model, "B", diagnostic: true) { ShowActivated = false };
        _ = new WindowInteropHelper(cancelled).EnsureHandle();
        var attempts = 0; var completed = 0;
        cancelled.Closing += (_, e) => { attempts++; cancelled.CancelOnDeactivation(); e.Cancel = attempts == 1; };
        cancelled.Closed += (_, _) => completed++;
        cancelled.CancelOnDeactivation(); Assert(attempts == 1 && completed == 0, "Cancelled close destroyed the recorder.");
        cancelled.CancelOnDeactivation(); Assert(attempts == 2 && completed == 1, "Cancelled close left the recorder unable to close.");
        Assert(ReferenceEquals(saved, model.Settings), "Closing recorder changed saved settings.");
        Assert(foreground == FullTilt.Windows.NativeMethods.GetForegroundWindow(), "Hidden recorder closing checks changed foreground.");
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T item) yield return item; foreach (var next in Descendants<T>(child)) yield return next; }
    }
    private static void Layout(FrameworkElement root, int width, int height) { root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); }
    private static void Render(FrameworkElement root, string output, string name, int width, int height)
    {
        Layout(root, width, height); var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle(ThemePalette.Get("#FFFFFF"), null, new Rect(0, 0, width, height));
        bitmap.Render(background); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
    }
}
