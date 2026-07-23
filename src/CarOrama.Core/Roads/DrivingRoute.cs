using System.Collections.ObjectModel;
using CarOrama.Core.Geometry;

namespace CarOrama.Core.Roads;

/// <summary>
/// A validated, directed lane sequence with a continuous distance coordinate.
/// Straight connector segments bridge the structured lane endpoints across an
/// intersection so route progress remains meaningful inside the junction.
/// </summary>
public sealed class DrivingRoute
{
    private readonly ReadOnlyCollection<string> _laneIds;
    private readonly ReadOnlyCollection<RoutePathSegment> _pathSegments;

    private DrivingRoute(
        string id,
        IReadOnlyList<string> laneIds,
        IReadOnlyList<RoutePathSegment> pathSegments,
        double totalLengthMeters)
    {
        Id = id;
        _laneIds = Array.AsReadOnly(laneIds.ToArray());
        _pathSegments = Array.AsReadOnly(pathSegments.ToArray());
        TotalLengthMeters = totalLengthMeters;
    }

    public string Id { get; }

    public IReadOnlyList<string> LaneIds => _laneIds;

    public double TotalLengthMeters { get; }

    internal IReadOnlyList<RoutePathSegment> PathSegments => _pathSegments;

    public static DrivingRoute Create(
        string? id,
        RoadNetwork? network,
        IEnumerable<string>? laneIds)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A route identifier is required.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(laneIds);
        var routeLaneIds = laneIds.ToArray();
        if (routeLaneIds.Length == 0)
        {
            throw new ArgumentException("A driving route must contain at least one lane.", nameof(laneIds));
        }

        if (routeLaneIds.Distinct(StringComparer.Ordinal).Count() != routeLaneIds.Length)
        {
            throw new ArgumentException("A driving route cannot repeat a lane.", nameof(laneIds));
        }

        var lanes = routeLaneIds.Select(laneId =>
        {
            if (!network.TryGetLane(laneId, out var lane))
            {
                throw new ArgumentException($"Route lane '{laneId}' does not exist.", nameof(laneIds));
            }

            return lane;
        }).ToArray();

        for (var index = 1; index < lanes.Length; index++)
        {
            if (!lanes[index - 1].SuccessorLaneIds.Contains(lanes[index].Id, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"Route lane '{lanes[index].Id}' is not a successor of '{lanes[index - 1].Id}'.",
                    nameof(laneIds));
            }
        }

        var pathSegments = new List<RoutePathSegment>();
        var distance = 0.0;
        for (var laneIndex = 0; laneIndex < lanes.Length; laneIndex++)
        {
            var lane = lanes[laneIndex];
            if (laneIndex > 0)
            {
                AddIntersectionConnector(lanes[laneIndex - 1], lane);
            }

            for (var pointIndex = 1; pointIndex < lane.CenterLine.Count; pointIndex++)
            {
                AddSegment(
                    lane.Id,
                    lane.CenterLine[pointIndex - 1],
                    lane.CenterLine[pointIndex],
                    isIntersectionConnector: false);
            }
        }

        if (pathSegments.Count == 0)
        {
            throw new ArgumentException("The route has no usable path geometry.", nameof(laneIds));
        }

        return new DrivingRoute(id.Trim(), routeLaneIds, pathSegments, distance);

        void AddSegment(string laneId, Vector2D start, Vector2D end, bool isIntersectionConnector)
        {
            var delta = end - start;
            var length = delta.Length;
            if (length <= 1e-9)
            {
                return;
            }

            pathSegments.Add(new RoutePathSegment(
                laneId,
                start,
                end,
                delta / length,
                distance,
                length,
                isIntersectionConnector));
            distance += length;
        }

