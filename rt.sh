#!/usr/bin/env bash
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
EXE="$SCRIPT_DIR/src/RoslynTools.Cli/bin/Release/net8.0/rt.exe"
PROJ="$SCRIPT_DIR/src/RoslynTools.Cli/RoslynTools.Cli.csproj"

needs_build() {
    [ ! -f "$EXE" ] && return 0
    [ -n "$(find "$SCRIPT_DIR/src" \( -name obj -o -name bin \) -prune -o \( -name "*.cs" -o -name "*.csproj" \) -newer "$EXE" -print 2>/dev/null | head -1)" ] && return 0
    [ -n "$(find "$SCRIPT_DIR" -maxdepth 1 \( -name "*.slnx" -o -name "Directory.Build.props" -o -name "Directory.Packages.props" \) -newer "$EXE" 2>/dev/null | head -1)" ] && return 0
    return 1
}

if needs_build; then
    echo 'rt: rebuilding...' >&2
    dotnet build "$PROJ" -c Release --nologo -v quiet || { echo 'rt: build failed' >&2; exit 1; }
    command -v dotnet-suggest &>/dev/null && dotnet-suggest register --command-path "$EXE" 2>/dev/null || true
fi

exec "$EXE" "$@"
