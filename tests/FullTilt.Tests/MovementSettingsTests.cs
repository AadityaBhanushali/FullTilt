using FullTilt.Core;

static class MovementSettingsTests
{
    public static void Register(TestSuite suite)
    {
        foreach (var stick in new[] { CursorStick.Left, CursorStick.Right })
        foreach (var reverse in new[] { false, true })
            suite.Add($"Movement reference boosted/reversed scroll follows {stick} cursor role reverse={reverse}", () =>
            {
                var settings = new AppSettings { CursorStick = stick, ScrollSpeed = 10, ScrollBoostMultiplier = 3, ReverseScroll = reverse,
                    Mappings = new(AppSettings.DefaultMappings()) { [Control.LB] = InputAction.SpeedBoostHold, [Control.RB] = InputAction.PrecisionHold } };
                var h = Harness.New(settings); var sign = reverse ? -1 : 1;
                var state = stick == CursorStick.Left ? new ControllerState(RightX: 32767, RightY: 32767) : new ControllerState(LeftX: 32767, LeftY: 32767);
                h.Engine.Tick(state, .02); Check.Equal(24 * sign, h.Sink.ScrollX); Check.Equal(24 * sign, h.Sink.ScrollY);
                h.Engine.Tick(state with { Buttons = ControllerButtons.LB }, .02); Check.Equal(96 * sign, h.Sink.ScrollX); Check.Equal(96 * sign, h.Sink.ScrollY);
                h.Engine.Tick(state with { Buttons = ControllerButtons.LB | ControllerButtons.RB }, .02); Check.Equal(120 * sign, h.Sink.ScrollX); Check.Equal(120 * sign, h.Sink.ScrollY);
                h.Engine.Reset(); h.Engine.Tick(state with { Buttons = ControllerButtons.LB }, .02); Check.Equal(120 * sign, h.Sink.ScrollY);
                h.Engine.Tick(default, .02); h.Engine.Tick(state, .02); Check.Equal(144 * sign, h.Sink.ScrollY);
            });
        suite.Add("Movement reference v11 migration preserves exact original and prior scrolling", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path);
            const string original = """{"Version":11,"CursorSpeed":642,"ScrollSpeed":13.4,"SpeedBoostMultiplier":3.6,"Mappings":{"L3":"Undo"}}""";
            File.WriteAllText(store.FilePath, original); var loaded = store.Load();
            Check.Equal(original, File.ReadAllText(store.FilePath + ".v11-backup")); Check.Near(1, loaded.Settings.ScrollBoostMultiplier); Check.True(!loaded.Settings.ReverseScroll);
            Check.Equal(InputAction.Undo, loaded.Settings.Mappings[Control.L3]); Check.Near(642, loaded.Settings.CursorSpeed);
            Check.True(store.Save(loaded.Settings with { ScrollBoostMultiplier = 3, ReverseScroll = true }) is null);
            var saved = store.Load().Settings; Check.Near(3, saved.ScrollBoostMultiplier); Check.True(saved.ReverseScroll); Check.Near(13.4, saved.ScrollSpeed);
            Check.Equal(original, File.ReadAllText(store.FilePath + ".v11-backup"));
        });
        suite.Add("Movement reference failed v11 preservation disables saving", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path); const string original = """{"Version":11,"CursorSpeed":642}""";
            File.WriteAllText(store.FilePath, original); Directory.CreateDirectory(store.FilePath + ".v11-backup");
            Check.True(store.Load().Warning is not null); Check.True(store.Save(new()) is not null); Check.Equal(original, File.ReadAllText(store.FilePath));
        });
        suite.Add("Movement reference malformed optional data preserves unrelated settings", () =>
        {
            using var temp = new TestDirectory(); var store = new SettingsStore(temp.Path); const string original = """{"Version":12,"CursorSpeed":642,"ReverseScroll":"bad","ScrollBoostMultiplier":{},"Mappings":{"R3":"Undo"}}""";
            File.WriteAllText(store.FilePath, original); var loaded = store.Load(); Check.True(loaded.Warning is not null);
            Check.Near(642, loaded.Settings.CursorSpeed); Check.Equal(InputAction.Undo, loaded.Settings.Mappings[Control.R3]); Check.Near(1, loaded.Settings.ScrollBoostMultiplier);
            Check.Equal(original, File.ReadAllText(Directory.GetFiles(temp.Path, "settings.json.profiles-backup-*").Single()));
        });
    }
}
