namespace FullTilt.Windows;

public interface ITouchKeyboardBackend
{
    Task<bool> ObserveAsync(CancellationToken cancellation);
    Task EnsureStartedAsync(CancellationToken cancellation);
    Task ToggleAsync(CancellationToken cancellation);
}
public interface IKeyboardClock
{
    double Seconds { get; }
    Task DelayAsync(CancellationToken cancellation);
}
internal sealed class KeyboardClock : IKeyboardClock
{
    public double Seconds => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
    public Task DelayAsync(CancellationToken cancellation) => Task.Delay(100, cancellation);
}

// One async pump; pending fresh presses coalesce by parity after the current transition settles.
// Each operation re-observes visibility. No cached window handles or focus changes.
public sealed class TouchKeyboardController(ITouchKeyboardBackend backend, IKeyboardClock? clock = null) : IDisposable
{
    public const double OperationTimeoutSeconds = 8;
    private readonly IKeyboardClock _clock = clock ?? new KeyboardClock();
    private readonly object _gate = new();
    private bool _pending, _running, _disposed;
    private CancellationTokenSource? _operation;
    private Task _completion = Task.CompletedTask;
    private string? _error;
    public string? Error => Volatile.Read(ref _error);
    public Task Completion { get { lock (_gate) return _completion; } }
    public void Request()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _pending = !_pending;
            if (!_running) Start();
        }
    }
    private void Start() { _running = true; _completion = Task.Run(RunAsync); }
    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                CancellationTokenSource operation;
                lock (_gate)
                {
                    if (_disposed || !_pending) return;
                    _pending = false;
                    _operation = operation = new(TimeSpan.FromSeconds(OperationTimeoutSeconds));
                }
                using (operation)
                {
                    try
                    {
                        Volatile.Write(ref _error, null);
                        var cancellation = operation.Token;
                        var deadline = _clock.Seconds + OperationTimeoutSeconds;
                        var visible = await backend.ObserveAsync(cancellation).ConfigureAwait(false);
                        cancellation.ThrowIfCancellationRequested();
                        var desired = !visible;
                        if (desired)
                        {
                            await backend.EnsureStartedAsync(cancellation).ConfigureAwait(false);
                            await _clock.DelayAsync(cancellation).ConfigureAwait(false);
                            visible = await backend.ObserveAsync(cancellation).ConfigureAwait(false);
                        }
                        cancellation.ThrowIfCancellationRequested();
                        if (visible != desired) await backend.ToggleAsync(cancellation).ConfigureAwait(false);
                        var confirmations = 0;
                        while (confirmations < 2)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (_clock.Seconds >= deadline) throw new TimeoutException();
                            await _clock.DelayAsync(cancellation).ConfigureAwait(false);
                            visible = await backend.ObserveAsync(cancellation).ConfigureAwait(false);
                            confirmations = visible == desired ? confirmations + 1 : 0;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        lock (_gate)
                            if (!_disposed && ReferenceEquals(_operation, operation))
                            { _pending = false; Volatile.Write(ref _error, "Touch keyboard timed out. Try the Windows taskbar keyboard button, then retry."); }
                    }
                    catch (Exception ex) when (ex is TimeoutException or System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
                    {
                        lock (_gate) _pending = false;
                        Volatile.Write(ref _error, ex is TimeoutException
                            ? "Touch keyboard did not reach the requested visibility. Try the Windows taskbar keyboard button, then retry."
                            : "Touch keyboard unavailable: " + ex.Message);
                    }
                    finally { lock (_gate) if (ReferenceEquals(_operation, operation)) _operation = null; }
                }
            }
        }
        finally { lock (_gate) { _running = false; if (!_disposed && _pending) Start(); } }
    }
    public void CancelPending()
    {
        lock (_gate)
        {
            _pending = false;
            var operation = _operation; _operation = null;
            operation?.Cancel();
        }
    }
    public void Dispose() { lock (_gate) { _disposed = true; CancelPending(); } }
}
