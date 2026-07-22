namespace CarOrama.Core.Control;

/// <summary>
/// Boundary shared by manual input, scripted scenarios, remote control, and a
/// future driving agent for exterior vehicle lighting.
/// </summary>
public interface IVehicleLightingCommandSource
{
    VehicleLightingCommand ReadLightingCommand(double deltaSeconds);
}
