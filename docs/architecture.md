# Architecture

## Decision record

**Status:** accepted for the environment and vehicle foundations  
**Engine baseline:** Godot 4.6.1 .NET  
**Runtime baseline:** .NET 8 / C# 12  
**Physics baseline:** built-in Jolt Physics with a CarOrama-owned vehicle-force model

### Quality priorities

In descending order:

1. Explicit simulation semantics and reproducibility.
2. Maintainable boundaries between domain state and presentation.
3. Physically meaningful, replaceable vehicle subsystems.
4. Headless throughput and automation.
5. Visual fidelity that can grow with sensor requirements.

The simulator is not expected to be bit-identical across processors. A seed reproduces topology, identifiers, controls, and scenario parameters; fixed-step integration and recorded build metadata will later provide bounded numerical repeatability.

## Dependency boundaries

```text
manual input ─┐
future agent ─┼─> IVehicleCommandSource ─> VehicleCommand ─> EVVehicle
replay input ─┘                                      │
                                                    ├─ MotorModel
                                                    ├─ BatteryModel
                                                    ├─ Brake blending
                                                    ├─ Tire forces
                                                    └─ Suspension forces

manual input ─┐
future agent ─┼─> IVehicleLightingCommandSource ─> lights / indicators
replay input ─┘

RoadGenerator ─> RoadNetwork ─┬─> RoadSceneBuilder
                              ├─> routing / spawn queries
                              ├─> route-relative ground truth
                              └─> episode runner / metrics
```

`CarOrama.Core` contains immutable or narrowly mutable domain data, deterministic algorithms, validation, and testable drivetrain calculations. It cannot reference scene nodes, materials, input devices, or wall-clock time.

`CarOrama.Game` is an adapter. It turns the domain graph into Godot nodes, applies commands to Jolt rigid bodies, reads manual devices, and displays telemetry. Scene objects may reference stable domain IDs; domain objects never reference scene objects.

## Road representation

The first generator produces a connected Manhattan-style graph, not unconstrained geometry. A seeded randomized spanning tree guarantees reachability, deterministic optional links add loops, and a connected central arterial provides repeatable multi-lane coverage. Nodes and segments receive stable IDs derived from grid coordinates rather than creation order.

Each segment identifies its road classification and lanes per direction. Local roads and arterials therefore differ in structured topology, width, speed limit, and markings rather than only in visual scale. Directed lanes follow right-hand traffic. Junction insets use the widest incident road so lane endpoints, zebra crossings, and stop lines remain outside the shared conflict area when a local street meets an arterial.

Each directed lane stores:

- centre-line points and left/right boundaries;
- travel direction and metric speed limit;
- predecessor/successor lane IDs through intersections;
- the containing road-segment ID;
- a stop-line ID when applicable.

Intersections store approaches, controls, and positions. A traffic control belongs to one road approach and references every incoming lane it governs. Stop signs are placed beside the near-side stop line. Traffic-light controls drive synchronized heads mounted above each governed lane on a far-side mast arm, separating one logical control state from its potentially multiple visual heads. Spawn points reference a directed lane and heading. Routes are graph searches over lane successors, so the same information used to draw roads is usable by navigation and evaluation code.

Traffic-light intersections use a deterministic two-phase actuated controller. Opposing approaches share a protected phase, while perpendicular approaches remain red. Lane-level virtual detectors cover the final 34 metres before each stop line. Competing demand triggers a change only after minimum green; continuing traffic can extend green to a maximum, followed by a 3.5-second yellow and a 1-second all-red clearance interval. With no competing demand, the active phase rests on green. Timing and state transitions live in `CarOrama.Core`, independent of Godot rendering, and the current runtime state is available through `RoadWorld.GetTrafficSignalState` for future rule evaluation and agent observations.

The current topology is intentionally simple. Future generators can implement suburbs, arterials, roundabouts, ramps, grades, and OpenDRIVE import behind `IRoadNetworkGenerator` without changing consumers.

## Vehicle representation

The vehicle is a Jolt rigid body with an intentionally low centre of mass. Four suspension rays compute spring/damper forces at wheel locations. Applying tire forces at those contact patches creates pitch, roll, and load transfer naturally instead of faking chassis animation.

Longitudinal force flows through explicit modules:

```text
throttle -> motor torque limit -> power limit -> reduction gear -> driven wheels
regen command -> battery acceptance / motor limit -> driven wheels
brake command -> mechanical brake limit -> all wheels
wheel work -> drivetrain efficiency -> battery state of charge
```

