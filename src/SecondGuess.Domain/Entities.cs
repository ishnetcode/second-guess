namespace SecondGuess.Domain;

/// <summary>How a team likes to play. All values are 0–1.</summary>
public sealed record TeamStyle(double Directness, double PressIntensity, double ShootingAppetite);

/// <summary>A player and their base position in the team's shape (own perspective, attacking towards x = 105).</summary>
public sealed record Player(string Id, string Name, int Number, string Position, double BaseX, double BaseY);

public sealed record Team(
    string Id,
    string Name,
    string ShortName,
    string Formation,
    TeamStyle Style,
    IReadOnlyList<Player> Players);

public sealed record MatchInfo(
    string MatchId,
    int Seed,
    string Competition,
    string Venue,
    DateTimeOffset KickOff,
    Team Home,
    Team Away);

/// <summary>A player's location at the moment of an event (same perspective as the event).</summary>
public sealed record PlayerPosition(string PlayerId, string TeamId, double X, double Y);

/// <summary>
/// One match event. Coordinates are in metres from the perspective of <see cref="TeamId"/>,
/// attacking towards x = 105. Optional fields are omitted from JSON when not relevant.
/// </summary>
public sealed record MatchEvent
{
    public required string EventId { get; init; }
    public required string MatchId { get; init; }
    public required int Period { get; init; }
    public required string MatchClock { get; init; }
    public required long TimestampMs { get; init; }
    public required string Type { get; init; }
    public string? TeamId { get; init; }
    public string? PlayerId { get; init; }
    public string? ReceiverId { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
    public double? EndX { get; init; }
    public double? EndY { get; init; }
    public string? Outcome { get; init; }
    public double? Pressure { get; init; }
    public double? BallSpeedKmh { get; init; }
    public double? PlayerSpeedKmh { get; init; }
    public double? Xg { get; init; }
    public string? BodyPart { get; init; }
    public required int PossessionId { get; init; }
    public required int HomeScore { get; init; }
    public required int AwayScore { get; init; }
    public IReadOnlyList<PlayerPosition>? FreezeFrame { get; init; }
}

/// <summary>Event type names used in the dataset.</summary>
public static class EventTypes
{
    public const string PeriodStart = "period_start";
    public const string PeriodEnd = "period_end";
    public const string PossessionChange = "possession_change";
    public const string Pass = "pass";
    public const string Carry = "carry";
    public const string Shot = "shot";
    public const string Pressure = "pressure";
    public const string Tackle = "tackle";
    public const string Interception = "interception";
    public const string BallRecovery = "ball_recovery";
}
