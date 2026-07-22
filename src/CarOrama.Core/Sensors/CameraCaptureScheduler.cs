namespace CarOrama.Core.Sensors;

/// <summary>
/// Maps camera frequencies to exact physics ticks without accumulating floating
/// point time drift. Call once for every consecutive physics tick.
/// </summary>
public sealed class CameraCaptureScheduler
{
    private readonly CameraSensorLayout _layout;
    private readonly int _physicsTicksPerSecond;
    private readonly Dictionary<CameraSensorId, long> _emittedFrameNumbers = [];
    private long _lastPhysicsTick;

    public CameraCaptureScheduler(CameraSensorLayout layout, int physicsTicksPerSecond)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        if (physicsTicksPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(physicsTicksPerSecond));
        }

        _physicsTicksPerSecond = physicsTicksPerSecond;
        Reset();
    }

    public void Reset(long initialPhysicsTick = 0)
    {
        if (initialPhysicsTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialPhysicsTick));
        }

        _lastPhysicsTick = initialPhysicsTick;
        _emittedFrameNumbers.Clear();
        foreach (var camera in _layout.Cameras)
        {
            _emittedFrameNumbers[camera.Id] = FrameNumberAt(camera, initialPhysicsTick);
        }
    }

    public IReadOnlyList<CameraSensorId> Advance(long physicsTick)
    {
        if (physicsTick != _lastPhysicsTick + 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(physicsTick),
                "Camera scheduling requires consecutive physics ticks.");
        }

        var due = new List<CameraSensorId>();
        foreach (var camera in _layout.Cameras)
        {
            var frameNumber = FrameNumberAt(camera, physicsTick);
            if (frameNumber <= _emittedFrameNumbers[camera.Id])
            {
                continue;
            }

            due.Add(camera.Id);
            _emittedFrameNumbers[camera.Id] = frameNumber;
        }

        _lastPhysicsTick = physicsTick;
        return due;
    }

    private long FrameNumberAt(CameraSensorSpecification camera, long physicsTick)
    {
        return (long)Math.Floor(
            (physicsTick * camera.CaptureFrequencyHertz / _physicsTicksPerSecond) + 1e-9);
    }
}
