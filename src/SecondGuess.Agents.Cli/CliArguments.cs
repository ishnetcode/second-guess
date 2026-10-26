namespace SecondGuess.Agents.Cli;

/// <summary>Parsed command-line options for the agent client.</summary>
public sealed record CliArguments(
    string Question,
    string? Match,
    string Mode,
    string Language,
    string? Team)
{
    /// <summary>Parses the question and optional named arguments; returns false when no question is supplied.</summary>
    public static bool TryParse(string[] args, out CliArguments? parsed)
    {
        parsed = null;
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
            return false;

        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 1; index < args.Length - 1; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal)) continue;
            options[args[index][2..]] = args[index + 1];
            index++;
        }

        parsed = new CliArguments(
            args[0],
            options.GetValueOrDefault("match"),
            options.GetValueOrDefault("mode")?.Equals("expert", StringComparison.OrdinalIgnoreCase) == true ? "expert" : "fan",
            options.GetValueOrDefault("lang") ?? "English",
            options.GetValueOrDefault("team"));
        return true;
    }
}
