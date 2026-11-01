namespace FullTilt.Core;

// Confined to the polling thread. UI writes only validated settings snapshots and commands.
public sealed class InputEngine
{
    private readonly IInputSink _sink;
    private readonly ActionCoordinator _actions;
    private readonly IPiePlatform? _piePlatform;
    private PiePresentation? _pie;
    private PieSelection? _selection;
    private int _opener;
    private bool _closingPie, _menuRoutedThisTick;
    private InputAction _pendingPieAction;
    private KeyboardChord? _pendingRecorded;
    private PieTarget _pieTarget;
    private readonly bool[] _preservedHolds = new bool[14];
    private readonly bool[] _contextArmed = new bool[4], _contextHeld = new bool[4];
    public bool PieOpen => _pie is not null;
    public int PieSelected => _selection?.Selected ?? -1;
    private AppSettings _settings = new AppSettings().Validated();
    private readonly bool[] _pressed = new bool[14];
    private readonly bool[] _armed = new bool[14];
    private FractionalMotion _cursor, _dpad, _scroll;
    private bool _leftArmed, _rightArmed, _dpadArmed;
    private bool _comboReady, _connected, _reservedActive;
    private ControllerButtons _lastDpad, _activeDpadAction;
    private const ControllerButtons DpadMask = ControllerButtons.DPadUp | ControllerButtons.DPadDown | ControllerButtons.DPadLeft | ControllerButtons.DPadRight;
    private static readonly ControllerButtons[] ButtonMap = [ControllerButtons.A, ControllerButtons.X, ControllerButtons.Y, ControllerButtons.B, ControllerButtons.LB, ControllerButtons.RB,
        0, 0, ControllerButtons.LeftThumb, ControllerButtons.RightThumb, ControllerButtons.DPadUp, ControllerButtons.DPadDown, ControllerButtons.DPadLeft, ControllerButtons.DPadRight];
    public bool Paused { get; private set; }
    public bool PrecisionActive => _actions.PrecisionActive;
    public bool SpeedBoostActive => _actions.SpeedBoostActive;
    public bool DragLocked => _actions.DragLocked;
    public bool CtrlActive => _actions.CtrlActive;
    public bool ShiftActive => _actions.ShiftActive;
    public bool AltActive => _actions.AltActive;
    public bool PendingDoubleClick => _actions.PendingDoubleClick;
    public bool AwaitingNeutral => _connected && !Paused && (!_leftArmed || !_rightArmed || !_dpadArmed || _armed.Any(static armed => !armed));

