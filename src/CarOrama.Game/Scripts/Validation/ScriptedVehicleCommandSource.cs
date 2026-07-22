using CarOrama.Core.Control;

namespace CarOrama.Game.Validation;

/// <summary>
/// Repeatable integration input used only by the headless smoke scenario.
/// </summary>
public sealed class ScriptedVehicleCommandSource : IVehicleCommandSource, IVehicleLightingCommandSource
{
    private double _elapsedSeconds;

    public VehicleCommand ReadCommand(double deltaSeconds)
    {
        _elapsedSeconds += deltaSeconds;
        return _elapsedSeconds switch
        {
            < 0.75 => VehicleCommand.Neutral,
            < 2.75 => VehicleCommand.Create(0.08, 0.62, 0.0, 0.0),
            < 3.75 => VehicleCommand.Create(0.0, 0.0, 0.85, 0.0),
            _ => VehicleCommand.Neutral,
        };
    }

    public VehicleLightingCommand ReadLightingCommand(double deltaSeconds)
    {
        _ = deltaSeconds;
        return VehicleLightingCommand.Create(
            headlightsEnabled: true,
            turnSignal: TurnSignalState.Off,
            hazardLightsEnabled: _elapsedSeconds >= 0.75);
    }
}
