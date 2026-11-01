using System.ComponentModel;
using System.Text.Json;
using FullTilt.Core;
using FullTilt.Windows;

static class RecordedKeyboardTests
{
    private static KeyboardChord Chord(params ushort[] keys) => new(keys.Select(k => new KeyboardKey(k)).ToArray());
    public static void Register(TestSuite suite)
    {
        foreach (var key in Enumerable.Range(0x70, 24).Concat(Enumerable.Range(0x30, 10)).Concat(Enumerable.Range(0x60, 10)).Concat(new[] { 8, 9, 13, 19, 27, 32, 44, 144, 145, 186, 191, 222, 0xA2, 0xA3, 0x5B, 0x5C, 0xAF }))
        {
            var vk = (ushort)key;
            suite.Add($"Recorded keyboard captures key {key:X2} once including repeats", () =>
            {
                var recording = new KeyboardRecording(); recording.Update(new(vk), true); recording.Update(new(vk), true); recording.Update(new(vk), false);
                Check.True(recording.Complete); Check.Equal(1, recording.Result!.Keys.Length); Check.Equal(vk, recording.Result.Keys[0].VirtualKey);
                var input = new FakeKeys(); new KeyboardOwnership(input).Play(recording.Result);
                Check.Equal(2, input.Events.Count); Check.True(input.Events[0].Down && !input.Events[1].Down); Check.Equal(0, input.Down.Count);
            });
        }
        suite.Add("Recorded keyboard waits for entry keys and all shortcut releases", () =>
        {
            var recording = new KeyboardRecording([new(0xA2)]); recording.Update(new(0x41), true); recording.Update(new(0xA2), false);
            Check.True(recording.WaitingForRelease); recording.Update(new(0x41), false); Check.True(!recording.WaitingForRelease && !recording.Complete);
            recording.Update(new(0xA3, 29, true), true); recording.Update(new(0x31), true); recording.Update(new(0x70), true); recording.Update(new(0x31), false);
            recording.Update(new(0xA3, 29, true), false); Check.True(!recording.Complete); recording.Update(new(0x70), false);
            Check.True(recording.Complete); Check.Equal("Right Ctrl + 1 + F1", recording.Result!.Label);
        });
        suite.Add("Recorded keyboard rejects invalid data and secure attention chord", () =>
        {
            Check.True(Chord(0xA2, 0xA4, 0x2E).Validated() is null); Check.True(Chord(1).Validated() is null);
            Check.True(new KeyboardChord(null!).Validated() is null); Check.True(Chord().Validated() is null);
            Check.True(new KeyboardChord([new(65, 999)]).Validated() is null);
            Check.Equal("X", new KeyboardChord([new(65, Name: "\nX\t")]).Validated()!.Label);
        });
        suite.Add("Recorded keyboard filter forwards initial releases and suppresses fresh keys through entry gate", () =>
        {
            var filter = new KeyboardRecordingFilter([new(0xA3), new(13)]);
            Check.True(filter.Update(new(65), true)); Check.True(filter.Update(new(65), false));
            Check.True(!filter.Update(new(0xA3, 29, true), false)); Check.True(filter.Recording.WaitingForRelease);
            Check.True(!filter.Update(new(13, 28, true), false)); Check.True(!filter.Recording.WaitingForRelease);
            Check.True(filter.Update(new(9), true)); Check.True(filter.Update(new(9), true)); Check.True(filter.Update(new(9), false));
            Check.True(filter.Recording.Complete); Check.Equal("Tab", filter.Recording.Result!.Label);
            Check.True(!filter.Update(new(65), true));
        });
        suite.Add("Recorded keyboard preserves exact extended Enter variants and reverse releases", () =>
        {
            var chord = new KeyboardChord([new(0xA3, 29, true), new(13, 28), new(13, 28, true)]);
            var input = new ExactKeys(); var keyboard = new KeyboardOwnership(input); keyboard.Play(chord);
            Check.Equal(6, input.Events.Count); Check.True(!input.Events[1].IsExtended && input.Events[2].IsExtended);
            Check.Equal(input.Events[2] with { Down = false }, input.Events[3]); Check.Equal(0, input.Down.Count);
            Check.Equal(0u, NativeKeyInput.Encode(input.Events[1]).Data.Keyboard.Flags);
            Check.Equal(1u, NativeKeyInput.Encode(input.Events[2]).Data.Keyboard.Flags);
        });
        for (var prefix = 0; prefix < 8; prefix++)
        {
            var limit = prefix;
            suite.Add($"Recorded keyboard partial prefix {limit} cleans exact owned keys", () =>
            {
                var input = new ExactKeys(); var keyboard = new KeyboardOwnership(input); keyboard.SetLatch(StickyModifier.Alt, true);
                input.Limit = limit;
                var chord = new KeyboardChord([new(0xA3, 29, true), new(13, 28), new(13, 28, true)]);
                try { keyboard.Play(chord); throw new Exception("Expected partial batch failure"); } catch (Win32Exception) { }
                keyboard.ReleaseAll(); Check.Equal(0, input.Down.Count);
            });
        }
        suite.Add("Recorded keyboard borrows physical modifiers and never releases physical keys", () =>
        {
            var input = new FakeKeys(); input.Physical.Add(0xA3); var keyboard = new KeyboardOwnership(input); keyboard.Play(Chord(0xA3, 65));
            Check.Equal(2, input.Events.Count); Check.True(input.Events.All(e => e.Key == 65)); keyboard.ReleaseAll(); Check.True(input.Physical.Contains(0xA3));
            input.Physical.Add(65); input.Events.Clear(); keyboard.Play(Chord(0xA2, 65)); Check.Equal(0, input.Events.Count);
            input.Physical.Clear(); keyboard.SetLatch(StickyModifier.Alt, true); input.Events.Clear(); keyboard.Play(Chord(65));
            Check.Equal(new KeyTransition(0xA4, false), input.Events[0]); Check.Equal(new KeyTransition(0xA4, true), input.Events[^1]);
            keyboard.ReleaseAll(); Check.Equal(0, input.Down.Count);
        });
        suite.Add("Recorded keyboard normal bindings hold until release and rearm after reset", () =>
        {
            var settings = new AppSettings { Mappings = new() { [Control.A] = InputAction.None }, RecordedBindings = new() { [Control.A] = Chord(0xA2, 0x31) } };
            var h = Harness.New(settings); for (var i = 0; i < 90; i++) h.Engine.Tick(new(ControllerButtons.A), .008);
            Check.Equal(1, h.Sink.Recordings.Count); Check.Equal(1, h.Sink.RecordedKeysHeld.Length); Check.True(h.Sink.Actions.IsEmpty && !h.Sink.Held.Any(b => b));
            h.Engine.Reset(); Check.Equal(0, h.Sink.RecordedKeysHeld.Length); h.Engine.Tick(new(ControllerButtons.A), .008); Check.Equal(1, h.Sink.Recordings.Count);
            h.Engine.Tick(default, .008); h.Engine.Tick(new(ControllerButtons.A), .008); Check.Equal(2, h.Sink.Recordings.Count);
        });
        foreach (var cursor in new[] { false, true }) for (var direction = 0; direction < 4; direction++)
        {
            var index = direction; var movement = cursor;
            suite.Add($"Recorded keyboard contextual direction {index} cursor mode {movement} is isolated", () =>
            {
                var h = new PieHarness(dpadMovement: movement); var menu = h.Menu.Record(index, Chord(0x70), true);
                var control = Control.DPadUp + index; var button = Enum.Parse<ControllerButtons>(control.ToString());
                h.Settings.Mappings[control] = InputAction.Backspace; h.Settings = h.Settings with { PieMenus = [menu] }; h.Engine.Configure(h.Settings); h.Tick(default);
                h.Tick(new(ControllerButtons.LB)); for (var i = 0; i < 60; i++) h.Tick(new(ControllerButtons.LB | button));
                Check.Equal(1, h.Sink.Recordings.Count); Check.True(h.Engine.PieOpen && h.Sink.Actions.IsEmpty); Check.Equal(-1, h.Engine.PieSelected);
                Check.Equal(1, h.Sink.RecordedKeysHeld.Length);
                h.Tick(new(button)); h.Tick(new(button)); Check.Equal(1, h.Sink.Recordings.Count); Check.True(h.Sink.Actions.IsEmpty);
                Check.Equal(0, h.Sink.RecordedKeysHeld.Length);
                h.Tick(default); h.Tick(new(button)); if (!movement) Check.Equal(InputAction.Backspace, h.Sink.Actions.Single());
                Check.Equal(InputAction.Backspace, h.Settings.Mappings[control]);
            });
        }
        suite.Add("Recorded keyboard pie segment waits for hide and cancels on focus change", () =>
        {
            foreach (var focusChange in new[] { false, true })
            {
                var h = new PieHarness(); h.Settings = h.Settings with { PieMenus = [h.Menu.Record(0, Chord(0xA2, 65))] }; h.Engine.Configure(h.Settings); h.Tick(default);
                h.Platform.AutoHide = false; h.Tick(new(ControllerButtons.LB, LeftY: 32767)); h.Tick(new(LeftY: 32767));
                Check.Equal(0, h.Sink.Recordings.Count); if (focusChange) h.Platform.Foreground = 2;
                h.Platform.IsHidden = true; h.Tick(default); Check.Equal(focusChange ? 0 : 1, h.Sink.Recordings.Count); Check.True(h.Sink.Actions.IsEmpty);
            }
        });
        suite.Add("Recorded keyboard contextual cancel wins and held mouse blocks shortcuts", () =>
        {
            var h = new PieHarness(); var menu = h.Menu.Record(0, Chord(65), true).AssignDPad(1, InputAction.ReleaseCancel).AssignDPad(2, InputAction.LeftClick);
            h.Settings = h.Settings with { PieMenus = [menu] }; h.Engine.Configure(h.Settings); h.Tick(default); h.Tick(new(ControllerButtons.LB));
            h.Tick(new(ControllerButtons.LB | ControllerButtons.DPadUp | ControllerButtons.DPadDown)); Check.True(!h.Engine.PieOpen); Check.Equal(0, h.Sink.Recordings.Count);
            h.Tick(default); h.Tick(new(ControllerButtons.LB)); h.Tick(new(ControllerButtons.LB | ControllerButtons.DPadLeft));
            h.Tick(new(ControllerButtons.LB | ControllerButtons.DPadLeft | ControllerButtons.DPadUp)); Check.Equal(0, h.Sink.Recordings.Count);
            h.Engine.Reset(); Check.True(!h.Sink.Held.Any(b => b));
        });
        suite.Add("Recorded keyboard drafts history swap clone resize and profiles stay detached", () =>
        {
            var menu = PieMenu.Create("Keys", 4).Assign(0, InputAction.Copy); var settings = ProfileLibrary.Initialize(new AppSettings { PieMenus = [menu] });
            var draft = new PieDraft(menu, settings); draft.Record(0, Chord(65)); draft.Record(2, Chord(0xA2, 0x70), true);
            Check.True(draft.Dirty); Check.Equal(InputAction.Copy, menu.Slots[0]); var edited = draft.Apply(settings);
            var before = EditFields.Read(EditTab.PieMenus, settings); var after = EditFields.Read(EditTab.PieMenus, edited); var history = new EditHistory(); history.Record(before, after);
            var undone = EditFields.Apply(EditTab.PieMenus, history.Undo(after), edited); Check.Equal(InputAction.Copy, undone.PieMenus[0].Slots[0]); Check.Equal(0, undone.PieMenus[0].RecordedDPad.Count);
            var redone = EditFields.Apply(EditTab.PieMenus, history.Redo(EditFields.Read(EditTab.PieMenus, undone)), undone);
            Check.Equal("Ctrl + F1", redone.PieMenus[0].DPadLabel(2)); var copy = redone.PieMenus[0].Duplicate(); copy.RecordedSlots[0].Keys[0] = new(66);
            Check.Equal("A", redone.PieMenus[0].SlotLabel(0)); Check.Equal("A", redone.PieMenus[0].Swap(0, 3).SlotLabel(3));
            Check.Equal(0, redone.PieMenus[0].Swap(0, 3).Resize(2).RecordedSlots.Count);
            var other = ProfileLibrary.Create(edited, "Other"); other = other with { PieMenus = [menu] }; other = ProfileLibrary.Switch(other, settings.ActiveProfileId!.Value);
            Check.Equal("A", other.PieMenus[0].SlotLabel(0)); draft.Restore(); Check.Equal(0, draft.Menu.RecordedSlots.Count); Check.Equal(0, draft.Menu.RecordedDPad.Count);
        });
        suite.Add("Recorded keyboard keybinding field history replaces actions and pies independently", () =>
        {
            var settings = new AppSettings().Validated(); var edited = settings with { Mappings = new(settings.Mappings) { [Control.A] = InputAction.None }, RecordedBindings = new() { [Control.A] = Chord(0x70) } };
            var before = EditFields.Read(EditTab.Keybindings, settings); var after = EditFields.Read(EditTab.Keybindings, edited); var history = new EditHistory(); history.Record(before, after);
            var undone = EditFields.Apply(EditTab.Keybindings, history.Undo(after), edited); Check.Equal(InputAction.LeftClick, undone.Mappings[Control.A]); Check.Equal(0, undone.RecordedBindings.Count);
            var restored = EditFields.Apply(EditTab.Keybindings, history.Redo(before), undone); Check.Equal("F1", restored.RecordedBindings[Control.A].Label);
            var menu = PieMenu.Create(); var draft = new PieDraft(menu, restored, true) { OpenWith = Control.A }; Check.Equal("F1", draft.BindingConflict(restored)!);
            Check.Equal(0, draft.Apply(restored).RecordedBindings.Count);
        });
        suite.Add("Recorded keyboard settings migrate v15 exactly and persist all targets", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path); const string original = """{"Version":15,"CursorSpeed":1743,"Mappings":{"A":"None","X":"RightClick"}}""";
            File.WriteAllText(store.FilePath, original); var loaded = store.Load(); Check.Equal(original, File.ReadAllText(store.FilePath + ".v15-backup")); Check.Equal(original, File.ReadAllText(store.FilePath));
            var menu = PieMenu.Create("Kept", 4).Record(0, Chord(0x87)).Record(3, Chord(0xA3, 0x31), true);
            Check.True(store.Save(loaded.Settings with { RecordedBindings = new() { [Control.A] = Chord(0x31) }, PieMenus = [menu] }) is null);
            var again = new SettingsStore(dir.Path).Load().Settings; Check.Equal("1", again.RecordedBindings[Control.A].Label); Check.Equal("F24", again.PieMenus[0].SlotLabel(0));
            Check.Equal("Right Ctrl + 1", again.PieMenus[0].DPadLabel(3)); Check.Near(1743, again.CursorSpeed); Check.Equal(InputAction.RightClick, again.Mappings[Control.X]);
            Check.True(!File.ReadAllText(store.FilePath).Contains("WaitingForRelease"));
        });
        suite.Add("Recorded keyboard malformed optional entries retain unrelated settings and exact backup", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path);
            const string json = """{"Version":16,"CursorSpeed":1743,"Mappings":{"A":"None","X":"None"},"RecordedBindings":{"A":{"Keys":[{"VirtualKey":112}]},"X":{"Keys":"invalid"},"Unknown":{"Keys":[{"VirtualKey":65}]}},"PieMenus":[{"Id":"098db238-091f-4507-bbb5-54447e9c7ec9","Name":"Kept","SegmentCount":4,"Slots":["None","Copy"],"RecordedSlots":{"0":{"Keys":[{"VirtualKey":113}]},"2":false}}]}""";
            File.WriteAllText(store.FilePath, json); var loaded = store.Load(); Check.True(loaded.Warning is not null); Check.Equal(json, File.ReadAllText(Directory.GetFiles(dir.Path, "*.profiles-backup-*").Single()));
            Check.Near(1743, loaded.Settings.CursorSpeed); Check.Equal("F1", loaded.Settings.RecordedBindings[Control.A].Label); Check.Equal(1, loaded.Settings.RecordedBindings.Count);
            Check.Equal("F2", loaded.Settings.PieMenus[0].SlotLabel(0)); Check.Equal(InputAction.Copy, loaded.Settings.PieMenus[0].Slots[1]);
            Check.True(store.Save(loaded.Settings) is null);
        });
        suite.Add("Recorded keyboard failed migration preservation disables saving", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path); const string json = """{"Version":15,"CursorSpeed":1743}""";
            File.WriteAllText(store.FilePath, json); Directory.CreateDirectory(store.FilePath + ".v15-backup"); var loaded = store.Load();
            Check.True(loaded.Warning is not null && store.Save(loaded.Settings) is not null); Check.Equal(json, File.ReadAllText(store.FilePath));
        });
        suite.Add("Recorded keyboard out of range optional segment preserves exact recovery file", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path);
            const string json = """{"Version":16,"CursorSpeed":1743,"PieMenus":[{"Id":"098db238-091f-4507-bbb5-54447e9c7ec9","Name":"Kept","SegmentCount":2,"Slots":["Copy","Paste"],"RecordedSlots":{"7":{"Keys":[{"VirtualKey":112}]}}}]}""";
            File.WriteAllText(store.FilePath, json); var loaded = store.Load(); Check.True(loaded.Warning is not null);
            Check.Equal(json, File.ReadAllText(Directory.GetFiles(dir.Path, "*.profiles-backup-*").Single()));
            Check.Equal(InputAction.Copy, loaded.Settings.PieMenus[0].Slots[0]); Check.Near(1743, loaded.Settings.CursorSpeed);
        });
        suite.Add("Recorded keyboard worker suppresses controller through review and neutral gates on exit", () =>
        {
            var source = new FakeSource(); var sink = new FakeSink(); source.Set(0, default);
            using var service = new ControllerService(new(), source, sink); service.Start(); Check.Eventually(() => !service.Status.AwaitingNeutral);
            source.Set(0, new(ControllerButtons.A)); Check.Eventually(() => sink.Held[0]); service.BeginKeyboardRecording();
            Check.Eventually(() => service.KeyboardRecordingReady && !sink.Held[0]); source.Set(0, new(ControllerButtons.A | PauseShortcutRules.Default)); Thread.Sleep(60);
            service.EndKeyboardRecording(); Check.Eventually(() => !service.KeyboardRecordingReady); Thread.Sleep(60); Check.True(!service.Status.Paused && !sink.Held[0]);
            source.Set(0, default); Check.Eventually(() => !service.Status.AwaitingNeutral); source.Set(0, new(ControllerButtons.A)); Check.Eventually(() => sink.Held[0]);
            service.SetPaused(true); Check.Eventually(() => service.Status.Paused); service.BeginKeyboardRecording(); Check.Eventually(() => service.KeyboardRecordingReady);
            service.EndKeyboardRecording(); Thread.Sleep(60); Check.True(service.Status.Paused);
        });
    }
    private sealed class ExactKeys : IKeyInput
    {
        public readonly HashSet<(ushort, bool)> Down = []; public readonly List<KeyTransition> Events = []; public int? Limit;
        public bool IsDown(ushort key) => Down.Any(k => k.Item1 == key);
        public int Send(ReadOnlySpan<KeyTransition> events)
        {
            var count = Math.Min(Limit ?? events.Length, events.Length); Limit = null;
            foreach (var item in events[..count]) { Events.Add(item); if (item.Down) Down.Add((item.Key, item.IsExtended)); else Down.Remove((item.Key, item.IsExtended)); }
            return count;
        }
    }
}
