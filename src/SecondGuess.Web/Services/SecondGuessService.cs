using System.Collections.Concurrent;
using SecondGuess.Agents;
using SecondGuess.Simulation;

namespace SecondGuess.Web.Services;

/// <summary>
/// Everything the web app needs: matches, instant engine previews, cached key decisions,
/// and the multi-agent orchestrator (started on the first question).
/// </summary>
public sealed class SecondGuessService : IAsyncDisposable
{
    private readonly MatchRepository _repository;
    private readonly WhatIfEngine _engine = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<DecisionRanking>> _keyDecisions = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly AgentSettings? _settings;
    private WhatIfOrchestrator? _orchestrator;

    public SecondGuessService(IConfiguration configuration, IHostEnvironment environment)
    {
        string repoRoot = configuration["SecondGuess:RepoRoot"] ?? FindRepoRoot(environment.ContentRootPath);
        string dataRoot = configuration["SecondGuess:DataRoot"] ?? Path.Combine(repoRoot, "data");
        _repository = new MatchRepository(dataRoot);

        // Environment variables are part of configuration, so AZURE_OPENAI_* set in the terminal work here too.
        string? endpoint = configuration["AZURE_OPENAI_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            AgentsUnavailableReason = "Set AZURE_OPENAI_ENDPOINT and AZURE_OPENAI_DEPLOYMENT, then restart the app to ask the agents.";
        }
        else
        {
            _settings = new AgentSettings(
                Endpoint: endpoint,
                Deployment: configuration["AZURE_OPENAI_DEPLOYMENT"] ?? "gpt-4.1-mini",
                DataRoot: dataRoot,
                McpServerProject: configuration["SecondGuess:McpServerProject"] ?? Path.Combine(repoRoot, "src", "SecondGuess.McpServer"),
                ApiKey: configuration["AZURE_OPENAI_API_KEY"]);
        }
    }

    /// <summary>Why the agents can't run, or null if they can.</summary>
    public string? AgentsUnavailableReason { get; }

    public IReadOnlyList<MatchListItem> ListMatches() => _repository.ListMatches();

    /// <summary>The shots where another option was best, simulated once per match (same run count as the full simulation) and cached.</summary>
    public Task<IReadOnlyList<DecisionRanking>> GetKeyDecisionsAsync(string matchId) =>
        Task.Run(() => _keyDecisions.GetOrAdd(matchId, id => _engine
            .RankDecisions(_repository.Get(id), limit: 6, runs: WhatIfEngine.DefaultRuns)
            .Where(d => d.Difference > 0)   // only moments where another option really was better
            .ToList()));

    /// <summary>Instant, deterministic preview of a moment: player positions plus the simulated options.</summary>
    public Task<MomentView> PreviewAsync(string matchId, string eventId) => Task.Run(() =>
    {
        var match = _repository.Get(matchId);
        return new MomentView(MomentFinder.GetGameState(match, eventId), _engine.Simulate(match, eventId));
    });

    /// <summary>Runs the full multi-agent pipeline.</summary>
    public async Task<WhatIfAnswer> AskAsync(WhatIfRequest request, Action<AgentStep>? onStep = null, CancellationToken cancellationToken = default)
    {
        if (_settings is null) throw new InvalidOperationException(AgentsUnavailableReason);

        await _startGate.WaitAsync(cancellationToken);
        try
        {
            _orchestrator ??= await WhatIfOrchestrator.CreateAsync(_settings, cancellationToken);
        }
        finally
        {
            _startGate.Release();
        }

        return await _orchestrator.AskAsync(request, cancellationToken, onStep);
    }

    private static string FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SecondGuess.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? start;
    }

    public async ValueTask DisposeAsync()
    {
        if (_orchestrator is not null) await _orchestrator.DisposeAsync();
    }
}

/// <summary>A moment ready to draw: positions and the simulated options.</summary>
public sealed record MomentView(GameState State, WhatIfResult Result);
