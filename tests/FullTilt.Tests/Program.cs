using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using FullTilt.Core;
using FullTilt.Windows;

// Dependency-free executable test runner. Every sink and controller in these tests is a fake.
var suite = new TestSuite();
suite.Add("Signed stick extremes normalize symmetrically", () =>
{
    Check.Near(-1, MotionMath.Normalize(short.MinValue)); Check.Near(1, MotionMath.Normalize(short.MaxValue));
});
suite.Add("Radial dead zone suppresses drift and handles boundary", () =>
{
    Check.Near(0, MotionMath.Radial(3000, 3000, .2, 1).Length);
    Check.Near(0, MotionMath.Radial(0, 0, 0, 1).Length);
    Check.Near(0, MotionMath.Radial(6553, 0, .2, 1).Length);
});
suite.Add("Dead zone output is renormalized", () =>
{
    var value = MotionMath.Radial(19660, 0, .2, 1);
    Check.Near(.5, value.X, .0001); Check.Near(0, value.Y);
});
suite.Add("Response curve softens half deflection and retains full speed", () =>
{
    Check.Near(.25, MotionMath.Radial(19660, 0, .2, 2).X, .0001);
    Check.Near(1, MotionMath.Radial(32767, 0, .2, 3).X);
});
suite.Add("Overrange stick diagonals are clamped and direction preserved", () =>
{
    var value = MotionMath.Radial(-32768, 32767, .18, 1.8);
    Check.Near(1, value.Length); Check.Near(-value.X, value.Y);
});
suite.Add("D-pad continuous diagonal is unit length", () =>
{
    var value = MotionMath.DPad(ControllerButtons.DPadRight | ControllerButtons.DPadDown);
    Check.Near(1, value.Length); Check.Near(Math.Sqrt(.5), value.X);
    Check.Near(0, MotionMath.DPad(ControllerButtons.DPadLeft | ControllerButtons.DPadRight).Length);
});
suite.Add("Fractional motion accumulates positive and negative pixels", () =>
{
    var fraction = new FractionalMotion(); int x = 0, y = 0;
    for (var i = 0; i < 10; i++) { var take = fraction.Take(.25, -.25); x += take.X; y += take.Y; }
    Check.Equal(2, x); Check.Equal(-2, y);
    Check.Equal((1, -1), fraction.Take(.5, -.5));
});
suite.Add("Elapsed time is capped and invalid elapsed is rejected", () =>
{
    Check.Near(.032, MotionMath.Elapsed(5)); Check.Near(0, MotionMath.Elapsed(-1));
    Check.Near(0, MotionMath.Elapsed(double.NaN)); Check.Near(0, MotionMath.Elapsed(double.PositiveInfinity));
});
suite.Add("Cursor speed is time based across polling intervals", () =>
{
    var a = Harness.New(new AppSettings { CursorSpeed = 1000 });
    var b = Harness.New(new AppSettings { CursorSpeed = 1000 });
    for (var i = 0; i < 100; i++) a.Engine.Tick(new(LeftX: 32767), .01);
    for (var i = 0; i < 200; i++) b.Engine.Tick(new(LeftX: 32767), .005);
    Check.Equal(1000, a.Sink.X); Check.Equal(a.Sink.X, b.Sink.X);
});
suite.Add("Cursor stall never jumps by unbounded elapsed", () =>
{
    var h = Harness.New(new AppSettings { CursorSpeed = 1000 });
    h.Engine.Tick(new(LeftX: 32767), 30); Check.Equal(32, h.Sink.X);
});
suite.Add("Low cursor velocity preserves fractions between ticks", () =>
{
    var h = Harness.New(new AppSettings { CursorSpeed = 50, LeftDeadZone = .05, ResponseCurve = 1 });
    for (var i = 0; i < 100; i++) h.Engine.Tick(new(LeftX: 32767), .005);
    Check.Equal(25, h.Sink.X);
});
suite.Add("D-pad takes priority over opposite stick movement", () =>
{
    var h = Harness.New();
    for (var i = 0; i < 30; i++) h.Engine.Tick(new(ControllerButtons.DPadRight, LeftX: -32768), .01);
    Check.True(h.Sink.X > 0); Check.Equal(0, h.Sink.Y);
});
suite.Add("Opposing D-pad buttons suppress stick instead of leaking movement", () =>
{
    var h = Harness.New();
    h.Engine.Tick(new(ControllerButtons.DPadRight | ControllerButtons.DPadLeft, LeftX: 32767), .01);
    Check.Equal(0, h.Sink.X);
});
suite.Add("D-pad cardinal and diagonal taps are dependable", () =>
{
    var h = Harness.New(new AppSettings { DPadTapDistance = 1 });
    h.Engine.Tick(new(ControllerButtons.DPadRight), .008); Check.Equal(1, h.Sink.X);
    h.Engine.Tick(default, .008);
    h.Engine.Tick(new(ControllerButtons.DPadRight | ControllerButtons.DPadUp), .008);
    Check.Equal(2, h.Sink.X); Check.Equal(-1, h.Sink.Y);
});
suite.Add("D-pad initial nudge continues immediately at held speed", () =>
{
    var h = Harness.New(new AppSettings { PrecisionSpeed = 100 });
    for (var i = 0; i < 10; i++) h.Engine.Tick(new(ControllerButtons.DPadRight), .01);
    Check.Equal(14, h.Sink.X);
    for (var i = 0; i < 30; i++) h.Engine.Tick(new(ControllerButtons.DPadRight), .01);
    Check.Equal(44, h.Sink.X);
});
suite.Add("Continuous D-pad diagonals match cardinal distance", () =>
{
    var a = Harness.New(new AppSettings { PrecisionSpeed = 100 }); var b = Harness.New(new AppSettings { PrecisionSpeed = 100 });
    for (var i = 0; i < 100; i++)
    {
        a.Engine.Tick(new(ControllerButtons.DPadRight), .01);
        b.Engine.Tick(new(ControllerButtons.DPadRight | ControllerButtons.DPadDown), .01);
    }
    Check.Near(a.Sink.X, Math.Sqrt(b.Sink.X * b.Sink.X + b.Sink.Y * b.Sink.Y), 2);
});
suite.Add("Scroll drift in X cannot leak from a full Y deflection", () =>
{
    var h = Harness.New();
    for (var i = 0; i < 100; i++) h.Engine.Tick(new(RightX: 5000, RightY: 32767), .01);
    Check.Equal(0, h.Sink.ScrollX); Check.True(h.Sink.ScrollY > 0);
});
suite.Add("Scroll axes operate independently in both signs", () =>
{
    var h = Harness.New(new AppSettings { ScrollSpeed = 10 });
    for (var i = 0; i < 100; i++) h.Engine.Tick(new(RightX: -32768, RightY: 32767), .01);
    Check.Equal(-1200, h.Sink.ScrollX); Check.Equal(1200, h.Sink.ScrollY);
});
suite.Add("Mouse buttons send one down and one up while supporting drag", () =>
{
    var h = Harness.New();
    for (var i = 0; i < 20; i++) h.Engine.Tick(new(ControllerButtons.A, LeftX: 32767), .01);
    Check.Equal(1, h.Sink.ButtonEvents.Count); Check.True(h.Sink.Held[0]); Check.True(h.Sink.X > 0);
    h.Engine.Tick(default, .01);
    Check.Equal((MouseButton.Left, false), h.Sink.ButtonEvents.Last()); Check.Equal(2, h.Sink.ButtonEvents.Count);
});
suite.Add("X and Y hold right and middle buttons", () =>
{
    var h = Harness.New(); h.Engine.Tick(new(ControllerButtons.X | ControllerButtons.Y), .01);
    Check.True(h.Sink.Held[1] && h.Sink.Held[2]); h.Engine.Tick(default, .01);
    Check.True(!h.Sink.Held.Any(x => x));
});
suite.Add("Window action fires once per press", () =>
{
    var h = Harness.New(); for (var i = 0; i < 10; i++) h.Engine.Tick(new(ControllerButtons.B), .01);
    Check.Equal(1, h.Sink.Actions.Count); Check.Equal(InputAction.TaskView, h.Sink.Actions.Single());
    h.Engine.Tick(default, .01); h.Engine.Tick(new(ControllerButtons.B), .01); Check.Equal(2, h.Sink.Actions.Count);
});
suite.Add("All window action mappings execute as edge actions", () =>
{
    foreach (var action in new[] { InputAction.MinimizeWindow, InputAction.CloseWindow, InputAction.SnapLeft, InputAction.SnapRight })
    {
        var settings = new AppSettings(); settings.Mappings[Control.RB] = action; var h = Harness.New(settings);
        h.Engine.Tick(new(ControllerButtons.RB), .01); h.Engine.Tick(new(ControllerButtons.RB), .01);
        Check.Equal(action, h.Sink.Actions.Single());
    }
});
suite.Add("Trigger hysteresis prevents threshold chatter", () =>
{
    var settings = new AppSettings(); settings.Mappings[Control.LT] = InputAction.LeftClick; var h = Harness.New(settings);
    h.Engine.Tick(new(LeftTrigger: 139), .01); Check.Equal(0, h.Sink.ButtonEvents.Count);
    h.Engine.Tick(new(LeftTrigger: 140), .01); h.Engine.Tick(new(LeftTrigger: 120), .01); h.Engine.Tick(new(LeftTrigger: 101), .01);
    Check.Equal(1, h.Sink.ButtonEvents.Count); Check.True(h.Sink.Held[0]);
    h.Engine.Tick(new(LeftTrigger: 100), .01); Check.Equal(2, h.Sink.ButtonEvents.Count);
});
suite.Add("Duplicate mouse mappings release only after final owner", () =>
{
    var settings = new AppSettings(); settings.Mappings[Control.RB] = InputAction.LeftClick; var h = Harness.New(settings);
    h.Engine.Tick(new(ControllerButtons.A | ControllerButtons.RB), .01);
    h.Engine.Tick(new(ControllerButtons.RB), .01); Check.Equal(1, h.Sink.ButtonEvents.Count);
    h.Engine.Tick(default, .01); Check.Equal(2, h.Sink.ButtonEvents.Count);
});
suite.Add("Simultaneous duplicate-binding handoff does not release", () =>
{
    var settings = new AppSettings(); settings.Mappings[Control.LB] = InputAction.LeftClick; var h = Harness.New(settings);
    h.Engine.Tick(new(ControllerButtons.A), .01); h.Engine.Tick(new(ControllerButtons.LB), .01);
    Check.Equal(1, h.Sink.ButtonEvents.Count); Check.True(h.Sink.Held[0]);
});
suite.Add("Pause releases all holds and stops movement", () =>
{
    var h = Harness.New(); h.Engine.Tick(new(ControllerButtons.A | ControllerButtons.X | ControllerButtons.Y), .01);
    h.Engine.SetPaused(true); Check.True(!h.Sink.Held.Any(x => x));
    h.Engine.Tick(new(ControllerButtons.A, LeftX: 32767, RightY: 32767), .01);
    Check.Equal(0, h.Sink.X); Check.Equal(0, h.Sink.ScrollY);
});
suite.Add("Resume requires buttons, triggers and sticks to neutral independently", () =>
{
    var settings = new AppSettings(); settings.Mappings[Control.RT] = InputAction.RightClick; var h = Harness.New(settings);
    h.Engine.SetPaused(true); h.Engine.SetPaused(false);
    h.Engine.Tick(new(ControllerButtons.A | ControllerButtons.DPadRight, LeftX: 32767, RightY: 32767, RightTrigger: 255), .01);
    Check.Equal(0, h.Sink.X); Check.Equal(0, h.Sink.ScrollY); Check.True(!h.Sink.Held.Any(x => x)); Check.True(h.Engine.AwaitingNeutral);
    h.Engine.Tick(default, .01); Check.True(!h.Engine.AwaitingNeutral);
    h.Engine.Tick(new(ControllerButtons.A, LeftX: 32767, RightTrigger: 255), .01);
    Check.True(h.Sink.Held[0] && h.Sink.Held[1]); Check.True(h.Sink.X > 0);
});
suite.Add("Disconnect releases held input and reconnect held buttons are suppressed", () =>
{
    var h = Harness.New(); h.Engine.Tick(new(ControllerButtons.A), .01); h.Engine.Disconnect();
    Check.True(!h.Sink.Held[0]); h.Engine.Tick(new(ControllerButtons.A), .01); Check.True(!h.Sink.Held[0]);
    h.Engine.Tick(default, .01); h.Engine.Tick(new(ControllerButtons.A), .01); Check.True(h.Sink.Held[0]);
});
suite.Add("Start+Back toggles once and requires full combo release", () =>
{
    var h = Harness.New(); var combo = new ControllerState(ControllerButtons.Start | ControllerButtons.Back);
    for (var i = 0; i < 10; i++) h.Engine.Tick(combo, .01); Check.True(h.Engine.Paused);
    h.Engine.Tick(new(ControllerButtons.Start), .01); h.Engine.Tick(combo, .01); Check.True(h.Engine.Paused);
    h.Engine.Tick(default, .01); h.Engine.Tick(combo, .01); Check.True(!h.Engine.Paused);
});
suite.Add("Held pause combo on reconnect does not toggle until released", () =>
{
    var h = Harness.New(); h.Engine.Disconnect(); h.Engine.Tick(new(ControllerButtons.Start | ControllerButtons.Back), .01);
    Check.True(!h.Engine.Paused);
});
suite.Add("Only the full pause shortcut clears an existing drag", () =>
{
    var h = Harness.New(); h.Engine.Tick(new(ControllerButtons.A), .01); h.Engine.Tick(new(ControllerButtons.A | ControllerButtons.Start), .01);
    Check.True(h.Sink.Held[0]);
    h.Engine.Tick(new(ControllerButtons.A | ControllerButtons.Start | ControllerButtons.Back), .01);
    Check.True(!h.Sink.Held[0]); Check.True(h.Engine.Paused);
});
suite.Add("Mapping changes release input and require rearming", () =>
{
    var h = Harness.New(); h.Engine.Tick(new(ControllerButtons.A), .01);
    var settings = new AppSettings(); settings.Mappings[Control.A] = InputAction.RightClick; h.Engine.Configure(settings);
    Check.True(!h.Sink.Held.Any(x => x)); h.Engine.Tick(new(ControllerButtons.A), .01); Check.True(!h.Sink.Held.Any(x => x));
    h.Engine.Tick(default, .01); h.Engine.Tick(new(ControllerButtons.A), .01); Check.True(h.Sink.Held[1]);
});
suite.Add("Speed changes do not break a settings slider drag", () =>
{
    var h = Harness.New(); h.Engine.Tick(new(ControllerButtons.A), .01); h.Engine.Configure(new AppSettings { CursorSpeed = 2000 });
    Check.True(h.Sink.Held[0]); h.Engine.Tick(new(ControllerButtons.A, LeftX: 32767), .01); Check.Equal(20, h.Sink.X);
});
suite.Add("Controller preference changes clear holds", () =>
{
    var h = Harness.New(); h.Engine.Tick(new(ControllerButtons.A), .01); h.Engine.Configure(new AppSettings { ControllerSlot = 2 });
    Check.True(!h.Sink.Held[0]);
});
suite.Add("Settings validation clamps unsafe values and restores missing bindings", () =>
{
    var settings = new AppSettings { CursorSpeed = double.NaN, PrecisionSpeed = -1, ScrollSpeed = 999, LeftDeadZone = 1, RightDeadZone = -1, ResponseCurve = 0, ControllerSlot = 40, TriggerPressThreshold = 0, TriggerReleaseThreshold = 255, Mappings = new() { [Control.A] = (InputAction)int.MaxValue } }.Validated();
    Check.Near(1200, settings.CursorSpeed); Check.Near(10, settings.PrecisionSpeed); Check.Near(30, settings.ScrollSpeed);
    Check.Near(.45, settings.LeftDeadZone); Check.Near(.05, settings.RightDeadZone); Check.Near(1, settings.ResponseCurve);
    Check.Equal(3, settings.ControllerSlot); Check.True(settings.TriggerReleaseThreshold < settings.TriggerPressThreshold);
    Check.Equal(InputAction.LeftClick, settings.Mappings[Control.A]); Check.Equal(14, settings.Mappings.Count);
});
suite.Add("Defaults use 2:1 normal to precision speed", () => Check.Near(2, new AppSettings().CursorSpeed / new AppSettings().PrecisionSpeed));
suite.Add("Atomic settings save round-trips and leaves no temporary files", () =>
{
    using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path);
    Check.True(store.Save(new AppSettings { CursorSpeed = 900 }) is null);
    Check.True(store.Save(new AppSettings { CursorSpeed = 1700 }) is null);
    Check.Near(1700, store.Load().Settings.CursorSpeed); Check.Equal(1, Directory.GetFiles(temp.Path).Length);
});
suite.Add("Malformed JSON is preserved byte for byte then safe defaults load", () =>
{
    using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path); const string broken = "{ invalid: settings";
    File.WriteAllText(store.FilePath, broken); var load = store.Load();
    Check.True(load.Warning is not null); Check.Near(1200, load.Settings.CursorSpeed);
    Check.Equal(broken, File.ReadAllText(Directory.GetFiles(temp.Path, "*.corrupt-*").Single()));
    Check.True(store.Save(load.Settings) is null); Check.Near(1200, store.Load().Settings.CursorSpeed);
});
suite.Add("Invalid JSON types and action names recover without crashing", () =>
{
    foreach (var json in new[] { "null", "[]", "{\"CursorSpeed\":\"fast\"}", "{\"Mappings\":{\"A\":\"DeleteEverything\"}}", "{\"Version\":0}" })
    {
        using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path); File.WriteAllText(store.FilePath, json);
        Check.True(store.Load().Warning is not null); Check.Equal(1, Directory.GetFiles(temp.Path, "*.corrupt-*").Length);
    }
});
suite.Add("Null mapping collection receives safe defaults", () =>
{
    using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path); File.WriteAllText(store.FilePath, "{\"Mappings\":null}");
    Check.Equal(14, store.Load().Settings.Mappings.Count);
});
suite.Add("Newer settings versions are preserved without downgrade", () =>
{
    using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path); const string future = "{\"Version\":999,\"CursorSpeed\":888}";
    File.WriteAllText(store.FilePath, future); Check.True(store.Load().Warning is not null); Check.True(store.Save(new()) is not null);
    Check.Equal(future, File.ReadAllText(store.FilePath));
});
suite.Add("Unwritable settings path fails gracefully without losing session values", () =>
{
    using var temp = new TestDirectory(); var blocker = Path.Combine(temp.Path, "file"); File.WriteAllText(blocker, "keep");
    var store = new SettingsStore(Path.Combine(blocker, "settings"));
    Check.True(store.Save(new()) is not null); Check.Equal("keep", File.ReadAllText(blocker));
});
suite.Add("Profiles always use the executable Profiles folder without a portable marker", () =>
{
    using var temp = new TestDirectory();
    var local = Path.Combine(temp.Path, "local");
    var opened = SettingsStore.OpenDefault(temp.Path, local);
    Check.True(opened.Warning is null);
    Check.Equal(Path.Combine(temp.Path, "Profiles"), opened.Store.DirectoryPath);
    Check.True(Directory.Exists(opened.Store.DirectoryPath));
    Check.True(opened.Store.Save(opened.Store.Load().Settings) is null);
    Check.True(!Directory.Exists(local));
});

