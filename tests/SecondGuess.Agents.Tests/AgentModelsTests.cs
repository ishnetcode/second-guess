using SecondGuess.Agents;
using SecondGuess.Simulation;

namespace SecondGuess.Agents.Tests;

public sealed class AgentModelsTests
{
    [Fact]
    public void Session_RecordsEachStepAndRaisesCallback()
    {
        var observed = new List<AgentStep>();
        var session = new WhatIfSession(new WhatIfRequest("What if he passed?"), observed.Add);
        var step = new AgentStep("Moment Finder", "choose moment", "evt_000001", true, TimeSpan.FromMilliseconds(5));

        session.AddStep(step);

        Assert.Equal([step], session.Trace);
        Assert.Equal([step], observed);
    }

    [Fact]
    public void RequestAndAnswerRetainViewerPreferences()
    {
        var request = new WhatIfRequest(
            "What if he passed?",
            "m_42",
            ViewerMode.Expert,
            "Spanish",
            "Northvale Rovers",
            "evt_000010");

        Assert.Equal(ViewerMode.Expert, request.Mode);
        Assert.Equal("Spanish", request.Language);
        Assert.Equal("Northvale Rovers", request.FavouriteTeam);
        Assert.Equal("evt_000010", request.EventId);
    }

    [Fact]
    public void AgentSettings_FromEnvironmentRequiresEndpoint()
    {
        string? previousEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        string? previousDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");
        string? previousKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

        try
        {
            Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", null);
            Environment.SetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT", null);
            Environment.SetEnvironmentVariable("AZURE_OPENAI_API_KEY", null);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => AgentSettings.FromEnvironment(@"C:\repo"));

            Assert.Contains("AZURE_OPENAI_ENDPOINT", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", previousEndpoint);
            Environment.SetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT", previousDeployment);
            Environment.SetEnvironmentVariable("AZURE_OPENAI_API_KEY", previousKey);
        }
    }

    [Fact]
    public void AgentSettings_FromEnvironmentUsesConfiguredValuesAndDefaults()
    {
        string? previousEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
        string? previousDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT");
        string? previousKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

        try
        {
            Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", "https://example.openai.azure.com/");
            Environment.SetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT", null);
            Environment.SetEnvironmentVariable("AZURE_OPENAI_API_KEY", null);

            AgentSettings settings = AgentSettings.FromEnvironment(@"C:\repo");

            Assert.Equal("https://example.openai.azure.com/", settings.Endpoint);
            Assert.Equal("gpt-4.1-mini", settings.Deployment);
            Assert.Equal(Path.Combine(@"C:\repo", "data"), settings.DataRoot);
            Assert.Equal(Path.Combine(@"C:\repo", "src", "SecondGuess.McpServer"), settings.McpServerProject);
            Assert.Equal("m_100", settings.DefaultMatchId);
            Assert.Null(settings.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AZURE_OPENAI_ENDPOINT", previousEndpoint);
            Environment.SetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT", previousDeployment);
            Environment.SetEnvironmentVariable("AZURE_OPENAI_API_KEY", previousKey);
        }
    }

    [Fact]
    public void AgentInstructions_RequireEvidenceAndRecoveryBehavior()
    {
        Assert.Contains("Never invent an event ID", AgentInstructions.MomentFinder);
        Assert.Contains("Use ONLY numbers", AgentInstructions.Explainer);
        Assert.Contains("Keep every number exactly as written", AgentInstructions.Localiser);
        Assert.Contains("If you receive fact-check feedback", AgentInstructions.Recapper);
    }

    [Fact]
    public void FactCheckReport_FormatsFeedbackForRewrite()
    {
        var failed = new FactCheckReport(false, ["Wrong percentage.", "Missing event ID."], 2);
        var passed = new FactCheckReport(true, [], 3);

        Assert.Contains("Wrong percentage.", failed.ToFeedback());
        Assert.Contains("Missing event ID.", failed.ToFeedback());
        Assert.Equal("All numbers match the simulation.", passed.ToFeedback());
    }
}
