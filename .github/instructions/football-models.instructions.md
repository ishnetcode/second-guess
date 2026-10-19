---
applyTo: "src/SecondGuess.Domain/**"
description: Rules for football models and geometry
---
# Football model rules
- Models must stay simple and explainable: logistic formulas with named inputs, not black boxes.
- Every coefficient change must keep generated matches inside the realistic ranges in
  `.github/skills/match-realism-check/SKILL.md`. Run that check after changing a model.
- Probabilities are always returned in the range 0–1; clamp where needed.
- Geometry assumes the attacking goal is at (105, 34). Do not add a second coordinate convention.
- When adding a model, also describe it in the README "Synthetic data generator" section.
