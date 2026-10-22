using System.Globalization;
using SecondGuess.Domain;

namespace SecondGuess.Simulation.Live;

/// <summary>
/// Turns a stream of match events into live football intelligence: running stats, a pass difficulty rating,
/// player speed tags, a Control vs Chaos index, momentum, and timed overlay cards that explain why moments matter.
/// Feed events in order with <see cref="Process"/>; it is deterministic, so the same match always yields the same cards.
/// </summary>
public sealed class LiveStatsEngine
{
    // Thresholds and windows. All are named so the reasoning behind each card is easy to audit.
    public const double SprintThresholdKmh = 24;
    public const int DifficultPassThreshold = 60;      // pass difficulty (0-100) worth a card when completed
    public const int ElitePassThreshold = 75;
    public const double SecondGuessMinimumGain = 0.05;  // a better option must add 5+ points of goal chance
    private const long ChaosWindowMs = 5 * 60_000;
    private const long MomentumWindowMs = 10 * 60_000;
    private const long RhythmSampleMs = 30_000;
    private const double ChaosHigh = 68, ChaosLow = 45;

    private static readonly HashSet<string> TurnoverReasons = ["tackle", EventTypes.Interception, EventTypes.BallRecovery, "throw_in"];

    private readonly LoadedMatch _match;
    private readonly WhatIfEngine? _whatIf;
    private readonly string[] _teamIds;
    private readonly Dictionary<string, TeamCounter> _teams;
    private readonly Dictionary<string, PlayerCounter> _players;
    private readonly List<OverlayCard> _cards = [];
    private readonly List<RhythmPoint> _rhythm = [];
    private readonly Queue<(long Ts, string Kind, string? Team)> _window = new();
    private readonly Queue<(long Ts, string Team, double Xg)> _shotWindow = new();
    private readonly Dictionary<string, long> _cooldowns = [];

    private string? _possessionTeam;
    private long _possessionStartMs;
    private int _period = 1;
    private string _clock = "00:00";
    private long _nowMs;
    private long _periodStartMs;
    private int _homeScore, _awayScore;
    private string? _chaosLabel;
    private long _lastRhythmMs = -RhythmSampleMs;
    private (string PasserId, string ReceiverId, int PossessionId, string EventId)? _lastCompletedPass;
    private string? _lastActionEventId;
    private bool _finished;
    private double _fastestCarryKmh;
    private int _cardSequence;

    public LiveStatsEngine(LoadedMatch match, WhatIfEngine? whatIf = null)
    {
        _match = match;
        _whatIf = whatIf;
        _teamIds = [match.Info.Home.Id, match.Info.Away.Id];
        _teams = new()
        {
            [match.Info.Home.Id] = new TeamCounter(match.Info.Home.Id, match.Info.Home.Name),
            [match.Info.Away.Id] = new TeamCounter(match.Info.Away.Id, match.Info.Away.Name)
        };
        _players = match.Players.Values.ToDictionary(
            p => p.Id,
            p => new PlayerCounter(p.Id, p.Name, p.Id.StartsWith(match.Info.Home.Id[0]) ? match.Info.Home.Id : match.Info.Away.Id, p.Position));
    }

    /// <summary>All cards produced so far, oldest first.</summary>
    public IReadOnlyList<OverlayCard> Cards => _cards;

    /// <summary>The Control vs Chaos and momentum timeline, sampled every 30 seconds of match time.</summary>
    public IReadOnlyList<RhythmPoint> Rhythm => _rhythm;

    /// <summary>Runs a whole match through the engine and returns every overlay card, for the overlay API.</summary>
    public static IReadOnlyList<OverlayCard> BuildAllCards(LoadedMatch match, WhatIfEngine? whatIf = null)
    {
        var engine = new LiveStatsEngine(match, whatIf);
        foreach (var e in match.Events) engine.Process(e);
        return engine.Cards;
    }

