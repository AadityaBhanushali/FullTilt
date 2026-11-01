namespace FullTilt.Core;

public sealed record AppSettings
{
    // New persisted fields/IDs need a version boundary so older builds preserve the file.
    public const int CurrentVersion = 17;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<FullTiltProfile>? Profiles { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ActiveProfileId { get; init; }
    public int Version { get; init; } = CurrentVersion;
    public double CursorSpeed { get; init; } = 1200;
    // Independent absolute stick speeds. Null occurs only before legacy conversion.
    public const double MaximumCursorSpeed = int.MaxValue;
    public double? PrecisionCursorSpeed { get; init; }
    public double? BoostedCursorSpeed { get; init; }
    public double PrecisionSpeed { get; init; } = 600;
    public double DPadTapDistance { get; init; } = 5;
    public double SpeedBoostMultiplier { get; init; } = 1.5;
    public double PrecisionPercent { get; init; } = 50;
    public CursorStick CursorStick { get; init; } = CursorStick.Left;
    public DPadMode DPadMode { get; init; } = DPadMode.CursorMovement;
    public ControllerButtons PauseShortcut { get; init; } = PauseShortcutRules.Default;
    public bool ShowStateIndicator { get; init; } = true;
    public double ScrollSpeed { get; init; } = 8; // Wheel notches per second; one notch = 120 Windows units.
    public double ScrollBoostMultiplier { get; init; } = 1; // Missing older data keeps previous scrolling behavior.
    public bool ReverseScroll { get; init; }
    public double LeftDeadZone { get; init; } = 0.18;
    public double RightDeadZone { get; init; } = 0.22;
    public double ResponseCurve { get; init; } = 1.8;
    public int TriggerPressThreshold { get; init; } = 140;
    public int TriggerReleaseThreshold { get; init; } = 100;
    public int ControllerSlot { get; init; } = -1;
    public bool TrayHintShown { get; init; }
    public Dictionary<Control, InputAction> Mappings { get; init; } = DefaultMappings();
    public Dictionary<Control, KeyboardChord> RecordedBindings { get; init; } = [];
    public List<PieMenu> PieMenus { get; init; } = [];
    public double PieMenuSizePercent { get; init; } = 100; // App-wide desktop size, retained across profiles.
    public Dictionary<Control, Guid> PieBindings { get; init; } = [];
    public int PiePresetMigration { get; init; }
    public static AppSettings FreshInstallation() => FactoryDefaults.Create();

    public static Dictionary<Control, InputAction> DefaultMappings() => new()
    {
        [Control.A] = InputAction.LeftClick, [Control.X] = InputAction.RightClick,
        [Control.Y] = InputAction.MiddleClick, [Control.B] = InputAction.TaskView,
        [Control.LB] = InputAction.None, [Control.RB] = InputAction.None,
        [Control.LT] = InputAction.None, [Control.RT] = InputAction.None,
        [Control.L3] = InputAction.None, [Control.R3] = InputAction.None,
        [Control.DPadUp] = InputAction.None, [Control.DPadDown] = InputAction.None,
        [Control.DPadLeft] = InputAction.None, [Control.DPadRight] = InputAction.None
    };

    public AppSettings Validated()
    {
        var mappings = DefaultMappings();
        if (Mappings is not null)
            foreach (var (key, value) in Mappings)
                if (Enum.IsDefined(key) && ActionCatalog.Contains(value)) mappings[key] = value;
        var press = Math.Clamp(TriggerPressThreshold, 2, 255);
        var menus = PieMenu.Validate(PieMenus);
        var bindings = new Dictionary<Control, Guid>();
        foreach (var (control, id) in PieBindings ?? [])
            if (Enum.IsDefined(control))
            {
                mappings[control] = InputAction.None;
                if (menus.Any(menu => menu.Id == id)) bindings[control] = id;
            }
        var recorded = KeyboardChord.Clean(RecordedBindings, c => Enum.IsDefined(c) && mappings[c] == InputAction.None && !bindings.ContainsKey(c));
        var normal = Version < 17 ? InRange(CursorSpeed, 50, 4000, 1200) : InRange(CursorSpeed, 1, MaximumCursorSpeed, 1200);
        var legacyPrecision = Math.Round(InRange(PrecisionPercent, 5, 100, 50));
        var legacyBoost = InRange(SpeedBoostMultiplier, 1, 10, 1.5);
        var precisionFallback = InRange(normal * legacyPrecision / 100, 1, MaximumCursorSpeed, 600);
        var boostFallback = InRange(normal * legacyBoost, 1, MaximumCursorSpeed, 1800);
        return this with
        {
            Version = CurrentVersion,
            PauseShortcut = PauseShortcutRules.IsValid(PauseShortcut) ? PauseShortcut : PauseShortcutRules.Default,
            CursorSpeed = normal,
            PrecisionCursorSpeed = InRange(PrecisionCursorSpeed ?? precisionFallback, 1, MaximumCursorSpeed, precisionFallback),
            BoostedCursorSpeed = InRange(BoostedCursorSpeed ?? boostFallback, 1, MaximumCursorSpeed, boostFallback),
            PrecisionSpeed = InRange(PrecisionSpeed, 10, 2000, 600),
            DPadTapDistance = Math.Round(InRange(DPadTapDistance, 1, 50, 5)),
            SpeedBoostMultiplier = InRange(SpeedBoostMultiplier, 1, 10, 1.5),
            PrecisionPercent = Math.Round(InRange(PrecisionPercent, 5, 100, 50)),
            CursorStick = Enum.IsDefined(CursorStick) ? CursorStick : CursorStick.Left,
            DPadMode = Enum.IsDefined(DPadMode) ? DPadMode : DPadMode.CursorMovement,
            ScrollSpeed = InRange(ScrollSpeed, 0.5, 30, 8),
            ScrollBoostMultiplier = InRange(ScrollBoostMultiplier, 1, 10, 1),
            LeftDeadZone = InRange(LeftDeadZone, 0.05, 0.45, 0.18),
            RightDeadZone = InRange(RightDeadZone, 0.05, 0.45, 0.22),
            ResponseCurve = InRange(ResponseCurve, 1, 3, 1.8),
            TriggerPressThreshold = press,
            TriggerReleaseThreshold = Math.Clamp(TriggerReleaseThreshold, 0, press - 1),
            ControllerSlot = Math.Clamp(ControllerSlot, -1, 3), Mappings = mappings, RecordedBindings = recorded,
            PieMenus = menus, PieBindings = bindings, PieMenuSizePercent = PieMenu.ValidSize(PieMenuSizePercent)
        };
    }

    private static double InRange(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
