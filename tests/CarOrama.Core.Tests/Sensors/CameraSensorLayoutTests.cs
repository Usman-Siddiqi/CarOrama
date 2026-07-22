using CarOrama.Core.Sensors;

namespace CarOrama.Core.Tests.Sensors;

public sealed class CameraSensorLayoutTests
{
    [Fact]
    public void Model3InspiredLayoutContainsEightUniquelyIdentifiedCameras()
    {
        var layout = CameraSensorLayout.CreateModel3Inspired();

        Assert.Equal(8, layout.Cameras.Count);
        Assert.Equal(8, layout.Cameras.Select(camera => camera.Id).Distinct().Count());
        Assert.Equal(Enum.GetValues<CameraSensorId>(), layout.Cameras.Select(camera => camera.Id));
    }

    [Fact]
    public void EveryDefaultCameraHasValidCaptureAndPoseValues()
    {
        var layout = CameraSensorLayout.CreateModel3Inspired();

        foreach (var camera in layout.Cameras)
        {
            Assert.InRange(camera.HorizontalFieldOfViewDegrees, 1.0, 179.0);
            Assert.InRange(camera.VerticalFieldOfViewDegrees, 1.0, 179.0);
            Assert.True(camera.ImageWidthPixels > 0);
            Assert.True(camera.ImageHeightPixels > 0);
            Assert.True(camera.CaptureFrequencyHertz > 0.0);
            Assert.True(double.IsFinite(camera.Pose.ForwardMeters));
            Assert.True(double.IsFinite(camera.Pose.LeftMeters));
            Assert.True(double.IsFinite(camera.Pose.UpMeters));
            Assert.InRange(camera.Pose.YawDegrees, -180.0, 180.0);
            Assert.InRange(camera.Pose.PitchDegrees, -90.0, 90.0);
            Assert.InRange(camera.Pose.RollDegrees, -180.0, 180.0);
        }
    }

    [Fact]
    public void DefaultOrientationsProvideForwardSideAndRearCoverage()
    {
        var layout = CameraSensorLayout.CreateModel3Inspired();

        Assert.Equal(0.0, layout[CameraSensorId.FrontBumper].Pose.YawDegrees);
        Assert.Equal(0.0, layout[CameraSensorId.WindshieldMain].Pose.YawDegrees);
        Assert.Equal(0.0, layout[CameraSensorId.WindshieldWide].Pose.YawDegrees);
        Assert.True(
            layout[CameraSensorId.WindshieldWide].HorizontalFieldOfViewDegrees >
            layout[CameraSensorId.WindshieldMain].HorizontalFieldOfViewDegrees);

        Assert.InRange(layout[CameraSensorId.DoorPillarLeft].Pose.YawDegrees, 45.0, 90.0);
        Assert.InRange(layout[CameraSensorId.DoorPillarRight].Pose.YawDegrees, -90.0, -45.0);
        Assert.InRange(layout[CameraSensorId.FenderLeft].Pose.YawDegrees, 90.0, 179.0);
        Assert.InRange(layout[CameraSensorId.FenderRight].Pose.YawDegrees, -179.0, -90.0);
        Assert.True(Math.Abs(layout[CameraSensorId.FenderLeft].Pose.LeftMeters) > 1.0);
        Assert.True(Math.Abs(layout[CameraSensorId.FenderRight].Pose.LeftMeters) > 1.0);
        Assert.Equal(180.0, layout[CameraSensorId.Rear].Pose.YawDegrees);
    }

    [Fact]
    public void PairedSideCamerasAreMirrored()
    {
        var layout = CameraSensorLayout.CreateModel3Inspired();

        AssertMirrored(
            layout[CameraSensorId.DoorPillarLeft],
            layout[CameraSensorId.DoorPillarRight]);
        AssertMirrored(
            layout[CameraSensorId.FenderLeft],
            layout[CameraSensorId.FenderRight]);
    }

    [Fact]
    public void DefaultFactoryIsDeterministicAndCaptureSettingsAreConfigurable()
    {
        var first = CameraSensorLayout.CreateModel3Inspired(960, 540, 20.0);
        var second = CameraSensorLayout.CreateModel3Inspired(960, 540, 20.0);

        Assert.Equal(first.Cameras, second.Cameras);
        Assert.All(first.Cameras, camera =>
        {
            Assert.Equal(960, camera.ImageWidthPixels);
            Assert.Equal(540, camera.ImageHeightPixels);
            Assert.Equal(20.0, camera.CaptureFrequencyHertz);
        });
    }

    [Fact]
    public void LayoutCopiesInputAndExposesReadOnlyCollection()
    {
        var source = CameraSensorLayout.CreateModel3Inspired().Cameras.ToArray();
        var layout = new CameraSensorLayout(source);
        var originalFrontCamera = layout[CameraSensorId.FrontBumper];

        source[0] = source[0] with { HorizontalFieldOfViewDegrees = 40.0 };

        Assert.Equal(originalFrontCamera, layout[CameraSensorId.FrontBumper]);
        var exposedCollection = Assert.IsAssignableFrom<ICollection<CameraSensorSpecification>>(layout.Cameras);
        Assert.True(exposedCollection.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => exposedCollection.Add(originalFrontCamera));
    }

    [Fact]
    public void LayoutRejectsDuplicateIdentifiersAndInvalidValues()
    {
        var validCamera = CameraSensorLayout.CreateModel3Inspired()[CameraSensorId.FrontBumper];

        Assert.Throws<ArgumentException>(() => new CameraSensorLayout([validCamera, validCamera]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraSensorLayout(
        [
            validCamera with { HorizontalFieldOfViewDegrees = double.NaN },
        ]));
        Assert.Throws<ArgumentOutOfRangeException>(() => CameraSensorLayout.CreateModel3Inspired(
            imageWidthPixels: 0));
    }

    private static void AssertMirrored(
        CameraSensorSpecification left,
        CameraSensorSpecification right)
    {
        Assert.Equal(left.Pose.ForwardMeters, right.Pose.ForwardMeters);
        Assert.Equal(left.Pose.LeftMeters, -right.Pose.LeftMeters);
        Assert.Equal(left.Pose.UpMeters, right.Pose.UpMeters);
        Assert.Equal(left.Pose.YawDegrees, -right.Pose.YawDegrees);
        Assert.Equal(left.Pose.PitchDegrees, right.Pose.PitchDegrees);
        Assert.Equal(left.Pose.RollDegrees, right.Pose.RollDegrees);
        Assert.Equal(left.HorizontalFieldOfViewDegrees, right.HorizontalFieldOfViewDegrees);
    }
}
