using System.ComponentModel;
using FullTilt.Core;
using FullTilt.Windows;

static class ActionExpansionTests
{
    public static void Register(TestSuite suite)
    {
        var shortcuts = new (InputAction Action, ushort Key, ushort[] Modifiers)[]
        {
            (InputAction.PageUp, 0x21, []),
            (InputAction.PageDown, 0x22, []),
            (InputAction.JumpTop, 0x24, [KeyboardOwnership.Ctrl]),
            (InputAction.JumpBottom, 0x23, [KeyboardOwnership.Ctrl]),
            (InputAction.Escape, 0x1B, []),
            (InputAction.Tab, 0x09, []),
            (InputAction.PreviousField, 0x09, [KeyboardOwnership.Shift]),
            (InputAction.Home, 0x24, []),
            (InputAction.End, 0x23, []),
            (InputAction.ClipboardHistory, 0x56, [KeyboardOwnership.Win]),
            (InputAction.EmojiSymbols, 0xBE, [KeyboardOwnership.Win]),
            (InputAction.VoiceTyping, 0x48, [KeyboardOwnership.Win]),
            (InputAction.Save, 0x53, [KeyboardOwnership.Ctrl]),
            (InputAction.SaveAs, 0x53, [KeyboardOwnership.Ctrl, KeyboardOwnership.Shift]),
            (InputAction.Find, 0x46, [KeyboardOwnership.Ctrl]),
            (InputAction.OpenItem, 0x0D, []),
            (InputAction.DeleteItem, 0x2E, []),
            (InputAction.NewFolder, 0x4E, [KeyboardOwnership.Ctrl, KeyboardOwnership.Shift]),
            (InputAction.Properties, 0x0D, [KeyboardOwnership.Alt]),
            (InputAction.ContextMenu, 0x79, [KeyboardOwnership.Shift]),
            (InputAction.SnapTop, 0x26, [KeyboardOwnership.Win, KeyboardOwnership.Alt]),
            (InputAction.SnapBottom, 0x28, [KeyboardOwnership.Win, KeyboardOwnership.Alt]),
            (InputAction.NextApp, 0x09, [KeyboardOwnership.Alt, KeyboardOwnership.Shift]),
            (InputAction.ShowDesktop, 0x44, [KeyboardOwnership.Win]),
            (InputAction.StartMenu, 0x5B, []),
            (InputAction.WindowsSearch, 0x53, [KeyboardOwnership.Win]),
            (InputAction.NextDesktop, 0x27, [KeyboardOwnership.Win, KeyboardOwnership.Ctrl]),
            (InputAction.PreviousDesktop, 0x25, [KeyboardOwnership.Win, KeyboardOwnership.Ctrl]),
            (InputAction.CreateDesktop, 0x44, [KeyboardOwnership.Win, KeyboardOwnership.Ctrl]),
            (InputAction.CloseDesktop, 0x73, [KeyboardOwnership.Win, KeyboardOwnership.Ctrl]),
            (InputAction.FileExplorer, 0x45, [KeyboardOwnership.Win]),
            (InputAction.QuickSettings, 0x41, [KeyboardOwnership.Win]),
            (InputAction.Notifications, 0x4E, [KeyboardOwnership.Win]),
            (InputAction.Refresh, 0x74, []),
            (InputAction.FocusAddressBar, 0x4C, [KeyboardOwnership.Ctrl]),
            (InputAction.FindOnPage, 0x46, [KeyboardOwnership.Ctrl]),
            (InputAction.BrowserZoomIn, 0xBB, [KeyboardOwnership.Ctrl]),
            (InputAction.BrowserZoomOut, 0xBD, [KeyboardOwnership.Ctrl]),
            (InputAction.ResetZoom, 0x30, [KeyboardOwnership.Ctrl]),
            (InputAction.OpenDownloads, 0x4A, [KeyboardOwnership.Ctrl]),
            (InputAction.TakeScreenshot, 0x2C, [KeyboardOwnership.Win]),
            (InputAction.SnippingTool, 0x53, [KeyboardOwnership.Win, KeyboardOwnership.Shift]),
            (InputAction.ScreenRecording, 0x52, [KeyboardOwnership.Win, KeyboardOwnership.Shift]),
            (InputAction.Magnifier, 0x6B, [KeyboardOwnership.Win]),
            (InputAction.MagnifierZoomIn, 0x6B, [KeyboardOwnership.Win]),
            (InputAction.MagnifierZoomOut, 0x6D, [KeyboardOwnership.Win]),
            (InputAction.CloseMagnifier, 0x1B, [KeyboardOwnership.Win]),
        };
        foreach (var (action, key, modifiers) in shortcuts)
        {
            suite.Add($"Expansion {action} sends exact balanced shortcut", () =>
            {
                var input = new FakeKeys(); var keyboard = new KeyboardOwnership(input);
                new WindowsActionDispatcher(keyboard, new FakeDesktop()).Execute(action);
                var expected = modifiers.Select(m => new KeyTransition(m, true)).Concat([new(key, true), new(key, false)])
                    .Concat(modifiers.Reverse().Select(m => new KeyTransition(m, false)));
                Check.True(expected.SequenceEqual(input.Events)); Check.Equal(0, input.Down.Count);
                Check.Equal(1, input.Batches);
            });
            suite.Add($"Expansion {action} partial sends clean up", () =>
            {
                for (var prefix = 0; prefix < modifiers.Length * 2 + 2; prefix++)
                {
                    var input = new FakeKeys { NextLimit = prefix }; var keyboard = new KeyboardOwnership(input);
                    try { new WindowsActionDispatcher(keyboard, new FakeDesktop()).Execute(action); throw new Exception("Expected partial failure"); }
                    catch (Win32Exception) { }
                    keyboard.ReleaseAll(); Check.Equal(0, input.Down.Count);
                }
            });
            suite.Add($"Expansion {action} works through pie and D-pad assignment", () =>
            {
                var sink = new FakeSink(); var coordinator = new ActionCoordinator(sink);
                Check.True(coordinator.ActivatePie(action, [])); Check.Equal(action, sink.Actions.Single());
                var h = Harness.New(new() { DPadMode = DPadMode.AssignedActions, Mappings = new() { [Control.DPadUp] = action } });
                h.Engine.Tick(new(ControllerButtons.DPadUp), .01);
                for (var i=0; i<50; i++) h.Engine.Tick(new(ControllerButtons.DPadUp), .01);
                Check.Equal(action, h.Sink.Actions.Single());
            });
        }
        suite.Add("Expansion scroll directions and center use desktop adapter only", () =>
        {
            var desktop = new FakeDesktop(); var keys = new FakeKeys(); var dispatcher = new WindowsActionDispatcher(new(keys), desktop);
            foreach(var action in new[] { InputAction.ScrollUp, InputAction.ScrollDown, InputAction.ScrollLeft, InputAction.ScrollRight, InputAction.CenterCursor }) dispatcher.Execute(action);
            Check.True(desktop.Scrolls.SequenceEqual(new[] { (0,120), (0,-120), (-120,0), (120,0) }));
            Check.Equal(1, desktop.CenterRequests); Check.Equal(0, keys.Events.Count);
        });
        suite.Add("Expansion monitor center handles negative origins and wide coordinates", () =>
        {
            Check.Equal((-960,540), CursorCentering.Center(-1920,0,0,1080));
            Check.Equal((1280,-720), CursorCentering.Center(0,-1440,2560,0));
            Check.Equal((-1,-1), CursorCentering.Center(int.MinValue,int.MinValue,int.MaxValue,int.MaxValue));
        });
        suite.Add("Expansion release modifiers preserves drag and boost and wins sticky edges", () =>
        {
            var sink = new FakeSink(); var coordinator = new ActionCoordinator(sink);
            coordinator.Update([], [InputAction.StickyCtrl, InputAction.StickyShift, InputAction.StickyAlt, InputAction.DragLock, InputAction.SpeedBoostToggle], .01);
            Check.True(coordinator.CtrlActive && coordinator.ShiftActive && coordinator.AltActive);
            coordinator.Update([], [InputAction.ReleaseModifiers, InputAction.StickyCtrl], .01);
            Check.True(!coordinator.CtrlActive && !coordinator.ShiftActive && !coordinator.AltActive);
            Check.True(coordinator.DragLocked && coordinator.MouseHeld && coordinator.SpeedBoostActive);
            Check.Equal(0, sink.Actions.Count);
            coordinator.Update([], [InputAction.StickyAlt], .01);
            Check.True(coordinator.ActivatePie(InputAction.ReleaseModifiers, [])); Check.True(!coordinator.AltActive && coordinator.DragLocked);
        });
        suite.Add("Expansion shared page aliases preserve canonical IDs and held actions stay out of pies", () =>
        {
            Check.Equal(ActionCatalog.All.Count, ActionCatalog.All.Select(a => a.Id).Distinct().Count());
            foreach(var action in new[] {InputAction.PageUp, InputAction.PageDown})
            {
                var choices = ActionCatalog.PickerEntries.Where(a => a.Action == action).ToArray();
                Check.Equal(2, choices.Length); Check.Equal(choices[0].Id, choices[1].Id);
                Check.True(choices.Select(a => a.Category).SequenceEqual(new[] {"Cursor & Scroll", "Keyboard & Navigation"}));
            }
            Check.True(ActionCatalog.PieDisabledReason(InputAction.PrecisionHold, false) is not null);
            Check.True(ActionCatalog.PieDisabledReason(InputAction.SpeedBoostHold, false) is not null);
        });
        suite.Add("Expansion v10 migration preserves exact backup and new action settings", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path);
            const string original = """{"Version":10,"CursorSpeed":1234,"SpeedBoostMultiplier":3.25,"Mappings":{"A":"ArrowUp","RB":"StickyAlt"}}""";
            File.WriteAllText(store.FilePath, original); var loaded = store.Load(); Check.True(loaded.Warning is null);
            Check.Equal(original, File.ReadAllText(store.FilePath + ".v10-backup"));
            Check.Near(1234, loaded.Settings.CursorSpeed); Check.Near(3.25, loaded.Settings.SpeedBoostMultiplier);
            Check.Equal(InputAction.StickyAlt, loaded.Settings.Mappings[Control.RB]);
            Check.True(store.Save(loaded.Settings with { Mappings = new(loaded.Settings.Mappings) { [Control.A] = InputAction.ClipboardHistory } }) is null);
            Check.Equal(InputAction.ClipboardHistory, store.Load().Settings.Mappings[Control.A]);
            Check.Equal(original, File.ReadAllText(store.FilePath + ".v10-backup"));
        });
        suite.Add("Expansion failed v10 backup prevents saving", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path);
            const string original = """{"Version":10,"CursorSpeed":1234}""";
            File.WriteAllText(store.FilePath, original); Directory.CreateDirectory(store.FilePath + ".v10-backup");
            Check.True(store.Load().Warning is not null); Check.True(store.Save(new()) is not null);
            Check.Equal(original, File.ReadAllText(store.FilePath));
        });
    }
}
