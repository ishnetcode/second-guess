using System.Text.Json;
using SecondGuess.Domain;

namespace SecondGuess.Domain.Tests;

public sealed class SerializationAndRandomExtensionsTests
{
    [Fact]
    public void CompactJson_UsesCamelCaseAndOmitsNullProperties()
    {
        var value = new JsonProbe("Northvale", null);

        string json = JsonSerializer.Serialize(value, JsonDefaults.Compact);

        Assert.Contains("\"teamName\"", json);
        Assert.DoesNotContain("optionalNote", json);
        JsonProbe? roundTrip = JsonSerializer.Deserialize<JsonProbe>(json, JsonDefaults.Compact);
        Assert.Equal(value, roundTrip);
    }

    [Fact]
    public void IndentedJson_UsesCompactOptionsAndAddsFormatting()
    {
        string json = JsonSerializer.Serialize(new JsonProbe("Northvale", "synthetic"), JsonDefaults.Indented);

        Assert.Contains(Environment.NewLine, json);
        Assert.Contains("\"teamName\"", json);
        Assert.Contains("\"optionalNote\"", json);
    }

    [Fact]
    public void NextGaussian_IsDeterministicForSameSeed()
    {
        var first = new Random(42);
        var second = new Random(42);

        double[] firstSamples = Enumerable.Range(0, 20).Select(_ => first.NextGaussian(3, 2)).ToArray();
        double[] secondSamples = Enumerable.Range(0, 20).Select(_ => second.NextGaussian(3, 2)).ToArray();

        Assert.Equal(firstSamples, secondSamples);
        Assert.All(firstSamples, sample => Assert.InRange(sample, -10, 16));
    }

    [Fact]
    public void Uniform_ReturnsValuesInsideRequestedRange()
    {
        var rng = new Random(24);

        for (int sample = 0; sample < 100; sample++)
            Assert.InRange(rng.Uniform(-2.5, 8.25), -2.5, 8.25);
    }

    [Fact]
    public void WeightedIndex_SelectsOnlyPositiveWeight()
    {
        var rng = new Random(17);

        for (int sample = 0; sample < 100; sample++)
            Assert.Equal(1, rng.WeightedIndex([0, 4, 0]));
    }

    [Fact]
    public void WeightedIndex_IsDeterministicForSameSeed()
    {
        var first = new Random(84);
        var second = new Random(84);

        int[] firstSelections = Enumerable.Range(0, 100).Select(_ => first.WeightedIndex([1, 3, 6])).ToArray();
        int[] secondSelections = Enumerable.Range(0, 100).Select(_ => second.WeightedIndex([1, 3, 6])).ToArray();

        Assert.Equal(firstSelections, secondSelections);
        Assert.All(firstSelections, selection => Assert.InRange(selection, 0, 2));
    }

    private sealed record JsonProbe(string TeamName, string? OptionalNote);
}
