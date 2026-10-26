using SecondGuess.Agents;

namespace SecondGuess.Agents.Tests;

public sealed class OrchestratorParsingTests
{
    [Fact]
    public void ParseMoment_ExtractsMomentFromAgentJson()
    {
        MomentPick? pick = WhatIfOrchestrator.ParseMoment(
            """{"matchId":"m_42","eventId":"evt_000123","reason":"A clear shooting chance."}""");

        Assert.Equal(new MomentPick("m_42", "evt_000123", "A clear shooting chance."), pick);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not JSON")]
    [InlineData("""{"matchId":"m_42","reason":"missing event"}""")]
    [InlineData("""{"matchId":null,"eventId":"evt_000123"}""")]
    public void ParseMoment_RejectsInvalidOrIncompleteJson(string reply)
    {
        Assert.Null(WhatIfOrchestrator.ParseMoment(reply));
    }

    [Fact]
    public void TryParseError_DistinguishesToolErrorsFromSuccessfulResults()
    {
        Assert.True(WhatIfOrchestrator.TryParseError("""{"error":"event not found"}""", out string error));
        Assert.Equal("event not found", error);
        Assert.False(WhatIfOrchestrator.TryParseError("""{"eventId":"evt_000123"}""", out _));
    }

    [Fact]
    public void TryParseError_ReportsInvalidJson()
    {
        Assert.True(WhatIfOrchestrator.TryParseError("{", out string error));

        Assert.Equal("the simulator returned invalid JSON", error);
    }

    [Fact]
    public void FanStyleIssues_EnforcesSentenceAndPercentageLimits()
    {
        List<string> issues = WhatIfOrchestrator.FanStyleIssues(
            "One is 20%. Two is 30%. Three is 40%. Four is 50%. (Source: evt_000001, 100 simulations)");

        Assert.Contains(issues, issue => issue.Contains("at most 3 sentences"));
        Assert.Contains(issues, issue => issue.Contains("at most 3 percentages"));
        Assert.Empty(WhatIfOrchestrator.FanStyleIssues("A short verdict. (Source: evt_000001, 100 simulations)"));
    }

    [Fact]
    public void Summarise_CollapsesWhitespaceAndBoundsLength()
    {
        Assert.Equal("short result", WhatIfOrchestrator.Summarise(" short \n result "));

        string summary = WhatIfOrchestrator.Summarise(new string('x', 200));
        Assert.Equal(160, summary.Length);
        Assert.EndsWith("...", summary);
    }
}
