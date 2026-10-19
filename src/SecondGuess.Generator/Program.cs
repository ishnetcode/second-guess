using System.Text.Json;
using SecondGuess.Domain;
using SecondGuess.Generator;

// Usage:
//   dotnet run --project src/SecondGuess.Generator -- generate [--seed 42] [--matches 1] [--out data]
//   dotnet run --project src/SecondGuess.Generator -- replay --match data/m_42 [--speed 20]
//   dotnet run --project src/SecondGuess.Generator -- whatif --match data/m_42 [--event evt_000123]

string command = args.Length > 0 ? args[0] : "generate";
var options = ParseOptions(args.Skip(1).ToArray());

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    switch (command)
    {
        case "generate":
            Generate(
                seed: int.Parse(options.GetValueOrDefault("seed", "42")),
                matches: int.Parse(options.GetValueOrDefault("matches", "1")),
                outputRoot: options.GetValueOrDefault("out", "data"));
            break;

        case "replay":
            await MatchReplayer.RunAsync(
                matchFolder: options.GetValueOrDefault("match", "data/m_42"),
                speed: double.Parse(options.GetValueOrDefault("speed", "20"), System.Globalization.CultureInfo.InvariantCulture),
                cancellationToken: cts.Token);
            break;

        case "whatif":
            WhatIfCommand.Run(
                matchFolder: options.GetValueOrDefault("match", "data/m_42"),
                eventId: options.GetValueOrDefault("event"));
            break;

        default:
            Console.Error.WriteLine($"Unknown command '{command}'. Use 'generate', 'replay' or 'whatif'.");
            return 1;
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nStopped.");
}

return 0;

static void Generate(int seed, int matches, string outputRoot)
{
    for (int i = 0; i < matches; i++)
    {
        var info = MatchFactory.Create(seed + i);
        var result = new MatchSimulator(info).Run();

        string folder = Path.Combine(outputRoot, info.MatchId);
        Directory.CreateDirectory(folder);

        File.WriteAllText(Path.Combine(folder, "match.json"),
            JsonSerializer.Serialize(info, JsonDefaults.Indented));

        using (var writer = new StreamWriter(Path.Combine(folder, "events.jsonl")))
        {
            foreach (var e in result.Events)
                writer.WriteLine(JsonSerializer.Serialize(e, JsonDefaults.Compact));
        }

        MatchSummary.Print(result);
        Console.WriteLine($"  Saved to {folder}");
    }
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i].StartsWith("--"))
        {
            options[args[i][2..]] = args[i + 1];
            i++;
        }
    }
    return options;
}
