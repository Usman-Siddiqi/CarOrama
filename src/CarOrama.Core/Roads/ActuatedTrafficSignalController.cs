namespace CarOrama.Core.Roads;

public enum TrafficSignalState
{
    Red,
    Yellow,
    Green,
}

public enum TrafficSignalPhase
{
    Horizontal,
    Vertical,
}

public sealed record TrafficSignalTiming
{
    public double MinimumGreenSeconds { get; init; } = 6.0;

    public double MaximumGreenSeconds { get; init; } = 24.0;

    public double PassageGapSeconds { get; init; } = 2.5;

    public double YellowSeconds { get; init; } = 3.5;

    public double AllRedSeconds { get; init; } = 1.0;

    public void Validate()
    {
        if (MinimumGreenSeconds <= 0.0 ||
            MaximumGreenSeconds < MinimumGreenSeconds ||
            PassageGapSeconds < 0.0 ||
            YellowSeconds <= 0.0 ||
            AllRedSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(TrafficSignalTiming), "Signal timings must form a valid, non-negative sequence.");
        }
    }
}

/// <summary>
/// Controls one orthogonal intersection using two non-conflicting, actuated phases.
/// Demand may be supplied by any detector implementation; this class owns only
/// deterministic timing and safety transitions.
/// </summary>
public sealed class ActuatedTrafficSignalController
{
    private readonly IReadOnlyDictionary<string, TrafficSignalPhase> _phaseByControlId;
    private readonly HashSet<string> _demandedControlIds = new(StringComparer.Ordinal);
    private readonly TrafficSignalPhase _initialPhase;
    private double _stageElapsedSeconds;
    private double _secondsSinceCurrentPhaseDemand;

    public ActuatedTrafficSignalController(
        IReadOnlyDictionary<string, TrafficSignalPhase> phaseByControlId,
        TrafficSignalTiming? timing = null,
        TrafficSignalPhase initialPhase = TrafficSignalPhase.Horizontal)
    {
        ArgumentNullException.ThrowIfNull(phaseByControlId);
        if (phaseByControlId.Count == 0)
        {
            throw new ArgumentException("At least one signal approach is required.", nameof(phaseByControlId));
        }

        _phaseByControlId = new Dictionary<string, TrafficSignalPhase>(phaseByControlId, StringComparer.Ordinal);
        Timing = timing ?? new TrafficSignalTiming();
        Timing.Validate();
        _initialPhase = HasPhase(initialPhase) ? initialPhase : Other(initialPhase);
        CurrentPhase = _initialPhase;
    }

    public TrafficSignalTiming Timing { get; }

    public TrafficSignalPhase CurrentPhase { get; private set; }

    public TrafficSignalState CurrentPhaseState { get; private set; } = TrafficSignalState.Green;

    public double StageElapsedSeconds => _stageElapsedSeconds;

    public void Reset()
    {
        CurrentPhase = _initialPhase;
        CurrentPhaseState = TrafficSignalState.Green;
        _stageElapsedSeconds = 0.0;
        _secondsSinceCurrentPhaseDemand = 0.0;
        _demandedControlIds.Clear();
    }

    public void Step(double deltaSeconds, IEnumerable<string> demandedControlIds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        ArgumentNullException.ThrowIfNull(demandedControlIds);
        _demandedControlIds.Clear();
        foreach (var controlId in demandedControlIds)
        {
            if (_phaseByControlId.ContainsKey(controlId))
            {
                _demandedControlIds.Add(controlId);
            }
        }

        var remainingSeconds = deltaSeconds;
        while (remainingSeconds > 0.0)
        {
            var transitionAfter = GetTransitionTimeSeconds();
            var stepSeconds = Math.Min(remainingSeconds, Math.Max(0.0, transitionAfter - _stageElapsedSeconds));
            AdvanceClocks(stepSeconds);
            remainingSeconds -= stepSeconds;

            if (_stageElapsedSeconds + 1e-9 < transitionAfter)
            {
                break;
            }

            if (!TryTransition())
            {
                if (remainingSeconds <= 0.0)
                {
                    break;
                }

                continue;
            }
        }
    }

