using CarOrama.Core.Geometry;

namespace CarOrama.Core.Simulation;

public static class SimulationContract
{
    public const int CurrentVersion = 2;
}

internal static class SimulationContractValidation
{
    public static string RequireIdentifier(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty identifier is required.", parameterName);
        }

        return value;
    }

    public static double RequireFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value must be finite.");
        }

        return value;
    }

    public static double RequireNonNegative(double value, string parameterName)
    {
        RequireFinite(value, parameterName);
        if (value < 0.0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value cannot be negative.");
        }

        return value;
    }

    public static Vector2D RequireFinite(Vector2D value, string parameterName)
    {
        if (!double.IsFinite(value.X) || !double.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Both vector components must be finite.");
        }

        return value;
    }
}
