---
name: synthetic-data-rules
description: Rules and the event schema for Second Guess synthetic football data. Use when adding a new event type, a new field, new teams or players, or anything that writes match data.
---
# Synthetic data rules

## Legal and hackathon rules
- All data must be synthetic. Never import or copy real match data, even for calibration.
- Never use real club names, player names, badges, sponsors, stadiums or Premier League branding.
- Invented names must be checked so they do not obviously match a famous real player.

## Coordinate convention
- Metres on a 105 x 68 pitch.
- Each event is recorded from the perspective of its `teamId`, attacking towards x = 105.
- Freeze frames use the same perspective as their event.

## Adding a new event type
1. Add a constant to `EventTypes` in `src/SecondGuess.Domain/Entities.cs`.
2. Add any new optional fields to `MatchEvent` as nullable `init` properties (they are omitted from JSON when null).
3. Emit the event from `MatchSimulator` using `NewEvent(...) with { ... }` so IDs, clock and score are filled in.
4. Add a case to `MatchReplayer.Describe` so it shows in the live feed.
5. If it should be counted, add a row to `MatchSummary`.
6. Document the event and fields in the README "Data schema" table.
7. Run the `match-realism-check` skill.

## Determinism
Only use the simulator's seeded `Random`. The same seed must always produce identical `events.jsonl` output.
