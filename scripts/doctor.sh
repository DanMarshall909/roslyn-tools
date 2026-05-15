#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
repo_root="$(dirname "$script_dir")"
cd "$repo_root"

failed=0

echo "doctor: dotnet sdk" >&2
dotnet --version || failed=1

if [ ! -f RoslynTools.slnx ]; then
    echo "doctor: missing RoslynTools.slnx" >&2
    failed=1
fi

hooks_path="$(git config --get core.hooksPath || true)"
if [ "$hooks_path" = "scripts/git-hooks" ]; then
    echo "doctor: git hooks installed" >&2
else
    echo "doctor: git hooks not installed; run scripts/install-git-hooks.ps1" >&2
fi

echo "doctor: dotnet tools" >&2
dotnet tool restore || failed=1

echo "doctor: rt dump-help" >&2
./rt.sh dump-help >/dev/null || failed=1

if [ "$failed" -ne 0 ]; then
    echo "doctor: issues found" >&2
    exit 1
fi

echo "doctor: OK" >&2
