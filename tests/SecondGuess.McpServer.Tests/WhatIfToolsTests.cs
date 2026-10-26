using System.Text.Json;
using SecondGuess.Domain;
using SecondGuess.Generator;
using SecondGuess.McpServer;
using SecondGuess.Simulation;

namespace SecondGuess.McpServer.Tests;

public sealed class WhatIfToolsTests : IDisposable
{
    private readonly string _dataRoot;
    private readonly MatchRepository _repository;
    private readonly MatchEvent _moment;

    public WhatIfToolsTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "second-guess-mcp-tests", Guid.NewGuid().ToString("N"));
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
        _moment = result.Events.First(e =>
            e.FreezeFrame is not null &&
            e.X is not null &&
            e.Y is not null &&
            e.Type is EventTypes.Shot or EventTypes.Pass or EventTypes.Carry);
    }

    [Fact]
    public void ListMatches_ReturnsGeneratedMatchSummary()
    {
        using JsonDocument document = JsonDocument.Parse(WhatIfTools.ListMatches(_repository));
        JsonElement match = Assert.Single(document.RootElement.EnumerateArray());

        Assert.Equal("m_42", match.GetProperty("matchId").GetString());
        Assert.Equal("Northvale Rovers", match.GetProperty("home").GetString());
        Assert.Equal("Eastport Athletic", match.GetProperty("away").GetString());
    }

    [Fact]
    public void FindMoments_ReturnsRequestedMoment()
    {
        string json = WhatIfTools.FindMoments(_repository, "m_42", _moment.Type, limit: 1);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement moment = Assert.Single(document.RootElement.EnumerateArray());

        Assert.Equal(_moment.EventId, moment.GetProperty("eventId").GetString());
        Assert.Equal(_moment.Type, moment.GetProperty("type").GetString());
    }

    [Fact]
    public void GetGameState_ReturnsPlayersSortedByDistance()
    {
        string json = WhatIfTools.GetGameState(_repository, "m_42", _moment.EventId);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement players = document.RootElement.GetProperty("players");
        double previousDistance = 0;

        Assert.NotEmpty(players.EnumerateArray());
        foreach (JsonElement player in players.EnumerateArray())
        {
            double distance = player.GetProperty("distanceToBallMetres").GetDouble();
            Assert.True(distance >= previousDistance);
            previousDistance = distance;
        }
    }

    [Fact]
    public void SimulateOptions_ClampsRunsAndIncludesSourceEvent()
    {
        var store = new CounterfactualStore(_dataRoot);
        string json = WhatIfTools.SimulateOptions(
            _repository,
            new WhatIfEngine(),
            store,
            "m_42",
            _moment.EventId,
            runs: 1);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal(100, document.RootElement.GetProperty("simulationRuns").GetInt32());
        Assert.Equal(_moment.EventId, document.RootElement.GetProperty("sourceEventIds")[0].GetString());
        Assert.True(File.Exists(Path.Combine(_dataRoot, "m_42", "counterfactuals.jsonl")));
    }

    [Fact]
    public void RankDecisions_ReturnsRankedResultsAndReusesCachedResult()
    {
        var engine = new WhatIfEngine();

        string first = WhatIfTools.RankDecisions(_repository, engine, "m_42", limit: 1);
        string second = WhatIfTools.RankDecisions(_repository, engine, "m_42", limit: 1);
        using JsonDocument document = JsonDocument.Parse(first);
        JsonElement[] rankings = document.RootElement.EnumerateArray().ToArray();

        Assert.Equal(first, second);
        Assert.InRange(rankings.Length, 0, 1);
        Assert.All(rankings, ranking => Assert.True(ranking.GetProperty("eventId").GetString()?.Length > 0));
    }

    [Fact]
    public void FindMoments_ForUnknownMatch_ReturnsErrorObject()
    {
        using JsonDocument document = JsonDocument.Parse(WhatIfTools.FindMoments(_repository, "m_missing"));

        Assert.Contains("m_missing", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void GetGameState_ForUnknownEvent_ReturnsErrorObject()
    {
        using JsonDocument document = JsonDocument.Parse(WhatIfTools.GetGameState(_repository, "m_42", "evt_missing"));

        Assert.Contains("evt_missing", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void FindMoments_ClampsRequestedLimit()
    {
        string json = WhatIfTools.FindMoments(_repository, "m_42", limit: 500);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.InRange(document.RootElement.GetArrayLength(), 0, 50);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }
}
