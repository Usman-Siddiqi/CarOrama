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

The engine smoke test checks that a network is generated, scene geometry and a vehicle are instantiated, structured data validation passes, exterior-light commands reach the vehicle, and a short fixed-step drivetrain scenario stays finite.

The complete sequence is also available through `scripts/validate.ps1` on Windows or `scripts/validate.sh` on macOS/Linux. Set `CARORAMA_GODOT` when the Godot .NET executable is not on `PATH`.

For a repeatable close inspection of generated intersection geometry, run:

```powershell
godot --path src/CarOrama.Game -- --intersection-preview
```

This camera prioritizes the widest multi-lane junction and freezes the vehicle so road surfaces, sidewalk termination, curbs, markings, and traffic controls can be inspected without driving to the location.

## Manual checks

For at least seeds `7`, `42`, and `2026`:

1. Confirm every road has a yellow centre treatment and white edge lines.
2. Confirm arterial roads have multiple lanes per direction, dashed white lane dividers, and aligned arrows.
3. Confirm stop bars and zebra crossings remain outside the intersection conflict area, including local-to-arterial junctions.
4. Confirm each approach has one stop sign or traffic signal beyond its outer-right curb, facing incoming traffic.
5. Drive a full loop with keyboard and controller.
6. Confirm steering self-stabilizes, suspension settles, and the chassis collides with curbs/scenery.
7. Confirm headlights, tail lamps, brake lamps, both indicators, side repeaters, and hazards respond to their controls.
8. Confirm regenerative braking slows the car and can increase state of charge slightly.
9. Confirm friction braking works at full state of charge and at very low speed.
10. Reset the vehicle and generate a new seed without restarting the application.

## Physics calibration status

Current parameters are plausible engineering starting values and are covered by invariant tests. They are not yet calibrated against a specific production EV. Future acceptance tests should use published mass, wheelbase, torque/power curves, acceleration, coast-down, skidpad, braking distance, and consumption data with stated tolerances.
