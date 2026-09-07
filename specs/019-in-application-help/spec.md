# Feature Specification: In-application help

**Folder:** `019-in-application-help`
**Created:** 2026-09-06
**Status:** **Implemented** (2026-09-06)
**Input:** "Make the documentation accessible through a menu as Help."

## Why this is a gap

A documentation set exists — a user guide, a developer section and a reference, thirty-one
pages — and **nothing in the application points at it**. There is no Help command, no `F1`,
no About. A user who was handed the executable has no way to know the pages exist, let alone
where.

That matters more here than it would elsewhere, because the questions this application raises
are the ones a user cannot experiment their way out of:

| The question | Where it is asked | Whether guessing is safe |
|---|---|---|
| What happens if I forget the password? | The create screen | **No.** It is unrecoverable. |
| What is this second file, and can I delete it? | Anywhere | **No.** Deleting it destroys the book. |
| Why do my migrated balances disagree with Money's? | The migration wizard | No — the reasonable conclusion is that the migration is broken. |
| Is the export encrypted? | The export command | **No.** It is not. |
| Which of these backups will open with which password? | The restore screen | No. |

Each has a written answer already. None of them is reachable from the screen where it is
asked.

## User Scenarios & Testing

### Primary user story

Somebody is on a screen and does not understand something about it. They press `F1`, or they
click Help, and they are reading about **that screen** — offline, on a machine with no network
connection, in a page that describes the version they are running.

### Acceptance scenarios

1. **Given** any page of the shell, **When** the user presses `F1`, **Then** the documentation
   opens at the page about that screen.
2. **Given** the unlock screen with no book open, **When** the user presses `F1`, **Then** the
   documentation opens at the page about creating and opening a book.
3. **Given** a machine with no network connection, **When** the user asks for help, **Then**
   they get it.
4. **Given** a build made without the documentation toolchain, **When** the user asks for help,
   **Then** they are told plainly and the application carries on.
5. **Given** the user upgrades to a newer build, **When** they ask for help, **Then** they read
   the pages belonging to the build they are running.

### Edge cases

- The profile folder is unwritable, or the pages cannot be unpacked → the application says so
  and carries on.
- No browser is associated with `.html` → the same.
- Extraction is interrupted half way → the next attempt must not find and trust a partial
  folder.
- A page is renamed in the documentation → this must fail the build, not the user.

## Requirements

### Functional requirements

- **FR-001**: The system MUST make its documentation reachable from the application, both by a
  command in the top bar and by `F1`.
- **FR-002**: Help MUST be **context-sensitive**: the page opened is the one about the screen
  the user is on. A screen that has not been given a topic MUST fall back to the contents
  rather than doing nothing.
- **FR-003**: Help MUST be reachable **before a book is open**, because the questions with the
  worst consequences — the password, the second file, which backup opens with what — are asked
  on that screen.
- **FR-004**: The documentation MUST be **carried inside the executable** and read from the
  local machine. It MUST NOT be fetched over a network, and the system MUST NOT offer to open a
  hosted copy. Constitution 7: a request to a documentation host tells that host this machine's
  address and that it runs this application, at the moment somebody is asking a question about
  their own finances.
- **FR-005**: The pages MUST correspond to the build that is running. A user who upgrades must
  not read a description of the version they replaced.
- **FR-006**: Asking for help MUST NOT be able to fail the application. Every failure is
  reported as a sentence and recorded in the diagnostics log.
- **FR-007**: A build made without the documentation toolchain MUST still build, test and run.
  Help then reports that the pages are absent and names where they can be read instead.
- **FR-008**: Every topic the application can ask for MUST resolve to a page that exists, and
  this MUST be enforced at build time rather than discovered by a user.
- **FR-009**: The system MUST NOT ship a second rendering of the documentation. What the user
  reads in the application is the same page the documentation build produces.

### Non-functional requirements

- **NFR-001**: The documentation MUST NOT add materially to the download. The measured cost is
  **284 KB** against an 85 MB executable.
- **NFR-002**: Bundling MUST NOT introduce a runtime dependency. Constitution and `011`: the
  application ships as one self-contained file needing nothing installed.
- **NFR-003**: Unpacking MUST be atomic. An interrupted extraction must leave nothing a later
  launch would treat as complete.
- **NFR-004**: The pages of a superseded build MUST NOT accumulate in the user's profile.

## Key Entities

- **Help topic** — a screen's subject, and the page that explains it.
- **The packed documentation** — the built pages, carried in the executable.
- **The unpacked documentation** — where a version's pages are read from.

## Success Criteria

- **SC-001**: Every topic resolves to a page that exists in the documentation, asserted against
  the sources so it holds on a machine with no documentation toolchain.
  *`HelpPageTests.Every_topic_opens_a_page_that_exists`.*
- **SC-002**: Every page of the user guide is reachable from some screen — a page nobody can
  open is caught too. *`HelpPageTests.Every_user_guide_page_is_reachable_from_a_topic`.*
- **SC-003**: No topic's path can escape the folder the pages were unpacked into.
  *`HelpTopicTests.Every_page_path_is_relative_and_html`.*
- **SC-004**: A topic added without a page is refused rather than silently opening the
  contents. *`HelpTopicTests.An_unmapped_topic_is_refused_rather_than_sent_to_the_contents`.*
- **SC-005**: The solution builds, tests and runs with the packed documentation absent —
  verified by removing it and running the whole suite.
- **SC-006**: Asking for help makes no network request. There is no code path that could.

## Assumptions

- Depends on constitution principle **7** (nothing leaves the machine — which is the whole
  argument for bundling rather than linking), **4** (the topic map is in `MyFinance.Core` so it
  can be tested without Windows), **9** (a failure here never takes the application down), and
  on `011-packaging-and-release` for the single-file constraint that rules out a bundled
  browser control.
- The user has a browser. Every Windows install does, and it already has search, back, zoom,
  print and bookmarks — none of which would be worth rebuilding.

## Clarifications

### 2026-09-06

- **Q: Where do the pages come from at run time?** → **Bundled, offline, no hosted copy
  offered** (FR-004). Linking out would be one line and would make a network request the
  application's own affordance rather than something the user chose to do. It would also fail
  offline and, during an upgrade, describe a version that is not running.
- **Q: How does help behave?** → **Context-sensitive, with `F1`**, and present on the unlock
  screen (FR-002, FR-003). A contents page answers *how does this application work*; the
  question somebody actually has is *what is this screen*.
- **Q: How is the documentation build wired in?** → **An optional step that degrades
  gracefully** (FR-007), exactly as the embedding model already does. A machine without Python
  still builds and tests everything.

## Out of Scope

- **A viewer inside the application.** A browser control is the runtime dependency the
  single-file build exists to avoid, and rendering the Markdown a second way would produce
  pages that disagree with the real ones. See FR-009.
- **Offering the hosted copy.** Permanently — constitution 7. The documentation says where it
  is; going there is the user's act.
- **Search across the documentation from inside the application.** The pages ship with
  Sphinx's own search, and the browser has find-on-page.
- **An About window.** ~~A separate, smaller question.~~ **Built since**, on this feature's
  plumbing: it takes `IHelpService` for its Documentation button and sits beside Help in the
  top bar and on the unlock screen. It was not a smaller question in the end — the licences
  MyFinance ships under require their notices to travel with the binary, and About is where
  they travel. See `docs/dev/licensing.md` and `011-packaging-and-release`.
