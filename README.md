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

## Built with GitHub Copilot

The repository ships a GitHub Copilot toolkit in `.github/`, so every contributor (human or AI)
works to the same rules.

**Custom agents** (`.github/agents/`) form a development team with handoffs between them:

| Agent | Role | Hands off to |
|---|---|---|
| **Architect** | Plans a feature against the codebase and hackathon criteria; never edits code | Builder |
| **Builder** | Implements in small steps, building and verifying after each one | Match QA, Hackathon Judge |
| **Match QA** | Generates matches and checks the stats look like real football | Builder (to fix issues) |
| **Hackathon Judge** | Scores the repo against the rules and judging criteria | Architect (to plan improvements) |
| **Feature Lead** | Orchestrates the others as subagents to deliver a feature end to end | — |

**Skills** (`.github/skills/`) package reusable know-how:
`match-realism-check`, `synthetic-data-rules` and `hackathon-submission`.

**Slash commands** (`.github/prompts/`):

| Command | What it does |
|---|---|
| `/generate-match` | Generates a match and writes a short report, including a good "what if?" moment |
| `/new-event-type` | Adds a new event type to the generator, following the data rules |
| `/judge-check` | Scores the project against the judging criteria |
| `/sync-readme` | Updates the README so it matches the code |

**Instructions:** `.github/copilot-instructions.md` holds project-wide rules (synthetic data only,
coordinate convention, determinism, no secrets), and `.github/instructions/` adds rules per file type.

**Workflows:** `ci.yml` builds the solution, generates sample matches and flags unrealistic stats on
every push; `copilot-setup-steps.yml` prepares the environment for the Copilot coding agent.

## Explainable by design

Every answer shows its working: the original decision, the alternatives considered, the simulated
probabilities and the source event IDs. Simulations are presented as estimates, not facts.

## Project structure

```
.github/
  agents/ skills/ prompts/ instructions/   GitHub Copilot toolkit (see "Built with GitHub Copilot")
  workflows/               CI and Copilot coding agent setup
src/
  SecondGuess.Domain/      Shared types, pitch geometry and football models (xG, pass success, pressure)
  SecondGuess.Generator/   Synthetic match generator, real-time replayer and what-if CLI
  SecondGuess.Simulation/  What-if engine: moment finder, Monte Carlo simulation, counterfactual dataset
  SecondGuess.McpServer/   MCP server exposing the what-if engine as tools for agents
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

# Find the shots where another option was better, then simulate the biggest one (or pass --event evt_000123)
dotnet run --project src/SecondGuess.Generator -- whatif --match data/m_42

# Run the tests
dotnet test SecondGuess.slnx
```

The same seed always produces the same match, so demos are reproducible.

## Synthetic data generator

Matches are simulated possession by possession:

- **Team shapes:** each team plays a formation (4-3-3, 4-2-3-1). Players shift with the ball;
  the defending side holds a compact block and its nearest player presses the ball.
- **Attacking runs:** in the final third, forwards sometimes run into the box and the nearest
  defender tracks them, so a better-placed teammate is sometimes available.
- **Decisions:** the ball carrier shoots, carries or passes. Shot likelihood rises with chance quality;
  pass targets favour forward options and teammates in space.
- **Outcomes:** pass success depends on distance, defenders in the passing lane and pressure;
  goals come from an expected-goals (xG) model based on distance, angle and defenders in the shot cone.
- **Team styles:** directness, pressing intensity and shooting appetite make each team play differently.
- **Timing:** ball speed, running speed, restarts and stoppage time give realistic event timing.

After each run the generator checks every team total against a plausible range for a single match.
Averages across many matches stay close to real football:

| Metric (per team) | Typical generated average | Plausible single-match range |
|---|---|---|
| Passes | ~500 | 300–700 |
| Pass accuracy | ~78% | 70–92% |
| Shots | ~10–14 | 3–25 |
| Shots on target | ~4 (about a third of shots) | 0–12 |
| xG | ~1.2–1.9 | 0.1–3.5 |
| Interceptions | ~25 | 5–40 |
| Ball recoveries | ~50 | 20–80 |

## What-if simulation engine

For any shot, pass or carry, the engine compares the decision that was taken with the realistic
alternatives: **shoot**, **carry towards goal**, or **pass to each outfield teammate** within 5–50 m.

For each option it runs 2,000 Monte Carlo simulations, adding about 1 m of uncertainty to every
player's position, and estimates the **chance that the option leads to a goal from the next shot**:

| Option | Value per simulation |
|---|---|
| Shoot | xG from the shooter's position, given defenders in the shot cone |
| Carry | chance of keeping the ball (pressure, defenders in the carry path and near where it ends) × xG after carrying ~6 m towards goal |
| Pass | pass success probability (distance, defenders in the lane, pressure) × receiver's xG, reduced if the receiver is under pressure |

Each option returns the average, a P10–P90 range, and the **football factors** behind it (pass
distance, defenders in the lane, the receiver's space, defenders in the shot cone), so agents can
explain *why* one decision was better. Results are **deterministic** (seeded from the event ID),
so a fact-checker can re-run and verify any number.

Every simulation is appended to `data/{matchId}/counterfactuals.jsonl` as a labelled
"actual decision vs best alternative" record, which grows into a new synthetic dataset.

## MCP server

`SecondGuess.McpServer` exposes the engine over the Model Context Protocol (stdio):

| Tool | What it does |
|---|---|
| `list_matches` | Lists the generated matches |
| `rank_decisions` | Simulates every shot and ranks the decisions where a better option existed |
| `find_moments` | Finds simulatable moments by player name and/or minute; otherwise the biggest chances first |
| `get_game_state` | Returns all 22 player positions at a moment |
| `simulate_options` | Simulates the actual decision and alternatives, and saves the counterfactual record |

The repository includes `.vscode/mcp.json`, so in VS Code you can start the **second-guess** server
and ask GitHub Copilot in agent mode: *"In match m_42, what if the biggest missed chance had been a pass?"*

To explore the tools directly, use the MCP Inspector:

```bash
npx @modelcontextprotocol/inspector dotnet run --project src/SecondGuess.McpServer -- --data data
```

## Data schema

**Coordinates:** metres on a 105 x 68 pitch, always from the perspective of the event's team,
which attacks towards x = 105 (goal centre at 105, 34).

`match.json` holds match metadata, teams, formations, styles and players.

`events.jsonl` holds one event per line:

| Field | Description |
|---|---|
| `eventId`, `matchId` | Identifiers |
| `period`, `matchClock`, `timestampMs` | Half, clock (`mm:ss`) and milliseconds since kick-off |
| `type` | `period_start`, `period_end`, `possession_change`, `pass`, `carry`, `shot`, `pressure`, `tackle`, `interception`, `ball_recovery` |
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
- [x] GitHub Copilot toolkit (agents, skills, slash commands) and CI
- [ ] Stream events to Microsoft Fabric Real-Time Intelligence
- [x] What-if simulation engine, exposed as an MCP server
- [x] Unit tests
- [ ] Multi-agent what-if system (Microsoft Agent Framework)
- [ ] Fact-checking, multilingual output and web UI
