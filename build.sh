#!/usr/bin/env bash
# Build, test and package from a Linux dev box.
#
# The WPF project compiles here (EnableWindowsTargeting) but cannot run; the engine
# projects are plain net10.0 and are fully exercised by the test suites below.
# `publish` cross-builds the Windows executable, which also works from here.
set -euo pipefail

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

cd "$(dirname "$0")"

APP=src/MyFinance.App/MyFinance.App.csproj
OUT=artifacts/publish
HELP=src/MyFinance.App/Assets/help.zip
DOCS_VENV=.venv-docs

# Builds the documentation and packs it for embedding in the executable.
#
# Optional on purpose. A machine without Python still builds and tests the whole solution;
# Help then reports that this build shipped without its pages, the same way the application
# behaves when the embedding model is absent. `-W` matches .readthedocs.yaml -- a broken
# cross-reference must not ship inside the executable.
docs() {
  if ! command -v python3 >/dev/null 2>&1; then
    echo "note: python3 is not installed, so the documentation was not built" >&2
    return 1
  fi

  # Tested on the interpreter rather than the directory: a half-created or partly-cleaned
  # environment leaves the folder there, and testing for the folder would then skip the
  # repair and fail on the next line instead.
  [[ -x "$DOCS_VENV/bin/pip" ]] || python3 -m venv --clear "$DOCS_VENV"
  "$DOCS_VENV/bin/pip" install -q -r docs/requirements.txt

  rm -rf docs/_build/html
  "$DOCS_VENV/bin/sphinx-build" -W -q -b html docs docs/_build/html

  # The doctree cache is Sphinx's own bookkeeping and the source maps are for debugging
  # someone else's CSS. Together they are most of the size and none of the use.
  rm -rf docs/_build/html/.doctrees
  find docs/_build/html -name '*.map' -delete

  mkdir -p "$(dirname "$HELP")"
  rm -f "$HELP"

  if ! command -v zip >/dev/null 2>&1; then
    echo "note: 'zip' is not installed, so the documentation was not packed" >&2
    return 1
  fi

  (cd docs/_build/html && zip -qr9 "$OLDPWD/$HELP" .)

  echo "Documentation: $HELP ($(du -h "$HELP" | cut -f1))"
}

publish() {
  local rid="${1:-win-x64}"
  local dir="$OUT/$rid"

  # Before the build, so the pages end up inside the single file. A failure here is a
  # warning rather than a stop: a release without documentation is worse than one with, and
  # much better than no release at all on a machine that happens to lack Python.
  docs || echo "warning: publishing without embedded documentation" >&2

  rm -rf "$dir"

  # Self-contained so the machine needs no .NET runtime installed.
  # IncludeNativeLibrariesForSelfExtract matters specifically here: SQLCipher ships as a
  # native library, and without it the single file launches and then fails the moment it
  # tries to open a book.
  # Not trimmed -- WPF relies on reflection the trimmer cannot see.
  dotnet publish "$APP" \
    --configuration Release \
    --runtime "$rid" \
    --self-contained true \
    --output "$dir" \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:DebugType=embedded

  # Beside the executable as well as inside it. The embedded copies are what satisfy the
  # notice requirements when somebody has only the .exe; these are what a person reads
  # without launching anything, and what a package maintainer expects to find.
  cp LICENSE NOTICE "$dir/"

  local exe="$dir/MyFinance.exe"
  if [[ -f "$exe" ]]; then
    local version
    version=$(grep -oP '(?<=<Version>)[^<]+' Directory.Build.props | head -1)
    local zip="artifacts/MyFinance-$version-$rid.zip"

    rm -f "$zip"

    # Two things had to be right here and neither was, which is why no release archive was
    # ever produced on a Linux host:
    #
    #   -r          without it zip treats the '.' as one directory entry, skips it, and
    #               exits saying "Nothing to do".
    #   absolute    the path is written from inside $dir, three levels down, so a relative
    #               one has to count those levels and got it wrong.
    #
    # Both failed quietly into a fallback that blamed a missing tool.
    local target="$PWD/$zip"

    if ! command -v zip >/dev/null 2>&1; then
      echo "note: 'zip' is not installed, leaving the folder unpacked" >&2
    elif ! (cd "$dir" && zip -q -9 -r "$target" .); then
      echo "note: the release archive could not be written, leaving the folder unpacked" >&2
    fi

    echo
    echo "Published: $exe ($(du -h "$exe" | cut -f1))"
    [[ -f "$zip" ]] && echo "Archive:   $zip ($(du -h "$zip" | cut -f1))"
  fi
}

case "${1:-all}" in
  build)   dotnet build MyFinance.slnx ;;
  docs)    docs ;;
  test)    dotnet test MyFinance.slnx ;;
  all)     dotnet build MyFinance.slnx && dotnet test MyFinance.slnx ;;
  publish) publish "${2:-win-x64}" ;;
  clean)
    dotnet clean MyFinance.slnx
    find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
    rm -rf artifacts docs/_build "$HELP"
    ;;
  *) echo "usage: $0 [build|test|all|docs|publish [rid]|clean]" >&2; exit 2 ;;
esac
