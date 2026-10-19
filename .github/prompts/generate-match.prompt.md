---
name: generate-match
description: Generate a synthetic match and summarise it like a football reporter
argument-hint: Seed number, e.g. 42
agent: agent
tools: ['read', 'execute']
---
Generate a synthetic match with seed `${input:seed:42}`:

```bash
dotnet run --project src/SecondGuess.Generator -- generate --seed ${input:seed:42}
```

Then read `data/m_${input:seed:42}/events.jsonl` and `match.json` and give:
1. The final score and a three-sentence match report in plain English, using the invented player names.
2. The three biggest chances (highest xG shots) with minute, player, xG and outcome.
3. One moment that would make a good "what if?" question for the demo, with its event ID.
