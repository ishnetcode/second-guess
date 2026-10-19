namespace SecondGuess.Generator;

public static class RandomExtensions
{
    /// <summary>Normally distributed random number (Box–Muller transform).</summary>
    public static double NextGaussian(this Random rng, double mean = 0, double standardDeviation = 1)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        double standardNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + standardDeviation * standardNormal;
    }

    /// <summary>Uniform random number between <paramref name="min"/> and <paramref name="max"/>.</summary>
    public static double Uniform(this Random rng, double min, double max) =>
        min + (max - min) * rng.NextDouble();

    /// <summary>Picks an index with probability proportional to its weight.</summary>
    public static int WeightedIndex(this Random rng, IReadOnlyList<double> weights)
    {
        double total = 0;
        foreach (double w in weights) total += w;

        double target = rng.NextDouble() * total;
        for (int i = 0; i < weights.Count; i++)
        {
            target -= weights[i];
            if (target <= 0) return i;
        }
        return weights.Count - 1;
    }
}
