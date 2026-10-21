using SecondGuess.Domain;

namespace SecondGuess.Simulation;

/// <summary>Finds moments in a match from a loose description (player, minute, type).</summary>
public static class MomentFinder
{
    /// <summary>Event types that carry a freeze frame and can be simulated.</summary>
    public static readonly IReadOnlySet<string> SimulatableTypes =
        new HashSet<string> { EventTypes.Shot, EventTypes.Pass, EventTypes.Carry };

    public static IReadOnlyList<MomentSummary> Find(
        LoadedMatch match,
        string? eventType = EventTypes.Shot,
        string? playerName = null,
        int? minute = null,
        int limit = 10)
    {
        IEnumerable<MatchEvent> query = match.Events
            .Where(e => e.FreezeFrame is not null && SimulatableTypes.Contains(e.Type));

        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(e => e.Type.Equals(eventType, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(playerName))
            query = query.Where(e => match.PlayerName(e.PlayerId).Contains(playerName, StringComparison.OrdinalIgnoreCase));

        if (minute is int m)
        {
            query = query
                .Where(e => Math.Abs(Minute(e) - m) <= 1)
                .OrderBy(e => Math.Abs(Minute(e) - m));
        }
        else
        {
            // Without a minute, the biggest chances are the most interesting moments.
            query = query.OrderByDescending(e => e.Xg ?? 0).ThenBy(e => e.TimestampMs);
        }

        return query.Take(Math.Clamp(limit, 1, 50)).Select(e => Summarise(match, e)).ToList();
    }

    public static MomentSummary Summarise(LoadedMatch match, MatchEvent e) => new(
        EventId: e.EventId,
        Period: e.Period,
        MatchClock: e.MatchClock,
        Type: e.Type,
        TeamName: match.TeamName(e.TeamId),
        PlayerId: e.PlayerId ?? "",
        PlayerName: match.PlayerName(e.PlayerId),
        ReceiverName: e.ReceiverId is null ? null : match.PlayerName(e.ReceiverId),
        Outcome: e.Outcome,
        Xg: e.Xg,
        ScoreBefore: ScoreBefore(e));

    public static GameState GetGameState(LoadedMatch match, string eventId)
    {
        MatchEvent e = match.GetEvent(eventId);
        if (e.FreezeFrame is null || e.X is null || e.Y is null)
            throw new InvalidOperationException($"Event '{eventId}' has no player positions. Choose a pass, carry or shot.");

        var ball = new Point(e.X.Value, e.Y.Value);
        var players = e.FreezeFrame
            .Select(p => new PlayerSnapshot(
                PlayerId: p.PlayerId,
                Name: match.PlayerName(p.PlayerId),
                Position: match.Players.TryGetValue(p.PlayerId, out var pl) ? pl.Position : "",
                TeamName: match.TeamName(p.TeamId),
                IsTeammate: p.TeamId == e.TeamId,
                X: p.X,
                Y: p.Y,
                DistanceToBallMetres: Math.Round(Geometry.Distance(ball, new Point(p.X, p.Y)), 1)))
            .OrderBy(p => p.DistanceToBallMetres)
            .ToList();

        return new GameState(Summarise(match, e), ball.X, ball.Y, players, e.EndX, e.EndY);
    }

    private static int Minute(MatchEvent e) => int.Parse(e.MatchClock.Split(':')[0]);

    // The score stored on a goal-scoring shot already includes that goal.
    private static string ScoreBefore(MatchEvent e)
    {
        int home = e.HomeScore, away = e.AwayScore;
        if (e.Type == EventTypes.Shot && e.Outcome == "goal")
        {
            if (e.TeamId == "home") home--; else away--;
        }
        return $"{home}-{away}";
    }
}
