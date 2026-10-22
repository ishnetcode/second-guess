namespace SecondGuess.Simulation.Live;

/// <summary>Running team totals at a point in the match.</summary>
public sealed record TeamLiveStats(
    string TeamId,
    string Name,
    int Goals,
    int Passes,
    int CompletedPasses,
    int Shots,
    int ShotsOnTarget,
    double Xg,
    double PossessionPct,
    int Tackles,
    int Interceptions,
    int Recoveries,
    int Pressures)
{
    public double PassAccuracy => Passes == 0 ? 0 : (double)CompletedPasses / Passes;
}

/// <summary>Running totals for one player, used by Player Focus mode.</summary>
public sealed record PlayerLiveStats(
    string PlayerId,
    string Name,
    string TeamId,
    string Position,
    int Touches,
    int Passes,
    int CompletedPasses,
    int KeyPasses,
    int DifficultPasses,
    int Shots,
    double Xg,
    int TacklesWon,
    double TopSpeedKmh)
{
    public double PassAccuracy => Passes == 0 ? 0 : (double)CompletedPasses / Passes;
}

/// <summary>
/// A timed, machine-readable graphic for the live feed. A broadcast rendering partner can show it in sync with
/// the match using <see cref="TimestampMs"/> and <see cref="DisplaySeconds"/>; every card cites its source events.
/// </summary>
public sealed record OverlayCard(
    string Id,
    string MatchId,
    int Period,
    string MatchClock,
    long TimestampMs,
    string Type,
    string? TeamId,
    string? PlayerId,
    string? PlayerName,
    string Headline,
    string Detail,
    double? Value,
    string? Unit,
    int DisplaySeconds,
    int Priority,
    IReadOnlyList<string> SourceEventIds);

/// <summary>Overlay card types.</summary>
public static class CardTypes
{
    public const string Goal = "goal";
    public const string Shot = "shot";
    public const string SecondGuess = "second_guess";
    public const string PassQuality = "pass_quality";
    public const string Sprint = "sprint";
    public const string ControlChaos = "control_chaos";
    public const string Momentum = "momentum";
    public const string Milestone = "milestone";
    public const string Period = "period";
}

/// <summary>One point on the Control vs Chaos timeline.</summary>
public sealed record RhythmPoint(int Period, string MatchClock, long TimestampMs, double Chaos, double HomeXgLast10, double AwayXgLast10);

/// <summary>Everything the live screen shows at a moment.</summary>
public sealed record LiveSnapshot(
    string MatchId,
    int Period,
    string MatchClock,
    long TimestampMs,
    TeamLiveStats Home,
    TeamLiveStats Away,
    double Chaos,
    string ChaosLabel,
    string ChaosExplanation,
    double HomeXgLast10,
    double AwayXgLast10,
    IReadOnlyList<PlayerLiveStats> Players,
    string? LastActionEventId,
    bool Finished);
