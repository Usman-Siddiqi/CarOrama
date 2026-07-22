# Validation

## Automated checks

Run from the repository root:

```powershell
dotnet test CarOrama.sln --configuration Release
godot --headless --path src/CarOrama.Game --build-solutions --quit
godot --headless --path src/CarOrama.Game -- --smoke-test
```

The core suite checks:

- identical seeds produce equivalent serialized road networks;
- different seeds vary optional topology while retaining connectivity;
- local and arterial segments expose balanced lane counts, matching widths, and deterministic classification;
- every directed lane has valid boundaries, direction, segment, and successor references;
- each controlled approach has one control covering all of its incoming lanes;
- routes contain connected lanes and spawns reference valid lanes;
- motor output obeys torque, power, speed, and gear limits;
- regenerative braking cannot charge above battery/motor acceptance limits;
- friction braking remains independent from regenerative braking;
- normalized commands are finite and clamped.
- exterior-light commands normalize turn-signal state and resolve hazard outputs.
- actuated signals honor minimum/maximum green, passage-gap, yellow, and all-red timing;
- opposing approaches share a phase and conflicting approaches are never green together.
- the eight-camera exterior layout has unique identifiers, valid calibration ranges, mirrored side mounts, and deterministic configuration;
- simulation scenarios enforce an integer fixed physics/control cadence;
- versioned reset, action, privileged-observation, metric, and terminal contracts reject invalid or non-finite state.

The engine smoke test checks that a network is generated, scene geometry and a vehicle are instantiated, structured data validation passes, traffic-signal runtime states are available, all eight camera channels are registered without rendering in headless mode, exterior-light commands reach the vehicle, and a short fixed-step drivetrain scenario stays finite.

The complete sequence is also available through `scripts/validate.ps1` on Windows or `scripts/validate.sh` on macOS/Linux. Set `CARORAMA_GODOT` when the Godot .NET executable is not on `PATH`.

To launch a repeatable camera channel directly, pass its stable identifier:

```powershell
godot --path src/CarOrama.Game -- --camera-preview FenderLeft
godot --path src/CarOrama.Game -- --camera-preview Rear
```

Valid identifiers are `FrontBumper`, `WindshieldMain`, `WindshieldWide`, `DoorPillarLeft`, `DoorPillarRight`, `FenderLeft`, `FenderRight`, and `Rear`.

For a repeatable close inspection of generated intersection geometry, run:

```powershell
godot --path src/CarOrama.Game -- --intersection-preview
```

This camera prioritizes the widest multi-lane junction and freezes the vehicle so road surfaces, sidewalk termination, curbs, markings, and traffic controls can be inspected without driving to the location.

To inspect a generated ninety-degree bend and verify that its asphalt, markings, curbs, and sidewalks share the same curve, run:

```powershell
godot --path src/CarOrama.Game -- --corner-preview
```

For a close inspection of stop-sign face, border, text, pole, orientation, and roadside placement, run:

```powershell
godot --path src/CarOrama.Game -- --traffic-control-preview
```

For a driver's-eye inspection of far-side mast arms, overhead lane alignment, and synchronized signal heads, run:

```powershell
godot --path src/CarOrama.Game -- --traffic-signal-preview
```

## Manual checks

For at least seeds `7`, `42`, and `2026`:

1. Confirm every road has a yellow centre treatment and white edge lines.
2. Confirm arterial roads have multiple lanes per direction, dashed white lane dividers, and aligned arrows.
3. Confirm stop bars and zebra crossings remain outside the intersection conflict area, including local-to-arterial junctions.
4. Confirm stop signs sit beside the near-side stop line and traffic lights hang above governed lanes on the far side, facing incoming traffic.
5. Approach a red signal and confirm demand causes the perpendicular green to progress through yellow, all-red, and then the requested green.
6. Confirm a green phase remains active when no vehicle is waiting on a conflicting approach.
7. Drive a full loop with keyboard and controller.
8. Confirm steering self-stabilizes, suspension settles, and the chassis collides with curbs/scenery.
9. Confirm headlights, tail lamps, brake lamps, both indicators, side repeaters, and hazards respond to their controls.
10. Confirm regenerative braking slows the car and can increase state of charge slightly.
11. Confirm friction braking works at full state of charge and at very low speed.
12. Reset the vehicle and generate a new seed without restarting the application.
13. Press `C` (or controller right shoulder) repeatedly and confirm the monitor cycles through two windshield, two pillar, two fender, front-bumper, and rear views with the expected direction and no view attached to the follow camera.

## Physics calibration status

The default vehicle is calibrated to the Jaguar I-PACE class used in Waymo's fleet. Its baseline uses the published 2,208 kg EU mass, 294 kW peak power, 696 Nm peak torque, 2.99 m wheelbase, and 4.8-second 0-100 km/h time. The drivetrain keeps explicit aerodynamic drag, rolling resistance, launch control, tire grip, regenerative braking, and friction braking; Godot's generic linear and angular damping are disabled so they do not duplicate those forces.

Regression tests require:

- 0-100 km/h in 4.65-5.05 seconds;
- full regeneration to average 1.6-2.3 m/s² of deceleration during the first second from 100 km/h;
- a full emergency stop from 100 km/h in 42-50 meters and 3.0-3.6 seconds; and
- combined longitudinal/lateral tire demand to remain inside the configured dry-road friction circle.

Holding `S`/Down while moving forward ramps in the lower regenerative limit; once the vehicle slows below 0.5 m/s, the same input selects reverse. Reverse is limited to 2.5 m/s² and 30 km/h. `Space` is intentionally the full emergency friction brake.
