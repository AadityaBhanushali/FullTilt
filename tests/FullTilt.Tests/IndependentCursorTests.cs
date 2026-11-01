using FullTilt.Core;
using System.Text.Json;

static class IndependentCursorTests
{
    public static void Register(TestSuite suite)
    {
        suite.Add("Independent cursor v16 migration preserves all profile speeds and exact original", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path);
            var first = Guid.NewGuid(); var second = Guid.NewGuid();
            var original = JsonSerializer.Serialize(new { Version = 16, CursorSpeed = 642d, PrecisionPercent = 20d, SpeedBoostMultiplier = 3.6,
                ActiveProfileId = first, Profiles = new[] {
                    new { Id = first, Name = "First", Configuration = new { Version = 16, CursorSpeed = 642d, PrecisionPercent = 20d, SpeedBoostMultiplier = 3.6 } },
                    new { Id = second, Name = "Second", Configuration = new { Version = 16, CursorSpeed = 1875d, PrecisionPercent = 35d, SpeedBoostMultiplier = 2.75 } } } });
            File.WriteAllText(store.FilePath, original); var loaded = store.Load(); Check.True(loaded.Warning is null);
            Check.Equal(original, File.ReadAllText(store.FilePath)); Check.Equal(original, File.ReadAllText(store.FilePath + ".v16-backup"));
            Check.Near(128.4, loaded.Settings.PrecisionCursorSpeed!.Value); Check.Near(2311.2, loaded.Settings.BoostedCursorSpeed!.Value);
            var switched = ProfileLibrary.Switch(loaded.Settings, second);
            Check.Near(656.25, switched.PrecisionCursorSpeed!.Value); Check.Near(5156.25, switched.BoostedCursorSpeed!.Value);
            Check.True(store.Save(switched with { CursorSpeed = 3 }) is null);
            var again = store.Load().Settings; Check.Equal(17, again.Version); Check.Near(3, again.CursorSpeed);
            Check.Near(656.25, again.PrecisionCursorSpeed!.Value); Check.Near(5156.25, again.BoostedCursorSpeed!.Value);
            Check.Near(128.4, ProfileLibrary.Switch(again, first).PrecisionCursorSpeed!.Value);
        });
        suite.Add("Independent cursor legacy backup failure blocks saving", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path); const string original = "{\"Version\":16,\"CursorSpeed\":600}";
            File.WriteAllText(store.FilePath, original); Directory.CreateDirectory(store.FilePath + ".v16-backup");
            Check.True(store.Load().Warning is not null); Check.True(store.Save(new()) is not null); Check.Equal(original, File.ReadAllText(store.FilePath));
        });
        suite.Add("Independent cursor repeat migration preserves changed originals and prior backups", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path);
            const string first = "{\"Version\":16,\"CursorSpeed\":600}";
            const string next = "{\"Version\":16,\"CursorSpeed\":900}";
            File.WriteAllText(store.FilePath, first); Check.True(store.Load().Warning is null);
            File.WriteAllText(store.FilePath, next); Check.True(store.Load().Warning is null);
            Check.Equal(first, File.ReadAllText(store.FilePath + ".v16-backup"));
            Check.Equal(next, File.ReadAllText(Directory.GetFiles(temp.Path, "settings.json.v16-backup-*").Single()));
        });
        suite.Add("Independent cursor malformed absolute fields recover without losing bindings", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path);
            const string original = "{\"Version\":17,\"CursorSpeed\":600,\"PrecisionCursorSpeed\":{},\"BoostedCursorSpeed\":90000,\"Mappings\":{\"R3\":\"Undo\"}}";
            File.WriteAllText(store.FilePath, original); var loaded = store.Load(); Check.True(loaded.Warning is not null);
            Check.Equal(InputAction.Undo, loaded.Settings.Mappings[Control.R3]); Check.Near(90000, loaded.Settings.BoostedCursorSpeed!.Value);
            Check.Near(300, loaded.Settings.PrecisionCursorSpeed!.Value);
            Check.Equal(original, File.ReadAllText(Directory.GetFiles(temp.Path, "settings.json.profiles-backup-*").Single()));
        });
        suite.Add("Independent cursor each absolute speed survives normal and legacy ratio changes", () =>
        {
            var settings = new AppSettings { CursorSpeed = 600, PrecisionCursorSpeed = 9000, BoostedCursorSpeed = 80000 }.Validated();
            settings = (settings with { CursorSpeed = 100, PrecisionPercent = 5, SpeedBoostMultiplier = 1 }).Validated();
            Check.Near(9000, settings.PrecisionCursorSpeed!.Value); Check.Near(80000, settings.BoostedCursorSpeed!.Value);
            settings = (settings with { PrecisionCursorSpeed = 1 }).Validated(); Check.Near(100, settings.CursorSpeed); Check.Near(80000, settings.BoostedCursorSpeed!.Value);
            settings = (settings with { BoostedCursorSpeed = 2 }).Validated(); Check.Near(1, settings.PrecisionCursorSpeed!.Value); Check.Near(100, settings.CursorSpeed);
        });
        foreach (var stick in new[] { CursorStick.Left, CursorStick.Right })
            suite.Add($"Independent cursor runtime absolute speeds and precision priority {stick}", () =>
            {
                var settings = new AppSettings { CursorStick = stick, CursorSpeed = 100, PrecisionCursorSpeed = 9000, BoostedCursorSpeed = 80000,
                    Mappings = new(AppSettings.DefaultMappings()) { [Control.LB] = InputAction.SpeedBoostHold, [Control.RB] = InputAction.PrecisionHold } }.Validated();
                var h = Harness.New(settings);
                var state = stick == CursorStick.Left ? new ControllerState(LeftX: 32767) : new ControllerState(RightX: 32767);
                h.Engine.Tick(state, .02); Check.Equal(2, h.Sink.X);
                h.Engine.Tick(state with { Buttons = ControllerButtons.LB }, .02); Check.Equal(1602, h.Sink.X);
                h.Engine.Tick(state with { Buttons = ControllerButtons.LB | ControllerButtons.RB }, .02); Check.Equal(1782, h.Sink.X);
                h.Engine.Configure(settings with { CursorSpeed = 50 });
                h.Engine.Tick(state with { Buttons = ControllerButtons.LB }, .02); Check.Equal(3382, h.Sink.X);
                h.Engine.Tick(state, .02); Check.Equal(3383, h.Sink.X);
            });
        suite.Add("Independent cursor history restores direct values without changing another tab", () =>
        {
            var initial = new AppSettings { PrecisionCursorSpeed = 300, BoostedCursorSpeed = 3000 }.Validated();
            var changed = initial with { BoostedCursorSpeed = 80000 }; var history = new EditHistory();
            history.Record(EditFields.Read(EditTab.Movement, initial), EditFields.Read(EditTab.Movement, changed));
            var unrelated = changed with { Mappings = new(changed.Mappings) { [Control.B] = InputAction.Undo } };
            var undo = EditFields.Apply(EditTab.Movement, history.Undo(EditFields.Read(EditTab.Movement, unrelated)), unrelated);
            Check.Near(3000, undo.BoostedCursorSpeed!.Value); Check.Equal(InputAction.Undo, undo.Mappings[Control.B]);
            var redo = EditFields.Apply(EditTab.Movement, history.Redo(EditFields.Read(EditTab.Movement, undo)), undo);
            Check.Near(80000, redo.BoostedCursorSpeed!.Value); Check.Near(300, redo.PrecisionCursorSpeed!.Value);
        });
        suite.Add("Independent cursor full absolute range stays finite and within native delta range", () =>
        {
            var h = Harness.New(new AppSettings { CursorSpeed = AppSettings.MaximumCursorSpeed });
            h.Engine.Tick(new(LeftX: 32767), 30); Check.True(h.Sink.X > 0 && h.Sink.X < int.MaxValue);
            foreach (var value in new[] { double.NaN, double.PositiveInfinity, -1, double.MaxValue })
            {
                var s = new AppSettings { CursorSpeed = AppSettings.MaximumCursorSpeed, PrecisionCursorSpeed = value, BoostedCursorSpeed = value }.Validated();
                Check.True(double.IsFinite(s.PrecisionCursorSpeed!.Value) && s.PrecisionCursorSpeed >= 1 && s.PrecisionCursorSpeed <= AppSettings.MaximumCursorSpeed);
                Check.True(double.IsFinite(s.BoostedCursorSpeed!.Value) && s.BoostedCursorSpeed >= 1 && s.BoostedCursorSpeed <= AppSettings.MaximumCursorSpeed);
            }
        });
        suite.Add("Independent cursor captured public defaults retain the current saved movement", () =>
        {
            var s = FactoryDefaults.Create(); Check.Near(600, s.CursorSpeed); Check.Near(200, s.PrecisionCursorSpeed!.Value); Check.Near(3000, s.BoostedCursorSpeed!.Value);
            Check.Equal(InputAction.ArrowLeft, s.Mappings[Control.DPadLeft]); Check.Equal(InputAction.ArrowRight, s.Mappings[Control.DPadRight]);
        });
    }
}
