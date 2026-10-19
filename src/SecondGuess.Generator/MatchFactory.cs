using SecondGuess.Domain;

namespace SecondGuess.Generator;

/// <summary>Creates invented teams and players. No real clubs or players are used.</summary>
public static class MatchFactory
{
    // Position, shirt number, base X, base Y (own perspective, attacking towards x = 105).
    private static readonly (string Position, int Number, double X, double Y)[] Shape433 =
    [
        ("GK", 1, 5, 34), ("RB", 2, 25, 58), ("RCB", 4, 22, 42), ("LCB", 5, 22, 26), ("LB", 3, 25, 10),
        ("RCM", 8, 42, 46), ("CM", 6, 38, 34), ("LCM", 10, 42, 22),
        ("RW", 7, 62, 58), ("ST", 9, 66, 34), ("LW", 11, 62, 10)
    ];

    private static readonly (string Position, int Number, double X, double Y)[] Shape4231 =
    [
        ("GK", 1, 5, 34), ("RB", 2, 25, 58), ("RCB", 4, 22, 42), ("LCB", 5, 22, 26), ("LB", 3, 25, 10),
        ("RDM", 6, 38, 42), ("LDM", 8, 38, 26),
        ("RAM", 7, 58, 56), ("CAM", 10, 56, 34), ("LAM", 11, 58, 12), ("ST", 9, 68, 34)
    ];

    private static readonly string[] FirstNames =
    [
        "Alden", "Bram", "Cyrus", "Dario", "Eli", "Femi", "Gael", "Hugo", "Ilan", "Jovan",
        "Kai", "Leon", "Mats", "Nico", "Oren", "Pavel", "Quin", "Rafe", "Sami", "Tomas",
        "Ugo", "Viktor", "Wes", "Yusuf", "Zane", "Arlo", "Bastian", "Callum", "Devan", "Emre"
    ];

    private static readonly string[] LastNames =
    [
        "Ashdown", "Brenner", "Calloway", "Dunmore", "Ellery", "Fairbank", "Garrow", "Holloway",
        "Ingleby", "Jessop", "Kettering", "Lindqvist", "Marlow", "Northcote", "Okoro", "Pellham",
        "Quarrie", "Radley", "Sutcliffe", "Tarrant", "Underhill", "Varga", "Whitlock", "Yardley",
        "Zeller", "Abara", "Belmonte", "Castell", "Dravic", "Esposo"
    ];

    public static MatchInfo Create(int seed)
    {
        var rng = new Random(seed * 7919 + 17);
        var usedLastNames = new HashSet<string>();

        var home = CreateTeam("home", "Northvale Rovers", "NVR", "4-3-3", Shape433,
            new TeamStyle(Vary(rng, 0.65), Vary(rng, 0.50), Vary(rng, 1.00)), rng, usedLastNames);

        var away = CreateTeam("away", "Eastport Athletic", "EPA", "4-2-3-1", Shape4231,
            new TeamStyle(Vary(rng, 0.75), Vary(rng, 0.62), Vary(rng, 1.00)), rng, usedLastNames);

        return new MatchInfo(
            MatchId: $"m_{seed}",
            Seed: seed,
            Competition: "Synthetic League",
            Venue: "Northvale Park",
            KickOff: new DateTimeOffset(2026, 10, 24, 15, 0, 0, TimeSpan.Zero),
            Home: home,
            Away: away);
    }

    private static Team CreateTeam(
        string id, string name, string shortName, string formation,
        (string Position, int Number, double X, double Y)[] shape,
        TeamStyle style, Random rng, HashSet<string> usedLastNames)
    {
        var players = shape
            .Select(s => new Player(
                Id: $"{id[0]}_{s.Number:00}",
                Name: UniqueName(rng, usedLastNames),
                Number: s.Number,
                Position: s.Position,
                BaseX: s.X,
                BaseY: s.Y))
            .ToList();

        return new Team(id, name, shortName, formation, style, players);
    }

    /// <summary>Picks a first and last name; every player in the match gets a different surname.</summary>
    private static string UniqueName(Random rng, HashSet<string> usedLastNames)
    {
        while (true)
        {
            string lastName = LastNames[rng.Next(LastNames.Length)];
            if (usedLastNames.Add(lastName))
                return $"{FirstNames[rng.Next(FirstNames.Length)]} {lastName}";
        }
    }

    private static double Vary(Random rng, double value) =>
        Math.Clamp(value + rng.NextGaussian(0, 0.05), 0.1, 1.2);
}
