using System.ComponentModel;
using FullTilt.Core;
using FullTilt.Windows;

static class HeldKeyboardTests
{
    private static KeyboardChord Chord(params ushort[] keys) => new(keys.Select(k => new KeyboardKey(k, Extended: new KeyTransition(k, true).IsExtended)).ToArray());
    private static AppSettings Recorded(Control control, KeyboardChord chord) => new()
    {
        DPadMode = DPadMode.AssignedActions,
        Mappings = new() { [control] = InputAction.None }, RecordedBindings = new() { [control] = chord }
    };
    public static void Register(TestSuite suite)
    {
        foreach (var control in Enum.GetValues<Control>())
        {
            var target = control;
            suite.Add($"Held keyboard Caps Lock follows {target} beyond tap durations", () =>
            {
                var sink = new OwnedSink(); var engine = Engine(Recorded(target, Chord(0x14)), sink);
                for (var i = 0; i < 1500; i++) engine.Tick(PieTests.State(target), .008);
                Check.Equal(1, sink.Keys.Events.Count); Check.True(sink.Keys.Down.Contains((0x14, false)));
                engine.Tick(default, .008); Check.Equal(2, sink.Keys.Events.Count); Check.Equal(0, sink.Keys.Down.Count);
                Check.True(!sink.Keys.Events[^1].Down);
            });
        }
        foreach (var vk in Enumerable.Range(0x70, 24).Concat(Enumerable.Range(0x30, 10)).Concat(Enumerable.Range(0x60, 10))
            .Concat(new[] { 8, 9, 13, 19, 20, 27, 32, 44, 144, 145, 186, 191, 222, 226, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C, 0xAF }))
        {
            var key = (ushort)vk;
            suite.Add($"Held keyboard key {key:X2} stays down and releases once", () =>
            {
                var input = new Keys(); var keyboard = new KeyboardOwnership(input); var chord = Chord(key);
                for (var i = 0; i < 20; i++) { keyboard.BeginFrame(); keyboard.SetHolds([chord]); }
                Check.Equal(1, input.Events.Count); Check.Equal(1, input.Down.Count);
                keyboard.SetHolds([]); Check.Equal(2, input.Events.Count); Check.Equal(0, input.Down.Count);
            });
        }
        foreach (var action in ActionCatalog.All.Where(a => KeyboardActions.SingleKey(a.Action) is not null).Select(a => a.Action))
        {
            var assigned = action;
            suite.Add($"Held keyboard built-in {assigned} holds on buttons and taps from pie segments", () =>
            {
                var key = KeyboardActions.SingleKey(assigned)!.Value;
                var sink = new OwnedSink(); var engine = Engine(new AppSettings { Mappings = new() { [Control.B] = assigned } }, sink);
                for (var i = 0; i < 500; i++) engine.Tick(new(ControllerButtons.B), .008);
                Check.Equal(1, sink.Keys.Events.Count); Check.True(sink.Keys.Down.Any(k => k.Key == key));
                engine.Tick(default, .008); Check.Equal(0, sink.Keys.Down.Count);
                var start = sink.Keys.Events.Count; Check.True(new ActionCoordinator(sink).ActivatePie(assigned, []));
                Check.Equal(start + 2, sink.Keys.Events.Count); Check.Equal(0, sink.Keys.Down.Count);
                Check.Equal(key, sink.Keys.Events[^2].Key);
            });
        }
        suite.Add("Held keyboard overlapping recorded chords share keys through final release", () =>
        {
            var sink = new OwnedSink(); var settings = Recorded(Control.B, Chord(0xA2, 0xA0, 65));
            settings.Mappings[Control.X] = InputAction.None; settings.RecordedBindings[Control.X] = Chord(0xA2, 66);
            var engine = Engine(settings, sink); engine.Tick(new(ControllerButtons.B), .008);
            Check.Equal(3, sink.Keys.Down.Count); engine.Tick(new(ControllerButtons.B | ControllerButtons.X), .008);
            Check.Equal(4, sink.Keys.Down.Count); engine.Tick(new(ControllerButtons.X), .008);
            Check.True(sink.Keys.Down.SetEquals(new[] { ((ushort)0xA2, false), ((ushort)66, false) }));
            Check.True(!sink.Keys.Events.Any(e => e.Key == 0xA2 && !e.Down));
            engine.Tick(default, .008); Check.Equal(0, sink.Keys.Down.Count);
            Check.Equal((ushort)66, sink.Keys.Events[^2].Key); Check.Equal((ushort)0xA2, sink.Keys.Events[^1].Key);
        });
        suite.Add("Held keyboard duplicate handoff never retaps Caps Lock", () =>
        {
            var sink = new OwnedSink(); var settings = Recorded(Control.B, Chord(0x14)); settings.Mappings[Control.A] = InputAction.None; settings.RecordedBindings[Control.A] = Chord(0x14);
            var engine = Engine(settings, sink); engine.Tick(new(ControllerButtons.B), .008); engine.Tick(new(ControllerButtons.A), .008);
            engine.Tick(new(ControllerButtons.A | ControllerButtons.B), .008); engine.Tick(new(ControllerButtons.B), .008);
            Check.Equal(1, sink.Keys.Events.Count); engine.Tick(default, .008); Check.Equal(2, sink.Keys.Events.Count);
        });
        suite.Add("Held keyboard recorded and built-in Enter share the same ownership", () =>
        {
            var sink = new OwnedSink(); var settings = Recorded(Control.B, Chord(13)); settings.Mappings[Control.A] = InputAction.Enter;
            var engine = Engine(settings, sink); engine.Tick(new(ControllerButtons.B), .008); engine.Tick(new(ControllerButtons.A), .008);
            Check.Equal(1, sink.Keys.Events.Count); engine.Tick(default, .008); Check.Equal(2, sink.Keys.Events.Count);
        });
        suite.Add("Held keyboard physical keys are borrowed until all controller owners release", () =>
        {
            var input = new Keys(); var keyboard = new KeyboardOwnership(input); input.Physical.Add(0x14); input.Physical.Add(0xA3);
            keyboard.SetHolds([Chord(0x14), Chord(0xA2, 65)]); Check.Equal(1, input.Events.Count); Check.Equal((ushort)65, input.Events[0].Key);
            input.Physical.Clear(); keyboard.SetHolds([Chord(0x14), Chord(0xA2, 65)]); Check.Equal(1, input.Events.Count);
            keyboard.SetHolds([]); Check.Equal(2, input.Events.Count); keyboard.SetHolds([Chord(0x14)]);
            Check.Equal(3, input.Events.Count); keyboard.ReleaseAll(); Check.Equal(0, input.Down.Count);
        });
        suite.Add("Held keyboard sticky modifiers coexist, suspend and restore without releasing held modifiers", () =>
        {
            var input = new Keys(); var keyboard = new KeyboardOwnership(input); keyboard.SetLatch(StickyModifier.Ctrl, true);
            keyboard.SetHolds([Chord(0xA2, 65)]); keyboard.SetLatch(StickyModifier.Ctrl, false);
            Check.True(input.Down.Contains((0xA2, false))); keyboard.Shortcut(66); Check.True(input.Down.Contains((0xA2, false)));
            keyboard.SetLatch(StickyModifier.Ctrl, true); keyboard.SetHolds([]); Check.True(input.Down.Contains((0xA2, false)));
            keyboard.SetHolds([Chord(0x14)]); Check.True(!input.Down.Any(k => k.Key == 0xA2)); keyboard.SetLatch(StickyModifier.Ctrl, true);
            Check.True(!input.Down.Any(k => k.Key == 0xA2)); keyboard.SetHolds([]); Check.True(input.Down.Contains((0xA2, false)));
            keyboard.ReleaseAll(); Check.Equal(0, input.Down.Count);
        });
        suite.Add("Held keyboard one-shot actions cannot release an explicitly held key or chord modifier", () =>
        {
            var input = new Keys(); var keyboard = new KeyboardOwnership(input); keyboard.SetHolds([Chord(0xA2, 65)]);
            keyboard.Shortcut(65, 0xA2); keyboard.Play(Chord(65)); Check.Equal(2, input.Events.Count);
            keyboard.Play(Chord(66)); Check.True(input.Down.Contains((0xA2, false)) && input.Down.Contains((65, false)));
            keyboard.SetHolds([]); Check.Equal(0, input.Down.Count);
        });
        suite.Add("Held keyboard same-frame release ledger tolerates delayed native state", () =>
        {
            var input = new Keys(); var keyboard = new KeyboardOwnership(input); keyboard.SetLatch(StickyModifier.Ctrl, true);
            input.ReportedDown.Add(0xA2); input.ReportedDown.Add(0x11);
            keyboard.BeginFrame(); keyboard.SetLatch(StickyModifier.Ctrl, false); keyboard.SetHolds([Chord(0xA2, 0x14)]);
            Check.True(input.Down.Contains((0xA2, false)) && input.Down.Contains((0x14, false)));
            input.ReportedDown.Add(0x14); keyboard.SetHolds([]); var start = input.Events.Count; keyboard.Play(Chord(0x14));
            Check.Equal(start + 2, input.Events.Count); keyboard.BeginFrame(); start = input.Events.Count;
            keyboard.Play(Chord(0x14)); Check.Equal(start, input.Events.Count);
        });
        for (var prefix = 0; prefix < 4; prefix++) foreach (var ending in new[] { false, true })
        {
            var limit = prefix; var release = ending;
            suite.Add($"Held keyboard partial {(release ? "release" : "press")} prefix {limit} retains cleanup ownership", () =>
            {
                var input = new Keys(); var keyboard = new KeyboardOwnership(input); keyboard.SetLatch(StickyModifier.Alt, true);
                if (release) keyboard.SetHolds([Chord(0xA2, 0xA0, 65)]);
                input.Limit = limit;
                try { keyboard.SetHolds(release ? [] : [Chord(0xA2, 0xA0, 65)]); throw new Exception("Expected blocked/partial send."); } catch (Win32Exception) { }
                input.Limit = 0; try { keyboard.ReleaseAll(); } catch (Win32Exception) { }
                keyboard.ReleaseAll(); Check.Equal(0, input.Down.Count);
                keyboard.BeginFrame(); keyboard.SetHolds([]); Check.Equal(0, input.Down.Count);
            });
        }
        suite.Add("Held keyboard preserves exact Enter and numpad Enter release metadata", () =>
        {
            var input = new Keys(); var keyboard = new KeyboardOwnership(input);
            keyboard.SetHolds([new([new(13, 28), new(13, 28, true)])]); Check.Equal(2, input.Down.Count);
            keyboard.SetHolds([new([new(13, 28, true)])]); Check.Equal(1, input.Down.Count); Check.True(input.Down.Contains((13, true)));
            keyboard.ReleaseAll(); Check.Equal(0, input.Down.Count); Check.True(input.Events[^1].IsExtended); Check.Equal((ushort)28, input.Events[^1].ScanCode);
        });
        foreach (var change in new[] { "pause", "disconnect", "configure", "profile", "cancel", "reset" })
        {
            var lifecycle = change;
            suite.Add("Held keyboard lifecycle clears keys and rearms on " + lifecycle, () =>
            {
                var sink = new OwnedSink(); var settings = Recorded(Control.B, Chord(0xA2, 0xA0, 65)); var engine = Engine(settings, sink);
                engine.Tick(new(ControllerButtons.B), .008); Check.Equal(3, sink.Keys.Down.Count);
                switch (lifecycle)
                {
                    case "pause": engine.SetPaused(true); break;
                    case "disconnect": engine.Disconnect(); break;
                    case "configure": engine.Configure(settings with { ControllerSlot = 1 }); break;
                    case "profile": engine.Configure(settings with { ActiveProfileId = Guid.NewGuid() }); break;
                    case "cancel": engine.Tick(new(ControllerButtons.B | ControllerButtons.Y), .008); break;
                    default: engine.Reset(); break;
                }
                Check.Equal(0, sink.Keys.Down.Count); engine.SetPaused(false); engine.Tick(new(ControllerButtons.B), .008); Check.Equal(0, sink.Keys.Down.Count);
                engine.Tick(default, .008); engine.Tick(new(ControllerButtons.B), .008); Check.Equal(3, sink.Keys.Down.Count); engine.Reset();
            });
        }
        foreach (var cursor in new[] { false, true }) for (var direction = 0; direction < 4; direction++)
        {
            var index = direction; var movement = cursor;
            suite.Add($"Held keyboard contextual direction {index} mode {movement} releases on close without defaults", () =>
            {
                var menu = PieMenu.Create("Held", 4).Record(index, Chord(0xA2, 0x70), true);
                var sink = new OwnedSink(); var platform = new FakePiePlatform(); var settings = Recorded(Control.DPadUp + index, Chord(0x14)) with { PieMenus = [menu], PieBindings = new() { [Control.LB] = menu.Id }, DPadMode = movement ? DPadMode.CursorMovement : DPadMode.AssignedActions };
                var engine = Engine(settings, sink, platform); var button = PieTests.State(Control.DPadUp + index).Buttons;
                engine.Tick(new(ControllerButtons.LB), .008); for (var i = 0; i < 500; i++) engine.Tick(new(ControllerButtons.LB | button), .008);
                Check.Equal(2, sink.Keys.Events.Count); Check.Equal(2, sink.Keys.Down.Count); Check.True(engine.PieOpen);
                engine.Tick(new(button), .008); engine.Tick(new(button), .008); Check.Equal(0, sink.Keys.Down.Count); Check.Equal(4, sink.Keys.Events.Count);
                engine.Tick(default, .008); engine.Tick(new(button), .008);
                if (movement) Check.Equal(0, sink.Keys.Down.Count); else Check.True(sink.Keys.Down.Contains((0x14, false)));
                engine.Reset();
            });
        }
        suite.Add("Held keyboard pie mouse conflicts release once and require a fresh press", () =>
        {
            var menu = PieMenu.Create("Held", 4).Record(0, Chord(65), true).AssignDPad(2, InputAction.LeftClick);
            var sink = new OwnedSink(); var engine = Engine(new AppSettings { PieMenus = [menu], PieBindings = new() { [Control.LB] = menu.Id } }, sink, new FakePiePlatform());
            engine.Tick(new(ControllerButtons.LB), .008); engine.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp), .008); Check.Equal(1, sink.Keys.Down.Count);
            engine.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp | ControllerButtons.DPadLeft), .008); Check.Equal(0, sink.Keys.Down.Count);
            engine.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp), .008); engine.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp), .008); Check.Equal(0, sink.Keys.Down.Count);
            engine.Tick(new(ControllerButtons.LB), .008); engine.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp), .008); Check.Equal(1, sink.Keys.Down.Count); engine.Reset();
        });
        foreach (var lifecycle in new[] { "focus", "pause", "disconnect", "reset" })
        {
            var change = lifecycle;
            suite.Add("Held keyboard contextual keys clean up on " + change, () =>
            {
                var menu = PieMenu.Create("Held", 4).Record(0, Chord(0xA2, 65), true); var sink = new OwnedSink(); var platform = new FakePiePlatform();
                var engine = Engine(new AppSettings { PieMenus = [menu], PieBindings = new() { [Control.LB] = menu.Id } }, sink, platform);
                engine.Tick(new(ControllerButtons.LB), .008); engine.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp), .008); Check.Equal(2, sink.Keys.Down.Count);
                if (change == "focus") { platform.Foreground = 2; engine.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp), .008); }
                else if (change == "pause") engine.SetPaused(true); else if (change == "disconnect") engine.Disconnect(); else engine.Reset();
                Check.Equal(0, sink.Keys.Down.Count); Check.True(!engine.PieOpen);
            });
        }
        suite.Add("Held keyboard contextual same-key release still permits the selected pie tap", () =>
        {
            var menu = PieMenu.Create("Held", 4).Record(0, Chord(0x14)).Record(0, Chord(0x14), true); var sink = new OwnedSink();
            var engine = Engine(new AppSettings { PieMenus = [menu], PieBindings = new() { [Control.LB] = menu.Id } }, sink, new FakePiePlatform());
            engine.Tick(new(ControllerButtons.LB, LeftY: 32767), .008); engine.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp, LeftY: 32767), .008);
            sink.Keys.ReportedDown.Add(0x14); engine.Tick(new(ControllerButtons.DPadUp, LeftY: 32767), .008);
            Check.Equal(4, sink.Keys.Events.Count); Check.Equal(0, sink.Keys.Down.Count);
        });
        suite.Add("Held keyboard recorder entry releases keys and exit requires neutral", () =>
        {
            var source = new FakeSource(); var sink = new OwnedSink(); source.Set(0, default);
            using var service = new ControllerService(Recorded(Control.B, Chord(0x14)), source, sink); service.Start(); Check.Eventually(() => !service.Status.AwaitingNeutral);
            source.Set(0, new(ControllerButtons.B)); Check.Eventually(() => sink.Keys.PressedCount == 1);
            Thread.Sleep(2100); Check.Equal(1, sink.Keys.PressedCount);
            service.BeginKeyboardRecording(); Check.Eventually(() => service.KeyboardRecordingReady && sink.Keys.PressedCount == 0);
            service.EndKeyboardRecording(); Check.Eventually(() => !service.KeyboardRecordingReady); Thread.Sleep(40); Check.Equal(0, sink.Keys.PressedCount);
            source.Set(0, default); Check.Eventually(() => !service.Status.AwaitingNeutral); source.Set(0, new(ControllerButtons.B)); Check.Eventually(() => sink.Keys.PressedCount == 1);
        });
        suite.Add("Held keyboard worker partial press pauses, cleans up and requires neutral", () =>
        {
            var source = new FakeSource(); var sink = new OwnedSink(); source.Set(0, default);
            using var service = new ControllerService(Recorded(Control.B, Chord(0xA2, 0xA0, 0x14)), source, sink);
            service.Start(); Check.Eventually(() => !service.Status.AwaitingNeutral); sink.Keys.Limit = 1; source.Set(0, new(ControllerButtons.B));
            Check.Eventually(() => service.Status.Paused && sink.Keys.PressedCount == 0); service.SetPaused(false);
            Check.Eventually(() => !service.Status.Paused); Thread.Sleep(40); Check.Equal(0, sink.Keys.PressedCount);
            source.Set(0, default); Check.Eventually(() => !service.Status.AwaitingNeutral); source.Set(0, new(ControllerButtons.B)); Check.Eventually(() => sink.Keys.PressedCount == 3);
        });
        suite.Add("Held keyboard opening a pie preserves ordinary holds until physical release", () =>
        {
            var menu = PieMenu.Create("Held", 4).Record(0, Chord(0x70), true); var sink = new OwnedSink();
            var settings = Recorded(Control.B, Chord(0x14)) with { PieMenus = [menu], PieBindings = new() { [Control.LB] = menu.Id } };
            var engine = Engine(settings, sink, new FakePiePlatform()); engine.Tick(new(ControllerButtons.B), .008); Check.True(sink.Keys.Down.Contains((0x14, false)));
            engine.Tick(new(ControllerButtons.B | ControllerButtons.LB), .008); Check.True(sink.Keys.Down.Contains((0x14, false)));
            engine.Tick(new(ControllerButtons.B | ControllerButtons.LB | ControllerButtons.DPadUp), .008); Check.Equal(2, sink.Keys.Down.Count);
            engine.Tick(new(ControllerButtons.B | ControllerButtons.DPadUp), .008); engine.Tick(new(ControllerButtons.B), .008); engine.Tick(new(ControllerButtons.B), .008);
            Check.True(sink.Keys.Down.SetEquals(new[] { ((ushort)0x14, false) })); Check.Equal(1, sink.Keys.Events.Count(k => k.Key == 0x14));
            engine.Tick(default, .008); Check.Equal(0, sink.Keys.Down.Count); engine.Reset();
        });
        suite.Add("Held keyboard built-in holds survive a pie while new ordinary presses remain suppressed", () =>
        {
            var menu = PieMenu.Create("Held", 4); var sink = new OwnedSink();
            var engine = Engine(new AppSettings { Mappings = new() { [Control.B] = InputAction.Space, [Control.X] = InputAction.Enter }, PieMenus = [menu], PieBindings = new() { [Control.LB] = menu.Id } }, sink, new FakePiePlatform());
            engine.Tick(new(ControllerButtons.B), .008); engine.Tick(new(ControllerButtons.B | ControllerButtons.LB | ControllerButtons.X), .008);
            Check.True(sink.Keys.Down.SetEquals(new[] { ((ushort)0x20, false) }));
            engine.Tick(new(ControllerButtons.B | ControllerButtons.X), .008); engine.Tick(new(ControllerButtons.X), .008); Check.Equal(0, sink.Keys.Down.Count);
            engine.Tick(default, .008); engine.Tick(new(ControllerButtons.X), .008); Check.True(sink.Keys.Down.Contains((13, false))); engine.Reset();
        });
        suite.Add("Held keyboard release-modifiers clears sticky state without dropping a physical hold", () =>
        {
            var sink = new OwnedSink(); var coordinator = new ActionCoordinator(sink);
            coordinator.Update([], [InputAction.StickyCtrl], .008); coordinator.Update([], [], .008, [Chord(0xA2, 65)]);
            coordinator.Update([], [InputAction.ReleaseModifiers], .008, [Chord(0xA2, 65)]);
            Check.True(!coordinator.CtrlActive && sink.Keys.Down.Contains((0xA2, false)) && sink.Keys.Down.Contains((65, false)));
            coordinator.Reset(); Check.Equal(0, sink.Keys.Down.Count);
        });
    }
    private static InputEngine Engine(AppSettings settings, OwnedSink sink, IPiePlatform? platform = null)
    {
        if (!settings.RecordedBindings.ContainsKey(Control.Y)) settings.Mappings[Control.Y] = InputAction.ReleaseCancel;
        var engine = new InputEngine(sink, piePlatform: platform); engine.Configure(settings); engine.Tick(default, .008); return engine;
    }
    private sealed class Keys : IKeyInput
    {
        public readonly HashSet<(ushort Key, bool Extended)> Down = []; public readonly HashSet<ushort> Physical = [], ReportedDown = [];
        public readonly List<KeyTransition> Events = []; public int? Limit; private int _pressedCount;
        public int PressedCount => Volatile.Read(ref _pressedCount);
        public bool IsDown(ushort key) => Physical.Contains(key) || ReportedDown.Contains(key) || Down.Any(k => k.Key == key);
        public int Send(ReadOnlySpan<KeyTransition> transitions)
        {
            var count = Math.Min(Limit ?? transitions.Length, transitions.Length); Limit = null;
            foreach (var item in transitions[..count]) { Events.Add(item); if (item.Down) Down.Add((item.Key, item.IsExtended)); else Down.Remove((item.Key, item.IsExtended)); }
            Volatile.Write(ref _pressedCount, Down.Count); return count;
        }
    }
    private sealed class OwnedSink : IInputSink
    {
        public readonly Keys Keys = new(); private readonly WindowsActionDispatcher _dispatcher;
        public OwnedSink() => _dispatcher = new(new(Keys), new FakeDesktop());
        public void Move(int x, int y) { }
        public void Scroll(int x, int y) { }
        public void SetMouseButton(MouseButton button, bool down) { }
        public void BeginKeyboardFrame() => _dispatcher.BeginKeyboardFrame();
        public void SetKeyboardHolds(ReadOnlySpan<InputAction> actions, IReadOnlyList<KeyboardChord> recordings) => _dispatcher.SetKeyboardHolds(actions, recordings);
        public void Execute(InputAction action) => _dispatcher.Execute(action);
        public void ExecuteKeyboard(KeyboardChord chord) => _dispatcher.ExecuteKeyboard(chord);
        public void SetStickyModifier(StickyModifier modifier, bool active) => _dispatcher.SetStickyModifier(modifier, active);
        public void ReleaseAll() => _dispatcher.ReleaseAll();
        public void CancelPendingActions() => _dispatcher.CancelPending();
    }
}
