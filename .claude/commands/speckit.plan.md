---
description: Turn a clarified specification into an implementation plan.
---

Plan the implementation for the spec at: **$ARGUMENTS**.

1. Read the spec. **If it still has `[NEEDS CLARIFICATION]` markers, stop** and run
   `/speckit.clarify` first — planning around an open question produces a plan that has to
   be thrown away.
2. Read `.specify/memory/constitution.md` and fill in the Constitution check table
   honestly. A violation is listed with its justification, or the design changes; it is
   never omitted.
3. Read the existing code the feature touches. Reuse what is there — this repository has a
   great deal already built, and a plan that reinvents `Money`, `RegisterService` or the
   suggestion chain is a bad plan.
4. Write `specs/<folder>/plan.md` from `.specify/templates/plan-template.md`, and
   `data-model.md` if the feature owns entities.
5. State the alternatives you rejected and why. A plan that presents one option has not
   been made yet.
6. Report the plan path and anything you could not resolve without a decision from the user.
