namespace CarOrama.Core.Random;

/// <summary>
/// Small, explicitly specified generator so procedural results do not depend on
/// changes to the framework's Random implementation.
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _state;

    public DeterministicRandom(long seed)
    {
        _state = unchecked((ulong)seed) + 0x9E3779B97F4A7C15UL;
        if (_state == 0)
        {
            _state = 0xA0761D6478BD642FUL;
        }

        NextUInt64();
    }

    public ulong NextUInt64()
    {
        var value = _state;
        value ^= value >> 12;
        value ^= value << 25;
        value ^= value >> 27;
        _state = value;
        return value * 0x2545F4914F6CDD1DUL;
    }

    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public int NextInt(int exclusiveMaximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMaximum);
        return (int)(NextUInt64() % (uint)exclusiveMaximum);
    }

    public void Shuffle<T>(IList<T> values)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            var other = NextInt(index + 1);
            (values[index], values[other]) = (values[other], values[index]);
        }
    }
}

