using CarOrama.Core.Geometry;
using CarOrama.Core.Roads;
using Godot;

namespace CarOrama.Game.Environment;

public sealed partial class TrafficSignalSystem : Node
{
    private const double DetectorRangeMeters = 34.0;
    private const double DetectorDownstreamToleranceMeters = 3.0;
    private const double DetectorLateralMarginMeters = 0.9;

    private readonly RoadNetwork _network;
    private readonly IReadOnlyDictionary<string, TrafficSignalHead> _heads;
    private readonly Dictionary<string, IntersectionController> _controllers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<LaneDetector>> _detectorsByControlId = new(StringComparer.Ordinal);

    public TrafficSignalSystem(
        RoadNetwork network,
        IReadOnlyDictionary<string, TrafficSignalHead> heads)
    {
        _network = network ?? throw new ArgumentNullException(nameof(network));
        _heads = heads ?? throw new ArgumentNullException(nameof(heads));
        Name = "ActuatedTrafficSignals";
        BuildControllers();
        ApplyAllStates();
    }

    public Node3D? ObservedVehicle { get; set; }

    public TrafficSignalState GetState(string controlId)
    {
        foreach (var controller in _controllers.Values)
        {
            if (controller.ControlIds.Contains(controlId, StringComparer.Ordinal))
            {
                return controller.Controller.GetState(controlId);
            }
        }

        throw new KeyNotFoundException($"No active traffic signal exists for '{controlId}'.");
    }

    public override void _PhysicsProcess(double delta)
    {
        var vehiclePosition = ObservedVehicle is null || !IsInstanceValid(ObservedVehicle)
            ? (Vector2D?)null
            : new Vector2D(ObservedVehicle.GlobalPosition.X, ObservedVehicle.GlobalPosition.Z);

        foreach (var intersection in _controllers.Values)
        {
            var demand = vehiclePosition is null
                ? []
                : intersection.ControlIds
                    .Where(controlId => IsDemanded(controlId, vehiclePosition.Value))
                    .ToArray();
            intersection.Controller.Step(delta, demand);

            foreach (var controlId in intersection.ControlIds)
            {
                if (_heads.TryGetValue(controlId, out var head))
                {
                    head.SetState(intersection.Controller.GetState(controlId));
                }
            }
        }
    }

    private void BuildControllers()
    {
        foreach (var intersection in _network.Intersections)
        {
            var signalControls = intersection.TrafficControlIds
                .Select(id => _network.TrafficControls.Single(control => control.Id == id))
                .Where(control => control.Kind == TrafficControlKind.TrafficLight)
                .ToArray();
            if (signalControls.Length == 0)
            {
                continue;
            }

            var phases = signalControls.ToDictionary(
                control => control.Id,
                control => Math.Abs(control.FacingDirection.X) >= Math.Abs(control.FacingDirection.Y)
                    ? TrafficSignalPhase.Horizontal
                    : TrafficSignalPhase.Vertical,
                StringComparer.Ordinal);
            var controller = new ActuatedTrafficSignalController(phases);
            _controllers.Add(
                intersection.NodeId,
                new IntersectionController(controller, signalControls.Select(control => control.Id).ToArray()));

            foreach (var control in signalControls)
            {
                _detectorsByControlId[control.Id] = control.IncomingLaneIds
                    .Select(BuildDetector)
                    .ToArray();
            }
        }
    }

    private LaneDetector BuildDetector(string laneId)
    {
        var lane = _network.GetLane(laneId);
        var stopLine = _network.StopLines.Single(line => line.LaneId == laneId);
        var stopLineCenter = (stopLine.LeftPoint + stopLine.RightPoint) * 0.5;
        return new LaneDetector(stopLineCenter, lane.Direction, (lane.WidthMeters * 0.5) + DetectorLateralMarginMeters);
    }

    private bool IsDemanded(string controlId, Vector2D vehiclePosition)
    {
        return _detectorsByControlId[controlId].Any(detector =>
        {
            var vehicleToStopLine = detector.StopLineCenter - vehiclePosition;
            var longitudinalDistance = Vector2D.Dot(vehicleToStopLine, detector.ApproachDirection);
            var lateralDistance = Math.Abs(Vector2D.Dot(
                vehiclePosition - detector.StopLineCenter,
                detector.ApproachDirection.PerpendicularLeft()));
            return longitudinalDistance >= -DetectorDownstreamToleranceMeters &&
                longitudinalDistance <= DetectorRangeMeters &&
                lateralDistance <= detector.HalfWidthMeters;
        });
    }

    private void ApplyAllStates()
    {
        foreach (var intersection in _controllers.Values)
        {
            foreach (var controlId in intersection.ControlIds)
            {
                if (_heads.TryGetValue(controlId, out var head))
                {
                    head.SetState(intersection.Controller.GetState(controlId), force: true);
                }
            }
        }
    }

    private sealed record IntersectionController(
        ActuatedTrafficSignalController Controller,
        IReadOnlyList<string> ControlIds);

    private readonly record struct LaneDetector(
        Vector2D StopLineCenter,
        Vector2D ApproachDirection,
        double HalfWidthMeters);
}
