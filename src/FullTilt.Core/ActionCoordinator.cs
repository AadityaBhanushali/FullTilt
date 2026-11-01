namespace FullTilt.Core;

// All FullTilt mouse ownership and transient action state live here, on the input worker.
public sealed class ActionCoordinator(IInputSink sink, double doubleClickIntervalSeconds = .04)
{
    private readonly bool[] _mouseHeld = new bool[3];
    private bool _precisionHeld, _precisionToggle, _boostHeld, _boostToggle;
    private double _secondClickRemaining;
    public const double VolumeRepeatDelaySeconds = .300;
    public const double VolumeRepeatIntervalSeconds = .080;
    private readonly bool[] _volumeHeld = new bool[2];
    private readonly double[] _volumeRemaining = new double[2];
    private bool _keyboardHeld;
    public bool CtrlActive { get; private set; }
    public bool ShiftActive { get; private set; }
    public bool AltActive { get; private set; }
    public bool PrecisionActive => _precisionHeld || _precisionToggle;
    public bool SpeedBoostActive => (_boostHeld || _boostToggle) && !PrecisionActive;
    public bool DragLocked { get; private set; }
    public bool PendingDoubleClick { get; private set; }
    public bool MouseHeld => _mouseHeld.Any(down => down);
    public void ActivateKeyboard(KeyboardChord chord, bool pie = false) { if (!pie || !MouseHeld) sink.ExecuteKeyboard(chord); }
    public void SuspendSequences()
    {
        PendingDoubleClick = false; _secondClickRemaining = 0;
        Array.Clear(_volumeHeld); Array.Clear(_volumeRemaining);
        sink.CancelPendingActions();
    }
    // Context directions have physical owners, so holds and repeat actions use the
    // same coordinator as Keybindings. Never dispatch an unsafe shortcut during a drag.
    public bool CanActivatePieDPad(InputAction action) => action is InputAction.PrecisionHold or InputAction.SpeedBoostHold || ActionCatalog.PieDisabledReason(action, MouseHeld) is null;
    public bool UpdatePieDPad(Span<InputAction> held, Span<InputAction> pressed, double elapsed, IReadOnlyList<KeyboardChord>? recordings = null)
    {
        var mouseHeld = MouseHeld;
        foreach (var action in held) mouseHeld |= ActionCatalog.Get(action).MouseButton is not null;
        for (var i = 0; i < pressed.Length; i++)
            if (pressed[i] is not (InputAction.PrecisionHold or InputAction.SpeedBoostHold) && ActionCatalog.PieDisabledReason(pressed[i], mouseHeld) is not null)
                pressed[i] = InputAction.None;
        return Update(held, pressed, elapsed, recordings);
    }
    // A discrete menu activation shares the normal toggle/shortcut/click path and ownership.
    public bool ActivatePie(InputAction action, ReadOnlySpan<InputAction> physicallyHeld)
    {
        if (ActionCatalog.PieDisabledReason(action, MouseHeld) is not null) return true;
        if (action == InputAction.ReleaseCancel) return false;
        if (ActionCatalog.Get(action).MouseButton is { } button)
        {
            SetMouse(button, true); SetMouse(button, false); return true;
        }
        if (action is InputAction.VolumeUp or InputAction.VolumeDown) { sink.Execute(action); return true; }
        if (KeyboardActions.SingleKey(action) is not null) { sink.Execute(action); return true; }
        return Update(physicallyHeld, [action], 0, beginFrame: false);
    }

