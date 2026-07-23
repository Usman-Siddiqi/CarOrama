using CarOrama.Core.Control;
using CarOrama.Core.Vehicles;
using CarOrama.Core.Vehicles.Dynamics;
using CarOrama.Game.Environment;
using Godot;

namespace CarOrama.Game.Vehicle;

public partial class ElectricVehicle : RigidBody3D
{
    private const float AirDensityKilogramsPerCubicMeter = 1.225f;

    private readonly VehicleSpecification _specification;
    private readonly ElectricDrivetrain _drivetrain;
    private readonly IVehicleCommandSource _commandSource;
    private readonly IVehicleLightingCommandSource _lightingCommandSource;
    private readonly SuspensionSpecification _suspensionSpecification = new();
    private readonly TireForceModel _tireModel;
    private readonly List<WheelContact> _wheels = [];
    private VehicleExteriorLighting? _exteriorLighting;
    private float _steeringInput;
    private float _steeringAngleRadians;
    private Transform3D _resetTransform;

    public ElectricVehicle(
        VehicleSpecification specification,
        IVehicleCommandSource commandSource,
        IVehicleLightingCommandSource? lightingCommandSource = null)
    {
        _specification = specification ?? throw new ArgumentNullException(nameof(specification));
        _commandSource = commandSource ?? throw new ArgumentNullException(nameof(commandSource));
        _lightingCommandSource = lightingCommandSource ??
            commandSource as IVehicleLightingCommandSource ??
            DisabledVehicleLightingCommandSource.Instance;
        _drivetrain = new ElectricDrivetrain(specification);
        _tireModel = new TireForceModel(new TireSpecification
        {
            FrictionCoefficient = specification.TireFrictionCoefficient,
        });
        Name = "ElectricVehicle";
        Mass = (float)specification.MassKilograms;
        LinearDampMode = RigidBody3D.DampMode.Replace;
        LinearDamp = 0.0f;
        AngularDampMode = RigidBody3D.DampMode.Replace;
        AngularDamp = 0.0f;
        ContinuousCd = true;
        CanSleep = false;
        ContactMonitor = true;
        MaxContactsReported = 12;
        CenterOfMassMode = CenterOfMassModeEnum.Custom;
        CenterOfMass = new Vector3(0.0f, -0.34f, 0.12f);
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.32f, Bounce = 0.02f };
        BodyEntered += body =>
        {
            if (!body.IsInGroup("drivable_surface"))
            {
                CollisionCount++;
                LastCollisionBodyName = body.Name;
            }
        };
        BuildVehicleNodes();
    }

    public double SpeedMetersPerSecond => LinearVelocity.Length();

    public double PeakSpeedMetersPerSecond { get; private set; }

    public double StateOfCharge => _drivetrain.Battery.StateOfCharge;

    public double MotorSpeedRpm { get; private set; }

    public double BatteryPowerKilowatts { get; private set; }

    public int GroundedWheelCount { get; private set; }

    public int CollisionCount { get; private set; }

    public string? LastCollisionBodyName { get; private set; }

    public VehicleCommand LastCommand { get; private set; } = VehicleCommand.Neutral;

    public VehicleLightingCommand LastLightingCommand { get; private set; } = VehicleLightingCommand.Off;

    public bool BrakeLightsActive => _exteriorLighting?.BrakeLightsActive ?? false;

    public bool LeftIndicatorLit => _exteriorLighting?.LeftIndicatorLit ?? false;

    public bool RightIndicatorLit => _exteriorLighting?.RightIndicatorLit ?? false;

    public event Action? PhysicsIntegrated;

    public void SetResetTransform(Transform3D transform)
    {
        _resetTransform = transform;
        ResetVehicle();
    }

    public void ResetVehicle()
    {
        Freeze = true;
        GlobalTransform = _resetTransform;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        Sleeping = false;
        _drivetrain.Battery.SetStateOfCharge(_specification.Battery.InitialStateOfCharge);
        _steeringInput = 0.0f;
        _steeringAngleRadians = 0.0f;
        PeakSpeedMetersPerSecond = 0.0;
        MotorSpeedRpm = 0.0;
        BatteryPowerKilowatts = 0.0;
        GroundedWheelCount = 0;
        Freeze = false;
        CollisionCount = 0;
        LastCollisionBodyName = null;
        LastCommand = VehicleCommand.Neutral;
        LastLightingCommand = VehicleLightingCommand.Off;
        foreach (var wheel in _wheels)
        {
            wheel.Suspension.Reset();
        }
    }

    public override void _IntegrateForces(PhysicsDirectBodyState3D state)
    {
        _ = state;
        PhysicsIntegrated?.Invoke();
    }

    public override void _PhysicsProcess(double delta)
    {
        LastCommand = _commandSource.ReadCommand(delta);
        LastLightingCommand = _lightingCommandSource.ReadLightingCommand(delta);
        PeakSpeedMetersPerSecond = Math.Max(PeakSpeedMetersPerSecond, SpeedMetersPerSecond);
        _steeringInput = Mathf.MoveToward(_steeringInput, (float)LastCommand.Steering, (float)(delta * 3.8));
        _steeringAngleRadians = Mathf.DegToRad((float)_specification.MaximumSteeringAngleDegrees) * _steeringInput;

        var bodyForward = -GlobalTransform.Basis.Z.Normalized();
        var signedForwardSpeed = LinearVelocity.Dot(bodyForward);
        var wheelSpeed = signedForwardSpeed / (float)_specification.WheelRadiusMeters;
        var drivetrain = _drivetrain.Evaluate(LastCommand, wheelSpeed, delta);
        MotorSpeedRpm = drivetrain.MotorSpeedRpm;
        BatteryPowerKilowatts = drivetrain.BatteryPowerWatts / 1_000.0;

        GroundedWheelCount = 0;
        var drivenWheelCount = Math.Max(1, _wheels.Count(wheel => wheel.Driven));
        foreach (var wheel in _wheels)
        {
            ApplyWheelForces(wheel, drivetrain, drivenWheelCount, (float)delta, bodyForward);
        }

        ApplyBodyResistance(bodyForward, signedForwardSpeed);
        UpdateWheelVisuals((float)delta, signedForwardSpeed);
        var drivetrainBraking = drivetrain.RegenerativeBrakeForceNewtons > 100.0;
        var reverseSelected = LastCommand.Throttle < -0.02 && signedForwardSpeed <= 0.5f;
        _exteriorLighting?.ApplyLampState(
            LastLightingCommand,
            LastCommand,
            drivetrainBraking,
            reverseSelected,
            delta);
    }

    private void ApplyWheelForces(
        WheelContact wheel,
        DrivetrainOutput drivetrain,
        int drivenWheelCount,
        float delta,
        Vector3 bodyForward)
    {
        if (!wheel.Ray.IsColliding())
        {
            wheel.Suspension.Reset();
            return;
        }

        GroundedWheelCount++;
        var contactPoint = wheel.Ray.GetCollisionPoint();
        var contactNormal = wheel.Ray.GetCollisionNormal().Normalized();
        var anchor = wheel.Ray.GlobalPosition;
        var wheelRadius = (float)_specification.WheelRadiusMeters;
        var springLength = Mathf.Max(0.0f, anchor.DistanceTo(contactPoint) - wheelRadius);
        var suspension = wheel.Suspension.Evaluate(springLength, delta);
        var normalForceMagnitude = (float)suspension.NormalForceNewtons;
        var offset = contactPoint - GlobalPosition;
        ApplyForce(contactNormal * normalForceMagnitude, offset);

        var forward = wheel.Steering
            ? bodyForward.Rotated(contactNormal, -_steeringAngleRadians).Normalized()
            : bodyForward;
        var right = forward.Cross(contactNormal).Normalized();
        var contactVelocity = LinearVelocity + AngularVelocity.Cross(offset);
        var longitudinalVelocity = contactVelocity.Dot(forward);
        var lateralVelocity = contactVelocity.Dot(right);

        var longitudinalForce = wheel.Driven
            ? (float)(drivetrain.DriveForceNewtons / drivenWheelCount)
            : 0.0f;
        var brakeMagnitude = (float)(drivetrain.FrictionBrakeForceNewtons / _wheels.Count);
        if (wheel.Driven)
        {
            brakeMagnitude += (float)(drivetrain.RegenerativeBrakeForceNewtons / drivenWheelCount);
        }

        if (Mathf.Abs(longitudinalVelocity) > 0.001f)
        {
            var maximumNonReversingBrake = Mathf.Abs(longitudinalVelocity) * (float)_specification.MassKilograms
                / (_wheels.Count * delta);
            longitudinalForce -= Mathf.Sign(longitudinalVelocity) * Mathf.Min(brakeMagnitude, maximumNonReversingBrake);
        }

        var tireForce = _tireModel.Evaluate(normalForceMagnitude, longitudinalForce, lateralVelocity);
        ApplyForce(
            (forward * (float)tireForce.LongitudinalForceNewtons) +
            (right * (float)tireForce.LateralForceNewtons),
            offset);
    }

    private void ApplyBodyResistance(Vector3 bodyForward, float signedForwardSpeed)
    {
        if (LinearVelocity.LengthSquared() < 0.01f)
        {
            return;
        }

        var speed = LinearVelocity.Length();
        var dragMagnitude = 0.5f * AirDensityKilogramsPerCubicMeter * (float)_specification.DragCoefficient
            * (float)_specification.FrontalAreaSquareMeters * speed * speed;
        ApplyCentralForce(-LinearVelocity.Normalized() * dragMagnitude);

        if (Mathf.Abs(signedForwardSpeed) > 0.2f && GroundedWheelCount > 0)
        {
            var rollingMagnitude = (float)(_specification.RollingResistanceCoefficient * _specification.MassKilograms * 9.80665);
            ApplyCentralForce(-bodyForward * Mathf.Sign(signedForwardSpeed) * rollingMagnitude);
        }
    }

    private void UpdateWheelVisuals(float delta, float signedForwardSpeed)
    {
        var rotationStep = signedForwardSpeed * delta / (float)_specification.WheelRadiusMeters;
        foreach (var wheel in _wheels)
        {
            wheel.SpinRadians = Mathf.Wrap(wheel.SpinRadians + rotationStep, -Mathf.Pi, Mathf.Pi);
            var yaw = wheel.Steering ? -_steeringAngleRadians : 0.0f;
            wheel.VisualPivot.Basis = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, wheel.SpinRadians);
        }
    }

    private void BuildVehicleNodes()
    {
        var chassisMaterial = PrimitiveFactory.Material(new Color("1864ab"), 0.34f);
        var glassMaterial = PrimitiveFactory.Material(new Color("15394f"), 0.18f);
        var batteryMaterial = PrimitiveFactory.Material(new Color("20282c"), 0.62f);
        var tireMaterial = PrimitiveFactory.Material(new Color("111315"), 0.94f);
        var rimMaterial = PrimitiveFactory.Material(new Color("a9b2b8"), 0.3f);

        AddChild(new CollisionShape3D
        {
            Name = "ChassisCollision",
            Position = new Vector3(0.0f, 0.12f, 0.0f),
            Shape = new BoxShape3D { Size = new Vector3(1.92f, 0.58f, 4.62f) },
        });
        AddChild(PrimitiveFactory.Box(
            "Body",
            new Vector3(1.9f, 0.62f, 4.55f),
            new Transform3D(Basis.Identity, new Vector3(0.0f, 0.16f, 0.0f)),
            chassisMaterial));
        AddChild(PrimitiveFactory.Box(
            "Cabin",
            new Vector3(1.62f, 0.66f, 2.22f),
            new Transform3D(Basis.Identity, new Vector3(0.0f, 0.75f, 0.18f)),
            glassMaterial));
        AddChild(PrimitiveFactory.Box(
            "BatteryPack",
            new Vector3(1.58f, 0.16f, 2.72f),
            new Transform3D(Basis.Identity, new Vector3(0.0f, -0.2f, 0.18f)),
            batteryMaterial));

        _exteriorLighting = new VehicleExteriorLighting();
        AddChild(_exteriorLighting);

        var halfTrack = (float)_specification.TrackWidthMeters * 0.5f;
        var halfWheelbase = (float)_specification.WheelbaseMeters * 0.5f;
        AddWheel("FrontLeft", new Vector3(-halfTrack, 0.15f, -halfWheelbase), true, IsDriven(front: true), tireMaterial, rimMaterial);
        AddWheel("FrontRight", new Vector3(halfTrack, 0.15f, -halfWheelbase), true, IsDriven(front: true), tireMaterial, rimMaterial);
        AddWheel("RearLeft", new Vector3(-halfTrack, 0.15f, halfWheelbase), false, IsDriven(front: false), tireMaterial, rimMaterial);
        AddWheel("RearRight", new Vector3(halfTrack, 0.15f, halfWheelbase), false, IsDriven(front: false), tireMaterial, rimMaterial);
    }

    private void AddWheel(
        string name,
        Vector3 position,
        bool steering,
        bool driven,
        Material tireMaterial,
        Material rimMaterial)
    {
        var ray = new RayCast3D
        {
            Name = $"{name}SuspensionRay",
            Position = position,
            TargetPosition = new Vector3(
                0.0f,
                -(float)(_suspensionSpecification.RestLengthMeters +
                    _suspensionSpecification.MaximumCompressionMeters +
                    _specification.WheelRadiusMeters),
                0.0f),
            Enabled = true,
            ExcludeParent = true,
        };
        AddChild(ray);

        var pivot = new Node3D
        {
            Name = $"{name}Visual",
            Position = position + new Vector3(0.0f, -(float)_suspensionSpecification.RestLengthMeters, 0.0f),
        };
        AddChild(pivot);
        var tire = PrimitiveFactory.Cylinder(
            "Tire",
            (float)_specification.WheelRadiusMeters,
            0.24f,
            tireMaterial,
            24);
        tire.RotationDegrees = new Vector3(0.0f, 0.0f, 90.0f);
        pivot.AddChild(tire);
        var rim = PrimitiveFactory.Cylinder(
            "Rim",
            (float)_specification.WheelRadiusMeters * 0.55f,
            0.255f,
            rimMaterial,
            16);
        rim.RotationDegrees = new Vector3(0.0f, 0.0f, 90.0f);
        pivot.AddChild(rim);

        _wheels.Add(new WheelContact(
            ray,
            pivot,
            steering,
            driven,
            new SuspensionModel(_suspensionSpecification)));
    }

    private bool IsDriven(bool front)
    {
        return _specification.DrivetrainLayout switch
        {
            DrivetrainLayout.FrontWheelDrive => front,
            DrivetrainLayout.RearWheelDrive => !front,
            DrivetrainLayout.AllWheelDrive => true,
            _ => false,
        };
    }

    private sealed class WheelContact(
        RayCast3D ray,
        Node3D visualPivot,
        bool steering,
        bool driven,
        SuspensionModel suspension)
    {
        public RayCast3D Ray { get; } = ray;

        public Node3D VisualPivot { get; } = visualPivot;

        public bool Steering { get; } = steering;

        public bool Driven { get; } = driven;

        public SuspensionModel Suspension { get; } = suspension;

        public float SpinRadians { get; set; }
    }

    private sealed class DisabledVehicleLightingCommandSource : IVehicleLightingCommandSource
    {
        public static DisabledVehicleLightingCommandSource Instance { get; } = new();

        public VehicleLightingCommand ReadLightingCommand(double deltaSeconds)
        {
            _ = deltaSeconds;
            return VehicleLightingCommand.Off;
        }
    }
}
