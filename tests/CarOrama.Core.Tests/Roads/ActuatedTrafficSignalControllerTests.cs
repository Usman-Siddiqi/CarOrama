using CarOrama.Core.Roads;

namespace CarOrama.Core.Tests.Roads;

public sealed class ActuatedTrafficSignalControllerTests
{
    private static readonly IReadOnlyDictionary<string, TrafficSignalPhase> Phases =
        new Dictionary<string, TrafficSignalPhase>(StringComparer.Ordinal)
        {
            ["east"] = TrafficSignalPhase.Horizontal,
            ["west"] = TrafficSignalPhase.Horizontal,
            ["north"] = TrafficSignalPhase.Vertical,
            ["south"] = TrafficSignalPhase.Vertical,
        };

    private static readonly TrafficSignalTiming Timing = new()
    {
        MinimumGreenSeconds = 5.0,
        MaximumGreenSeconds = 12.0,
        PassageGapSeconds = 2.0,
        YellowSeconds = 3.0,
        AllRedSeconds = 1.0,
    };

    [Fact]
    public void OpposingApproachesShareAProtectedGreen()
    {
        var controller = new ActuatedTrafficSignalController(Phases, Timing);

        Assert.Equal(TrafficSignalState.Green, controller.GetState("east"));
        Assert.Equal(TrafficSignalState.Green, controller.GetState("west"));
        Assert.Equal(TrafficSignalState.Red, controller.GetState("north"));
        Assert.Equal(TrafficSignalState.Red, controller.GetState("south"));
    }

    [Fact]
    public void GreenRestsWhenThereIsNoCompetingDemand()
    {
        var controller = new ActuatedTrafficSignalController(Phases, Timing);

        controller.Step(60.0, ["east"]);

        Assert.Equal(TrafficSignalState.Green, controller.GetState("east"));
        Assert.Equal(TrafficSignalState.Red, controller.GetState("north"));
    }

    [Fact]
    public void CompetingDemandReceivesGreenAfterSafeClearanceSequence()
    {
        var controller = new ActuatedTrafficSignalController(Phases, Timing);

        controller.Step(5.0, ["north"]);
        Assert.Equal(TrafficSignalState.Yellow, controller.GetState("east"));
        Assert.Equal(TrafficSignalState.Red, controller.GetState("north"));

        controller.Step(3.0, ["north"]);
        Assert.Equal(TrafficSignalState.Red, controller.GetState("east"));
        Assert.Equal(TrafficSignalState.Red, controller.GetState("north"));

        controller.Step(1.0, ["north"]);
        Assert.Equal(TrafficSignalState.Red, controller.GetState("east"));
        Assert.Equal(TrafficSignalState.Green, controller.GetState("north"));
    }

    [Fact]
    public void ContinuousCurrentDemandExtendsGreenOnlyToMaximum()
    {
        var controller = new ActuatedTrafficSignalController(Phases, Timing);

        controller.Step(11.9, ["east", "north"]);
        Assert.Equal(TrafficSignalState.Green, controller.GetState("east"));

        controller.Step(0.1, ["east", "north"]);
        Assert.Equal(TrafficSignalState.Yellow, controller.GetState("east"));
    }

    [Fact]
    public void LargeStepCannotSkipMaximumGreen()
    {
        var controller = new ActuatedTrafficSignalController(Phases, Timing);

        controller.Step(13.0, ["east", "north"]);

        Assert.Equal(TrafficSignalState.Yellow, controller.GetState("east"));
        Assert.Equal(1.0, controller.StageElapsedSeconds, precision: 6);
    }

    [Fact]
    public void LargeStepPreservesYellowAndAllRedDurations()
    {
        var controller = new ActuatedTrafficSignalController(Phases, Timing);

        controller.Step(9.0, ["north"]);

        Assert.Equal(TrafficSignalState.Green, controller.GetState("north"));
        Assert.Equal(TrafficSignalPhase.Vertical, controller.CurrentPhase);
    }

    [Fact]
    public void ConflictingApproachesAreNeverGreenTogether()
    {
        var controller = new ActuatedTrafficSignalController(Phases, Timing);

        for (var index = 0; index < 200; index++)
        {
            controller.Step(0.25, ["east", "north"]);
            var horizontalGreen = controller.GetState("east") == TrafficSignalState.Green;
            var verticalGreen = controller.GetState("north") == TrafficSignalState.Green;
            Assert.False(horizontalGreen && verticalGreen);
        }
    }

    [Fact]
    public void ResetRestoresTheDeterministicInitialPhaseAndClearsDemand()
    {
        var controller = new ActuatedTrafficSignalController(Phases, Timing);
        controller.Step(9.0, ["north"]);
        Assert.Equal(TrafficSignalPhase.Vertical, controller.CurrentPhase);

        controller.Reset();

        Assert.Equal(TrafficSignalPhase.Horizontal, controller.CurrentPhase);
        Assert.Equal(TrafficSignalState.Green, controller.CurrentPhaseState);
        Assert.Equal(0.0, controller.StageElapsedSeconds);
        controller.Step(Timing.MinimumGreenSeconds, []);
        Assert.Equal(TrafficSignalState.Green, controller.GetState("east"));
    }
}
