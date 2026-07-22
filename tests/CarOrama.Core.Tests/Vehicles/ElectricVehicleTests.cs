using CarOrama.Core.Control;
using CarOrama.Core.Vehicles;
using CarOrama.Core.Vehicles.Dynamics;

namespace CarOrama.Core.Tests.Vehicles;

public sealed class ElectricVehicleTests
{
    [Fact]
    public void CommandsClampAndRejectNonFiniteInput()
    {
        var command = VehicleCommand.Create(double.PositiveInfinity, 2.0, -1.0, double.NaN);
        var reverse = VehicleCommand.Create(0.0, -2.0, 0.0, 0.0);

        Assert.Equal(0.0, command.Steering);
        Assert.Equal(1.0, command.Throttle);
        Assert.Equal(0.0, command.RegenerativeBrake);
        Assert.Equal(0.0, command.FrictionBrake);
        Assert.Equal(-1.0, reverse.Throttle);
    }

    [Fact]
    public void LightingCommandExposesEffectiveHazardAndTurnIndicators()
    {
        var leftTurn = VehicleLightingCommand.Create(true, TurnSignalState.Left, false);
        var hazards = VehicleLightingCommand.Create(false, TurnSignalState.Off, true);

        Assert.True(leftTurn.HeadlightsEnabled);
        Assert.True(leftTurn.LeftIndicatorEnabled);
        Assert.False(leftTurn.RightIndicatorEnabled);
        Assert.True(hazards.LeftIndicatorEnabled);
        Assert.True(hazards.RightIndicatorEnabled);
    }

    [Fact]
    public void LightingCommandRejectsUnknownTurnSignalValues()
    {
        var command = VehicleLightingCommand.Create(false, (TurnSignalState)999, false);

        Assert.Equal(TurnSignalState.Off, command.TurnSignal);
        Assert.False(command.LeftIndicatorEnabled);
        Assert.False(command.RightIndicatorEnabled);
    }

    [Fact]
    public void MotorTransitionsFromTorqueLimitToPowerLimit()
    {
        var specification = new ElectricMotorSpecification();
        var motor = new ElectricMotorModel(specification);

        var lowSpeedTorque = motor.GetDriveTorqueNewtonMeters(50.0, 1.0);
        var highSpeedTorque = motor.GetDriveTorqueNewtonMeters(1_000.0, 1.0);

        Assert.Equal(specification.MaximumTorqueNewtonMeters, lowSpeedTorque, 6);
        Assert.Equal(specification.MaximumPowerWatts / 1_000.0, highSpeedTorque, 6);
        Assert.True(highSpeedTorque < lowSpeedTorque);
    }

    [Fact]
    public void MotorStopsProducingTorqueAboveMaximumSpeed()
    {
        var specification = new ElectricMotorSpecification();
        var motor = new ElectricMotorModel(specification);
        var excessiveSpeed = specification.MaximumSpeedRpm * (2.0 * Math.PI / 60.0) * 1.01;

        Assert.Equal(0.0, motor.GetDriveTorqueNewtonMeters(excessiveSpeed, 1.0));
        Assert.Equal(0.0, motor.GetRegenerativeTorqueNewtonMeters(excessiveSpeed, 1.0));
    }

    [Fact]
    public void BatteryHonorsChargeLimitsAndCapacity()
    {
        var specification = new BatterySpecification { InitialStateOfCharge = 0.9999 };
        var battery = new BatteryModel(specification);

        var accepted = battery.ApplyPower(-500_000.0, 60.0);

        Assert.InRange(accepted, -specification.MaximumChargePowerWatts, 0.0);
        Assert.InRange(battery.StateOfCharge, 0.9999, 1.0);
    }

