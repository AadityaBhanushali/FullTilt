using FullTilt.Core;
using FullTilt.Windows;

static class SpeedBoostTests
{
    private static AppSettings Settings => new() { CursorSpeed = 800, PrecisionPercent = 25, Mappings = new(AppSettings.DefaultMappings()) { [Control.LB] = InputAction.SpeedBoostHold, [Control.RB] = InputAction.SpeedBoostHold, [Control.LT] = InputAction.SpeedBoostToggle, [Control.RT] = InputAction.PrecisionHold, [Control.L3] = InputAction.ReleaseCancel, [Control.R3] = InputAction.PrecisionToggle } };
    public static void Register(TestSuite suite)
    {
        foreach (var multiplier in new[] { 1.0, 1.35, 1.5, 1.75, 2.5, 3.25, 6.125, 10.0 })
            suite.Add($"Speed Boost {multiplier} uses exact multiplier and restores normal speed on release", () =>
            {
                var h = Harness.New(Settings with { SpeedBoostMultiplier = multiplier });
                for (var i = 0; i < 50; i++) h.Engine.Tick(new(ControllerButtons.LB | ControllerButtons.RB, LeftX: 32767), .02);
                Check.True(h.Engine.SpeedBoostActive); Check.True(Math.Abs(h.Sink.X - 800 * multiplier) <= 1); var before = h.Sink.X;
                for (var i = 0; i < 50; i++) h.Engine.Tick(new(LeftX: 32767), .02);
                Check.True(!h.Engine.SpeedBoostActive); Check.True(Math.Abs(h.Sink.X - before - 800) <= 1);
            });
        suite.Add("Speed Boost toggles once per edge and combines hold without stacking", () =>
        {
            var h = Harness.New(Settings); h.Engine.Tick(new(LeftTrigger: 255), .01); Check.True(h.Engine.SpeedBoostActive);
            h.Engine.Tick(new(LeftTrigger: 255), .01); Check.True(h.Engine.SpeedBoostActive); h.Engine.Tick(default, .01); Check.True(h.Engine.SpeedBoostActive);
            h.Engine.Tick(new(ControllerButtons.LB | ControllerButtons.RB, LeftX: 32767), .02); Check.Equal(24, h.Sink.X);
            h.Engine.Tick(new(LeftTrigger: 255), .01); Check.True(!h.Engine.SpeedBoostActive);
            var coordinator = new ActionCoordinator(new FakeSink()); coordinator.Update([], [InputAction.SpeedBoostToggle, InputAction.SpeedBoostToggle], 0); Check.True(coordinator.SpeedBoostActive);
        });
        suite.Add("Speed Boost precision held and toggled take priority without changing precision", () =>
        {
            var h = Harness.New(Settings with { SpeedBoostMultiplier = 10 });
            h.Engine.Tick(new(ControllerButtons.LB, LeftX: 32767, RightTrigger: 255), .02); Check.Equal(4, h.Sink.X); Check.True(!h.Engine.SpeedBoostActive && h.Engine.PrecisionActive);
            h.Engine.Tick(new(ControllerButtons.LB | ControllerButtons.RightThumb, LeftX: 32767), .02); Check.Equal(8, h.Sink.X);
            h.Engine.Tick(new(ControllerButtons.LB), .01); h.Engine.Tick(new(ControllerButtons.LB | ControllerButtons.RightThumb, LeftX: 32767), .02);
            Check.Equal(168, h.Sink.X); Check.True(h.Engine.SpeedBoostActive && !h.Engine.PrecisionActive);
        });
        foreach (var toggle in new[] { false, true })
        foreach (var boundary in new[] { "pause", "disconnect", "controller", "binding", "cancel" })
            suite.Add($"Speed Boost {(toggle ? "toggle" : "hold")} clears on {boundary} and requires neutral", () =>
            {
                var settings = Settings; var h = Harness.New(settings); var down = toggle ? new ControllerState(LeftTrigger: 255) : new ControllerState(ControllerButtons.LB);
                h.Engine.Tick(down, .01); Check.True(h.Engine.SpeedBoostActive);
                switch (boundary)
                {
                    case "pause": h.Engine.SetPaused(true); h.Engine.SetPaused(false); break;
                    case "disconnect": h.Engine.Disconnect(); break;
                    case "controller": h.Engine.Configure(settings with { ControllerSlot = 2 }); break;
                    case "binding": h.Engine.Configure(settings with { Mappings = new(settings.Mappings) { [Control.RB] = InputAction.None } }); break;
                    case "cancel": h.Engine.Tick(down with { Buttons = down.Buttons | ControllerButtons.LeftThumb }, .01); break;
                }
                Check.True(!h.Engine.SpeedBoostActive); h.Engine.Tick(down, .01); Check.True(!h.Engine.SpeedBoostActive);
                h.Engine.Tick(default, .01); h.Engine.Tick(down, .01); Check.True(h.Engine.SpeedBoostActive);
            });
        suite.Add("Speed Boost only scales cursor role preserves diagonals scroll and D-pad", () =>
        {
            var s = Settings with { CursorStick = CursorStick.Right, SpeedBoostMultiplier = 2.5 };
            var normal = Harness.New(s); var boosted = Harness.New(s);
            var state = new ControllerState(LeftY: 32767, RightX: 32767, RightY: 32767);
            for (var i = 0; i < 50; i++) { normal.Engine.Tick(state, .02); boosted.Engine.Tick(state with { Buttons = ControllerButtons.LB }, .02); }
            Check.True(Math.Abs(boosted.Sink.X - normal.Sink.X * 2.5) < 3); Check.Equal(boosted.Sink.X, -boosted.Sink.Y); Check.Equal(normal.Sink.ScrollY, boosted.Sink.ScrollY);
            normal.Sink.X = boosted.Sink.X = 0;
            for (var i = 0; i < 10; i++) { normal.Engine.Tick(new(ControllerButtons.DPadRight), .02); boosted.Engine.Tick(new(ControllerButtons.LB | ControllerButtons.DPadRight), .02); }
            Check.Equal(normal.Sink.X, boosted.Sink.X);
        });
        suite.Add("Speed Boost settings preserve decimal values and migrate v5 with exact backup", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path);
            const string json = "{\"Version\":5,\"CursorSpeed\":1875,\"PrecisionPercent\":35,\"PiePresetMigration\":1}";
            File.WriteAllText(store.FilePath, json); var loaded = store.Load(); Check.True(loaded.Warning is null);
            Check.Near(1.5, loaded.Settings.SpeedBoostMultiplier); Check.Near(35, loaded.Settings.PrecisionPercent); Check.Equal(json, File.ReadAllText(store.FilePath + ".v5-backup"));
            foreach (var value in new[] { 1.35, 1.75, 2.5, 3.25, 6.125 })
            {
                Check.True(store.Save(loaded.Settings with { SpeedBoostMultiplier = value }) is null);
                var again = new SettingsStore(dir.Path).Load().Settings; Check.Near(value, again.SpeedBoostMultiplier); Check.Near(1875, again.CursorSpeed);
            }
            Check.True(!File.ReadAllText(store.FilePath).Contains("SpeedBoostActive"));
        });
        suite.Add("Speed Boost invalid persisted values stay finite bounded and factory held boost remains assigned", () =>
        {
            foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1, 0, .25, 1000000 })
            {
                var s = (Settings with { SpeedBoostMultiplier = value }).Validated(); Check.True(double.IsFinite(s.SpeedBoostMultiplier) && s.SpeedBoostMultiplier >= 1 && s.SpeedBoostMultiplier <= 10);
            }
            Check.Equal(InputAction.SpeedBoostHold, AppSettings.FreshInstallation().Mappings[Control.LT]);
            Check.True(!ActionCatalog.SupportsPie(InputAction.SpeedBoostHold)); Check.True(ActionCatalog.SupportsPie(InputAction.SpeedBoostToggle));
            Check.Equal(72, (int)InputAction.SpeedBoostHold); Check.Equal(73, (int)InputAction.SpeedBoostToggle);
        });
        suite.Add("Speed Boost worker publishes active and released indicator state", () =>
        {
            var source = new FakeSource(); source.Set(0, default); using var service = new ControllerService(Settings, source, new FakeSink()); service.Start();
            Check.True(SpinWait.SpinUntil(() => !service.Status.AwaitingNeutral, 2000)); source.Set(0, new(ControllerButtons.LB));
            Check.True(SpinWait.SpinUntil(() => service.Status.SpeedBoostActive, 2000)); source.Set(0, default);
            Check.True(SpinWait.SpinUntil(() => !service.Status.SpeedBoostActive, 2000));
        });
    }
}
