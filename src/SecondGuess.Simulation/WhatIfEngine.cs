using SecondGuess.Domain;

namespace SecondGuess.Simulation;

/// <summary>
/// Simulates the actual decision at a moment and its realistic alternatives (shoot, carry, or pass
/// to each teammate) using Monte Carlo runs over the freeze frame, and compares how likely each
/// option is to lead to a goal from the next shot.
/// </summary>
/// <remarks>
/// Results are deterministic: each option is seeded from the event ID and option key, so the same
/// question always returns the same numbers. That lets the fact-checker verify answers exactly.
/// </remarks>
public sealed class WhatIfEngine
{
    public const string ModelVersion = "whatif-v1";
    public const int DefaultRuns = 2000;

    private const double PositionNoiseMetres = 1.0;   // uncertainty in each player's position
    private const double CarryMetres = 6.0;           // how far a carry moves towards goal
    private const double MinPassMetres = 5;
    private const double MaxPassMetres = 50;
    private const double ReceiverPressurePenalty = 0.3;
    private const int MaxOptionsReturned = 6;
    private const double CarryPathWidthMetres = 2.5;   // defenders this close to the carry path can challenge
    private const double CarryEndChallengeMetres = 4;  // defenders this close to where the carry ends

    public WhatIfResult Simulate(LoadedMatch match, string eventId, int runs = DefaultRuns)
    {
        runs = Math.Clamp(runs, 100, 20000);
        MatchEvent e = match.GetEvent(eventId);

        if (e.FreezeFrame is null || e.X is null || e.Y is null || e.TeamId is null || e.PlayerId is null)
            throw new InvalidOperationException($"Event '{eventId}' has no player positions. Choose a pass, carry or shot.");

        var ball = new Point(e.X.Value, e.Y.Value);
        var teammates = e.FreezeFrame
            .Where(p => p.TeamId == e.TeamId && p.PlayerId != e.PlayerId && !IsGoalkeeper(match, p.PlayerId))
            .ToList();
        var opponents = e.FreezeFrame.Where(p => p.TeamId != e.TeamId).Select(p => new Point(p.X, p.Y)).ToArray();

        var options = new List<OptionResult>
        {
            SimulateShot(eventId, ball, opponents, runs),
            SimulateCarry(eventId, ball, opponents, runs)
        };

        foreach (var mate in teammates)
        {
            var target = new Point(mate.X, mate.Y);
            double distance = Geometry.Distance(ball, target);
            if (distance < MinPassMetres || distance > MaxPassMetres) continue;
            options.Add(SimulatePass(eventId, ball, mate.PlayerId, match.PlayerName(mate.PlayerId), target, opponents, runs));
        }

        string actualKey = e.Type switch
        {
            EventTypes.Shot => "shot",
            EventTypes.Carry => "carry",
            EventTypes.Pass => $"pass:{e.ReceiverId}",
            _ => "shot"
        };

        // Mark the decision that was actually taken.
        options = options.Select(o => o with { IsActual = o.Key == actualKey }).ToList();
        OptionResult actual = options.FirstOrDefault(o => o.IsActual)
            ?? options.First(o => o.Key == "shot") with { IsActual = true };

        var ranked = options.OrderByDescending(o => o.MeanValue).ToList();
        OptionResult? best = ranked.FirstOrDefault(o => !o.IsActual);

        var shown = ranked.Take(MaxOptionsReturned).ToList();
        if (!shown.Any(o => o.IsActual)) shown.Add(actual);

        return new WhatIfResult(
            MatchId: match.Info.MatchId,
            EventId: e.EventId,
            MatchClock: e.MatchClock,
            PlayerName: match.PlayerName(e.PlayerId),
            TeamName: match.TeamName(e.TeamId),
            ActualAction: e.Type,
            ActualOutcome: e.Outcome,
            Actual: actual,
            BestAlternative: best,
            DifferenceVsActual: best is null ? 0 : Math.Round(best.MeanValue - actual.MeanValue, 3),
            Options: shown,
            SimulationRuns: runs,
            ModelVersion: ModelVersion,
            SourceEventIds: [e.EventId],
            Note: "Values are simulated estimates of the chance that each decision leads to a goal from the next shot, not certainties.");
    }

    /// <summary>
    /// Simulates every moment of a type (shots by default) and ranks them by how much better the
    /// best alternative would have been. The top results are the decisions that "cost" the most,
    /// which make the strongest what-if questions.
    /// </summary>
    public IReadOnlyList<DecisionRanking> RankDecisions(
        LoadedMatch match, string eventType = EventTypes.Shot, int limit = 5, int runs = 500)
    {
        return match.Events
            .Where(e => e.Type == eventType && e.FreezeFrame is not null)
            .Select(e => Simulate(match, e.EventId, runs))
            .Where(r => r.BestAlternative is not null)
            .OrderByDescending(r => r.DifferenceVsActual)
            .Take(Math.Clamp(limit, 1, 20))
            .Select(r => new DecisionRanking(
                EventId: r.EventId,
                MatchClock: r.MatchClock,
                PlayerName: r.PlayerName,
                TeamName: r.TeamName,
                ActualAction: r.ActualAction,
                ActualOutcome: r.ActualOutcome,
                ActualValue: r.Actual.MeanValue,
                BestAlternative: r.BestAlternative!.Description,
                BestAlternativeValue: r.BestAlternative.MeanValue,
                Difference: r.DifferenceVsActual))
            .ToList();
    }

    // ------------------------------------------------------------------ options

