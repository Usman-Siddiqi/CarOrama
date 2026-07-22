# CarOrama

CarOrama is a long-term autonomous-driving simulation project. The current milestone provides a deterministic, machine-readable road environment, one manually drivable electric vehicle, and an engine-independent reference episode loop. It intentionally stops before learned policies, traffic participants, or simulated perception datasets.

## Current capabilities

- Seeded, connected road graphs with local streets and multi-lane arterials, right-hand directed lanes, boundaries, centre lines, speed limits, stop lines, traffic controls, routes, and spawn points.
- Runtime-built roads with yellow centre treatments, white lane dividers and edge lines, stop bars, zebra crossings, directional arrows, concrete curbs, sidewalks, signs, actuated traffic signals, and lightweight roadside scenery.
- Vehicle-responsive traffic lights with lane-level approach detection, minimum/maximum green timing, passage-gap extension, yellow transition, and an all-red clearance interval.
- A rigid-body electric vehicle with a single-speed motor model, configurable driven axles, regenerative and friction braking, ray-cast suspension, tire forces, collision handling, battery state of charge, exterior lighting, a follow camera, and an independently switchable eight-camera exterior rig with a live engineering monitor.
- Separate motion and exterior-light command boundaries that keep keyboard/controller input replaceable by future driving controllers.
- A versioned reset/observe/step protocol with exact control/physics ticks, deterministic route ground truth, cumulative metrics, and terminal/truncation semantics.
- A faster-than-real-time reference environment using the EV drivetrain, a bicycle motion model, regenerative/friction brake allocation, and a non-learning route-following baseline.
- Engine-independent domain code and automated tests for the road graph, electric drivetrain, episode loop, and baseline controller.

## Technical stack

| Area | Selection | Reason |
|---|---|---|
| Engine | Godot 4.6.1 .NET | Open-source runtime, source access, procedural 3D APIs, command-line/headless operation, and low-overhead multi-instance execution. |
| Language | C# 12 on .NET 8 | Strong tooling and type safety, fast iteration, mature serialization/networking, and a clean path to native C++ extensions for measured hotspots. |
| Physics | Built-in Jolt Physics plus a custom force-based vehicle layer | Jolt supplies robust rigid bodies, collision queries, and constraints. CarOrama owns motor, braking, suspension, and tire-force models instead of hiding them behind game-oriented vehicle behavior. |
| Future ML | Out-of-process Python/PyTorch service over a versioned simulation protocol | Training dependencies remain independent of rendering and physics. The simulator can later expose batched observations/actions, deterministic resets, and faster-than-real-time stepping without embedding Python in the scene. |
| Tests | xUnit for the engine-independent core; Godot headless smoke validation for integration | Domain invariants run quickly without an editor, while integration checks exercise the real engine. |
| Development | Git, .NET 8 SDK, Godot 4.6.1 .NET, and any C# IDE | The editor is used for runtime inspection; normal code and tests remain CLI-friendly. |

### Why this stack

CarOrama needs ownership of the simulation contract more than it needs a game-engine-specific learning package. Godot provides rendering, scene composition, input, and an open native runtime while the C# domain layer keeps road semantics and drivetrain calculations portable and independently testable. Jolt handles general rigid-body contact, but tire, suspension, motor, brake, and energy behavior are explicit modules that can be calibrated or replaced.

The main alternatives were Unreal Engine with Chaos Vehicles and Unity with PhysX/ML-Agents. Unreal is an excellent choice for maximum sensor-image fidelity and is proven in automotive simulators, but its asset-heavy vehicle workflow, build footprint, and slower headless iteration would make the simulation core harder to own at this stage. Unity offers a mature learning toolkit, but couples the project more closely to a proprietary training integration whose development cadence and Python constraints are outside this project's control. The chosen boundary can still use PyTorch, RLlib, Stable-Baselines3, imitation-learning pipelines, or a custom trainer later.

If future camera-domain research requires a different renderer, the engine-independent road model, scenario seeds, control contract, metrics schema, and training protocol are intended to survive a renderer migration.

## Architecture

```text
src/
  CarOrama.Core/       deterministic road, routing, vehicle, and control domain
  CarOrama.Game/       Godot scene construction, Jolt integration, input, camera, UI
tests/
  CarOrama.Core.Tests/ fast domain tests
docs/                  decisions, implementation plan, and extension guidance
```

The dependency direction is one-way: `CarOrama.Game -> CarOrama.Core`. Core code never references Godot. Future sensor, agent, scenario-runner, and telemetry adapters will depend on stable domain interfaces rather than player input or scene-node details.

See [Architecture](docs/architecture.md), [Implementation plan](docs/implementation-plan.md), and the [Extension guide](docs/extending.md) for the training-readiness gates, detailed boundaries, and roadmap.

## Prerequisites

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Install the [Godot 4.6.1 .NET editor](https://godotengine.org/download/archive/4.6.1-stable/). The standard build does not include C# support.
3. Clone the repository.

No external art assets or runtime packages are required for the current milestone.

## Run

Open `src/CarOrama.Game/project.godot` in the Godot .NET editor and press **F6** or **F5**. The scene is built entirely at runtime.

From a terminal, replace `godot` with the path to the Godot .NET executable if it is not on `PATH`:

```powershell
godot --path src/CarOrama.Game --editor
godot --path src/CarOrama.Game
```

Controls:

| Action | Keyboard | Controller |
|---|---|---|
| Steer | A/D or Left/Right | Left stick |
| Throttle | W or Up | Right trigger |
| Brake / reverse | S or Down | Left trigger |
| Emergency friction brake | Space | South face button |
| Left/right turn signal | Q / E | D-pad left/right |
| Hazard lights | X | D-pad down |
| Headlights | H | D-pad up |
| Cycle monitored vehicle camera | C | Right shoulder button |
| Reset vehicle | R | Start |
| New deterministic seed | N | North face button |

The current seed and compact vehicle telemetry are shown on screen. The default seed can also be changed in `project.godot` or with `-- --seed 12345`.

## Test and validate

```powershell
dotnet test CarOrama.sln --configuration Release
godot --headless --path src/CarOrama.Game --build-solutions --quit
godot --headless --path src/CarOrama.Game -- --smoke-test
```

The smoke test creates a world, validates its structured network, advances the vehicle briefly, and exits with a non-zero status on failure. See [Validation](docs/validation.md) for the expected checks.

To run the complete sequence with one command, set `CARORAMA_GODOT` to the Godot .NET executable if `godot` is not on `PATH`, then run:

```powershell
./scripts/validate.ps1
```

## Current scope and next extensions

This milestone does **not** contain learned driving policies, neural networks, reinforcement or imitation learning, sensor recording or perception models, LiDAR/radar, traffic vehicles, pedestrians, or a traffic simulation. The included privileged-state controller is a deterministic reference baseline, not a learned policy.

The next architectural increments should be:

1. Add a Godot rigid-body episode adapter that implements the same fixed-step semantics as the fast reference environment.
2. Complete collision and traffic-law metrics, deterministic episode recording, and train/validation/held-out seed manifests.
3. Add synchronized sensor capture and ground-truth labels before LiDAR, radar, GNSS, and IMU implementations.
4. Add OpenDRIVE import/export and richer lane topology while keeping stable internal identifiers.
5. Add batched headless workers and dataset infrastructure, and only then connect Python training code.

## License

CarOrama is available under the [MIT License](LICENSE).
