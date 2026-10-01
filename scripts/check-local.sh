#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
repo_root="$(dirname "$script_dir")"
cd "$repo_root"

solution="RoslynTools.slnx"
test_project="tests/RoslynTools.Tests/RoslynTools.Tests.csproj"
filter=""
full=0
format_changed=0
mutation=0

while [ "$#" -gt 0 ]; do
    case "$1" in
        --solution) solution="$2"; shift 2 ;;
        --test-project) test_project="$2"; shift 2 ;;
        --filter) filter="$2"; shift 2 ;;
        --full) full=1; shift ;;
        --format-changed) format_changed=1; shift ;;
        --mutation) mutation=1; shift ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

echo "check-local: build" >&2
dotnet build "$solution" -v minimal

test_target="$test_project"
[ "$full" -eq 1 ] && test_target="$solution"

echo "check-local: test $test_target" >&2
if [ -n "$filter" ]; then
    dotnet test "$test_target" -v minimal --no-build --filter "$filter"
else
    dotnet test "$test_target" -v minimal --no-build
fi

if [ "$format_changed" -eq 1 ]; then
    if command -v pwsh >/dev/null 2>&1; then
        pwsh -NoProfile -File scripts/format-changed.ps1 -Solution "$solution" -Verify
    else
        echo "check-local: --format-changed requires pwsh" >&2
        exit 1
    fi
else
    echo "check-local: format verify" >&2
    dotnet format "$solution" --verify-no-changes
fi

if [ "$mutation" -eq 1 ]; then
    if command -v pwsh >/dev/null 2>&1; then
        pwsh -NoProfile -File scripts/pre-commit-gates.ps1
    else
        echo "check-local: --mutation requires pwsh" >&2
        exit 1
    fi
fi

echo "check-local: OK" >&2
