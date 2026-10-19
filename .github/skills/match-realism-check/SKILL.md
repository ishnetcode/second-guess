---
name: match-realism-check
description: Generate synthetic Second Guess matches and check that passes, accuracy, shots, xG and other per-team stats fall within realistic football ranges. Use after changing the generator, the football models or any tuning constant.
---
# Match realism check

## When to use
After any change to `src/SecondGuess.Domain/FootballModels.cs`, `src/SecondGuess.Generator/MatchSimulator.cs`
or `MatchFactory.cs`, and before recording a demo.

## Steps
1. Build: `dotnet build SecondGuess.slnx`
2. Generate ten matches with fixed seeds:
   ```bash
   dotnet run --project src/SecondGuess.Generator -- generate --seed 100 --matches 10 --out artifacts/qa
   ```
3. Each match prints a summary table. Every row with a range must say `OK`.
4. Summarise the results across all ten matches (average, minimum, maximum per metric).

## Plausible per-team ranges for one match

| Metric | Single-match range | Typical average |
|---|---|---|
| Passes | 300–700 | ~500 |
| Pass accuracy % | 70–92 | ~78 |
| Shots | 3–25 | 10–14 |
| Shots on target | 0–12 | about a third of shots |
| xG | 0.1–3.5 | 1.2–1.9 |
| Tackles won | 10–55 | ~35 |
| Interceptions | 5–40 | ~25 |
| Ball recoveries | 20–80 | ~50 |

A single CHECK on one match can be normal variance. Judge the **averages** across all ten matches:
they should sit near the typical column. Goals per match (both teams) should average around 2.5–3.

## Which constant to adjust
| Symptom | Adjust in `MatchSimulator` / `FootballModels` |
|---|---|
| Too many / too few shots | `ShotBaseProbability`, `ShotXgFactor`, `ShotRangeMetres` |
| xG too high because of runners | `RunProbability`, marking tightness in `ComputePositions` |
| Pass accuracy too low / high | `FootballModels.PassSuccess` intercept (3.4) or distance weight |
| Too many / too few passes | Hold time in `TryPass` (`Uniform(1.3, 3.0)`) |
| xG too high | `ExpectedGoals` distance or angle weights |
| Too many tackles | Carry success `0.93 - 0.35 * pressure` |
| Too many shots on target | `SaveShareOfUnblocked` |
| Too many interceptions | `CleanInterceptionShare`, `OutOfPlayShare` |

Change one constant at a time and re-run the check.
