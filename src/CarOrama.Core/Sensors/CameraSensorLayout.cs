using System.Collections.ObjectModel;

namespace CarOrama.Core.Sensors;

/// <summary>
/// Stable identifiers for the exterior cameras in the default vehicle sensor rig.
/// </summary>
public enum CameraSensorId
{
    FrontBumper,
    WindshieldMain,
    WindshieldWide,
    DoorPillarLeft,
    DoorPillarRight,
    FenderLeft,
    FenderRight,
    Rear,
}

/// <summary>
/// A camera transform in a vehicle-local, engine-independent coordinate frame.
/// Position uses metres with forward, left, and up as the positive axes. Zero
/// rotation looks forward; positive yaw looks left and positive pitch looks up.
/// </summary>
public readonly record struct CameraSensorPose(
    double ForwardMeters,
    double LeftMeters,
    double UpMeters,
    double YawDegrees,
    double PitchDegrees,
    double RollDegrees);

/// <summary>
/// Immutable capture and mounting parameters for one simulated camera.
/// </summary>
public sealed record CameraSensorSpecification(
    CameraSensorId Id,
    CameraSensorPose Pose,
    double HorizontalFieldOfViewDegrees,
    int ImageWidthPixels,
    int ImageHeightPixels,
    double CaptureFrequencyHertz)
{
    /// <summary>
    /// Vertical field of view implied by the horizontal field of view and image
    /// aspect ratio, assuming square pixels.
    /// </summary>
    public double VerticalFieldOfViewDegrees
    {
        get
        {
            var horizontalRadians = HorizontalFieldOfViewDegrees * Math.PI / 180.0;
            var verticalRadians = 2.0 * Math.Atan(
                Math.Tan(horizontalRadians * 0.5) * ImageHeightPixels / ImageWidthPixels);
            return verticalRadians * 180.0 / Math.PI;
        }
    }
}

/// <summary>
/// An immutable, deterministic collection of vehicle camera specifications.
/// </summary>
public sealed class CameraSensorLayout
{
    private readonly ReadOnlyCollection<CameraSensorSpecification> _cameras;
    private readonly IReadOnlyDictionary<CameraSensorId, CameraSensorSpecification> _camerasById;

    public CameraSensorLayout(IEnumerable<CameraSensorSpecification> cameras)
    {
        ArgumentNullException.ThrowIfNull(cameras);

        var cameraArray = cameras.ToArray();
        if (cameraArray.Length == 0)
        {
            throw new ArgumentException("A camera layout must contain at least one camera.", nameof(cameras));
        }

        var camerasById = new Dictionary<CameraSensorId, CameraSensorSpecification>();
        foreach (var camera in cameraArray)
        {
            Validate(camera);
            if (!camerasById.TryAdd(camera.Id, camera))
            {
                throw new ArgumentException($"Camera identifier '{camera.Id}' occurs more than once.", nameof(cameras));
            }
        }

        _cameras = Array.AsReadOnly(cameraArray);
        _camerasById = new ReadOnlyDictionary<CameraSensorId, CameraSensorSpecification>(camerasById);
    }

    public IReadOnlyList<CameraSensorSpecification> Cameras => _cameras;

    public CameraSensorSpecification this[CameraSensorId id] => _camerasById.TryGetValue(id, out var camera)
        ? camera
        : throw new KeyNotFoundException($"Camera '{id}' is not present in this layout.");

