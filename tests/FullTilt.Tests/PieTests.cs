using FullTilt.Core;
using FullTilt.Windows;

static class PieTests
{
    public static void Register(TestSuite suite)
    {
        suite.Add("Pie editor creation rename duplicate and arbitrary collection size retain stable identities", () =>
        {
            var menu = PieMenu.Create("Editing").Assign(0, InputAction.Copy);
            var renamed = menu.Rename("Work"); Check.Equal(menu.Id, renamed.Id);
            var copy = menu.Duplicate().Assign(0, InputAction.Paste); Check.True(copy.Id != menu.Id); Check.Equal(InputAction.Copy, menu.Slots[0]);
            var settings = new AppSettings { PieMenus = [menu, copy], PieBindings = new() { [Control.A] = menu.Id } }.Validated();
            settings = (settings with { PieMenus = [copy, renamed] }).Validated(); Check.Equal(menu.Id, settings.PieBindings[Control.A]);
            Check.Equal(200, new AppSettings { PieMenus = Enumerable.Range(0, 200).Select(i => PieMenu.Create("Menu " + i)).ToList() }.Validated().PieMenus.Count);
        });
        suite.Add("Pie slots replace swap and clear without rotating empty directions", () =>
        {
            var menu = PieMenu.Create().Assign(0, InputAction.Copy).Assign(0, InputAction.Paste).Assign(3, InputAction.NextTrack).Swap(0, 7);
            Check.Equal(InputAction.None, menu.Slots[0]); Check.Equal(InputAction.Paste, menu.Slots[7]);
            menu = menu.Assign(3, InputAction.None); Check.Equal(InputAction.Paste, menu.Slots[7]); Check.Equal(8, menu.Slots.Length);
            Check.True(ReferenceEquals(menu, menu.Assign(8, InputAction.Copy))); Check.True(ReferenceEquals(menu, menu.Assign(0, InputAction.PrecisionHold)));
            Check.True(ReferenceEquals(menu, menu.Swap(-1, 2)));
        });
        suite.Add("Deleting a pie clears only its referencing bindings", () =>
        {
            var one = PieMenu.Create(); var two = PieMenu.Create();
            var settings = new AppSettings { PieMenus = [one, two], PieBindings = new() { [Control.A] = one.Id, [Control.RT] = one.Id, [Control.B] = two.Id } }.Validated();
            settings = PieMenu.Delete(settings, one.Id).Validated();
            Check.Equal(1, settings.PieBindings.Count); Check.Equal(two.Id, settings.PieBindings[Control.B]); Check.Equal(InputAction.None, settings.Mappings[Control.A]); Check.Equal(InputAction.RightClick, settings.Mappings[Control.X]);
        });
        suite.Add("Pie validation handles duplicate IDs slot counts unsupported actions names and stale references", () =>
        {
            var id = Guid.NewGuid(); var menu = new PieMenu(id, "\n ", [InputAction.Copy, (InputAction)999, InputAction.PrecisionHold]);
            var settings = new AppSettings { PieMenus = [menu, menu, new(Guid.Empty, "Bad", [])], PieBindings = new() { [Control.A] = Guid.NewGuid(), [Control.B] = id } }.Validated();
            Check.Equal(1, settings.PieMenus.Count); Check.Equal(8, settings.PieMenus[0].Slots.Length); Check.Equal("Unnamed menu", settings.PieMenus[0].Name);
            Check.Equal(InputAction.None, settings.PieMenus[0].Slots[1]); Check.Equal(InputAction.None, settings.PieMenus[0].Slots[2]);
            Check.Equal(1, settings.PieBindings.Count); Check.Equal(InputAction.None, settings.Mappings[Control.A]);
        });
        suite.Add("Update 2.1 settings migrate with exact v3 backup and unbound starter menus", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path);
            const string json = """{"Version":3,"CursorSpeed":1733,"DPadTapDistance":12,"ShowStateIndicator":false,"Mappings":{"A":"ToggleKeyboard","R3":"StickyCtrl"}}""";
            File.WriteAllText(store.FilePath, json); var loaded = store.Load();
            Check.True(loaded.Warning is null); Check.Equal(AppSettings.CurrentVersion, loaded.Settings.Version); Check.Equal(4, loaded.Settings.PieMenus.Count); Check.Equal(0, loaded.Settings.PieBindings.Count);
            Check.Equal(json, File.ReadAllText(store.FilePath + ".v3-backup")); Check.Equal(json, File.ReadAllText(store.FilePath));
            Check.Near(1733, loaded.Settings.CursorSpeed); Check.Equal(InputAction.ToggleKeyboard, loaded.Settings.Mappings[Control.A]); Check.True(!loaded.Settings.ShowStateIndicator);
            Check.True(store.Save(loaded.Settings) is null); Check.Equal(InputAction.StickyCtrl, store.Load().Settings.Mappings[Control.R3]);
        });
        suite.Add("Pie settings recover malformed optional data without erasing unrelated settings", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path); var id = Guid.NewGuid();
            var json = $$$"""{"Version":4,"CursorSpeed":1744,"Mappings":{"X":"Paste"},"PieMenus":[null,12,{"Id":"bad"},{"Id":"{{{id}}}","Name":12,"Slots":["Copy","FutureThing","PrecisionHold",{},null,"NextTrack"]}],"PieBindings":{"A":"bad","RT":"{{{id}}}","Unknown":"{{{id}}}"}}""";
            File.WriteAllText(store.FilePath, json); var loaded = store.Load();
            Check.True(loaded.Warning is null); Check.Near(1744, loaded.Settings.CursorSpeed); Check.Equal(InputAction.Paste, loaded.Settings.Mappings[Control.X]);
            Check.Equal(5, loaded.Settings.PieMenus.Count); Check.Equal(InputAction.NextTrack, loaded.Settings.PieMenus[0].Slots[5]); Check.Equal(InputAction.None, loaded.Settings.PieMenus[0].Slots[1]);
            Check.Equal(InputAction.None, loaded.Settings.Mappings[Control.A]); Check.Equal(id, loaded.Settings.PieBindings[Control.RT]);
            Check.True(store.Save(loaded.Settings) is null); Check.Equal(id, store.Load().Settings.PieMenus[0].Id);
            var saved = File.ReadAllText(store.FilePath); Check.True(!saved.Contains("Foreground") && !saved.Contains("Selected") && !saved.Contains("PieOpen"));
        });
        suite.Add("v3 backup failure disables migration saves", () =>
        {
            using var dir = new TestDirectory(); var store = new SettingsStore(dir.Path); const string json = "{\"Version\":3,\"CursorSpeed\":1600}";
            File.WriteAllText(store.FilePath, json); Directory.CreateDirectory(store.FilePath + ".v3-backup");
            var loaded = store.Load(); Check.True(loaded.Warning is not null); Check.True(store.Save(loaded.Settings) is not null); Check.Equal(json, File.ReadAllText(store.FilePath));
        });
        for (var sector = 0; sector < 8; sector++)
        {
            var index = sector;
            suite.Add($"Pie direction {PieMenu.Directions[index]} selects and cancels after held center", () =>
            {
                var selection = new PieSelection(); selection.Update(0, 0); var (x, y) = Stick(index * 45);
                selection.Update(x, y); Check.Equal(index, selection.Selected); selection.Update(0, 0, .101); Check.Equal(-1, selection.Selected);
            });
        }
        suite.Add("Pie selection accepts initial deflection but requires deliberate magnitude", () =>
        {
            var selection = new PieSelection(); selection.Update(9000, 0); Check.Equal(-1, selection.Selected);
            selection.Update(32767, 0); Check.Equal(2, selection.Selected);
            selection.Update(0, 0, .101); selection.Update(10000, 0); Check.Equal(-1, selection.Selected);
            selection.Update(0, 32767); Check.Equal(0, selection.Selected);
        });
        suite.Add("Pie center cancellation retains radial hysteresis and permits deliberate reselection", () =>
        {
            var selection = new PieSelection(); selection.Update(0, 0); selection.Update(0, 32767);
            selection.Update(0, 7210); Check.Equal(0, selection.Selected); // Just outside 22%.
            selection.Update(0, 7208, .101); Check.Equal(-1, selection.Selected); // Just inside 22%.
            selection.Update(0, 7210); Check.Equal(-1, selection.Selected);
            selection.Update(0, 18021); Check.Equal(-1, selection.Selected); // Below 55%.
            selection.Update(18022, 0); Check.Equal(2, selection.Selected);
            selection.Update(5000, 5000, .101); Check.Equal(-1, selection.Selected); // Radial neutral diagonal.
        });
        foreach (var right in new[] { false, true })
        foreach (var sameFrame in new[] { false, true })
        {
            var useRight = right; var immediate = sameFrame;
            suite.Add($"Pie RT centered release tolerates simultaneity or cancels held center right={right} same-frame={sameFrame}", () =>
            {
                var h = new PieHarness(opener: Control.RT, right: useRight); var held = State(Control.RT);
                var selected = useRight ? held with { RightY = 32767 } : held with { LeftY = 32767 };
                h.Tick(held); h.Tick(selected); Check.Equal(0, h.Engine.PieSelected);
                if (!immediate) { for (var i = 0; i < 14; i++) h.Tick(held); Check.Equal(-1, h.Engine.PieSelected); Check.Equal(-1, h.Platform.Last!.Selected); }
                h.Tick(default); h.Tick(default);
                Check.True(!h.Engine.PieOpen && h.Platform.Last is null); Check.Equal(immediate ? 1 : 0, h.Sink.Actions.Count);
                Check.Equal(0, h.Sink.ButtonEvents.Count);
                h.Tick(held); h.Tick(selected); h.Tick(held); h.Tick(selected);
                h.Tick(useRight ? new(RightY: 32767) : new(LeftY: 32767));
                Check.Equal(immediate ? 2 : 1, h.Sink.Actions.Count); Check.Equal(InputAction.Copy, h.Sink.Actions.Last());
            });
        }
        suite.Add("Pie angular boundary hysteresis works including wraparound", () =>
        {
            var selection = new PieSelection(); selection.Update(0, 0); selection.Update(0, 32767);
            var p = Stick(25); selection.Update(p.X, p.Y); Check.Equal(0, selection.Selected);
            p = Stick(30); selection.Update(p.X, p.Y); Check.Equal(1, selection.Selected);
            p = Stick(20); selection.Update(p.X, p.Y); Check.Equal(1, selection.Selected);
            p = Stick(15); selection.Update(p.X, p.Y); Check.Equal(0, selection.Selected);
            p = Stick(335); selection.Update(p.X, p.Y); Check.Equal(0, selection.Selected);
            p = Stick(330); selection.Update(p.X, p.Y); Check.Equal(7, selection.Selected);
        });
        foreach (var control in Enum.GetValues<Control>())
        {
            var opener = control;
            suite.Add($"Pie opener {opener} opens immediately and release executes once", () =>
            {
                var h = new PieHarness(opener: opener); var state = State(opener);
                h.Tick(state); Check.True(h.Engine.PieOpen); Check.True(h.Platform.Last is not null);
                h.Tick(state with { LeftY = 32767 }); Check.Equal(0, h.Engine.PieSelected);
                h.Tick(new(LeftY: 32767)); h.Tick(default); Check.Equal(1, h.Sink.Actions.Count); Check.Equal(InputAction.Copy, h.Sink.Actions.Single());
                Check.Equal(0, h.Sink.ButtonEvents.Count); Check.True(h.Platform.Last is null);
            });
        }
        suite.Add("Pie release with no selection does nothing; empty sector clears executable choice", () =>
        {
            var h = new PieHarness(); h.Tick(State(Control.LB)); h.Tick(default); Check.Equal(0, h.Sink.Actions.Count);
            h.Tick(State(Control.LB)); h.Tick(State(Control.LB) with { LeftY = 32767 }); h.Tick(State(Control.LB) with { LeftX = 32767 });
            Check.Equal(2, h.Engine.PieSelected); h.Tick(new(LeftX: 32767)); Check.Equal(0, h.Sink.Actions.Count);
        });
        suite.Add("Pie opener immediately chooses pre-positioned motion", () =>
        {
            var h = new PieHarness(); h.Tick(State(Control.LB) with { LeftY = 32767 }); Check.Equal(0, h.Engine.PieSelected); h.Tick(new(LeftY: 32767)); Check.Equal(InputAction.Copy, h.Sink.Actions.Single()); Check.Equal(0, h.Sink.Y);
        });
        suite.Add("Pie uses cursor role and independent physical L3 identity", () =>
        {
            var h = new PieHarness(opener: Control.L3, right: true); var state = State(Control.L3); h.Tick(state); h.Tick(state with { LeftY = 32767 }); Check.Equal(-1, h.Engine.PieSelected);
            h.Tick(state with { RightY = 32767 }); h.Tick(new(RightY: 32767)); Check.Equal(InputAction.Copy, h.Sink.Actions.Single());
        });
        suite.Add("Pie trigger opener preserves press and release hysteresis", () =>
        {
            var h = new PieHarness(opener: Control.LT); h.Tick(new(LeftTrigger: 139)); Check.True(!h.Engine.PieOpen);
            h.Tick(new(LeftTrigger: 140)); Check.True(h.Engine.PieOpen); h.Tick(new(LeftTrigger: 110, LeftY: 32767)); Check.True(h.Engine.PieOpen);
            h.Tick(new(LeftTrigger: 100, LeftY: 32767)); Check.Equal(1, h.Sink.Actions.Count);
        });
        suite.Add("Pie suppresses stick D-pad scroll and ordinary click presses then rearms after release", () =>
        {
            var h = new PieHarness(dpadMovement: true); var held = State(Control.LB); h.Tick(held);
            h.Tick(held with { Buttons = held.Buttons | ControllerButtons.A | ControllerButtons.DPadRight, LeftX = 32767, RightY = 32767 });
            Check.Equal(0, h.Sink.X); Check.Equal(0, h.Sink.ScrollY); Check.Equal(0, h.Sink.ButtonEvents.Count);
            h.Tick(new(ControllerButtons.A | ControllerButtons.DPadRight, LeftX: 32767, RightY: 32767));
            h.Tick(new(ControllerButtons.A | ControllerButtons.DPadRight, LeftX: 32767, RightY: 32767));
            Check.Equal(0, h.Sink.X); Check.Equal(0, h.Sink.ScrollY); Check.Equal(0, h.Sink.ButtonEvents.Count);
            h.Tick(default); h.Tick(new(ControllerButtons.A, LeftX: 32767)); Check.True(h.Sink.Held[0]); Check.True(h.Sink.X > 0);
        });
        suite.Add("Other pie presses are consumed without replay; physical D-pad opener survives arbitration", () =>
        {
            var h = new PieHarness(opener: Control.DPadUp); h.Bind(Control.DPadDown, h.Menu.Id); h.Bind(Control.RB, h.Menu.Id);
            h.Tick(State(Control.DPadUp)); h.Tick(new(ControllerButtons.DPadUp | ControllerButtons.DPadDown | ControllerButtons.RB, LeftY: 32767));
            Check.True(h.Engine.PieOpen); h.Tick(new(ControllerButtons.DPadDown | ControllerButtons.RB, LeftY: 32767)); Check.Equal(1, h.Sink.Actions.Count);
            h.Tick(new(ControllerButtons.DPadDown | ControllerButtons.RB)); Check.True(!h.Engine.PieOpen);
            h.Tick(default); h.Tick(State(Control.RB)); Check.True(h.Engine.PieOpen);
        });
        suite.Add("A suppressed mouse press cannot acquire ownership by opening a second pie", () =>
        {
            var h = new PieHarness(); h.Tick(State(Control.LB)); h.Tick(new(ControllerButtons.LB | ControllerButtons.A));
            h.Tick(State(Control.A)); h.Tick(new(ControllerButtons.A | ControllerButtons.LB));
            Check.True(h.Engine.PieOpen); Check.Equal(0, h.Sink.ButtonEvents.Count); h.Tick(State(Control.A)); Check.Equal(0, h.Sink.ButtonEvents.Count);
            h.Tick(default); h.Tick(State(Control.A)); Check.True(h.Sink.Held[0]);
        });
        suite.Add("Ambiguous D-pad menu directions wait for arbitration then present immediately", () =>
        {
            var h = new PieHarness(opener: Control.DPadUp); h.Bind(Control.DPadDown, h.Menu.Id);
            h.Tick(new(ControllerButtons.DPadUp | ControllerButtons.DPadDown)); Check.True(!h.Engine.PieOpen);
            h.Tick(State(Control.DPadUp)); Check.True(h.Engine.PieOpen && h.Platform.Last is not null);
            Check.Equal(new PieTarget(1, -300, 450), h.Platform.Last!.Target);
            h.Tick(State(Control.DPadUp) with { LeftY = 32767 }); h.Tick(new(LeftY: 32767)); Check.Equal(1, h.Sink.Actions.Count);
        });
        suite.Add("D-pad movement mode ignores a stored Release/cancel mapping while selecting", () =>
        {
            var h = new PieHarness(dpadMovement: true); h.Map(Control.DPadDown, InputAction.ReleaseCancel);
            h.Tick(State(Control.LB)); h.Tick(new(ControllerButtons.LB | ControllerButtons.DPadDown)); Check.True(h.Engine.PieOpen); Check.Equal(0, h.Sink.Y);
        });
        suite.Add("Pie flicks change selection independently of cursor speed and acceleration", () =>
        {
            var h = new PieHarness(); var menu = h.Menu.Assign(2, InputAction.Paste);
            h.Settings = h.Settings with { PieMenus = [menu], CursorSpeed = 50, ResponseCurve = 3 }; h.Engine.Configure(h.Settings); h.Tick(default);
            h.Tick(State(Control.LB)); h.Tick(State(Control.LB) with { LeftY = 32767 }); h.Tick(State(Control.LB) with { LeftX = 32767 });
            Check.Equal(2, h.Engine.PieSelected); h.Tick(new(LeftX: 32767)); Check.Equal(InputAction.Paste, h.Sink.Actions.Single()); Check.Equal(0, h.Sink.X);
        });
        suite.Add("Worker shutdown hides an active pie and never dispatches its highlight", () =>
        {
            var menu = PieMenu.Create().Assign(0, InputAction.Copy); var source = new FakeSource(); var sink = new FakeSink(); var platform = new FakePiePlatform();
            var settings = new AppSettings { PieMenus = [menu], PieBindings = new() { [Control.LB] = menu.Id } };
            source.Set(0, default);
            using var service = new ControllerService(settings, source, sink, platform); service.Start(); Check.Eventually(() => service.Status.ActiveSlot == 0 && !service.Status.AwaitingNeutral);
            source.Set(0, State(Control.LB)); Check.Eventually(() => platform.Last is not null);
            source.Set(0, State(Control.LB) with { LeftY = 32767 }); Check.Eventually(() => platform.Last?.Selected == 0);
            service.Dispose(); Check.True(platform.Last is null && platform.IsHidden); Check.Equal(0, sink.Actions.Count);
        });
        suite.Add("Simultaneous pie openers create only one session and releasing the other cannot execute", () =>
        {
            var h = new PieHarness(); h.Bind(Control.RB, h.Menu.Id); h.Tick(new(ControllerButtons.LB | ControllerButtons.RB)); h.Tick(new(ControllerButtons.LB, LeftY: 32767)); Check.True(h.Engine.PieOpen); Check.Equal(0, h.Sink.Actions.Count); h.Tick(new(LeftY: 32767)); Check.Equal(1, h.Sink.Actions.Count);
        });
        foreach (var boundary in new[] { "cancel", "pause", "disconnect", "reset", "controller", "binding", "menu", "foreground", "reserved" })
        {
            var name = boundary;
            suite.Add($"Pie {name} cancellation cannot execute or reopen held opener", () =>
            {
                var h = new PieHarness(); var held = State(Control.LB); h.Tick(held); h.Tick(held with { LeftY = 32767 });
                switch (name)
                {
                    case "cancel": h.Tick(held with { Buttons = held.Buttons | ControllerButtons.Y }); break;
                    case "pause": h.Engine.SetPaused(true); h.Engine.SetPaused(false); break;
                    case "disconnect": h.Engine.Disconnect(); break;
                    case "reset": h.Engine.Reset(); break;
                    case "controller": h.Engine.Configure(h.Settings with { ControllerSlot = 2 }); break;
                    case "binding": h.Engine.Configure(h.Settings with { PieBindings = [] }); break;
                    case "menu": h.Engine.Configure(h.Settings with { PieMenus = [] }); break;
                    case "foreground": h.Platform.Foreground = 2; h.Tick(held); break;
                    case "reserved": h.Tick(held with { Buttons = held.Buttons | ControllerButtons.Start | ControllerButtons.Back }); break;
                }
                h.Tick(held); Check.True(!h.Engine.PieOpen); h.Tick(default); Check.Equal(0, h.Sink.Actions.Count);
            });
        }
        suite.Add("Overlay hide acknowledgment gates dispatch and intervening focus change cancels", () =>
        {
            var h = new PieHarness(); h.Platform.AutoHide = false;
            h.Tick(State(Control.LB)); h.Tick(State(Control.LB) with { LeftY = 32767 }); h.Tick(new(LeftY: 32767)); Check.Equal(0, h.Sink.Actions.Count);
            h.Tick(default); Check.Equal(0, h.Sink.Actions.Count); h.Platform.IsHidden = true; h.Platform.Foreground = 2; h.Tick(default); Check.Equal(0, h.Sink.Actions.Count);
            h.Platform.Foreground = 1; h.Tick(State(Control.LB)); h.Tick(State(Control.LB) with { LeftY = 32767 }); h.Tick(new(LeftY: 32767)); h.Platform.IsHidden = true; h.Tick(default); Check.Equal(1, h.Sink.Actions.Count);
        });
        suite.Add("A foreground change while waiting for hiding cancels even if target returns", () =>
        {
            var h = new PieHarness(); h.Platform.AutoHide = false; h.Tick(State(Control.LB)); h.Tick(State(Control.LB) with { LeftY = 32767 }); h.Tick(new(LeftY: 32767));
            h.Platform.Foreground = 2; h.Tick(default); h.Platform.Foreground = 1; h.Platform.IsHidden = true; h.Tick(default); Check.Equal(0, h.Sink.Actions.Count);
        });
        suite.Add("Existing physical mouse hold survives pie opening and releases while selection remains active", () =>
        {
            var h = new PieHarness(); h.Tick(State(Control.A)); Check.True(h.Sink.Held[0]);
            h.Tick(new(ControllerButtons.A | ControllerButtons.LB)); Check.True(h.Sink.Held[0]); h.Tick(State(Control.LB)); Check.True(!h.Sink.Held[0]); Check.True(h.Engine.PieOpen); Check.Equal(2, h.Sink.ButtonEvents.Count);
        });
        suite.Add("Drag lock and sticky modifier ownership survive pie opening and a precision toggle", () =>
        {
            var h = new PieHarness(InputAction.PrecisionToggle); h.Map(Control.X, InputAction.DragLock); h.Map(Control.B, InputAction.StickyCtrl);
            h.Tick(new(ControllerButtons.X | ControllerButtons.B)); h.Tick(default); Check.True(h.Engine.DragLocked && h.Sink.CtrlActive);
            h.Choose(); Check.True(h.Engine.DragLocked && h.Sink.Held[0] && h.Sink.CtrlActive && h.Engine.PrecisionActive); Check.Equal(1, h.Sink.ButtonEvents.Count);
        });
        foreach (var action in new[] { InputAction.LeftClick, InputAction.RightClick, InputAction.MiddleClick, InputAction.DoubleClick, InputAction.CloseWindow, InputAction.MinimizeWindow, InputAction.NextTab, InputAction.Paste, InputAction.ToggleKeyboard })
        {
            var selected = action;
            suite.Add($"Pie {selected} is disabled during synthetic drag ownership", () =>
            {
                var h = new PieHarness(selected); h.Map(Control.X, InputAction.DragLock); h.Tick(State(Control.X)); h.Tick(default); h.Choose();
                Check.True(h.Engine.DragLocked); Check.Equal(1, h.Sink.ButtonEvents.Count); Check.Equal(0, h.Sink.Actions.Count);
                Check.True(ActionCatalog.PieDisabledReason(selected, true) is not null);
            });
        }
        foreach (var action in new[] { InputAction.LeftClick, InputAction.RightClick, InputAction.MiddleClick })
        {
            var selected = action;
            suite.Add($"Pie {selected} emits one complete click", () =>
            {
                var h = new PieHarness(selected); h.Choose(); Check.Equal(2, h.Sink.ButtonEvents.Count); Check.True(h.Sink.Held.All(held => !held)); h.Tick(default); Check.Equal(2, h.Sink.ButtonEvents.Count);
            });
        }
        suite.Add("Pie double-click uses pending sequence and cancel aborts its second pair", () =>
        {
            var h = new PieHarness(InputAction.DoubleClick); h.Choose(); Check.Equal(2, h.Sink.ButtonEvents.Count); Check.True(h.Engine.PendingDoubleClick); h.Engine.Reset(); h.Tick(default); Check.Equal(2, h.Sink.ButtonEvents.Count);
            h.Choose(); for (var i = 0; i < 8; i++) h.Tick(default); Check.Equal(6, h.Sink.ButtonEvents.Count);
        });
        suite.Add("Opening pie stops volume repeat and pending double click without replay", () =>
        {
            var h = new PieHarness(); h.Map(Control.X, InputAction.VolumeUp); h.Tick(State(Control.X)); h.Tick(new(ControllerButtons.X | ControllerButtons.LB));
            for (var i = 0; i < 100; i++) h.Tick(new(ControllerButtons.X | ControllerButtons.LB)); Check.Equal(1, h.Sink.Actions.Count);
            h.Tick(State(Control.X)); for (var i = 0; i < 100; i++) h.Tick(State(Control.X)); Check.Equal(1, h.Sink.Actions.Count);
            h.Tick(default); h.Map(Control.X, InputAction.DoubleClick); h.Tick(default); h.Tick(State(Control.X)); Check.True(h.Engine.PendingDoubleClick); h.Tick(new(ControllerButtons.X | ControllerButtons.LB)); Check.True(!h.Engine.PendingDoubleClick);
        });
        foreach (var action in new[] { InputAction.VolumeUp, InputAction.VolumeDown, InputAction.Copy, InputAction.ToggleKeyboard, InputAction.NextTrack, InputAction.NextTab })
        {
            var selected = action;
            suite.Add($"Pie {selected} dispatches exactly once without held repeat", () =>
            {
                var h = new PieHarness(selected); h.Choose(); for (var i = 0; i < 100; i++) h.Tick(default); Check.Equal(selected, h.Sink.Actions.Single());
            });
        }
        suite.Add("Pie drag-lock release and Release/cancel use centralized ownership", () =>
        {
            var h = new PieHarness(InputAction.DragLock); h.Map(Control.X, InputAction.DragLock); h.Tick(State(Control.X)); h.Tick(default); h.Choose(); Check.True(!h.Engine.DragLocked && !h.Sink.Held[0]);
            h = new PieHarness(InputAction.ReleaseCancel); h.Map(Control.X, InputAction.StickyShift); h.Tick(State(Control.X)); h.Tick(default); h.Choose(); Check.True(!h.Sink.ShiftActive); Check.True(!h.Engine.PieOpen);
        });
        suite.Add("Pie shortcut uses existing sticky modifier conflict batch", () =>
        {
            var keys = new FakeKeys(); var keyboard = new KeyboardOwnership(keys); var dispatcher = new WindowsActionDispatcher(keyboard, new FakeDesktop());
            var coordinator = new ActionCoordinator(new ShortcutSink(keyboard, dispatcher)); coordinator.Update([], [InputAction.StickyCtrl, InputAction.StickyShift], 0);
            Check.True(coordinator.ActivatePie(InputAction.Copy, [])); Check.True(keyboard.IsOwned(KeyboardOwnership.Ctrl)); Check.True(keyboard.IsOwned(KeyboardOwnership.Shift));
            coordinator.Reset(); Check.True(!keyboard.IsOwned(KeyboardOwnership.Ctrl) && !keyboard.IsOwned(KeyboardOwnership.Shift));
        });
        suite.Add("Suspending pie sequences cancels pending desktop actions without releasing sticky ownership", () =>
        {
            var keys = new FakeKeys(); var keyboard = new KeyboardOwnership(keys); var desktop = new FakeDesktop();
            var coordinator = new ActionCoordinator(new ShortcutSink(keyboard, new WindowsActionDispatcher(keyboard, desktop)));
            coordinator.Update([], [InputAction.StickyCtrl], 0); coordinator.SuspendSequences();
            Check.Equal(1, desktop.Cancels); Check.True(keyboard.IsOwned(KeyboardOwnership.Ctrl) && coordinator.CtrlActive);
        });
        foreach (var screen in new[] { (0, 0, 1920, 1080, 96), (-1920, -100, 0, 980, 144), (1920, -1080, 3840, 0, 192), (-300, -200, 0, 0, 192) })
        {
            var (left, top, right, bottom, dpi) = screen;
            suite.Add($"Pie placement clamps all edges on ({left},{top}) at {dpi} DPI", () =>
            {
                foreach (var x in new[] { left - 100, left, (left + right) / 2, right, right + 100 })
                    foreach (var y in new[] { top - 100, top, (top + bottom) / 2, bottom, bottom + 100 })
                    {
                        var p = PiePlacement.Fit(x, y, left, top, right, bottom, dpi);
                        Check.True(p.X >= left && p.Y >= top && p.X + p.Size <= right && p.Y + p.Size <= bottom);
                        Check.Equal(Math.Min((int)(440 * dpi / 96.0), Math.Min(right - left, bottom - top)), p.Size);
                    }
            });
        }
    }
    private static (short X, short Y) Stick(double degrees) => ((short)(Math.Sin(degrees * Math.PI / 180) * 32767), (short)(Math.Cos(degrees * Math.PI / 180) * 32767));
    public static ControllerState State(Control control) => control switch
    {
        Control.LT => new(LeftTrigger: 255), Control.RT => new(RightTrigger: 255), Control.L3 => new(ControllerButtons.LeftThumb), Control.R3 => new(ControllerButtons.RightThumb),
        _ => new(Enum.Parse<ControllerButtons>(control.ToString()))
    };
}
sealed class FakePiePlatform : IPiePlatform
{
    public nint Foreground { get; set; } = 1;
    public bool IsHidden { get; set; } = true;
    public bool AutoHide = true;
    public PiePresentation? Last;
    public PieTarget Capture() => new(Foreground, -300, 450);
    public void Present(PiePresentation? presentation) { Last = presentation; if (presentation is not null) IsHidden = false; else if (AutoHide) IsHidden = true; }
}
sealed class PieHarness
{
    public readonly FakeSink Sink = new(); public readonly FakePiePlatform Platform = new(); public readonly InputEngine Engine;
    public readonly PieMenu Menu; public AppSettings Settings; private readonly Control _opener;
    public PieHarness(InputAction action = InputAction.Copy, Control opener = Control.LB, bool right = false, bool dpadMovement = false)
    {
        _opener = opener; Menu = PieMenu.Create("Test pie").Assign(0, action);
        Settings = new AppSettings { PieMenus = [Menu], PieBindings = new() { [opener] = Menu.Id }, CursorStick = right ? CursorStick.Right : CursorStick.Left, DPadMode = dpadMovement ? DPadMode.CursorMovement : DPadMode.AssignedActions };
        Settings.Mappings[Control.Y] = InputAction.ReleaseCancel;
        Engine = new(Sink, piePlatform: Platform); Engine.Configure(Settings); Tick(default);
    }
    public void Tick(ControllerState state) => Engine.Tick(state, .008);
    public void Map(Control control, InputAction action) { Settings.Mappings[control] = action; Engine.Configure(Settings); Tick(default); }
    public void Bind(Control control, Guid id) { Settings.PieBindings[control] = id; Engine.Configure(Settings); Tick(default); }
    public void Choose() { var state = PieTests.State(_opener); Tick(state); Tick(state with { LeftY = 32767 }); Tick(new(LeftY: 32767)); Tick(default); }
}
sealed class ShortcutSink(KeyboardOwnership keyboard, WindowsActionDispatcher dispatcher) : IInputSink
{
    public void BeginKeyboardFrame() => dispatcher.BeginKeyboardFrame();
    public void SetKeyboardHolds(ReadOnlySpan<InputAction> actions, IReadOnlyList<KeyboardChord> recordings) => dispatcher.SetKeyboardHolds(actions, recordings);
    public void Move(int x, int y) => throw new Exception("Unexpected mouse movement");
    public void Scroll(int x, int y) => throw new Exception("Unexpected scroll");
    public void SetMouseButton(MouseButton button, bool down) => throw new Exception("Unexpected click");
    public void Execute(InputAction action) => dispatcher.Execute(action);
    public void SetStickyModifier(StickyModifier modifier, bool active) => keyboard.SetLatch(modifier, active);
    public void CancelPendingActions() => dispatcher.CancelPending();
    public void ReleaseAll() => keyboard.ReleaseAll();
}
