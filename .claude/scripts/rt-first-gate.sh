#!/usr/bin/env bash
# PreToolUse hook on Read|Edit|Write. For .cs files, deny unless `rt inspect`
# has recently mentioned the file's basename — forces inspect-first discipline.
# To bypass: run `rt inspect outline <file> --solution <slnx>` first, OR
# `rt inspect describe <FQN>` for the symbol (basename match is enough).
set -e

input=$(cat)
fp=$(echo "$input" | jq -r '.tool_input.file_path // ""')
tool_name=$(echo "$input" | jq -r '.tool_name // ""')

# Non-.cs files: silent pass.
if [[ "$fp" != *.cs ]]; then
  exit 0
fi

basename=$(basename "$fp" .cs)
log=/c/temp/rt-inspected.log

# Write: always allow, and log the basename so subsequent Edits pass the gate.
if [[ "$tool_name" == "Write" ]]; then
  mkdir -p /c/temp
  echo "$basename" >> "$log"
  exit 0
fi

# If the basename appears in the last 50 logged rt-inspect commands, allow with reminder.
if [[ -f "$log" ]] && tail -n 50 "$log" 2>/dev/null | grep -qF "$basename"; then
  jq -nc --arg msg "rt-first reminder: prefer rt for C# files. inspect outline <file> for structure, inspect describe <FQN> for symbols, inspect find-refs --symbol X for callers, refactor rename/move/extract-method/inline-method for mutations. If rt has a gap, log via /task to ralph/backlog-prd.json before falling back to manual Read/Edit." \
    '{hookSpecificOutput: {hookEventName: "PreToolUse", additionalContext: $msg}}'
  exit 0
fi

# Not inspected — deny.
jq -nc --arg fp "$fp" --arg bn "$basename" \
  '{hookSpecificOutput: {hookEventName: "PreToolUse", permissionDecision: "deny", permissionDecisionReason: ("rt-first gate: " + $fp + " has not been inspected via rt in this session. Run `rt inspect outline " + $fp + " --solution <slnx>` (or `rt inspect describe <FQN>` referencing " + $bn + ") first, then retry. To bypass intentionally, the user can approve this Read in the next prompt.")}}'

exit 0
