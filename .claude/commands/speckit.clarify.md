---
description: Resolve the open questions in a specification by asking the user.
---

Clarify the spec at: **$ARGUMENTS** (a folder under `specs/`, or the most recently
modified one if not given).

1. Read the spec and collect every `[NEEDS CLARIFICATION: …]` marker.
2. Also look for questions the spec did not think to ask: an unstated failure mode, an
   ambiguous quantity, a requirement with no measurable success criterion, an edge case the
   acceptance scenarios skip.
3. Ask the user with `AskUserQuestion` — at most four questions at a time, each with real
   options and a recommendation where you have one. Do not ask what the codebase can answer.
4. Rewrite the affected requirements in place. Delete the marker; do not leave it beside the
   answer.
5. Append a **Clarifications** section recording each question, the answer, and the date, so
   a later reader can see the decision was made rather than assumed.
6. Set `Status: Clarified` when no markers remain.
