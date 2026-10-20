using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SecondGuess.Domain;
using SecondGuess.Simulation;

namespace SecondGuess.Agents;

/// <summary>
/// Coordinates five specialist agents to answer a what-if question:
/// <list type="number">
///   <item><b>Moment Finder</b> (LLM + MCP tools) turns the question into a match and event ID.</item>
///   <item><b>Simulator</b> (deterministic, via MCP) runs the what-if engine for that moment.</item>
///   <item><b>Explainer</b> (LLM) writes the answer for the viewer's mode and team.</item>
///   <item><b>Fact-Checker</b> (deterministic) verifies every number; failures go back to the Explainer.</item>
///   <item><b>Localiser</b> (LLM) translates the answer; the Fact-Checker verifies the numbers survived.</item>
/// </list>
/// All agents share a <see cref="WhatIfSession"/>, and every hand-off is recorded in its trace.
/// </summary>
public sealed partial class WhatIfOrchestrator : IAsyncDisposable
{
    private const int MaxExplainerAttempts = 3;
    private const int MaxMomentAttempts = 2;

    private static readonly string[] MomentFinderToolNames = ["list_matches", "rank_decisions", "find_moments", "get_game_state"];

    private readonly AgentSettings _settings;
    private readonly McpClient _mcp;
    private readonly AIAgent _momentFinder;
    private readonly AIAgent _explainer;
    private readonly AIAgent _localiser;

    private WhatIfOrchestrator(AgentSettings settings, McpClient mcp, AIAgent momentFinder, AIAgent explainer, AIAgent localiser)
    {
        _settings = settings;
        _mcp = mcp;
        _momentFinder = momentFinder;
        _explainer = explainer;
        _localiser = localiser;
    }

