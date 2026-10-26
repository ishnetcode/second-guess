using System.Text.Json;
using SecondGuess.Domain;
using SecondGuess.Generator;

namespace SecondGuess.Generator.Tests;

public sealed class MatchOutputTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "second-guess-output-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void MatchSummary_PrintsTeamsTotalsAndRangeChecks()
    {
        MatchResult result = new MatchSimulator(MatchFactory.Create(42)).Run();
        using var output = new StringWriter();
        TextWriter original = Console.Out;

        try
        {
            Console.SetOut(output);
            MatchSummary.Print(result);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Contains(result.Info.Home.Name, output.ToString());
        Assert.Contains(result.Info.Away.Name, output.ToString());
        Assert.Contains("Pass accuracy %", output.ToString());
        Assert.Contains("plausible range for one match", output.ToString());
    }

    [Fact]
    public async Task MatchReplayer_EmitsReadableEventsFromGeneratedFiles()
    {
        Directory.CreateDirectory(_folder);
        MatchInfo info = MatchFactory.Create(42);
        MatchResult result = new MatchSimulator(info).Run();
        await File.WriteAllTextAsync(Path.Combine(_folder, "match.json"), JsonSerializer.Serialize(info, JsonDefaults.Compact));
        await File.WriteAllLinesAsync(
            Path.Combine(_folder, "events.jsonl"),
            result.Events.Where(e => e.Type is EventTypes.PeriodStart or EventTypes.PeriodEnd)
                .Select(e => JsonSerializer.Serialize(e, JsonDefaults.Compact)));
        using var output = new StringWriter();
        TextWriter original = Console.Out;

        try
        {
            Console.SetOut(output);
            await MatchReplayer.RunAsync(_folder, speed: 1_000_000_000, CancellationToken.None);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Contains($"Replaying {info.Home.Name} vs {info.Away.Name}", output.ToString());
        Assert.Contains("PERIOD_START", output.ToString());
        Assert.Contains("PERIOD_END", output.ToString());
    }

    [Fact]
    public async Task MatchReplayer_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => MatchReplayer.RunAsync(_folder, 1, cancellation.Token));
    }

    [Fact]
    public void WhatIfCommand_ReportsUnknownEventWithoutWritingCounterfactual()
    {
        MatchInfo info = MatchFactory.Create(42);
        MatchResult result = new MatchSimulator(info).Run();
        string matchFolder = Path.Combine(_folder, info.MatchId);
        Directory.CreateDirectory(matchFolder);
        File.WriteAllText(Path.Combine(matchFolder, "match.json"), JsonSerializer.Serialize(info, JsonDefaults.Compact));
        File.WriteAllLines(Path.Combine(matchFolder, "events.jsonl"),
            result.Events.Select(e => JsonSerializer.Serialize(e, JsonDefaults.Compact)));

        KeyNotFoundException exception = Assert.Throws<KeyNotFoundException>(
            () => WhatIfCommand.Run(matchFolder, "evt_missing"));

        Assert.Contains("evt_missing", exception.Message);
        Assert.False(File.Exists(Path.Combine(matchFolder, "counterfactuals.jsonl")));
    }

    [Fact]
    public void WhatIfCommand_SimulatesExplicitEventAndSavesResult()
    {
        MatchInfo info = MatchFactory.Create(7);
        MatchResult result = new MatchSimulator(info).Run();
        MatchEvent shot = result.Events.First(e => e.Type == EventTypes.Shot && e.FreezeFrame is not null);
        string matchFolder = Path.Combine(_folder, info.MatchId);
        Directory.CreateDirectory(matchFolder);
        File.WriteAllText(Path.Combine(matchFolder, "match.json"), JsonSerializer.Serialize(info, JsonDefaults.Compact));
        File.WriteAllLines(Path.Combine(matchFolder, "events.jsonl"),
            result.Events.Select(e => JsonSerializer.Serialize(e, JsonDefaults.Compact)));
        using var output = new StringWriter();
        TextWriter original = Console.Out;

        try
        {
            Console.SetOut(output);
            WhatIfCommand.Run(matchFolder, shot.EventId);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Contains(shot.EventId, output.ToString());
        Assert.Contains("simulations per option", output.ToString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }
}
