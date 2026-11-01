using FullTilt.Windows;

static class TouchKeyboardTests
{
    private static void Add(TestSuite suite, string name, Func<Task> test) => suite.Add(name, () => test().WaitAsync(TimeSpan.FromSeconds(4)).GetAwaiter().GetResult());
    public static void Register(TestSuite suite)
    {
        Add(suite, "Touch keyboard hidden shows and visible hides without a focus API", async () =>
        {
            var backend = new FakeTouchKeyboard(); using var controller = new TouchKeyboardController(backend, new FakeKeyboardClock());
            controller.Request(); await controller.Completion; Check.True(backend.Visible); Check.Equal(1, backend.Launches);
            controller.Request(); await controller.Completion; Check.True(!backend.Visible); Check.Equal(2, backend.Toggles); Check.True(controller.Error is null);
        });
        Add(suite, "Touch visibility follows external dismissal and external reopening", async () =>
        {
            var backend = new FakeTouchKeyboard(); using var controller = new TouchKeyboardController(backend, new FakeKeyboardClock());
            controller.Request(); await controller.Completion;
            backend.Visible = false; controller.Request(); await controller.Completion; Check.True(backend.Visible); Check.Equal(1, backend.Launches);
            backend.Visible = true; controller.Request(); await controller.Completion; Check.True(!backend.Visible);
            backend.Running = false; controller.Request(); await controller.Completion; Check.Equal(2, backend.Launches);
        });
        Add(suite, "A running hidden touch process is shown without another launch", async () =>
        {
            var backend = new FakeTouchKeyboard { Running = true }; using var controller = new TouchKeyboardController(backend, new FakeKeyboardClock());
            controller.Request(); await controller.Completion; Check.True(backend.Visible); Check.Equal(0, backend.Launches); Check.Equal(1, backend.Toggles);
        });
        Add(suite, "Startup that shows the pane is not accidentally toggled closed", async () =>
        {
            var backend = new FakeTouchKeyboard { ShowOnStart = true }; using var controller = new TouchKeyboardController(backend, new FakeKeyboardClock());
            controller.Request(); await controller.Completion; Check.True(backend.Visible); Check.Equal(0, backend.Toggles);
        });
        foreach (var extraPresses in new[] { 1, 2 })
            Add(suite, $"{extraPresses} rapid presses during opening coalesce deterministically", async () =>
            {
                var backend = new FakeTouchKeyboard { BlockEnsure = true }; using var controller = new TouchKeyboardController(backend, new FakeKeyboardClock());
                controller.Request(); await backend.Entered.Task;
                for (var i = 0; i < extraPresses; i++) controller.Request();
                backend.Continue.TrySetResult(); await controller.Completion;
                Check.Equal(extraPresses == 2, backend.Visible); Check.Equal(1, backend.Launches); Check.Equal(1, backend.MaxConcurrent);
            });
        Add(suite, "Press during closing shows again after the close completes", async () =>
        {
            var backend = new FakeTouchKeyboard { Visible = true, Running = true, BlockToggle = true };
            using var controller = new TouchKeyboardController(backend, new FakeKeyboardClock()); controller.Request(); await backend.Entered.Task;
            controller.Request(); backend.Continue.TrySetResult(); await controller.Completion;
            Check.True(backend.Visible); Check.Equal(2, backend.Toggles); Check.Equal(1, backend.MaxConcurrent);
        });
        foreach (var operation in new[] { "observe", "ensure", "toggle" })
            Add(suite, $"Touch backend {operation} failure reports once and permits explicit retry", async () =>
            {
                var backend = new FakeTouchKeyboard { Fail = operation }; using var controller = new TouchKeyboardController(backend, new FakeKeyboardClock());
                controller.Request(); await controller.Completion; Check.True(controller.Error is not null);
                backend.Fail = null; controller.Request(); await controller.Completion; Check.True(controller.Error is null && backend.Visible);
            });
        Add(suite, "Touch visibility timeout is bounded with no blind toggle retry loop", async () =>
        {
            var backend = new FakeTouchKeyboard { IgnoreToggle = true }; var clock = new FakeKeyboardClock(); using var controller = new TouchKeyboardController(backend, clock);
            controller.Request(); await controller.Completion; Check.True(controller.Error is not null); Check.Equal(1, backend.Toggles); Check.True(clock.Seconds < 9);
        });
        foreach (var dispose in new[] { false, true })
            Add(suite, $"Touch {(dispose ? "shutdown" : "cancel")} during pending startup prevents later toggle", async () =>
            {
                var backend = new FakeTouchKeyboard { BlockEnsure = true }; using var controller = new TouchKeyboardController(backend, new FakeKeyboardClock());
                controller.Request(); await backend.Entered.Task; controller.Request();
                if (dispose) controller.Dispose(); else controller.CancelPending();
                backend.Continue.TrySetResult(); await controller.Completion; Check.Equal(0, backend.Toggles); Check.True(controller.Error is null);
                if (dispose) { controller.Request(); await controller.Completion; Check.Equal(0, backend.Toggles); }
            });
        Add(suite, "Touch helper timeout is surfaced without uncontrolled retries", async () =>
        {
            var backend = new FakeTouchKeyboard { Fail = "timeout" }; using var controller = new TouchKeyboardController(backend, new FakeKeyboardClock());
            controller.Request(); await controller.Completion; Check.True(controller.Error is not null); Check.Equal(0, backend.Toggles);
        });
        suite.Add("Touch backend retains Update 2 keyboard ID across pie schema migration", () =>
        {
            Check.Equal("ToggleKeyboard", FullTilt.Core.ActionCatalog.Get(FullTilt.Core.InputAction.ToggleKeyboard).Id);
            Check.Equal(33, (int)FullTilt.Core.InputAction.ToggleKeyboard); Check.Equal(17, FullTilt.Core.AppSettings.CurrentVersion);
        });
    }
}
sealed class FakeKeyboardClock : IKeyboardClock
{
    public double Seconds { get; private set; }
    public Task DelayAsync(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); Seconds += .1; return Task.CompletedTask; }
}
sealed class FakeTouchKeyboard : ITouchKeyboardBackend
{
    public bool Visible, Running, ShowOnStart, BlockEnsure, BlockToggle, IgnoreToggle;
    public int Launches, Toggles, Concurrent, MaxConcurrent;
    public string? Fail;
    public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private void Failure(string operation)
    {
        if (Fail == "timeout") throw new TimeoutException();
        if (Fail == operation) throw new InvalidOperationException("simulated " + operation);
    }
    public Task<bool> ObserveAsync(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); Failure("observe"); return Task.FromResult(Visible); }
    public async Task EnsureStartedAsync(CancellationToken cancellation)
    {
        Concurrent++; MaxConcurrent = Math.Max(MaxConcurrent, Concurrent);
        try
        {
            Failure("ensure");
            if (BlockEnsure) { Entered.TrySetResult(); await Continue.Task.WaitAsync(cancellation); }
            cancellation.ThrowIfCancellationRequested();
            if (!Running) { Running = true; Launches++; if (ShowOnStart) Visible = true; }
        }
        finally { Concurrent--; }
    }
    public async Task ToggleAsync(CancellationToken cancellation)
    {
        Concurrent++; MaxConcurrent = Math.Max(MaxConcurrent, Concurrent);
        try
        {
            Failure("toggle");
            if (BlockToggle) { Entered.TrySetResult(); await Continue.Task.WaitAsync(cancellation); }
            cancellation.ThrowIfCancellationRequested(); Toggles++; if (!IgnoreToggle) Visible = !Visible;
        }
        finally { Concurrent--; }
    }
}
