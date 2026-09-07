---
description: Create or update a feature specification from a natural-language description.
---

Write a feature specification for: **$ARGUMENTS**

1. Read `.specify/memory/constitution.md`. Every requirement you write must be consistent
   with it; if the feature requires breaking a principle, say so explicitly rather than
   writing around it.
2. Read `specs/README.md` to find the next free number and to check no existing spec
   already covers this.
3. Create `specs/<NNN>-<kebab-name>/spec.md` from `.specify/templates/spec-template.md`.
4. Fill it in from the description, and from the codebase where the feature touches
   existing behaviour. Search before assuming something does not exist.
5. Rules that are not negotiable:
   - **What and why only.** No class names, no file paths, no libraries, no schema.
   - Requirements numbered `FR-001…`, prohibitions included where one matters.
   - Success criteria numbered `SC-001…`, **measurable and technology-agnostic**.
   - Anything genuinely undecided gets `[NEEDS CLARIFICATION: <the question>]`. Do not
     guess and do not paper over it — an unmarked guess is the expensive kind of error.
   - Name the constitution principles the feature depends on, under Assumptions.
6. Add the new spec to the index table in `specs/README.md`.
7. Report the path, the requirement count, and every `[NEEDS CLARIFICATION]` marker left.