suite.Add("X64 INPUT union uses documented size and alignment", () =>
{
    Check.Equal(40, WindowsInputSink.NativeInputSize); Check.Equal(8, (int)Marshal.OffsetOf<NativeMethods.Input>(nameof(NativeMethods.Input.Data)));
    Check.Equal(32, Marshal.SizeOf<NativeMethods.MouseInput>()); Check.Equal(24, Marshal.SizeOf<NativeMethods.KeyboardInput>());
});
suite.Add("Absolute desktop coordinates support negative origins and clamp bounds", () =>
{
    Check.Equal(8, WindowsInputSink.AbsoluteCoordinate(-1920, -1920, 3840));
    Check.Equal(32776, WindowsInputSink.AbsoluteCoordinate(0, -1920, 3840));
    Check.Equal(65527, WindowsInputSink.AbsoluteCoordinate(1919, -1920, 3840));
    Check.Equal(8, WindowsInputSink.AbsoluteCoordinate(-9000, -1920, 3840));
    Check.Equal(65527, WindowsInputSink.AbsoluteCoordinate(9000, -1920, 3840));
});
suite.Add("Automatic controller choice is sticky and scans all four slots", () =>
{
    Check.Equal(3, ControllerService.SelectSlot(-1, -1, 8)); Check.Equal(3, ControllerService.SelectSlot(-1, 3, 15));
    Check.Equal(1, ControllerService.SelectSlot(-1, 3, 6)); Check.Equal(-1, ControllerService.SelectSlot(0, 3, 8));
    Check.Equal(2, ControllerService.SelectSlot(2, 0, 15));
});
suite.Add("Polling worker releases on disconnect and selects replacement slot", () =>
{
    var source = new FakeSource(); var sink = new FakeSink(); source.Set(3, default);
    using var service = new ControllerService(new(), source, sink); service.Start();
    Check.Eventually(() => service.Status.ActiveSlot == 3 && !service.Status.AwaitingNeutral);
    source.Set(3, new(ControllerButtons.A)); Check.Eventually(() => sink.Held[0]);
    source.Remove(3); source.Set(1, default); Check.Eventually(() => !sink.Held[0] && service.Status.ActiveSlot == 1);
});
suite.Add("Worker pause and shutdown release fake injected buttons", () =>
{
    var source = new FakeSource(); var sink = new FakeSink(); source.Set(0, default);
    var service = new ControllerService(new(), source, sink); service.Start();
    try
    {
        Check.Eventually(() => !service.Status.AwaitingNeutral && service.Status.ActiveSlot == 0);
        source.Set(0, new(ControllerButtons.Y)); Check.Eventually(() => sink.Held[2]);
        service.SetPaused(true); Check.Eventually(() => service.Status.Paused && !sink.Held[2]);
        source.Set(0, default); service.SetPaused(false); Check.Eventually(() => !service.Status.Paused && !service.Status.AwaitingNeutral);
        source.Set(0, new(ControllerButtons.A)); Check.Eventually(() => sink.Held[0]);
    }
    finally { service.Dispose(); }
    Check.True(!sink.Held.Any(x => x));
});
suite.Add("Worker recovers an injection failure by pausing and requiring neutral", () =>
{
    var source = new FakeSource(); var sink = new FakeSink(); source.Set(0, default);
    using var service = new ControllerService(new(), source, sink); service.Start();
    Check.Eventually(() => service.Status.ActiveSlot == 0 && !service.Status.AwaitingNeutral);
    sink.FailNextPress = true; source.Set(0, new(ControllerButtons.A));
    Check.Eventually(() => service.Status.Paused && service.Status.Error is not null);
    service.SetPaused(false); Check.Eventually(() => !service.Status.Paused);
    Check.True(!sink.Held[0]); source.Set(0, default); Check.Eventually(() => !service.Status.AwaitingNeutral);
    source.Set(0, new(ControllerButtons.A)); Check.Eventually(() => sink.Held[0]);
});
suite.Add("Disconnected polling is bounded instead of a busy loop", () =>
{
    var source = new FakeSource(); using var service = new ControllerService(new(), source, new FakeSink()); service.Start();
    Thread.Sleep(600); Check.True(source.Reads is >= 4 and <= 20, $"Unexpected read count: {source.Reads}");
});