    public TrafficSignalState GetState(string controlId)
    {
        if (!_phaseByControlId.TryGetValue(controlId, out var phase))
        {
            throw new KeyNotFoundException($"Unknown traffic signal control '{controlId}'.");
        }

        return phase == CurrentPhase ? CurrentPhaseState : TrafficSignalState.Red;
    }

    private double GetTransitionTimeSeconds()
    {
        return CurrentPhaseState switch
        {
            TrafficSignalState.Green => GetGreenTransitionTimeSeconds(),
            TrafficSignalState.Yellow => Timing.YellowSeconds,
            TrafficSignalState.Red => Timing.AllRedSeconds,
            _ => throw new InvalidOperationException("Unknown traffic-signal state."),
        };
    }

    private double GetGreenTransitionTimeSeconds()
    {
        if (!HasPhase(Other(CurrentPhase)) || !HasDemand(Other(CurrentPhase)))
        {
            return double.PositiveInfinity;
        }

        if (_stageElapsedSeconds < Timing.MinimumGreenSeconds)
        {
            return Timing.MinimumGreenSeconds;
        }

        if (_stageElapsedSeconds >= Timing.MaximumGreenSeconds)
        {
            return _stageElapsedSeconds;
        }

        if (!HasDemand(CurrentPhase) && _secondsSinceCurrentPhaseDemand >= Timing.PassageGapSeconds)
        {
            return _stageElapsedSeconds;
        }

        return Math.Min(Timing.MaximumGreenSeconds, _stageElapsedSeconds + Timing.PassageGapSeconds);
    }

    private bool TryTransition()
    {
        switch (CurrentPhaseState)
        {
            case TrafficSignalState.Green:
                if (!HasPhase(Other(CurrentPhase)) || !HasDemand(Other(CurrentPhase)))
                {
                    return false;
                }

                var gapExpired = !HasDemand(CurrentPhase) &&
                    _secondsSinceCurrentPhaseDemand >= Timing.PassageGapSeconds;
                if (_stageElapsedSeconds < Timing.MinimumGreenSeconds ||
                    (!gapExpired && _stageElapsedSeconds < Timing.MaximumGreenSeconds))
                {
                    return false;
                }

                CurrentPhaseState = TrafficSignalState.Yellow;
                _stageElapsedSeconds = 0.0;
                return true;

            case TrafficSignalState.Yellow:
                CurrentPhaseState = TrafficSignalState.Red;
                _stageElapsedSeconds = 0.0;
                return true;

            case TrafficSignalState.Red:
                CurrentPhase = Other(CurrentPhase);
                CurrentPhaseState = TrafficSignalState.Green;
                _stageElapsedSeconds = 0.0;
                _secondsSinceCurrentPhaseDemand = HasDemand(CurrentPhase) ? 0.0 : Timing.PassageGapSeconds;
                return true;

            default:
                throw new InvalidOperationException("Unknown traffic-signal state.");
        }
    }

    private void AdvanceClocks(double deltaSeconds)
    {
        _stageElapsedSeconds += deltaSeconds;
        if (CurrentPhaseState != TrafficSignalState.Green)
        {
            return;
        }

        if (HasDemand(CurrentPhase))
        {
            _secondsSinceCurrentPhaseDemand = 0.0;
        }
        else
        {
            _secondsSinceCurrentPhaseDemand += deltaSeconds;
        }
    }

    private bool HasDemand(TrafficSignalPhase phase) =>
        _demandedControlIds.Any(controlId => _phaseByControlId[controlId] == phase);

    private bool HasPhase(TrafficSignalPhase phase) => _phaseByControlId.Values.Contains(phase);

    private static TrafficSignalPhase Other(TrafficSignalPhase phase) =>
        phase == TrafficSignalPhase.Horizontal ? TrafficSignalPhase.Vertical : TrafficSignalPhase.Horizontal;
}
