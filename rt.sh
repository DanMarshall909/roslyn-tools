#!/usr/bin/env bash
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
OUT_DIR="$SCRIPT_DIR/src/RoslynTools.Cli/bin/Release/net10.0"
EXE="$OUT_DIR/rt"
WIN_EXE="$OUT_DIR/rt.exe"
DLL="$OUT_DIR/rt.dll"
PROJ="$SCRIPT_DIR/src/RoslynTools.Cli/RoslynTools.Cli.csproj"

# The build-status marker path is shared with .claude/scripts/rt-first-gate.sh,
# which reads it to bypass the rt-first gate while the build is broken. Both
# sides resolve it through the single definition in rt-paths.sh so they cannot
# drift. rt.sh always runs from a checkout (it builds $PROJ from source), so
# rt-paths.sh is always alongside it.
# shellcheck source=.claude/scripts/rt-paths.sh
. "$SCRIPT_DIR/.claude/scripts/rt-paths.sh"
BUILD_STATUS_MARKER="$(rt_build_status_marker)"

write_build_status() {
    mkdir -p "$(dirname "$BUILD_STATUS_MARKER")" 2>/dev/null || true
    printf '%s' "$1" > "$BUILD_STATUS_MARKER" 2>/dev/null || true
}

runner_path() {
    [ -x "$EXE" ] && printf '%s' "$EXE" && return 0
    [ -x "$WIN_EXE" ] && printf '%s' "$WIN_EXE" && return 0
    [ -f "$DLL" ] && printf '%s' "$DLL" && return 0
    return 1
}

needs_build() {
    local marker
    marker="$(runner_path || true)"
    [ -z "$marker" ] && return 0
    [ -n "$(find "$SCRIPT_DIR/src" \( -name obj -o -name bin \) -prune -o \( -name "*.cs" -o -name "*.csproj" \) -newer "$marker" -print 2>/dev/null | head -1)" ] && return 0
    [ -n "$(find "$SCRIPT_DIR" -maxdepth 1 \( -name "*.slnx" -o -name "Directory.Build.props" -o -name "Directory.Packages.props" \) -newer "$marker" 2>/dev/null | head -1)" ] && return 0
    return 1
}

if needs_build; then
    # Serialise concurrent rebuilds across parallel rt invocations.
    # Mirrors rt.ps1's named-mutex strategy (commit 0ad69f3) so the two
    # wrappers behave identically on the POSIX side. Without this, two
    # `bash rt.sh ...` calls landing after a source edit can both run
    # `dotnet build` at once and trip CS2012 (file in use).
    LOCK_PATH="${TMPDIR:-/tmp}/rt-rebuild.lock"
    if command -v flock >/dev/null 2>&1; then
        exec 9>"$LOCK_PATH"
        if ! flock -w 300 9; then
            echo 'rt: timed out waiting for rebuild lock' >&2
            exit 1
        fi
        # fd 9 is released automatically when the script exits.
    else
        echo 'rt: flock not available; concurrent invocations may race' >&2
    fi

    echo 'rt: rebuilding...' >&2
    if ! dotnet build "$PROJ" -c Release --nologo -v quiet 1>&2; then
        echo 'rt: build failed' >&2
        write_build_status broken
        exit 1
    fi
    command -v dotnet-suggest &>/dev/null && [ -x "$EXE" ] && dotnet-suggest register --command-path "$EXE" 2>/dev/null || true
fi
write_build_status ok

RUNNER="$(runner_path || true)"
if [ -z "$RUNNER" ]; then
    echo 'rt: build did not produce a runnable output' >&2
    exit 1
fi

case "$RUNNER" in
    *.dll) exec dotnet "$RUNNER" "$@" ;;
    *) exec "$RUNNER" "$@" ;;
esac
