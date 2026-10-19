---
name: new-event-type
description: Add a new event type to the synthetic match generator
argument-hint: Event type and when it happens, e.g. "foul when a tackle fails under high pressure"
agent: Builder
---
Add a new event type to the generator: ${input:eventType}

Follow the `synthetic-data-rules` skill step by step (EventTypes constant, MatchEvent fields, emit in
MatchSimulator, replayer description, summary row, README schema). Keep the match deterministic.
When finished, run the `match-realism-check` skill and report the results.
