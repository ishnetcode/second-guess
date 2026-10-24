using SecondGuess.Simulation;

namespace SecondGuess.Agents;

/// <summary>How the answer is pitched to the viewer.</summary>
public enum ViewerMode
{
    /// <summary>Short, punchy verdict for a casual fan.</summary>
    Fan,
    /// <summary>Tactical breakdown with the numbers and factors, for analysts and studio segments.</summary>
    Expert
}

/// <summary>A viewer's question plus how they want the answer personalised.</summary>
public sealed record WhatIfRequest(
    string Question,
    string? MatchId = null,
    ViewerMode Mode = ViewerMode.Fan,
    string Language = "English",
    string? FavouriteTeam = null,
    string? EventId = null);   // set when the viewer picked a specific moment, so no search is needed

/// <summary>The moment the Moment Finder chose, and why.</summary>
public sealed record MomentPick(string MatchId, string EventId, string Reason);

/// <summary>One step in the agent pipeline, kept so the hand-offs are visible and auditable.</summary>
public sealed record AgentStep(string Agent, string Action, string Detail, bool Succeeded, TimeSpan Duration);

/// <summary>The final, fact-checked answer.</summary>
public sealed record WhatIfAnswer(
    string Answer,
    string EnglishAnswer,
    string Language,
    ViewerMode Mode,
    MomentPick Moment,
    WhatIfResult Simulation,
    FactCheckReport FactCheck,
    int ExplainerAttempts,
    IReadOnlyList<AgentStep> Trace,
    string? FocusOptionKey = null);   // the option the question asked about, if it named one

/// <summary>
/// Shared state passed between the agents. Each agent reads what earlier agents produced and adds its
/// own output, so the whole run can be inspected afterwards.
/// </summary>
public sealed class WhatIfSession(WhatIfRequest request, Action<AgentStep>? onStep = null)
{
    public WhatIfRequest Request { get; } = request;
    public MomentPick? Moment { get; set; }
    public WhatIfResult? Simulation { get; set; }
    public string? SimulationJson { get; set; }
    public string? Draft { get; set; }
    public FactCheckReport? FactCheck { get; set; }
    public int ExplainerAttempts { get; set; }
    public string? FinalAnswer { get; set; }
    public List<AgentStep> Trace { get; } = [];

    /// <summary>Records a hand-off and reports it live (for example to the studio UI).</summary>
    public void AddStep(AgentStep step)
    {
        Trace.Add(step);
        onStep?.Invoke(step);
    }
}

/// <summary>Settings for connecting to Microsoft Foundry and the Second Guess MCP server.</summary>
public sealed record AgentSettings(
    string Endpoint,
    string Deployment,
    string DataRoot,
    string McpServerProject,
    string? ApiKey = null,
    string DefaultMatchId = "m_100",
    string? McpServerDll = null)   // published MCP server (used in the container); otherwise the local build is used
{
    /// <summary>
    /// Reads AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_DEPLOYMENT and (optionally) AZURE_OPENAI_API_KEY.
    /// Without an API key, the agents sign in with your Azure account (az login).
    /// </summary>
    public static AgentSettings FromEnvironment(string repoRoot, string? defaultMatchId = null) => new(
        Endpoint: Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
                  ?? throw new InvalidOperationException("Set AZURE_OPENAI_ENDPOINT to your Foundry / Azure OpenAI endpoint."),
        Deployment: Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4.1-mini",
        DataRoot: Path.Combine(repoRoot, "data"),
        McpServerProject: Path.Combine(repoRoot, "src", "SecondGuess.McpServer"),
        ApiKey: Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY"),
        DefaultMatchId: defaultMatchId ?? "m_100");
}

/// <summary>A request for a match recap written from live stats (<see cref="Context"/> is the data JSON).</summary>
public sealed record RecapRequest(
    string MatchId,
    string Context,
    ViewerMode Mode = ViewerMode.Fan,
    string Language = "English",
    string? FavouriteTeam = null,
    string? FocusPlayerName = null);

/// <summary>A fact-checked recap.</summary>
public sealed record RecapAnswer(
    string Recap,
    string EnglishRecap,
    string Language,
    FactCheckReport FactCheck,
    int Attempts,
    IReadOnlyList<AgentStep> Trace);
