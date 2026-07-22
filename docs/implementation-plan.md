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

## Milestone 3 — training readiness

Current progress:

- 3A complete: the eight-camera rig, drift-free physics-tick scheduler, synchronized atomic PNG capture, and high-level longitudinal brake allocator are implemented and validated.
- 3B complete in process: versioned contracts, the synchronous deterministic runner, and a pausable exact-substep Godot/Jolt adapter are implemented. External transport remains deferred until a trainer exists.
- 3C complete: route-relative ground truth, live rule/safety metrics, quality-gated reference driver, immutable episode recording, and fixed disjoint seed splits are implemented. All 24 configured Jolt routes pass the current quality gate.
- 3D not started: dynamic actors and scenario diversity remain the next major environment milestone.
- 3E partial: synchronized RGB recording with calibration/build metadata is complete; semantic/depth and other sensors plus the Python bridge remain.

### 3A — vehicle sensing and control semantics

- Add a configurable eight-camera exterior rig based on the documented 2024+ Model 3 locations: two windshield, two door-pillar, two front-fender, rear, and optional front-bumper positions.
- Keep camera intrinsics, extrinsics, resolution, frequency, and activation independent of the on-screen monitor.
- Give autonomous controllers a high-level deceleration request and a testable low-level brake allocator; retain the actuator-level channels used by manual controls, diagnostics, and plant integration.
- Timestamp sensor samples from simulation time rather than wall-clock time.

Exit condition: all camera channels have unique calibrated mounts and can be selected or captured without changing the vehicle controller.

### 3B — deterministic episode protocol

- Define versioned `Scenario`, `Reset`, `Action`, `Observation`, `StepResult`, termination, and metric contracts in `CarOrama.Core`.
- Run control at a configured rate over fixed physics substeps.
- Support seeded reset, explicit simulation ticks, headless operation, and faster-than-real-time stepping.
- Keep transport independent: the same contracts must serve an in-process test runner and a future gRPC/shared-memory bridge.

Exit condition: a test can reset a scenario and advance an exact number of ticks with repeatable structured results.

### 3C — privileged-state driving baseline

- Assign routes and expose lane-relative position, heading error, speed limit, route look-ahead, stop-line distance, and traffic-control state.
- Add collision, lane-departure, wrong-way, speeding, signal/stop-sign violation, comfort, route-progress, and energy metrics.
- Implement a non-learning pure-pursuit/Stanley steering baseline with longitudinal speed and stop control.
- Record deterministic episodes and split procedural seeds into training, validation, and held-out test sets.

Exit condition: the baseline completes unseen routes and produces trustworthy evaluation reports without rendered sensors.

### 3D — traffic and scenario coverage

- Add rule-aware road vehicles, pedestrians, cyclists, parked/occluding actors, and deterministic behavior controllers.
- Add lane changes, merges, blocked lanes, varied friction, lighting, and weather scenarios.
- Build curriculum tiers and reproducible regression cases from failures.

Exit condition: scenarios exercise interaction and traffic-law decisions rather than empty-road lane following.

### 3E — perception and learning bridge

- Add synchronized camera frames, depth/semantic ground truth, LiDAR, radar, GNSS, and IMU with configurable noise, latency, and dropout.
- Add dataset recording with calibration and build metadata.
- Connect Python workers only after the in-process protocol and metrics pass determinism tests.
- Begin with imitation learning from the privileged baseline, then reinforcement learning, and evaluate both against held-out seeds.

Exit condition: policies can be trained and evaluated without scene-node access or train/test leakage.

## Deliberately deferred until its milestone

- Driving agents and learning algorithms.
- LiDAR, radar, GNSS, IMU, and learned perception pipelines.
- Traffic vehicles, pedestrians, and traffic-flow simulation.
- Distributed worker orchestration and external training infrastructure.
- High-fidelity tire calibration and production vehicle assets.

## Work order

Static-road privileged-state and RGB imitation-learning experiments may now begin. Results must be described as lane/route-following research, not general autonomous driving. Broader camera-policy training must wait for scenario diversity in 3D and labels/extra sensors in 3E.
