---
description: Break an implementation plan into an ordered, traceable task list.
---

Generate tasks for: **$ARGUMENTS**.

1. Read `spec.md`, `plan.md` and `data-model.md` in that folder.
2. Write `tasks.md` from `.specify/templates/tasks-template.md`.
3. Rules:
   - Order by dependency: entities and migrations, then services, then view models, then
     views. Tests alongside the code they cover, not in a phase at the end.
   - Mark `[P]` only where two tasks genuinely touch different files.
   - Every task names the requirement it satisfies and the test that will prove it.
   - Finish with the coverage table: every `FR-` mapped to at least one task. An
     unreferenced requirement means the plan missed something.
4. Report the task count and any requirement with no task against it.
