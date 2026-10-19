namespace SecondGuess.Domain;

/// <summary>
/// A point on the pitch, in metres.
/// </summary>
public readonly record struct Point(double X, double Y);

/// <summary>
/// Pitch geometry. All coordinates are in metres on a 105 x 68 pitch, from the
/// perspective of the team performing the event, which always attacks towards x = 105.
/// </summary>
public static class Pitch
{
    public const double Length = 105.0;
    public const double Width = 68.0;
    public const double CentreX = Length / 2;
    public const double CentreY = Width / 2;
    public const double GoalWidth = 7.32;

    public static readonly Point GoalCentre = new(Length, CentreY);

    /// <summary>Converts a point to the other team's perspective.</summary>
    public static Point Flip(Point p) => new(Length - p.X, Width - p.Y);

    /// <summary>Keeps a point at least 1 m inside the pitch.</summary>
    public static Point Clamp(double x, double y) =>
        new(Math.Clamp(x, 1, Length - 1), Math.Clamp(y, 1, Width - 1));
}
