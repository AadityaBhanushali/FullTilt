using FullTilt.Core;
using FullTilt.Windows;

static class PauseCaptureTests
{
    public static void Register(TestSuite suite)
    {
        suite.Add("Capture ignores held entry and reviews simultaneous chord only after release", () =>
        {
            var capture = new PauseShortcutCapture();
            capture.Tick(ControllerButtons.A); capture.Tick(ControllerButtons.A | ControllerButtons.B);
            Check.Equal(ControllerButtons.None, capture.Buttons);
            capture.Tick(ControllerButtons.None); capture.Tick(ControllerButtons.LB);
            capture.Tick(ControllerButtons.LB | ControllerButtons.RB); capture.Tick(ControllerButtons.RB);
            Check.True(!capture.Complete); capture.Tick(ControllerButtons.None);
            Check.True(capture.Valid); Check.Equal(ControllerButtons.LB | ControllerButtons.RB, capture.Buttons);
        });
        suite.Add("Capture rejects sequential single buttons, five buttons and opposite D-pad", () =>
        {
            foreach (var mask in new[] { ControllerButtons.A, ControllerButtons.A | ControllerButtons.B | ControllerButtons.X | ControllerButtons.Y | ControllerButtons.LB, ControllerButtons.DPadUp | ControllerButtons.DPadDown })
            {
                var capture = new PauseShortcutCapture(); capture.Tick(0); capture.Tick(mask); capture.Tick(ControllerButtons.A); capture.Tick(0);
                Check.True(capture.Complete && !capture.Valid);
            }
            var sequential = new PauseShortcutCapture(); sequential.Tick(0); sequential.Tick(ControllerButtons.A); sequential.Tick(ControllerButtons.B); sequential.Tick(0); Check.True(!sequential.Valid);
        });
        suite.Add("Capture worker releases held input and suppresses actions through review then rearms", () =>
        {
            var source = new FakeSource(); var sink = new FakeSink(); source.Set(0, default);
            using var service = new ControllerService(new(), source, sink); service.Start();
            Check.Eventually(() => !service.Status.AwaitingNeutral);
            source.Set(0, new(ControllerButtons.A)); Check.Eventually(() => sink.Held[0]);
            service.BeginShortcutCapture(); Check.Eventually(() => !sink.Held[0]);
            source.Set(0, default); Thread.Sleep(50);
            source.Set(0, new(ControllerButtons.B | ControllerButtons.Y)); Check.Eventually(() => service.ShortcutCapture.Buttons != 0);
            Check.True(!sink.Held.Any(held => held) && sink.Actions.IsEmpty); source.Set(0, default);
            Check.Eventually(() => service.ShortcutCapture.Complete);
            source.Set(0, new(ControllerButtons.A)); Thread.Sleep(50); Check.True(!sink.Held[0]);
            service.EndShortcutCapture(); Check.Eventually(() => !service.ShortcutCapture.Active); Thread.Sleep(50); Check.True(!sink.Held[0]);
            source.Set(0, default); Check.Eventually(() => !service.Status.AwaitingNeutral);
            source.Set(0, new(ControllerButtons.A)); Check.Eventually(() => sink.Held[0]);
        });
        suite.Add("Capture worker disconnect resets a partial chord and cancellation preserves pause", () =>
        {
            var source = new FakeSource(); source.Set(0, default); var sink = new FakeSink();
            using var service = new ControllerService(new(), source, sink); service.Start(); Check.Eventually(() => service.Status.ActiveSlot == 0);
            service.SetPaused(true); Check.Eventually(() => service.Status.Paused);
            service.BeginShortcutCapture(); Thread.Sleep(50); source.Set(0, new(ControllerButtons.LB | ControllerButtons.RB)); Check.Eventually(() => service.ShortcutCapture.Buttons != 0);
            source.Remove(0); Check.Eventually(() => service.Status.ActiveSlot == -1);
            Check.True(!service.ShortcutCapture.Complete && service.ShortcutCapture.Buttons == 0);
            service.EndShortcutCapture(); Check.Eventually(() => !service.ShortcutCapture.Active); Check.True(service.Status.Paused);
        });
        suite.Add("Capture cleanup retains a failed release and shutdown clears fake ownership", () =>
        {
            var source = new FakeSource(); source.Set(0, default); var sink = new ReleaseFailureSink();
            var service = new ControllerService(new(), source, sink);
            try
            {
                service.Start(); Check.Eventually(() => !service.Status.AwaitingNeutral);
                source.Set(0, new(ControllerButtons.A)); Check.Eventually(() => sink.Inner.Held[0]);
                sink.FailRelease = true; service.BeginShortcutCapture(); Check.Eventually(() => sink.Failures == 1 && !sink.Inner.Held[0]);
                source.Set(0, default); Thread.Sleep(350);
                source.Set(0, new(ControllerButtons.B | ControllerButtons.Y)); Check.Eventually(() => service.ShortcutCapture.Buttons != 0);
                Check.True(sink.Inner.Actions.IsEmpty && !sink.Inner.Held.Any(held => held));
            }
            finally { service.Dispose(); }
            Check.True(!sink.Inner.Held.Any(held => held));
            Check.True(!service.ShortcutCapture.Active); service.EndShortcutCapture();
        });
    }
    private sealed class ReleaseFailureSink : IInputSink
    {
        public readonly FakeSink Inner = new();
        public volatile bool FailRelease;
        public int Failures;
        public void Move(int x, int y) => Inner.Move(x, y);
        public void Scroll(int x, int y) => Inner.Scroll(x, y);
        public void SetMouseButton(MouseButton button, bool down)
        {
            Inner.SetMouseButton(button, down);
        }
        public void Execute(InputAction action) => Inner.Execute(action);
        public void SetKeyboardHolds(ReadOnlySpan<InputAction> actions, IReadOnlyList<KeyboardChord> recordings) => Inner.SetKeyboardHolds(actions, recordings);
        public void SetStickyModifier(StickyModifier modifier, bool active) => Inner.SetStickyModifier(modifier, active);
        public void ReleaseAll()
        {
            if (FailRelease) { FailRelease = false; Interlocked.Increment(ref Failures); throw new System.ComponentModel.Win32Exception(5); }
            Inner.ReleaseAll();
        }
    }
}
