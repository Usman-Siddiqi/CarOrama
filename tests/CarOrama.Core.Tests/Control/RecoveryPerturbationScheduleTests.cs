using CarOrama.Core.Control;
using CarOrama.Core.Simulation;

namespace CarOrama.Core.Tests.Control;

public sealed class RecoveryPerturbationScheduleTests
{
    [Fact]
    public void AppliesDeterministicFourTickDisturbanceAndSuppressesThoseFrames()
    {
        var schedule = new RecoveryPerturbationSchedule(20, driverSeed: 2);
        var decisions = Enumerable.Range(0, 130)
            .Select(tick => schedule.Apply(
                CreateObservation(
                    controlTick: tick,
                    physicsTick: tick * 6,
                    speedMetersPerSecond: 10.0),
                DrivingAction.Create(tick, 0.0, 1.0)))
            .ToArray();

        Assert.All(decisions.Take(120), decision => Assert.False(decision.IsPerturbation));
        Assert.All(decisions.Skip(120).Take(4), decision =>
        {
            Assert.True(decision.IsPerturbation);
            Assert.True(decision.SuppressSensorFrames);
            Assert.Equal(0.22, decision.ExecutedAction.Steering, 6);
        });
        Assert.False(decisions[124].IsPerturbation);
    }

    private static PrivilegedObservation CreateObservation(
        long controlTick,
        long physicsTick,
        double speedMetersPerSecond) => PrivilegedObservation.Create(
            controlTick,
            physicsTick,
            EgoVehicleObservation.Create(
                CarOrama.Core.Geometry.Vector2D.Zero,
                headingRadians: 0.0,
                speedMetersPerSecond,
                longitudinalAccelerationMetersPerSecondSquared: 0.0,
                yawRateRadiansPerSecond: 0.0,
                steering: 0.0,
                batteryStateOfCharge: 0.8),
            LaneReferenceObservation.Create(
                "lane-1",
                CarOrama.Core.Geometry.Vector2D.Zero,
                new CarOrama.Core.Geometry.Vector2D(1.0, 0.0),
                lateralOffsetMeters: 0.0,
                headingErrorRadians: 0.0,
                curvaturePerMeter: 0.0,
                speedLimitMetersPerSecond: 13.9),
            RouteProgressObservation.Create(
                "route-1",
                new CarOrama.Core.Geometry.Vector2D(15.0, 0.0),
                lookaheadDistanceMeters: 15.0,
                distanceTravelledMeters: 10.0,
                remainingDistanceMeters: 100.0),
            UpcomingTrafficControlObservation.None);

    [Fact]
    public void DoesNotPerturbAtLowSpeed()
    {
        var schedule = new RecoveryPerturbationSchedule(20, driverSeed: 1);
        for (var tick = 0; tick < 400; tick++)
        {
            var decision = schedule.Apply(
                CreateObservation(
                    controlTick: tick,
                    physicsTick: tick * 6,
                    speedMetersPerSecond: 3.0),
                DrivingAction.Create(tick, 0.0, 1.0));
            Assert.False(decision.IsPerturbation);
        }
    }
}