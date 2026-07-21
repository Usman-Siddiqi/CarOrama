namespace CarOrama.Core.Control;

/// <summary>
/// Boundary shared by manual input, replay, remote control, and a future driving agent.
/// </summary>
public interface IVehicleCommandSource
{
    VehicleCommand ReadCommand(double deltaSeconds);
}

