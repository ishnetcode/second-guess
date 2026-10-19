---
name: sync-readme
description: Update the README so it matches what the code actually does
agent: Builder
tools: ['read', 'search', 'edit']
---
Compare the README with the code in `src/` and `.github/`:
- Update commands, project structure and the data schema table to match the code exactly.
- Tick roadmap items only if they are fully built; untick any that are not.
- Mark planned architecture and technology as planned.
- Keep the existing tone and structure. Do not invent features.
Show a short list of what you changed.