    // Returns false when Release/cancel wins the frame; the caller must reset neutral gates too.
    public bool Update(ReadOnlySpan<InputAction> held, ReadOnlySpan<InputAction> pressed, double elapsed, IReadOnlyList<KeyboardChord>? recordings = null, bool beginFrame = true)
    {
        if (pressed.Contains(InputAction.ReleaseCancel)) return false;
        if (beginFrame) sink.BeginKeyboardFrame();
        if (pressed.Contains(InputAction.ReleaseModifiers)) CtrlActive = ShiftActive = AltActive = false;
        else
        {
            if (pressed.Contains(InputAction.StickyCtrl)) CtrlActive = !CtrlActive;
            if (pressed.Contains(InputAction.StickyShift)) ShiftActive = !ShiftActive;
            if (pressed.Contains(InputAction.StickyAlt)) AltActive = !AltActive;
        }
        sink.SetStickyModifier(StickyModifier.Ctrl, CtrlActive);
        sink.SetStickyModifier(StickyModifier.Shift, ShiftActive);
        sink.SetStickyModifier(StickyModifier.Alt, AltActive);
        Span<bool> desired = stackalloc bool[3];
        desired.Clear();
        foreach (var action in held)
            if (ActionCatalog.Get(action).MouseButton is { } button) desired[(int)button] = true;
        _precisionHeld = held.Contains(InputAction.PrecisionHold);
        _boostHeld = held.Contains(InputAction.SpeedBoostHold);
        if (pressed.Contains(InputAction.SpeedBoostToggle)) _boostToggle = !_boostToggle;
        // Simultaneous duplicate toggle edges constitute one request, never a double toggle.
        if (pressed.Contains(InputAction.PrecisionToggle)) _precisionToggle = !_precisionToggle;
        if (pressed.Contains(InputAction.DragLock)) DragLocked = !DragLocked;
        desired[(int)MouseButton.Left] |= DragLocked;
        if (desired[0]) PendingDoubleClick = false;
        for (var i = 0; i < 3; i++) SetMouse((MouseButton)i, desired[i]);
        var keyboardActions = new List<InputAction>();
        foreach (var action in held)
            if (KeyboardActions.SingleKey(action) is not null && !keyboardActions.Contains(action)) keyboardActions.Add(action);
        var recorded = recordings ?? Array.Empty<KeyboardChord>();
        if (_keyboardHeld || keyboardActions.Count != 0 || recorded.Count != 0)
        {
            sink.SetKeyboardHolds(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(keyboardActions), recorded);
            _keyboardHeld = keyboardActions.Count != 0 || recorded.Count != 0;
        }

        if (PendingDoubleClick)
        {
            _secondClickRemaining -= MotionMath.Elapsed(elapsed);
            if (_secondClickRemaining <= 0) { ClickLeft(); PendingDoubleClick = false; }
        }
        else if (pressed.Contains(InputAction.DoubleClick) && !desired[0])
        {
            ClickLeft();
            if (doubleClickIntervalSeconds <= 0) ClickLeft();
            else
            {
                _secondClickRemaining = double.IsFinite(doubleClickIntervalSeconds) ? Math.Clamp(doubleClickIntervalSeconds, .001, .04) : .04;
                PendingDoubleClick = true;
            }
        }
        // Aggregate by action so duplicate bindings share one edge/repeat clock.
        foreach (var definition in ActionCatalog.All)
            if (definition.Capabilities == ActivationCapabilities.OneShot && pressed.Contains(definition.Action)
                && !keyboardActions.Contains(definition.Action)
                && definition.Action is not (InputAction.None or InputAction.ReleaseCancel or InputAction.ReleaseModifiers or InputAction.DoubleClick))
                sink.Execute(definition.Action);
        UpdateVolume(InputAction.VolumeUp, 0, held, elapsed);
        UpdateVolume(InputAction.VolumeDown, 1, held, elapsed);
        return true;
    }
    private void UpdateVolume(InputAction action, int index, ReadOnlySpan<InputAction> held, double elapsed)
    {
        if (!held.Contains(action)) { _volumeHeld[index] = false; _volumeRemaining[index] = 0; return; }
        if (!_volumeHeld[index])
        {
            _volumeHeld[index] = true; _volumeRemaining[index] = VolumeRepeatDelaySeconds;
            sink.Execute(action); return;
        }
        // No catch-up loop, including when invoked outside ControllerService's stall guard.
        if (!double.IsFinite(elapsed) || elapsed > .25) { _volumeRemaining[index] = VolumeRepeatDelaySeconds; return; }
        _volumeRemaining[index] -= Math.Max(0, elapsed);
        if (_volumeRemaining[index] <= .000000001)
        {
            sink.Execute(action); _volumeRemaining[index] = VolumeRepeatIntervalSeconds;
        }
    }
    private void ClickLeft() { SetMouse(MouseButton.Left, true); SetMouse(MouseButton.Left, false); }
    private void SetMouse(MouseButton button, bool down)
    {
        if (_mouseHeld[(int)button] == down) return;
        sink.SetMouseButton(button, down);
        _mouseHeld[(int)button] = down;
    }
    public void Reset()
    {
        _boostHeld = _boostToggle = _precisionHeld = _precisionToggle = DragLocked = PendingDoubleClick = false;
        CtrlActive = ShiftActive = AltActive = false;
        Array.Clear(_volumeHeld); Array.Clear(_volumeRemaining);
        _secondClickRemaining = 0;
        _keyboardHeld = false;
        Array.Clear(_mouseHeld);
        sink.ReleaseAll(); // Includes Windows key ownership and retains failed releases for retry.
    }
}
