using System.Text.Json;
using SecondGuess.Domain;
using SecondGuess.Generator;
using SecondGuess.Simulation;

namespace SecondGuess.Simulation.Tests;

public sealed class SimulationTests : IDisposable
{
    private readonly string _dataRoot;
    private readonly MatchRepository _repository;
    private readonly LoadedMatch _match;
    private readonly MatchEvent _moment;

    public SimulationTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "second-guess-simulation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataRoot);

        MatchInfo match = MatchFactory.Create(42);
        MatchResult result = new MatchSimulator(match).Run();
        string matchFolder = Path.Combine(_dataRoot, match.MatchId);
        Directory.CreateDirectory(matchFolder);
        File.WriteAllText(Path.Combine(matchFolder, "match.json"), JsonSerializer.Serialize(match, JsonDefaults.Compact));
        File.WriteAllLines(
            Path.Combine(matchFolder, "events.jsonl"),
            result.Events.Select(e => JsonSerializer.Serialize(e, JsonDefaults.Compact)));

        _repository = new MatchRepository(_dataRoot);
        _match = _repository.Get(match.MatchId);
        _moment = _match.Events.First(e => IsSimulatable(e));
    }

    [Fact]
    public void StableHash_IsStableAndDistinguishesInputs()
    {
        Assert.Equal(StableHash.Of("event|shot"), StableHash.Of("event|shot"));
        Assert.NotEqual(StableHash.Of("event|shot"), StableHash.Of("event|carry"));
    }

    [Fact]
    public void Repository_ListsAndCachesGeneratedMatch()
    {
        MatchListItem summary = Assert.Single(_repository.ListMatches());

        Assert.Equal(_match.Info.MatchId, summary.MatchId);
        Assert.Equal(_match.Info.Home.Name, summary.Home);
        Assert.Equal(_match.Info.Away.Name, summary.Away);
        Assert.Equal(_match.Events.Count, summary.EventCount);
        Assert.Same(_match, _repository.Get(_match.Info.MatchId));
    }

    [Fact]
    public void Repository_ThrowsForUnknownMatch()
    {
        KeyNotFoundException exception = Assert.Throws<KeyNotFoundException>(() => _repository.Get("m_missing"));

        Assert.Contains("m_missing", exception.Message);
    }

    [Fact]
    public void Repository_ListsNoMatchesWhenDataRootDoesNotExist()
    {
        var repository = new MatchRepository(Path.Combine(_dataRoot, "missing"));

        Assert.Empty(repository.ListMatches());
    }

    [Fact]
    public void LoadedMatch_ResolvesKnownAndUnknownPlayersTeamsAndEvents()
    {
        Assert.Equal(_match.Info.Home.Players[0].Name, _match.PlayerName(_match.Info.Home.Players[0].Id));
        Assert.Equal("Unknown", _match.PlayerName("player_missing"));
        Assert.Equal(_match.Info.Home.Name, _match.TeamName(_match.Info.Home.Id));
        Assert.Equal(_match.Info.Away.Name, _match.TeamName(_match.Info.Away.Id));
        Assert.Equal("Unknown", _match.TeamName("team_missing"));
        Assert.Equal(_moment, _match.GetEvent(_moment.EventId));
        Assert.Throws<KeyNotFoundException>(() => _match.GetEvent("evt_missing"));
    }

    [Fact]
    public void Find_FiltersByTypeAndHonorsLimit()
    {
        IReadOnlyList<MomentSummary> moments = MomentFinder.Find(_match, _moment.Type, limit: 1);

        Assert.Single(moments);
        Assert.Equal(_moment.EventId, moments[0].EventId);
        Assert.Equal(_moment.Type, moments[0].Type);
    }

    [Fact]
    public void Find_FiltersByPlayerMinuteAndOrdersHighXgFirst()
    {
        MatchEvent shot = _match.Events.First(e => e.Type == EventTypes.Shot && e.FreezeFrame is not null);
        string playerName = _match.PlayerName(shot.PlayerId);
        int minute = int.Parse(shot.MatchClock.Split(':')[0]);

        IReadOnlyList<MomentSummary> byPlayer = MomentFinder.Find(_match, EventTypes.Shot, playerName, minute, limit: 5);
        IReadOnlyList<MomentSummary> byChance = MomentFinder.Find(_match, EventTypes.Shot, limit: 5);

        Assert.Contains(byPlayer, moment => moment.EventId == shot.EventId);
        Assert.Equal(byChance.OrderByDescending(moment => moment.Xg ?? 0).Select(moment => moment.EventId),
            byChance.Select(moment => moment.EventId));
    }

    [Fact]
    public void GetGameState_RejectsEventWithoutFreezeFrame()
    {
        MatchEvent eventWithoutFrame = _match.Events.First(e => e.FreezeFrame is null);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => MomentFinder.GetGameState(_match, eventWithoutFrame.EventId));

        Assert.Contains(eventWithoutFrame.EventId, exception.Message);
    }

    [Fact]
    public void GetGameState_SortsPlayersByDistanceToBall()
    {
        GameState state = MomentFinder.GetGameState(_match, _moment.EventId);

        Assert.Equal(_moment.EventId, state.Moment.EventId);
        Assert.NotEmpty(state.Players);
        Assert.Equal(
            state.Players.OrderBy(player => player.DistanceToBallMetres).Select(player => player.PlayerId),
            state.Players.Select(player => player.PlayerId));
        Assert.All(state.Players, player => Assert.InRange(player.DistanceToBallMetres, 0, double.MaxValue));
    }

    [Fact]
    public void Summarise_GoalShotReportsScoreBeforeGoal()
    {
        MatchEvent goal = _moment with
        {
            Type = EventTypes.Shot,
            Outcome = "goal",
            TeamId = "home",
            HomeScore = 2,
            AwayScore = 1
        };

        MomentSummary summary = MomentFinder.Summarise(_match, goal);

        Assert.Equal("1-1", summary.ScoreBefore);
    }

    [Fact]
    public void Simulate_IsDeterministicAndClampsRunCount()
    {
        var engine = new WhatIfEngine();

        WhatIfResult first = engine.Simulate(_match, _moment.EventId, runs: 1);
        WhatIfResult second = engine.Simulate(_match, _moment.EventId, runs: 1);

        Assert.Equal(100, first.SimulationRuns);
        Assert.Equal(WhatIfEngine.ModelVersion, first.ModelVersion);
        Assert.Equal(_moment.EventId, Assert.Single(first.SourceEventIds));
        Assert.Equal(
            JsonSerializer.Serialize(first, JsonDefaults.Compact),
            JsonSerializer.Serialize(second, JsonDefaults.Compact));
        Assert.NotEmpty(first.Options);
        Assert.Contains(first.Options, option => option.IsActual);
        Assert.All(first.Options, option =>
        {
            Assert.InRange(option.MeanValue, 0, 1);
            Assert.InRange(option.P10, 0, 1);
            Assert.InRange(option.P90, 0, 1);
            Assert.True(option.P10 <= option.MeanValue);
            Assert.True(option.MeanValue <= option.P90);
        });
    }

    [Fact]
    public void Simulate_RejectsMomentWithoutFreezeFrame()
    {
        MatchEvent eventWithoutFrame = _match.Events.First(e => e.FreezeFrame is null);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new WhatIfEngine().Simulate(_match, eventWithoutFrame.EventId));

        Assert.Contains(eventWithoutFrame.EventId, exception.Message);
    }

    [Theory]
    [InlineData(EventTypes.Shot)]
    [InlineData(EventTypes.Pass)]
    [InlineData(EventTypes.Carry)]
    public void Simulate_UsesActualActionForEachSupportedMomentType(string eventType)
    {
        MatchEvent moment = _match.Events.First(e => e.Type == eventType && IsSimulatable(e));

        WhatIfResult result = new WhatIfEngine().Simulate(_match, moment.EventId, runs: 100);

        Assert.Equal(eventType, result.ActualAction);
        Assert.True(result.Actual.IsActual);
        Assert.Equal(moment.EventId, Assert.Single(result.SourceEventIds));
        Assert.InRange(result.SimulationRuns, 100, 20_000);
    }

    [Fact]
    public void RankDecisions_ReturnsDescendingDifferencesWithinRequestedLimit()
    {
        IReadOnlyList<DecisionRanking> rankings = new WhatIfEngine().RankDecisions(_match, limit: 3, runs: 100);

        Assert.InRange(rankings.Count, 0, 3);
        Assert.Equal(rankings.OrderByDescending(ranking => ranking.Difference), rankings);
        Assert.All(rankings, ranking => Assert.NotEqual(string.Empty, ranking.EventId));
    }

    [Fact]
    public void CounterfactualStore_PersistsBestAlternativeAsJsonLines()
    {
        WhatIfResult result = new WhatIfEngine().Simulate(_match, _moment.EventId, runs: 100);
        var store = new CounterfactualStore(_dataRoot);

        CounterfactualRecord? record = store.Save(result);

        Assert.NotNull(record);
        string path = Path.Combine(_dataRoot, _match.Info.MatchId, "counterfactuals.jsonl");
        Assert.True(File.Exists(path));
        CounterfactualRecord? persisted = JsonSerializer.Deserialize<CounterfactualRecord>(File.ReadAllText(path), JsonDefaults.Compact);
        Assert.NotNull(persisted);
        Assert.Equal(record.EventId, persisted.EventId);
        Assert.Equal(record.AlternativeKey, persisted.AlternativeKey);
        Assert.Equal(result.SimulationRuns, persisted.SimulationRuns);
    }

    [Fact]
    public void CounterfactualStore_AppendsRecordsAndSkipsResultsWithoutAlternative()
    {
        WhatIfResult result = new WhatIfEngine().Simulate(_match, _moment.EventId, runs: 100);
        var store = new CounterfactualStore(_dataRoot);

        store.Save(result);
        store.Save(result);
        CounterfactualRecord? noAlternative = store.Save(result with { BestAlternative = null });

        string path = Path.Combine(_dataRoot, _match.Info.MatchId, "counterfactuals.jsonl");
        Assert.Null(noAlternative);
        Assert.Equal(2, File.ReadLines(path).Count());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static bool IsSimulatable(MatchEvent matchEvent) =>
        matchEvent.FreezeFrame is not null &&
        matchEvent.X is not null &&
        matchEvent.Y is not null &&
        matchEvent.Type is EventTypes.Shot or EventTypes.Pass or EventTypes.Carry;
}
