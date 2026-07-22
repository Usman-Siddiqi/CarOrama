# CarOrama

CarOrama is a long-term autonomous-driving simulation project. The current milestone provides a deterministic, machine-readable road environment, one manually drivable electric vehicle, fixed-step reference and Godot/Jolt episode loops, and a crash-safe synchronized RGB dataset recorder. It intentionally stops before learned policies and dynamic traffic participants.

## Current capabilities

- Seeded, connected road graphs with local streets and multi-lane arterials, right-hand directed lanes, boundaries, centre lines, speed limits, stop lines, traffic controls, routes, and spawn points.
- Runtime-built roads with yellow centre treatments, white lane dividers and edge lines, stop bars, zebra crossings, directional arrows, concrete curbs, sidewalks, signs, actuated traffic signals, and lightweight roadside scenery.
- Vehicle-responsive traffic lights with lane-level approach detection, minimum/maximum green timing, passage-gap extension, yellow transition, and an all-red clearance interval.
- A rigid-body electric vehicle with a single-speed motor model, configurable driven axles, regenerative and friction braking, ray-cast suspension, tire forces, collision handling, battery state of charge, exterior lighting, a follow camera, and an independently switchable eight-camera exterior rig with a live engineering monitor.
- Separate motion and exterior-light command boundaries that keep keyboard/controller input replaceable by future driving controllers.
- A versioned reset/observe/step protocol with exact control/physics ticks, deterministic route ground truth, cumulative metrics, and terminal/truncation semantics.
- A faster-than-real-time reference environment using the EV drivetrain, a bicycle motion model, regenerative/friction brake allocation, and a non-learning route-following baseline.
- A pausable Godot/Jolt episode adapter that advances an exact integer number of physics ticks per action and remains paused while an external controller or recorder works.
- Fixed training, validation, and held-out test seed manifests; route-aware collision, lane, speeding, stop-sign, red-light, comfort, energy, and completion metrics; and quality-gated reference demonstrations.
- Drift-free simulation-time camera scheduling, atomic PNG capture for all eight calibrated views, JSON Lines action/observation indexing, calibration/build metadata, and crash-detectable episode and dataset completion markers.
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
tools/                 manifest export and fast reference recording CLI
config/                fixed scenario splits
scripts/               validation and Godot/Jolt dataset recording entry points
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
godot --headless --path src/CarOrama.Game -- --episode-smoke-test
```

The smoke test creates a world, validates its structured network, advances the vehicle briefly, and exits with a non-zero status on failure. See [Validation](docs/validation.md) for the expected checks.

To run the complete sequence with one command, set `CARORAMA_GODOT` to the Godot .NET executable if `godot` is not on `PATH`, then run:

```powershell
./scripts/validate.ps1
```

## Record a dataset

The default recorder runs the privileged reference driver through the real Godot/Jolt vehicle and captures all eight RGB channels. Output is placed under the ignored `artifacts/datasets` directory and is never overwritten:

```powershell
$env:CARORAMA_GODOT = 'C:\path\to\Godot_v4.6.1-stable_mono_win64.exe'
./scripts/record-dataset.ps1 -Split Training -MaxEpisodes 1 -DatasetId first-camera-run
```

Use state-only mode for fast physics and quality regression without rendering:

```powershell
./scripts/record-dataset.ps1 -NoCameras -DatasetId jolt-regression
```

The fixed-FPS offline runner is faster than real time on suitable hardware. A recording succeeds only when every selected route completes with no collision, lane departure, red-light violation, rolling stop, or speeding interval. See [Dataset format](docs/dataset-format.md) for the layout and lifecycle rules.

## Current scope and next extensions

This milestone does **not** contain learned driving policies, neural networks, reinforcement learning, semantic/depth perception labels, LiDAR/radar, traffic vehicles, pedestrians, or traffic simulation. The included privileged-state controller is a deterministic reference driver and demonstration source, not a learned policy.

The repository is now suitable for collecting clean static-road privileged-state or RGB imitation-learning data. It is not yet a sufficient dataset source for a general self-driving policy: dynamic traffic actors, pedestrians, occlusions, weather/lighting diversity, semantic labels, and a separate Python training/evaluation bridge remain required.

The next architectural increments should be:

1. Add deterministic traffic vehicles, pedestrians, cyclists, parked/occluding actors, and scenario curricula.
2. Add semantic/depth labels, then LiDAR, radar, GNSS, and IMU with configurable noise, latency, and dropout.
3. Add OpenDRIVE import/export, grades, ramps, roundabouts, and richer lane topology while keeping stable identifiers.
4. Add batched workers and a separate Python training/evaluation package over the versioned protocol.

## License

CarOrama is available under the [MIT License](LICENSE).
