using SecondGuess.Domain;

namespace SecondGuess.Generator;

public sealed record MatchResult(MatchInfo Info, IReadOnlyList<MatchEvent> Events);

/// <summary>
/// Simulates a full match, possession by possession, producing football-realistic events
/// with freeze frames (all 22 player positions) for every on-ball action.
/// The same seed always produces the same match.
/// </summary>
public sealed class MatchSimulator
{
    // Tuning constants. Chosen so generated matches fall inside realistic per-team ranges:
    // ~450-550 passes, ~75-80% pass accuracy, ~7-14 shots, ~0.6-1.6 xG.
    private const double ShotRangeMetres = 32;
    private const double ShotBaseProbability = 0.025;
    private const double ShotXgFactor = 1.2;
    private const double MaxShotProbability = 0.85;
    private const double CarryBaseProbability = 0.10;
    private const double CarryOpenSpaceBonus = 0.25;
    private const double DirectnessScale = 12;
    private const double PassDistanceDecay = 22;
    private const double MinPassMetres = 5;
    private const double MaxPassMetres = 50;
    private const double PressureEventThreshold = 0.6;
    private const double PositionJitterMetres = 2;
    private const double SaveShareOfUnblocked = 0.35;  // keeps roughly a third of shots on target
    private const double OutOfPlayShare = 0.3;         // failed passes that go out for a throw-in
    private const double CleanInterceptionShare = 0.35; // of the rest: clean interceptions vs loose-ball recoveries
    private const double FinalThirdX = 70;           // runs into the box start once the ball is past this line
    private const double RunProbability = 0.35;      // chance each forward makes a run into the box
    private const double RunCentralPull = 0.6;       // how strongly runners move towards the middle

    private static readonly HashSet<string> ForwardPositions = ["RW", "ST", "LW", "RAM", "LAM"];

    private readonly MatchInfo _info;
    private readonly Random _rng;
    private readonly Team[] _teams;
    private readonly List<MatchEvent> _events = [];
    private readonly int[] _score = new int[2];

    private int _attacking;          // 0 = home, 1 = away (team in possession)
    private int _carrier;            // index of ball carrier in the attacking team
    private Point _ball;             // ball position, attacking team's perspective
    private int _period;
    private double _clockSeconds;    // match clock (restarts at 45:00 in the second half)
    private double _elapsedSeconds;  // continuous time since kick-off, including stoppages
    private int _possessionId;
    private int _sequence;

    public MatchSimulator(MatchInfo info)
    {
        _info = info;
        _rng = new Random(info.Seed);
        _teams = [info.Home, info.Away];
    }

    public MatchResult Run()
    {
        PlayPeriod(1, startClock: 0, endClock: 45 * 60 + _rng.Uniform(60, 240), kickOffTeam: 0);
        PlayPeriod(2, startClock: 45 * 60, endClock: 90 * 60 + _rng.Uniform(120, 360), kickOffTeam: 1);
        return new MatchResult(_info, _events);
    }

    private void PlayPeriod(int period, double startClock, double endClock, int kickOffTeam)
    {
        _period = period;
        _clockSeconds = startClock;
        _events.Add(NewEvent(EventTypes.PeriodStart, team: null, player: null));
        KickOff(kickOffTeam, "kick_off");

        while (_clockSeconds < endClock)
        {
            Step();
        }

        _events.Add(NewEvent(EventTypes.PeriodEnd, team: null, player: null));
    }

