# AGENTS.md

This project is deliberately minimalist. Follow these rules for all future changes:

- Prefer the simplest implementation that satisfies the current requirement.
- Do not add speculative abstractions or future-proofing.
- Do not introduce new interfaces/classes/services unless they have a clear current purpose.
- Reuse existing models, validators and services.
- Avoid duplicate validation and business logic.
- Do not add dependencies unless necessary.
- Do not refactor unrelated working code.
- Delete unused code rather than leaving placeholders.
- Prefer readable code over clever code.
- Make the smallest possible change for each task.
- Tests should protect behaviour, not implementation details.
- Do not add excessive tests purely to increase test count.
- AI providers may only produce structured planning data, never PHP/plugin source.
