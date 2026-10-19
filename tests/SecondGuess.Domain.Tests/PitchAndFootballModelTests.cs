using SecondGuess.Domain;

namespace SecondGuess.Domain.Tests;

public class PitchAndFootballModelTests
{
    [Fact]
    public void Flip_IsItsOwnInverse()
    {
        var point = new Point(23.5, 11.25);

        Assert.Equal(point, Pitch.Flip(Pitch.Flip(point)));
    }

    [Fact]
    public void Clamp_KeepsCoordinatesInsideOneMetreBoundary()
    {
        var point = Pitch.Clamp(-10, 100);

        Assert.Equal(1, point.X);
        Assert.Equal(Pitch.Width - 1, point.Y);
    }

    [Fact]
    public void DistanceToSegment_UsesClosestPointOnSegment()
    {
        double distance = Geometry.DistanceToSegment(
            new Point(5, 3),
            new Point(0, 0),
            new Point(10, 0));

        Assert.Equal(3, distance, precision: 10);
    }

    [Fact]
    public void InShotCone_RecognizesPointBetweenShooterAndGoal()
    {
        Assert.True(Geometry.InShotCone(new Point(95, Pitch.CentreY), new Point(80, Pitch.CentreY)));
        Assert.False(Geometry.InShotCone(new Point(95, 10), new Point(80, Pitch.CentreY)));
    }

    [Fact]
    public void ExpectedGoals_DecreaseAsDefendersEnterTheCone()
    {
        var shooter = new Point(85, Pitch.CentreY);

        double openShot = FootballModels.ExpectedGoals(shooter, 0);
        double defendedShot = FootballModels.ExpectedGoals(shooter, 3);

        Assert.InRange(openShot, 0, 1);
        Assert.InRange(defendedShot, 0, 1);
        Assert.True(openShot > defendedShot);
    }

    [Fact]
    public void PassSuccess_DecreasesWithDistanceDefendersAndPressure()
    {
        double easyPass = FootballModels.PassSuccess(5, 0, 0);
        double difficultPass = FootballModels.PassSuccess(40, 2, 1);

        Assert.InRange(easyPass, 0, 1);
        Assert.InRange(difficultPass, 0, 1);
        Assert.True(easyPass > difficultPass);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 0)]
    [InlineData(20, 0)]
    public void PressureFromDistance_IsClampedToExpectedRange(double distance, double expected)
    {
        Assert.Equal(expected, FootballModels.PressureFromDistance(distance));
    }
}
