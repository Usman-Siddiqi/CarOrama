using CarOrama.Core.Geometry;
using CarOrama.Core.Sensors;
using CarOrama.Core.Simulation;

namespace CarOrama.Core.Data;

public static class EpisodeDataSchema
{
    public const int CurrentVersion = 2;
}

public sealed record DatasetDescriptor(
    int SchemaVersion,
    int SimulationContractVersion,
    string DatasetId,
    string BuildId,
    DateTimeOffset CreatedAtUtc,
    string ScenarioManifestFile,
    string CoordinateConvention,
    string Units);

public sealed record EpisodeDescriptor(
    int SchemaVersion,
    string ScenarioId,
    DatasetSplit Split,
    long Seed,
    string RouteId,
    string SpawnPointId,
    string DestinationLaneId,
    IReadOnlyList<string> LaneIds,
    int PhysicsTicksPerSecond,
    int ControlTicksPerSecond,
    long MaximumControlTicks,
    IReadOnlyList<CameraCalibrationRecord> Cameras);

public sealed record CameraCalibrationRecord(
    CameraSensorId Id,
    CameraSensorPose Pose,
    double HorizontalFieldOfViewDegrees,
    double VerticalFieldOfViewDegrees,
    int ImageWidthPixels,
    int ImageHeightPixels,
    double CaptureFrequencyHertz)
{
    public static CameraCalibrationRecord From(CameraSensorSpecification specification) => new(
        specification.Id,
        specification.Pose,
        specification.HorizontalFieldOfViewDegrees,
        specification.VerticalFieldOfViewDegrees,
        specification.ImageWidthPixels,
        specification.ImageHeightPixels,
        specification.CaptureFrequencyHertz);
}

public sealed record SensorFrameReference(
    CameraSensorId CameraId,
    long PhysicsTick,
    double SimulationTimeSeconds,
    string RelativePath);

public sealed record DrivingActionRecord(
    long ControlTick,
    double Steering,
    double LongitudinalAccelerationMetersPerSecondSquared)
{
    public static DrivingActionRecord From(DrivingAction action) => new(
        action.ControlTick,
        action.Steering,
        action.LongitudinalAccelerationMetersPerSecondSquared);
}

public sealed record EgoObservationRecord(
    Vector2D WorldPosition,
    double HeadingRadians,
    double SpeedMetersPerSecond,
    double LongitudinalAccelerationMetersPerSecondSquared,
    double YawRateRadiansPerSecond,
    double Steering,
    double BatteryStateOfCharge);

public sealed record LaneObservationRecord(
    string LaneId,
    Vector2D CenterPoint,
    Vector2D Direction,
    double LateralOffsetMeters,
    double HeadingErrorRadians,
    double CurvaturePerMeter,
    double SpeedLimitMetersPerSecond);

public sealed record RouteObservationRecord(
    string RouteId,
    Vector2D LookaheadPoint,
    double LookaheadDistanceMeters,
    double DistanceTravelledMeters,
    double RemainingDistanceMeters);

public sealed record TrafficControlObservationRecord(
    ObservedTrafficControlKind Kind,
    ObservedTrafficControlState State,
    string? TrafficControlId,
    string? StopLineId,
    double? DistanceToStopLineMeters);

