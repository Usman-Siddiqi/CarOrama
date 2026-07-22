using CarOrama.Core.Geometry;

namespace CarOrama.Core.Simulation;

public sealed record EgoVehicleObservation
{
    private EgoVehicleObservation(
        Vector2D worldPosition,
        double headingRadians,
        double speedMetersPerSecond,
        double longitudinalAccelerationMetersPerSecondSquared,
        double yawRateRadiansPerSecond,
        double steering,
        double batteryStateOfCharge)
    {
        WorldPosition = worldPosition;
        HeadingRadians = headingRadians;
        SpeedMetersPerSecond = speedMetersPerSecond;
        LongitudinalAccelerationMetersPerSecondSquared = longitudinalAccelerationMetersPerSecondSquared;
        YawRateRadiansPerSecond = yawRateRadiansPerSecond;
        Steering = steering;
        BatteryStateOfCharge = batteryStateOfCharge;
    }

    public Vector2D WorldPosition { get; }

    public double HeadingRadians { get; }

    public double SpeedMetersPerSecond { get; }

    public double LongitudinalAccelerationMetersPerSecondSquared { get; }

    public double YawRateRadiansPerSecond { get; }

    public double Steering { get; }

    public double BatteryStateOfCharge { get; }

    public static EgoVehicleObservation Create(
        Vector2D worldPosition,
        double headingRadians,
        double speedMetersPerSecond,
        double longitudinalAccelerationMetersPerSecondSquared,
        double yawRateRadiansPerSecond,
        double steering,
        double batteryStateOfCharge)
    {
        SimulationContractValidation.RequireFinite(worldPosition, nameof(worldPosition));
        SimulationContractValidation.RequireFinite(headingRadians, nameof(headingRadians));
        SimulationContractValidation.RequireNonNegative(speedMetersPerSecond, nameof(speedMetersPerSecond));
        SimulationContractValidation.RequireFinite(
            longitudinalAccelerationMetersPerSecondSquared,
            nameof(longitudinalAccelerationMetersPerSecondSquared));
        SimulationContractValidation.RequireFinite(yawRateRadiansPerSecond, nameof(yawRateRadiansPerSecond));

        if (!double.IsFinite(steering) || steering is < -1.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(steering), "Steering must be within [-1, 1].");
        }

        if (!double.IsFinite(batteryStateOfCharge) || batteryStateOfCharge is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batteryStateOfCharge),
                "Battery state of charge must be within [0, 1].");
        }

        return new EgoVehicleObservation(
            worldPosition,
            headingRadians,
            speedMetersPerSecond,
            longitudinalAccelerationMetersPerSecondSquared,
            yawRateRadiansPerSecond,
            steering,
            batteryStateOfCharge);
    }
}

public sealed record LaneReferenceObservation
{
    private LaneReferenceObservation(
        string laneId,
        Vector2D centerPoint,
        Vector2D direction,
        double lateralOffsetMeters,
        double headingErrorRadians,
        double curvaturePerMeter,
        double speedLimitMetersPerSecond)
    {
        LaneId = laneId;
        CenterPoint = centerPoint;
        Direction = direction;
        LateralOffsetMeters = lateralOffsetMeters;
        HeadingErrorRadians = headingErrorRadians;
        CurvaturePerMeter = curvaturePerMeter;
        SpeedLimitMetersPerSecond = speedLimitMetersPerSecond;
    }

    public string LaneId { get; }

    public Vector2D CenterPoint { get; }

    public Vector2D Direction { get; }

    public double LateralOffsetMeters { get; }

    public double HeadingErrorRadians { get; }

    public double CurvaturePerMeter { get; }

    public double SpeedLimitMetersPerSecond { get; }

