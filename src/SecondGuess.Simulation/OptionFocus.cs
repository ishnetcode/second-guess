using System.Text.RegularExpressions;

namespace SecondGuess.Simulation;

/// <summary>
/// Works out which option a viewer's question is about, so the pitch and the answer focus on it.
/// Deterministic keyword matching: a teammate's name means "pass to them", dribble/carry words mean the carry.
/// </summary>
public static partial class OptionFocus
{
    /// <summary>True when nothing would have been better than what the player actually did.</summary>
    public static bool ActualWasBest(WhatIfResult result) =>
        result.BestAlternative is null || result.BestAlternative.MeanValue <= result.Actual.MeanValue;

    /// <summary>The option the question names, or null if it doesn't name one (or names what actually happened).</summary>
    public static OptionResult? Resolve(string question, WhatIfResult result)
    {
        if (string.IsNullOrWhiteSpace(question)) return null;

        // The player on the ball is usually named in the question ("what was Quin Abara's best option?").
        // Remove his name first so it can't be mistaken for a teammate who shares his first name.
        string text = Regex.Replace(question, Regex.Escape(result.PlayerName), " ", RegexOptions.IgnoreCase);

        // 1. A teammate named in the question, by full name or surname. First names alone are too ambiguous.
        foreach (var option in result.Options.Where(o => o.Kind == "pass" && o.TargetPlayerName is not null))
        {
            string name = option.TargetPlayerName!;
            string surname = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];
            if (ContainsWord(text, name) || (surname.Length > 2 && ContainsWord(text, surname)))
                return option.IsActual ? null : option;
        }

        // 2. Dribbling or carrying the ball.
        if (CarryWords().IsMatch(text))
            return result.Options.FirstOrDefault(o => o.Kind == "carry" && !o.IsActual);

        return null;
    }

    private static bool ContainsWord(string text, string word) =>
        Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);

    [GeneratedRegex(@"\b(dribbl\w*|carr(y|ied|ying)|run with|ran with|take (him|them) on|beat (his|the) man)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CarryWords();
}