    /// <summary>
    /// Creates an eight-camera exterior layout inspired by the camera coverage of
    /// 2024-and-newer Tesla Model 3 vehicles. Exact production mounting poses,
    /// optics, calibration, and capture timing are not public, so these values are
    /// documented simulation approximations rather than manufacturer calibration.
    /// Callers can replace any record with a <c>with</c> expression and construct a
    /// new layout when a scenario needs different poses or optics.
    /// </summary>
    public static CameraSensorLayout CreateModel3Inspired(
        int imageWidthPixels = 1280,
        int imageHeightPixels = 720,
        double captureFrequencyHertz = 30.0)
    {
        // The two windshield views provide complementary narrow and wide forward
        // coverage. Pillar cameras look across the front quarters, fender cameras
        // look rearward along each side, and bumper/rear cameras cover close zones.
        CameraSensorSpecification[] cameras =
        [
            Camera(
                CameraSensorId.FrontBumper,
                new CameraSensorPose(2.30, 0.0, 0.58, 0.0, -3.0, 0.0),
                120.0),
            Camera(
                CameraSensorId.WindshieldMain,
                new CameraSensorPose(0.72, 0.0, 1.43, 0.0, -1.0, 0.0),
                70.0),
            Camera(
                CameraSensorId.WindshieldWide,
                new CameraSensorPose(0.70, 0.0, 1.44, 0.0, -4.0, 0.0),
                120.0),
            Camera(
                CameraSensorId.DoorPillarLeft,
                new CameraSensorPose(0.28, 0.91, 1.27, 68.0, -2.0, 0.0),
                100.0),
            Camera(
                CameraSensorId.DoorPillarRight,
                new CameraSensorPose(0.28, -0.91, 1.27, -68.0, -2.0, 0.0),
                100.0),
            Camera(
                CameraSensorId.FenderLeft,
                new CameraSensorPose(1.12, 1.10, 0.88, 145.0, -4.0, 0.0),
                100.0),
            Camera(
                CameraSensorId.FenderRight,
                new CameraSensorPose(1.12, -1.10, 0.88, -145.0, -4.0, 0.0),
                100.0),
            Camera(
                CameraSensorId.Rear,
                new CameraSensorPose(-2.25, 0.0, 0.93, 180.0, -3.0, 0.0),
                120.0),
        ];

        return new CameraSensorLayout(cameras);

        CameraSensorSpecification Camera(
            CameraSensorId id,
            CameraSensorPose pose,
            double horizontalFieldOfViewDegrees) => new(
                id,
                pose,
                horizontalFieldOfViewDegrees,
                imageWidthPixels,
                imageHeightPixels,
                captureFrequencyHertz);
    }

    private static void Validate(CameraSensorSpecification camera)
    {
        ArgumentNullException.ThrowIfNull(camera);

        if (!Enum.IsDefined(camera.Id))
        {
            throw new ArgumentOutOfRangeException(nameof(camera), $"Unknown camera identifier '{camera.Id}'.");
        }

        var pose = camera.Pose;
        if (!double.IsFinite(pose.ForwardMeters) ||
            !double.IsFinite(pose.LeftMeters) ||
            !double.IsFinite(pose.UpMeters) ||
            !double.IsFinite(pose.YawDegrees) ||
            !double.IsFinite(pose.PitchDegrees) ||
            !double.IsFinite(pose.RollDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(camera), "Camera pose values must be finite.");
        }

        if (pose.YawDegrees is < -180.0 or > 180.0 ||
            pose.PitchDegrees is < -90.0 or > 90.0 ||
            pose.RollDegrees is < -180.0 or > 180.0)
        {
            throw new ArgumentOutOfRangeException(nameof(camera), "Camera orientation is outside the supported range.");
        }

        if (!double.IsFinite(camera.HorizontalFieldOfViewDegrees) ||
            camera.HorizontalFieldOfViewDegrees is <= 0.0 or >= 180.0)
        {
            throw new ArgumentOutOfRangeException(nameof(camera), "Camera field of view must be between 0 and 180 degrees.");
        }

        if (camera.ImageWidthPixels <= 0 || camera.ImageHeightPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(camera), "Camera image dimensions must be positive.");
        }

        if (!double.IsFinite(camera.CaptureFrequencyHertz) || camera.CaptureFrequencyHertz <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(camera), "Camera capture frequency must be positive and finite.");
        }
    }
}
