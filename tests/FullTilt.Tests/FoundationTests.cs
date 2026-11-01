using System.Text.Json;
using FullTilt.Core;
using FullTilt.Windows;

static class FoundationTests
{
    private const ControllerButtons Right = ControllerButtons.DPadRight;
    private const ControllerButtons Down = ControllerButtons.DPadDown;
    private const ControllerButtons Left = ControllerButtons.DPadLeft;
    private const ControllerButtons Up = ControllerButtons.DPadUp;

    public static void Register(TestSuite suite)
    {
        suite.Add("Rounded half-pixel remainder does not oscillate during zero motion", () =>
        {
            var fraction = new FractionalMotion(); Check.Equal((2, -2), fraction.TakeRounded(1.5, -1.5));
            for (var i = 0; i < 20; i++) Check.Equal((0, 0), fraction.TakeRounded(0, 0));
        });
        suite.Add("Extremely short Windows double-click thresholds select immediate pairs", () =>
        {
            Check.Near(0, ControllerService.ClickIntervalForSystemTiming(20));
            Check.True(ControllerService.ClickIntervalForSystemTiming(100) < .1);
            var sink = new FakeSink(); var engine = new InputEngine(sink, 0); engine.Configure(With((Control.LB, InputAction.DoubleClick))); engine.Tick(default, 0);
            engine.Tick(new(ControllerButtons.LB), 0); Check.Equal(4, sink.ButtonEvents.Count); Check.True(!engine.PendingDoubleClick);
            engine.Tick(new(ControllerButtons.LB), .01); Check.Equal(4, sink.ButtonEvents.Count);
        });
        suite.Add("Tap distance responds on first poll even with zero elapsed", () =>
        {
            var h = Harness.New(new AppSettings { DPadTapDistance = 17, PrecisionSpeed = 100 });
            h.Engine.Tick(new(Right), 0); Check.Equal(17, h.Sink.X);
            h.Engine.Tick(new(Right), .01); Check.Equal(18, h.Sink.X);
        });
        suite.Add("Repeated taps get exactly one nudge each without a release action", () =>
        {
            var h = Harness.New(new AppSettings { DPadTapDistance = 7 });
            for (var i = 0; i < 10; i++) { h.Engine.Tick(new(Right), 0); h.Engine.Tick(new(Right), 0); h.Engine.Tick(default, 0); }
            Check.Equal(70, h.Sink.X);
        });
        suite.Add("Diagonal tap fractions accumulate to normalized total distance", () =>
        {
            var h = Harness.New(new AppSettings { DPadTapDistance = 5 });
            for (var i = 0; i < 100; i++) { h.Engine.Tick(new(Right | Down), 0); h.Engine.Tick(default, 0); }
            Check.Near(500 / Math.Sqrt(2), h.Sink.X, .5); Check.Equal(h.Sink.X, h.Sink.Y);
        });
        suite.Add("Slow D-pad held movement preserves subpixel velocity", () =>
        {
            var h = Harness.New(new AppSettings { DPadTapDistance = 1, PrecisionSpeed = 10 });
            h.Engine.Tick(new(Right), 0);
            for (var i = 0; i < 100; i++) h.Engine.Tick(new(Right), .001);
            Check.Equal(2, h.Sink.X);
        });
        suite.Add("D-pad held motion is independent of poll rate", () =>
        {
            var a = Harness.New(new AppSettings { PrecisionSpeed = 100 }); var b = Harness.New(new AppSettings { PrecisionSpeed = 100 });
            a.Engine.Tick(new(Right), 0); b.Engine.Tick(new(Right), 0);
            for (var i = 0; i < 100; i++) a.Engine.Tick(new(Right), .01);
            for (var i = 0; i < 200; i++) b.Engine.Tick(new(Right), .005);
            Check.Equal(105, a.Sink.X); Check.Equal(a.Sink.X, b.Sink.X);
        });
        suite.Add("Adding a direction nudges only its new axis; release never adds tap", () =>
        {
            var h = Harness.New(new AppSettings { DPadTapDistance = 10, PrecisionSpeed = 100 });
            h.Engine.Tick(new(Right), 0); h.Engine.Tick(new(Right | Down), 0);
            Check.Equal(10, h.Sink.X); Check.Equal(10, h.Sink.Y);
            h.Engine.Tick(new(Right), .01); Check.Equal(11, h.Sink.X); Check.Equal(10, h.Sink.Y);
        });
        suite.Add("Opposites cancel and uncanceling resumes velocity without extra tap", () =>
        {
            var h = Harness.New(new AppSettings { DPadTapDistance = 10, PrecisionSpeed = 100 });
            h.Engine.Tick(new(Right | Left), .01); Check.Equal(0, h.Sink.X);
            h.Engine.Tick(new(Right), .01); Check.Equal(1, h.Sink.X);
            h.Engine.Tick(new(Left), 0); Check.Equal(-9, h.Sink.X);
        });
        suite.Add("Cursor stick swap moves with right and scrolls with left", () =>
        {
            var h = Harness.New(new AppSettings { CursorStick = CursorStick.Right, CursorSpeed = 1000, ScrollSpeed = 10 });
            h.Engine.Tick(new(LeftY: 32767, RightX: 32767), .01);
            Check.Equal(10, h.Sink.X); Check.Equal(0, h.Sink.Y); Check.Equal(12, h.Sink.ScrollY);
        });
        suite.Add("Physical L3 and R3 assignments do not swap with stick roles", () =>
        {
            var settings = With((Control.L3, InputAction.LeftClick), (Control.R3, InputAction.RightClick));
            var h = Harness.New(settings with { CursorStick = CursorStick.Right });
            h.Engine.Tick(new(ControllerButtons.LeftThumb), .01); Check.True(h.Sink.Held[0] && !h.Sink.Held[1]);
            h.Engine.Tick(new(ControllerButtons.RightThumb), .01); Check.True(!h.Sink.Held[0] && h.Sink.Held[1]);
        });
        suite.Add("Active stick swap clears drag and blocks held stick until neutral", () =>
        {
            var settings = With((Control.LB, InputAction.DragLock)); var h = Harness.New(settings);
            h.Engine.Tick(new(ControllerButtons.LB), .01); Check.True(h.Engine.DragLocked);
            h.Engine.Configure(settings with { CursorStick = CursorStick.Right });
            h.Engine.Tick(new(ControllerButtons.LB, RightX: 32767), .01);
            Check.True(!h.Engine.DragLocked && !h.Sink.Held[0]); Check.Equal(0, h.Sink.X);
            h.Engine.Tick(default, .01); h.Engine.Tick(new(RightX: 32767), .01); Check.True(h.Sink.X > 0);
        });
        suite.Add("Physical dead zones remain with sticks while response follows cursor", () =>
        {
            var h = Harness.New(new AppSettings { CursorStick = CursorStick.Right, CursorSpeed = 1000, LeftDeadZone = .1, RightDeadZone = .4, ResponseCurve = 2 });
            h.Engine.Tick(new(LeftY: 10000, RightX: 10000), .01);
            Check.Equal(0, h.Sink.X); Check.True(h.Sink.ScrollY > 0);
            h.Engine.Tick(new(RightX: 22937), .02); Check.Near(5, h.Sink.X, 1);
        });
        suite.Add("Precision hold applies on its press frame and ends on release", () =>
        {
            var h = Harness.New(With((Control.LB, InputAction.PrecisionHold)) with { CursorSpeed = 1000 });
            h.Engine.Tick(new(ControllerButtons.LB, LeftX: 32767), .01); Check.Equal(5, h.Sink.X); Check.True(h.Engine.PrecisionActive);
            h.Engine.Tick(new(LeftX: 32767), .01); Check.Equal(15, h.Sink.X); Check.True(!h.Engine.PrecisionActive);
        });
        suite.Add("Multiple precision holds neither stack nor release another owner", () =>
        {
            var h = Harness.New(With((Control.LB, InputAction.PrecisionHold), (Control.RB, InputAction.PrecisionHold)) with { CursorSpeed = 1000 });
            h.Engine.Tick(new(ControllerButtons.LB | ControllerButtons.RB, LeftX: 32767), .01);
            h.Engine.Tick(new(ControllerButtons.RB, LeftX: 32767), .01); Check.Equal(10, h.Sink.X); Check.True(h.Engine.PrecisionActive);
        });
        suite.Add("Precision toggle fires once per press and combines with held precision", () =>
        {
            var h = Harness.New(With((Control.LB, InputAction.PrecisionToggle), (Control.RB, InputAction.PrecisionHold)));
            h.Engine.Tick(new(ControllerButtons.LB), .01); h.Engine.Tick(new(ControllerButtons.LB), .01); Check.True(h.Engine.PrecisionActive);
            h.Engine.Tick(new(ControllerButtons.RB), .01); h.Engine.Tick(new(ControllerButtons.LB | ControllerButtons.RB), .01);
            Check.True(h.Engine.PrecisionActive); h.Engine.Tick(default, .01); Check.True(!h.Engine.PrecisionActive);
        });
        suite.Add("Simultaneous duplicate toggles do not cancel each other", () =>
        {
            var h = Harness.New(With((Control.LB, InputAction.PrecisionToggle), (Control.RB, InputAction.PrecisionToggle)));
            h.Engine.Tick(new(ControllerButtons.LB | ControllerButtons.RB), .01); Check.True(h.Engine.PrecisionActive);
        });
        suite.Add("Precision percentage affects right cursor but not D-pad or scroll", () =>
        {
            var h = Harness.New(With((Control.LB, InputAction.PrecisionHold)) with { CursorStick = CursorStick.Right, CursorSpeed = 1000, PrecisionPercent = 25, PrecisionSpeed = 100, DPadTapDistance = 9, ScrollSpeed = 10 });
            h.Engine.Tick(new(ControllerButtons.LB, LeftY: 32767, RightX: 32767), .02); Check.Equal(5, h.Sink.X); Check.Equal(24, h.Sink.ScrollY);
            h.Engine.Tick(new(ControllerButtons.LB | Right), .01); Check.Equal(14, h.Sink.X);
            h.Engine.Tick(new(ControllerButtons.LB | Right), .01); Check.Equal(15, h.Sink.X);
        });
        suite.Add("Precision retains the cursor response curve", () =>
        {
            var settings = With((Control.LB, InputAction.PrecisionHold)) with { CursorSpeed = 1000, LeftDeadZone = .2, ResponseCurve = 2, PrecisionPercent = 50 };
            var a = Harness.New(settings); var b = Harness.New(settings);
            for (var i = 0; i < 100; i++) { a.Engine.Tick(new(LeftX: 19660), .01); b.Engine.Tick(new(ControllerButtons.LB, LeftX: 19660), .01); }
            Check.Near(a.Sink.X / 2.0, b.Sink.X, 1);
        });
        suite.Add("Precision actions work on both triggers with hysteresis", () =>
        {
            var h = Harness.New(With((Control.LT, InputAction.PrecisionHold), (Control.RT, InputAction.PrecisionToggle)));
            h.Engine.Tick(new(LeftTrigger: 150), .01); h.Engine.Tick(new(LeftTrigger: 110), .01); Check.True(h.Engine.PrecisionActive);
            h.Engine.Tick(new(LeftTrigger: 100), .01); Check.True(!h.Engine.PrecisionActive);
            h.Engine.Tick(new(RightTrigger: 150), .01); h.Engine.Tick(default, .01); Check.True(h.Engine.PrecisionActive);
        });
        suite.Add("D-pad actions keep current owner while another direction is pressed", () =>
        {
            var h = Harness.New(With((Control.DPadUp, InputAction.LeftClick), (Control.DPadRight, InputAction.RightClick)) with { DPadMode = DPadMode.AssignedActions });
            h.Engine.Tick(new(Up), .01); h.Engine.Tick(new(Up | Right), .01); Check.True(h.Sink.Held[0] && !h.Sink.Held[1]);
            h.Engine.Tick(new(Right), .01); Check.True(!h.Sink.Held[0] && h.Sink.Held[1]);
            h.Engine.Tick(default, .01); Check.True(!h.Sink.Held.Any(x => x));
        });
        suite.Add("Ambiguous simultaneous D-pad actions wait for one direction", () =>
        {
            var h = Harness.New(With((Control.DPadUp, InputAction.TaskView), (Control.DPadDown, InputAction.CloseWindow)) with { DPadMode = DPadMode.AssignedActions });
            h.Engine.Tick(new(Up | Down), .01); Check.Equal(0, h.Sink.Actions.Count);
            h.Engine.Tick(new(Down), .01); h.Engine.Tick(new(Down), .01); Check.Equal(InputAction.CloseWindow, h.Sink.Actions.Single());
        });
        suite.Add("Action-mode D-pad does not move the pointer or suppress cursor stick", () =>
        {
            var h = Harness.New(new AppSettings { DPadMode = DPadMode.AssignedActions, CursorSpeed = 1000 });
            h.Engine.Tick(new(Right, LeftX: -32768), .01); Check.Equal(-10, h.Sink.X);
        });
        suite.Add("D-pad precision actions combine with physical button owners", () =>
        {
            var h = Harness.New(With((Control.DPadUp, InputAction.PrecisionHold), (Control.LB, InputAction.PrecisionHold)) with { DPadMode = DPadMode.AssignedActions });
            h.Engine.Tick(new(Up | ControllerButtons.LB), .01); h.Engine.Tick(new(Up), .01); Check.True(h.Engine.PrecisionActive);
        });
        suite.Add("Switching D-pad modes releases input and waits for full neutral", () =>
        {
            var settings = With((Control.DPadRight, InputAction.LeftClick)) with { DPadMode = DPadMode.AssignedActions };
            var h = Harness.New(settings); h.Engine.Tick(new(Right), .01); Check.True(h.Sink.Held[0]);
            h.Engine.Configure(settings with { DPadMode = DPadMode.CursorMovement }); h.Engine.Tick(new(Right), .01);
            Check.True(!h.Sink.Held[0]); Check.Equal(0, h.Sink.X);
            h.Engine.Tick(default, .01); h.Engine.Tick(new(Right), 0); Check.Equal(5, h.Sink.X);
            h.Engine.Configure(settings); h.Engine.Tick(default, .01); h.Engine.Tick(new(Right), .01); Check.True(h.Sink.Held[0]);
        });
        suite.Add("Drag lock latches without repeating down and toggles off on fresh press", () =>
        {
            var h = Harness.New(With((Control.LB, InputAction.DragLock)));
            h.Engine.Tick(new(ControllerButtons.LB), .01); h.Engine.Tick(new(ControllerButtons.LB, LeftX: 32767), .01); h.Engine.Tick(default, .01);
            Check.True(h.Engine.DragLocked && h.Sink.Held[0]); Check.Equal(1, h.Sink.ButtonEvents.Count); Check.True(h.Sink.X > 0);
            h.Engine.Tick(new(ControllerButtons.LB), .01); Check.True(!h.Engine.DragLocked && !h.Sink.Held[0]);
        });
        suite.Add("Ordinary mouse hold survives drag-lock owner release", () =>
        {
            var h = Harness.New(With((Control.LB, InputAction.DragLock)));
            h.Engine.Tick(new(ControllerButtons.LB), .01); h.Engine.Tick(new(ControllerButtons.A), .01);
            h.Engine.Tick(new(ControllerButtons.A | ControllerButtons.LB), .01); Check.True(!h.Engine.DragLocked && h.Sink.Held[0]); Check.Equal(1, h.Sink.ButtonEvents.Count);
            h.Engine.Tick(default, .01); Check.Equal(2, h.Sink.ButtonEvents.Count);
        });
        suite.Add("Drag lock survives ordinary hold release and duplicate bindings", () =>
        {
            var h = Harness.New(With((Control.LB, InputAction.DragLock), (Control.RB, InputAction.LeftClick)));
            h.Engine.Tick(new(ControllerButtons.A | ControllerButtons.RB | ControllerButtons.LB), .01); h.Engine.Tick(default, .01);
            Check.True(h.Sink.Held[0]); Check.Equal(1, h.Sink.ButtonEvents.Count);
        });
        suite.Add("Double-click emits ordered down/up pairs and holds do not repeat", () =>
        {
            var h = Harness.New(With((Control.LB, InputAction.DoubleClick)));
            h.Engine.Tick(new(ControllerButtons.LB), .01); Check.Equal(2, h.Sink.ButtonEvents.Count); Check.True(h.Engine.PendingDoubleClick);
            for (var i = 0; i < 10; i++) h.Engine.Tick(new(ControllerButtons.LB), .01);
            Check.True(h.Sink.ButtonEvents.SequenceEqual(new[] { (MouseButton.Left, true), (MouseButton.Left, false), (MouseButton.Left, true), (MouseButton.Left, false) }));
            Check.True(!h.Engine.PendingDoubleClick && !h.Sink.Held[0]);
        });
        suite.Add("Double-click respects injected interval without blocking the input loop", () =>
        {
            var sink = new FakeSink(); var engine = new InputEngine(sink, .01); engine.Configure(With((Control.LB, InputAction.DoubleClick))); engine.Tick(default, 0);
            engine.Tick(new(ControllerButtons.LB), 0); engine.Tick(default, .005); Check.Equal(2, sink.ButtonEvents.Count);
            engine.Tick(default, .005); Check.Equal(4, sink.ButtonEvents.Count);
        });
        suite.Add("Controller cursor is kept stationary until second click is emitted", () =>
        {
            var h = Harness.New(With((Control.LB, InputAction.DoubleClick)));
            h.Engine.Tick(new(ControllerButtons.LB, LeftX: 32767), .01); h.Engine.Tick(new(LeftX: 32767), .01);
            Check.Equal(0, h.Sink.X); h.Engine.Tick(new(LeftX: 32767), .032); Check.True(h.Sink.X > 0);
        });
        suite.Add("Double-click is suppressed by an existing synthetic hold or drag lock", () =>
        {
            foreach (var owner in new[] { InputAction.LeftClick, InputAction.DragLock })
            {
                var h = Harness.New(With((Control.LB, InputAction.DoubleClick), (Control.RB, owner)));
                h.Engine.Tick(new(ControllerButtons.RB), .01); h.Engine.Tick(new(ControllerButtons.RB | ControllerButtons.LB), .01);
                Check.Equal(1, h.Sink.ButtonEvents.Count); Check.True(!h.Engine.PendingDoubleClick);
            }
        });
        suite.Add("Simultaneous ordinary hold wins over double-click", () =>
        {
            var h = Harness.New(With((Control.LB, InputAction.DoubleClick)));
            h.Engine.Tick(new(ControllerButtons.A | ControllerButtons.LB), .01); Check.Equal(1, h.Sink.ButtonEvents.Count); Check.True(h.Sink.Held[0]);
        });
        suite.Add("New left hold cancels pending second click without releasing its owner", () =>
        {
            var h = Harness.New(With((Control.LB, InputAction.DoubleClick)));
            h.Engine.Tick(new(ControllerButtons.LB), .01); h.Engine.Tick(new(ControllerButtons.A), .01);
            for (var i = 0; i < 10; i++) h.Engine.Tick(new(ControllerButtons.A), .01);
            Check.Equal(3, h.Sink.ButtonEvents.Count); Check.True(h.Sink.Held[0] && !h.Engine.PendingDoubleClick);
        });
        suite.Add("Pause disconnect and cancel abort pending double-click", () =>
        {
            foreach (var cancel in new Action<InputEngine>[] { e => e.SetPaused(true), e => e.Disconnect(), e => e.Reset() })
            {
                var h = Harness.New(With((Control.LB, InputAction.DoubleClick))); h.Engine.Tick(new(ControllerButtons.LB), .01); cancel(h.Engine);
                for (var i = 0; i < 10; i++) h.Engine.Tick(default, .01);
                Check.Equal(2, h.Sink.ButtonEvents.Count); Check.True(!h.Engine.PendingDoubleClick);
            }
        });
        suite.Add("Release/cancel clears all owners, keys and modes and requires neutral", () =>
        {
            var h = Harness.New(With((Control.LB, InputAction.DragLock), (Control.RB, InputAction.PrecisionToggle), (Control.L3, InputAction.ReleaseCancel)));
            h.Engine.Tick(new(ControllerButtons.LB | ControllerButtons.RB | ControllerButtons.X), .01); h.Sink.KeyHeld = true;
            h.Engine.Tick(new(ControllerButtons.LeftThumb | ControllerButtons.X, LeftX: 32767), .01);
            Check.True(!h.Sink.Held.Any(x => x) && !h.Sink.KeyHeld && !h.Engine.PrecisionActive && !h.Engine.DragLocked);
            h.Engine.Tick(new(ControllerButtons.X, LeftX: 32767), .01); Check.Equal(0, h.Sink.X); Check.True(!h.Sink.Held[1]);
            h.Engine.Tick(default, .01); h.Engine.Tick(new(ControllerButtons.A, LeftX: 32767), .01); Check.True(h.Sink.Held[0] && h.Sink.X > 0);
        });
        suite.Add("Release/cancel has priority over same-frame actions", () =>
        {
            var h = Harness.New(With((Control.L3, InputAction.ReleaseCancel), (Control.LB, InputAction.DragLock), (Control.RB, InputAction.DoubleClick)));
            h.Engine.Tick(new(ControllerButtons.LeftThumb | ControllerButtons.B | ControllerButtons.LB | ControllerButtons.RB), .01);
            Check.Equal(0, h.Sink.Actions.Count); Check.Equal(0, h.Sink.ButtonEvents.Count); Check.True(!h.Engine.DragLocked && !h.Engine.PendingDoubleClick);
        });
        suite.Add("Reserved pause chord cannot trigger precision drag or window actions", () =>
        {
            var h = Harness.New(With((Control.LB, InputAction.DragLock), (Control.RB, InputAction.PrecisionToggle)));
            h.Engine.Tick(new(ControllerButtons.Start | ControllerButtons.Back | ControllerButtons.LB | ControllerButtons.RB | ControllerButtons.B), .01);
            Check.True(h.Engine.Paused && !h.Engine.PrecisionActive && !h.Engine.DragLocked); Check.Equal(0, h.Sink.Actions.Count);
        });
        suite.Add("All lifecycle cleanup paths clear transient modes", () =>
        {
            foreach (var reset in new Action<InputEngine>[] { e => e.Reset(), e => e.Disconnect(), e => e.SetPaused(true), e => e.Configure(new AppSettings { ControllerSlot = 3 }) })
            {
                var h = Harness.New(With((Control.LB, InputAction.DragLock), (Control.RB, InputAction.PrecisionToggle)));
                h.Engine.Tick(new(ControllerButtons.LB | ControllerButtons.RB), .01); reset(h.Engine);
                Check.True(!h.Engine.DragLocked && !h.Engine.PrecisionActive && !h.Sink.Held[0]);
            }
        });
        suite.Add("Mapping edits clear latch and never reassign a currently held control", () =>
        {
            var h = Harness.New(With((Control.LB, InputAction.DragLock))); h.Engine.Tick(new(ControllerButtons.LB), .01);
            h.Engine.Configure(With((Control.LB, InputAction.PrecisionToggle))); h.Engine.Tick(new(ControllerButtons.LB), .01);
            Check.True(!h.Engine.DragLocked && !h.Engine.PrecisionActive && !h.Sink.Held[0]);
        });
        suite.Add("Version-one migration preserves speeds and bindings with one-pixel taps", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path);
            const string old = "{\"Version\":1,\"CursorSpeed\":1777,\"PrecisionSpeed\":345,\"ScrollSpeed\":3.5,\"LeftDeadZone\":0.3,\"ResponseCurve\":2.2,\"Mappings\":{\"A\":\"RightClick\",\"RT\":\"SnapLeft\"}}";
            File.WriteAllText(store.FilePath, old); var load = store.Load();
            Check.True(load.Warning is null); Check.Equal(AppSettings.CurrentVersion, load.Settings.Version); Check.Near(1, load.Settings.DPadTapDistance);
            Check.Near(1777, load.Settings.CursorSpeed); Check.Near(345, load.Settings.PrecisionSpeed); Check.Near(3.5, load.Settings.ScrollSpeed);
            Check.Near(.3, load.Settings.LeftDeadZone); Check.Near(2.2, load.Settings.ResponseCurve);
            Check.Equal(InputAction.RightClick, load.Settings.Mappings[Control.A]); Check.Equal(InputAction.SnapLeft, load.Settings.Mappings[Control.RT]);
            Check.Equal(InputAction.None, load.Settings.Mappings[Control.L3]); Check.Equal(InputAction.None, load.Settings.Mappings[Control.R3]);
            Check.Equal(old, File.ReadAllText(store.FilePath + ".v1-backup")); store.Save(load.Settings); Check.Near(1, store.Load().Settings.DPadTapDistance);
        });
        suite.Add("Unversioned existing files also migrate with one-pixel taps", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path); File.WriteAllText(store.FilePath, "{}");
            Check.Near(1, store.Load().Settings.DPadTapDistance);
        });
        suite.Add("New settings and reset defaults use five-pixel taps", () =>
        {
            using var temp = new TestDirectory(); Check.Near(5, new SettingsStore(temp.Path).Load().Settings.DPadTapDistance);
            Check.Near(5, new AppSettings().Validated().DPadTapDistance);
        });
        suite.Add("Stable persisted action IDs round-trip independently of catalog order", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path);
            var settings = With((Control.A, InputAction.DoubleClick), (Control.L3, InputAction.PrecisionHold), (Control.DPadUp, InputAction.DragLock));
            Check.True(store.Save(settings) is null); using var json = JsonDocument.Parse(File.ReadAllText(store.FilePath));
            Check.Equal("DoubleClick", json.RootElement.GetProperty("Mappings").GetProperty("A").GetString());
            Check.Equal("PrecisionHold", json.RootElement.GetProperty("Mappings").GetProperty("L3").GetString());
            Check.Equal(InputAction.DragLock, store.Load().Settings.Mappings[Control.DPadUp]);
            Check.Equal("LeftClick", ActionCatalog.Get(InputAction.LeftClick).Id); Check.Equal(1, (int)InputAction.LeftClick); Check.Equal(8, (int)InputAction.SnapRight);
            Check.Equal(ActionCatalog.All.Count, ActionCatalog.All.Select(a => a.Id).Distinct().Count());
            Check.True(ActionCatalog.All.All(a => !string.IsNullOrEmpty(a.Category)));
            Check.Equal(ActivationCapabilities.Held, ActionCatalog.Get(InputAction.PrecisionHold).Capabilities);
            Check.Equal(ActivationCapabilities.Toggle, ActionCatalog.Get(InputAction.DragLock).Capabilities);
        });
        suite.Add("Legacy numeric action IDs retain their meaning", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path); File.WriteAllText(store.FilePath, "{\"Version\":1,\"Mappings\":{\"A\":3,\"B\":8}}");
            var settings = store.Load().Settings; Check.Equal(InputAction.MiddleClick, settings.Mappings[Control.A]); Check.Equal(InputAction.SnapRight, settings.Mappings[Control.B]);
        });
        suite.Add("Modes and new configuration persist without erasing alternate D-pad bindings", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path);
            var settings = With((Control.DPadDown, InputAction.DoubleClick)) with { DPadMode = DPadMode.AssignedActions, CursorStick = CursorStick.Right, PrecisionPercent = 25, DPadTapDistance = 18, ShowStateIndicator = false };
            store.Save(settings); var load = store.Load().Settings; Check.Equal(CursorStick.Right, load.CursorStick); Check.Near(25, load.PrecisionPercent); Check.True(!load.ShowStateIndicator);
            store.Save(load with { DPadMode = DPadMode.CursorMovement }); load = store.Load().Settings;
            Check.Equal(InputAction.DoubleClick, load.Mappings[Control.DPadDown]); Check.Near(18, load.DPadTapDistance);
        });
        suite.Add("Invalid and nonfinite foundation settings load safe ranges", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path);
            File.WriteAllText(store.FilePath, "{\"Version\":2,\"DPadTapDistance\":\"NaN\",\"PrecisionPercent\":\"Infinity\",\"CursorStick\":999,\"DPadMode\":999}");
            var settings = store.Load().Settings; Check.Near(5, settings.DPadTapDistance); Check.Near(50, settings.PrecisionPercent);
            Check.Equal(CursorStick.Left, settings.CursorStick); Check.Equal(DPadMode.CursorMovement, settings.DPadMode);
            settings = (settings with { DPadTapDistance = 500, PrecisionPercent = -4 }).Validated(); Check.Near(50, settings.DPadTapDistance); Check.Near(5, settings.PrecisionPercent);
        });
        suite.Add("Malformed versions recover; future unknown actions do not overwrite files", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path); File.WriteAllText(store.FilePath, "{\"Version\":\"bad\"}"); Check.True(store.Load().Warning is not null);
            using var future = new TestDirectory(); store = new SettingsStore(future.Path); const string text = "{\"Version\":99,\"Mappings\":{\"A\":\"FutureAction\"}}";
            File.WriteAllText(store.FilePath, text); Check.True(store.Load().Warning is not null); Check.True(store.Save(new()) is not null); Check.Equal(text, File.ReadAllText(store.FilePath));
        });
        suite.Add("Transient drag precision and pending state are never serialized or restored", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path); var settings = With((Control.LB, InputAction.DragLock), (Control.RB, InputAction.PrecisionToggle));
            var h = Harness.New(settings); h.Engine.Tick(new(ControllerButtons.LB | ControllerButtons.RB), .01); store.Save(settings);
            using var json = JsonDocument.Parse(File.ReadAllText(store.FilePath));
            Check.True(!json.RootElement.TryGetProperty("DragLocked", out _) && !json.RootElement.TryGetProperty("PrecisionActive", out _) && !json.RootElement.TryGetProperty("PendingDoubleClick", out _));
            var fresh = Harness.New(store.Load().Settings); Check.True(!fresh.Engine.DragLocked && !fresh.Engine.PrecisionActive && !fresh.Sink.Held[0]);
        });
        suite.Add("Worker publishes active modes and honors UI release without reactivating held controls", () =>
        {
            var source = new FakeSource(); var sink = new FakeSink(); source.Set(0, default);
            var service = new ControllerService(With((Control.LB, InputAction.DragLock), (Control.RB, InputAction.PrecisionToggle)), source, sink);
            service.Start();
            try
            {
                Check.Eventually(() => service.Status.ActiveSlot == 0 && !service.Status.AwaitingNeutral);
                source.Set(0, new(ControllerButtons.LB | ControllerButtons.RB));
                Check.Eventually(() => service.Status.DragLocked && service.Status.PrecisionActive);
                service.ResetInput();
                Check.Eventually(() => !service.Status.DragLocked && !service.Status.PrecisionActive && service.Status.AwaitingNeutral);
                Check.True(!sink.Held[0]); source.Set(0, default); Check.Eventually(() => !service.Status.AwaitingNeutral);
                source.Set(0, new(ControllerButtons.LB | ControllerButtons.RB)); Check.Eventually(() => service.Status.DragLocked);
            }
            finally { service.Dispose(); }
            Check.True(!sink.Held[0] && !service.Status.DragLocked && !service.Status.PrecisionActive);
        });
    }
    private static AppSettings With(params (Control Control, InputAction Action)[] bindings)
    {
        var settings = new AppSettings(); foreach (var (control, action) in bindings) settings.Mappings[control] = action; return settings;
    }
}
