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
configuration=""
results_directory=""
skip_build=0
skip_format=0

while [ "$#" -gt 0 ]; do
    case "$1" in
        --solution) solution="$2"; shift 2 ;;
        --test-project) test_project="$2"; shift 2 ;;
        --filter) filter="$2"; shift 2 ;;
        --full) full=1; shift ;;
        --format-changed) format_changed=1; shift ;;
        --mutation) mutation=1; shift ;;
        --configuration) configuration="$2"; shift 2 ;;
        --results-directory) results_directory="$2"; shift 2 ;;
        --skip-build) skip_build=1; shift ;;
        --skip-format) skip_format=1; shift ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

configuration_args=()
if [ -n "$configuration" ]; then
    configuration_args=(-c "$configuration")
fi

if [ "$skip_build" -eq 0 ]; then
    echo "check-local: build" >&2
    dotnet build "$solution" -v minimal "${configuration_args[@]}"
fi

test_projects=("$test_project")
if [ "$full" -eq 1 ]; then
    test_projects=()
    solution_projects="$(dotnet sln "$solution" list)"
    while IFS= read -r project; do
        case "$project" in
            tests/*.csproj) test_projects+=("$project") ;;
        esac
    done <<< "$solution_projects"
    if [ "${#test_projects[@]}" -eq 0 ]; then
        echo "check-local: no test projects found in $solution" >&2
        exit 1
    fi
fi

preserve_results=0
if [ -n "$results_directory" ]; then
    mkdir -p "$results_directory"
    results_run="$(mktemp -d "$results_directory/run-XXXXXXXX")"
    preserve_results=1
else
    results_run="$(mktemp -d "${TMPDIR:-/tmp}/rt-check-local-XXXXXXXX")"
fi

project_index=0
for project in "${test_projects[@]}"; do
    project_index=$((project_index + 1))
    project_results="$results_run/project-$project_index"
    mkdir -p "$project_results"
    echo "check-local: test $project (report: $project_results/results.trx)" >&2
    test_args=(test "$project" -v minimal --no-build "${configuration_args[@]}"
        --logger 'trx;LogFileName=results.trx' --results-directory "$project_results")
    if [ -n "$filter" ]; then
        test_args+=(--filter "$filter")
    fi
    dotnet "${test_args[@]}"
    python3 "$script_dir/verify-test-trx.py" "$project_results/results.trx" "$project"
done

if [ "$skip_format" -eq 0 ]; then
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
fi

if [ "$mutation" -eq 1 ]; then
    if command -v pwsh >/dev/null 2>&1; then
        pwsh -NoProfile -File scripts/pre-commit-gates.ps1
    else
        echo "check-local: --mutation requires pwsh" >&2
        exit 1
    fi
fi

if [ "$preserve_results" -eq 0 ]; then
    rm -rf "$results_run"
fi

echo "check-local: OK" >&2