    [Fact]
    public void RegenerationSlowsVehicleAndRecoversEnergy()
    {
        var model = new LongitudinalVehicleModel(new VehicleSpecification());
        const double step = 1.0 / 120.0;
        for (var index = 0; index < 600; index++)
        {
            model.Step(VehicleCommand.Create(0.0, 0.85, 0.0, 0.0), step);
        }

        var speedBefore = model.State.SpeedMetersPerSecond;
        var chargeBefore = model.State.StateOfCharge;
        for (var index = 0; index < 240; index++)
        {
            model.Step(VehicleCommand.Create(0.0, 0.0, 1.0, 0.0), step);
        }

        Assert.True(model.State.SpeedMetersPerSecond < speedBefore);
        Assert.True(model.State.StateOfCharge > chargeBefore);
    }

    [Fact]
    public void FrictionBrakeWorksWhenBatteryIsFull()
    {
        var specification = new VehicleSpecification
        {
            Battery = new BatterySpecification { InitialStateOfCharge = 1.0 },
        };
        var model = new LongitudinalVehicleModel(specification);
        const double step = 1.0 / 120.0;
        for (var index = 0; index < 400; index++)
        {
            model.Step(VehicleCommand.Create(0.0, 0.75, 0.0, 0.0), step);
        }

        var speedBefore = model.State.SpeedMetersPerSecond;
        for (var index = 0; index < 300; index++)
        {
            model.Step(VehicleCommand.Create(0.0, 0.0, 0.0, 1.0), step);
        }

        Assert.True(model.State.SpeedMetersPerSecond < speedBefore * 0.25);
    }

    [Fact]
    public void DefaultVehicleReproducesWaymoClassZeroToOneHundredTime()
    {
        var model = new LongitudinalVehicleModel(new VehicleSpecification());

        AccelerateToKilometersPerHour(model, 100.0);

        Assert.InRange(model.State.TimeSeconds, 4.65, 5.05);
    }

    [Fact]
    public void FullRegenerationProducesComfortableOnePedalDeceleration()
    {
        var model = new LongitudinalVehicleModel(new VehicleSpecification());
        AccelerateToKilometersPerHour(model, 100.0);
        var speedBefore = model.State.SpeedMetersPerSecond;
        const double step = 1.0 / 120.0;

        for (var index = 0; index < 120; index++)
        {
            model.Step(VehicleCommand.Create(0.0, 0.0, 1.0, 0.0), step);
        }

        var averageDeceleration = speedBefore - model.State.SpeedMetersPerSecond;
        Assert.InRange(averageDeceleration, 1.6, 2.3);
    }

    [Fact]
    public void EmergencyBrakeStopsFromOneHundredInRealisticDryRoadDistance()
    {
        var model = new LongitudinalVehicleModel(new VehicleSpecification());
        AccelerateToKilometersPerHour(model, 100.0);
        var brakingStartDistance = model.State.DistanceMeters;
        var brakingStartTime = model.State.TimeSeconds;
        const double step = 1.0 / 120.0;

        while (model.State.SpeedMetersPerSecond > 0.01 && model.State.TimeSeconds - brakingStartTime < 10.0)
        {
            model.Step(VehicleCommand.Create(0.0, 0.0, 0.0, 1.0), step);
        }

        var stoppingDistance = model.State.DistanceMeters - brakingStartDistance;
        var stoppingTime = model.State.TimeSeconds - brakingStartTime;
        Assert.InRange(stoppingDistance, 42.0, 50.0);
        Assert.InRange(stoppingTime, 3.0, 3.6);
    }

    [Theory]
    [InlineData(DrivetrainLayout.FrontWheelDrive)]
    [InlineData(DrivetrainLayout.RearWheelDrive)]
    [InlineData(DrivetrainLayout.AllWheelDrive)]
    public void EveryLayoutProducesFiniteDriveForce(DrivetrainLayout layout)
    {
        var drivetrain = new ElectricDrivetrain(new VehicleSpecification { DrivetrainLayout = layout });

        var output = drivetrain.Evaluate(VehicleCommand.Create(0.0, 1.0, 0.0, 0.0), 35.0, 1.0 / 120.0);

        Assert.True(double.IsFinite(output.DriveForceNewtons));
        Assert.True(output.DriveForceNewtons > 0.0);
    }