    /// <summary>One on-ball decision by the current ball carrier: shoot, carry or pass.</summary>
    private void Step()
    {
        Team attackers = _teams[_attacking];
        Team defenders = _teams[1 - _attacking];

        var (attack, defence, presser) = ComputePositions();
        attack[_carrier] = _ball;

        double pressure = FootballModels.PressureFromDistance(Geometry.Distance(defence[presser], _ball));
        if (pressure >= PressureEventThreshold)
        {
            AddPressureEvent(defenders.Players[presser], defence[presser], pressure);
        }

        var freezeFrame = BuildFreezeFrame(attack, defence);
        int defendersInCone = defence.Count(p => Geometry.InShotCone(p, _ball));
        double xg = FootballModels.ExpectedGoals(_ball, defendersInCone);
        double distanceToGoal = Geometry.Distance(_ball, Pitch.GoalCentre);

        double shotProbability = distanceToGoal < ShotRangeMetres
            ? Math.Clamp((ShotBaseProbability + ShotXgFactor * xg) * attackers.Style.ShootingAppetite, 0, MaxShotProbability)
            : 0;
        double carryProbability = CarryBaseProbability + (1 - pressure) * CarryOpenSpaceBonus;

        double roll = _rng.NextDouble();
        if (roll < shotProbability)
        {
            Shoot(xg, defendersInCone, pressure, freezeFrame);
        }
        else if (roll < shotProbability + carryProbability || !TryPass(attack, defence, pressure, freezeFrame))
        {
            Carry(presser, pressure, freezeFrame);
        }
    }

    // ---------------------------------------------------------------- positions

    /// <summary>
    /// Places all 22 players relative to the ball, in the attacking team's perspective.
    /// Returns the index of the defender pressing the ball.
    /// </summary>
    private (Point[] Attack, Point[] Defence, int Presser) ComputePositions()
    {
        Team attackers = _teams[_attacking];
        Team defenders = _teams[1 - _attacking];
        var attack = new Point[11];
        var defence = new Point[11];

        // Attacking team pushes up with the ball.
        for (int i = 0; i < 11; i++)
        {
            Player p = attackers.Players[i];
            double x, y;
            if (p.Position == "GK")
            {
                x = 5 + 0.1 * _ball.X;
                y = Pitch.CentreY;
            }
            else
            {
                x = p.BaseX + 0.45 * (_ball.X - 40);
                y = p.BaseY + 0.25 * (_ball.Y - Pitch.CentreY);
            }
            attack[i] = Pitch.Clamp(x + Jitter(), y + Jitter());
        }

        // Defending team holds a compact block, computed in its own perspective then flipped.
        Point ballForDefenders = Pitch.Flip(_ball);
        for (int i = 0; i < 11; i++)
        {
            Player p = defenders.Players[i];
            double x, y;
            if (p.Position == "GK")
            {
                x = 4 + 0.05 * ballForDefenders.X;
                y = Pitch.CentreY;
            }
            else
            {
                x = p.BaseX + 0.4 * (ballForDefenders.X - Pitch.CentreX) - 4 * (1 - defenders.Style.PressIntensity);
                y = p.BaseY + 0.3 * (ballForDefenders.Y - Pitch.CentreY);
            }
            Point flipped = Pitch.Flip(new Point(x + Jitter(), y + Jitter()));
            defence[i] = Pitch.Clamp(flipped.X, flipped.Y);
        }

        // In the final third, forwards make runs into the box and the nearest defender tracks them.
        if (_ball.X > FinalThirdX)
        {
            for (int i = 0; i < 11; i++)
            {
                if (i == _carrier || !ForwardPositions.Contains(attackers.Players[i].Position)) continue;
                if (_rng.NextDouble() > RunProbability) continue;

                double runX = Math.Max(attack[i].X, Math.Clamp(_rng.Uniform(_ball.X - 4, 99), FinalThirdX, 100));
                double runY = Pitch.CentreY + (attack[i].Y - Pitch.CentreY) * RunCentralPull + _rng.NextGaussian(0, 4);
                attack[i] = Pitch.Clamp(runX, runY);

                int marker = NearestOutfieldIndex(defence, attack[i]);
                double tightness = 0.6 + 0.35 * _rng.NextDouble();
                defence[marker] = new Point(
                    defence[marker].X + (attack[i].X - defence[marker].X) * tightness,
                    defence[marker].Y + (attack[i].Y - defence[marker].Y) * tightness);
            }
        }

        // Nearest outfield defender steps in to press; harder pressing teams get tighter.
        int presser = NearestOutfieldIndex(defence, _ball);
        double gap = 1.0 + (1 - defenders.Style.PressIntensity) * 4 + Math.Abs(_rng.NextGaussian(0, 1.5));
        double angle = _rng.Uniform(0, 2 * Math.PI);
        defence[presser] = Pitch.Clamp(
            _ball.X + gap * Math.Cos(angle) * 0.6 + gap * 0.4,
            _ball.Y + gap * Math.Sin(angle));

        return (attack, defence, presser);
    }