    private static OptionResult SimulateShot(string eventId, Point ball, Point[] opponents, int runs)
    {
        var rng = new Random(StableHash.Of(eventId + "|shot"));
        var values = new double[runs];
        for (int i = 0; i < runs; i++)
        {
            var defenders = Jitter(opponents, rng);
            values[i] = FootballModels.ExpectedGoals(ball, defenders.Count(d => Geometry.InShotCone(d, ball)));
        }

        return Build("shot", "shot", "Shoot", null, values, new OptionFactors(
            DistanceToGoalMetres: Round1(Geometry.Distance(ball, Pitch.GoalCentre)),
            DefendersInShotCone: opponents.Count(d => Geometry.InShotCone(d, ball)),
            PressureOnBall: Round2(Pressure(ball, opponents))));
    }

    private static OptionResult SimulateCarry(string eventId, Point ball, Point[] opponents, int runs)
    {
        double toGoal = Geometry.Distance(ball, Pitch.GoalCentre);
        double step = Math.Min(CarryMetres, Math.Max(toGoal - 6, 0));
        var end = toGoal == 0
            ? ball
            : Pitch.Clamp(ball.X + (Pitch.GoalCentre.X - ball.X) / toGoal * step,
                          ball.Y + (Pitch.GoalCentre.Y - ball.Y) / toGoal * step);

        var rng = new Random(StableHash.Of(eventId + "|carry"));
        var values = new double[runs];
        double keepSum = 0;
        for (int i = 0; i < runs; i++)
        {
            var defenders = Jitter(opponents, rng);
            int inPath = defenders.Count(d => Geometry.DistanceToSegment(d, ball, end) < CarryPathWidthMetres);
            int nearEnd = defenders.Count(d => Geometry.Distance(d, end) < CarryEndChallengeMetres);

            // Harder to keep the ball under pressure, through defenders, or into a crowded area.
            double keep = Math.Clamp(0.9 - 0.35 * Pressure(ball, defenders) - 0.2 * inPath - 0.15 * nearEnd, 0.05, 1);
            keepSum += keep;

            double shotAfterCarry = FootballModels.ExpectedGoals(end, defenders.Count(d => Geometry.InShotCone(d, end)))
                                    * (1 - ReceiverPressurePenalty * Pressure(end, defenders));
            values[i] = keep * shotAfterCarry;
        }

        return Build("carry", "carry", "Carry the ball towards goal, then shoot", null, values, new OptionFactors(
            DistanceToGoalMetres: Round1(Geometry.Distance(end, Pitch.GoalCentre)),
            PressureOnBall: Round2(Pressure(ball, opponents)),
            DefendersInCarryPath: opponents.Count(d => Geometry.DistanceToSegment(d, ball, end) < CarryPathWidthMetres),
            KeepBallProbability: Round2(keepSum / runs)));
    }

    private static OptionResult SimulatePass(
        string eventId, Point ball, string receiverId, string receiverName, Point receiver, Point[] opponents, int runs)
    {
        var rng = new Random(StableHash.Of(eventId + "|pass:" + receiverId));
        var values = new double[runs];
        double successSum = 0, receiverXgSum = 0;

        for (int i = 0; i < runs; i++)
        {
            var defenders = Jitter(opponents, rng);
            var target = new Point(
                receiver.X + rng.NextGaussian(0, PositionNoiseMetres),
                receiver.Y + rng.NextGaussian(0, PositionNoiseMetres));

            double success = FootballModels.PassSuccess(
                Geometry.Distance(ball, target),
                DefendersInLane(ball, target, defenders),
                Pressure(ball, defenders));

            double receiverXg = FootballModels.ExpectedGoals(target, defenders.Count(d => Geometry.InShotCone(d, target)))
                                * (1 - ReceiverPressurePenalty * Pressure(target, defenders));

            successSum += success;
            receiverXgSum += receiverXg;
            values[i] = success * receiverXg;
        }

        return Build($"pass:{receiverId}", "pass", $"Pass to {receiverName}", receiverName, values, new OptionFactors(
            PassDistanceMetres: Round1(Geometry.Distance(ball, receiver)),
            DefendersInPassingLane: DefendersInLane(ball, receiver, opponents),
            PassSuccessProbability: Round2(successSum / runs),
            ReceiverSpaceMetres: Round1(opponents.Min(d => Geometry.Distance(d, receiver))),
            ReceiverShotXg: Round2(receiverXgSum / runs),
            PressureOnBall: Round2(Pressure(ball, opponents))));
    }

    // ------------------------------------------------------------------ helpers

    private static OptionResult Build(string key, string kind, string description, string? target, double[] values, OptionFactors factors)
    {
        Array.Sort(values);
        return new OptionResult(
            Key: key,
            Kind: kind,
            Description: description,
            TargetPlayerName: target,
            IsActual: false,
            MeanValue: Math.Round(values.Average(), 3),
            P10: Math.Round(values[values.Length / 10], 3),
            P90: Math.Round(values[values.Length * 9 / 10], 3),
            Factors: factors);
    }

    private static Point[] Jitter(Point[] players, Random rng) =>
        players.Select(p => new Point(
            p.X + rng.NextGaussian(0, PositionNoiseMetres),
            p.Y + rng.NextGaussian(0, PositionNoiseMetres))).ToArray();

    private static double Pressure(Point at, IEnumerable<Point> opponents) =>
        FootballModels.PressureFromDistance(opponents.Min(d => Geometry.Distance(d, at)));

    private static int DefendersInLane(Point from, Point to, IEnumerable<Point> opponents) =>
        opponents.Count(d => Geometry.Distance(d, from) > 3 && Geometry.DistanceToSegment(d, from, to) < 1.5);

    private static bool IsGoalkeeper(LoadedMatch match, string playerId) =>
        match.Players.TryGetValue(playerId, out var p) && p.Position == "GK";

    private static double Round1(double v) => Math.Round(v, 1);
    private static double Round2(double v) => Math.Round(v, 2);
}
