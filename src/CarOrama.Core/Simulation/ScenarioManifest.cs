using CarOrama.Core.Roads;

namespace CarOrama.Core.Simulation;

public enum DatasetSplit
{
    Training,
    Validation,
    Test,
}

public sealed record ScenarioSplitConfiguration(
    IReadOnlyList<long> TrainingSeeds,
    IReadOnlyList<long> ValidationSeeds,
    IReadOnlyList<long> TestSeeds,
    int RoutesPerSeed = 2,
    int PhysicsTicksPerSecond = 120,
    int ControlTicksPerSecond = 20,
    long MaximumControlTicks = 4_000)
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentException($"Unsupported scenario split schema version {SchemaVersion}.");
        }

        ArgumentNullException.ThrowIfNull(TrainingSeeds);
        ArgumentNullException.ThrowIfNull(ValidationSeeds);
        ArgumentNullException.ThrowIfNull(TestSeeds);
        if (TrainingSeeds.Count == 0 || ValidationSeeds.Count == 0 || TestSeeds.Count == 0)
        {
            throw new ArgumentException("Training, validation, and test splits must each contain at least one seed.");
        }

        var allSeeds = TrainingSeeds.Concat(ValidationSeeds).Concat(TestSeeds).ToArray();
        if (allSeeds.Distinct().Count() != allSeeds.Length)
        {
            throw new ArgumentException("A procedural seed cannot occur in more than one dataset split.");
        }

        if (RoutesPerSeed <= 0 || MaximumControlTicks <= 0 ||
            PhysicsTicksPerSecond <= 0 || ControlTicksPerSecond <= 0 ||
            PhysicsTicksPerSecond < ControlTicksPerSecond ||
            PhysicsTicksPerSecond % ControlTicksPerSecond != 0)
        {
            throw new ArgumentException("Scenario cadence, route count, and duration must be positive and compatible.");
        }
    }
}

public sealed record ScenarioManifestEntry(
    string ScenarioId,
    DatasetSplit Split,
    long Seed,
    string RouteId,
    string SpawnPointId,
    string DestinationLaneId,
    IReadOnlyList<string> LaneIds,
    int PhysicsTicksPerSecond,
    int ControlTicksPerSecond,
    long MaximumControlTicks);

public sealed record ScenarioManifest(
    int SchemaVersion,
    int SimulationContractVersion,
    IReadOnlyList<ScenarioManifestEntry> Scenarios)
{
    public const int CurrentSchemaVersion = 1;

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion || SimulationContractVersion != SimulationContract.CurrentVersion)
        {
            throw new ArgumentException("The scenario manifest schema or simulation contract version is unsupported.");
        }

        ArgumentNullException.ThrowIfNull(Scenarios);
        if (Scenarios.Count == 0)
        {
            throw new ArgumentException("A scenario manifest cannot be empty.");
        }

        if (Scenarios.Select(scenario => scenario.ScenarioId).Distinct(StringComparer.Ordinal).Count() != Scenarios.Count)
        {
            throw new ArgumentException("Scenario identifiers must be unique.");
        }

        foreach (var scenario in Scenarios)
        {
            if (string.IsNullOrWhiteSpace(scenario.ScenarioId) ||
                string.IsNullOrWhiteSpace(scenario.RouteId) ||
                string.IsNullOrWhiteSpace(scenario.SpawnPointId) ||
                string.IsNullOrWhiteSpace(scenario.DestinationLaneId) ||
                scenario.LaneIds is null || scenario.LaneIds.Count == 0)
            {
                throw new ArgumentException("Every scenario requires stable identifiers and a non-empty lane route.");
            }

            _ = SimulationScenario.Create(
                scenario.ScenarioId,
                scenario.PhysicsTicksPerSecond,
                scenario.ControlTicksPerSecond,
                scenario.MaximumControlTicks);
        }

        var seedSplits = Scenarios
            .GroupBy(scenario => scenario.Seed)
            .Select(group => group.Select(scenario => scenario.Split).Distinct().Count());
        if (seedSplits.Any(splitCount => splitCount != 1))
        {
            throw new ArgumentException("All scenarios from one seed must remain in exactly one dataset split.");
        }
    }
}

