# Writing the documentation

These pages are Sphinx + [MyST](https://myst-parser.readthedocs.io/), so every source file
is Markdown — the same markup the specifications and the constitution already use. A fact can
move between `specs/` and `docs/` without being translated on the way.

## Building them

```bash
python3 -m venv .venv-docs
.venv-docs/bin/pip install -r docs/requirements.txt
.venv-docs/bin/sphinx-build -W -b html docs docs/_build/html
```

Then open `docs/_build/html/index.html`.

`-W` turns warnings into errors, which is what Read the Docs does too — see
`.readthedocs.yaml`. A broken cross-reference or a page missing from a toctree fails the
build rather than shipping. **Documentation that quietly rots is worse than none, because it
is still believed.**

## Layout

```text
.readthedocs.yaml       the build configuration Read the Docs reads
docs/conf.py            Sphinx configuration; reads the version from Directory.Build.props
docs/requirements.txt   pinned, so a build in a year produces the same pages
docs/index.md           the front page and the three toctrees
docs/user/              for the person whose money this is
docs/dev/               for whoever changes the code
docs/reference/    formats, keys, migration coverage, glossary
```

## House rules

- **One home per fact.** The README is an overview that links here; the details live on one
  page each. Two copies of the backup rules will disagree within a month.
- **State the consequence, not just the rule.** Every principle in this project is written
  with the cost that makes it non-negotiable, and the documentation reads the same way.
- **Cross-reference with `{doc}`**, so a moved or renamed page fails the build instead of
  leaving a dead link.
- **Warn where it matters and nowhere else.** `danger` is reserved for things that destroy
  data — the password, the sidecar, an unencrypted export. Used for anything less it stops
  being read.
- **Never put real financial data in a page.** Principle 8 applies here exactly as it does to
  tests: no balance, no account name, no payee. Examples are invented.
- **Version-dependent text uses a substitution** — `{{ release }}` comes from
  `Directory.Build.props`, so a release does not need a documentation edit.

## How the pages get into the application

`build docs` (or `./build.sh docs`) builds the HTML, strips Sphinx's doctree cache and the
theme's source maps — the difference between 2.2 MB and 284 KB — and packs the rest to
`src/MyFinance.App/Assets/help.zip`. The application embeds that **conditionally**:

```xml
<EmbeddedResource Include="Assets\help.zip" LogicalName="help.zip"
                  Condition="Exists('Assets\help.zip')" />
```

So a machine without Python still builds and tests the whole solution; Help then reports that
the pages are absent, exactly as the application behaves when the embedding model is missing.
`publish` builds them first and warns rather than fails.

At run time `HelpService` unpacks them once to `%LOCALAPPDATA%\MyFinance\help\<version>\`
and opens a page in the default browser. Under the version, so an upgrade cannot leave the old
build's pages to be read as current.

`specs/019-in-application-help/` carries the full reasoning, in particular why the pages are
bundled rather than linked to the hosted copy: a request to a documentation host tells that
host this machine's address and that it runs this application, at the moment somebody is
asking a question about their own finances. See {doc}`principles`, principle 7.

### Topics

`MyFinance.Core.Help.HelpTopics` maps a `HelpTopic` to a page path, and each page view model
declares its topic. **Two tests keep that map honest**: every topic must resolve to a page that
exists under `docs/`, and every page in `docs/user/` must be reachable from some topic. Rename
a page without updating the map and the build fails, rather than a user finding a Help button
that opens nothing.

A new page needs a topic **only if a screen should open it**. Developer and reference pages
ship and are reachable by navigating.

## Adding a page

1. Write the Markdown under `docs/user/`, `docs/dev/` or `docs/reference/`.
2. Add it to the matching `toctree` in `docs/index.md` **and** in that section's `index.md`.
   A page in neither is a warning, and warnings are errors.
3. Build with `-W` before you commit.
