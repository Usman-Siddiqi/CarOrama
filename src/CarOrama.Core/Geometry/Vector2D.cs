namespace CarOrama.Core.Geometry;

public readonly record struct Vector2D(double X, double Y)
{
    public static Vector2D Zero => new(0.0, 0.0);

    public double Length => Math.Sqrt((X * X) + (Y * Y));

    public Vector2D Normalized()
    {
        var length = Length;
        return length > 1e-9 ? this / length : Zero;
    }

    // Road-plane Y increases southward, so the clockwise rotation is the
    // driver's left rather than the usual Cartesian XY-plane rotation.
    public Vector2D PerpendicularLeft() => new(Y, -X);

    public static double Distance(Vector2D a, Vector2D b) => (a - b).Length;

    public static double Dot(Vector2D a, Vector2D b) => (a.X * b.X) + (a.Y * b.Y);

    public static Vector2D Lerp(Vector2D a, Vector2D b, double amount) => a + ((b - a) * amount);

    public static Vector2D operator +(Vector2D a, Vector2D b) => new(a.X + b.X, a.Y + b.Y);

    public static Vector2D operator -(Vector2D a, Vector2D b) => new(a.X - b.X, a.Y - b.Y);

    public static Vector2D operator *(Vector2D value, double scale) => new(value.X * scale, value.Y * scale);

    public static Vector2D operator /(Vector2D value, double scale) => new(value.X / scale, value.Y / scale);
}