        void AddIntersectionConnector(Lane incoming, Lane outgoing)
        {
            const int curveSegments = 16;
            var start = incoming.CenterLine[^1];
            var end = outgoing.CenterLine[0];
            var chordLength = Vector2D.Distance(start, end);
            if (chordLength <= 1e-9)
            {
                return;
            }

            // Cubic handles preserve the incoming and outgoing lane tangents. The
            // structured connector then describes a plausible path through the
            // conflict area instead of a diagonal with discontinuous headings.
            // Longer tangent handles keep the connector clear of the inside
            // curb while preserving the incoming and outgoing lane headings.
            var handleLength = chordLength * 0.7;
            var firstControl = start + (incoming.Direction * handleLength);
            var secondControl = end - (outgoing.Direction * handleLength);
            var previousPoint = start;
            for (var sample = 1; sample <= curveSegments; sample++)
            {
                var amount = sample / (double)curveSegments;
                var inverse = 1.0 - amount;
                var point = (start * (inverse * inverse * inverse)) +
                    (firstControl * (3.0 * inverse * inverse * amount)) +
                    (secondControl * (3.0 * inverse * amount * amount)) +
                    (end * (amount * amount * amount));
                AddSegment(outgoing.Id, previousPoint, point, isIntersectionConnector: true);
                previousPoint = point;
            }
        }
    }

    public Vector2D GetDirectionAtDistance(double distanceMeters)
    {
        if (!double.IsFinite(distanceMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(distanceMeters));
        }

        var clampedDistance = Math.Clamp(distanceMeters, 0.0, TotalLengthMeters);
        return _pathSegments.FirstOrDefault(candidate => clampedDistance <= candidate.EndDistanceMeters)?.Direction ??
            _pathSegments[^1].Direction;
    }

    public Vector2D GetPointAtDistance(double distanceMeters)
    {
        if (!double.IsFinite(distanceMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(distanceMeters));
        }

        var clampedDistance = Math.Clamp(distanceMeters, 0.0, TotalLengthMeters);
        var segment = _pathSegments.FirstOrDefault(candidate => clampedDistance <= candidate.EndDistanceMeters) ??
            _pathSegments[^1];
        var amount = segment.LengthMeters <= 1e-9
            ? 0.0
            : Math.Clamp((clampedDistance - segment.StartDistanceMeters) / segment.LengthMeters, 0.0, 1.0);
        return Vector2D.Lerp(segment.Start, segment.End, amount);
    }

    internal bool TryGetDistanceForLanePoint(string laneId, Vector2D point, out double routeDistanceMeters)
    {
        var found = false;
        var bestDistanceSquared = double.PositiveInfinity;
        routeDistanceMeters = 0.0;
        foreach (var segment in _pathSegments.Where(candidate => candidate.LaneId == laneId))
        {
            var projection = ProjectOntoSegment(point, segment);
            var distanceSquared = SquaredDistance(point, projection.Point);
            if (distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            found = true;
            bestDistanceSquared = distanceSquared;
            routeDistanceMeters = segment.StartDistanceMeters + (projection.Amount * segment.LengthMeters);
        }

        return found;
    }

    /// <summary>
    /// Projects a world point onto the route and returns only its public
    /// distance coordinate, keeping the internal segment representation private.
    /// </summary>
    public double ProjectProgress(
        Vector2D point,
        double minimumProgressMeters,
        double maximumProgressMeters = double.PositiveInfinity) =>
        Project(point, minimumProgressMeters, maximumProgressMeters).ProgressMeters;
    internal RoutePathProjection Project(
        Vector2D point,
        double minimumProgressMeters,
        double maximumProgressMeters = double.PositiveInfinity)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
            !double.IsFinite(minimumProgressMeters) ||
            double.IsNaN(maximumProgressMeters) ||
            maximumProgressMeters < minimumProgressMeters)
        {
            throw new ArgumentOutOfRangeException(nameof(point), "Projection values must be finite.");
        }

        var minimum = Math.Clamp(minimumProgressMeters, 0.0, TotalLengthMeters);
        var maximum = Math.Clamp(maximumProgressMeters, minimum, TotalLengthMeters);
        RoutePathProjection? best = null;
        var bestDistanceSquared = double.PositiveInfinity;
        foreach (var segment in _pathSegments)
        {
            if (segment.EndDistanceMeters + 1e-9 < minimum)
            {
                continue;
            }

            if (segment.StartDistanceMeters - 1e-9 > maximum)
            {
                break;
            }

            var projection = ProjectOntoSegment(point, segment);
            var minimumAmount = minimum <= segment.StartDistanceMeters
                ? 0.0
                : Math.Clamp((minimum - segment.StartDistanceMeters) / segment.LengthMeters, 0.0, 1.0);
            var maximumAmount = maximum >= segment.EndDistanceMeters
                ? 1.0
                : Math.Clamp((maximum - segment.StartDistanceMeters) / segment.LengthMeters, 0.0, 1.0);
            var amount = Math.Clamp(projection.Amount, minimumAmount, maximumAmount);
            var projectedPoint = Vector2D.Lerp(segment.Start, segment.End, amount);
            var distanceSquared = SquaredDistance(point, projectedPoint);
            if (distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            bestDistanceSquared = distanceSquared;
            best = new RoutePathProjection(
                segment,
                projectedPoint,
                segment.StartDistanceMeters + (amount * segment.LengthMeters),
                Math.Sqrt(distanceSquared));
        }

        return best ?? throw new InvalidOperationException("The route has no projectable geometry.");
    }

    private static (Vector2D Point, double Amount) ProjectOntoSegment(Vector2D point, RoutePathSegment segment)
    {
        var amount = Math.Clamp(
            Vector2D.Dot(point - segment.Start, segment.End - segment.Start) /
            (segment.LengthMeters * segment.LengthMeters),
            0.0,
            1.0);
        return (Vector2D.Lerp(segment.Start, segment.End, amount), amount);
    }

    private static double SquaredDistance(Vector2D a, Vector2D b)
    {
        var delta = a - b;
        return (delta.X * delta.X) + (delta.Y * delta.Y);
    }
}

internal sealed record RoutePathSegment(
    string LaneId,
    Vector2D Start,
    Vector2D End,
    Vector2D Direction,
    double StartDistanceMeters,
    double LengthMeters,
    bool IsIntersectionConnector)
{
    public double EndDistanceMeters => StartDistanceMeters + LengthMeters;
}

internal sealed record RoutePathProjection(
    RoutePathSegment Segment,
    Vector2D Point,
    double ProgressMeters,
    double DistanceMeters);
