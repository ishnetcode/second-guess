using SecondGuess.Agents.Cli;

namespace SecondGuess.Agents.Cli.Tests;

public sealed class CliArgumentsTests
{
    [Theory]
    [MemberData(nameof(MissingQuestionArgs))]
    public void TryParse_RejectsMissingQuestion(string[] args)
    {
        Assert.False(CliArguments.TryParse(args, out CliArguments? parsed));
        Assert.Null(parsed);
    }

    public static IEnumerable<object[]> MissingQuestionArgs()
    {
        yield return [Array.Empty<string>()];
        yield return [new[] { "--mode", "expert" }];
    }

    [Fact]
    public void TryParse_UsesDefaultsForOptionalArguments()
    {
        Assert.True(CliArguments.TryParse(["What if he passed?"], out CliArguments? parsed));

        Assert.NotNull(parsed);
        Assert.Equal("What if he passed?", parsed.Question);
        Assert.Null(parsed.Match);
        Assert.Equal("fan", parsed.Mode);
        Assert.Equal("English", parsed.Language);
        Assert.Null(parsed.Team);
    }

    [Fact]
    public void TryParse_ParsesOptionsWithoutCaseSensitivity()
    {
        Assert.True(CliArguments.TryParse(
            ["Question", "--MATCH", "m_42", "--MODE", "EXPERT", "--Lang", "Spanish", "--Team", "Northvale"],
            out CliArguments? parsed));

        Assert.NotNull(parsed);
        Assert.Equal("m_42", parsed.Match);
        Assert.Equal("expert", parsed.Mode);
        Assert.Equal("Spanish", parsed.Language);
        Assert.Equal("Northvale", parsed.Team);
    }

    [Fact]
    public void TryParse_TreatsUnknownModeAsFan()
    {
        Assert.True(CliArguments.TryParse(["Question", "--mode", "unknown"], out CliArguments? parsed));

        Assert.Equal("fan", parsed?.Mode);
    }
}
