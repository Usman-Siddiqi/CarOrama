using CarOrama.Core.Control;

namespace CarOrama.Game.Input;

/// <summary>
/// Holds one actuator command constant across the fixed physics substeps of a
/// controller tick. GodotDrivingEnvironment is the sole writer.
/// </summary>
public sealed class BufferedVehicleCommandSource : IVehicleCommandSource, IVehicleLightingCommandSource
{
    public VehicleCommand CurrentCommand { get; private set; } = VehicleCommand.Neutral;

    public VehicleLightingCommand CurrentLightingCommand { get; private set; } = VehicleLightingCommand.Off;

    public void Set(VehicleCommand command, VehicleLightingCommand? lightingCommand = null)
    {
        CurrentCommand = command;
        CurrentLightingCommand = lightingCommand ?? VehicleLightingCommand.Off;
    }

    public void Reset()
    {
        CurrentCommand = VehicleCommand.Neutral;
        CurrentLightingCommand = VehicleLightingCommand.Off;
    }

    public VehicleCommand ReadCommand(double deltaSeconds)
    {
        _ = deltaSeconds;
        return CurrentCommand;
    }

    public VehicleLightingCommand ReadLightingCommand(double deltaSeconds)
    {
        _ = deltaSeconds;
        return CurrentLightingCommand;
    }
}