public sealed class ScenarioManifestBuilder(IRoadNetworkGenerator? roadNetworkGenerator = null)
{
    private readonly IRoadNetworkGenerator _roadNetworkGenerator =
        roadNetworkGenerator ?? new GridRoadNetworkGenerator();

    public ScenarioManifest Build(ScenarioSplitConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        var scenarios = new List<ScenarioManifestEntry>();
        AddSplit(DatasetSplit.Training, configuration.TrainingSeeds);
        AddSplit(DatasetSplit.Validation, configuration.ValidationSeeds);
        AddSplit(DatasetSplit.Test, configuration.TestSeeds);

        var manifest = new ScenarioManifest(
            ScenarioManifest.CurrentSchemaVersion,
            SimulationContract.CurrentVersion,
            scenarios);
        manifest.Validate();
        return manifest;

        void AddSplit(DatasetSplit split, IReadOnlyList<long> seeds)
        {
            foreach (var seed in seeds.Order())
            {
                var network = _roadNetworkGenerator.Generate(new RoadNetworkConfig { Seed = seed });
                var orderedSpawns = network.SpawnPoints.OrderBy(spawn => spawn.Id, StringComparer.Ordinal).ToArray();
                var startingIndex = PositiveModulo(StableSeedHash(seed), orderedSpawns.Length);
                for (var routeIndex = 0; routeIndex < configuration.RoutesPerSeed; routeIndex++)
                {
                    var searchStart = startingIndex + (routeIndex * 17);
                    SpawnPoint? spawn = null;
                    IReadOnlyList<string>? route = null;
                    for (var offset = 0; offset < orderedSpawns.Length; offset++)
                    {
                        var candidateSpawn = orderedSpawns[(searchStart + offset) % orderedSpawns.Length];
                        route = TrySelectRoute(network, candidateSpawn.LaneId, routeIndex);
                        if (route is not null)
                        {
                            spawn = candidateSpawn;
                            break;
                        }
                    }

                    if (spawn is null || route is null)
                    {
                        throw new InvalidOperationException($"Seed {seed} has no useful non-U-turn route.");
                    }

                    var splitName = split.ToString().ToLowerInvariant();
                    var scenarioId = $"{splitName}-seed-{FormatSeed(seed)}-route-{routeIndex:D2}";
                    scenarios.Add(new ScenarioManifestEntry(
                        scenarioId,
                        split,
                        seed,
                        $"route-{FormatSeed(seed)}-{routeIndex:D2}",
                        spawn.Id,
                        route[^1],
                        route,
                        configuration.PhysicsTicksPerSecond,
                        configuration.ControlTicksPerSecond,
                        configuration.MaximumControlTicks));
                }
            }
        }
    }

    private static IReadOnlyList<string>? TrySelectRoute(RoadNetwork network, string startLaneId, int routeIndex)
    {
        var candidates = network.Lanes
            .Where(lane => lane.Id != startLaneId)
            .Select(lane => RoutePlanner.FindLaneRoute(network, startLaneId, lane.Id))
            .Where(route => route.Count >= 3)
            .Where(route => HasNoImmediateUTurn(network, route))
            .OrderByDescending(route => route.Count)
            .ThenBy(route => route[^1], StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        // Rotate within the longest quarter so multiple episodes remain substantial
        // without selecting the exact same destination for every seed.
        var candidateWindow = Math.Max(1, candidates.Length / 4);
        return candidates[routeIndex % candidateWindow];
    }

    private static bool HasNoImmediateUTurn(RoadNetwork network, IReadOnlyList<string> route)
    {
        for (var index = 1; index < route.Count; index++)
        {
            var previous = network.GetLane(route[index - 1]);
            var current = network.GetLane(route[index]);
            if (CarOrama.Core.Geometry.Vector2D.Dot(previous.Direction, current.Direction) < -0.5)
            {
                return false;
            }
        }

        return true;
    }

    private static int StableSeedHash(long seed)
    {
        unchecked
        {
            var value = (ulong)seed;
            value ^= value >> 33;
            value *= 0xff51afd7ed558ccdUL;
            value ^= value >> 33;
            return (int)(value ^ (value >> 32));
        }
    }

    private static int PositiveModulo(int value, int divisor) => (int)((uint)value % (uint)divisor);

    private static string FormatSeed(long seed) => seed < 0
        ? $"neg-{Math.Abs(seed):D6}"
        : seed.ToString("D6");
}
