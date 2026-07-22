namespace CarOrama.Core.Control;

public enum TurnSignalState
{
    Off,
    Left,
    Right,
}

/// <summary>
/// Exterior-lighting request kept separate from motion commands so manual and
/// automated controllers can command the same vehicle systems independently.
/// </summary>
public readonly record struct VehicleLightingCommand
{
    private VehicleLightingCommand(
        bool headlightsEnabled,
        TurnSignalState turnSignal,
        bool hazardLightsEnabled)
    {
        HeadlightsEnabled = headlightsEnabled;
        TurnSignal = turnSignal;
        HazardLightsEnabled = hazardLightsEnabled;
    }

    public bool HeadlightsEnabled { get; }

    public TurnSignalState TurnSignal { get; }

    public bool HazardLightsEnabled { get; }

    public bool LeftIndicatorEnabled => HazardLightsEnabled || TurnSignal == TurnSignalState.Left;

    public bool RightIndicatorEnabled => HazardLightsEnabled || TurnSignal == TurnSignalState.Right;

    public static VehicleLightingCommand Off => new(false, TurnSignalState.Off, false);

    public static VehicleLightingCommand Create(
        bool headlightsEnabled,
        TurnSignalState turnSignal,
        bool hazardLightsEnabled)
    {
        var normalizedTurnSignal = Enum.IsDefined(turnSignal) ? turnSignal : TurnSignalState.Off;
        return new VehicleLightingCommand(headlightsEnabled, normalizedTurnSignal, hazardLightsEnabled);
    }
}
