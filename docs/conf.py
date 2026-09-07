"""Sphinx configuration for the MyFinance documentation.

Every page is Markdown, parsed by MyST. The project's specifications, its constitution
and its README are Markdown too, so text can move between them unchanged -- which is the
only way a fact ends up with exactly one home.
"""

import re
from pathlib import Path

# -- Project ----------------------------------------------------------------------

project = "MyFinance"
author = "MyFinance"
copyright = "2026, MyFinance"


def _version_from_build_props() -> str:
    """Read the version from the single place the build already keeps it.

    Hard-coding it here would give the docs a version of their own, and the two would
    disagree the first time a release was cut.
    """
    props = Path(__file__).resolve().parent.parent / "Directory.Build.props"

    try:
        found = re.search(r"<Version>([^<]+)</Version>", props.read_text(encoding="utf-8"))
    except OSError:
        return "0.0.0"

    return found.group(1) if found else "0.0.0"


release = _version_from_build_props()
version = ".".join(release.split(".")[:2])

# -- General ----------------------------------------------------------------------

extensions = [
    "myst_parser",
    "sphinx.ext.todo",
    "sphinx.ext.intersphinx",
    "sphinx_copybutton",
    "sphinx_design",
]

myst_enable_extensions = [
    "colon_fence",
    "deflist",
    "fieldlist",
    "linkify",
    "substitution",
    "tasklist",
]

# Headings down to h3 get anchors, so pages can link to a section of another page.
myst_heading_anchors = 3

# Substituted into any page, so a version or a file extension is stated once.
myst_substitutions = {
    "release": release,
    "book_ext": "`.mfdb`",
    "sidecar_ext": "`.mfmeta`",
    "backup_ext": "`.mfbak`",
}

templates_path = ["_templates"]
exclude_patterns = [
    "_build",
    "Thumbs.db",
    ".DS_Store",
    "requirements.txt",
    # Design sources, not documentation: the icon artwork and the Microsoft Money screens
    # this application is modelled on. The latter are screenshots of a real book -- they
    # stay out of anything published. Principle 8.
    "design",
]

# Warnings are errors on Read the Docs (see .readthedocs.yaml). These are the only ones
# suppressed, and each needs a reason; nothing else may be.
suppress_warnings = [
    # The epub builder walks the output directory and complains about Sphinx's own
    # doctree cache, which is not content and has no mimetype. Purely an artefact of
    # where the cache sits, so it must not fail the epub format build.
    "epub.unknown_project_files",
]

nitpicky = False
todo_include_todos = True

# -- HTML -------------------------------------------------------------------------

html_theme = "furo"
html_title = f"MyFinance {release}"
html_static_path = ["_static"]
html_last_updated_fmt = "%Y-%m-%d"
html_copy_source = False
html_show_sphinx = False

html_theme_options = {
    "navigation_with_keys": True,
}

# -- Other builders ----------------------------------------------------------------

latex_documents = [
    ("index", "MyFinance.tex", "MyFinance Documentation", author, "manual"),
]

epub_show_urls = "footnote"

# Nothing here links out to a Python API, but the mapping is kept so a future
# reference to the standard library resolves rather than warning.
intersphinx_mapping = {}
