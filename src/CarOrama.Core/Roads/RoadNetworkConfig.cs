namespace CarOrama.Core.Roads;

public sealed record RoadNetworkConfig
{
    public long Seed { get; init; } = 42;

    public int GridWidth { get; init; } = 5;

    public int GridHeight { get; init; } = 5;

    public double BlockSizeMeters { get; init; } = 72.0;

    public double LaneWidthMeters { get; init; } = 3.6;

    public int LocalLanesPerDirection { get; init; } = 1;

    public int ArterialLanesPerDirection { get; init; } = 2;

    public double SidewalkWidthMeters { get; init; } = 2.4;

    public double IntersectionApproachSetbackMeters { get; init; } = 4.2;

    public double OptionalConnectionProbability { get; init; } = 0.42;

    public double ArterialSpeedLimitMetersPerSecond { get; init; } = 13.89;

    public double LocalSpeedLimitMetersPerSecond { get; init; } = 11.11;

    public void Validate()
    {
        if (GridWidth < 2 || GridHeight < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(GridWidth), "The road grid must contain at least two nodes per axis.");
        }

        if (BlockSizeMeters < 25.0)
        {
            throw new ArgumentOutOfRangeException(nameof(BlockSizeMeters), "Blocks must leave useful distance between intersections.");
        }

        if (LaneWidthMeters is < 2.5 or > 5.0)
        {
            throw new ArgumentOutOfRangeException(nameof(LaneWidthMeters));
        }

        if (LocalLanesPerDirection is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(LocalLanesPerDirection));
        }

        if (ArterialLanesPerDirection is < 2 or > 4 || ArterialLanesPerDirection < LocalLanesPerDirection)
        {
            throw new ArgumentOutOfRangeException(nameof(ArterialLanesPerDirection));
        }

        if (IntersectionApproachSetbackMeters is < 3.5 or > 12.0)
        {
            throw new ArgumentOutOfRangeException(nameof(IntersectionApproachSetbackMeters));
        }

        var minimumBlockSize =
            (2.0 * ((ArterialLanesPerDirection * LaneWidthMeters) + IntersectionApproachSetbackMeters)) + 5.0;
        if (BlockSizeMeters < minimumBlockSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BlockSizeMeters),
                "Blocks must leave usable lane length between the configured intersection approaches.");
        }

        if (OptionalConnectionProbability is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(OptionalConnectionProbability));
        }
    }
}
