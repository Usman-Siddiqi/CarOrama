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

RoadGenerator ─> RoadNetwork ─┬─> RoadSceneBuilder
                              ├─> routing / spawn queries
                              ├─> future ground-truth sensors
                              └─> future scenario and metrics services
```

`CarOrama.Core` contains immutable or narrowly mutable domain data, deterministic algorithms, validation, and testable drivetrain calculations. It cannot reference scene nodes, materials, input devices, or wall-clock time.

`CarOrama.Game` is an adapter. It turns the domain graph into Godot nodes, applies commands to Jolt rigid bodies, reads manual devices, and displays telemetry. Scene objects may reference stable domain IDs; domain objects never reference scene objects.

## Road representation

The first generator produces a connected Manhattan-style graph, not unconstrained geometry. A seeded randomized spanning tree guarantees reachability, then deterministic optional links add loops. Nodes and segments receive stable IDs derived from grid coordinates rather than creation order.

Each directed lane stores:

- centre-line points and left/right boundaries;
- travel direction and metric speed limit;
- predecessor/successor lane IDs through intersections;
- the containing road-segment ID;
- a stop-line ID when applicable.

Intersections store approaches, controls, and positions. Spawn points reference a directed lane and heading. Routes are graph searches over lane successors, so the same information used to draw roads is usable by navigation and evaluation code.

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

The planned simulation protocol will have explicit `Reset(Scenario)`, `Observe()`, and `Step(Action, ticks)` operations. Actions map to normalized steering, throttle, regenerative braking, and friction braking—the same `VehicleCommand` used manually. Observations will be timestamped bundles from sensor adapters plus route and optional privileged ground truth.

Python will remain a separate process. A versioned gRPC or shared-memory transport will allow local training, distributed workers, recorded replay, and independent simulator releases. No learning framework will be referenced from scene or physics assemblies.

Planned metrics include collision impulse, lane departure duration, stop-line and signal violations, route progress/completion, jerk/lateral acceleration, intervention count, energy use, and wall-clock simulation throughput.

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

