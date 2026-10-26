using SecondGuess.Domain;
using SecondGuess.Generator;
using SecondGuess.Simulation;

namespace SecondGuess.Simulation.Tests;

public sealed class OptionFocusTests
{
    private readonly WhatIfResult _result;

    public OptionFocusTests()
    {
        MatchInfo info = MatchFactory.Create(7);
        LoadedMatch match = new(info, new MatchSimulator(info).Run().Events);
        MatchEvent moment = match.Events.First(e =>
            e.FreezeFrame is not null && e.Type is EventTypes.Shot or EventTypes.Pass or EventTypes.Carry);
        _result = new WhatIfEngine().Simulate(match, moment.EventId, runs: 100);
    }

    [Fact]
    public void Resolve_SelectsPassWhenQuestionNamesReceiver()
    {
        OptionResult? pass = _result.Options.FirstOrDefault(o =>
            o.Kind == "pass" && !o.IsActual && o.TargetPlayerName is not null);
        OptionResult selectedPass = Assert.IsType<OptionResult>(pass);

        OptionResult? selected = OptionFocus.Resolve($"Should he have passed to {selectedPass.TargetPlayerName}?", _result);

        Assert.Equal(selectedPass.Key, selected?.Key);
    }

    [Theory]
    [InlineData("Should he dribble?")]
    [InlineData("Could he carry the ball?")]
    [InlineData("Should he run with it?")]
    public void Resolve_SelectsCarryForCarryLanguage(string question)
    {
        OptionResult? carry = _result.Options.FirstOrDefault(o => o.Kind == "carry" && !o.IsActual);
        OptionResult selectedCarry = Assert.IsType<OptionResult>(carry);

        Assert.Equal(selectedCarry.Key, OptionFocus.Resolve(question, _result)?.Key);
    }

    [Fact]
    public void Resolve_ReturnsNullForBlankOrUnfocusedQuestion()
    {
        Assert.Null(OptionFocus.Resolve("", _result));
        Assert.Null(OptionFocus.Resolve("What happened?", _result));
    }

    [Fact]
    public void ActualWasBest_HandlesAlternativeAndNoAlternative()
    {
        Assert.Equal(
            _result.BestAlternative is null || _result.BestAlternative.MeanValue <= _result.Actual.MeanValue,
            OptionFocus.ActualWasBest(_result));
        Assert.True(OptionFocus.ActualWasBest(_result with { BestAlternative = null }));
    }
}
