using System.Globalization;
using System.Text.RegularExpressions;

namespace SecondGuess.Simulation;

/// <summary>The outcome of checking a written answer against a simulation result.</summary>
public sealed record FactCheckReport(bool Passed, IReadOnlyList<string> Issues, int NumbersChecked)
{
    /// <summary>Feedback the writing agent can act on.</summary>
    public string ToFeedback() => Passed
        ? "All numbers match the simulation."
        : "Fix these problems and rewrite the answer:\n- " + string.Join("\n- ", Issues);
}

/// <summary>
/// Deterministic fact-checker. Extracts every percentage, distance and number from an answer and
/// verifies each one against the simulation result it is meant to describe. Because the simulation
/// is deterministic, a passing answer is provably grounded in the data.
/// </summary>
/// <remarks>Works for translated answers too: decimal commas and "%" spacing are normalised.</remarks>
public static partial class AnswerFactChecker
{
    private const double PercentTolerance = 1.0;   // percentage points, allows rounding 41.8% to 42%
    private const double MetresTolerance = 0.6;
    private const double DecimalTolerance = 0.01;

    public static FactCheckReport Check(string answer, WhatIfResult result, bool requireEventId = true)
    {
        var issues = new List<string>();
        var allowed = AllowedValues.From(result);

        // Normalise: decimal commas (e.g. "41,8 %") and remove IDs and clock times so their digits are not checked.
        string text = DecimalComma().Replace(answer, "$1.$2");
        bool citesEvent = text.Contains(result.EventId, StringComparison.OrdinalIgnoreCase);
        text = EventIdPattern().Replace(text, " ");
        text = ClockPattern().Replace(text, " ");

        int checkedCount = 0;

        foreach (Match m in PercentPattern().Matches(text))
        {
            checkedCount++;
            double value = Parse(m.Groups["n"].Value);
            if (!allowed.Percentages.Any(a => Math.Abs(a - value) <= PercentTolerance))
                issues.Add($"\"{m.Value.Trim()}\" does not match any probability in the simulation.");
        }
        text = PercentPattern().Replace(text, " ");

        foreach (Match m in MetresPattern().Matches(text))
        {
            checkedCount++;
            double value = Parse(m.Groups["n"].Value);
            if (!allowed.Metres.Any(a => Math.Abs(a - value) <= MetresTolerance))
                issues.Add($"\"{m.Value.Trim()}\" does not match any distance in the simulation.");
        }
        text = MetresPattern().Replace(text, " ");

        foreach (Match m in NumberPattern().Matches(text))
        {
            checkedCount++;
            double value = Parse(m.Value);
            bool ok = m.Value.Contains('.')
                ? allowed.Decimals.Any(a => Math.Abs(a - value) <= DecimalTolerance)
                : allowed.Integers.Contains((int)value);
            if (!ok) issues.Add($"The number \"{m.Value}\" does not appear in the simulation result.");
        }

        if (requireEventId && !citesEvent)
            issues.Add($"Cite the source event ID {result.EventId}.");

        return new FactCheckReport(issues.Count == 0, issues, checkedCount);
    }

    /// <summary>Checks that a translation kept exactly the same numbers as the original answer.</summary>
    public static FactCheckReport CheckTranslation(string original, string translated, WhatIfResult result)
    {
        var report = Check(translated, result, requireEventId: true);
        var originalNumbers = ExtractNumbers(original);
        var translatedNumbers = ExtractNumbers(translated);
        var missing = originalNumbers.Where(n => !translatedNumbers.Any(t => Math.Abs(t - n) < 0.05)).ToList();

        if (missing.Count == 0) return report;

        var issues = report.Issues.Concat(missing.Select(n =>
            $"The translation is missing the number {n.ToString(CultureInfo.InvariantCulture)} from the original.")).ToList();
        return new FactCheckReport(false, issues, report.NumbersChecked);
    }

    private static List<double> ExtractNumbers(string text)
    {
        string normalised = ClockPattern().Replace(EventIdPattern().Replace(DecimalComma().Replace(text, "$1.$2"), " "), " ");
        return NumberPattern().Matches(normalised).Select(m => Parse(m.Value)).ToList();
    }

    private static double Parse(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"(\d),(\d)")]
    private static partial Regex DecimalComma();

    [GeneratedRegex(@"evt_\d+", RegexOptions.IgnoreCase)]
    private static partial Regex EventIdPattern();

    [GeneratedRegex(@"\b\d{1,3}:\d{2}\b")]
    private static partial Regex ClockPattern();

    [GeneratedRegex(@"(?<n>\d+(?:\.\d+)?)\s*(?:%|percent|per cent|pp|percentage points?)", RegexOptions.IgnoreCase)]
    private static partial Regex PercentPattern();

    [GeneratedRegex(@"(?<n>\d+(?:\.\d+)?)\s*(?:m\b|metres?\b|meters?\b)", RegexOptions.IgnoreCase)]
    private static partial Regex MetresPattern();

    [GeneratedRegex(@"\d+(?:\.\d+)?")]
    private static partial Regex NumberPattern();

    /// <summary>Every value an answer is allowed to mention, derived from the simulation result.</summary>
    private sealed record AllowedValues(List<double> Percentages, List<double> Metres, List<double> Decimals, HashSet<int> Integers)
    {
        public static AllowedValues From(WhatIfResult r)
        {
            var pct = new List<double>();
            var metres = new List<double>();
            var decimals = new List<double>();
            var ints = new HashSet<int> { r.SimulationRuns, 0, 1, 2, 3, 4, 5, 10, 90, 100 };

            void Prob(double? p)
            {
                if (p is not double v) return;
                pct.Add(v * 100);
                decimals.Add(v);
            }

            foreach (var o in r.Options.Append(r.Actual))
            {
                Prob(o.MeanValue);
                Prob(o.P10);
                Prob(o.P90);
                Prob(o.Factors.PressureOnBall);
                Prob(o.Factors.PassSuccessProbability);
                Prob(o.Factors.ReceiverShotXg);
                Prob(o.Factors.KeepBallProbability);

                foreach (var d in new[] { o.Factors.DistanceToGoalMetres, o.Factors.PassDistanceMetres, o.Factors.ReceiverSpaceMetres })
                    if (d is double m) { metres.Add(m); decimals.Add(m); ints.Add((int)Math.Round(m)); }

                foreach (var c in new[] { o.Factors.DefendersInShotCone, o.Factors.DefendersInPassingLane, o.Factors.DefendersInCarryPath })
                    if (c is int n) ints.Add(n);
            }

            // Differences between options, in percentage points, and how many times better.
            if (r.BestAlternative is { } best)
            {
                pct.Add(Math.Abs(r.DifferenceVsActual) * 100);
                if (r.Actual.MeanValue > 0)
                {
                    double ratio = best.MeanValue / r.Actual.MeanValue;
                    decimals.Add(Math.Round(ratio, 1));
                    ints.Add((int)Math.Round(ratio));
                }
            }

            // Minute of the moment, e.g. "63:12" -> 63 and 64 ("in the 64th minute").
            if (int.TryParse(r.MatchClock.Split(':')[0], out int minute))
            {
                ints.Add(minute);
                ints.Add(minute + 1);
            }

            return new AllowedValues(pct, metres, decimals, ints);
        }
    }
}
