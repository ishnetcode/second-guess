# Second Guess — The Football What-If Engine

**What if he'd passed instead of shot?**

Ask "what if?" at any key moment of a match. AI agents simulate the alternative decision
and explain, in plain language, how it would have changed the game.

> Built for the Microsoft *Inside the Game* Developer Hackathon.
> All teams, players and matches are synthetic and invented.

## What this project does

Every fan asks "what if?" after a big moment, and every expert argues about it. Today those debates
run on gut feeling and replays. Expected goals (xG) tells us how good a chance *was*, but not whether
a *different decision* would have been better.

Second Guess answers that question with evidence:

1. **Ask in natural language:** *"What if the striker had squared it instead of shooting?"*
2. **Find the moment:** an agent identifies the exact event and the positions of all 22 players at that instant.
3. **Simulate the alternatives:** a simulation engine runs thousands of variations of the actual
   action and the realistic alternatives (passes to each teammate, carries) and estimates the chance of scoring for each.
4. **Explain the difference:** an agent explains the result in plain words, for example
   *"The square pass would have raised the chance of scoring from 8% to 31%, because the teammate was unmarked with an open goal angle."*
5. **Check every claim:** a fact-check agent verifies every number against the simulation output
   and source events before the answer is shown.
6. **Personalise it:** *Fan mode* gives short, punchy verdicts; *Expert mode* gives a tactical
   breakdown for studio segments. Answers are available in the viewer's language.

Every simulation is also saved as a new labelled "alternative decision → outcome" record, building a
growing synthetic dataset for training better football decision models.

## How it covers the challenge

| Stage | What Second Guess does |
|---|---|
| **Ingest** | Streams synthetic match events (passes, shots, tackles, pressure, possession changes) as they happen |
| **Interpret** | Computes xG, pass success, pass difficulty, pressure, ball and shot speed |
| **Explain** | Explains why a decision mattered by comparing it with the alternatives |
| **Render** | Shows the actual and alternative paths on an animated pitch, plus timestamped JSON overlay cards for broadcast |
| **Personalise** | Fan vs Expert mode, favourite club, and multiple languages |
| **Data innovation** | Generates synthetic, football-realistic matches and a counterfactual dataset |

## Architecture

```
Synthetic match generator ──► Microsoft Fabric (Eventstream → Eventhouse)
                                        │
                     Simulation engine (MCP server, .NET)
                                        │
           Multi-agent system (Microsoft Agent Framework + Microsoft.Extensions.AI)
     Moment Finder → Simulator → Explainer → Fact-Checker → Localiser
                                        │
              Azure OpenAI via Microsoft Foundry · Azure AI Translator / Speech
                                        │
                Web UI (pitch view, Fan/Expert modes) on Azure Container Apps
```

| Agent | Role |
|---|---|
| **Moment Finder** | Turns a question into a specific event ID |
| **Simulator** | Calls the simulation tools and returns value ranges |
| **Explainer** | Writes the Fan-mode or Expert-mode answer |
| **Fact-Checker** | Checks every number against the simulation output; sends failures back to be rewritten |
| **Localiser** | Translates the answer into the viewer's language |

## Technology

- **.NET 10 / C#** with **Microsoft.Extensions.AI** and the **Microsoft Agent Framework**
- **Microsoft Foundry** and **Azure OpenAI** for models and evaluation
- **Model Context Protocol (MCP)** to expose the simulation engine as agent tools
- **Microsoft Fabric Real-Time Intelligence** for streaming and storing match events
- **Azure AI Translator** and **Azure AI Speech** for multilingual output
- **Azure Container Apps** for hosting
- **GitHub, GitHub Copilot and GitHub Actions** for development and CI/CD

## Explainable by design

Every answer shows its working: the original decision, the alternatives considered, the simulated
probabilities and the source event IDs. Simulations are presented as estimates, not facts.

## Project structure

```
src/
  SecondGuess.Domain/      Shared types, pitch geometry and football models (xG, pass success, pressure)
  SecondGuess.Generator/   Synthetic match generator and real-time replayer
data/                      Generated matches (match.json + events.jsonl per match)
```

## Getting started

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
# Generate one match (seed 42) into data/m_42
dotnet run --project src/SecondGuess.Generator -- generate --seed 42

# Generate five matches (seeds 100-104)
dotnet run --project src/SecondGuess.Generator -- generate --seed 100 --matches 5

# Replay a match as a live feed at 20x speed
dotnet run --project src/SecondGuess.Generator -- replay --match data/m_42 --speed 20
```

The same seed always produces the same match, so demos are reproducible.

## Synthetic data generator

Matches are simulated possession by possession:

- **Team shapes:** each team plays a formation (4-3-3, 4-2-3-1). Players shift with the ball;
  the defending side holds a compact block and its nearest player presses the ball.
- **Decisions:** the ball carrier shoots, carries or passes. Shot likelihood rises with chance quality;
  pass targets favour forward options and teammates in space.
- **Outcomes:** pass success depends on distance, defenders in the passing lane and pressure;
  goals come from an expected-goals (xG) model based on distance, angle and defenders in the shot cone.
- **Team styles:** directness, pressing intensity and shooting appetite make each team play differently.
- **Timing:** ball speed, running speed, restarts and stoppage time give realistic event timing.

Generated matches are checked against realistic per-team ranges (printed after each run):

| Metric | Typical generated value | Realistic range |
|---|---|---|
| Passes | ~490 | 350–650 |
| Pass accuracy | ~78% | 72–90% |
| Shots | ~10–15 | 5–20 |
| xG | ~1.0–1.5 | 0.3–2.8 |

## Data schema

**Coordinates:** metres on a 105 x 68 pitch, always from the perspective of the event's team,
which attacks towards x = 105 (goal centre at 105, 34).

`match.json` holds match metadata, teams, formations, styles and players.

`events.jsonl` holds one event per line:

| Field | Description |
|---|---|
| `eventId`, `matchId` | Identifiers |
| `period`, `matchClock`, `timestampMs` | Half, clock (`mm:ss`) and milliseconds since kick-off |
| `type` | `period_start`, `period_end`, `possession_change`, `pass`, `carry`, `shot`, `pressure`, `tackle`, `interception` |
| `teamId`, `playerId`, `receiverId` | Who performed (and received) the action |
| `x`, `y`, `endX`, `endY` | Start and end locations |
| `outcome` | e.g. `complete`, `incomplete`, `goal`, `saved`, `off_target`, `blocked`, `dispossessed`, `won`; for possession changes, the reason (`interception`, `throw_in`, `goal_kick`...) |
| `pressure` | Pressure on the ball carrier, 0–1 |
| `ballSpeedKmh`, `playerSpeedKmh` | Pass/shot speed and carry speed |
| `xg`, `bodyPart` | Shot chance quality and foot |
| `possessionId`, `homeScore`, `awayScore` | Match state |
| `freezeFrame` | Positions of all 22 players at the moment of a pass, carry or shot |

## Roadmap

- [x] Synthetic match generator and replayer
- [ ] Stream events to Microsoft Fabric Real-Time Intelligence
- [ ] Simulation engine exposed as an MCP server
- [ ] Multi-agent what-if system (Microsoft Agent Framework)
- [ ] Fact-checking, multilingual output and web UI
