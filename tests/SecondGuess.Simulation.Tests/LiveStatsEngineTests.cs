using System.Text.Json;
using SecondGuess.Domain;
using SecondGuess.Generator;
using SecondGuess.Simulation.Live;

namespace SecondGuess.Simulation.Tests;

public sealed class LiveStatsEngineTests
{
    private readonly MatchInfo _info = MatchFactory.Create(42);
    private readonly MatchResult _result;

    public LiveStatsEngineTests()
    {
        _result = new MatchSimulator(_info).Run();
    }

    [Fact]
    public void Process_AccumulatesMatchStatsAndMarksFullTime()
    {
        var engine = new LiveStatsEngine(new LoadedMatch(_info, _result.Events));
        foreach (MatchEvent e in _result.Events) engine.Process(e);

        LiveSnapshot snapshot = engine.Snapshot();
        int homeShots = _result.Events.Count(e => e.TeamId == _info.Home.Id && e.Type == EventTypes.Shot);
        int awayShots = _result.Events.Count(e => e.TeamId == _info.Away.Id && e.Type == EventTypes.Shot);
        int homePasses = _result.Events.Count(e => e.TeamId == _info.Home.Id && e.Type == EventTypes.Pass);
        int awayPasses = _result.Events.Count(e => e.TeamId == _info.Away.Id && e.Type == EventTypes.Pass);

        Assert.True(snapshot.Finished);
        Assert.Equal(homeShots, snapshot.Home.Shots);
        Assert.Equal(awayShots, snapshot.Away.Shots);
        Assert.Equal(homePasses, snapshot.Home.Passes);
        Assert.Equal(awayPasses, snapshot.Away.Passes);
        Assert.InRange(snapshot.Chaos, 0, 100);
        Assert.Equal(22, snapshot.Players.Count);
        Assert.Equal(100, snapshot.Home.PossessionPct + snapshot.Away.PossessionPct, precision: 1);
    }

    [Fact]
    public void PassDifficulty_IncreasesWithDistanceAndPressure()
    {
        var shortPass = new MatchEvent
        {
            EventId = "evt_000001", MatchId = _info.MatchId, Period = 1, MatchClock = "01:00",
            TimestampMs = 60_000, Type = EventTypes.Pass, TeamId = _info.Home.Id,
            PossessionId = 1, HomeScore = 0, AwayScore = 0, X = 20, Y = 30, EndX = 25, EndY = 30,
            Pressure = 0
        };
        var longPressuredPass = shortPass with { EndX = 55, Pressure = 1 };

        Assert.InRange(LiveStatsEngine.PassDifficulty(shortPass), 0, 100);
        Assert.True(LiveStatsEngine.PassDifficulty(longPressuredPass) > LiveStatsEngine.PassDifficulty(shortPass));
        Assert.Equal(0, LiveStatsEngine.PassDifficulty(shortPass with { EndX = null }));
    }

    [Fact]
    public void BuildAllCards_CreatesTimedSourceLinkedCards()
    {
        IReadOnlyList<OverlayCard> cards = LiveStatsEngine.BuildAllCards(new LoadedMatch(_info, _result.Events));

        Assert.NotEmpty(cards);
        Assert.Equal(cards.Count, cards.Select(card => card.Id).Distinct().Count());
        Assert.All(cards, card =>
        {
            Assert.Equal(_info.MatchId, card.MatchId);
            Assert.True(card.DisplaySeconds > 0);
            Assert.True(card.TimestampMs >= 0);
        });
        Assert.Contains(cards, card => card.Type == CardTypes.Period && card.Headline.StartsWith("Full-time", StringComparison.Ordinal));
        Assert.Equal(_result.Events.Count(e => e.Type == EventTypes.Shot), cards.Count(card => card.Type is CardTypes.Shot or CardTypes.Goal));
    }

    [Fact]
    public void RecapContext_BuildsJsonWithTeamStatsFocusAndKeyMoments()
    {
        var match = new LoadedMatch(_info, _result.Events);
        var engine = new LiveStatsEngine(match);
        foreach (MatchEvent e in _result.Events) engine.Process(e);
        LiveSnapshot snapshot = engine.Snapshot();
        string focusPlayerId = _info.Home.Players[1].Id;
        string json = RecapContext.Build(snapshot, engine.Cards, focusPlayerId);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal(_info.MatchId, document.RootElement.GetProperty("matchId").GetString());
        Assert.Equal(_info.Home.Name, document.RootElement.GetProperty("home").GetProperty("name").GetString());
        Assert.Equal(focusPlayerId, document.RootElement.GetProperty("focusPlayer").GetProperty("playerId").GetString());
        Assert.True(document.RootElement.GetProperty("keyMoments").GetArrayLength() <= 12);
    }
}
