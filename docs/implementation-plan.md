# Implementation plan

## Milestone 0 — foundation

- Select the engine, language, physics, testing, and future learning boundary.
- Establish dependency rules and repository structure.
- Keep generated engine state, binaries, datasets, and local configuration out of source control.

Exit condition: a minimal scene and both projects build from a clean checkout.

## Milestone 1 — procedural driving environment

- Define stable domain records for nodes, segments, lanes, boundaries, controls, stop lines, spawns, and routes.
- Generate a connected grid-derived graph from a seed.
- Link directed lanes through intersections and validate every reference.
- Build local and multi-lane arterial roads, intersection surfaces, complete applicable markings, curbs, sidewalks, signs, signals, and basic scenery at runtime.
- Expose the `RoadNetwork` independently of visual nodes.
- Add unit tests for determinism, connectivity, geometry invariants, routing, and seed variation.

Exit condition: multiple seeds create valid, navigable road networks and the headless scene smoke test succeeds.

## Milestone 2 — electric vehicle foundation

- Add normalized vehicle commands and a replaceable command-source interface.
- Add configurable motor, reduction gear, driven axles, regenerative braking, friction brakes, mass, battery, and state of charge.
- Integrate four-wheel ray-cast suspension and clamped tire forces with a Jolt rigid body.
- Add stable keyboard/controller input, reset behavior, telemetry, follow and bumper cameras, and functional exterior lighting.
- Keep motion and exterior-light commands behind replaceable controller interfaces.
- Add tests for torque/power limits, regeneration, energy conservation direction, input clamping, and drivetrain configuration.
- Add repeatable acceleration/braking smoke scenarios.

Exit condition: one vehicle can be driven through the generated network without a controller depending on scene internals.

## Deliberately deferred

- Driving agents and learning algorithms.
- Camera, LiDAR, radar, GNSS, IMU, and perception pipelines.
- Traffic vehicles, pedestrians, and traffic-flow simulation.
- Dataset and distributed-training infrastructure.
- High-fidelity tire calibration and production vehicle assets.

## Future milestones

1. Simulation clock, reset/step service, scenario manifest, and telemetry schema.
2. Ground-truth queries and non-visual sensors.
3. Rendered sensors with calibration/noise models.
4. Traffic participants and rule-aware scenario generation.
5. Python training client, vectorized workers, replay, and evaluation dashboards.
