#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
scratch="$(mktemp -d)"
child_file="$scratch/compiler-child.pid"
cleanup() {
    for pid_file in "$scratch"/*.pid; do
        [ -f "$pid_file" ] || continue
        kill "$(cat "$pid_file")" 2>/dev/null || true
    done
    rm -rf "$scratch"
}
trap cleanup EXIT

if ! command -v flock >/dev/null 2>&1; then
    echo "wrapper-lock-test: skipped (flock unavailable)" >&2
    exit 0
fi

fixture="$scratch/checkout"
mkdir -p "$fixture/.claude/scripts" "$fixture/src/RoslynTools.Cli" "$scratch/bin"
cp "$repo_root/rt.sh" "$fixture/rt.sh"
cp "$repo_root/.claude/scripts/rt-paths.sh" "$fixture/.claude/scripts/rt-paths.sh"
touch "$fixture/src/RoslynTools.Cli/RoslynTools.Cli.csproj"

cat > "$scratch/runner" <<'RUNNER'
#!/usr/bin/env bash
printf '%s\n' 'fixture rt invoked'
RUNNER

cat > "$scratch/bin/dotnet" <<'DOTNET'
#!/usr/bin/env bash
if [ "$1" != build ]; then
    exit 2
fi
sleep 30 >/dev/null 2>&1 &
printf '%s\n' "$!" > "$RT_FAKE_CHILD_FILE"
if [ "${RT_FAKE_BUILD_FAIL:-0}" = 1 ]; then
    exit 1
fi
mkdir -p "$(dirname "$RT_FAKE_EXE")"
cp "$RT_FAKE_RUNNER" "$RT_FAKE_EXE"
chmod +x "$RT_FAKE_EXE"
DOTNET
chmod +x "$scratch/bin/dotnet"
cat > "$scratch/bin/dotnet-suggest" <<'SUGGEST'
#!/usr/bin/env bash
exit 0
SUGGEST
chmod +x "$scratch/bin/dotnet-suggest"

export PATH="$scratch/bin:$PATH"
export TMPDIR="$scratch"
export RT_TMP_DIR="$scratch/rt-meta"
export RT_FAKE_CHILD_FILE="$child_file"
export RT_FAKE_RUNNER="$scratch/runner"
export RT_FAKE_EXE="$fixture/src/RoslynTools.Cli/bin/Release/net10.0/rt"

output="$(bash "$fixture/rt.sh" probe)"
[ "$output" = 'fixture rt invoked' ]
kill -0 "$(cat "$child_file")"
if ! flock -n "$scratch/rt-rebuild.lock" true; then
    echo "wrapper-lock-test: a build child retained the rebuild lock after rt exited" >&2
    exit 1
fi

failure_fixture="$scratch/failing-checkout"
mkdir -p "$failure_fixture/.claude/scripts" "$failure_fixture/src/RoslynTools.Cli"
cp "$repo_root/rt.sh" "$failure_fixture/rt.sh"
cp "$repo_root/.claude/scripts/rt-paths.sh" "$failure_fixture/.claude/scripts/rt-paths.sh"
touch "$failure_fixture/src/RoslynTools.Cli/RoslynTools.Cli.csproj"
export RT_FAKE_CHILD_FILE="$scratch/failure-child.pid"
export RT_FAKE_BUILD_FAIL=1
if bash "$failure_fixture/rt.sh" probe >/dev/null 2>&1; then
    echo "wrapper-lock-test: expected the failed build to stop rt" >&2
    exit 1
fi
kill -0 "$(cat "$RT_FAKE_CHILD_FILE")"
if ! flock -n "$scratch/rt-rebuild.lock" true; then
    echo "wrapper-lock-test: a failed build child retained the rebuild lock" >&2
    exit 1
fi

echo "wrapper-lock-test: OK" >&2