public sealed record ObservationRecord(
    long ControlTick,
    long PhysicsTick,
    EgoObservationRecord EgoVehicle,
    LaneObservationRecord Lane,
    RouteObservationRecord Route,
    TrafficControlObservationRecord UpcomingTrafficControl,
    bool HasCollision,
    bool IsOutsideDrivableArea,
    bool IsWrongWay)
{
    public static ObservationRecord From(PrivilegedObservation observation) => new(
        observation.ControlTick,
        observation.PhysicsTick,
        new EgoObservationRecord(
            observation.EgoVehicle.WorldPosition,
            observation.EgoVehicle.HeadingRadians,
            observation.EgoVehicle.SpeedMetersPerSecond,
            observation.EgoVehicle.LongitudinalAccelerationMetersPerSecondSquared,
            observation.EgoVehicle.YawRateRadiansPerSecond,
            observation.EgoVehicle.Steering,
            observation.EgoVehicle.BatteryStateOfCharge),
        new LaneObservationRecord(
            observation.Lane.LaneId,
            observation.Lane.CenterPoint,
            observation.Lane.Direction,
            observation.Lane.LateralOffsetMeters,
            observation.Lane.HeadingErrorRadians,
            observation.Lane.CurvaturePerMeter,
            observation.Lane.SpeedLimitMetersPerSecond),
        new RouteObservationRecord(
            observation.Route.RouteId,
            observation.Route.LookaheadPoint,
            observation.Route.LookaheadDistanceMeters,
            observation.Route.DistanceTravelledMeters,
            observation.Route.RemainingDistanceMeters),
        new TrafficControlObservationRecord(
            observation.UpcomingTrafficControl.Kind,
            observation.UpcomingTrafficControl.State,
            observation.UpcomingTrafficControl.TrafficControlId,
            observation.UpcomingTrafficControl.StopLineId,
            observation.UpcomingTrafficControl.DistanceToStopLineMeters),
        observation.HasCollision,
        observation.IsOutsideDrivableArea,
        observation.IsWrongWay);
}

public sealed record EpisodeMetricsRecord(
    double ElapsedSimulationSeconds,
    double DistanceTravelledMeters,
    double RouteCompletionFraction,
    int CollisionCount,
    int LaneDepartureCount,
    int RedLightViolationCount,
    int StopSignViolationCount,
    double EnergyConsumedWattHours,
    double MeanAbsoluteJerkMetersPerSecondCubed,
    double SpeedingDurationSeconds,
    double LaneDepartureDurationSeconds)
{
    public static EpisodeMetricsRecord From(EpisodeMetrics metrics) => new(
        metrics.ElapsedSimulationSeconds,
        metrics.DistanceTravelledMeters,
        metrics.RouteCompletionFraction,
        metrics.CollisionCount,
        metrics.LaneDepartureCount,
        metrics.RedLightViolationCount,
        metrics.StopSignViolationCount,
        metrics.EnergyConsumedWattHours,
        metrics.MeanAbsoluteJerkMetersPerSecondCubed,
        metrics.SpeedingDurationSeconds,
        metrics.LaneDepartureDurationSeconds);
}

public sealed record EpisodeTerminationRecord(
    EpisodeTerminationKind Kind,
    EpisodeTerminationReason Reason,
    string? Detail);

public sealed record EpisodeResetDataRecord(
    string RecordType,
    int SchemaVersion,
    ObservationRecord Observation)
{
    public static EpisodeResetDataRecord Create(PrivilegedObservation observation) => new(
        "reset",
        EpisodeDataSchema.CurrentVersion,
        ObservationRecord.From(observation));
}

public sealed record EpisodeStepDataRecord(
    string RecordType,
    int SchemaVersion,
    DrivingActionRecord Action,
    ObservationRecord Observation,
    double Reward,
    EpisodeMetricsRecord Metrics,
    EpisodeTerminationRecord Termination,
    IReadOnlyList<SensorFrameReference> SensorFrames)
{
    public static EpisodeStepDataRecord Create(
        DrivingAction action,
        EpisodeStepResult result,
        IReadOnlyList<SensorFrameReference>? sensorFrames = null) => new(
            "step",
            EpisodeDataSchema.CurrentVersion,
            DrivingActionRecord.From(action),
            ObservationRecord.From(result.Observation),
            result.Reward,
            EpisodeMetricsRecord.From(result.Metrics),
            new EpisodeTerminationRecord(
                result.Termination.Kind,
                result.Termination.Reason,
                result.Termination.Detail),
            sensorFrames ?? []);
}

public sealed record EpisodeSummary(
    int SchemaVersion,
    string ScenarioId,
    long FinalControlTick,
    EpisodeMetricsRecord Metrics,
    EpisodeTerminationRecord Termination,
    int RecordedStepCount,
    int RecordedSensorFrameCount);