    public InputEngine(IInputSink sink, double doubleClickIntervalSeconds = .04, IPiePlatform? piePlatform = null)
    {
        _sink = sink; _actions = new(sink, doubleClickIntervalSeconds); _piePlatform = piePlatform;
    }
    public void Configure(AppSettings settings)
    {
        var next = settings.Validated();
        var shortcutChanged = next.PauseShortcut != _settings.PauseShortcut || next.ActiveProfileId != _settings.ActiveProfileId;
        if (shortcutChanged) { _comboReady = false; _reservedActive = false; }
        var changed = shortcutChanged || KeyboardChord.Fingerprint(next.RecordedBindings) != KeyboardChord.Fingerprint(_settings.RecordedBindings) || next.PieMenuSizePercent != _settings.PieMenuSizePercent || next.ControllerSlot != _settings.ControllerSlot || next.CursorStick != _settings.CursorStick || next.DPadMode != _settings.DPadMode ||
            next.TriggerPressThreshold != _settings.TriggerPressThreshold || next.TriggerReleaseThreshold != _settings.TriggerReleaseThreshold ||
            next.Mappings.Any(pair => _settings.Mappings[pair.Key] != pair.Value) ||
            !next.PieBindings.OrderBy(p => p.Key).SequenceEqual(_settings.PieBindings.OrderBy(p => p.Key)) ||
            !next.PieMenus.Select(MenuState).SequenceEqual(_settings.PieMenus.Select(MenuState));
        _settings = next;
        if (changed) Reset(); // Numeric edits must preserve controller-driven slider drags.
    }
    private static string MenuState(PieMenu m) => $"{m.Id}|{m.Name}|{m.SizePercent}|{string.Join(',', m.Slots)}|{string.Join(',', m.DPadActions)}|{KeyboardChord.Fingerprint(m.RecordedSlots)}|{KeyboardChord.Fingerprint(m.RecordedDPad)}";
    public void SetPaused(bool paused)
    {
        if (Paused == paused) return;
        Paused = paused; Reset();
    }
    public void Disconnect()
    {
        _connected = false; _comboReady = false; _reservedActive = false; Reset();
    }
    public void Reset()
    {
        _pie = null; _selection = null; _closingPie = false; _pendingPieAction = InputAction.None;
        _pendingRecorded = null;
        _piePlatform?.Present(null);
        Array.Clear(_preservedHolds);
        Array.Clear(_contextArmed); Array.Clear(_contextHeld);
        Array.Clear(_pressed); Array.Clear(_armed);
        _leftArmed = _rightArmed = _dpadArmed = false;
        _cursor.Reset(); _dpad.Reset(); _scroll.Reset(); _lastDpad = _activeDpadAction = 0;
        _actions.Reset();
    }
    public void Tick(ControllerState state, double elapsedSeconds)
    {
        _connected = true;
        var combo = _settings.PauseShortcut;
        var comboBits = state.Buttons & combo;
        if (comboBits == combo && !_reservedActive) { Reset(); _reservedActive = true; }
        if (comboBits == 0) { _comboReady = true; _reservedActive = false; }
        if (comboBits == combo && _comboReady) { _comboReady = false; SetPaused(!Paused); }
        if (Paused || _reservedActive) return;
        var dt = MotionMath.Elapsed(elapsedSeconds);
        if ((state.Buttons & DpadMask) == 0) _dpadArmed = true;
        if (!UpdateButtons(state, elapsedSeconds)) { Reset(); return; }
        if (!_menuRoutedThisTick) UpdateMovement(state, dt);
    }
    private bool UpdateButtons(ControllerState state, double dt)
    {
        var previousDpadAction = _activeDpadAction;
        var bits = state.Buttons & DpadMask;
        if (_settings.DPadMode != DPadMode.AssignedActions || !_dpadArmed) _activeDpadAction = 0;
        else
        {
            if ((_activeDpadAction & bits) == 0) _activeDpadAction = 0;
            // Retain the owner until released. Ambiguous new directions have no owner.
            if (_activeDpadAction == 0 && bits != 0 && ((int)bits & ((int)bits - 1)) == 0) _activeDpadAction = bits;
        }
        _menuRoutedThisTick = false;
        Span<InputAction> held = stackalloc InputAction[14];
        Span<InputAction> pressed = stackalloc InputAction[14];
        Span<bool> fresh = stackalloc bool[14];
        var recordedHolds = new List<KeyboardChord>();
        held.Clear(); pressed.Clear(); fresh.Clear();
        var opening = -1;
        for (var i = 0; i < _pressed.Length; i++)
        {
            var trigger = i == 6 ? state.LeftTrigger : state.RightTrigger;
            var wasPressed = _pressed[i];
            var neutral = i is 6 or 7 ? trigger <= _settings.TriggerReleaseThreshold : !state.Has(ButtonMap[i]);
            var down = i is 6 or 7 ? trigger >= (wasPressed ? _settings.TriggerReleaseThreshold + 1 : _settings.TriggerPressThreshold) : !neutral;
            // Track physical D-pad buttons even when arbitration gives another direction ownership.
            _pressed[i] = down;
            if (neutral) { _armed[i] = true; _preservedHolds[i] = false; }
            fresh[i] = down && !wasPressed;
            var action = _settings.Mappings[(Control)i];
            if (down && (_armed[i] || _preservedHolds[i]) && (i < 10 || _activeDpadAction == ButtonMap[i] || _preservedHolds[i]))
            {
                held[i] = action;
                var activationEdge = fresh[i] || (i >= 10 && _activeDpadAction != previousDpadAction);
                if (activationEdge && _armed[i]) pressed[i] = action;
                if ((_armed[i] || _preservedHolds[i]) && _settings.RecordedBindings.TryGetValue((Control)i, out var recording)) recordedHolds.Add(recording);
                if (activationEdge && _armed[i] && opening < 0 && _settings.PieBindings.ContainsKey((Control)i)) opening = i;
            }
        }
        if (_pie is null && !_closingPie && opening >= 0 && _piePlatform is not null)
        {
            var menu = _settings.PieMenus.First(m => m.Id == _settings.PieBindings[(Control)opening]);
            _pieTarget = _piePlatform.Capture();
            _opener = opening; _selection = new(menu.SegmentCount);
            _pie = new(menu, _pieTarget, -1, _actions.MouseHeld) { SizePercent = _settings.PieMenuSizePercent };
            for (var i = 0; i < 4; i++) { _contextArmed[i] = !_pressed[10 + i] || fresh[10 + i]; _contextHeld[i] = false; }
            for (var i = 0; i < 14; i++)
                _preservedHolds[i] &= _pressed[i] && (i < 10 || menu.DPadActions[i - 10] is null);
            _actions.SuspendSequences();
            _piePlatform.Present(_pie);
        }
        if (_pie is not null || _closingPie)
        {
            _menuRoutedThisTick = true;
            recordedHolds.Clear();
            if (_closingPie && _piePlatform!.Foreground != _pieTarget.Foreground) { _pendingPieAction = InputAction.None; _pendingRecorded = null; }
            for (var i = 0; i < 14; i++)
            {
                if (_pressed[i]) _armed[i] = false;
                held[i] = _pressed[i] && _preservedHolds[i] ? _settings.Mappings[(Control)i] : InputAction.None;
                if (i < 10 && _pressed[i] && _preservedHolds[i] && _settings.RecordedBindings.TryGetValue((Control)i, out var recording)) recordedHolds.Add(recording);
                // Release/cancel remains available after suppressed ordinary presses.
                if (fresh[i] && (i < 10 || _settings.DPadMode == DPadMode.AssignedActions) &&
                    (i < 10 || _pie?.Menu.DPadActions[i - 10] is null) && _settings.Mappings[(Control)i] == InputAction.ReleaseCancel) return false;
            }
            if (_pie is not null)
            {
                if (_piePlatform!.Foreground == 0 || _piePlatform.Foreground != _pieTarget.Foreground) DismissPie(InputAction.None);
                else
                {
                    var right = _settings.CursorStick == CursorStick.Right;
                    // Use actual polling elapsed time for the 100 ms release window,
                    // independently of the capped movement integration interval.
                    _selection!.Update(right ? state.RightX : state.LeftX, right ? state.RightY : state.LeftY, dt);
                    if (!_pressed[_opener])
                        DismissPie(_selection.Selected < 0 ? InputAction.None : _pie.Menu.Slots[_selection.Selected], _pie.Menu.RecordedSlots.GetValueOrDefault(_selection.Selected));
                    else
                    {
                        Span<InputAction> contextualPressed = stackalloc InputAction[4]; contextualPressed.Clear();
                        var contextualRecordings = new List<KeyboardChord>(recordedHolds);
                        for (var direction = 0; direction < 4; direction++)
                        {
                            var index = 10 + direction;
                            if (!_pressed[index]) { _contextArmed[direction] = true; _contextHeld[direction] = false; }
                            if (index == _opener || _pie.Menu.DPadActions[direction] is not { } contextual) continue;
                            if (fresh[index] && _contextArmed[direction])
                            {
                                _contextArmed[direction] = false;
                                _contextHeld[direction] = _actions.CanActivatePieDPad(contextual);
                                if (_contextHeld[direction]) contextualPressed[direction] = contextual;
                            }
                            if (_contextHeld[direction]) held[index] = contextual;
                        }
                        var mouseHeld = _actions.MouseHeld;
                        foreach (var action in held) mouseHeld |= ActionCatalog.Get(action).MouseButton is not null;
                        for (var direction = 0; direction < 4; direction++)
                        {
                            if (!_contextHeld[direction]) continue;
                            var index = 10 + direction;
                            if (_pie.Menu.RecordedDPad.TryGetValue(direction, out var recording))
                            {
                                if (mouseHeld) _contextHeld[direction] = false;
                                else contextualRecordings.Add(recording);
                            }
                            else if (KeyboardActions.SingleKey(held[index]) is not null && ActionCatalog.PieDisabledReason(held[index], mouseHeld) is not null)
                            { _contextHeld[direction] = false; held[index] = InputAction.None; }
                        }
                        if (!_actions.UpdatePieDPad(held, contextualPressed, dt, contextualRecordings)) return false;
                        var next = _pie with { Selected = _selection.Selected, MouseHeld = _actions.MouseHeld, DPadPressed = bits };
                        if (next != _pie || fresh[_opener]) { _pie = next; _piePlatform.Present(next); }
                    }
                }
            }
            if (_pie is null) _actions.Update(held, [], 0, recordedHolds); // Preserve pre-existing ordinary holds; contextual holds end on close.
            if (_closingPie && _piePlatform!.IsHidden)
            {
                _closingPie = false;
                var action = _pendingPieAction; _pendingPieAction = InputAction.None;
                var recorded = _pendingRecorded; _pendingRecorded = null;
                if (_piePlatform.Foreground == _pieTarget.Foreground && _pieTarget.Foreground != 0 && recorded is not null)
                    _actions.ActivateKeyboard(recorded, pie: true);
                if (_piePlatform.Foreground == _pieTarget.Foreground && _pieTarget.Foreground != 0 && action != InputAction.None)
                    return _actions.ActivatePie(action, held);
            }
            return true;
        }
        if (pressed.Contains(InputAction.ReleaseCancel)) return false;
        for (var i = 0; i < 14; i++) _preservedHolds[i] = ActionCatalog.Get(held[i]).MouseButton is not null ||
            (i < 10 && _pressed[i] && (_armed[i] || _preservedHolds[i]) &&
                (KeyboardActions.SingleKey(held[i]) is not null || _settings.RecordedBindings.ContainsKey((Control)i)));
        if (!_actions.Update(held, pressed, dt, recordedHolds)) return false;
        return true;
    }
    private void DismissPie(InputAction action, KeyboardChord? recorded = null)
    {
        _pendingRecorded = recorded;
        _pendingPieAction = action; _pie = null; _selection = null; _closingPie = true;
        _piePlatform!.Present(null); // Hide acknowledged before any selected action can dispatch.
        Array.Clear(_contextArmed); Array.Clear(_contextHeld);
        _actions.SuspendSequences();
        _leftArmed = _rightArmed = _dpadArmed = false;
        _cursor.Reset(); _scroll.Reset(); _dpad.Reset(); _lastDpad = _activeDpadAction = 0;
    }
    private void UpdateMovement(ControllerState state, double dt)
    {
        // Dead zones remain physical; cursor response/speed and scrolling follow the selected roles.
        if (MotionMath.Radial(state.LeftX, state.LeftY, _settings.LeftDeadZone, 1).Length == 0) _leftArmed = true;
        if (MotionMath.Radial(state.RightX, state.RightY, _settings.RightDeadZone, 1).Length == 0) _rightArmed = true;
        var rightCursor = _settings.CursorStick == CursorStick.Right;
        var cursorX = rightCursor ? state.RightX : state.LeftX;
        var cursorY = rightCursor ? state.RightY : state.LeftY;
        var scrollX = rightCursor ? state.LeftX : state.RightX;
        var scrollY = rightCursor ? state.LeftY : state.RightY;
        var cursorZone = rightCursor ? _settings.RightDeadZone : _settings.LeftDeadZone;
        var scrollZone = rightCursor ? _settings.LeftDeadZone : _settings.RightDeadZone;
        var cursorArmed = rightCursor ? _rightArmed : _leftArmed;
        var scrollArmed = rightCursor ? _leftArmed : _rightArmed;
        var bits = state.Buttons & DpadMask;
        var dpadCursor = _settings.DPadMode == DPadMode.CursorMovement;
        // Keep the two clicks aligned without sleeping or blocking action/cancellation processing.
        if (!_actions.PendingDoubleClick)
        {
            (int X, int Y) movement = default;
            if (dpadCursor && bits != 0)
            {
                if (_dpadArmed)
                {
                    var direction = MotionMath.DPad(bits);
                    var fresh = bits & ~_lastDpad;
                    // Releasing a diagonal component or uncanceling opposites never adds a tap.
                    if (direction.X == 0) fresh &= ~(ControllerButtons.DPadLeft | ControllerButtons.DPadRight);
                    if (direction.Y == 0) fresh &= ~(ControllerButtons.DPadUp | ControllerButtons.DPadDown);
                    var tap = MotionMath.DPad(fresh);
                    movement = tap.Length > 0
                        ? _dpad.TakeRounded(tap.X * _settings.DPadTapDistance, tap.Y * _settings.DPadTapDistance)
                        : _dpad.TakeRounded(direction.X * _settings.PrecisionSpeed * dt, direction.Y * _settings.PrecisionSpeed * dt);
                }
            }
            else if (cursorArmed)
            {
                var direction = MotionMath.Radial(cursorX, cursorY, cursorZone, _settings.ResponseCurve);
                var speed = PrecisionActive ? _settings.PrecisionCursorSpeed!.Value : SpeedBoostActive ? _settings.BoostedCursorSpeed!.Value : _settings.CursorSpeed;
                movement = _cursor.Take(direction.X * speed * dt, -direction.Y * speed * dt);
            }
            if (movement != (0, 0)) _sink.Move(movement.X, movement.Y);
        }
        _lastDpad = bits;
        if (!scrollArmed) return;
        var scroll = MotionMath.Radial(scrollX, scrollY, scrollZone, 1);
        var x = scroll.Length > 0 ? MotionMath.ScrollAxis(scrollX, scrollZone) : 0;
        var y = scroll.Length > 0 ? MotionMath.ScrollAxis(scrollY, scrollZone) : 0;
        if (x == 0) _scroll.ResetX();
        if (y == 0) _scroll.ResetY();
        var scrollSpeed = _settings.ScrollSpeed * (SpeedBoostActive ? _settings.ScrollBoostMultiplier : 1)
            * (_settings.ReverseScroll ? -1 : 1);
        var scrolling = _scroll.Take(x * scrollSpeed * 120 * dt, y * scrollSpeed * 120 * dt);
        if (scrolling != (0, 0)) _sink.Scroll(scrolling.X, scrolling.Y);
    }
}
