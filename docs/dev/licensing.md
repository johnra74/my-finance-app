# Licensing

MyFinance is released under the **Apache License, Version 2.0**. The licence is in `LICENSE`
at the repository root; what the published executable carries is in `NOTICE` beside it.

## Why Apache-2.0

The choice was made against this code base rather than by habit, and four things decided it.

**The reader is the part worth reusing.** `MyFinance.Import.Mny` is a pure-C# reader for the
Jet 4 / MSISAM format a Microsoft Money file uses. It is original work — nothing here is
ported from mdbtools or Jackcess — so nothing constrained the choice, and that is worth
stating plainly, because the first question anyone reusing it will have is whether it carries
someone else's copyleft. It does not. A permissive licence is what makes the reader useful to
the people most likely to want it: whoever is next trying to get twenty-five years of records
out of a dead product.

**The patent grant is cheap insurance.** Apache-2.0 §3 grants a patent licence and terminates
it for anyone who sues over the work. MIT is silent on patents. For software that implements a
long-lived proprietary format, saying so explicitly costs nothing and settles a question a
downstream user would otherwise have to weigh.

**§6 disclaims trademark grants.** This project names someone else's product on nearly every
page, and must: describing what it reads is the whole point. An explicit clause saying the
licence conveys no rights in those names is exactly the right posture.

**The notice discipline already existed.** The embedding model is Apache-2.0 and was already
credited before the project had a licence of its own. Apache-2.0 makes that the convention
rather than a one-off.

:::{note}
**Not copyleft.** GPL was considered and rejected. It cannot enforce the thing principle 7
actually cares about — that nothing leaves the machine — because that is enforced by the code
and by the user building it themselves, not by a licence. What it would do is make the reader
unusable to most of the people who need it. AGPL is meaningless for software with no network
service at all.
:::

## What the executable carries

MyFinance publishes as **one self-contained file**. That file contains the .NET runtime,
SQLCipher and its LibTomCrypt provider, the sentence-embedding model, and the help pages —
and several of those licences require their copyright notice to be reproduced wherever the
binary goes.

So the notice is in three places, and each covers a case the others do not:

| Where | Covers |
|---|---|
| `NOTICE` at the repository root | reading the source |
| Embedded in the executable, shown by **About** | somebody who has only the `.exe` |
| Copied beside the executable by `build publish` | somebody who never launches it |

Nothing in the dependency graph is copyleft. SQLCipher is BSD-3-Clause, its crypto provider is
LibTomCrypt rather than OpenSSL, and everything else is MIT, Apache-2.0, BSD or public domain.

## Adding a dependency

Add it to `NOTICE` in the same change. `NoticeTests` reads `Directory.Packages.props` and
fails the build for any package the notice does not name — deliberately for *every* package,
shipped or build-time-only, because deciding which ones reach the executable is exactly the
judgement that goes wrong quietly.

A dependency with no package id of its own needs adding by hand. There are three of them
today, and they are the easiest to lose precisely because no dependency review would surface
them:

- **SQLCipher** and **LibTomCrypt** arrive inside `SQLitePCLRaw.bundle_e_sqlcipher`'s native
  library and appear in no project file.
- **all-MiniLM-L6-v2** is downloaded by `tools/fetch-model.sh` (`tools\fetch-model` on
  Windows) and is not in the repository.
- **Sphinx** and **Furo** contribute the stylesheets and scripts inside `help.zip`.

`NoticeTests` names all five, so removing one from the notice fails too.

## Before publishing the repository

Three things are worth checking once, and only one of them is about a licence:

1. **The application icon.** `docs/design/app-icon-source.png` is the source for the shipped
   `.ico`, and its own README notes that it carries a stock file badge — which suggests a
   stock download whose terms are not recorded anywhere. Establish where it came from, or
   redraw it. An asset of unknown provenance is the likeliest thing to cause trouble.
2. **The book.** A `.mny` may be sitting in the repository root. `.gitignore` excludes it;
   confirm `git status` agrees before the first commit rather than after the first push.
3. **The Money screenshots.** `docs/design/money-screens/` holds another product's interface
   showing a real book. Ignored, excluded from the documentation build, and to stay that way.

## The copyright holder

`LICENSE` and `Directory.Build.props` both say **The MyFinance Authors**. That is a
placeholder in the sense that it is a project rather than a person: replace it with a legal
name if the project ever needs one on paper.
