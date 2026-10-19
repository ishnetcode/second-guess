# Second Guess: Copilot instructions

Second Guess is a football "what-if" engine built for the Microsoft *Inside the Game* Developer Hackathon.
Users ask "what if he'd passed instead of shot?" and a multi-agent system simulates the alternative,
explains the difference in plain language and fact-checks every number.

## Tech stack
- .NET 10 / C# 13, solution file `SecondGuess.slnx`
- `src/SecondGuess.Domain`: shared types, pitch geometry, football models (xG, pass success, pressure)
- `src/SecondGuess.Generator`: synthetic match generator and real-time replayer
- Planned: Microsoft Agent Framework + Microsoft.Extensions.AI agents, an MCP server for the simulation
  engine, Microsoft Foundry / Azure OpenAI, Microsoft Fabric Real-Time Intelligence, Azure Container Apps

## Build and run
```bash
dotnet build SecondGuess.slnx
dotnet run --project src/SecondGuess.Generator -- generate --seed 42
dotnet run --project src/SecondGuess.Generator -- replay --match data/m_42 --speed 20
```

## Rules that always apply
- **Synthetic data only.** Never add real match data, real club names, real player names, logos or
  Premier League branding. Teams and players are invented (Northvale Rovers, Eastport Athletic).
- **Coordinates:** metres on a 105 x 68 pitch, from the perspective of the event's team, which always
  attacks towards x = 105. Use `Pitch.Flip` to switch perspective.
- **Determinism:** the same seed must always produce the same match. Only use the seeded `Random`
  passed into the simulator; never `Random.Shared` or `new Random()` without a seed.
- **Explainability:** every AI-generated number must trace back to a tool output or source event ID.
  Agents state numbers only from tool results, never from model memory.
- **No secrets in the repo.** Read keys and endpoints from configuration or environment variables.
- Keep the README roadmap honest: only tick items that are actually built.