    /// <summary>Starts the MCP server, connects to the model in Microsoft Foundry and creates the agents.</summary>
    public static async Task<WhatIfOrchestrator> CreateAsync(AgentSettings settings, CancellationToken cancellationToken = default)
    {
        // 1. Connect to the Second Guess MCP server (the simulation engine) over stdio.
        //    Use the built server DLL when available, so build output can't interfere with the stdio protocol.
        string serverDll = Path.Combine(settings.McpServerProject, "bin", "Debug", "net10.0", "SecondGuess.McpServer.dll");
        string[] arguments = File.Exists(serverDll)
            ? new[] { serverDll, "--data", settings.DataRoot }
            : new[] { "run", "--project", settings.McpServerProject, "--", "--data", settings.DataRoot };

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "second-guess",
            Command = "dotnet",
            Arguments = arguments,
            ShutdownTimeout = TimeSpan.FromSeconds(10)
        });
        McpClient mcp = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

        IList<McpClientTool> allTools = await mcp.ListToolsAsync(cancellationToken: cancellationToken);
        AITool[] momentTools = allTools.Where(t => MomentFinderToolNames.Contains(t.Name)).Cast<AITool>().ToArray();

        // 2. Connect to the model deployed in Microsoft Foundry (Azure OpenAI). No key = sign in with your Azure account.
        AzureOpenAIClient openAi = string.IsNullOrWhiteSpace(settings.ApiKey)
            ? new AzureOpenAIClient(new Uri(settings.Endpoint), new DefaultAzureCredential())
            : new AzureOpenAIClient(new Uri(settings.Endpoint), new AzureKeyCredential(settings.ApiKey));
        // Convert to the Microsoft.Extensions.AI IChatClient abstraction that Agent Framework builds on.
        IChatClient chatClient = openAi.GetChatClient(settings.Deployment).AsIChatClient();

        // 3. Create the specialist agents with Microsoft Agent Framework.
        AIAgent momentFinder = chatClient.AsAIAgent(instructions: AgentInstructions.MomentFinder, name: "MomentFinder", tools: momentTools);
        AIAgent explainer = chatClient.AsAIAgent(instructions: AgentInstructions.Explainer, name: "Explainer");
        AIAgent localiser = chatClient.AsAIAgent(instructions: AgentInstructions.Localiser, name: "Localiser");

        return new WhatIfOrchestrator(settings, mcp, momentFinder, explainer, localiser);
    }

    /// <summary>Answers a what-if question end to end.</summary>
    public async Task<WhatIfAnswer> AskAsync(WhatIfRequest request, CancellationToken cancellationToken = default)
    {
        var session = new WhatIfSession(request);

        await FindAndSimulateAsync(session, cancellationToken);
        await ExplainWithFactCheckAsync(session, cancellationToken);
        await LocaliseAsync(session, cancellationToken);

        return new WhatIfAnswer(
            Answer: session.FinalAnswer!,
            EnglishAnswer: session.Draft!,
            Language: request.Language,
            Mode: request.Mode,
            Moment: session.Moment!,
            Simulation: session.Simulation!,
            FactCheck: session.FactCheck!,
            ExplainerAttempts: session.ExplainerAttempts,
            Trace: session.Trace);
    }

    // ------------------------------------------------------------ agents 1 + 2

    /// <summary>Moment Finder picks a moment; Simulator runs it. If the moment can't be simulated, the error goes back to the Moment Finder.</summary>
    private async Task FindAndSimulateAsync(WhatIfSession session, CancellationToken ct)
    {
        string? feedback = null;

        for (int attempt = 1; attempt <= MaxMomentAttempts; attempt++)
        {
            string prompt = $"""
                Viewer question: {session.Request.Question}
                Default match if none is named: {session.Request.MatchId ?? _settings.DefaultMatchId}
                {(feedback is null ? "" : $"Your previous choice failed: {feedback}. Choose a different moment.")}
                """;

            string reply = await RunStepAsync(session, "Moment Finder", "choose moment", () => RunAgentAsync(_momentFinder, prompt, ct));
            MomentPick? pick = ParseMoment(reply);
            if (pick is null)
            {
                feedback = "the reply was not the required JSON";
                continue;
            }
            session.Moment = pick;

            // Simulator: deterministic call to the MCP tool, so the numbers are exact and reproducible.
            string json = await RunStepAsync(session, "Simulator", $"simulate {pick.MatchId}/{pick.EventId}",
                () => CallMcpToolAsync("simulate_options", new() { ["matchId"] = pick.MatchId, ["eventId"] = pick.EventId }, ct));

            if (TryParseError(json, out string error))
            {
                feedback = error;
                session.Trace.Add(new AgentStep("Simulator", "reject moment", error, false, TimeSpan.Zero));
                continue;
            }

            session.SimulationJson = json;
            session.Simulation = JsonSerializer.Deserialize<WhatIfResult>(json, JsonDefaults.Compact)
                                 ?? throw new InvalidOperationException("The simulation returned no result.");
            return;
        }

        throw new InvalidOperationException($"Could not find a moment that matches the question. Last problem: {feedback}");
    }

    // ------------------------------------------------------------ agents 3 + 4

    /// <summary>Explainer writes the answer; Fact-Checker verifies it and sends problems back until it passes.</summary>
    private async Task ExplainWithFactCheckAsync(WhatIfSession session, CancellationToken ct)
    {
        var request = session.Request;
        string basePrompt = $"""
            Viewer question: {request.Question}
            Viewer mode: {request.Mode}
            Favourite team: {request.FavouriteTeam ?? "none"}
            Simulation result (JSON):
            {session.SimulationJson}
            """;

        string prompt = basePrompt;
        for (int attempt = 1; attempt <= MaxExplainerAttempts; attempt++)
        {
            session.ExplainerAttempts = attempt;
            string draft = await RunStepAsync(session, "Explainer", attempt == 1 ? "write answer" : $"rewrite (attempt {attempt})",
                () => RunAgentAsync(_explainer, prompt, ct));
            session.Draft = draft.Trim();

            var stopwatch = Stopwatch.StartNew();
            FactCheckReport report = AnswerFactChecker.Check(session.Draft, session.Simulation!);
            session.FactCheck = report;
            session.Trace.Add(new AgentStep("Fact-Checker", "verify numbers",
                report.Passed ? $"{report.NumbersChecked} numbers verified" : string.Join("; ", report.Issues),
                report.Passed, stopwatch.Elapsed));

            if (report.Passed) return;

            // Hand the problems back to the Explainer with its previous draft.
            prompt = $"""
                {basePrompt}

                Your previous answer:
                {session.Draft}

                Fact-check feedback:
                {report.ToFeedback()}
                """;
        }
        // After the final attempt the best draft is kept, but the report shows it did not fully pass.
    }

    // ------------------------------------------------------------ agent 5

    /// <summary>Localiser translates the checked answer; the Fact-Checker confirms every number survived.</summary>
    private async Task LocaliseAsync(WhatIfSession session, CancellationToken ct)
    {
        string language = session.Request.Language;
        if (language.Equals("English", StringComparison.OrdinalIgnoreCase) || language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            session.FinalAnswer = session.Draft;
            return;
        }

        string translated = "";
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            string prompt = $"Target language: {language}\n\nText:\n{session.Draft}";
            translated = (await RunStepAsync(session, "Localiser", $"translate to {language}", () => RunAgentAsync(_localiser, prompt, ct))).Trim();

            var report = AnswerFactChecker.CheckTranslation(session.Draft!, translated, session.Simulation!);
            session.Trace.Add(new AgentStep("Fact-Checker", "verify translation",
                report.Passed ? "numbers preserved" : string.Join("; ", report.Issues), report.Passed, TimeSpan.Zero));
            if (report.Passed) break;
        }

        session.FinalAnswer = translated;
    }

    // ------------------------------------------------------------ helpers

    private static async Task<string> RunAgentAsync(AIAgent agent, string prompt, CancellationToken ct)
    {
        // One retry for transient model or network errors.
        try
        {
            return (await agent.RunAsync(prompt, cancellationToken: ct)).ToString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            return (await agent.RunAsync(prompt, cancellationToken: ct)).ToString();
        }
    }

    private async Task<string> CallMcpToolAsync(string tool, Dictionary<string, object?> arguments, CancellationToken ct)
    {
        CallToolResult result = await _mcp.CallToolAsync(tool, arguments, cancellationToken: ct);
        string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
        return result.IsError is true ? JsonSerializer.Serialize(new { error = text }) : text;
    }

    private static async Task<string> RunStepAsync(WhatIfSession session, string agent, string action, Func<Task<string>> step)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            string output = await step();
            session.Trace.Add(new AgentStep(agent, action, Summarise(output), true, stopwatch.Elapsed));
            return output;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            session.Trace.Add(new AgentStep(agent, action, ex.Message, false, stopwatch.Elapsed));
            throw;
        }
    }

    private static MomentPick? ParseMoment(string reply)
    {
        Match json = JsonObject().Match(reply);
        if (!json.Success) return null;
        try
        {
            using var doc = JsonDocument.Parse(json.Value);
            var root = doc.RootElement;
            string? matchId = root.TryGetProperty("matchId", out var m) ? m.GetString() : null;
            string? eventId = root.TryGetProperty("eventId", out var e) ? e.GetString() : null;
            string reason = root.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
            return string.IsNullOrWhiteSpace(matchId) || string.IsNullOrWhiteSpace(eventId) ? null : new MomentPick(matchId, eventId, reason);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryParseError(string json, out string error)
    {
        error = "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var e))
            {
                error = e.GetString() ?? "unknown error";
                return true;
            }
            return false;
        }
        catch (JsonException)
        {
            error = "the simulator returned invalid JSON";
            return true;
        }
    }

    private static string Summarise(string text)
    {
        string oneLine = WhitespacePattern().Replace(text, " ").Trim();
        return oneLine.Length <= 160 ? oneLine : oneLine[..157] + "...";
    }

    [GeneratedRegex(@"\{[\s\S]*\}")]
    private static partial Regex JsonObject();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    public async ValueTask DisposeAsync() => await _mcp.DisposeAsync();
}