WindowPlacementTests.Register(suite);
EditHistoryTests.Register(suite);
ProfileTests.Register(suite);
AppPreferencesTests.Register(suite);
GitHubUpdateTests.Register(suite);
Update32Tests.Register(suite);
KeyboardAdditionTests.Register(suite);
SpeedBoostTests.Register(suite);
PieRedesignTests.Register(suite);
PieTests.Register(suite);
PieDPadTests.Register(suite);
RecordedKeyboardTests.Register(suite);
HeldKeyboardTests.Register(suite);
PieQuickGestureTests.Register(suite);
FoundationTests.Register(suite);
ActionCollectionTests.Register(suite);
ActionExpansionTests.Register(suite);
PauseCaptureTests.Register(suite);
MovementSettingsTests.Register(suite);
IndependentCursorTests.Register(suite);
PieSizeTests.Register(suite);
return suite.Run(args.Length > 0 ? args[0] : null, args.Skip(1).FirstOrDefault());

static class Check
{
    public static void True(bool value, string message = "Condition was false") { if (!value) throw new Exception(message); }
    public static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
    public static void Near(double expected, double actual, double tolerance = .00000001) => True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected} ± {tolerance}; got {actual}");
    public static void Eventually(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 3000) { if (condition()) return; Thread.Sleep(10); }
        True(condition(), "Timed out waiting for worker state");
    }
}
sealed class TestSuite
{
    private readonly List<(string Name, Action Test)> _tests = [];
    public void Add(string name, Action test) => _tests.Add((name, test));
    public int Run(string? output, string? filter = null)
    {
        var failures = 0; var xml = new XElement("testsuite", new XAttribute("name", "FullTilt"));
        var selected = _tests.Where(t => filter is null || t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var (name, test) in selected)
        {
            var clock = Stopwatch.StartNew(); var result = new XElement("testcase", new XAttribute("name", name));
            try { test(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}\n{ex}"); result.Add(new XElement("failure", ex.ToString())); }
            result.Add(new XAttribute("time", clock.Elapsed.TotalSeconds)); xml.Add(result);
        }
        xml.Add(new XAttribute("tests", selected.Length), new XAttribute("failures", failures));
        if (output is not null) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!); new XDocument(xml).Save(output); }
        Console.WriteLine($"\n{selected.Length - failures}/{selected.Length} passed. No real input injection was used."); return failures == 0 ? 0 : 1;
    }
}
sealed class FakeSink : IInputSink
{
    public readonly bool[] Held = new bool[3];
    public readonly ConcurrentQueue<(MouseButton Button, bool Down)> ButtonEvents = new();
    public readonly ConcurrentQueue<InputAction> Actions = new();
    public readonly ConcurrentQueue<KeyboardChord> Recordings = new();
    public InputAction[] KeyboardActionsHeld = [];
    public KeyboardChord[] RecordedKeysHeld = [];
    public int X, Y, ScrollX, ScrollY;
    public volatile bool FailNextPress;
    public bool KeyHeld;
    public bool CtrlActive, ShiftActive, AltActive;
    public void SetStickyModifier(StickyModifier modifier, bool active) { if (modifier == StickyModifier.Ctrl) CtrlActive = active; else if (modifier == StickyModifier.Shift) ShiftActive = active; else AltActive = active; }
    public void Move(int x, int y) { X += x; Y += y; }
    public void Scroll(int x, int y) { ScrollX += x; ScrollY += y; }
    public void SetMouseButton(MouseButton button, bool down)
    {
        if (down && FailNextPress) { FailNextPress = false; throw new System.ComponentModel.Win32Exception("Simulated injection failure"); }
        Volatile.Write(ref Held[(int)button], down); ButtonEvents.Enqueue((button, down));
    }
    public void Execute(InputAction action) => Actions.Enqueue(action);
    public void ExecuteKeyboard(KeyboardChord chord) => Recordings.Enqueue(chord);
    public void SetKeyboardHolds(ReadOnlySpan<InputAction> actions, IReadOnlyList<KeyboardChord> recordings)
    {
        foreach (var action in actions) if (!KeyboardActionsHeld.Contains(action)) Actions.Enqueue(action);
        foreach (var chord in recordings) if (!RecordedKeysHeld.Any(c => KeyboardChord.Same(c, chord))) Recordings.Enqueue(chord);
        KeyboardActionsHeld = actions.ToArray(); RecordedKeysHeld = recordings.ToArray();
    }
    public void ReleaseAll() { for (var i = 0; i < Held.Length; i++) if (Held[i]) SetMouseButton((MouseButton)i, false); KeyHeld = CtrlActive = ShiftActive = AltActive = false; KeyboardActionsHeld = []; RecordedKeysHeld = []; }
}
sealed class FakeSource : IControllerSource
{
    private readonly ConcurrentDictionary<int, ControllerState> _states = new();
    public int Reads;
    public void Set(int slot, ControllerState state) => _states[slot] = state;
    public void Remove(int slot) => _states.TryRemove(slot, out _);
    public bool TryGetState(int slot, out ControllerState state) { Interlocked.Increment(ref Reads); return _states.TryGetValue(slot, out state); }
}
static class Harness
{
    public static (InputEngine Engine, FakeSink Sink) New(AppSettings? settings = null)
    {
        var sink = new FakeSink(); var engine = new InputEngine(sink); engine.Configure(settings ?? new()); engine.Tick(default, .008); return (engine, sink);
    }
}
sealed class TestDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FullTilt.Tests", Guid.NewGuid().ToString("N"));
    public TestDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, true);
}