    private double Jitter() => _rng.NextGaussian(0, PositionJitterMetres);

    private static int NearestOutfieldIndex(Point[] players, Point target)
    {
        int best = 1;
        double bestDistance = double.MaxValue;
        for (int i = 1; i < players.Length; i++) // index 0 is the goalkeeper
        {
            double d = Geometry.Distance(players[i], target);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }
        return best;
    }

    // ---------------------------------------------------------------- actions

    private void Shoot(double xg, int defendersInCone, double pressure, IReadOnlyList<PlayerPosition> freezeFrame)
    {
        Player shooter = _teams[_attacking].Players[_carrier];
        bool isGoal = _rng.NextDouble() < xg;

        string outcome =
            isGoal ? "goal" :
            defendersInCone >= 2 && _rng.NextDouble() < 0.4 ? "blocked" :
            _rng.NextDouble() < SaveShareOfUnblocked ? "saved" : "off_target";

        double endY = outcome switch
        {
            "goal" => _rng.Uniform(30.8, 37.2),
            "saved" => _rng.Uniform(31.0, 37.0),
            "off_target" => _rng.NextDouble() < 0.5 ? _rng.Uniform(24, 30.2) : _rng.Uniform(37.8, 44),
            _ => _ball.Y
        };
        double endX = outcome == "blocked" ? Math.Min(_ball.X + _rng.Uniform(1, 4), Pitch.Length) : Pitch.Length;
        double shotSpeedKmh = _rng.Uniform(70, 120);

        if (isGoal) _score[_attacking]++;

        _events.Add(NewEvent(EventTypes.Shot, _attacking, shooter) with
        {
            X = Round(_ball.X),
            Y = Round(_ball.Y),
            EndX = Round(endX),
            EndY = Round(endY),
            Outcome = outcome,
            Xg = Math.Round(xg, 3),
            Pressure = Math.Round(pressure, 2),
            BallSpeedKmh = Round(shotSpeedKmh),
            BodyPart = _rng.NextDouble() < 0.7 ? "right_foot" : "left_foot",
            FreezeFrame = freezeFrame
        });

        Advance(Geometry.Distance(_ball, new Point(endX, endY)) / (shotSpeedKmh / 3.6));

        if (isGoal)
        {
            Advance(_rng.Uniform(45, 75)); // celebration and restart
            KickOff(1 - _attacking, "kick_off_after_goal");
            return;
        }

        // Otherwise the defending goalkeeper restarts play.
        Advance(_rng.Uniform(20, 40));
        _attacking = 1 - _attacking;
        _ball = new Point(8, Pitch.CentreY);
        _carrier = 0;
        NewPossession(outcome switch
        {
            "off_target" => "goal_kick",
            "blocked" => "clearance",
            _ => "goalkeeper"
        });
    }

