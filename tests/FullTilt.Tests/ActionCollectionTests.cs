using System.ComponentModel;
using System.Text.Json;
using FullTilt.Core;
using FullTilt.Windows;

static class ActionCollectionTests
{
    private const ControllerButtons LB = ControllerButtons.LB, RB = ControllerButtons.RB;
    private static AppSettings Map(InputAction left, InputAction right = InputAction.None) => new()
    {
        Mappings = new() { [Control.LB] = left, [Control.RB] = right, [Control.L3] = InputAction.StickyCtrl,
            [Control.R3] = InputAction.StickyShift, [Control.X] = InputAction.StickyAlt, [Control.Y] = InputAction.ReleaseCancel }
    };
    public static void Register(TestSuite suite)
    {
        var shortcuts = new (InputAction Action, ushort Key, ushort[] Modifiers)[]
        {
            (InputAction.TaskView, 0x09, [KeyboardOwnership.Win]),
            (InputAction.SnapLeft, 0x25, [KeyboardOwnership.Win]), (InputAction.SnapRight, 0x27, [KeyboardOwnership.Win]),
            (InputAction.PreviousApp, 0x09, [KeyboardOwnership.Alt]),
            (InputAction.NextTab, 0x09, [KeyboardOwnership.Ctrl]),
            (InputAction.PreviousTab, 0x09, [KeyboardOwnership.Ctrl, KeyboardOwnership.Shift]),
            (InputAction.NewTab, 0x54, [KeyboardOwnership.Ctrl]), (InputAction.CloseTab, 0x57, [KeyboardOwnership.Ctrl]),
            (InputAction.ReopenTab, 0x54, [KeyboardOwnership.Ctrl, KeyboardOwnership.Shift]),
            (InputAction.SelectAll, 0x41, [KeyboardOwnership.Ctrl]), (InputAction.Copy, 0x43, [KeyboardOwnership.Ctrl]),
            (InputAction.Cut, 0x58, [KeyboardOwnership.Ctrl]), (InputAction.Paste, 0x56, [KeyboardOwnership.Ctrl]),
            (InputAction.Backspace, 0x08, []), (InputAction.ArrowUp, 0x26, []), (InputAction.ArrowDown, 0x28, []),
            (InputAction.ArrowLeft, 0x25, []), (InputAction.ArrowRight, 0x27, []),
            (InputAction.Delete, 0x2E, []), (InputAction.Space, 0x20, []), (InputAction.Enter, 0x0D, []),
            (InputAction.Rename, 0x71, []), (InputAction.Undo, 0x5A, [KeyboardOwnership.Ctrl]), (InputAction.Redo, 0x59, [KeyboardOwnership.Ctrl]),
            (InputAction.VolumeUp, 0xAF, []), (InputAction.VolumeDown, 0xAE, []), (InputAction.Mute, 0xAD, []),
            (InputAction.PlayPause, 0xB3, []), (InputAction.NextTrack, 0xB0, []), (InputAction.PreviousTrack, 0xB1, [])
        };
        foreach (var (action, key, modifiers) in shortcuts)
            suite.Add($"{action} dispatches exact ordered balanced shortcut", () =>
            {
                var input = new FakeKeys(); var keyboard = new KeyboardOwnership(input);
                new WindowsActionDispatcher(keyboard, new FakeDesktop()).Execute(action);
                var expected = modifiers.Select(m => new KeyTransition(m, true)).Concat([new(key, true), new(key, false)])
                    .Concat(modifiers.Reverse().Select(m => new KeyTransition(m, false)));
                Check.True(expected.SequenceEqual(input.Events)); Check.True(input.Down.Count == 0); Check.Equal(1, input.Batches);
            });
        suite.Add("Shortcut borrows Ctrl latch and leaves it active", () =>
        {
            var input = new FakeKeys(); var keyboard = new KeyboardOwnership(input); keyboard.SetLatch(StickyModifier.Ctrl, true); input.Events.Clear();
            keyboard.Shortcut(0x43, KeyboardOwnership.Ctrl);
            Check.True(input.Events.SequenceEqual(new KeyTransition[] { new(0x43, true), new(0x43, false) }));
            Check.True(keyboard.IsOwned(KeyboardOwnership.Ctrl)); keyboard.ReleaseAll(); Check.Equal(0, input.Down.Count);
        });
        suite.Add("Copy suspends and restores conflicting Shift while preserving Ctrl", () =>
        {
            var input = new FakeKeys(); var keyboard = new KeyboardOwnership(input);
            keyboard.SetLatch(StickyModifier.Ctrl, true); keyboard.SetLatch(StickyModifier.Shift, true); input.Events.Clear();
            keyboard.Shortcut(0x43, KeyboardOwnership.Ctrl);
            Check.True(input.Events.SequenceEqual(new KeyTransition[] { new(KeyboardOwnership.Shift, false), new(0x43, true), new(0x43, false), new(KeyboardOwnership.Shift, true) }));
            Check.True(keyboard.IsOwned(KeyboardOwnership.Ctrl) && keyboard.IsOwned(KeyboardOwnership.Shift));
        });
        suite.Add("Shortcut requiring both sticky modifiers never releases either latch", () =>
        {
            var input = new FakeKeys(); var keyboard = new KeyboardOwnership(input);
            keyboard.SetLatch(StickyModifier.Ctrl, true); keyboard.SetLatch(StickyModifier.Shift, true); input.Events.Clear();
            keyboard.Shortcut(0x54, KeyboardOwnership.Ctrl, KeyboardOwnership.Shift);
            Check.Equal(2, input.Events.Count); Check.Equal(2, input.Down.Count);
        });
        suite.Add("No-modifier command suspends both FullTilt latches in a single batch", () =>
        {
            var input = new FakeKeys(); var keyboard = new KeyboardOwnership(input);
            keyboard.SetLatch(StickyModifier.Ctrl, true); keyboard.SetLatch(StickyModifier.Shift, true); input.Events.Clear(); var before = input.Batches;
            keyboard.Shortcut(0x71);
            Check.True(input.Events.SequenceEqual(new KeyTransition[] { new(KeyboardOwnership.Ctrl, false), new(KeyboardOwnership.Shift, false), new(0x71, true), new(0x71, false), new(KeyboardOwnership.Ctrl, true), new(KeyboardOwnership.Shift, true) }));
            Check.Equal(before + 1, input.Batches);
        });
        suite.Add("Physical modifiers are borrowed and conflicting physical keys are never released", () =>
        {
            var input = new FakeKeys(); input.Physical.UnionWith([KeyboardOwnership.Ctrl, KeyboardOwnership.Shift, KeyboardOwnership.Alt]);
            var keyboard = new KeyboardOwnership(input); keyboard.SetLatch(StickyModifier.Ctrl, true); keyboard.Shortcut(0x43, KeyboardOwnership.Ctrl); keyboard.ReleaseAll();
            Check.True(input.Events.All(e => e.Key == 0x43)); Check.Equal(3, input.Physical.Count);
        });
        suite.Add("Borrowed physical Ctrl can become a FullTilt latch after physical release", () =>
        {
            var input = new FakeKeys(); input.Physical.Add(0xA3); var keyboard = new KeyboardOwnership(input);
            keyboard.SetLatch(StickyModifier.Ctrl, true); Check.Equal(0, input.Events.Count);
            input.Physical.Clear(); keyboard.SetLatch(StickyModifier.Ctrl, true); Check.True(keyboard.IsOwned(KeyboardOwnership.Ctrl));
            keyboard.SetLatch(StickyModifier.Ctrl, false); Check.Equal(0, input.Down.Count);
        });
        suite.Add("Physically held action key is not released or retriggered", () =>
        {
            var input = new FakeKeys(); input.Physical.Add(0x43); new KeyboardOwnership(input).Shortcut(0x43, KeyboardOwnership.Ctrl); Check.Equal(0, input.Events.Count);
        });
        suite.Add("Cancel after every partial shortcut prefix releases ownership without restoring canceled latches", () =>
        {
            for (var prefix = 0; prefix < 6; prefix++)
            {
                var input = new FakeKeys(); var keyboard = new KeyboardOwnership(input); keyboard.SetLatch(StickyModifier.Shift, true);
                input.NextLimit = prefix;
                try { keyboard.Shortcut(0x43, KeyboardOwnership.Ctrl); throw new Exception("Expected partial input failure"); } catch (Win32Exception) { }
                keyboard.ReleaseAll(); Check.Equal(0, input.Down.Count);
                Check.True(!keyboard.IsOwned(KeyboardOwnership.Ctrl) && !keyboard.IsOwned(KeyboardOwnership.Shift));
            }
        });
        suite.Add("Failed key release remains owned for retry and other keys still release", () =>
        {
            var input = new FakeKeys(); var keyboard = new KeyboardOwnership(input); keyboard.SetLatch(StickyModifier.Ctrl, true); keyboard.SetLatch(StickyModifier.Shift, true);
            input.NextLimit = 0;
            try { keyboard.ReleaseAll(); throw new Exception("Expected failure"); } catch (Win32Exception) { }
            Check.Equal(1, input.Down.Count); keyboard.ReleaseAll(); Check.Equal(0, input.Down.Count);
        });
        suite.Add("Sticky modifiers toggle on fresh edges and collapse simultaneous duplicate bindings", () =>
        {
            var h = Harness.New(Map(InputAction.StickyCtrl, InputAction.StickyCtrl));
            h.Engine.Tick(new(LB | RB), .01); Check.True(h.Engine.CtrlActive && h.Sink.CtrlActive);
            for (var i = 0; i < 100; i++) h.Engine.Tick(new(LB | RB), .01);
            Check.True(h.Engine.CtrlActive); h.Engine.Tick(default, .01); h.Engine.Tick(new(RB), .01); Check.True(!h.Engine.CtrlActive);
            h.Engine.Tick(new(ControllerButtons.RightThumb), .01); Check.True(h.Engine.ShiftActive);
        });
        foreach (var action in new[] { InputAction.VolumeUp, InputAction.VolumeDown })
            suite.Add($"{action} immediate step then 300 ms delay and 80 ms interval", () =>
            {
                var h = Harness.New(Map(action)); h.Engine.Tick(new(LB), .008); Check.Equal(1, h.Sink.Actions.Count);
                for (var i = 0; i < 29; i++) h.Engine.Tick(new(LB), .01);
                Check.Equal(1, h.Sink.Actions.Count); h.Engine.Tick(new(LB), .01); Check.Equal(2, h.Sink.Actions.Count);
                for (var i = 0; i < 7; i++) h.Engine.Tick(new(LB), .01);
                Check.Equal(2, h.Sink.Actions.Count); h.Engine.Tick(new(LB), .01); Check.Equal(3, h.Sink.Actions.Count);
                h.Engine.Tick(default, .01); for (var i = 0; i < 50; i++) h.Engine.Tick(default, .01); Check.Equal(3, h.Sink.Actions.Count);
                h.Engine.Tick(new(LB), .01); Check.Equal(4, h.Sink.Actions.Count);
            });
        suite.Add("Duplicate volume bindings share their clock through overlaps and handoffs", () =>
        {
            var h = Harness.New(Map(InputAction.VolumeUp, InputAction.VolumeUp)); h.Engine.Tick(new(LB), .01);
            h.Engine.Tick(new(LB | RB), .1); h.Engine.Tick(new(RB), .1); Check.Equal(1, h.Sink.Actions.Count);
            h.Engine.Tick(new(RB), .1); Check.Equal(2, h.Sink.Actions.Count);
        });
        suite.Add("Stalled and invalid updates never cause volume catch-up bursts", () =>
        {
            var sink = new FakeSink(); var actions = new ActionCoordinator(sink); actions.Update([InputAction.VolumeUp], [InputAction.VolumeUp], 0);
            foreach (var elapsed in new[] { 20.0, double.NaN, double.PositiveInfinity }) actions.Update([InputAction.VolumeUp], [], elapsed);
            Check.Equal(1, sink.Actions.Count); actions.Update([InputAction.VolumeUp], [], .2); Check.Equal(1, sink.Actions.Count);
            actions.Update([InputAction.VolumeUp], [], .1); Check.Equal(2, sink.Actions.Count);
            actions.Update([InputAction.VolumeUp], [], .2); Check.Equal(3, sink.Actions.Count);
        });
        foreach (var boundary in new[] { "cancel", "reset", "pause", "disconnect", "controller", "mapping", "mode", "stick", "reserved" })
            suite.Add($"{boundary} clears sticky modifiers and volume repeats and rearms neutral", () =>
            {
                var settings = Map(InputAction.VolumeUp); var h = Harness.New(settings);
                var held = new ControllerState(LB | ControllerButtons.LeftThumb | ControllerButtons.RightThumb | ControllerButtons.X);
                h.Engine.Tick(held, .01); Check.True(h.Engine.CtrlActive && h.Engine.ShiftActive && h.Engine.AltActive); Check.Equal(1, h.Sink.Actions.Count);
                switch (boundary)
                {
                    case "cancel": h.Engine.Tick(new(held.Buttons | ControllerButtons.Y), .01); break;
                    case "reset": h.Engine.Reset(); break;
                    case "pause": h.Engine.SetPaused(true); h.Engine.SetPaused(false); break;
                    case "disconnect": h.Engine.Disconnect(); break;
                    case "controller": h.Engine.Configure(settings with { ControllerSlot = 2 }); break;
                    case "mapping": h.Engine.Configure(settings with { Mappings = new(settings.Mappings) { [Control.RB] = InputAction.Copy } }); break;
                    case "mode": h.Engine.Configure(settings with { DPadMode = DPadMode.AssignedActions }); break;
                    case "stick": h.Engine.Configure(settings with { CursorStick = CursorStick.Right }); break;
                    case "reserved": h.Engine.Tick(new(held.Buttons | ControllerButtons.Start | ControllerButtons.Back), .01); h.Engine.SetPaused(false); break;
                }
                Check.True(!h.Engine.CtrlActive && !h.Engine.ShiftActive && !h.Sink.CtrlActive && !h.Sink.ShiftActive && !h.Engine.AltActive && !h.Sink.AltActive);
                for (var i = 0; i < 60; i++) h.Engine.Tick(held, .01);
                Check.Equal(1, h.Sink.Actions.Count); Check.True(h.Engine.AwaitingNeutral);
            });
        foreach (var definition in ActionCatalog.All.Where(a => a.Capabilities == ActivationCapabilities.OneShot && a.Action is not (InputAction.None or InputAction.ReleaseCancel or InputAction.ReleaseModifiers or InputAction.DoubleClick)))
            suite.Add($"{definition.Id} is one-shot despite held input and duplicate bindings", () =>
            {
                var h = Harness.New(Map(definition.Action, definition.Action)); h.Engine.Tick(new(LB | RB), .01);
                for (var i = 0; i < 100; i++) h.Engine.Tick(new(LB | RB), .01);
                Check.Equal(1, h.Sink.Actions.Count); h.Engine.Tick(default, .01); h.Engine.Tick(new(LB), .01); Check.Equal(2, h.Sink.Actions.Count);
            });
        suite.Add("New actions are assignable to physical stick clicks and all D-pad directions", () =>
        {
            foreach (var (control, button) in new[] { (Control.L3, ControllerButtons.LeftThumb), (Control.R3, ControllerButtons.RightThumb),
                (Control.DPadUp, ControllerButtons.DPadUp), (Control.DPadDown, ControllerButtons.DPadDown), (Control.DPadLeft, ControllerButtons.DPadLeft), (Control.DPadRight, ControllerButtons.DPadRight) })
            {
                var h = Harness.New(new() { DPadMode = DPadMode.AssignedActions, Mappings = new() { [control] = InputAction.Copy } });
                h.Engine.Tick(new(button), .01); Check.Equal(InputAction.Copy, h.Sink.Actions.Single());
            }
        });
        foreach (var (action, command, flag) in new[] { (InputAction.MinimizeWindow, WindowCommand.Minimize, 6), (InputAction.MaximizeWindow, WindowCommand.Maximize, 3), (InputAction.RestoreWindow, WindowCommand.Restore, 9), (InputAction.CloseWindow, WindowCommand.Close, -1) })
            suite.Add($"{action} targets root window with correct Windows command", () =>
            {
                var platform = new FakeWindow(); var desktop = new FakeDesktop { WindowHandler = new WindowActions(platform).Execute };
                new WindowsActionDispatcher(new(new FakeKeys()), desktop).Execute(action);
                Check.Equal(command, desktop.Commands.Single()); Check.Equal(flag, platform.Commands.Single().Command); Check.Equal((nint)123, platform.Commands.Single().Window);
            });
        suite.Add("Missing and unsupported windows no-op and rejected commands report failure", () =>
        {
            var platform = new FakeWindow { Usable = false }; var actions = new WindowActions(platform); actions.Execute(WindowCommand.Maximize); Check.Equal(0, platform.Commands.Count);
            platform.Usable = true; platform.Root = 0; actions.Execute(WindowCommand.Close); Check.Equal(0, platform.Commands.Count);
            platform.Root = 123; platform.Accept = false;
            try { actions.Execute(WindowCommand.Restore); throw new Exception("Expected failure"); } catch (Win32Exception) { }
        });
        suite.Add("Back Forward and keyboard toggle route through desktop adapter", () =>
        {
            var desktop = new FakeDesktop(); var keys = new FakeKeys(); var dispatcher = new WindowsActionDispatcher(new(keys), desktop);
            dispatcher.Execute(InputAction.Back); dispatcher.Execute(InputAction.Forward); dispatcher.Execute(InputAction.ToggleKeyboard);
            Check.True(desktop.Navigation.SequenceEqual(new[] { false, true })); Check.Equal(1, desktop.KeyboardRequests); Check.Equal(0, keys.Events.Count);
            dispatcher.ReleaseAll(); Check.Equal(1, desktop.Cancels);
        });
        TouchKeyboardTests.Register(suite);
        RegisterSettingsAndWorker(suite);
    }

