using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FullTilt.Core;
using IDataObject = System.Windows.IDataObject;

namespace FullTilt;

internal static class PieDPadDiagnostics
{
    public static string Verify(string output)
    {
        VerifyDrops(output);
        var savedCaptions = PieMenu.Create("Editing", 8);
        var draftCaptions = savedCaptions.AssignDPad(0, InputAction.Delete).AssignDPad(1, InputAction.SelectAll).AssignDPad(2, InputAction.Paste).AssignDPad(3, InputAction.Redo);
        foreach (var dark in new[] { true, false }) foreach (var width in new[] { 320, 200 })
        {
            ThemePalette.Apply(dark);
            var captions = new PieDPadEditor(); captions.Render(draftCaptions, savedCaptions, -1);
            Render(output, $"rounded-dpad-captions-{(dark ? "dark" : "light")}-{width}", captions, width, (int)(width * 380d / 320));
            VerifyCaptionLayout(captions);
            captions.Render(draftCaptions, draftCaptions, -1);
            captions.Measure(new Size(width, width * 380d / 320)); captions.Arrange(new Rect(0, 0, width, width * 380d / 320)); captions.UpdateLayout();
            Assert(Descendants<Border>(captions).All(b => !ReferenceEquals(b.Background, EditChrome.Cherry)), "Saved D-pad caption retained draft fill.");
            VerifyCaptionLayout(captions);
        }
        using var model = new SettingsViewModel(PiePresets.Initialize(new(), bind: true), new SettingsStore(Path.Combine(output, "context-dpad-isolated")), null, null);
        var editor = new PieEditor(model) { Confirm = (_, _) => MessageBoxResult.Yes };
        void Layout(double width = 1500, double height = 850) { editor.Measure(new Size(width, height)); editor.Arrange(new Rect(0, 0, width, height)); editor.UpdateLayout(); }
        Layout(); var first = editor.Draft!.Menu.Id; var second = model.Settings.PieMenus[1].Id;
        var up = ((Canvas)((Grid)editor.DPadEditor.Child).Children[2]).Children.OfType<Button>().First();
        up.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        editor.AssignSelected(InputAction.VolumeUp); Layout();
        Assert(editor.Draft!.Menu.DPadActions[0] == InputAction.VolumeUp && model.Settings.PieMenus[0].DPadActions[0] is null && model.Edits.Count(EditTab.PieMenus) == 1, "D-pad edit leaked or incorrect changed-direction count.");
        Assert(Descendants<TextBlock>(editor).Any(t => t.Text.Contains("Selected: Media · D-pad Up")), "Assign panel does not identify menu and direction.");
        Assert(Descendants<Button>(editor.DPadEditor).Count(b => ReferenceEquals(b.Background, EditChrome.Cherry)) == 1, "Unchanged D-pad direction received cherry.");
        Assert(((TextBlock)((Canvas)((Grid)editor.DPadEditor.Child).Children[2]).Children.OfType<Border>().Single(b => ReferenceEquals(b.Background, EditChrome.Cherry)).Child).Text == "Volume Up", "Changed D-pad label must contain only its assignment.");
        editor.SelectMenu(second); Assert(editor.Draft!.Menu.DPadActions.All(a => a is null), "Menu switch reused another D-pad assignment.");
        editor.SelectMenu(first); editor.SelectDPad(0); Assert(editor.SelectedAction == InputAction.VolumeUp, "Selected D-pad action not restored in picker.");
        model.Edits.Replay(EditTab.PieMenus, true); Assert(editor.Draft!.Menu.DPadActions[0] is null, "D-pad Undo failed.");
        model.Edits.Replay(EditTab.PieMenus, false); Assert(editor.Draft!.Menu.DPadActions[0] == InputAction.VolumeUp, "D-pad Redo failed.");
        editor.Discard(); Assert(editor.Draft!.Menu.DPadActions[0] is null, "D-pad Discard failed."); model.Edits.Replay(EditTab.PieMenus, true);
        Assert(editor.Draft!.Menu.DPadActions[0] == InputAction.VolumeUp && editor.Apply(), "Undo Discard or Save failed.");
        Assert(Descendants<Button>(editor.DPadEditor).All(b => !ReferenceEquals(b.Background, EditChrome.Cherry)), "Saving did not clear direction cherry.");
        editor.SelectDPad(1); editor.AssignSelected(InputAction.VolumeDown); editor.SelectDPad(2); editor.AssignSelected(InputAction.PreviousTrack); editor.SelectDPad(3); editor.AssignSelected(InputAction.NextTrack); Assert(editor.Apply(), "Four-direction save failed.");
        editor.SelectDPad(2); editor.AssignSelected(InputAction.Backspace);
        foreach (var dark in new[] { true, false })
        {
            ThemePalette.Apply(dark); editor.Refresh(); Layout();
            Render(output, "context-dpad-editor-" + (dark ? "dark" : "light"), editor, 1500, 850);
            Layout(1050, 700); Render(output, "context-dpad-editor-compact-" + (dark ? "dark" : "light"), editor, 1050, 700);
        }
        editor.Discard(); editor.SelectDPad(0); editor.AssignSelected(InputAction.PrecisionHold); Assert(editor.SelectedAction == InputAction.PrecisionHold, "Held actions absent from D-pad picker.");
        Descendants<Button>(editor).Single(b => AutomationProperties.GetName(b) == "Remove override").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(editor.Draft!.Menu.DPadActions[0] is null && editor.Apply(), "Removing an override did not save.");
        var store = new SettingsStore(Path.Combine(output, "context-dpad-save-failure")); Directory.CreateDirectory(store.DirectoryPath); const string future = "{\"Version\":99}"; File.WriteAllText(store.FilePath, future); _ = store.Load();
        using var blockedModel = new SettingsViewModel(PiePresets.Initialize(new(), bind: true), store, null, null);
        var blocked = new PieEditor(blockedModel); Assert(blocked.DPadEditor.TryDrop(2, new DataObject("FullTilt.PieAction", InputAction.Copy)), "Blocked store rejected draft drop.");
        Assert(!blocked.Apply() && blocked.Draft!.Menu.DPadActions[2] == InputAction.Copy && blockedModel.Settings.PieMenus[0].DPadActions[2] is null && File.ReadAllText(store.FilePath) == future, "Failed save changed runtime/file or lost override.");
        var menu = model.Settings.PieMenus[1].AssignDPad(0, InputAction.ScrollUp).AssignDPad(1, InputAction.ScrollDown).AssignDPad(2, InputAction.PreviousTab).AssignDPad(3, InputAction.NextTab);
        foreach (var dark in new[] { true, false }) foreach (var size in new[] { 440, 880 })
        {
            ThemePalette.Apply(dark); var overlay = new PieOverlay();
            try
            {
                overlay.DisplayAt(new(menu, new(1, -30000, -30000), -1, false) { DPadPressed = ControllerButtons.DPadLeft }, -30000, -30000, size);
                Assert(overlay.ShortcutStrip.Visibility == Visibility.Visible && overlay.ShortcutStrip.Highlighted == ControllerButtons.DPadLeft, "Missing shortcut strip/press flash.");
                var content = (FrameworkElement)overlay.Content;
                Assert(Math.Abs(content.ActualHeight / content.ActualWidth - PieDPadStrip.HeightRatio) < .01, "Overlay strip did not scale uniformly.");
                Render(output, $"context-dpad-overlay-{(dark ? "dark" : "light")}-{size}", content, (int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight));
                var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) }; timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
                Assert(overlay.ShortcutStrip.Highlighted == 0, "D-pad flash did not expire while direction remained held.");
                overlay.Dismiss(); Assert(!overlay.IsVisible, "D-pad strip survived menu close.");
            }
            finally { overlay.Close(); }
        }
        ThemePalette.Apply(false);
        return "PASS: native OLE HGLOBAL export of text action/segment payloads; action-row source arming, held capture-transfer survival and release/scrollbar cancellation; source-to-hit-tested scaled D-pad drops; removed help section and expanded pane fit independent of desktop Menu size. Routed drops on all four buttons and labels; valid hover, invalid/foreign/stale payload rejection and leave/cancel cleanup; held actions; source-preserving segment copies; picker synchronization; detached drafts and Undo/Redo/Discard/Save with failed-save retention. Light/dark expanded/compact/drag-target renders; off-desktop overlays at 100/200% with expiring flash. Isolated settings, native data buffers and offscreen/off-desktop presentation only; physical OLE pointer/controller/display acceptance remains manual.";
    }
    private static void VerifyDrops(string output)
    {
        var legacy = OlePayload(new DataObject("FullTilt.PieAction", InputAction.Copy), "FullTilt.PieAction");
        var native = OlePayload(PieDragData.ForAction(InputAction.Copy), "FullTilt.PieAction");
        File.WriteAllText(Path.Combine(output, "dpad-ole-payload.txt"), "Legacy enum: " + legacy + "\nText ID: " + native + "\n");
        Assert(native.StartsWith("PASS", StringComparison.Ordinal), "Text drag payload cannot cross native OLE: " + native);
        using var model = new SettingsViewModel(PiePresets.Initialize(new(), bind: true), new SettingsStore(Path.Combine(output, "dpad-drops-isolated")), null, null);
        var editor = new PieEditor(model);
        var assignments = new[] { InputAction.VolumeUp, InputAction.VolumeDown, InputAction.PreviousTrack, InputAction.NextTrack };
        for (var direction = 0; direction < 4; direction++)
        {
            editor.Measure(new Size(1500, 850)); editor.Arrange(new Rect(0, 0, 1500, 850)); editor.UpdateLayout();
            var key = Descendants<Button>(editor.DPadEditor).ElementAt(direction);
            var point = key.TransformToAncestor(editor).Transform(new Point(key.ActualWidth / 2, key.ActualHeight / 2));
            var hit = VisualTreeHelper.HitTest(editor, point)?.VisualHit;
            File.AppendAllText(Path.Combine(output, "dpad-hit-testing.txt"), $"Direction {direction}: {point}; leaf={hit?.GetType().Name}; key={key.ActualWidth}x{key.ActualHeight}; AllowDrop={key.AllowDrop}\n");
            Assert(hit is Visual visual && (ReferenceEquals(hit, key) || key.IsAncestorOf(visual)), "Real hit testing does not reach the D-pad key: " + hit?.GetType().Name);
            var before = key.Background;
            var data = PieDragData.ForAction(assignments[direction]);
            var hover = Drag(key, DragDrop.DragOverEvent, data);
            Assert(hover.Handled && hover.Effects == DragDropEffects.Move && editor.DPadEditor.DropHover == direction && editor.Draft!.Menu.DPadActions[direction] is null, "Direction hover assigned an action or failed to highlight.");
            Drag(key, DragDrop.DragLeaveEvent, data);
            Assert(editor.DPadEditor.DropHover == -1 && ReferenceEquals(before, key.Background), "Drag leave did not restore direction colors.");
            var drop = Drag(key, DragDrop.DropEvent, data);
            Assert(drop.Handled && drop.Effects == DragDropEffects.Move && editor.Draft!.Menu.DPadActions[direction] == assignments[direction] && editor.SelectedAction == assignments[direction], "Routed direction drop failed or picker selected another target.");
            Assert(model.Settings.PieMenus[0].DPadActions.All(a => a is null) && model.Edits.Count(EditTab.PieMenus) == direction + 1, "Drop leaked into saved settings or altered another field.");
            model.Edits.Replay(EditTab.PieMenus, true); Assert(editor.Draft!.Menu.DPadActions[direction] is null, "Dropped action is not undoable.");
            model.Edits.Replay(EditTab.PieMenus, false);
        }
        editor.UpdateLayout(); VerifyCaptionLayout(editor.DPadEditor);
        var unchanged = EditFields.Read(EditTab.PieMenus, model.Edits.View(EditTab.PieMenus));
        foreach (var invalid in new[] { new DataObject("FullTilt.PieAction", (InputAction)999), new DataObject("FullTilt.PieAction", "NotAnAction"), PieDragData.ForSlot(Guid.NewGuid(), 0), PieDragData.ForSlot(editor.Draft!.Menu.Id, 100), new DataObject("FullTilt.PieSlot", "bad/slot"), new DataObject("Text", "Copy") })
        {
            var key = Descendants<Button>(editor.DPadEditor).First();
            Assert(Drag(key, DragDrop.DragOverEvent, invalid).Effects == DragDropEffects.None && editor.DPadEditor.DropHover == -1, "Invalid drag was accepted for hover.");
            Assert(Drag(key, DragDrop.DropEvent, invalid).Effects == DragDropEffects.None && EditFields.Equal(unchanged, EditFields.Read(EditTab.PieMenus, model.Edits.View(EditTab.PieMenus))), "Invalid drop changed the draft.");
        }
        Assert(!editor.DPadEditor.TryDrop(-1, new DataObject("FullTilt.PieAction", InputAction.Copy)) && !editor.DPadEditor.TryDrop(4, new DataObject("FullTilt.PieAction", InputAction.Copy)), "Invalid direction accepted.");
        var label = ((Canvas)((Grid)editor.DPadEditor.Child).Children[2]).Children.OfType<Border>().ElementAt(2);
        Assert(Drag(label, DragDrop.DropEvent, PieDragData.ForAction(InputAction.PrecisionHold)).Effects == DragDropEffects.Move && editor.Draft!.Menu.DPadActions[2] == InputAction.PrecisionHold && editor.SelectedAction == InputAction.PrecisionHold, "Assignment label rejected held action drop.");
        var slots = (InputAction[])editor.Draft!.Menu.Slots.Clone();
        var segmentData = PieDragData.ForSlot(editor.Draft.Menu.Id, 0);
        Assert(OlePayload(segmentData, "FullTilt.PieSlot").StartsWith("PASS", StringComparison.Ordinal), "Segment text payload cannot cross native OLE.");
        Assert(editor.DPadEditor.TryDrop(3, segmentData) && editor.Draft.Menu.DPadActions[3] == slots[0] && editor.Draft.Menu.Slots.SequenceEqual(slots), "Segment drop changed its source or copied the wrong action.");
        foreach (var dark in new[] { true, false })
        {
            ThemePalette.Apply(dark); editor.Refresh(); Render(output, "dpad-drop-draft-" + (dark ? "dark" : "light"), editor, 1500, 850);
            var key = Descendants<Button>(editor.DPadEditor).ElementAt(2);
            Drag(key, DragDrop.DragOverEvent, new DataObject("FullTilt.PieAction", InputAction.Copy));
            Render(output, "dpad-drop-hover-" + (dark ? "dark" : "light"), editor, 1500, 850);
            editor.DPadEditor.SetDropHover(-1);
        }
        Assert(editor.Apply() && !model.Edits.Dirty(EditTab.PieMenus) && Descendants<Button>(editor.DPadEditor).All(b => !ReferenceEquals(b.Background, EditChrome.Cherry)), "Dropped assignments did not save/clear cherry.");
        editor.DPadEditor.TryDrop(0, new DataObject("FullTilt.PieAction", InputAction.Copy)); editor.Discard();
        Assert(editor.Draft!.Menu.DPadActions[0] == InputAction.VolumeUp, "Discard did not restore a dropped assignment.");
        model.Edits.Replay(EditTab.PieMenus, true); Assert(editor.Draft!.Menu.DPadActions[0] == InputAction.Copy, "Discard of a drop is not undoable.");
        VerifySourceAndFit(output);
    }
    private static void VerifySourceAndFit(string output)
    {
        using var model = new SettingsViewModel(PiePresets.Initialize(new(), bind: true), new SettingsStore(Path.Combine(output, "dpad-source-isolated")), null, null);
        var editor = new PieEditor(model);
        Render(output, "pie-editor-expanded-fit", editor, 2200, 1100);
        Assert(editor.Diagram.ActualWidth > 440 && editor.Diagram.ActualWidth <= editor.PreviewPane.ColumnDefinitions[0].ActualWidth && editor.Diagram.ActualHeight <= editor.PreviewPane.ActualHeight, "Pie does not use the expanded editor pane.");
        Assert(!Descendants<TextBlock>(editor).Any(t => t.Text.Contains("Hold center over 100 ms")), "Removed gesture-help section is still present.");
        var beforeSize = editor.Diagram.ActualWidth;
        editor.MenuSizePercent = 200; editor.UpdateLayout(); Assert(Math.Abs(beforeSize - editor.Diagram.ActualWidth) < .1, "Desktop Menu size changed editor fit."); editor.Discard();
        editor.SearchBox.Text = "Copy"; Render(output, "dpad-source-filtered", editor, 2200, 1100);
        var row = Descendants<ListBoxItem>(editor).First(r => r.Content is ActionDefinition { Action: InputAction.Copy });
        var calls = 0;
        editor.DiagnosticDragRunner = (_, action, data) =>
        {
            calls++; Assert(action == InputAction.Copy && data.GetData("FullTilt.PieAction", false) is string, "Action source did not create a text OLE payload.");
            for (var direction = 0; direction < 4; direction++)
            {
                editor.UpdateLayout(); var key = Descendants<Button>(editor.DPadEditor).ElementAt(direction);
                var point = key.TransformToAncestor(editor).Transform(new Point(key.ActualWidth / 2, key.ActualHeight / 2));
                var hit = VisualTreeHelper.HitTest(editor, point)?.VisualHit as FrameworkElement;
                Assert(hit is not null && (ReferenceEquals(hit, key) || key.IsAncestorOf(hit)), "Scaled pointer hit misses the D-pad key.");
                var target = hit!;
                while (!target.AllowDrop && target.Parent is FrameworkElement parent) target = parent;
                Assert(target.AllowDrop, "Hit-tested OLE target does not allow drops.");
                Assert(Drag(target, DragDrop.DragOverEvent, data).Effects == DragDropEffects.Move && editor.DPadEditor.DropHover == direction, "Hit-tested source drag did not highlight its direction.");
                Assert(Drag(target, DragDrop.DropEvent, data).Effects == DragDropEffects.Move && editor.Draft!.Menu.DPadActions[direction] == action, "Hit-tested source drag did not assign its direction.");
            }
        };
        row.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent });
        Assert(editor.ActionDragArmed, "Real action row did not arm dragging.");
        editor.ActionCaptureLost(true); Assert(editor.ActionDragArmed, "Capture transfer while pressed canceled action dragging.");
        editor.ContinueActionDrag(new Point(10000, 10000), true);
        Assert(calls == 1 && !editor.ActionDragArmed && model.Settings.PieMenus[0].DPadActions.All(a => a is null), "Drag source was replayed or leaked into runtime.");
        editor.UpdateLayout(); row = Descendants<ListBoxItem>(editor).First(r => r.Content is ActionDefinition { Action: InputAction.Copy });
        row.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent });
        editor.ContinueActionDrag(new Point(10000, 10000), true, scrollbarCaptured: true); Assert(calls == 1, "Scrollbar capture started assignment dragging.");
        editor.ActionCaptureLost(false); Assert(!editor.ActionDragArmed, "Released capture left a stale action drag armed.");
        editor.ContinueActionDrag(new Point(10000, 10000), false); Assert(calls == 1, "Released source replayed an assignment drag.");
        Assert(editor.Apply(), "Hit-tested drops did not save.");
        foreach (var dark in new[] { true, false })
        {
            ThemePalette.Apply(dark); editor.Refresh(); editor.SearchBox.Clear();
            Render(output, "pie-editor-fit-" + (dark ? "dark" : "light"), editor, 2200, 1100);
        }
    }
    private static void VerifyCaptionLayout(PieDPadEditor editor)
    {
        var root = (Grid)editor.Child; var canvas = (Canvas)root.Children[2];
        var keys = canvas.Children.OfType<Button>().ToArray(); var captions = canvas.Children.OfType<Border>().ToArray();
        for (var direction = 0; direction < 4; direction++)
        {
            var key = keys[direction]; var caption = captions[direction];
            var keyCenter = key.TranslatePoint(new Point(key.ActualWidth / 2, key.ActualHeight / 2), canvas);
            var captionCenter = caption.TranslatePoint(new Point(caption.ActualWidth / 2, caption.ActualHeight / 2), canvas);
            Assert(Math.Abs(direction < 2 ? keyCenter.X - captionCenter.X : keyCenter.Y - captionCenter.Y) < .1, "D-pad caption is not centered on its direction button.");
            var bounds = caption.TransformToAncestor(root).TransformBounds(new Rect(caption.RenderSize));
            Assert(caption.CornerRadius.TopLeft == 8 && caption.Padding.Left == 8 && bounds.Left >= 0 && bounds.Right <= root.ActualWidth && bounds.Top >= 60 && bounds.Bottom <= root.ActualHeight - 14, "Rounded D-pad caption escaped its reserved space.");
        }
    }
    private static string OlePayload(DataObject data, string format)
    {
        if (data is not System.Runtime.InteropServices.ComTypes.IDataObject ole) return "No COM interface: " + string.Join(", ", data.GetType().GetInterfaces().Select(t => t.FullName));
        var request = new FORMATETC { cfFormat = (short)DataFormats.GetDataFormat(format).Id, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
        STGMEDIUM medium = default;
        try { ole.GetData(ref request, out medium); return medium.unionmember != 0 ? "PASS: native HGLOBAL provided" : "FAIL: empty native buffer"; }
        catch (Exception ex) { return ex.GetType().Name + ": " + ex.Message; }
        finally { if (medium.unionmember != 0) ReleaseStgMedium(ref medium); }
    }
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
    private static DragEventArgs Drag(FrameworkElement target, RoutedEvent routedEvent, IDataObject data)
    {
        // WPF's OLE event constructor is internal; route packets without a real pointer drag.
        var constructor = typeof(DragEventArgs).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public).Single(c => c.GetParameters().Length == 5);
        var args = (DragEventArgs)constructor.Invoke(new object[] { data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, target, new Point(10, 10) }); args.RoutedEvent = routedEvent; target.RaiseEvent(args); return args;
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private static void Render(string output, string name, FrameworkElement root, int width, int height)
    {
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(output, name + ".png")); png.Save(stream);
    }
}