    /// <summary>Processes one event and returns any overlay cards it triggered.</summary>
    public IReadOnlyList<OverlayCard> Process(MatchEvent e)
    {
        int before = _cards.Count;
        _nowMs = e.TimestampMs;
        _period = e.Period;
        _clock = e.MatchClock;

        switch (e.Type)
        {
            case EventTypes.PeriodStart:
                _periodStartMs = e.TimestampMs;
                _window.Clear();
                break;
            case EventTypes.PeriodEnd:
                ClosePossession(e.TimestampMs);
                _possessionTeam = null;
                AddPeriodCard(e);
                if (e.Period == 2) _finished = true;
                break;
            case EventTypes.PossessionChange:
                OnPossessionChange(e);
                break;
            case EventTypes.Pass:
                OnPass(e);
                break;
            case EventTypes.Carry:
                OnCarry(e);
                break;
            case EventTypes.Shot:
                OnShot(e);
                break;
            case EventTypes.Pressure:
                Team(e).Pressures++;
                _window.Enqueue((e.TimestampMs, "pressure", e.TeamId));
                break;
            case EventTypes.Tackle:
                Team(e).Tackles++;
                if (Player(e) is { } t) t.TacklesWon++;
                break;
            case EventTypes.Interception:
                Team(e).Interceptions++;
                break;
            case EventTypes.BallRecovery:
                Team(e).Recoveries++;
                break;
        }

        _homeScore = e.HomeScore;
        _awayScore = e.AwayScore;

        if (e.Type is not (EventTypes.PeriodStart or EventTypes.PeriodEnd))
        {
            TrimWindows();
            UpdateRhythm();
        }

        if (_cards.Count == before) return Array.Empty<OverlayCard>();
        return _cards.GetRange(before, _cards.Count - before);
    }

    /// <summary>The current state of the match for the live screen.</summary>
    public LiveSnapshot Snapshot()
    {
        long possessionHome = _teams[_teamIds[0]].PossessionMs, possessionAway = _teams[_teamIds[1]].PossessionMs;
        if (_possessionTeam is not null)
        {
            long open = Math.Max(0, _nowMs - _possessionStartMs);
            if (_possessionTeam == _teamIds[0]) possessionHome += open; else possessionAway += open;
        }
        long totalPossession = Math.Max(1, possessionHome + possessionAway);

        var (chaos, _) = ComputeChaos();
        var (homeXg10, awayXg10) = RecentXg();

        return new LiveSnapshot(
            MatchId: _match.Info.MatchId,
            Period: _period,
            MatchClock: _clock,
            TimestampMs: _nowMs,
            Home: _teams[_teamIds[0]].ToStats(_homeScore, Math.Round(100.0 * possessionHome / totalPossession, 1)),
            Away: _teams[_teamIds[1]].ToStats(_awayScore, Math.Round(100.0 * possessionAway / totalPossession, 1)),
            Chaos: Math.Round(chaos),
            ChaosLabel: LabelFor(chaos),
            ChaosExplanation: ExplainChaos(),
            HomeXgLast10: Math.Round(homeXg10, 2),
            AwayXgLast10: Math.Round(awayXg10, 2),
            Players: _players.Values.Select(p => p.ToStats()).ToList(),
            LastActionEventId: _lastActionEventId,
            Finished: _finished);
    }

    /// <summary>
    /// Pass difficulty from 0 (routine) to 100 (very hard): the inverse of the pass success model, using the pass
    /// distance, the pressure on the passer and the defenders in the passing lane at that moment.
    /// </summary>
    public static int PassDifficulty(MatchEvent pass)
    {
        if (pass.X is null || pass.Y is null || pass.EndX is null || pass.EndY is null) return 0;
        var from = new Point(pass.X.Value, pass.Y.Value);
        var to = new Point(pass.EndX.Value, pass.EndY.Value);
        int inLane = pass.FreezeFrame?
            .Where(p => p.TeamId != pass.TeamId)
            .Select(p => new Point(p.X, p.Y))
            .Count(d => Geometry.Distance(d, from) > 3 && Geometry.DistanceToSegment(d, from, to) < 1.5) ?? 0;
        double success = FootballModels.PassSuccess(Geometry.Distance(from, to), inLane, pass.Pressure ?? 0);
        return (int)Math.Round(100 * (1 - success));
    }

