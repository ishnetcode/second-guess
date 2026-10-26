using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SecondGuess.Agents;
using SecondGuess.Domain;
using SecondGuess.Generator;
using SecondGuess.Simulation;
using SecondGuess.Simulation.Live;
using SecondGuess.Web.Services;

namespace SecondGuess.Web.Tests;

public sealed class SecondGuessServiceTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "second-guess-web-tests", Guid.NewGuid().ToString("N"));
    private readonly MatchInfo _info;
    private readonly MatchResult _result;

    public SecondGuessServiceTests()
    {
        _info = MatchFactory.Create(42);
        _result = new MatchSimulator(_info).Run();
        string matchFolder = Path.Combine(_dataRoot, _info.MatchId);
        Directory.CreateDirectory(matchFolder);
        File.WriteAllText(Path.Combine(matchFolder, "match.json"), JsonSerializer.Serialize(_info, JsonDefaults.Compact));
        File.WriteAllLines(Path.Combine(matchFolder, "events.jsonl"),
            _result.Events.Select(e => JsonSerializer.Serialize(e, JsonDefaults.Compact)));
    }

    [Fact]
    public async Task Service_ListsMatchesAndExposesUnavailableAgentReasonWithoutEndpoint()
    {
        await using SecondGuessService service = CreateService();

        MatchListItem match = Assert.Single(service.ListMatches());

        Assert.Equal(_info.MatchId, match.MatchId);
        Assert.Equal(_info.Home.Name, match.Home);
        Assert.NotNull(service.AgentsUnavailableReason);
    }

    [Fact]
    public async Task Service_LoadsMatchAndCreatesIndependentLiveEngine()
    {
        await using SecondGuessService service = CreateService();

        LoadedMatch loaded = service.GetMatch(_info.MatchId);
        var engine = service.CreateLiveEngine(_info.MatchId);

        Assert.Equal(_info.MatchId, loaded.Info.MatchId);
        Assert.Equal(22, engine.Snapshot().Players.Count);
        Assert.NotSame(engine, service.CreateLiveEngine(_info.MatchId));
    }

    [Fact]
    public async Task Service_PreviewsMomentWithStateAndSimulation()
    {
        await using SecondGuessService service = CreateService();
        MatchEvent eventToPreview = _result.Events.First(e =>
            e.Type == EventTypes.Shot && e.FreezeFrame is not null);

        MomentView preview = await service.PreviewAsync(_info.MatchId, eventToPreview.EventId);

        Assert.Equal(eventToPreview.EventId, preview.State.Moment.EventId);
        Assert.Equal(eventToPreview.EventId, preview.Result.EventId);
        Assert.Equal(22, preview.State.Players.Count);
    }

    [Fact]
    public async Task Service_CachesKeyDecisionsAndOverlayCards()
    {
        await using SecondGuessService service = CreateService();

        IReadOnlyList<DecisionRanking> firstDecisions = await service.GetKeyDecisionsAsync(_info.MatchId);
        IReadOnlyList<DecisionRanking> secondDecisions = await service.GetKeyDecisionsAsync(_info.MatchId);
        IReadOnlyList<OverlayCard> firstCards = service.GetOverlayCards(_info.MatchId);
        IReadOnlyList<OverlayCard> secondCards = service.GetOverlayCards(_info.MatchId);

        Assert.Same(firstDecisions, secondDecisions);
        Assert.Same(firstCards, secondCards);
        Assert.All(firstDecisions, decision => Assert.True(decision.Difference > 0));
        Assert.NotEmpty(firstCards);
    }

    [Fact]
    public async Task Service_ReturnsNotConfiguredErrorBeforeCallingAgents()
    {
        await using SecondGuessService service = CreateService();
        var request = new WhatIfRequest("What if he passed?");
        var recap = new RecapRequest(_info.MatchId, "{}");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AskAsync(request));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecapAsync(recap));
    }

    [Fact]
    public async Task Service_PropagatesUnknownMatchErrors()
    {
        await using SecondGuessService service = CreateService();

        Assert.Throws<KeyNotFoundException>(() => service.GetMatch("m_missing"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.PreviewAsync("m_missing", "evt_000001"));
    }

    private SecondGuessService CreateService()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecondGuess:DataRoot"] = _dataRoot
            })
            .Build();
        IHostEnvironment environment = new TestHostEnvironment(_dataRoot);
        return new SecondGuessService(configuration, environment);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "SecondGuess.Web.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
