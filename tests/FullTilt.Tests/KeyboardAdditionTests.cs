using System.ComponentModel;
using FullTilt.Core;
using FullTilt.Windows;

static class KeyboardAdditionTests
{
    public static void Register(TestSuite suite)
    {
        suite.Add("Sticky Alt toggles once per edge and shares Ctrl Shift ownership", () =>
        {
            var sink = new FakeSink(); var actions = new ActionCoordinator(sink);
            actions.Update([], [InputAction.StickyCtrl, InputAction.StickyShift, InputAction.StickyAlt, InputAction.StickyAlt], 0);
            Check.True(actions.AltActive && sink.AltActive && sink.CtrlActive && sink.ShiftActive);
            actions.Update([InputAction.StickyAlt], [], .1); Check.True(actions.AltActive);
            actions.Update([], [InputAction.StickyAlt], 0); Check.True(!actions.AltActive && !sink.AltActive && sink.CtrlActive && sink.ShiftActive);
            actions.ActivatePie(InputAction.StickyAlt, []); Check.True(actions.AltActive);
            actions.Reset(); Check.True(!sink.AltActive && !sink.CtrlActive && !sink.ShiftActive);
        });
        foreach (var (action, key) in new[] { (InputAction.Delete, (ushort)0x2E), (InputAction.Space, (ushort)0x20), (InputAction.Enter, (ushort)0x0D) })
        {
            suite.Add($"{action} suspends owned Alt for a plain one-tap key then restores it", () =>
            {
                var input = new FakeKeys(); var keyboard = new KeyboardOwnership(input); keyboard.SetLatch(StickyModifier.Alt, true); input.Events.Clear();
                new WindowsActionDispatcher(keyboard, new FakeDesktop()).Execute(action);
                Check.True(input.Events.SequenceEqual(new KeyTransition[] { new(KeyboardOwnership.Alt, false), new(key, true), new(key, false), new(KeyboardOwnership.Alt, true) }));
                Check.True(keyboard.IsOwned(KeyboardOwnership.Alt)); keyboard.ReleaseAll(); Check.Equal(0, input.Down.Count);
            });
            suite.Add($"{action} is available and executes once as a pie action", () =>
            {
                var sink = new FakeSink(); var coordinator = new ActionCoordinator(sink);
                Check.True(ActionCatalog.SupportsPie(action)); coordinator.ActivatePie(action, []);
                Check.True(sink.Actions.SequenceEqual(new[] { action })); coordinator.Update([], [], .2); Check.Equal(1, sink.Actions.Count);
            });
        }
        suite.Add("Sticky Alt respects physical right Alt and preserves compatible Alt shortcuts", () =>
        {
            var input = new FakeKeys(); input.Physical.Add(0xA5); var keyboard = new KeyboardOwnership(input);
            keyboard.SetLatch(StickyModifier.Alt, true); keyboard.SetLatch(StickyModifier.Alt, false); keyboard.ReleaseAll(); Check.Equal(0, input.Events.Count);
            input.Physical.Clear(); keyboard.SetLatch(StickyModifier.Alt, true); input.Events.Clear();
            keyboard.Shortcut(0x09, KeyboardOwnership.Alt); Check.True(input.Events.SequenceEqual(new KeyTransition[] { new(0x09, true), new(0x09, false) }));
            Check.True(keyboard.IsOwned(KeyboardOwnership.Alt)); keyboard.ReleaseAll(); Check.Equal(0, input.Down.Count);
        });
        for (var prefix = 0; prefix < 4; prefix++)
        {
            var limit = prefix;
            suite.Add($"Sticky Alt shortcut partial prefix {limit} retains cleanup ownership", () =>
            {
                var input = new FakeKeys(); var keyboard = new KeyboardOwnership(input); keyboard.SetLatch(StickyModifier.Alt, true); input.NextLimit = limit;
                try { keyboard.Shortcut(0x2E); throw new Exception("Expected partial-send failure"); } catch (Win32Exception) { }
                keyboard.ReleaseAll(); Check.Equal(0, input.Down.Count);
            });
        }
        suite.Add("Sticky Alt worker status activates and clears on reset", () =>
        {
            var source = new FakeSource(); source.Set(0, default);
            using var service = new ControllerService(new AppSettings { Mappings = new() { [Control.X] = InputAction.StickyAlt } }, source, new FakeSink()); service.Start();
            Check.Eventually(() => !service.Status.AwaitingNeutral); source.Set(0, new(ControllerButtons.X)); Check.Eventually(() => service.Status.AltActive);
            service.ResetInput(); Check.Eventually(() => !service.Status.AltActive && service.Status.AwaitingNeutral);
        });
        suite.Add("Keyboard additions migrate exact v7 settings and roundtrip button and pie IDs", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path);
            const string json = "{\"Version\":7,\"SpeedBoostMultiplier\":3.25,\"CursorSpeed\":1735,\"Mappings\":{\"LB\":\"SpeedBoostHold\"},\"PiePresetMigration\":1}";
            File.WriteAllText(store.FilePath, json); var load = store.Load(); Check.True(load.Warning is null);
            Check.Equal(json, File.ReadAllText(store.FilePath + ".v7-backup")); Check.Near(3.25, load.Settings.SpeedBoostMultiplier); Check.Equal(InputAction.SpeedBoostHold, load.Settings.Mappings[Control.LB]);
            var menu = PieMenu.Create("Keys", 4) with { Slots = [InputAction.StickyAlt, InputAction.Delete, InputAction.Space, InputAction.Enter] };
            Check.True(store.Save(load.Settings with { PieMenus = [menu], Mappings = new(load.Settings.Mappings) { [Control.X] = InputAction.StickyAlt, [Control.Y] = InputAction.Delete, [Control.A] = InputAction.Space, [Control.B] = InputAction.Enter } }) is null);
            var again = new SettingsStore(dir.Path).Load().Settings;
            Check.True(again.PieMenus[0].Slots.SequenceEqual(menu.Slots)); Check.Equal(InputAction.StickyAlt, again.Mappings[Control.X]); Check.Equal(InputAction.Enter, again.Mappings[Control.B]);
            Check.Near(3.25, again.SpeedBoostMultiplier); Check.Near(1735, again.CursorSpeed); Check.True(!File.ReadAllText(store.FilePath).Contains("AltActive"));
        });
        suite.Add("Controller wheel metadata preserves exact native axes and signed deltas", () =>
        {
            foreach (var horizontal in new[] { false, true }) foreach (var delta in new[] { -240, -120, -8, 1, 8, 120, 240 })
            {
                var input = WindowsInputSink.WheelInput(horizontal, delta); var mouse = input.Data.Mouse;
                Check.Equal(0u, input.Type); Check.Equal(horizontal ? 0x1000u : 0x0800u, mouse.Flags); Check.Equal(unchecked((uint)delta), mouse.MouseData);
                Check.Equal(WindowsInputSink.WheelInputTag, mouse.ExtraInfo); Check.Equal(0, mouse.Dx); Check.Equal(0, mouse.Dy); Check.Equal(0u, mouse.Time);
            }
        });
    }
}