    // ------------------------------------------------------------------ event handlers

    private void OnPossessionChange(MatchEvent e)
    {
        ClosePossession(e.TimestampMs);
        _possessionTeam = e.TeamId;
        _possessionStartMs = e.TimestampMs;
        _lastCompletedPass = null;
        if (e.Outcome is { } reason && TurnoverReasons.Contains(reason))
            _window.Enqueue((e.TimestampMs, "turnover", e.TeamId));
    }

    private void OnPass(MatchEvent e)
    {
        _lastActionEventId = e.EventId;
        var team = Team(e);
        var passer = Player(e);
        bool complete = e.Outcome == "complete";
        int difficulty = PassDifficulty(e);

        team.Passes++;
        if (complete) team.CompletedPasses++;
        _window.Enqueue((e.TimestampMs, complete ? "pass_ok" : "pass_fail", e.TeamId));

        if (passer is not null)
        {
            passer.Touches++;
            passer.Passes++;
            if (complete) passer.CompletedPasses++;
            if (complete && difficulty >= DifficultPassThreshold) passer.DifficultPasses++;

            if (passer.Passes == 50)
                AddCard(CardTypes.Milestone, e, passer.Name, $"{passer.Name}: 50 passes",
                        $"{Pct(passer.PassAccuracy)} of them found a teammate.", passer.Passes, "passes", 6, 2);
        }

        if (complete)
        {
            _lastCompletedPass = (e.PlayerId!, e.ReceiverId!, e.PossessionId, e.EventId);

            // Only hard, completed passes are worth a graphic; elite ones always, good ones at most every 3 minutes.
            if (difficulty >= ElitePassThreshold || (difficulty >= DifficultPassThreshold && Ready("pass", 180_000)))
            {
                double distance = Distance(e);
                string receiver = _match.PlayerName(e.ReceiverId);
                AddCard(CardTypes.PassQuality, e, passer?.Name,
                        difficulty >= ElitePassThreshold ? $"Elite pass from {passer?.Name}" : $"Quality pass from {passer?.Name}",
                        $"Found {receiver} over {Num(distance)} m{PressureText(e.Pressure)}. Difficulty {difficulty}/100, ball speed {Num(e.BallSpeedKmh ?? 0)} km/h.",
                        difficulty, "difficulty", 6, difficulty >= ElitePassThreshold ? 3 : 2);
            }
        }
    }

    private void OnCarry(MatchEvent e)
    {
        _lastActionEventId = e.EventId;
        var carrier = Player(e);
        if (carrier is null) return;
        carrier.Touches++;

        double speed = e.PlayerSpeedKmh ?? 0;
        if (speed > carrier.TopSpeedKmh) carrier.TopSpeedKmh = speed;

        // Player tag: only a new fastest carry of the whole match, over the sprint threshold, at most every 3 minutes.
        bool matchRecord = speed > _fastestCarryKmh;
        if (matchRecord) _fastestCarryKmh = speed;
        if (speed >= SprintThresholdKmh && matchRecord && Ready("sprint", 180_000))
        {
            AddCard(CardTypes.Sprint, e, carrier.Name, $"{carrier.Name} at full speed",
                    $"{Num(speed)} km/h carrying the ball, the fastest run of the match so far.", speed, "km/h", 5, 1);
        }
    }

