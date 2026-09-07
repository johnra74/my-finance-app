# Implementation Plan: In-application help

**Spec:** `./spec.md` · **Status:** **Built** — see `./tasks.md`

## Summary

The documentation is built to HTML, packed, and embedded in the executable as a resource. A
`Help` button and `F1` unpack it once into the user's profile under the running version and
open the page for the current screen in the default browser.

The decision this turns on: **the pages ship with the build rather than being fetched.** That
is what makes help work offline, keeps it honest across an upgrade, and keeps constitution 7
intact at the moment a user is asking a question about their own money.

## Constitution check

| Principle | How this feature satisfies it |
|---|---|
| 7 — nothing leaves the machine | The pages are inside the executable. There is no URL, no fetch and no "read this online" affordance to click by mistake. |
| 4 — correctness outside WPF | The topic-to-page map is in `MyFinance.Core` and is tested without Windows. Only the unpacking and the shell-out are WPF. |
| 9 — a failure never takes the application down | Every path in `HelpService.Open` is caught, reported as a sentence and recorded. |
| 10 — the user's data stays theirs | Unchanged, and reinforced: the pages that explain the export and the backups are now reachable from the screens that offer them. |
| 6 — store nothing you do not need | Nothing is stored about the user. The unpacked pages are the application's own files, under the version, and the previous version's are removed. |

## Technical context

- **Projects:** `MyFinance.Core/Help` (the map), `MyFinance.App/Services/HelpService.cs` (unpack
  and open), the page view models and two windows (the topics and the key bindings).
- **Dependencies:** **none new** at run time. At build time, Python and Sphinx —
  **optional**, and already needed to build the documentation at all.
- **Location:** `%LOCALAPPDATA%\MyFinance\help\<version>\`, beside the log's folder and for the
  same reasons.
- **Testing:** `tests/MyFinance.Core.Tests/Help` for the map; `tests/MyFinance.Data.Tests/Help`
  for the pages, which needs the repository rather than a book.
- **Measured cost:** 1.4 MB of pages, **284 KB packed**.

## Design

### Not linking to the hosted copy

One line of code, always current, nothing to build. Rejected:

- **It is a network request** made at the moment somebody is asking a question about their own
  finances, telling a third party this machine's address and that it runs this application.
  Constitution 7 does not have an exception for convenience.
- **It fails when the user has no connection**, which includes the case where they are trying
  to find out why something will not work.
- **It describes the wrong version.** The hosted copy tracks the source; the user is running a
  build. The two diverge exactly when a release is in flight.

The documentation names its own online home. Going there is the user's act, which is the same
line drawn for the diagnostics log.

### Not a viewer inside the application

- **WebView2** is a runtime dependency, and `011`'s whole promise is one self-contained file
  that needs nothing installed.
- **A `FlowDocument` renderer** would be a second rendering of the same Markdown, and the two
  would disagree. FR-009.
- The default browser already has search, back, zoom, print, bookmarks and the reader's own
  accessibility settings. None of that is worth rebuilding badly.

### Why unpack rather than serve from memory

The browser needs files: a page pulls in a stylesheet, a script and a font. Unpacking once to
the profile is what makes those resolve.

Under the **version**, so an upgrade cannot leave the previous build's pages to be read as
current (FR-005), and the previous version's folder is removed on the way (NFR-004).
Extraction goes to a `.unpacking` sibling and is then moved into place, so an interruption
leaves nothing that a later launch would find and trust (NFR-003).

### An enum, not a path, at each call site

The same argument the diagnostics log makes. A path typed into ten view models is ten paths
that rot silently, and the symptom — a Help key that opens nothing — reaches the user and
nobody else. `HelpTopic` is checked against the documentation at build time (SC-001).

`PageViewModel.HelpTopic` is **virtual with a default**, not abstract: a page added later that
has not thought about help opens the contents, which is a worse answer than the right one and a
far better answer than a dead key.

### Optional at build time

`EmbeddedResource … Condition="Exists('Assets\help.zip')"`, which is exactly how
`MyFinance.Semantics` carries the embedding model. A machine without Python builds and tests
the whole solution; help then says the pages are not there and names `docs/`.

`publish` builds them first and **warns rather than fails** if it cannot: a release without
documentation is worse than one with it, and much better than no release at all.

`sphinx-build -W` means a documentation warning breaks a release. That is intended, and matches
`.readthedocs.yaml`: a broken cross-reference should not ship inside the executable.

## Alternatives rejected

| Alternative | Why not |
|---|---|
| Open readthedocs.io | Constitution 7; fails offline; describes the wrong version. |
| A bundled browser control | A runtime dependency `011` exists to avoid. |
| Render the Markdown in WPF | A second rendering that would disagree with the real pages. |
| Ship `docs/` as loose files beside the exe | Defeats "one file you can copy anywhere", and it is a folder users move, zip without, or lose. |
| Commit the built HTML | ~100 generated files in source control, stale more often than not. |
| A Help menu of contents entries | Answers *how does this application work*; the question asked is *what is this screen*. |
