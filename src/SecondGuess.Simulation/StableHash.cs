namespace SecondGuess.Simulation;

/// <summary>
/// Deterministic string hash (FNV-1a). Unlike string.GetHashCode it is the same in every process,
/// so simulations seeded from it are reproducible and can be fact-checked.
/// </summary>
public static class StableHash
{
    public static int Of(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in value)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (int)(hash & 0x7FFFFFFF);
        }
    }
}
