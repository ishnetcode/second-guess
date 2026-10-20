using SecondGuess.Agents;

// Ask the Second Guess agents a what-if question from the command line.
//
//   dotnet run --project src/SecondGuess.Agents.Cli -- "Which shot in m_100 should have been a pass?"
//   dotnet run --project src/SecondGuess.Agents.Cli -- "..." --mode expert --lang Spanish --team "Eastport Athletic"
//
// Requires AZURE_OPENAI_ENDPOINT (and optionally AZURE_OPENAI_DEPLOYMENT, AZURE_OPENAI_API_KEY).

if (args.Length == 0 || args[0].StartsWith("--"))
{
    Console.WriteLine("Usage: SecondGuess.Agents.Cli \"<question>\" [--match m_100] [--mode fan|expert] [--lang English] [--team \"<team>\"]");
    return 1;
}

string question = args[0];
var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (int i = 1; i < args.Length - 1; i++)
{
    if (args[i].StartsWith("--")) { options[args[i][2..]] = args[i + 1]; i++; }
}

string repoRoot = FindRepoRoot();
var settings = AgentSettings.FromEnvironment(repoRoot, options.GetValueOrDefault("match"));
var request = new WhatIfRequest(
    Question: question,
    MatchId: options.GetValueOrDefault("match"),
    Mode: options.GetValueOrDefault("mode")?.Equals("expert", StringComparison.OrdinalIgnoreCase) == true ? ViewerMode.Expert : ViewerMode.Fan,
    Language: options.GetValueOrDefault("lang") ?? "English",
    FavouriteTeam: options.GetValueOrDefault("team"));

Console.WriteLine($"\n  Question: {question}");
Console.WriteLine($"  Mode: {request.Mode}   Language: {request.Language}   Favourite team: {request.FavouriteTeam ?? "-"}\n");
Console.WriteLine("  Starting agents...");

await using var orchestrator = await WhatIfOrchestrator.CreateAsync(settings);
var answer = await orchestrator.AskAsync(request);

Console.WriteLine();
Console.WriteLine("  ---------------------------------------------------------------");
Console.WriteLine(Indent(answer.Answer));
Console.WriteLine("  ---------------------------------------------------------------");
Console.WriteLine();
Console.WriteLine($"  Moment: {answer.Moment.MatchId} / {answer.Moment.EventId} at {answer.Simulation.MatchClock}  ({answer.Moment.Reason})");
Console.WriteLine($"  Fact-check: {(answer.FactCheck.Passed ? "PASSED" : "ISSUES")} - {answer.FactCheck.NumbersChecked} numbers checked, " +
                  $"{answer.ExplainerAttempts} explainer attempt(s)");
Console.WriteLine();
Console.WriteLine("  Agent hand-offs:");
foreach (var step in answer.Trace)
{
    string status = step.Succeeded ? "ok  " : "FAIL";
    Console.WriteLine($"   {status} {step.Agent,-13} {step.Action,-28} {step.Duration.TotalSeconds,5:0.0}s  {step.Detail}");
}
Console.WriteLine();
return 0;

static string Indent(string text) => "  " + text.Replace("\n", "\n  ");

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SecondGuess.slnx")))
        dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Run this from inside the second-guess repository.");
}
