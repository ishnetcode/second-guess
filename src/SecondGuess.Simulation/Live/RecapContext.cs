using System.Text.Json;
using SecondGuess.Domain;

namespace SecondGuess.Simulation.Live;

/// <summary>
/// Builds the compact data a recap is written from: the score, team stats, rhythm, standout players and the key
/// overlay cards so far. The same text is used to fact-check the recap, so every number in it must come from here.
/// </summary>
public static class RecapContext
{
    public static string Build(LiveSnapshot s, IReadOnlyList<OverlayCard> cards, string? focusPlayerId = null)
    {
        var standouts = s.Players
            .OrderByDescending(p => p.Xg * 10 + p.KeyPasses * 2 + p.DifficultPasses + p.TacklesWon * 0.5)
            .Take(4)
            .Select(p => new
            {
                p.Name, Team = p.TeamId == s.Home.TeamId ? s.Home.Name : s.Away.Name, p.Position,
                p.Passes, PassAccuracyPct = Math.Round(p.PassAccuracy * 100), p.KeyPasses, p.DifficultPasses,
                p.Shots, p.Xg, p.TacklesWon, p.TopSpeedKmh
            });

        var focus = focusPlayerId is null ? null : s.Players.FirstOrDefault(p => p.PlayerId == focusPlayerId);

        var data = new
        {
            s.MatchId,
            Minute = s.MatchClock,
            s.Period,
            Finished = s.Finished,
            Score = new { Home = s.Home.Name, HomeGoals = s.Home.Goals, Away = s.Away.Name, AwayGoals = s.Away.Goals },
            Home = TeamData(s.Home),
            Away = TeamData(s.Away),
            Rhythm = new { ControlVsChaos = s.Chaos, s.ChaosLabel, s.ChaosExplanation, s.HomeXgLast10, s.AwayXgLast10 },
            Standouts = standouts,
            FocusPlayer = focus,
            KeyMoments = cards
                .Where(c => c.Priority >= 3)
                .TakeLast(12)
                .Select(c => new { c.MatchClock, c.Type, c.Headline, c.Detail })
        };

        return JsonSerializer.Serialize(data, JsonDefaults.Compact);
    }

    private static object TeamData(TeamLiveStats t) => new
    {
        t.Name, t.Goals, t.Shots, t.ShotsOnTarget, t.Xg, PossessionPct = t.PossessionPct,
        t.Passes, PassAccuracyPct = Math.Round(t.PassAccuracy * 100), t.Tackles, t.Interceptions, t.Recoveries, t.Pressures
    };
}
