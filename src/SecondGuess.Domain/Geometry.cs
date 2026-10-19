namespace SecondGuess.Domain;

public static class Geometry
{
    public static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Shortest distance from point <paramref name="p"/> to the segment a→b.</summary>
    public static double DistanceToSegment(Point p, Point a, Point b)
    {
        double vx = b.X - a.X, vy = b.Y - a.Y;
        double lengthSquared = vx * vx + vy * vy;
        double t = lengthSquared == 0
            ? 0
            : Math.Clamp(((p.X - a.X) * vx + (p.Y - a.Y) * vy) / lengthSquared, 0, 1);
        return Distance(p, new Point(a.X + t * vx, a.Y + t * vy));
    }

    /// <summary>
    /// True if <paramref name="p"/> is inside the triangle formed by the shooter and the two goalposts.
    /// </summary>
    public static bool InShotCone(Point p, Point shooter)
    {
        var leftPost = new Point(Pitch.Length, Pitch.CentreY - Pitch.GoalWidth / 2);
        var rightPost = new Point(Pitch.Length, Pitch.CentreY + Pitch.GoalWidth / 2);

        double d1 = Sign(p, shooter, leftPost);
        double d2 = Sign(p, leftPost, rightPost);
        double d3 = Sign(p, rightPost, shooter);

        bool hasNegative = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPositive = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNegative && hasPositive);
    }

    private static double Sign(Point p1, Point p2, Point p3) =>
        (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
}
