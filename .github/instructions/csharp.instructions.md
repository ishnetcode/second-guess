---
applyTo: "**/*.cs"
description: C# coding conventions for Second Guess
---
# C# conventions
- File-scoped namespaces matching the folder (`SecondGuess.Domain`, `SecondGuess.Generator`).
- Prefer `sealed record` for data types and `readonly record struct` for small value types like `Point`.
- Nullable reference types are enabled: no `!` suppressions without a comment explaining why.
- Use `required` and `init` for event and DTO properties.
- Put tuning numbers in named `const` fields with a comment, never as magic numbers inline.
- Public types and members get a one-line `///` summary.
- Async methods end in `Async` and accept a `CancellationToken`.
- Serialize JSON only through `JsonDefaults.Compact` or `JsonDefaults.Indented`.
