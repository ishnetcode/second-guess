namespace SecondGuess.Domain;

/// <summary>
/// Simple, explainable probability models shared by the generator and (later) the simulation engine.
/// Coefficients are illustrative and tuned so generated matches fall within realistic ranges.
/// </summary>
public static class FootballModels
{
    /// <summary>Expected goals (0–1) for a shot from (x, y) with a number of defenders in the shot cone.</summary>
    public static double ExpectedGoals(Point shooter, int defendersInCone)
    {
        double dx = Pitch.Length - shooter.X;
        double dy = Math.Abs(Pitch.CentreY - shooter.Y);
        double distance = Math.Sqrt(dx * dx + dy * dy);
        double halfGoal = Pitch.GoalWidth / 2;

        // Angle the goal mouth subtends from the shooter's position.
        double angle = Math.Atan2(Pitch.GoalWidth * dx, dx * dx + dy * dy - halfGoal * halfGoal);
        if (angle < 0) angle += Math.PI;

        double z = -1.2 - 0.09 * distance + 1.6 * angle - 0.35 * defendersInCone;
        return Sigmoid(z);
    }

    /// <summary>Probability (0–1) that a pass reaches its target.</summary>
    public static double PassSuccess(double distance, int defendersInLane, double pressure) =>
        Sigmoid(3.4 - 0.05 * distance - 0.8 * defendersInLane - 1.0 * pressure);

    /// <summary>Pressure on the ball carrier (0–1) from the distance to the nearest opponent.</summary>
    public static double PressureFromDistance(double nearestOpponentMetres) =>
        Math.Clamp(1 - (nearestOpponentMetres - 1) / 6, 0, 1);

    private static double Sigmoid(double z) => 1 / (1 + Math.Exp(-z));
}
