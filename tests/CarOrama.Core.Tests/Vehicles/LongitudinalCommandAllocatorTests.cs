using CarOrama.Core.Simulation;
using CarOrama.Core.Vehicles;

namespace CarOrama.Core.Tests.Vehicles;

public sealed class LongitudinalCommandAllocatorTests
{
    private readonly LongitudinalCommandAllocator _allocator = new(new VehicleSpecification());

    [Fact]
    public void PositiveAccelerationMapsToThrottleWithoutBraking()
    {
        var command = _allocator.Allocate(
            DrivingAction.Create(0, 0.25, 3.15),
            speedMetersPerSecond: 10.0,
            batteryStateOfCharge: 0.8);

        Assert.Equal(0.25, command.Steering);
        Assert.Equal(0.5, command.Throttle, precision: 9);
        Assert.Equal(0.0, command.RegenerativeBrake);
        Assert.Equal(0.0, command.FrictionBrake);
    }

    [Fact]
    public void ModerateDecelerationUsesAvailableRegenerationFirst()
    {
        var command = _allocator.Allocate(
            DrivingAction.Create(0, 0.0, -1.0),
            speedMetersPerSecond: 15.0,
            batteryStateOfCharge: 0.8);

        Assert.Equal(0.0, command.Throttle);
        Assert.InRange(command.RegenerativeBrake, 0.01, 1.0);
        Assert.Equal(0.0, command.FrictionBrake);
    }

    [Fact]
    public void StrongDecelerationBlendsRegenerationAndFrictionBrakes()
    {
        var command = _allocator.Allocate(
            DrivingAction.Create(0, 0.0, -8.0),
            speedMetersPerSecond: 15.0,
            batteryStateOfCharge: 0.8);

        Assert.InRange(command.RegenerativeBrake, 0.99, 1.0);
        Assert.InRange(command.FrictionBrake, 0.01, 1.0);
    }

    [Theory]
    [InlineData(0.0, 0.8)]
    [InlineData(15.0, 1.0)]
    public void FrictionBrakesFillInWhenRegenerationIsUnavailable(double speed, double stateOfCharge)
    {
        var command = _allocator.Allocate(
            DrivingAction.Create(0, 0.0, -2.0),
            speed,
            stateOfCharge);

        Assert.Equal(0.0, command.RegenerativeBrake);
        Assert.True(command.FrictionBrake > 0.0);
    }
}
