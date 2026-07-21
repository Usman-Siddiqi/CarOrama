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
- every directed lane has valid boundaries, direction, segment, and successor references;
- routes contain connected lanes and spawns reference valid lanes;
- motor output obeys torque, power, speed, and gear limits;
- regenerative braking cannot charge above battery/motor acceptance limits;
- friction braking remains independent from regenerative braking;
- normalized commands are finite and clamped.

The engine smoke test checks that a network is generated, scene geometry and a vehicle are instantiated, structured data validation passes, and a short fixed-step drivetrain scenario stays finite.

The complete sequence is also available through `scripts/validate.ps1` on Windows or `scripts/validate.sh` on macOS/Linux. Set `CARORAMA_GODOT` when the Godot .NET executable is not on `PATH`.

## Manual checks

For at least seeds `7`, `42`, and `2026`:

1. Confirm all roads are connected and markings remain aligned through intersections.
2. Confirm stop signs and traffic lights face their approaches and stop lines precede intersections.
3. Drive a full loop with keyboard and controller.
4. Confirm steering self-stabilizes, suspension settles, and the chassis collides with curbs/scenery.
5. Confirm regenerative braking slows the car and can increase state of charge slightly.
6. Confirm friction braking works at full state of charge and at very low speed.
7. Reset the vehicle and generate a new seed without restarting the application.

## Physics calibration status

Current parameters are plausible engineering starting values and are covered by invariant tests. They are not yet calibrated against a specific production EV. Future acceptance tests should use published mass, wheelbase, torque/power curves, acceleration, coast-down, skidpad, braking distance, and consumption data with stated tolerances.
