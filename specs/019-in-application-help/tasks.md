# Tasks: In-application help

**Spec:** `./spec.md` · **Plan:** `./plan.md` · **Status:** **Complete**

## Tasks

| # | Task | File | Satisfies | Proven by |
|---|---|---|---|---|
| T001 | The topic vocabulary and its page map | `src/MyFinance.Core/Help/HelpTopic.cs` | FR-002, FR-008 | `HelpTopicTests.Every_topic_names_a_page` |
| T002 | Refuse an unmapped topic rather than defaulting | same | FR-008 | `HelpTopicTests.An_unmapped_topic_is_refused_rather_than_sent_to_the_contents` |
| T003 | Keep every path relative, so it cannot escape the unpack folder | same | FR-006 | `HelpTopicTests.Every_page_path_is_relative_and_html` |
| T004 | Unpack and open, with every failure caught | `src/MyFinance.App/Services/HelpService.cs` | FR-001, FR-006 | ⚠️ needs Windows — no test project reaches `MyFinance.App` |
| T005 | Unpack atomically, under the running version | same | FR-005, NFR-003 | ⚠️ needs Windows |
| T006 | Remove a superseded version's pages | same | NFR-004 | ⚠️ needs Windows |
| T007 | Say so plainly when no pages were embedded | same | FR-007 | Verified by removing `help.zip` and building |
| T008 | Record a help failure in the log | same, `Operation.OpenHelp` | FR-006 | ⚠️ needs Windows |
| T009 | A topic per page, defaulted not abstract | `src/MyFinance.App/ViewModels/PageViewModel.cs` and the ten pages | FR-002 | `HelpPageTests.Every_topic_opens_a_page_that_exists` |
| T010 | `Help` command and button in the top bar | `ShellViewModel.cs`, `Views/ShellWindow.xaml` | FR-001 | ⚠️ needs Windows |
| T011 | `F1` on the shell | `Views/ShellWindow.xaml` | FR-001 | ⚠️ needs Windows |
| T012 | Help on the unlock screen, before a book is open | `StartupViewModel.cs`, `Views/StartupWindow.xaml` | FR-003 | ⚠️ needs Windows |
| T013 | Help in the migration wizard | `MigrationWizardViewModel.cs`, `MigrationWizardWindow.xaml` | FR-003 | ⚠️ needs Windows |
| T014 | Build the pages and pack them | `build.sh`, `build.ps1` | FR-004, NFR-001 | `./build.sh docs` produces a 284 KB `help.zip` |
| T015 | Embed conditionally | `src/MyFinance.App/MyFinance.App.csproj` | FR-007, NFR-002 | Solution builds and tests with `help.zip` absent |
| T016 | Publish builds the pages first, warning rather than failing | `build.sh`, `build.ps1` | FR-005, FR-007 | Verified by publishing |
| T017 | Every topic resolves to a page that exists | `tests/MyFinance.Data.Tests/Help/HelpPageTests.cs` | FR-008 | itself — verified by renaming a page |
| T018 | Every user-guide page is reachable | same | FR-008 | itself |
| T019 | Share the source-tree walker between test classes | `tests/MyFinance.Data.Tests/Sources/SourceTree.cs` | — | Both suites pass |
| T020 | Documentation of the documentation | `docs/dev/documentation.md`, `docs/reference/keyboard.md`, `docs/user/troubleshooting.md` | FR-001 | Docs build clean with `-W` |

## Coverage

| Requirement | Covered by | |
|---|---|---|
| FR-001 Reachable by button and `F1` | T010, T011, T012 | ⚠️ by eye on Windows |
| FR-002 Context-sensitive, with a fallback | T001, T009 | ✅ |
| FR-003 Reachable before a book is open | T012, T013 | ⚠️ by eye on Windows |
| FR-004 Carried in the executable, never fetched | T014, T015 | ✅ — there is no network code to test |
| FR-005 Pages match the build | T005, T016 | ⚠️ by eye on Windows |
| FR-006 Cannot fail the application | T003, T004, T008 | ✅ for the path rules, ⚠️ for the rest |
| FR-007 Builds without the toolchain | T007, T015 | ✅ verified by removing `help.zip` |
| FR-008 Enforced at build time | T017, T018 | ✅ |
| FR-009 No second rendering | design — nothing renders Markdown | ✅ by construction |
| NFR-001 Negligible size | T014 | ✅ 284 KB measured |
| NFR-002 No runtime dependency | T015 | ✅ |
| NFR-003 Atomic unpack | T005 | ⚠️ needs Windows |
| NFR-004 No accumulation | T006 | ⚠️ needs Windows |

**Eight rows carry a ⚠️.** All of them are in `MyFinance.App`, which no test project can
reference — it targets `net10.0-windows` and the build runs on Linux. This is the same
boundary `016` and `018` report, and the honest position is to say so rather than to claim
coverage that does not exist. What *is* covered without Windows is the part that rots: the map
between a screen and a page.
