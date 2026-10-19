using SecondGuess.Domain;

namespace SecondGuess.Generator;

/// <summary>Prints per-team totals and checks them against realistic ranges.</summary>
public static class MatchSummary
{
    public static void Print(MatchResult result)
    {
        var info = result.Info;
        var events = result.Events;
        var last = events[^1];

        Console.WriteLine();
        Console.WriteLine($"  {info.Home.Name} {last.HomeScore} - {last.AwayScore} {info.Away.Name}   (seed {info.Seed}, {events.Count} events)");
        Console.WriteLine($"  {"",-22}{info.Home.ShortName,10}{info.Away.ShortName,10}   realistic range");

        Row("Passes", t => Count(events, t, EventTypes.Pass), 350, 650);
        Row("Pass accuracy %", t => Percent(
                Count(events, t, EventTypes.Pass, "complete"),
                Count(events, t, EventTypes.Pass)), 72, 90);
        Row("Shots", t => Count(events, t, EventTypes.Shot), 5, 20);
        Row("Shots on target", t =>
                Count(events, t, EventTypes.Shot, "goal") + Count(events, t, EventTypes.Shot, "saved"), 1, 9);
        Row("xG", t => Math.Round(events
                .Where(e => e.TeamId == t && e.Type == EventTypes.Shot)
                .Sum(e => e.Xg ?? 0), 2), 0.3, 2.8);
        Row("Tackles won", t => Count(events, t, EventTypes.Tackle), 10, 45);
        Row("Interceptions", t => Count(events, t, EventTypes.Interception), 10, 60);
        Row("Pressures", t => Count(events, t, EventTypes.Pressure), null, null);
        Console.WriteLine();
    }

    private static void Row(string label, Func<string, double> metric, double? min, double? max)
    {
        double home = metric("home"), away = metric("away");
        string range = min is null ? "" : $"{min}-{max}";
        bool ok = min is null || (home >= min && home <= max && away >= min && away <= max);
        string flag = min is null ? "" : ok ? "OK" : "CHECK";
        Console.WriteLine($"  {label,-22}{home,10}{away,10}   {range,-10} {flag}");
    }

    private static int Count(IEnumerable<MatchEvent> events, string teamId, string type, string? outcome = null) =>
        events.Count(e => e.TeamId == teamId && e.Type == type && (outcome is null || e.Outcome == outcome));

    private static double Percent(int part, int whole) => whole == 0 ? 0 : Math.Round(100.0 * part / whole, 1);
}
