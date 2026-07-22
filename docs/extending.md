# Extension guide

## Add a road generator

Implement `IRoadNetworkGenerator` in `CarOrama.Core`. Return the same `RoadNetwork` records and run `RoadNetworkValidator.Validate` before exposing the result. Stable identifiers must come from semantic coordinates or source data, not transient scene-node order.

Add generator tests for:

- repeatability for a fixed seed and configuration;
- connected road and directed-lane graphs;
- balanced lane counts and road-width/classification agreement;
- boundary/centre-line agreement;
- valid successor and stop-line references;
- viable spawn-to-destination routes.

`RoadSceneBuilder` is deliberately a consumer. Extend it for curved meshes or replace it with another renderer without adding navigation decisions to the scene layer.

## Add or replace a vehicle subsystem

Motor, battery, mechanical brake, tire, and suspension models live under `CarOrama.Core/Vehicles`. Each accepts metric inputs and returns forces, torque, power, or state; none reads Godot nodes or input devices.

To replace a model:

1. Preserve or deliberately version its metric contract.
2. Add engine-independent reference tests with physical limits and expected trends.
3. Inject or construct it at the `ElectricVehicle` adapter boundary.
4. Re-run the headless scripted drive scenario and record calibration changes.

The current tire model is a friction-circle-limited linear model, and the suspension is a ray-cast spring/damper model. More advanced replacements should keep per-wheel contact forces explicit so weight transfer and metrics remain observable.

## Add a control source

Implement `IVehicleCommandSource.ReadCommand`. Return a sanitized `VehicleCommand` containing normalized steering, throttle, regenerative brake, and friction brake values.

Manual controls and the scripted validation controller already use this boundary. A future remote or autonomous controller should be selected by the scenario runner and must not access `ElectricVehicle` internals, input actions, or visual nodes.

Exterior lamps use the parallel `IVehicleLightingCommandSource` boundary. A controller can provide both interfaces or inject separate sources. Turn signals, hazards, and headlights are explicit commands; running lamps and brake-lamp intensity are resolved by the vehicle lighting adapter.

## Add a future sensor

Use two layers:

1. A domain-facing sensor contract with timestamp, frame, calibration, and typed payload.
2. A Godot adapter that samples rendering or physics and produces that payload.

Ground-truth queries should use `RoadNetwork` identifiers and spatial data. Rendered perception sensors should not become the source of lane, signal, or route truth. Sensor scheduling must use the simulation clock rather than wall-clock time.

## Add a future training bridge

Do not add Python dependencies to either existing project. Introduce a versioned protocol assembly and a scenario-runner service with explicit reset/observe/step operations. Then create a separate Python package that communicates over gRPC or shared memory.

The initial in-process boundary is `ISimulationEnvironment`. `SimulationScenario` fixes an integer physics/control cadence, `EpisodeResetRequest` binds the seed/route/spawn, and each tick-addressed `DrivingAction` requests steering plus longitudinal acceleration. The environment returns `PrivilegedObservation` and `EpisodeStepResult` values without exposing Godot nodes. A future transport should map these semantics to explicit wire DTOs or codecs, preserve validation at the domain boundary, and prove round-trip compatibility in tests instead of exposing a second behavioral API.

`DeterministicDrivingEnvironment` is the fast reference implementation. Construct a validated `DrivingRoute` from `RoutePlanner` lane IDs, reset with the matching road seed and spawn, then pass each observation to a controller:

```csharp
var laneIds = RoutePlanner.FindLaneRoute(network, spawn.LaneId, destinationLaneId);
var route = DrivingRoute.Create("evaluation-route", network, laneIds);
var scenario = SimulationScenario.Create("evaluation", 120, 20, 2_400);
var environment = new DeterministicDrivingEnvironment(network, route);
var controller = new PrivilegedRouteFollower();
var observation = environment.Reset(
    EpisodeResetRequest.Create(scenario, network.Seed, route.Id, spawn.Id));

while (true)
{
    var result = environment.Step(controller.GetAction(observation));
    observation = result.Observation;
    if (result.IsTerminal)
    {
        break;
    }
}
```

This reference loop intentionally uses deterministic planar motion. A Godot environment adapter must advance the Jolt rigid body asynchronously over the same configured tick schedule and populate collision/contact metrics without changing these controller-facing semantics.

The protocol should support:

- seed and scenario manifests;
- fixed action and observation timestamps;
- independent sensor rates;
- repeated action stepping;
- terminal/truncation reasons;
- route and evaluation metrics;
- optional privileged ground truth for training only;
- capability negotiation so recorded datasets remain readable.

## Data and assets

Large maps, recordings, trained weights, and generated datasets should not be committed directly to Git. Define a versioned manifest and storage policy before adding them. Source assets need documented provenance and compatible licenses.
