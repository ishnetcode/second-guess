using SecondGuess.Domain;
using SecondGuess.Generator;

namespace SecondGuess.Generator.Tests;

public class MatchSimulatorTests
{
    [Fact]
    public void Run_IsDeterministicForTheSameSeed()
    {
        MatchResult first = new MatchSimulator(MatchFactory.Create(42)).Run();
        MatchResult second = new MatchSimulator(MatchFactory.Create(42)).Run();

        Assert.Equal(first.Events.Count, second.Events.Count);
        Assert.Equal(
            first.Events.Select(EventFingerprint),
            second.Events.Select(EventFingerprint));
    }

    [Fact]
    public void Run_ProducesSequentialEventIdsAndBothPeriods()
    {
        MatchResult result = new MatchSimulator(MatchFactory.Create(42)).Run();

        Assert.NotEmpty(result.Events);
        Assert.Equal("evt_000001", result.Events[0].EventId);
        Assert.Equal(
            Enumerable.Range(1, result.Events.Count).Select(index => $"evt_{index:000000}"),
            result.Events.Select(matchEvent => matchEvent.EventId));
        Assert.Contains(result.Events, matchEvent => matchEvent.Period == 1);
        Assert.Contains(result.Events, matchEvent => matchEvent.Period == 2);
        Assert.Equal(EventTypes.PeriodEnd, result.Events[^1].Type);
    }

    [Fact]
    public void Run_ProducesValidEventCoordinatesAndFreezeFrames()
    {
        MatchResult result = new MatchSimulator(MatchFactory.Create(7)).Run();

        foreach (MatchEvent matchEvent in result.Events)
        {
            Assert.InRange(matchEvent.Period, 1, 2);
            Assert.True(matchEvent.TimestampMs >= 0);

            if (matchEvent.X is double x)
                Assert.InRange(x, 0, Pitch.Length);
            if (matchEvent.Y is double y)
                Assert.InRange(y, 0, Pitch.Width);
            if (matchEvent.EndX is double endX)
                Assert.InRange(endX, 0, Pitch.Length);
            if (matchEvent.EndY is double endY)
                Assert.InRange(endY, 0, Pitch.Width);

            if (matchEvent.FreezeFrame is { } freezeFrame)
            {
                Assert.Equal(22, freezeFrame.Count);
                Assert.Equal(22, freezeFrame.Select(position => position.PlayerId).Distinct().Count());
                Assert.All(freezeFrame, position =>
                {
                    Assert.InRange(position.X, 0, Pitch.Length);
                    Assert.InRange(position.Y, 0, Pitch.Width);
                });
            }
        }
    }

    [Fact]
    public void Run_HasExpectedPeriodAndPossessionBoundaryEvents()
    {
        MatchResult result = new MatchSimulator(MatchFactory.Create(42)).Run();

        Assert.Equal(2, result.Events.Count(matchEvent => matchEvent.Type == EventTypes.PeriodStart));
        Assert.Equal(2, result.Events.Count(matchEvent => matchEvent.Type == EventTypes.PeriodEnd));
        Assert.Contains(result.Events, matchEvent =>
            matchEvent.Type == EventTypes.PossessionChange && matchEvent.Outcome == "kick_off");
    }

    private static string EventFingerprint(MatchEvent matchEvent) =>
        string.Join(
            '|',
            matchEvent.EventId,
            matchEvent.MatchId,
            matchEvent.Period,
            matchEvent.MatchClock,
            matchEvent.TimestampMs,
            matchEvent.Type,
            matchEvent.TeamId,
            matchEvent.PlayerId,
            matchEvent.ReceiverId,
            matchEvent.X,
            matchEvent.Y,
            matchEvent.EndX,
            matchEvent.EndY,
            matchEvent.Outcome,
            matchEvent.Pressure,
            matchEvent.Xg,
            matchEvent.HomeScore,
            matchEvent.AwayScore);
}