The tire model begins with clamped longitudinal and lateral force proportional to contact-patch slip velocity. It is a stable foundation, not a claim of high-fidelity tire simulation. The module boundary allows replacement by Pacejka, brush, or measured lookup-table models. Suspension geometry can later move from ray casts to constrained unsprung masses without changing the command or drivetrain contracts.

## Future autonomous-driving boundary

The simulation protocol has explicit `Reset(Scenario)`, `Observe()`, and `Step(Action)` boundaries; each step advances one configured control tick containing an integer number of fixed physics ticks. Learned and manual controllers should request steering and longitudinal acceleration/deceleration; `LongitudinalCommandAllocator` converts deceleration into speed- and battery-dependent regeneration plus any required friction braking. The existing actuator-level `VehicleCommand` remains the plant boundary and diagnostic interface. Observations are timestamped bundles from sensor adapters plus route and optional privileged ground truth.

`DeterministicDrivingEnvironment` combines the production electric drivetrain with deterministic bicycle-model planar motion so protocol, reward, route-query, and controller tests can run synchronously and faster than real time without rendering. `GodotDrivingEnvironment` implements the same contract over the real Jolt rigid body, pausing between actions and at requested sensor ticks so controller or rendering latency cannot advance the simulation. `DrivingRoute` adds explicit intersection connectors to the lane sequence, and `RoadGroundTruthQuery` supplies lane offset, heading error, route look-ahead, stop-line distance, traffic-control state, and safety flags directly from structured data.

`PrivilegedRouteFollower` consumes only `PrivilegedObservation` and produces tick-addressed `DrivingAction` values. Its pure-pursuit steering, speed control, signal stopping, and stop-sign hold behavior provide a deterministic evaluation baseline and future demonstration source without coupling a controller to road or scene internals.

The exterior camera layout follows the locations in the [2024+ Model 3 owner's manual](https://www.tesla.com/ownersmanual/model3/en_us/GUID-682FF4A7-D083-4C95-925A-5EE3752F4865.html) for a vehicle equipped with the optional front camera: front bumper, rear plate, two windshield, two door-pillar, and two front-fender channels. Camera count and approximate coverage follow the public layout, while intrinsics and extrinsics remain CarOrama-owned configuration because production calibration values are not public. Sensor viewports are independent of the dashboard monitor and may remain disabled in privileged/headless runs to avoid rendering cost.

Exterior lights use a separate `VehicleLightingCommand`. This allows a scenario controller to command headlights, indicators, and hazards without depending on input actions or lamp scene nodes; brake lamps remain a deterministic consequence of braking commands.

Python will remain a separate process. A versioned gRPC or shared-memory transport will allow local training, distributed workers, recorded replay, and independent simulator releases. No learning framework will be referenced from scene or physics assemblies.

Both runners report elapsed simulation time, distance, route completion, collision and lane-departure events, lane-departure and speeding duration, stop/signal violations, mean absolute jerk, and net battery energy. The Godot adapter evaluates live signal state and names the contacted body on collision termination. Collision impulse, lateral acceleration comfort, interventions, and per-worker throughput remain useful future metric extensions.

## Performance strategy

- Build repeated scenery with multimeshes when counts justify it.
- Generate topology and geometry from compact arrays, with stable spatial indices.
- Separate render-dependent sensors from ground-truth queries.
- Use fixed physics ticks and disable rendering for state-only workers.
- Profile before moving C# hotspots to a C++ GDExtension.
- Pool scenarios and nodes once reset cost becomes material.

## Rejected primary stacks

### Unreal Engine and Chaos

Excellent rendering and an automotive-proven ecosystem, but significantly heavier builds and content pipelines. Standard Chaos vehicle setup is asset-oriented; owning and testing the force model separately would still require substantial custom work. It remains the preferred migration target if photorealistic camera research outweighs headless iteration cost.

### Unity and PhysX with ML-Agents

Strong iteration, tooling, and an existing Python training bridge. It was not selected because the project's core training protocol should not depend on one toolkit, and because an open engine/physics stack is valuable for a long-lived portfolio simulator.

### CARLA as the application foundation

CARLA already supplies many eventual features, but using it as the foundation would shift the project toward configuring an existing simulator. CarOrama instead owns its road semantics, vehicle architecture, reset loop, and evaluation contract. CARLA remains a valuable behavioral and sensor-validation reference.