    public static LaneReferenceObservation Create(
        string? laneId,
        Vector2D centerPoint,
        Vector2D direction,
        double lateralOffsetMeters,
        double headingErrorRadians,
        double curvaturePerMeter,
        double speedLimitMetersPerSecond)
    {
        var validatedLaneId = SimulationContractValidation.RequireIdentifier(laneId, nameof(laneId));
        SimulationContractValidation.RequireFinite(centerPoint, nameof(centerPoint));
        SimulationContractValidation.RequireFinite(direction, nameof(direction));
        SimulationContractValidation.RequireFinite(lateralOffsetMeters, nameof(lateralOffsetMeters));
        SimulationContractValidation.RequireFinite(headingErrorRadians, nameof(headingErrorRadians));
        SimulationContractValidation.RequireFinite(curvaturePerMeter, nameof(curvaturePerMeter));

        if (direction.Length <= 1e-9)
        {
            throw new ArgumentException("The lane direction cannot be a zero vector.", nameof(direction));
        }

        if (!double.IsFinite(speedLimitMetersPerSecond) || speedLimitMetersPerSecond <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(speedLimitMetersPerSecond),
                "The speed limit must be finite and positive.");
        }

        return new LaneReferenceObservation(
            validatedLaneId,
            centerPoint,
            direction.Normalized(),
            lateralOffsetMeters,
            headingErrorRadians,
            curvaturePerMeter,
            speedLimitMetersPerSecond);
    }
}

public sealed record RouteProgressObservation
{
    private RouteProgressObservation(
        string routeId,
        Vector2D lookaheadPoint,
        double lookaheadDistanceMeters,
        double distanceTravelledMeters,
        double remainingDistanceMeters)
    {
        RouteId = routeId;
        LookaheadPoint = lookaheadPoint;
        LookaheadDistanceMeters = lookaheadDistanceMeters;
        DistanceTravelledMeters = distanceTravelledMeters;
        RemainingDistanceMeters = remainingDistanceMeters;
    }

    public string RouteId { get; }

    public Vector2D LookaheadPoint { get; }

    public double LookaheadDistanceMeters { get; }

    public double DistanceTravelledMeters { get; }

    public double RemainingDistanceMeters { get; }

    public static RouteProgressObservation Create(
        string? routeId,
        Vector2D lookaheadPoint,
        double lookaheadDistanceMeters,
        double distanceTravelledMeters,
        double remainingDistanceMeters)
    {
        return new RouteProgressObservation(
            SimulationContractValidation.RequireIdentifier(routeId, nameof(routeId)),
            SimulationContractValidation.RequireFinite(lookaheadPoint, nameof(lookaheadPoint)),
            SimulationContractValidation.RequireNonNegative(
                lookaheadDistanceMeters,
                nameof(lookaheadDistanceMeters)),
            SimulationContractValidation.RequireNonNegative(
                distanceTravelledMeters,
                nameof(distanceTravelledMeters)),
            SimulationContractValidation.RequireNonNegative(
                remainingDistanceMeters,
                nameof(remainingDistanceMeters)));
    }
}

public enum ObservedTrafficControlKind
{
    None,
    StopSign,
    TrafficSignal,
}

public enum ObservedTrafficControlState
{
    None,
    StopRequired,
    Red,
    Yellow,
    Green,
    Unknown,
}

public sealed record UpcomingTrafficControlObservation
{
    private UpcomingTrafficControlObservation(
        ObservedTrafficControlKind kind,
        ObservedTrafficControlState state,
        string? trafficControlId,
        string? stopLineId,
        double? distanceToStopLineMeters)
    {
        Kind = kind;
        State = state;
        TrafficControlId = trafficControlId;
        StopLineId = stopLineId;
        DistanceToStopLineMeters = distanceToStopLineMeters;
    }

    public static UpcomingTrafficControlObservation None { get; } = new(
        ObservedTrafficControlKind.None,
        ObservedTrafficControlState.None,
        null,
        null,
        null);

    public ObservedTrafficControlKind Kind { get; }

    public ObservedTrafficControlState State { get; }

    public string? TrafficControlId { get; }

    public string? StopLineId { get; }

