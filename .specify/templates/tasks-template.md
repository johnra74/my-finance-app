# Tasks: [FEATURE NAME]

**Spec:** `./spec.md` · **Plan:** `./plan.md`

`[X]` complete · `[ ]` outstanding · `[P]` may run in parallel with its neighbours.

Every task carries the file that implements it and the test that proves it. A task with no
test citation is either untested or not really a task — say which.

## Phase 1 — [name]

- [ ] **T001** [description]
  - Implements: `path/to/file.cs`
  - Proven by: `tests/.../SomeTests.cs::Test_name`
  - Requirement: FR-001

## Phase 2 — [name]

- [ ] **T002** [P] [description]
  - Implements: …
  - Proven by: …
  - Requirement: …

## Coverage

| Requirement | Tasks | State |
|---|---|---|
| FR-001 | T001 | ✅ |
