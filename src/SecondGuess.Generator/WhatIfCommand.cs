using SecondGuess.Simulation;

namespace SecondGuess.Generator;

/// <summary>
/// Runs a what-if simulation from the command line. Without an event ID it picks the biggest
/// missed chance in the match, which usually makes the best demo question.
/// </summary>
public static class WhatIfCommand
{
    public static void Run(string matchFolder, string? eventId)
    {
        string fullPath = Path.GetFullPath(matchFolder);
        string dataRoot = Path.GetDirectoryName(fullPath) ?? ".";
        string matchId = Path.GetFileName(fullPath);

        var repository = new MatchRepository(dataRoot);
        var match = repository.Get(matchId);

        var engine = new WhatIfEngine();

        if (eventId is null)
        {
            var ranking = engine.RankDecisions(match, limit: 5);
            if (ranking.Count == 0) throw new InvalidOperationException("No shots found in this match.");

            Console.WriteLine();
            Console.WriteLine("  Shots ranked by how much better the best alternative was:");
            Console.WriteLine();
            Console.WriteLine($"  {"Event",-12}{"Clock",-8}{"Player",-20}{"Shot",8}  {"Best alternative",-40}{"Value",8}");
            foreach (var r in ranking)
            {
                Console.WriteLine($"  {r.EventId,-12}{r.MatchClock,-8}{r.PlayerName,-20}{r.ActualValue,8:P1}  {r.BestAlternative,-40}{r.BestAlternativeValue,8:P1}");
            }

            eventId = ranking[0].EventId;
        }

        var result = engine.Simulate(match, eventId);
        new CounterfactualStore(dataRoot).Save(result);

        Console.WriteLine();
        Console.WriteLine($"  [{result.MatchClock}] {result.PlayerName} ({result.TeamName}): actual {result.ActualAction}, {result.ActualOutcome}   event {result.EventId}");
        Console.WriteLine();
        Console.WriteLine($"  {"Option",-40}{"Goal chance",12}{"P10-P90",18}");
        foreach (var o in result.Options)
        {
            string marker = o.IsActual ? "  <- actual" : o == result.BestAlternative ? "  <- best alternative" : "";
            Console.WriteLine($"  {o.Description,-40}{o.MeanValue,11:P1}{$"{o.P10:P1}-{o.P90:P1}",18}{marker}");
        }

        Console.WriteLine();
        if (result.BestAlternative is { } best)
        {
            string direction = result.DifferenceVsActual >= 0 ? "raised" : "lowered";
            Console.WriteLine($"  \"{best.Description}\" would have {direction} the chance of scoring from " +
                              $"{result.Actual.MeanValue:P0} to {best.MeanValue:P0}.");
        }
        Console.WriteLine($"  {result.SimulationRuns} simulations per option. Saved to counterfactuals.jsonl.");
        Console.WriteLine();
    }
}
