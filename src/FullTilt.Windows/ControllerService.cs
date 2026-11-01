using System.Diagnostics;
using FullTilt.Core;

namespace FullTilt.Windows;

public sealed record ControllerStatus(int ActiveSlot, int ConnectedMask, bool Paused, bool AwaitingNeutral, string? Error, bool PrecisionActive = false, bool DragLocked = false, bool CtrlActive = false, bool ShiftActive = false, bool SpeedBoostActive = false, bool AltActive = false)
{
    public static ControllerStatus Initial { get; } = new(-1, 0, false, true, null);
}

public sealed class ControllerService : IDisposable
{
    public sealed record CaptureState(bool Active, bool Complete, ControllerButtons Buttons);
    private CaptureState _captureStatus = new(false, false, ControllerButtons.None);
    private int _captureRequest;
    private int _keyboardRecording;
    private volatile bool _keyboardRecordingReady;
    public bool KeyboardRecordingReady => _keyboardRecordingReady;
    public void BeginKeyboardRecording() { if (_disposed) return; Volatile.Write(ref _keyboardRecording, 1); _wake.Set(); }
    public void EndKeyboardRecording() { if (_disposed) return; Volatile.Write(ref _keyboardRecording, 0); _keyboardRecordingReady = false; _wake.Set(); }
    public CaptureState ShortcutCapture => Volatile.Read(ref _captureStatus);
    public void BeginShortcutCapture() { if (_disposed) return; Volatile.Write(ref _captureStatus, new(true, false, ControllerButtons.None)); Interlocked.Exchange(ref _captureRequest, 1); _wake.Set(); }
    public void EndShortcutCapture() { if (_disposed) return; Interlocked.Exchange(ref _captureRequest, 2); _wake.Set(); }
    private readonly IControllerSource _source;
    private readonly IInputSink _sink;
    private readonly IPiePlatform? _piePlatform;
    private readonly double _doubleClickInterval;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private AppSettings _settings;
    private ControllerStatus _status = ControllerStatus.Initial;
    private int _pauseRequest = -1, _resetRequest;
    private volatile bool _stopping;
    private bool _started, _disposed;
    public ControllerStatus Status => Volatile.Read(ref _status);

    public ControllerService(AppSettings settings, IControllerSource? source = null, IInputSink? sink = null, IPiePlatform? piePlatform = null)
    {
        _settings = settings.Validated();
        _piePlatform = piePlatform;
        _source = source ?? new XInputSource();
        _sink = sink ?? new WindowsInputSink();
        _doubleClickInterval = sink is null ? ClickIntervalForSystemTiming(NativeMethods.GetDoubleClickTime()) : .04;
        _thread = new Thread(Run) { IsBackground = true, Name = "FullTilt input" };
    }
    public void Start() { if (_started) return; _started = true; _thread.Start(); }
    public void Configure(AppSettings settings) { Volatile.Write(ref _settings, settings.Validated()); _wake.Set(); }
    public void SetPaused(bool value) { Interlocked.Exchange(ref _pauseRequest, value ? 1 : 0); _wake.Set(); }
    public void ResetInput() { Interlocked.Exchange(ref _resetRequest, 1); _wake.Set(); }
    // Very short Windows thresholds cannot reliably span two worker polls; emit both pairs immediately.
    public static double ClickIntervalForSystemTiming(uint milliseconds) => milliseconds < 64 ? 0 : Math.Min(.04, milliseconds / 4000.0);