    private void OnShot(MatchEvent e)
    {
        _lastActionEventId = e.EventId;
        var team = Team(e);
        var shooter = Player(e);
        double xg = e.Xg ?? 0;

        team.Shots++;
        team.Xg += xg;
        if (e.Outcome is "goal" or "saved") team.ShotsOnTarget++;
        _shotWindow.Enqueue((e.TimestampMs, e.TeamId!, xg));

        if (shooter is not null)
        {
            shooter.Touches++;
            shooter.Shots++;
            shooter.Xg += xg;
        }

        // Key pass: the completed pass in the same possession that set up this shot.
        if (_lastCompletedPass is { } kp && kp.ReceiverId == e.PlayerId && kp.PossessionId == e.PossessionId &&
            _players.TryGetValue(kp.PasserId, out var creator))
        {
            creator.KeyPasses++;
            if (creator.KeyPasses is 2 or 3 or 5)
                AddCard(CardTypes.Milestone, e, creator.Name, $"{creator.Name}: {creator.KeyPasses} chances created",
                        $"His pass set up {shooter?.Name ?? "the shot"}.", creator.KeyPasses, "key passes", 6, 2, [kp.EventId, e.EventId]);
        }

        double distance = e.X is double x && e.Y is double y ? Geometry.Distance(new Point(x, y), Pitch.GoalCentre) : 0;
        string speed = $"{Num(e.BallSpeedKmh ?? 0)} km/h";
        string score = $"{_match.Info.Home.ShortName} {e.HomeScore}-{e.AwayScore} {_match.Info.Away.ShortName}";

        if (e.Outcome == "goal")
        {
            AddCard(CardTypes.Goal, e, shooter?.Name, $"Goal! {shooter?.Name}",
                    $"From {Num(distance)} m at {speed}. It was a {Pct(xg)} chance. {score}.", xg, "xG", 10, 5);
        }
        else
        {
            string what = e.Outcome switch { "saved" => "saved", "blocked" => "blocked", "off_target" => "wide", _ => "missed" };
            AddCard(CardTypes.Shot, e, shooter?.Name, $"{shooter?.Name}'s shot is {what}",
                    $"From {Num(distance)} m at {speed}, a {Pct(xg)} chance.", xg, "xG", 6, 3);
        }

        // Second Guess: was another option clearly better? Simulated live from the freeze frame.
        if (_whatIf is not null && e.FreezeFrame is not null)
        {
            var result = _whatIf.Simulate(_match, e.EventId, runs: 500);
            if (result.BestAlternative is { } best && best.MeanValue - result.Actual.MeanValue >= SecondGuessMinimumGain)
            {
                string option = best.Kind == "pass" ? $"a pass to {Surname(best.TargetPlayerName)}" : "carrying it closer";
                AddCard(CardTypes.SecondGuess, e, shooter?.Name, $"Second Guess: {option} was on",
                        $"That option was worth {Pct(best.MeanValue)}, against {Pct(result.Actual.MeanValue)} for the shot.",
                        best.MeanValue, "goal chance", 8, 4);
            }
        }
    }

    // ------------------------------------------------------------------ rhythm: control vs chaos, momentum

    /// <summary>
    /// Control vs Chaos (0-100) over the last five minutes: turnovers per minute (50%), pressures per minute (30%)
    /// and the share of passes that fail (20%). Low = one side is controlling the ball; high = the ball keeps changing hands.
    /// </summary>
    private (double Chaos, (int Turnovers, int Pressures, int Passes, int Completed) Counts) ComputeChaos()
    {
        int turnovers = 0, pressures = 0, passes = 0, completed = 0;
        foreach (var (_, kind, _) in _window)
        {
            switch (kind)
            {
                case "turnover": turnovers++; break;
                case "pressure": pressures++; break;
                case "pass_ok": passes++; completed++; break;
                case "pass_fail": passes++; break;
            }
        }

        double minutes = Math.Clamp((_nowMs - _periodStartMs) / 60_000.0, 1, ChaosWindowMs / 60_000.0);
        double failRate = passes == 0 ? 0 : 1 - (double)completed / passes;
        double chaos = 100 * (0.5 * Math.Min(turnovers / minutes / 4.0, 1)
                            + 0.3 * Math.Min(pressures / minutes / 14.0, 1)
                            + 0.2 * Math.Min(failRate / 0.5, 1));
        return (chaos, (turnovers, pressures, passes, completed));
    }

    private string ExplainChaos()
    {
        var (_, c) = ComputeChaos();
        return $"{c.Turnovers} turnovers, {c.Pressures} pressures and {(c.Passes == 0 ? "no" : Pct((double)c.Completed / c.Passes))} pass completion in the last 5 minutes.";
    }

