using System.Collections.Concurrent;
using System.Text.Json;
using SecondGuess.Domain;

namespace SecondGuess.Simulation;

/// <summary>A match loaded into memory with fast lookups.</summary>
public sealed class LoadedMatch
{
    public LoadedMatch(MatchInfo info, IReadOnlyList<MatchEvent> events)
    {
        Info = info;
        Events = events;
        EventsById = events.ToDictionary(e => e.EventId);
        Players = info.Home.Players.Concat(info.Away.Players).ToDictionary(p => p.Id);
    }

    public MatchInfo Info { get; }
    public IReadOnlyList<MatchEvent> Events { get; }
    public IReadOnlyDictionary<string, MatchEvent> EventsById { get; }
    public IReadOnlyDictionary<string, Player> Players { get; }

    public string PlayerName(string? playerId) =>
        playerId is not null && Players.TryGetValue(playerId, out var p) ? p.Name : "Unknown";

    public string TeamName(string? teamId) =>
        teamId == Info.Home.Id ? Info.Home.Name : teamId == Info.Away.Id ? Info.Away.Name : "Unknown";

    public MatchEvent GetEvent(string eventId) =>
        EventsById.TryGetValue(eventId, out var e)
            ? e
            : throw new KeyNotFoundException($"Event '{eventId}' was not found in match '{Info.MatchId}'.");
}

/// <summary>Loads generated matches (match.json + events.jsonl) from a data folder and caches them.</summary>
public sealed class MatchRepository
{
    private readonly ConcurrentDictionary<string, LoadedMatch> _cache = new();

    public MatchRepository(string dataRoot)
    {
        DataRoot = Path.GetFullPath(dataRoot);
    }

    public string DataRoot { get; }

    public IReadOnlyList<MatchListItem> ListMatches()
    {
        if (!Directory.Exists(DataRoot)) return [];

        return Directory.GetDirectories(DataRoot)
            .Where(d => File.Exists(Path.Combine(d, "match.json")))
            .Select(d => Get(Path.GetFileName(d)))
            .Select(m => new MatchListItem(
                m.Info.MatchId,
                m.Info.Home.Name,
                m.Info.Away.Name,
                $"{m.Events[^1].HomeScore}-{m.Events[^1].AwayScore}",
                m.Events.Count))
            .OrderBy(m => m.MatchId)
            .ToList();
    }

    public LoadedMatch Get(string matchId) => _cache.GetOrAdd(matchId, Load);

    private LoadedMatch Load(string matchId)
    {
        string folder = Path.Combine(DataRoot, matchId);
        string matchFile = Path.Combine(folder, "match.json");
        if (!File.Exists(matchFile))
            throw new KeyNotFoundException($"Match '{matchId}' was not found in {DataRoot}. Generate it first.");

        var info = JsonSerializer.Deserialize<MatchInfo>(File.ReadAllText(matchFile), JsonDefaults.Compact)
                   ?? throw new InvalidDataException($"{matchFile} is empty.");

        var events = File.ReadLines(Path.Combine(folder, "events.jsonl"))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<MatchEvent>(line, JsonDefaults.Compact)
                            ?? throw new InvalidDataException("Empty event line."))
            .ToList();

        return new LoadedMatch(info, events);
    }
}