    public double? DistanceToStopLineMeters { get; }

    public static UpcomingTrafficControlObservation Create(
        ObservedTrafficControlKind kind,
        ObservedTrafficControlState state,
        string? trafficControlId,
        string? stopLineId,
        double distanceToStopLineMeters)
    {
        if (!Enum.IsDefined(kind) || kind == ObservedTrafficControlKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Use None when no control is upcoming.");
        }

        if (!Enum.IsDefined(state) || state == ObservedTrafficControlState.None)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "An upcoming control requires an observed state.");
        }

        if (kind == ObservedTrafficControlKind.StopSign &&
            state != ObservedTrafficControlState.StopRequired)
        {
            throw new ArgumentException("A stop sign must use the StopRequired state.", nameof(state));
        }

        if (kind == ObservedTrafficControlKind.TrafficSignal &&
            state is not (ObservedTrafficControlState.Red or
                ObservedTrafficControlState.Yellow or
                ObservedTrafficControlState.Green or
                ObservedTrafficControlState.Unknown))
        {
            throw new ArgumentException("A traffic signal requires a signal-light state.", nameof(state));
        }

        return new UpcomingTrafficControlObservation(
            kind,
            state,
            SimulationContractValidation.RequireIdentifier(trafficControlId, nameof(trafficControlId)),
            SimulationContractValidation.RequireIdentifier(stopLineId, nameof(stopLineId)),
            SimulationContractValidation.RequireNonNegative(
                distanceToStopLineMeters,
                nameof(distanceToStopLineMeters)));
    }
}

public sealed record PrivilegedObservation
{
    private PrivilegedObservation(
        long controlTick,
        long physicsTick,
        EgoVehicleObservation egoVehicle,
        LaneReferenceObservation lane,
        RouteProgressObservation route,
        UpcomingTrafficControlObservation upcomingTrafficControl,
        bool hasCollision,
        bool isOutsideDrivableArea,
        bool isWrongWay)
    {
        ContractVersion = SimulationContract.CurrentVersion;
        ControlTick = controlTick;
        PhysicsTick = physicsTick;
        EgoVehicle = egoVehicle;
        Lane = lane;
        Route = route;
        UpcomingTrafficControl = upcomingTrafficControl;
        HasCollision = hasCollision;
        IsOutsideDrivableArea = isOutsideDrivableArea;
        IsWrongWay = isWrongWay;
    }

    public int ContractVersion { get; }

    public long ControlTick { get; }

    public long PhysicsTick { get; }

    public EgoVehicleObservation EgoVehicle { get; }

    public LaneReferenceObservation Lane { get; }

    public RouteProgressObservation Route { get; }

    public UpcomingTrafficControlObservation UpcomingTrafficControl { get; }

    public bool HasCollision { get; }

    public bool IsOutsideDrivableArea { get; }

    public bool IsWrongWay { get; }

    public static PrivilegedObservation Create(
        long controlTick,
        long physicsTick,
        EgoVehicleObservation? egoVehicle,
        LaneReferenceObservation? lane,
        RouteProgressObservation? route,
        UpcomingTrafficControlObservation? upcomingTrafficControl,
        bool hasCollision = false,
        bool isOutsideDrivableArea = false,
        bool isWrongWay = false)
    {
        if (controlTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(controlTick), "The control tick cannot be negative.");
        }

        if (physicsTick < 0 || physicsTick < controlTick)
        {
            throw new ArgumentOutOfRangeException(
                nameof(physicsTick),
                "The physics tick cannot be negative or precede the control tick.");
        }

        ArgumentNullException.ThrowIfNull(egoVehicle);
        ArgumentNullException.ThrowIfNull(lane);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(upcomingTrafficControl);

        return new PrivilegedObservation(
            controlTick,
            physicsTick,
            egoVehicle,
            lane,
            route,
            upcomingTrafficControl,
            hasCollision,
            isOutsideDrivableArea,
            isWrongWay);
    }
}
