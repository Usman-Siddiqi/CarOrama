using CarOrama.Core.Sensors;

namespace CarOrama.Core.Tests.Sensors;

public sealed class CameraCaptureSchedulerTests
{
    [Fact]
    public void ThirtyHertzCamerasAreScheduledEveryFourTicksAtOneHundredTwentyHertz()
    {
        var layout = CameraSensorLayout.CreateModel3Inspired(
            imageWidthPixels: 64,
            imageHeightPixels: 36,
            captureFrequencyHertz: 30.0);
        var scheduler = new CameraCaptureScheduler(layout, 120);

        for (var tick = 1; tick < 4; tick++)
        {
            Assert.Empty(scheduler.Advance(tick));
        }

        Assert.Equal(8, scheduler.Advance(4).Count);
        for (var tick = 5; tick < 8; tick++)
        {
            Assert.Empty(scheduler.Advance(tick));
        }

        Assert.Equal(8, scheduler.Advance(8).Count);
    }

    [Fact]
    public void ResetAndConsecutiveTickValidationPreventSilentFrameDrift()
    {
        var scheduler = new CameraCaptureScheduler(CameraSensorLayout.CreateModel3Inspired(), 120);

        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Advance(2));
        scheduler.Reset(11);
        Assert.Equal(8, scheduler.Advance(12).Count);
    }
}
