using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using SecondGuess.Domain;
using SecondGuess.Simulation;

namespace SecondGuess.McpServer;

/// <summary>
/// MCP tools for the Second Guess what-if engine. Every tool returns JSON; errors are returned as
/// {"error": "..."} so the calling agent can recover (for example by searching for a different moment).
/// </summary>
[McpServerToolType]
public static class WhatIfTools
{
    // Ranking simulates every shot in a match, so results are cached: matches never change once generated.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> RankingCache = new();

    [McpServerTool(Name = "list_matches")]
    [Description("Lists the synthetic matches available, with teams and final score.")]
    public static string ListMatches(MatchRepository repository) =>
        Run(() => repository.ListMatches());

    [McpServerTool(Name = "find_moments")]
    [Description("Finds moments in a match that can be simulated (shots, passes, carries). " +
                 "Filter by player name and/or minute. Without a minute, returns the biggest chances first.")]
    public static string FindMoments(
        MatchRepository repository,
        [Description("Match ID, e.g. m_42")] string matchId,
        [Description("Event type: shot, pass or carry. Defaults to shot.")] string? eventType = "shot",
        [Description("Part of the player's name, e.g. 'Marlow'")] string? playerName = null,
        [Description("Match minute, e.g. 63. Matches within one minute are returned.")] int? minute = null,
        [Description("Maximum number of moments to return (1-50).")] int limit = 10) =>
        Run(() => MomentFinder.Find(repository.Get(matchId), eventType, playerName, minute, limit));

    [McpServerTool(Name = "rank_decisions")]
    [Description("Simulates every shot (or pass/carry) in a match and ranks them by how much better the best alternative " +
                 "would have been. Use this to find the decisions that cost the most, e.g. 'which shot should have been a pass?'.")]
    public static string RankDecisions(
        MatchRepository repository,
        WhatIfEngine engine,
        [Description("Match ID, e.g. m_42")] string matchId,
        [Description("Event type: shot, pass or carry. Defaults to shot.")] string eventType = "shot",
        [Description("Number of moments to return (1-20).")] int limit = 5) =>
        CachedRanking(repository, engine, matchId, eventType, limit);

    private static string CachedRanking(MatchRepository repository, WhatIfEngine engine, string matchId, string eventType, int limit)
    {
        string key = $"{matchId}|{eventType}|{limit}";
        if (RankingCache.TryGetValue(key, out string? cached)) return cached;

        string json = Run(() => engine.RankDecisions(repository.Get(matchId), eventType, limit, WhatIfEngine.DefaultRuns));
        if (!json.Contains("\"error\"")) RankingCache[key] = json;   // never cache failures
        return json;
    }

    [McpServerTool(Name = "get_game_state")]
    [Description("Returns the positions of all 22 players and the ball at a moment, sorted by distance to the ball. " +
                 "Coordinates are metres from the acting team's perspective, attacking towards x = 105.")]
    public static string GetGameState(
        MatchRepository repository,
        [Description("Match ID, e.g. m_42")] string matchId,
        [Description("Event ID from find_moments, e.g. evt_000482")] string eventId) =>
        Run(() => MomentFinder.GetGameState(repository.Get(matchId), eventId));

    [McpServerTool(Name = "simulate_options")]
    [Description("Simulates the actual decision at a moment and the realistic alternatives (shoot, carry, pass to each teammate). " +
                 "Returns each option's estimated chance of leading to a goal, with a P10-P90 range and the football factors behind it. " +
                 "Results are deterministic and are saved to the counterfactual dataset. Only state numbers returned by this tool.")]
    public static string SimulateOptions(
        MatchRepository repository,
        WhatIfEngine engine,
        CounterfactualStore store,
        [Description("Match ID, e.g. m_42")] string matchId,
        [Description("Event ID from find_moments, e.g. evt_000482")] string eventId,
        [Description("Number of Monte Carlo runs per option (100-20000). Defaults to 2000.")] int runs = WhatIfEngine.DefaultRuns) =>
        Run(() =>
        {
            var result = engine.Simulate(repository.Get(matchId), eventId, runs);
            store.Save(result);
            return result;
        });

    private static string Run<T>(Func<T> action)
    {
        try
        {
            return JsonSerializer.Serialize(action(), JsonDefaults.Indented);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or ArgumentException or InvalidDataException)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonDefaults.Indented);
        }
    }
}
