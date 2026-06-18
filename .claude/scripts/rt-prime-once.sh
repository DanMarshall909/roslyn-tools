#!/bin/bash
# Runs rt prime --json once per Claude Code session (guarded by PPID-stamped marker).
# Output is picked up by the harness and injected as session context.
MARKER="/tmp/rt-prime-${PPID}"
if [ -f "$MARKER" ]; then
    exit 0
fi
touch "$MARKER"
rt prime --json 2>/dev/null
