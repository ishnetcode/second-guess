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
    private readonly AIAgent _recapper;

    private WhatIfOrchestrator(AgentSettings settings, McpClient mcp, AIAgent momentFinder, AIAgent explainer, AIAgent localiser, AIAgent recapper)
    {
        _settings = settings;
        _mcp = mcp;
        _momentFinder = momentFinder;
        _explainer = explainer;
        _localiser = localiser;
        _recapper = recapper;
    }

    /// <summary>Starts the MCP server, connects to the model in Microsoft Foundry and creates the agents.</summary>
    public static async Task<WhatIfOrchestrator> CreateAsync(AgentSettings settings, CancellationToken cancellationToken = default)
    {
        // 1. Connect to the Second Guess MCP server (the simulation engine) over stdio.
        //    Use the built server DLL when available, so build output can't interfere with the stdio protocol.
        string serverDll = settings.McpServerDll ?? Path.Combine(settings.McpServerProject, "bin", "Debug", "net10.0", "SecondGuess.McpServer.dll");
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
        AIAgent recapper = chatClient.AsAIAgent(instructions: AgentInstructions.Recapper, name: "Recapper");

        return new WhatIfOrchestrator(settings, mcp, momentFinder, explainer, localiser, recapper);
    }

    /// <summary>Answers a what-if question end to end. <paramref name="onStep"/> is called as each agent finishes.</summary>
    public async Task<WhatIfAnswer> AskAsync(WhatIfRequest request, CancellationToken cancellationToken = default, Action<AgentStep>? onStep = null)
    {
        var session = new WhatIfSession(request, onStep);

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
            Trace: session.Trace,
            FocusOptionKey: OptionFocus.Resolve(request.Question, session.Simulation!)?.Key);
    }

    // ------------------------------------------------------------ agents 1 + 2

    /// <summary>Moment Finder picks a moment; Simulator runs it. If the moment can't be simulated, the error goes back to the Moment Finder.</summary>
    private async Task FindAndSimulateAsync(WhatIfSession session, CancellationToken ct)
    {
        string? feedback = null;

        // The viewer already picked a moment (for example by clicking it in the studio): no search needed.
        if (session.Request.EventId is { } chosenEvent && session.Request.MatchId is { } chosenMatch)
        {
            var pick = new MomentPick(chosenMatch, chosenEvent, "Moment selected by the viewer");
            session.AddStep(new AgentStep("Moment Finder", "use selected moment", $"{chosenMatch}/{chosenEvent}", true, TimeSpan.Zero));
            if (await TrySimulateAsync(session, pick, ct) is null) return;
            feedback = "the selected moment could not be simulated";
        }

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
            string? error = await TrySimulateAsync(session, pick, ct);
            if (error is null) return;
            feedback = error;
        }

        throw new InvalidOperationException($"Could not find a moment that matches the question. Last problem: {feedback}");
    }

    /// <summary>Simulator: deterministic call to the MCP tool. Returns null on success, or the error to send back.</summary>
    private async Task<string?> TrySimulateAsync(WhatIfSession session, MomentPick pick, CancellationToken ct)
    {
        session.Moment = pick;
        string json = await RunStepAsync(session, "Simulator", $"simulate {pick.MatchId}/{pick.EventId}",
            () => CallMcpToolAsync("simulate_options", new() { ["matchId"] = pick.MatchId, ["eventId"] = pick.EventId }, ct));

        if (TryParseError(json, out string error))
        {
            session.AddStep(new AgentStep("Simulator", "reject moment", error, false, TimeSpan.Zero));
            return error;
        }

        session.SimulationJson = json;
        session.Simulation = JsonSerializer.Deserialize<WhatIfResult>(json, JsonDefaults.Compact)
                             ?? throw new InvalidOperationException("The simulation returned no result.");
        return null;
    }

    // ------------------------------------------------------------ agents 3 + 4

    /// <summary>Explainer writes the answer; Fact-Checker verifies it and sends problems back until it passes.</summary>
    private async Task ExplainWithFactCheckAsync(WhatIfSession session, CancellationToken ct)
    {
        var request = session.Request;
        var focus = OptionFocus.Resolve(request.Question, session.Simulation!);
        bool actualWasBest = OptionFocus.ActualWasBest(session.Simulation!);
        string basePrompt = $"""
            Viewer question: {request.Question}
            Viewer mode: {request.Mode}
            Favourite team: {request.FavouriteTeam ?? "none"}
            Option the viewer asked about: {(focus is null ? "none named" : $"{focus.Description} (key {focus.Key})")}
            Was the actual decision already the best option: {(actualWasBest ? "yes" : "no")}
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
            if (report.Passed && request.Mode == ViewerMode.Fan)
            {
                // Fan mode must stay short; enforce it in code rather than trusting the model.
                var styleIssues = FanStyleIssues(session.Draft);
                if (styleIssues.Count > 0) report = new FactCheckReport(false, styleIssues, report.NumbersChecked);
            }
            session.FactCheck = report;
            session.AddStep(new AgentStep("Fact-Checker", request.Mode == ViewerMode.Fan ? "verify numbers and length" : "verify numbers",
                report.Passed ? $"{report.NumbersChecked} numbers verified" : string.Join(" ", report.Issues),
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
            session.AddStep(new AgentStep("Fact-Checker", "verify translation",
                report.Passed ? "numbers preserved" : string.Join(" ", report.Issues), report.Passed, TimeSpan.Zero));
            if (report.Passed) break;
        }

        session.FinalAnswer = translated;
    }

    // ------------------------------------------------------------ recaps

    /// <summary>
    /// Recapper writes a recap from live stats; the Fact-Checker checks every number against that data and sends
    /// problems back; the Localiser translates and the Fact-Checker confirms the numbers survived.
    /// </summary>
    public async Task<RecapAnswer> RecapAsync(RecapRequest request, CancellationToken cancellationToken = default, Action<AgentStep>? onStep = null)
    {
        var session = new WhatIfSession(new WhatIfRequest("recap", request.MatchId, request.Mode, request.Language, request.FavouriteTeam), onStep);
        string basePrompt = $"""
            Viewer mode: {request.Mode}
            Favourite team: {request.FavouriteTeam ?? "none"}
            Focus player: {request.FocusPlayerName ?? "none"}
            Match data (JSON):
            {request.Context}
            """;

        string prompt = basePrompt;
        string draft = "";
        FactCheckReport report = new(false, ["No recap written."], 0);
        int attempts = 0;

        for (int attempt = 1; attempt <= MaxExplainerAttempts; attempt++)
        {
            attempts = attempt;
            draft = (await RunStepAsync(session, "Recapper", attempt == 1 ? "write recap" : $"rewrite (attempt {attempt})",
                () => RunAgentAsync(_recapper, prompt, cancellationToken))).Trim();

            report = AnswerFactChecker.CheckAgainstSource(draft, request.Context);
            session.AddStep(new AgentStep("Fact-Checker", "verify numbers against live stats",
                report.Passed ? $"{report.NumbersChecked} numbers verified" : string.Join(" ", report.Issues), report.Passed, TimeSpan.Zero));
            if (report.Passed) break;

            prompt = $"""
                {basePrompt}

                Your previous recap:
                {draft}

                Fact-check feedback:
                {report.ToFeedback()}
                """;
        }

        string final = draft;
        bool english = request.Language.Equals("English", StringComparison.OrdinalIgnoreCase) || request.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        if (!english)
        {
            string translatePrompt = $"Target language: {request.Language}\n\nText:\n{draft}";
            final = (await RunStepAsync(session, "Localiser", $"translate to {request.Language}",
                () => RunAgentAsync(_localiser, translatePrompt, cancellationToken))).Trim();
            var translationReport = AnswerFactChecker.CheckTranslationAgainstSource(draft, final, request.Context);
            session.AddStep(new AgentStep("Fact-Checker", "verify translation",
                translationReport.Passed ? "numbers preserved" : string.Join(" ", translationReport.Issues), translationReport.Passed, TimeSpan.Zero));
        }

        return new RecapAnswer(final, draft, request.Language, report, attempts, session.Trace);
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
            session.AddStep(new AgentStep(agent, action, Summarise(output), true, stopwatch.Elapsed));
            return output;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            session.AddStep(new AgentStep(agent, action, ex.Message, false, stopwatch.Elapsed));
            throw;
        }
    }

    /// <summary>Fan answers: at most 3 sentences (not counting the source line) and at most 3 percentages.</summary>
    private static List<string> FanStyleIssues(string answer)
    {
        var issues = new List<string>();
        string body = SourceLine().Replace(answer, "");
        int sentences = SentenceEnd().Matches(body).Count;
        int percentages = PercentToken().Matches(body).Count;

        if (sentences > 3) issues.Add($"Fan mode allows at most 3 sentences before the source line; this answer has {sentences}. Cut it down.");
        if (percentages > 3) issues.Add($"Fan mode allows at most 3 percentages; this answer has {percentages}. Keep only the most important.");
        return issues;
    }

    [GeneratedRegex(@"\(Source:[^)]*\)", RegexOptions.IgnoreCase)]
    private static partial Regex SourceLine();

    [GeneratedRegex(@"[.!?](\s|$)")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?\s*%")]
    private static partial Regex PercentToken();

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