    [Fact]
    public void ReverseDemandProducesLimitedBackwardDriveForceAtLowSpeed()
    {
        var specification = new VehicleSpecification();
        var drivetrain = new ElectricDrivetrain(specification);

        var output = drivetrain.Evaluate(VehicleCommand.Create(0.0, -1.0, 0.0, 0.0), 0.0, 1.0 / 120.0);
        var maximumReverseForce = specification.MassKilograms
            * specification.MaximumReverseAccelerationMetersPerSecondSquared;

        Assert.True(output.DriveForceNewtons < 0.0);
        Assert.InRange(Math.Abs(output.DriveForceNewtons), 1.0, maximumReverseForce + 0.01);
    }

    [Fact]
    public void ReverseDemandRegenerativelyBrakesBeforeChangingDirection()
    {
        var specification = new VehicleSpecification();
        var drivetrain = new ElectricDrivetrain(specification);
        var forwardWheelSpeed = 10.0 / specification.WheelRadiusMeters;

        var output = drivetrain.Evaluate(
            VehicleCommand.Create(0.0, -1.0, 0.0, 0.0),
            forwardWheelSpeed,
            1.0 / 120.0);

        Assert.Equal(0.0, output.DriveForceNewtons);
        Assert.True(output.RegenerativeBrakeForceNewtons > 0.0);
    }

    [Fact]
    public void ReverseDriveStopsApplyingTorqueAtConfiguredSpeedLimit()
    {
        var specification = new VehicleSpecification();
        var drivetrain = new ElectricDrivetrain(specification);
        var reverseWheelSpeed = -specification.MaximumReverseSpeedMetersPerSecond
            / specification.WheelRadiusMeters;

        var output = drivetrain.Evaluate(
            VehicleCommand.Create(0.0, -1.0, 0.0, 0.0),
            reverseWheelSpeed,
            1.0 / 120.0);

        Assert.Equal(0.0, output.DriveForceNewtons);
    }

    [Fact]
    public void SuspensionProducesLoadOnlyWhenCompressed()
    {
        var suspension = new SuspensionModel(new SuspensionSpecification());

        var unloaded = suspension.Evaluate(0.5, 1.0 / 120.0);
        suspension.Reset();
        var loaded = suspension.Evaluate(0.32, 1.0 / 120.0);

        Assert.Equal(0.0, unloaded.NormalForceNewtons);
        Assert.True(loaded.NormalForceNewtons > 0.0);
    }

    [Fact]
    public void TireForceCannotExceedFrictionCircle()
    {
        var specification = new TireSpecification { FrictionCoefficient = 1.0 };
        var tire = new TireForceModel(specification);

        var output = tire.Evaluate(5_000.0, 8_000.0, 2.0);
        var magnitude = Math.Sqrt(
            (output.LongitudinalForceNewtons * output.LongitudinalForceNewtons) +
            (output.LateralForceNewtons * output.LateralForceNewtons));

        Assert.True(output.Saturated);
        Assert.InRange(magnitude, 4_999.99, 5_000.01);
    }

    [Fact]
    public void MechanicalBrakeIsIndependentAndClamped()
    {
        var brake = new MechanicalBrakeModel(18_000.0);

        Assert.Equal(18_000.0, brake.GetForceNewtons(2.0));
        Assert.Equal(0.0, brake.GetForceNewtons(-1.0));
    }

    private static void AccelerateToKilometersPerHour(LongitudinalVehicleModel model, double targetKilometersPerHour)
    {
        var targetMetersPerSecond = targetKilometersPerHour / 3.6;
        const double step = 1.0 / 120.0;
        while (model.State.SpeedMetersPerSecond < targetMetersPerSecond && model.State.TimeSeconds < 10.0)
        {
            model.Step(VehicleCommand.Create(0.0, 1.0, 0.0, 0.0), step);
        }

        Assert.True(
            model.State.SpeedMetersPerSecond >= targetMetersPerSecond,
            $"Vehicle failed to reach {targetKilometersPerHour:F0} km/h within ten seconds.");
    }
}
