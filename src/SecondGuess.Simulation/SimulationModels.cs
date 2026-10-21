namespace SecondGuess.Simulation;

/// <summary>A short description of a match, for listing.</summary>
public sealed record MatchListItem(string MatchId, string Home, string Away, string FinalScore, int EventCount);

/// <summary>A moment that can be asked about.</summary>
public sealed record MomentSummary(
    string EventId,
    int Period,
    string MatchClock,
    string Type,
    string TeamName,
    string PlayerId,
    string PlayerName,
    string? ReceiverName,
    string? Outcome,
    double? Xg,
    string ScoreBefore);

/// <summary>A player's position at a moment, in the acting team's perspective (attacking towards x = 105).</summary>
public sealed record PlayerSnapshot(
    string PlayerId,
    string Name,
    string Position,
    string TeamName,
    bool IsTeammate,
    double X,
    double Y,
    double DistanceToBallMetres);

/// <summary>Everything an agent needs to describe a moment.</summary>
/// <remarks><see cref="EndX"/>/<see cref="EndY"/> are where the action actually ended (for a shot: where it was saved, blocked, went wide or scored).</remarks>
public sealed record GameState(
    MomentSummary Moment,
    double BallX,
    double BallY,
    IReadOnlyList<PlayerSnapshot> Players,
    double? EndX = null,
    double? EndY = null);

/// <summary>The football reasons behind an option's value, so answers can explain "why".</summary>
public sealed record OptionFactors(
    double? DistanceToGoalMetres = null,
    int? DefendersInShotCone = null,
    double? PressureOnBall = null,
    double? PassDistanceMetres = null,
    int? DefendersInPassingLane = null,
    double? PassSuccessProbability = null,
    double? ReceiverSpaceMetres = null,
    double? ReceiverShotXg = null,
    int? DefendersInCarryPath = null,
    double? KeepBallProbability = null);

/// <summary>
/// One possible decision. <see cref="MeanValue"/> is the estimated probability (0–1) that the
/// decision leads to a goal from the next shot. P10/P90 give the range across simulations.
/// </summary>
public sealed record OptionResult(
    string Key,
    string Kind,
    string Description,
    string? TargetPlayerName,
    bool IsActual,
    double MeanValue,
    double P10,
    double P90,
    OptionFactors Factors);

/// <summary>The result of a what-if simulation for one moment.</summary>
public sealed record WhatIfResult(
    string MatchId,
    string EventId,
    string MatchClock,
    string PlayerName,
    string TeamName,
    string ActualAction,
    string? ActualOutcome,
    OptionResult Actual,
    OptionResult? BestAlternative,
    double DifferenceVsActual,
    IReadOnlyList<OptionResult> Options,
    int SimulationRuns,
    string ModelVersion,
    IReadOnlyList<string> SourceEventIds,
    string Note);

/// <summary>A moment ranked by how much better the best alternative would have been.</summary>
public sealed record DecisionRanking(
    string EventId,
    string MatchClock,
    string PlayerName,
    string TeamName,
    string ActualAction,
    string? ActualOutcome,
    double ActualValue,
    string BestAlternative,
    double BestAlternativeValue,
    double Difference);

/// <summary>One saved "alternative decision → outcome" record for the counterfactual dataset.</summary>
public sealed record CounterfactualRecord(
    string MatchId,
    string EventId,
    string ActualKey,
    double ActualValue,
    string AlternativeKey,
    double AlternativeValue,
    double AlternativeP10,
    double AlternativeP90,
    int SimulationRuns,
    string ModelVersion,
    DateTimeOffset CreatedAt);
