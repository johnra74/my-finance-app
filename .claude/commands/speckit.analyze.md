---
description: Check the spec set for drift against the code and against itself.
---

Analyze: **$ARGUMENTS** (one spec folder, or all of `specs/` if not given).

Report, and do not fix anything without being asked:

1. **Dead citations.** Every file path named in a spec, plan or task list that no longer
   exists. This is how a spec set rots in its first month.
2. **Untested requirements.** Every `FR-` whose task cites no test, and every `SC-` with a
   number in it that no test asserts.
3. **Drift.** Where the spec and the code disagree about behaviour. The code is the fact;
   report the spec as the bug.
4. **Constitution conflicts.** Any requirement that contradicts a principle, or any
   principle that no spec depends on.
5. **Unresolved markers.** Every `[NEEDS CLARIFICATION]` still outstanding, with its age.
6. **Index gaps.** Folders under `specs/` missing from `specs/README.md`, and rows in that
   table with no folder.

Rank by what would actually mislead a reader, and say plainly when nothing is wrong.