    private void UpdateRhythm()
    {
        var (chaos, counts) = ComputeChaos();
        var (homeXg, awayXg) = RecentXg();

        // The first minutes of a half have too few events for a fair reading, so the timeline starts after 3 minutes.
        if (_nowMs - _lastRhythmMs >= RhythmSampleMs && _nowMs - _periodStartMs >= 3 * 60_000)
        {
            _lastRhythmMs = _nowMs;
            _rhythm.Add(new RhythmPoint(_period, _clock, _nowMs, Math.Round(chaos, 1), Math.Round(homeXg, 2), Math.Round(awayXg, 2)));
        }

        // Need a few minutes of the half before judging rhythm.
        if (_nowMs - _periodStartMs < 4 * 60_000) return;

        string label = LabelFor(chaos);
        if (_chaosLabel is not null && label != _chaosLabel && label != "Balanced" && Ready("chaos", 240_000))
        {
            MatchEvent? lastEvent = _match.EventsById.GetValueOrDefault(_lastActionEventId ?? "");
            string controller = WindowPassLeader();
            if (label == "Chaos")
            {
                AddCard(CardTypes.ControlChaos, lastEvent, null, "The game has tipped into chaos",
                        $"{counts.Turnovers} turnovers and {counts.Pressures} pressures in the last 5 minutes. Neither side can keep the ball.",
                        Math.Round(chaos), "chaos", 8, 3);
            }
            else
            {
                AddCard(CardTypes.ControlChaos, lastEvent, null, $"{controller} have taken control",
                        $"Only {counts.Turnovers} turnovers in the last 5 minutes and {(counts.Passes == 0 ? "steady" : Pct((double)counts.Completed / counts.Passes))} of passes completed.",
                        Math.Round(chaos), "chaos", 8, 3);
            }
        }
        _chaosLabel = label;

        // Momentum: one side's chances in the last 10 minutes clearly outweigh the other's.
        double lead = Math.Max(homeXg, awayXg), trail = Math.Min(homeXg, awayXg);
        if (lead >= 0.5 && lead >= 3 * Math.Max(trail, 0.05) && Ready("momentum", 600_000))
        {
            bool homeOnTop = homeXg > awayXg;
            string onTop = homeOnTop ? _match.Info.Home.Name : _match.Info.Away.Name;
            string other = homeOnTop ? _match.Info.Away.Name : _match.Info.Home.Name;
            AddCard(CardTypes.Momentum, _match.EventsById.GetValueOrDefault(_lastActionEventId ?? ""), null, $"{onTop} are on top",
                    $"{Num2(lead)} xG in the last 10 minutes, to {Num2(trail)} for {other}.", Math.Round(lead, 2), "xG", 8, 3);
        }
    }

    private (double Home, double Away) RecentXg()
    {
        double home = 0, away = 0;
        foreach (var (_, team, xg) in _shotWindow)
        {
            if (team == _teamIds[0]) home += xg; else away += xg;
        }
        return (home, away);
    }

    private string WindowPassLeader()
    {
        int home = _window.Count(w => w.Kind == "pass_ok" && w.Team == _teamIds[0]);
        int away = _window.Count(w => w.Kind == "pass_ok" && w.Team == _teamIds[1]);
        return home >= away ? _match.Info.Home.Name : _match.Info.Away.Name;
    }

    private static string LabelFor(double chaos) => chaos >= ChaosHigh ? "Chaos" : chaos <= ChaosLow ? "Control" : "Balanced";

    private void TrimWindows()
    {
        while (_window.Count > 0 && _nowMs - _window.Peek().Ts > ChaosWindowMs) _window.Dequeue();
        while (_shotWindow.Count > 0 && _nowMs - _shotWindow.Peek().Ts > MomentumWindowMs) _shotWindow.Dequeue();
    }

    // ------------------------------------------------------------------ helpers

    private void ClosePossession(long ts)
    {
        if (_possessionTeam is not null && _teams.TryGetValue(_possessionTeam, out var t))
            t.PossessionMs += Math.Max(0, ts - _possessionStartMs);
    }

