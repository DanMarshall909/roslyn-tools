#!/usr/bin/env bash
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
OUT_DIR="$SCRIPT_DIR/src/RoslynTools.Cli/bin/Release/net10.0"
EXE="$OUT_DIR/rt"
WIN_EXE="$OUT_DIR/rt.exe"
DLL="$OUT_DIR/rt.dll"
PROJ="$SCRIPT_DIR/src/RoslynTools.Cli/RoslynTools.Cli.csproj"

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
    echo 'rt: rebuilding...' >&2
    dotnet build "$PROJ" -c Release --nologo -v quiet || { echo 'rt: build failed' >&2; exit 1; }
    command -v dotnet-suggest &>/dev/null && [ -x "$EXE" ] && dotnet-suggest register --command-path "$EXE" 2>/dev/null || true
fi

RUNNER="$(runner_path || true)"
if [ -z "$RUNNER" ]; then
    echo 'rt: build did not produce a runnable output' >&2
    exit 1
fi

case "$RUNNER" in
    *.dll) exec dotnet "$RUNNER" "$@" ;;
    *) exec "$RUNNER" "$@" ;;
esac
