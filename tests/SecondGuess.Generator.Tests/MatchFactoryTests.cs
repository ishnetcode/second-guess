using SecondGuess.Domain;
using SecondGuess.Generator;

namespace SecondGuess.Generator.Tests;

public class MatchFactoryTests
{
    [Fact]
    public void Create_IsDeterministicForTheSameSeed()
    {
        MatchInfo first = MatchFactory.Create(42);
        MatchInfo second = MatchFactory.Create(42);

        Assert.Equal(first.MatchId, second.MatchId);
        Assert.Equal(first.Seed, second.Seed);
        Assert.Equal(first.Home.Style, second.Home.Style);
        Assert.Equal(first.Away.Style, second.Away.Style);
        Assert.Equal(first.Home.Players, second.Home.Players);
        Assert.Equal(first.Away.Players, second.Away.Players);
    }

    [Fact]
    public void Create_ProducesExpectedSyntheticMatchStructure()
    {
        MatchInfo match = MatchFactory.Create(42);

        Assert.Equal("m_42", match.MatchId);
        Assert.Equal("Synthetic League", match.Competition);
        Assert.Equal(11, match.Home.Players.Count);
        Assert.Equal(11, match.Away.Players.Count);
        Assert.Equal("4-3-3", match.Home.Formation);
        Assert.Equal("4-2-3-1", match.Away.Formation);
        Assert.All(match.Home.Players.Concat(match.Away.Players), player =>
        {
            Assert.InRange(player.BaseX, 0, Pitch.Length);
            Assert.InRange(player.BaseY, 0, Pitch.Width);
        });
    }

    [Fact]
    public void Create_UsesUniquePlayerIdsAndNamesAcrossBothTeams()
    {
        MatchInfo match = MatchFactory.Create(7);
        var players = match.Home.Players.Concat(match.Away.Players).ToArray();

        Assert.Equal(players.Length, players.Select(player => player.Id).Distinct().Count());
        Assert.Equal(players.Length, players.Select(player => player.Name).Distinct().Count());
    }

    [Fact]
    public void Create_ProducesDifferentMatchIdsForDifferentSeeds()
    {
        MatchInfo first = MatchFactory.Create(1);
        MatchInfo second = MatchFactory.Create(2);

        Assert.NotEqual(first.MatchId, second.MatchId);
        Assert.NotEqual(first.Home.Players.Select(player => player.Name), second.Home.Players.Select(player => player.Name));
    }
}
