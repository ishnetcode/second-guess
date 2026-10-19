using System.Text.Json;
using SecondGuess.Domain;

namespace SecondGuess.Simulation;

/// <summary>
/// Saves each simulation as a labelled "alternative decision → outcome" record in
/// data/{matchId}/counterfactuals.jsonl, building a reusable synthetic dataset.
/// </summary>
public sealed class CounterfactualStore(string dataRoot)
{
    private static readonly object Gate = new();

    public CounterfactualRecord? Save(WhatIfResult result)
    {
        if (result.BestAlternative is null) return null;

        var record = new CounterfactualRecord(
            MatchId: result.MatchId,
            EventId: result.EventId,
            ActualKey: result.Actual.Key,
            ActualValue: result.Actual.MeanValue,
            AlternativeKey: result.BestAlternative.Key,
            AlternativeValue: result.BestAlternative.MeanValue,
            AlternativeP10: result.BestAlternative.P10,
            AlternativeP90: result.BestAlternative.P90,
            SimulationRuns: result.SimulationRuns,
            ModelVersion: result.ModelVersion,
            CreatedAt: DateTimeOffset.UtcNow);

        string folder = Path.Combine(Path.GetFullPath(dataRoot), result.MatchId);
        Directory.CreateDirectory(folder);

        lock (Gate)
        {
            File.AppendAllText(
                Path.Combine(folder, "counterfactuals.jsonl"),
                JsonSerializer.Serialize(record, JsonDefaults.Compact) + Environment.NewLine);
        }
        return record;
    }
}
