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

## Realistic per-team ranges

| Metric | Range | Typical |
|---|---|---|
| Passes | 350–650 | ~490 |
| Pass accuracy % | 72–90 | ~78 |
| Shots | 5–20 | 10–15 |
| Shots on target | 1–9 | 3–5 |
| xG | 0.3–2.8 | 1.0–1.5 |
| Tackles won | 10–45 | ~30 |
| Interceptions | 10–60 | — |

Goals per match (both teams) should usually be 0–5, averaging around 2.5.

## Which constant to adjust
| Symptom | Adjust in `MatchSimulator` / `FootballModels` |
|---|---|
| Too many / too few shots | `ShotBaseProbability`, `ShotXgFactor`, `ShotRangeMetres` |
| Pass accuracy too low / high | `FootballModels.PassSuccess` intercept (3.4) or distance weight |
| Too many / too few passes | Hold time in `TryPass` (`Uniform(1.3, 3.0)`) |
| xG too high | `ExpectedGoals` distance or angle weights |
| Too many tackles | Carry success `0.93 - 0.35 * pressure` |

Change one constant at a time and re-run the check.