    private void Run()
    {
        var engine = new InputEngine(_sink, _doubleClickInterval, _piePlatform);
        AppSettings? applied = null;
        var stopwatch = Stopwatch.StartNew();
        var last = stopwatch.Elapsed.TotalSeconds;
        var nextScan = 0.0;
        var mask = 0;
        var active = -1;
        string? error = null;
        var retryCleanup = false;
        PauseShortcutCapture? capture = null;
        var keyboardSuppressed = false;
        try
        {
            while (!_stopping)
            {
                var now = stopwatch.Elapsed.TotalSeconds;
                var elapsed = now - last;
                last = now;
                var delay = 8;
                try
                {
                    if (retryCleanup) { engine.Reset(); retryCleanup = false; }
                    var suppressKeyboard = Volatile.Read(ref _keyboardRecording) != 0;
                    if (suppressKeyboard != keyboardSuppressed) { keyboardSuppressed = suppressKeyboard; engine.Disconnect(); elapsed = 0; }
                    _keyboardRecordingReady = keyboardSuppressed;
                    var captureRequest = Interlocked.Exchange(ref _captureRequest, 0);
                    if (captureRequest != 0)
                    {
                        // Set up suppression before cleanup so failed partial releases keep retrying safely.
                        capture = captureRequest == 1 ? new PauseShortcutCapture() : null;
                        engine.Reset(); elapsed = 0;
                        Volatile.Write(ref _captureStatus, new(capture is not null, false, ControllerButtons.None));
                    }
                    var settings = Volatile.Read(ref _settings);
                    if (!ReferenceEquals(applied, settings))
                    {
                        engine.Configure(settings);
                        if (applied?.ControllerSlot != settings.ControllerSlot) { engine.Disconnect(); active = -1; nextScan = 0; elapsed = 0; }
                        applied = settings;
                    }
                    var pause = Interlocked.Exchange(ref _pauseRequest, -1);
                    if (pause >= 0) { capture = null; Volatile.Write(ref _captureStatus, new(false, false, ControllerButtons.None)); engine.SetPaused(pause == 1); error = null; elapsed = 0; }
                    var reset = Interlocked.Exchange(ref _resetRequest, 0) == 1;
                    if (reset || elapsed > 0.25)
                    {
                        if (reset) { capture = null; Volatile.Write(ref _captureStatus, new(false, false, ControllerButtons.None)); }
                        else if (capture is not null) { capture = new(); Volatile.Write(ref _captureStatus, new(true, false, ControllerButtons.None)); }
                        engine.Reset(); elapsed = 0; nextScan = 0;
                    }
                    if (now >= nextScan)
                    {
                        mask = 0;
                        for (var i = 0; i < 4; i++) if (_source.TryGetState(i, out _)) mask |= 1 << i;
                        nextScan = now + (active < 0 ? 0.25 : 1.0);
                    }
                    var chosen = SelectSlot(settings.ControllerSlot, active, mask);
                    if (chosen != active) { engine.Disconnect(); active = chosen; elapsed = 0; if (capture is not null) { capture = new(); Volatile.Write(ref _captureStatus, new(true, false, ControllerButtons.None)); } }
                    if (active >= 0)
                    {
                        if (_source.TryGetState(active, out var state))
                        {
                            var wasPaused = engine.Paused;
                            if (keyboardSuppressed) { /* Recorder owns input; preserve paused state and require neutral on exit. */ }
                            else if (capture is not null)
                            {
                                capture.Tick(state.Buttons);
                                Volatile.Write(ref _captureStatus, new(true, capture.Complete, capture.Buttons));
                            }
                            else engine.Tick(state, elapsed);
                            if (wasPaused && !engine.Paused) error = null;
                        }
                        else { mask &= ~(1 << active); active = -1; engine.Disconnect(); nextScan = 0; if (capture is not null) { capture = new(); Volatile.Write(ref _captureStatus, new(true, false, ControllerButtons.None)); } }
                    }
                    if (engine.Paused) delay = 16;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
                {
                    _keyboardRecordingReady = false;
                    error = ex.Message;
                    try { engine.SetPaused(true); engine.Reset(); }
                    catch (System.ComponentModel.Win32Exception) { retryCleanup = true; }
                    delay = 250;
                }
                PublishStatus(active, mask, engine.Paused, engine.AwaitingNeutral, error ?? _sink.ActionError ?? _piePlatform?.Error, engine.PrecisionActive, engine.DragLocked, engine.CtrlActive, engine.ShiftActive, engine.SpeedBoostActive, engine.AltActive);
                _wake.WaitOne(retryCleanup ? 250 : active < 0 ? 250 : delay);
            }
        }
        finally
        {
            _keyboardRecordingReady = false;
            Volatile.Write(ref _captureStatus, new(false, false, ControllerButtons.None));
            // Bounded retries, no blocking feedback or spin loop.
            for (var attempt = 0; attempt < 3; attempt++)
                try { engine.Reset(); PublishStatus(-1, mask, true, false, error); break; }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    PublishStatus(-1, mask, true, true, ex.Message);
                    if (attempt < 2) _wake.WaitOne(50);
                }
        }
    }

    public static int SelectSlot(int requested, int active, int connectedMask)
    {
        if (requested >= 0) return (connectedMask & (1 << requested)) != 0 ? requested : -1;
        if (active >= 0 && (connectedMask & (1 << active)) != 0) return active;
        for (var i = 0; i < 4; i++) if ((connectedMask & (1 << i)) != 0) return i;
        return -1;
    }

    private void PublishStatus(int active, int mask, bool paused, bool neutral, string? error, bool precision = false, bool drag = false, bool ctrl = false, bool shift = false, bool boost = false, bool alt = false)
    {
        var old = Status;
        if (old.ActiveSlot != active || old.ConnectedMask != mask || old.Paused != paused || old.AwaitingNeutral != neutral || old.Error != error || old.PrecisionActive != precision || old.DragLocked != drag || old.CtrlActive != ctrl || old.ShiftActive != shift || old.SpeedBoostActive != boost || old.AltActive != alt)
            Volatile.Write(ref _status, new(active, mask, paused, neutral, error, precision, drag, ctrl, shift, boost, alt));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _stopping = true; _wake.Set();
        if (_started) _thread.Join(); // Worker calls only nonblocking native APIs plus bounded waits.
        if (_sink is IDisposable disposable) disposable.Dispose();
        _wake.Dispose();
    }
}