    private void AddPeriodCard(MatchEvent e)
    {
        var home = _teams[_teamIds[0]];
        var away = _teams[_teamIds[1]];
        string when = e.Period == 1 ? "Half-time" : "Full-time";
        var snap = Snapshot();
        AddCard(CardTypes.Period, e, null,
                $"{when}: {_match.Info.Home.Name} {e.HomeScore}-{e.AwayScore} {_match.Info.Away.Name}",
                $"xG {Num2(home.Xg)} to {Num2(away.Xg)}. Shots {home.Shots} to {away.Shots}. Possession {Num(snap.Home.PossessionPct)}% to {Num(snap.Away.PossessionPct)}%.",
                null, null, 12, 5);
    }

    private void AddCard(string type, MatchEvent? e, string? playerName, string headline, string detail,
                         double? value, string? unit, int displaySeconds, int priority, IReadOnlyList<string>? sources = null)
    {
        _cards.Add(new OverlayCard(
            Id: $"card_{++_cardSequence:0000}",
            MatchId: _match.Info.MatchId,
            Period: e?.Period ?? _period,
            MatchClock: e?.MatchClock ?? _clock,
            TimestampMs: e?.TimestampMs ?? _nowMs,
            Type: type,
            TeamId: e?.TeamId,
            PlayerId: playerName is null ? null : e?.PlayerId,
            PlayerName: playerName,
            Headline: headline,
            Detail: detail,
            Value: value is double v ? Math.Round(v, 3) : null,
            Unit: unit,
            DisplaySeconds: displaySeconds,
            Priority: priority,
            SourceEventIds: sources ?? (e is null ? Array.Empty<string>() : new[] { e.EventId })));
    }

    /// <summary>True (and starts the cooldown) if the named card type hasn't fired within the cooldown window.</summary>
    private bool Ready(string key, long cooldownMs)
    {
        if (_cooldowns.TryGetValue(key, out long last) && _nowMs - last < cooldownMs) return false;
        _cooldowns[key] = _nowMs;
        return true;
    }

    private TeamCounter Team(MatchEvent e) => _teams[e.TeamId ?? _teamIds[0]];

    private PlayerCounter? Player(MatchEvent e) => e.PlayerId is not null && _players.TryGetValue(e.PlayerId, out var p) ? p : null;

    private static double Distance(MatchEvent e) =>
        e.X is double x && e.Y is double y && e.EndX is double ex && e.EndY is double ey
            ? Geometry.Distance(new Point(x, y), new Point(ex, ey)) : 0;

    private static string PressureText(double? pressure) => pressure switch
    {
        >= 0.7 => " under heavy pressure",
        >= 0.4 => " under pressure",
        _ => ""
    };

    private static string Surname(string? name) => name is null ? "a teammate" : name[(name.LastIndexOf(' ') + 1)..];

    private static string Pct(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    private static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    private static string Num2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    private sealed class TeamCounter(string id, string name)
    {
        public int Passes, CompletedPasses, Shots, ShotsOnTarget, Tackles, Interceptions, Recoveries, Pressures;
        public double Xg;
        public long PossessionMs;

        public TeamLiveStats ToStats(int goals, double possessionPct) => new(
            id, name, goals, Passes, CompletedPasses, Shots, ShotsOnTarget, Math.Round(Xg, 2), possessionPct,
            Tackles, Interceptions, Recoveries, Pressures);
    }

    private sealed class PlayerCounter(string id, string name, string teamId, string position)
    {
        public string Id => id;
        public string Name => name;
        public int Touches, Passes, CompletedPasses, KeyPasses, DifficultPasses, Shots, TacklesWon;
        public double Xg, TopSpeedKmh;

        public double PassAccuracy => Passes == 0 ? 0 : (double)CompletedPasses / Passes;

        public PlayerLiveStats ToStats() => new(
            id, name, teamId, position, Touches, Passes, CompletedPasses, KeyPasses, DifficultPasses, Shots,
            Math.Round(Xg, 2), TacklesWon, Math.Round(TopSpeedKmh, 1));
    }
}
