# Feature Specification: [FEATURE NAME]

**Feature Branch / Folder:** `[###-feature-name]`
**Created:** [DATE]
**Status:** Draft | Clarified | Implemented
**Input:** [the original description this spec was written from]

> A spec says **what** and **why**. No technology, no file names, no API shapes — those
> belong in `plan.md`. Anything genuinely undecided is marked
> `[NEEDS CLARIFICATION: the question]` rather than guessed at.

## User Scenarios & Testing *(mandatory)*

### Primary user story

[One paragraph. Who, what they are trying to achieve, and what success looks like to them.]

### Acceptance scenarios

1. **Given** [starting state], **When** [action], **Then** [observable outcome]
2. **Given** …, **When** …, **Then** …

### Edge cases

- [What happens when …?]
- [How does the system behave when …?]

## Requirements *(mandatory)*

### Functional requirements

- **FR-001**: The system MUST [capability, stated observably]
- **FR-002**: The system MUST [capability]
- **FR-003**: The system MUST NOT [prohibition, where one matters]

### Non-functional requirements

- **NFR-001**: [performance, safety or privacy constraint, with a number where possible]

## Key Entities *(where the feature owns data)*

- **[Entity]**: [what it represents, what it holds, what it relates to]

## Success Criteria *(mandatory)*

Measurable and technology-agnostic — a criterion that names a class or a file is a design
statement in the wrong document.

- **SC-001**: [e.g. "A user can enter a split transaction across three categories in under
  30 seconds without the total ever being allowed to disagree with the parts."]
- **SC-002**: […]

## Assumptions

- Depends on constitution principles: [list].
- [Anything taken as given that a reader might otherwise question.]

## Out of Scope

- [What this feature deliberately does not do, and where it is covered instead if it is.]
