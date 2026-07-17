#!/usr/bin/env bash
# PreToolUse hook on Read|Edit|Write. For .cs files, deny unless `rt inspect`
# has recently mentioned the file's basename — forces inspect-first discipline.
# To bypass: run `rt inspect outline <file> --solution <slnx>` first, OR
# `rt inspect describe <FQN>` for the symbol (basename match is enough).
set -e

input=$(cat)

# Parse the payload. FB-366: on Windows the harness sends file paths with raw
# single backslashes (e.g. "c:\code\...") — invalid JSON escapes that make jq
# abort. Under `set -e` that aborted the gate and surfaced as a spurious deny.
# So: try jq on the payload as-is (the common, valid case); only if that fails,
# escape every backslash to "\\" and retry, recovering the path on Windows.
parse_field()
{
  local query="$1" out
  if out=$(printf '%s' "$input" | jq -er "$query" 2>/dev/null); then
    printf '%s' "$out"
    return 0
  fi
  printf '%s' "$input" | sed 's/\\/\\\\/g' | jq -r "$query // \"\"" 2>/dev/null || true
}

fp=$(parse_field '.tool_input.file_path // ""')
tool_name=$(parse_field '.tool_name // ""')

# Non-.cs files: silent pass.
if [[ "$fp" != *.cs ]]; then
  exit 0
fi

# Strip the directory (handle BOTH / and \ separators — Windows tool paths use
# backslashes, and `basename` only splits on /), then the .cs suffix, so we match
# on the bare filename. Matching the bare name (not the full path) makes the gate
# independent of drive-letter / path case (FB-364/FB-365).
fname="${fp##*/}"       # strip up to the last forward slash
fname="${fname##*\\}"   # strip up to the last backslash
basename="${fname%.cs}"

# shellcheck source=rt-paths.sh
. "$(dirname "${BASH_SOURCE[0]}")/rt-paths.sh"
log="$(rt_inspected_log)"

# Bypass if rt build is known broken. The rt wrappers write the status marker
# on every invocation: 'ok' on success, 'broken' on rebuild failure. Without
# this bypass, a broken build would lock the agent out of the very files it
# needs to read to fix the build.
status_marker="$(rt_build_status_marker)"
if [[ -f "$status_marker" ]] && [[ "$(cat "$status_marker" 2>/dev/null)" == "broken" ]]; then
  jq -nc --arg msg "rt-first gate bypassed: rt build is broken (per $status_marker). Fix the build, then run any rt command to clear the marker." \
    '{hookSpecificOutput: {hookEventName: "PreToolUse", additionalContext: $msg}}'
  exit 0
fi

# Write: always allow, and log the basename so subsequent Edits pass the gate.
if [[ "$tool_name" == "Write" ]]; then
  mkdir -p "$(dirname "$log")" 2>/dev/null || true
  echo "$basename" >> "$log"
  exit 0
fi

# If the basename appears in the last 50 logged rt-inspect commands, allow with
# reminder. FB-366: the inspect log can contain large pasted blobs (e.g. PRD JSON)
# whose bytes make some `grep` builds treat the piped stream as binary and
# silently skip line matches (observed live: a present basename matched 0 times).
# Do the case-insensitive substring search in awk under a byte-wise C locale so
# the decision never depends on grep's binary-detection heuristics.
if [[ -f "$log" ]] && tail -n 50 "$log" 2>/dev/null \
     | LC_ALL=C awk -v needle="$basename" '
         BEGIN { n = tolower(needle); found = 0 }
         { if (index(tolower($0), n) > 0) { found = 1 } }
         END { exit(found ? 0 : 1) }'; then
  jq -nc --arg msg "rt-first reminder: prefer rt for C# files. inspect outline <file> for structure, inspect describe <FQN> for symbols, inspect find-refs --symbol X for callers, refactor rename/move/extract-method/inline-method for mutations. If rt has a gap, log via /task to ralph/backlog-prd.json before falling back to manual Read/Edit." \
    '{hookSpecificOutput: {hookEventName: "PreToolUse", additionalContext: $msg}}'
  exit 0
fi

# Not inspected — deny.
jq -nc --arg fp "$fp" --arg bn "$basename" \
  '{hookSpecificOutput: {hookEventName: "PreToolUse", permissionDecision: "deny", permissionDecisionReason: ("rt-first gate: " + $fp + " has not been inspected via rt in this session. Run `rt inspect outline " + $fp + " --solution <slnx>` (or `rt inspect describe <FQN>` referencing " + $bn + ") first, then retry. To bypass intentionally, the user can approve this Read in the next prompt.")}}'

exit 0