    private void Carry(int presser, double pressure, IReadOnlyList<PlayerPosition> freezeFrame)
    {
        Player carrier = _teams[_attacking].Players[_carrier];
        double distance = _rng.Uniform(3, 12);
        double angle = _rng.NextGaussian(0, 0.5);
        Point end = Pitch.Clamp(_ball.X + distance * Math.Cos(angle), _ball.Y + distance * Math.Sin(angle));
        double runSpeedKmh = _rng.Uniform(14, 26);
        bool keptBall = _rng.NextDouble() < 0.93 - 0.35 * pressure;

        Advance(distance / (runSpeedKmh / 3.6));

        _events.Add(NewEvent(EventTypes.Carry, _attacking, carrier) with
        {
            X = Round(_ball.X),
            Y = Round(_ball.Y),
            EndX = Round(keptBall ? end.X : _ball.X),
            EndY = Round(keptBall ? end.Y : _ball.Y),
            Outcome = keptBall ? "complete" : "dispossessed",
            Pressure = Math.Round(pressure, 2),
            PlayerSpeedKmh = Round(runSpeedKmh),
            FreezeFrame = freezeFrame
        });

        if (keptBall)
        {
            _ball = end;
            return;
        }

        // Tackled: the pressing defender wins the ball.
        int defendingTeam = 1 - _attacking;
        Player tackler = _teams[defendingTeam].Players[presser];
        Point tacklePoint = Pitch.Flip(_ball);

        _events.Add(NewEvent(EventTypes.Tackle, defendingTeam, tackler) with
        {
            X = Round(tacklePoint.X),
            Y = Round(tacklePoint.Y),
            Outcome = "won"
        });

        _attacking = defendingTeam;
        _ball = tacklePoint;
        _carrier = presser;
        NewPossession("tackle");
    }

    /// <summary>Attempts a pass. Returns false if no sensible receiver is available.</summary>
    private bool TryPass(Point[] attack, Point[] defence, double pressure, IReadOnlyList<PlayerPosition> freezeFrame)
    {
        Team attackers = _teams[_attacking];
        var candidates = new List<int>();
        var weights = new List<double>();

        for (int k = 0; k < 11; k++)
        {
            if (k == _carrier) continue;
            double distance = Geometry.Distance(attack[k], _ball);
            if (distance < MinPassMetres || distance > MaxPassMetres) continue;
            if (attackers.Players[k].Position == "GK" && _ball.X > 35) continue;

            double forward = attack[k].X - _ball.X;
            double space = defence.Min(d => Geometry.Distance(d, attack[k]));
            double weight =
                Math.Exp(attackers.Style.Directness * forward / DirectnessScale) *
                (0.3 + Math.Min(space, 10) / 10) *
                Math.Exp(-distance / PassDistanceDecay);

            candidates.Add(k);
            weights.Add(weight);
        }

        if (candidates.Count == 0) return false;

        int receiverIndex = candidates[_rng.WeightedIndex(weights)];
        Point target = attack[receiverIndex];
        double passDistance = Geometry.Distance(_ball, target);
        int defendersInLane = defence.Count(d =>
            Geometry.Distance(d, _ball) > 3 && Geometry.DistanceToSegment(d, _ball, target) < 1.5);

        bool complete = _rng.NextDouble() < FootballModels.PassSuccess(passDistance, defendersInLane, pressure);
        double ballSpeedKmh = _rng.Uniform(40, 75);

        _events.Add(NewEvent(EventTypes.Pass, _attacking, attackers.Players[_carrier]) with
        {
            ReceiverId = attackers.Players[receiverIndex].Id,
            X = Round(_ball.X),
            Y = Round(_ball.Y),
            EndX = Round(target.X),
            EndY = Round(target.Y),
            Outcome = complete ? "complete" : "incomplete",
            Pressure = Math.Round(pressure, 2),
            BallSpeedKmh = Round(ballSpeedKmh),
            FreezeFrame = freezeFrame
        });

        Advance(passDistance / (ballSpeedKmh / 3.6) + _rng.Uniform(1.3, 3.0));

        if (complete)
        {
            _ball = target;
            _carrier = receiverIndex;
            return true;
        }

        // Pass failed: either it goes out for a throw-in, or a defender intercepts.
        int defendingTeam = 1 - _attacking;
        var midpoint = new Point((_ball.X + target.X) / 2, (_ball.Y + target.Y) / 2);
        int winner = NearestOutfieldIndex(defence, midpoint);

        if (_rng.NextDouble() < OutOfPlayShare)
        {
            Advance(_rng.Uniform(8, 20));
            double touchlineY = target.Y < Pitch.CentreY ? 0.5 : Pitch.Width - 0.5;
            _attacking = defendingTeam;
            _ball = Pitch.Flip(new Point(target.X, touchlineY));
            _carrier = winner;
            NewPossession("throw_in");
            return true;
        }

        // Either a clean interception of the pass, or the loose ball is recovered after it.
        bool clean = _rng.NextDouble() < CleanInterceptionShare;
        string type = clean ? EventTypes.Interception : EventTypes.BallRecovery;
        Point winPoint = Pitch.Flip(defence[winner]);
        _events.Add(NewEvent(type, defendingTeam, _teams[defendingTeam].Players[winner]) with
        {
            X = Round(winPoint.X),
            Y = Round(winPoint.Y),
            Outcome = "won"
        });

        _attacking = defendingTeam;
        _ball = winPoint;
        _carrier = winner;
        NewPossession(type);
        return true;
    }

