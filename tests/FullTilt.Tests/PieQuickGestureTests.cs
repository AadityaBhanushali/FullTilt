using FullTilt.Core;

static class PieQuickGestureTests
{
    public static void Register(TestSuite suite)
    {
        foreach (var right in new[] { false, true })
        for (var count = 2; count <= 8; count++)
        {
            var useRight = right; var segments = count;
            suite.Add($"Pie quick gesture pre-positioned and simultaneous release count={count} right={right}", () =>
            {
                var sink = new FakeSink(); var platform = new FakePiePlatform();
                var menu = PieMenu.Create("Quick", segments) with { Slots = Enumerable.Repeat(InputAction.Copy, segments).ToArray() };
                var engine = new InputEngine(sink, piePlatform: platform);
                engine.Configure(new() { PieMenus = [menu], PieBindings = new() { [Control.LB] = menu.Id }, CursorStick = useRight ? CursorStick.Right : CursorStick.Left });
                engine.Tick(default, .008);
                for (var slot = 0; slot < segments; slot++)
                {
                    var angle = PieGeometry.Center(segments, slot) * Math.PI / 180;
                    var x = (short)(Math.Sin(angle) * 32767); var y = (short)(Math.Cos(angle) * 32767);
                    var aimed = useRight ? new ControllerState(RightX: x, RightY: y) : new(LeftX: x, LeftY: y);
                    engine.Tick(aimed, .008); // Direction is already held before pressing the menu.
                    var mouseBefore = (sink.X, sink.Y, sink.ScrollX, sink.ScrollY);
                    engine.Tick(aimed with { Buttons = ControllerButtons.LB }, .008);
                    Check.Equal(slot, engine.PieSelected); Check.Equal(slot, platform.Last!.Selected);
                    engine.Tick(default, .008); // Both spring back in the very next sample.
                    Check.Equal(slot + 1, sink.Actions.Count); Check.Equal(InputAction.Copy, sink.Actions.Last());
                    Check.True(!engine.PieOpen && platform.Last is null); Check.Equal(mouseBefore, (sink.X, sink.Y, sink.ScrollX, sink.ScrollY));
                    engine.Tick(default, .008); Check.Equal(slot + 1, sink.Actions.Count);
                }
            });
        }
        foreach (var right in new[] { false, true })
        foreach (var delay in new[] { 0d, .050, .099, .100, .101, .150 })
        {
            var useRight = right; var releaseDelay = delay;
            suite.Add($"Pie quick gesture release tolerance actual elapsed={delay} right={right}", () =>
            {
                var h = new PieHarness(right: useRight);
                var aimed = useRight ? new ControllerState(ControllerButtons.LB, RightY: 32767) : new(ControllerButtons.LB, LeftY: 32767);
                h.Tick(aimed); Check.Equal(0, h.Engine.PieSelected);
                h.Engine.Tick(default, releaseDelay);
                Check.Equal(releaseDelay <= .100 ? 1 : 0, h.Sink.Actions.Count);
                h.Tick(default); Check.Equal(releaseDelay <= .100 ? 1 : 0, h.Sink.Actions.Count);
            });
        }
        foreach (var opener in Enum.GetValues<Control>())
        {
            var button = opener;
            suite.Add($"Pie quick gesture instant aim and joint release opener={opener}", () =>
            {
                var h = new PieHarness(opener: button);
                h.Tick(PieTests.State(button) with { LeftY = 32767 }); Check.Equal(0, h.Engine.PieSelected);
                h.Tick(default); Check.Equal(InputAction.Copy, h.Sink.Actions.Single());
                Check.Equal(0, h.Sink.ButtonEvents.Count);
            });
        }
        suite.Add("Pie quick gesture center clock expires beyond 100 ms and resets on reselection", () =>
        {
            var selection = new PieSelection(4); selection.Update(0, 32767);
            selection.Update(0, 0, .050); selection.Update(0, 0, .049); Check.Equal(0, selection.Selected);
            selection.Update(0, 0, .001); Check.Equal(0, selection.Selected);
            selection.Update(0, 0, .001); Check.Equal(-1, selection.Selected);
            selection.Update(10000, 0, .008); Check.Equal(-1, selection.Selected); // Under the unchanged 55% threshold.
            selection.Update(32767, 0, .008); Check.Equal(1, selection.Selected);
            selection.Update(0, 0, .060); selection.Update(0, -32767, .008); Check.Equal(2, selection.Selected);
            selection.Update(0, 0, .060); Check.Equal(2, selection.Selected);
            selection.Update(0, 0, .041); Check.Equal(-1, selection.Selected);
        });
        suite.Add("Pie quick gesture weak initial aim does not select and invalid elapsed does not corrupt tolerance", () =>
        {
            var selection = new PieSelection(); selection.Update(0, 18021); Check.Equal(-1, selection.Selected);
            selection.Update(0, 18022); Check.Equal(0, selection.Selected);
            foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d }) selection.Update(0, 0, invalid);
            Check.Equal(0, selection.Selected); selection.Update(0, 0, .101); Check.Equal(-1, selection.Selected);
        });
        suite.Add("Pie quick gesture repeats without centering while ordinary movement remains disarmed", () =>
        {
            var h = new PieHarness();
            for (var i = 0; i < 20; i++)
            {
                h.Tick(new(ControllerButtons.LB, LeftY: 32767)); Check.Equal(0, h.Engine.PieSelected);
                h.Tick(new(LeftY: 32767)); Check.Equal(i + 1, h.Sink.Actions.Count);
            }
            Check.Equal(0, h.Sink.Y); h.Tick(new(LeftY: 32767)); Check.Equal(0, h.Sink.Y);
            h.Tick(default); h.Tick(new(LeftY: 32767)); Check.True(h.Sink.Y != 0);
        });
        foreach (var boundary in new[] { "cancel", "pause", "disconnect", "reset", "configure", "foreground" })
        {
            var reason = boundary;
            suite.Add($"Pie quick gesture release memory never survives {boundary}", () =>
            {
                var h = new PieHarness(); h.Tick(new(ControllerButtons.LB, LeftY: 32767)); h.Tick(new(ControllerButtons.LB)); Check.Equal(0, h.Engine.PieSelected);
                switch (reason)
                {
                    case "cancel": h.Tick(new(ControllerButtons.LB | ControllerButtons.Y)); break;
                    case "pause": h.Engine.SetPaused(true); h.Engine.SetPaused(false); break;
                    case "disconnect": h.Engine.Disconnect(); break;
                    case "reset": h.Engine.Reset(); break;
                    case "configure": h.Engine.Configure(h.Settings with { CursorStick = CursorStick.Right }); break;
                    case "foreground": h.Platform.Foreground = 2; break;
                }
                h.Tick(default); h.Tick(default); Check.Equal(0, h.Sink.Actions.Count);
                h.Platform.Foreground = 1;
                h.Tick(PieTests.State(Control.LB)); h.Tick(default); Check.Equal(0, h.Sink.Actions.Count);
            });
        }
    }
}
