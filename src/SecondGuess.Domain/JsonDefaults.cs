using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecondGuess.Domain;

public static class JsonDefaults
{
    /// <summary>camelCase, case-insensitive, nulls omitted. Used for JSON Lines event files.</summary>
    public static readonly JsonSerializerOptions Compact = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Same as <see cref="Compact"/> but indented, for human-readable files like match.json.</summary>
    public static readonly JsonSerializerOptions Indented = new(Compact) { WriteIndented = true };
}