    // ---------------------------------------------------------------- helpers

    private void KickOff(int team, string reason)
    {
        _attacking = team;
        _ball = new Point(Pitch.CentreX, Pitch.CentreY);
        _carrier = IndexOfPosition(_teams[team], "ST");
        NewPossession(reason);
    }

    private void NewPossession(string reason)
    {
        _possessionId++;
        _events.Add(NewEvent(EventTypes.PossessionChange, _attacking, _teams[_attacking].Players[_carrier]) with
        {
            X = Round(_ball.X),
            Y = Round(_ball.Y),
            Outcome = reason
        });
    }

    private void AddPressureEvent(Player presser, Point presserPosition, double pressure)
    {
        int defendingTeam = 1 - _attacking;
        Point p = Pitch.Flip(presserPosition);
        _events.Add(NewEvent(EventTypes.Pressure, defendingTeam, presser) with
        {
            X = Round(p.X),
            Y = Round(p.Y),
            Pressure = Math.Round(pressure, 2)
        });
    }

    private IReadOnlyList<PlayerPosition> BuildFreezeFrame(Point[] attack, Point[] defence)
    {
        Team attackers = _teams[_attacking];
        Team defenders = _teams[1 - _attacking];
        var frame = new List<PlayerPosition>(22);
        for (int i = 0; i < 11; i++)
            frame.Add(new PlayerPosition(attackers.Players[i].Id, attackers.Id, Round(attack[i].X), Round(attack[i].Y)));
        for (int i = 0; i < 11; i++)
            frame.Add(new PlayerPosition(defenders.Players[i].Id, defenders.Id, Round(defence[i].X), Round(defence[i].Y)));
        return frame;
    }

    private MatchEvent NewEvent(string type, int? team, Player? player) => new()
    {
        EventId = $"evt_{++_sequence:000000}",
        MatchId = _info.MatchId,
        Period = _period,
        MatchClock = FormatClock(_clockSeconds),
        TimestampMs = (long)(_elapsedSeconds * 1000),
        Type = type,
        TeamId = team is int t ? _teams[t].Id : null,
        PlayerId = player?.Id,
        PossessionId = _possessionId,
        HomeScore = _score[0],
        AwayScore = _score[1]
    };

    private void Advance(double seconds)
    {
        _clockSeconds += seconds;
        _elapsedSeconds += seconds;
    }

    private static int IndexOfPosition(Team team, string position)
    {
        for (int i = 0; i < team.Players.Count; i++)
            if (team.Players[i].Position == position) return i;
        return team.Players.Count - 1;
    }

    private static string FormatClock(double seconds)
    {
        int total = (int)seconds;
        return $"{total / 60:00}:{total % 60:00}";
    }

    private static double Round(double value) => Math.Round(value, 1);
}
