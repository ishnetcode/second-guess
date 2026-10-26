using SecondGuess.Domain;
using SecondGuess.Generator;
using SecondGuess.Simulation;

namespace SecondGuess.Simulation.Tests;

public sealed class AnswerFactCheckerTests
{
    private readonly WhatIfResult _result;

    public AnswerFactCheckerTests()
    {
        MatchInfo info = MatchFactory.Create(42);
        LoadedMatch match = new(info, new MatchSimulator(info).Run().Events);
        MatchEvent moment = match.Events.First(e =>
            e.Type == EventTypes.Shot && e.FreezeFrame is not null && e.X is not null && e.Y is not null);
        _result = new WhatIfEngine().Simulate(match, moment.EventId, runs: 100);
    }

    [Fact]
    public void Check_AcceptsGroundedPercentageAndSourceEventId()
    {
        string answer = $"{_result.Actual.MeanValue:P0} chance (Source: {_result.EventId}, 100 simulations)";

        FactCheckReport report = AnswerFactChecker.Check(answer, _result);

        Assert.True(report.Passed, string.Join(" ", report.Issues));
        Assert.Equal(2, report.NumbersChecked);
    }

    [Fact]
    public void Check_RejectsUnsupportedNumbersAndMissingSourceId()
    {
        FactCheckReport report = AnswerFactChecker.Check("99% from 999 metres.", _result);

        Assert.False(report.Passed);
        Assert.Contains(report.Issues, issue => issue.Contains("99%"));
        Assert.Contains(report.Issues, issue => issue.Contains("999 metres"));
        Assert.Contains(report.Issues, issue => issue.Contains(_result.EventId));
    }

    [Fact]
    public void Check_NormalizesDecimalCommaAndOptionalSourceRequirement()
    {
        double? passDistance = _result.Options.Select(o => o.Factors.PassDistanceMetres).FirstOrDefault(d => d.HasValue);
        Assert.NotNull(passDistance);
        string decimalComma = passDistance.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

        FactCheckReport withSource = AnswerFactChecker.Check($"{decimalComma} m (Source: {_result.EventId})", _result);
        FactCheckReport noSourceRequired = AnswerFactChecker.Check("No numeric claims.", _result, requireEventId: false);

        Assert.True(withSource.Passed, string.Join(" ", withSource.Issues));
        Assert.True(noSourceRequired.Passed, string.Join(" ", noSourceRequired.Issues));
    }

    [Fact]
    public void CheckTranslation_RequiresAllOriginalNumbersToSurvive()
    {
        string original = $"{_result.Actual.MeanValue:P0} (Source: {_result.EventId})";
        string unchanged = $"Veredicto: {_result.Actual.MeanValue:P0} (Source: {_result.EventId})";
        string missingValue = $"Veredicto (Source: {_result.EventId})";

        Assert.True(AnswerFactChecker.CheckTranslation(original, unchanged, _result).Passed);
        FactCheckReport report = AnswerFactChecker.CheckTranslation(original, missingValue, _result);
        Assert.False(report.Passed);
        Assert.Contains(report.Issues, issue => issue.Contains("missing the number"));
    }

    [Fact]
    public void CheckAgainstSource_OnlyAllowsValuesPresentInInputData()
    {
        const string source = """{"passes":42,"accuracy":0.75,"clock":"45:12"}""";

        FactCheckReport valid = AnswerFactChecker.CheckAgainstSource("42 passes, 75% accuracy at 45:12.", source);
        FactCheckReport invalid = AnswerFactChecker.CheckAgainstSource("99 passes.", source);

        Assert.True(valid.Passed, string.Join(" ", valid.Issues));
        Assert.False(invalid.Passed);
    }

    [Fact]
    public void CheckTranslationAgainstSource_RejectsDroppedValues()
    {
        const string source = """{"passes":42,"accuracy":0.75}""";

        FactCheckReport report = AnswerFactChecker.CheckTranslationAgainstSource("42 passes and 75% accuracy.", "42 Pässe.", source);

        Assert.False(report.Passed);
        Assert.Contains(report.Issues, issue => issue.Contains("missing the number"));
    }
}
