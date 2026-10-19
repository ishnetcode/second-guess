using System.Text.Json;
using SecondGuess.Domain;

namespace SecondGuess.Generator;

/// <summary>
/// Replays a generated match in (accelerated) real time, emitting events in order with
/// realistic gaps between them. Later steps send these to Azure Event Hubs / Fabric Eventstream
/// instead of the console.
/// </summary>
public static class MatchReplayer
{
    public static async Task RunAsync(string matchFolder, double speed, CancellationToken cancellationToken)
    {
        var info = JsonSerializer.Deserialize<MatchInfo>(
            await File.ReadAllTextAsync(Path.Combine(matchFolder, "match.json"), cancellationToken),
            JsonDefaults.Compact) ?? throw new InvalidOperationException("match.json is empty.");

        var names = info.Home.Players.Concat(info.Away.Players).ToDictionary(p => p.Id, p => p.Name);
        Console.WriteLine($"Replaying {info.Home.Name} vs {info.Away.Name} at {speed}x speed. Press Ctrl+C to stop.\n");

        long previousTimestamp = 0;
        await foreach (string line in File.ReadLinesAsync(Path.Combine(matchFolder, "events.jsonl"), cancellationToken))
        {
            var e = JsonSerializer.Deserialize<MatchEvent>(line, JsonDefaults.Compact);
            if (e is null) continue;

            long gapMs = e.TimestampMs - previousTimestamp;
            if (gapMs > 0) await Task.Delay(TimeSpan.FromMilliseconds(gapMs / speed), cancellationToken);
            previousTimestamp = e.TimestampMs;

            Console.WriteLine(Describe(e, names));
        }
    }

    private static string Describe(MatchEvent e, IReadOnlyDictionary<string, string> names)
    {
        string Name(string? id) => id is not null && names.TryGetValue(id, out var n) ? n : "";
        string score = $"{e.HomeScore}-{e.AwayScore}";

        return e.Type switch
        {
            EventTypes.Pass => $"[{e.MatchClock}] {score}  pass   {Name(e.PlayerId)} -> {Name(e.ReceiverId)} ({e.Outcome})",
            EventTypes.Shot => $"[{e.MatchClock}] {score}  SHOT   {Name(e.PlayerId)}  xG {e.Xg:0.00}  {e.BallSpeedKmh} km/h  => {e.Outcome?.ToUpperInvariant()}",
            EventTypes.Carry => $"[{e.MatchClock}] {score}  carry  {Name(e.PlayerId)} ({e.Outcome})",
            EventTypes.Tackle => $"[{e.MatchClock}] {score}  tackle {Name(e.PlayerId)} wins the ball",
            EventTypes.Interception => $"[{e.MatchClock}] {score}  intercept {Name(e.PlayerId)}",
            EventTypes.Pressure => $"[{e.MatchClock}] {score}  press  {Name(e.PlayerId)} (pressure {e.Pressure:0.00})",
            EventTypes.PossessionChange => $"[{e.MatchClock}] {score}  -- possession {e.TeamId} ({e.Outcome})",
            _ => $"[{e.MatchClock}] {score}  {e.Type.ToUpperInvariant()} (period {e.Period})"
        };
    }
}
