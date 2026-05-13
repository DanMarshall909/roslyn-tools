#!/usr/bin/env bash
# Run once to wire up rt tab completion for bash or zsh.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
EXE="$REPO_ROOT/src/RoslynTools.Cli/bin/Release/net8.0/rt.exe"
PROJ="$REPO_ROOT/src/RoslynTools.Cli/RoslynTools.Cli.csproj"

# Build if binary doesn't exist yet
if [ ! -f "$EXE" ]; then
    echo "rt: binary not found, building..."
    dotnet build "$PROJ" -c Release --nologo -v quiet
fi

# Install dotnet-suggest if missing
if ! command -v dotnet-suggest &>/dev/null; then
    echo "Installing dotnet-suggest..."
    dotnet tool install -g dotnet-suggest
fi

# Register the binary (idempotent)
dotnet-suggest register --command-path "$EXE"
echo "Registered: $EXE"

# Detect shell and emit profile snippet
SHELL_NAME="$(basename "${SHELL:-bash}")"
case "$SHELL_NAME" in
    zsh)  PROFILE="$HOME/.zshrc" ;;
    *)    SHELL_NAME=bash; PROFILE="$HOME/.bashrc" ;;
esac

echo ""
echo "Add the following to $PROFILE, then restart your shell:"
echo ""
dotnet-suggest script "$SHELL_NAME"
