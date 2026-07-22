using CarOrama.Core.Roads;

namespace CarOrama.Core.Simulation;

/// <summary>
/// Measures traffic-rule events against route geometry rather than rendered
/// meshes. Stop-line crossings are derived from monotonically increasing route
/// progress, making results deterministic across rendering configurations.
/// </summary>
public sealed class RoadRuleMetricsMonitor
{
    private const double SpeedToleranceMetersPerSecond = 0.5;
    private const double StopZoneLengthMeters = 2.5;
    private const double StoppedSpeedMetersPerSecond = 0.15;
    private const double RequiredStopDurationSeconds = 0.5;
    private readonly IReadOnlyList<RouteControl> _routeControls;
    private readonly HashSet<string> _processedStopLines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _stoppedDurations = new(StringComparer.Ordinal);
    private double _previousProgressMeters;
    private bool _wasLaneDeparture;

    public RoadRuleMetricsMonitor(RoadNetwork network, DrivingRoute route)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(route);
        var controls = new List<RouteControl>();
        foreach (var laneId in route.LaneIds)
        {
            var lane = network.GetLane(laneId);
            if (lane.StopLineId is null)
            {
                continue;
            }

            var stopLine = network.StopLines.Single(candidate => candidate.Id == lane.StopLineId);
            var center = (stopLine.LeftPoint + stopLine.RightPoint) * 0.5;
            if (!route.TryGetDistanceForLanePoint(laneId, center, out var progressMeters))
            {
                continue;
            }

            var control = network.TrafficControls.Single(candidate =>
                candidate.IncomingLaneIds.Contains(laneId, StringComparer.Ordinal));
            controls.Add(new RouteControl(stopLine.Id, progressMeters, control));
        }

        _routeControls = controls
            .DistinctBy(control => control.StopLineId, StringComparer.Ordinal)
            .OrderBy(control => control.ProgressMeters)
            .ToArray();
    }

    public int LaneDepartureCount { get; private set; }

    public int RedLightViolationCount { get; private set; }

    public int StopSignViolationCount { get; private set; }

    public double SpeedingDurationSeconds { get; private set; }

    public double LaneDepartureDurationSeconds { get; private set; }

    public void Reset(double initialProgressMeters, bool isLaneDeparture = false)
    {
        if (!double.IsFinite(initialProgressMeters) || initialProgressMeters < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialProgressMeters));
        }

        _previousProgressMeters = initialProgressMeters;
        _wasLaneDeparture = isLaneDeparture;
        _processedStopLines.Clear();
        _stoppedDurations.Clear();
        LaneDepartureCount = 0;
        RedLightViolationCount = 0;
        StopSignViolationCount = 0;
        SpeedingDurationSeconds = 0.0;
        LaneDepartureDurationSeconds = 0.0;
    }

    public void Update(
        double progressMeters,
        double speedMetersPerSecond,
        double speedLimitMetersPerSecond,
        bool isLaneDeparture,
        double elapsedSeconds,
        Func<TrafficControl, ObservedTrafficControlState>? signalStateResolver = null)
    {
        ValidateUpdate(progressMeters, speedMetersPerSecond, speedLimitMetersPerSecond, elapsedSeconds);
        if (isLaneDeparture && !_wasLaneDeparture)
        {
            LaneDepartureCount++;
        }

        if (isLaneDeparture)
        {
            LaneDepartureDurationSeconds += elapsedSeconds;
        }

        if (speedMetersPerSecond > speedLimitMetersPerSecond + SpeedToleranceMetersPerSecond)
        {
            SpeedingDurationSeconds += elapsedSeconds;
        }

        foreach (var routeControl in _routeControls)
        {
            if (_processedStopLines.Contains(routeControl.StopLineId))
            {
                continue;
            }

            var distanceToLine = routeControl.ProgressMeters - progressMeters;
            if (routeControl.Control.Kind == TrafficControlKind.StopSign &&
                distanceToLine is >= 0.0 and <= StopZoneLengthMeters &&
                speedMetersPerSecond <= StoppedSpeedMetersPerSecond)
            {
                _stoppedDurations[routeControl.StopLineId] =
                    _stoppedDurations.GetValueOrDefault(routeControl.StopLineId) + elapsedSeconds;
            }

            var crossed = _previousProgressMeters < routeControl.ProgressMeters &&
                progressMeters >= routeControl.ProgressMeters;
            if (!crossed)
            {
                continue;
            }

            if (routeControl.Control.Kind == TrafficControlKind.TrafficLight)
            {
                var state = signalStateResolver?.Invoke(routeControl.Control) ?? ParseState(routeControl.Control.State);
                if (state == ObservedTrafficControlState.Red)
                {
                    RedLightViolationCount++;
                }
            }
            else if (_stoppedDurations.GetValueOrDefault(routeControl.StopLineId) + 1e-9 < RequiredStopDurationSeconds)
            {
                StopSignViolationCount++;
            }

            _processedStopLines.Add(routeControl.StopLineId);
        }

        _previousProgressMeters = Math.Max(_previousProgressMeters, progressMeters);
        _wasLaneDeparture = isLaneDeparture;
    }

    private static void ValidateUpdate(
        double progressMeters,
        double speedMetersPerSecond,
        double speedLimitMetersPerSecond,
        double elapsedSeconds)
    {
        if (!double.IsFinite(progressMeters) || progressMeters < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(progressMeters));
        }

        if (!double.IsFinite(speedMetersPerSecond) || speedMetersPerSecond < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(speedMetersPerSecond));
        }

        if (!double.IsFinite(speedLimitMetersPerSecond) || speedLimitMetersPerSecond <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(speedLimitMetersPerSecond));
        }

        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        }
    }

    private static ObservedTrafficControlState ParseState(string state) => state.ToUpperInvariant() switch
    {
        "RED" => ObservedTrafficControlState.Red,
        "YELLOW" => ObservedTrafficControlState.Yellow,
        "GREEN" => ObservedTrafficControlState.Green,
        _ => ObservedTrafficControlState.Unknown,
    };

    private sealed record RouteControl(string StopLineId, double ProgressMeters, TrafficControl Control);
}
