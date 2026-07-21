namespace CarOrama.Core.Vehicles;

public sealed class BatteryModel
{
    private const double JoulesPerKilowattHour = 3_600_000.0;
    private double _storedEnergyJoules;

    public BatteryModel(BatterySpecification specification)
    {
        Specification = specification ?? throw new ArgumentNullException(nameof(specification));
        CapacityJoules = specification.CapacityKilowattHours * JoulesPerKilowattHour;
        _storedEnergyJoules = CapacityJoules * Math.Clamp(specification.InitialStateOfCharge, 0.0, 1.0);
    }

    public BatterySpecification Specification { get; }

    public double CapacityJoules { get; }

    public double StateOfCharge => _storedEnergyJoules / CapacityJoules;

    /// <summary>
    /// Applies pack power for a duration. Positive power discharges the pack;
    /// negative power charges it. The returned value is the accepted pack power.
    /// </summary>
    public double ApplyPower(double requestedPowerWatts, double deltaSeconds)
    {
        if (!double.IsFinite(requestedPowerWatts) || !double.IsFinite(deltaSeconds) || deltaSeconds <= 0.0)
        {
            return 0.0;
        }

        var power = Math.Clamp(
            requestedPowerWatts,
            -Specification.MaximumChargePowerWatts,
            Specification.MaximumDischargePowerWatts);

        if (power >= 0.0)
        {
            var maximumFromEnergy = (_storedEnergyJoules * Specification.DischargeEfficiency) / deltaSeconds;
            power = Math.Min(power, maximumFromEnergy);
            _storedEnergyJoules -= (power / Specification.DischargeEfficiency) * deltaSeconds;
        }
        else
        {
            var remainingCapacity = CapacityJoules - _storedEnergyJoules;
            var maximumChargeAtTerminals = remainingCapacity / (Specification.ChargeEfficiency * deltaSeconds);
            power = -Math.Min(-power, maximumChargeAtTerminals);
            _storedEnergyJoules += (-power * Specification.ChargeEfficiency) * deltaSeconds;
        }

        _storedEnergyJoules = Math.Clamp(_storedEnergyJoules, 0.0, CapacityJoules);
        return power;
    }

    public void SetStateOfCharge(double stateOfCharge)
    {
        _storedEnergyJoules = CapacityJoules * Math.Clamp(stateOfCharge, 0.0, 1.0);
    }
}