    private static void RegisterSettingsAndWorker(TestSuite suite)
    {
        suite.Add("Update 1 settings migrate without movement mapping or overlay loss and preserve exact backup", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path);
            const string json = """{"Version":2,"CursorSpeed":1750,"PrecisionSpeed":345,"DPadTapDistance":17,"PrecisionPercent":35,"CursorStick":"Right","DPadMode":"AssignedActions","ShowStateIndicator":false,"ScrollSpeed":7.5,"LeftDeadZone":0.27,"RightDeadZone":0.31,"ResponseCurve":2.4,"TriggerPressThreshold":180,"TriggerReleaseThreshold":90,"ControllerSlot":2,"Mappings":{"A":"RightClick","LB":"PrecisionToggle","L3":"DragLock","R3":"DoubleClick","DPadUp":"SnapRight","DPadDown":"ReleaseCancel"}}""";
            File.WriteAllText(store.FilePath, json); var load = store.Load(); Check.True(load.Warning is null);
            var settings = load.Settings; Check.Equal(AppSettings.CurrentVersion, settings.Version); Check.Near(1750, settings.CursorSpeed); Check.Near(345, settings.PrecisionSpeed);
            Check.Near(17, settings.DPadTapDistance); Check.Near(35, settings.PrecisionPercent); Check.Equal(CursorStick.Right, settings.CursorStick);
            Check.Equal(DPadMode.AssignedActions, settings.DPadMode); Check.True(!settings.ShowStateIndicator); Check.Near(7.5, settings.ScrollSpeed);
            Check.Near(.27, settings.LeftDeadZone); Check.Near(.31, settings.RightDeadZone); Check.Near(2.4, settings.ResponseCurve);
            Check.Equal(180, settings.TriggerPressThreshold); Check.Equal(90, settings.TriggerReleaseThreshold); Check.Equal(2, settings.ControllerSlot);
            Check.Equal(InputAction.RightClick, settings.Mappings[Control.A]); Check.Equal(InputAction.PrecisionToggle, settings.Mappings[Control.LB]);
            Check.Equal(InputAction.DragLock, settings.Mappings[Control.L3]); Check.Equal(InputAction.DoubleClick, settings.Mappings[Control.R3]);
            Check.Equal(InputAction.SnapRight, settings.Mappings[Control.DPadUp]); Check.Equal(InputAction.ReleaseCancel, settings.Mappings[Control.DPadDown]);
            Check.Equal(json, File.ReadAllText(store.FilePath + ".v2-backup")); Check.Equal(json, File.ReadAllText(store.FilePath));
            Check.True(store.Save(settings) is null); Check.Equal(json, File.ReadAllText(store.FilePath + ".v2-backup"));
        });
        suite.Add("New action IDs all round trip with no sticky state or pending actions persisted", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path);
            foreach (var definition in ActionCatalog.All)
            {
                Check.True(!string.IsNullOrWhiteSpace(definition.Description));
                Check.True(store.Save(Map(definition.Action)) is null); var loaded = store.Load(); Check.Equal(definition.Action, loaded.Settings.Mappings[Control.LB]);
                using var json = JsonDocument.Parse(File.ReadAllText(store.FilePath)); Check.Equal(definition.Id, json.RootElement.GetProperty("Mappings").GetProperty("LB").GetString());
                Check.True(!json.RootElement.TryGetProperty("CtrlActive", out _) && !json.RootElement.TryGetProperty("ShiftActive", out _) && !json.RootElement.TryGetProperty("Pending", out _));
                var h = Harness.New(loaded.Settings); Check.True(!h.Engine.CtrlActive && !h.Engine.ShiftActive && !h.Engine.PendingDoubleClick);
            }
        });
        suite.Add("Legacy action IDs and numeric values stay fixed and defaults assign no new actions", () =>
        {
            foreach (var (id, value) in new[] { ("None", 0), ("LeftClick", 1), ("RightClick", 2), ("MiddleClick", 3), ("TaskView", 4), ("MinimizeWindow", 5), ("CloseWindow", 6), ("SnapLeft", 7), ("SnapRight", 8), ("PrecisionHold", 20), ("PrecisionToggle", 21), ("DoubleClick", 22), ("DragLock", 23), ("ReleaseCancel", 24) })
                Check.Equal(id, ActionCatalog.Get((InputAction)value).Id);
            Check.True(AppSettings.DefaultMappings().Values.All(a => (int)a < 30));
            Check.True(ActionCatalog.All.Select(a => a.Category).Distinct().SequenceEqual(ActionCatalog.Categories));
        });
        suite.Add("Update 2 migration backup failure disables saving and preserves Update 1 JSON", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path); const string json = "{\"Version\":2,\"DPadTapDistance\":12}";
            File.WriteAllText(store.FilePath, json); Directory.CreateDirectory(store.FilePath + ".v2-backup");
            Check.True(store.Load().Warning is not null); Check.True(store.Save(new()) is not null); Check.Equal(json, File.ReadAllText(store.FilePath));
        });
        suite.Add("Worker publishes sticky states and cancels them and repeats on UI release and shutdown", () =>
        {
            var source = new FakeSource(); var sink = new FakeSink(); source.Set(0, default); var service = new ControllerService(Map(InputAction.VolumeUp), source, sink); service.Start();
            try
            {
                Check.Eventually(() => service.Status.ActiveSlot == 0 && !service.Status.AwaitingNeutral);
                source.Set(0, new(LB | ControllerButtons.LeftThumb | ControllerButtons.RightThumb | ControllerButtons.X));
                Check.Eventually(() => service.Status.CtrlActive && service.Status.ShiftActive && sink.Actions.Count > 0);
                service.ResetInput(); Check.Eventually(() => !service.Status.CtrlActive && !service.Status.ShiftActive && service.Status.AwaitingNeutral);
                var count = sink.Actions.Count; Thread.Sleep(350); Check.Equal(count, sink.Actions.Count);
                source.Set(0, default); Check.Eventually(() => !service.Status.AwaitingNeutral);
                source.Set(0, new(ControllerButtons.LeftThumb)); Check.Eventually(() => service.Status.CtrlActive);
            }
            finally { service.Dispose(); }
            Check.True(!sink.CtrlActive && !sink.ShiftActive && !service.Status.CtrlActive);
        });
    }
}

