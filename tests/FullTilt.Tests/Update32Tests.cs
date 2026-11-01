using FullTilt.Core;
using FullTilt.Windows;

static class Update32Tests
{
    public static void Register(TestSuite suite)
    {
        var chords = new[] { PauseShortcutRules.Default, ControllerButtons.Back | ControllerButtons.LB | ControllerButtons.RB,
            ControllerButtons.Start | ControllerButtons.Back | ControllerButtons.LB | ControllerButtons.RB };
        foreach (var chord in chords)
        {
            var buttons = PauseShortcutRules.Buttons.Where(b => (b & chord) != 0).ToArray();
            suite.Add($"Pause shortcut {buttons.Length} buttons toggles once and needs complete release", () =>
            {
                var h = Harness.New(new() { PauseShortcut = chord });
                ControllerButtons partial = 0;
                foreach (var button in buttons[..^1]) { partial |= button; h.Engine.Tick(new(partial | ControllerButtons.A), .01); Check.True(!h.Engine.Paused); Check.True(h.Sink.Held[0]); }
                h.Engine.Tick(new(chord), .01); Check.True(h.Engine.Paused);
                for (var i = 0; i < 10; i++) h.Engine.Tick(new(chord), .01);
                Check.True(h.Engine.Paused); h.Engine.Tick(new(partial), .01); h.Engine.Tick(new(chord), .01); Check.True(h.Engine.Paused);
                h.Engine.Tick(default, .01); h.Engine.Tick(new(chord), .01); Check.True(!h.Engine.Paused);
                h.Engine.Tick(new(chord | ControllerButtons.A), .01); Check.True(h.Sink.Held.All(v => !v));
                h.Engine.Tick(default, .01); h.Engine.Tick(new(ControllerButtons.A), .01); Check.True(h.Sink.Held[0]);
            });
            suite.Add($"Pause shortcut {buttons.Length} buttons held across reconnect cannot toggle", () =>
            {
                var h = Harness.New(new() { PauseShortcut = chord }); h.Engine.Disconnect(); h.Engine.Tick(new(chord), .01); Check.True(!h.Engine.Paused);
                h.Engine.Tick(default, .01); h.Engine.Tick(new(chord), .01); Check.True(h.Engine.Paused);
            });
            suite.Add($"Pause shortcut {buttons.Length} buttons persists without active state", () =>
            {
                using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path); Check.True(store.Save(new() { PauseShortcut = chord, SpeedBoostMultiplier = 3.25 }) is null);
                var loaded = store.Load(); Check.True(loaded.Warning is null); Check.Equal(chord, loaded.Settings.PauseShortcut); Check.Near(3.25, loaded.Settings.SpeedBoostMultiplier);
                Check.True(!File.ReadAllText(store.FilePath).Contains("Paused"));
            });
        }
        suite.Add("Shortcut change releases owned input and requires new chord neutral", () =>
        {
            var settings = new AppSettings(); var h = Harness.New(settings); h.Engine.Tick(new(ControllerButtons.A), .01); Check.True(h.Sink.Held[0]);
            var next = settings with { PauseShortcut = ControllerButtons.LB | ControllerButtons.RB }; h.Engine.Configure(next); Check.True(!h.Sink.Held[0]);
            h.Engine.Tick(new(next.PauseShortcut), .01); Check.True(!h.Engine.Paused);
            h.Engine.Tick(default, .01); h.Engine.Tick(new(next.PauseShortcut), .01); Check.True(h.Engine.Paused);
        });
        suite.Add("Shortcut validation enforces distinct 2 to 4 buttons and feasible D-pad", () =>
        {
            foreach (var invalid in new[] { ControllerButtons.None, ControllerButtons.A | ControllerButtons.A, (ControllerButtons)0xffff,
                ControllerButtons.A | ControllerButtons.B | ControllerButtons.X | ControllerButtons.Y | ControllerButtons.LB,
                ControllerButtons.DPadUp | ControllerButtons.DPadDown, ControllerButtons.DPadLeft | ControllerButtons.DPadRight })
            { Check.True(!PauseShortcutRules.IsValid(invalid)); Check.Equal(PauseShortcutRules.Default, new AppSettings { PauseShortcut = invalid }.Validated().PauseShortcut); }
            Check.True(PauseShortcutRules.IsValid(ControllerButtons.LeftThumb | ControllerButtons.RightThumb));
            Check.True(PauseShortcutRules.IsValid(ControllerButtons.DPadUp | ControllerButtons.DPadLeft));
        });
        suite.Add("v8 migration preserves exact backup and malformed shortcut preserves other settings", () =>
        {
            foreach (var value in new[] { "null", "[]", "{}", "\"Unknown\"", "\"A, A\"", "65535", "\"A\"", "\"Start, Back\"" })
            {
                using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path);
                var json = "{\"Version\":8,\"CursorSpeed\":1876,\"SpeedBoostMultiplier\":2.75,\"Mappings\":{\"X\":\"StickyAlt\"},\"PauseShortcut\":" + value + "}";
                File.WriteAllText(store.FilePath, json); var loaded = store.Load(); Check.Equal(json, File.ReadAllText(store.FilePath + ".v8-backup"));
                Check.Near(1876, loaded.Settings.CursorSpeed); Check.Near(2.75, loaded.Settings.SpeedBoostMultiplier); Check.Equal(InputAction.StickyAlt, loaded.Settings.Mappings[Control.X]); Check.Equal(PauseShortcutRules.Default, loaded.Settings.PauseShortcut);
            }
        });
        foreach (var (action, key) in new[] { (InputAction.Backspace, (ushort)0x08), (InputAction.ArrowUp, (ushort)0x26), (InputAction.ArrowDown, (ushort)0x28), (InputAction.ArrowLeft, (ushort)0x25), (InputAction.ArrowRight, (ushort)0x27) })
        {
            suite.Add($"{action} pie activation sends one balanced key and native flags are correct", () =>
            {
                var keys = new FakeKeys(); var keyboard = new KeyboardOwnership(keys); var dispatcher = new WindowsActionDispatcher(keyboard, new FakeDesktop());
                var coordinator = new ActionCoordinator(new ShortcutSink(keyboard, dispatcher)); Check.True(ActionCatalog.SupportsPie(action)); coordinator.ActivatePie(action, []);
                Check.True(keys.Events.SequenceEqual(new KeyTransition[] { new(key, true), new(key, false) }));
                foreach (var down in new[] { false, true })
                { var encoded = NativeKeyInput.Encode(new(key, down)); Check.Equal(key, encoded.Data.Keyboard.VirtualKey); Check.Equal((down ? 0u : 2u) | (key == 0x08 ? 0u : 1u), encoded.Data.Keyboard.Flags); }
            });
        }
        foreach (var (control, button, action) in new[] { (Control.DPadUp, ControllerButtons.DPadUp, InputAction.ArrowUp), (Control.DPadDown, ControllerButtons.DPadDown, InputAction.ArrowDown), (Control.DPadLeft, ControllerButtons.DPadLeft, InputAction.ArrowLeft), (Control.DPadRight, ControllerButtons.DPadRight, InputAction.ArrowRight) })
            suite.Add($"D-pad {action} dispatches once per tap without cursor movement", () =>
            {
                var h = Harness.New(new() { DPadMode = DPadMode.AssignedActions, Mappings = new() { [control] = action } });
                h.Engine.Tick(new(button), .01); for (var i = 0; i < 50; i++) h.Engine.Tick(new(button), .01);
                Check.True(h.Sink.Actions.SequenceEqual(new[] { action })); Check.Equal(0, h.Sink.X); Check.Equal(0, h.Sink.Y);
                h.Engine.Tick(default, .01); h.Engine.Tick(new(button), .01); Check.Equal(2, h.Sink.Actions.Count);
            });
    }
}
