#!/usr/bin/env bash
# PreToolUse hook on Bash. If the command is `rt inspect ...`, log it so the
# Read/Edit gate can later allow Read/Edit on .cs files referenced.
set -e

input=$(cat)
cmd=$(echo "$input" | jq -r '.tool_input.command // ""')

# Match: `rt inspect`, `./rt.cmd inspect`, `rt.ps1 inspect`, etc.
if echo "$cmd" | grep -qE '(^|[^a-zA-Z./])(\./)?rt(\.(cmd|sh|ps1))?[[:space:]]+inspect[[:space:]]'; then
  mkdir -p /c/temp 2>/dev/null
  echo "$(date +%s) $cmd" >> /c/temp/rt-inspected.log
fi

exit 0