sealed class FakeKeys : IKeyInput
{
    public readonly HashSet<ushort> Physical = [], Down = [];
    public readonly List<KeyTransition> Events = [];
    public int? NextLimit;
    public int Batches;
    public bool IsDown(ushort key) => Physical.Contains(key) || Down.Contains(key);
    public int Send(ReadOnlySpan<KeyTransition> events)
    {
        Batches++; var count = Math.Min(NextLimit ?? events.Length, events.Length); NextLimit = null;
        foreach (var e in events[..count]) { Events.Add(e); if (e.Down) Down.Add(e.Key); else Down.Remove(e.Key); }
        return count;
    }
}
sealed class FakeDesktop : IDesktopActions
{
    public List<WindowCommand> Commands = []; public List<bool> Navigation = []; public int KeyboardRequests, Cancels;
    public Action<WindowCommand>? WindowHandler;
    public string? Error => null;
    public void Window(WindowCommand command) { Commands.Add(command); WindowHandler?.Invoke(command); }
    public void Navigate(bool forward) => Navigation.Add(forward);
    public void ToggleKeyboard() => KeyboardRequests++;
    public int CenterRequests; public List<(int X, int Y)> Scrolls = [];
    public void CenterCursor() => CenterRequests++;
    public void Scroll(int horizontal, int vertical) => Scrolls.Add((horizontal, vertical));
    public void CancelPending() => Cancels++;
}
sealed class FakeWindow : IWindowPlatform
{
    public nint Root = 123; public bool Usable = true, Accept = true;
    public List<(nint Window, int Command)> Commands = [];
    public nint ForegroundRoot() => Root;
    public bool IsUsable(nint window) => Usable;
    public bool Show(nint window, int command) { Commands.Add((window, command)); return Accept; }
    public bool Close(nint window) { Commands.Add((window, -1)); return Accept; }
}
