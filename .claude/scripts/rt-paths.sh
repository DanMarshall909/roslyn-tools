#!/usr/bin/env bash
# Single source of truth for the scratch paths shared by the rt-first gate,
# the inspect log, and the rt wrapper's build-status marker.
#
# These paths were previously hardcoded as /c/temp/... in three places. That is
# a Git-Bash-on-Windows path: on Linux `mkdir -p /c/temp` fails at the root, so
# every write silently no-oped. The consequences compounded — rt-inspect-log.sh
# could not record an inspect (so the gate could never be satisfied) and
# rt-first-gate.sh could not see the 'broken' marker (so the broken-build bypass
# could never fire). Net effect on Linux: every .cs Read/Edit denied forever,
# including the files needed to repair the build.
#
# Resolution order keeps existing Windows checkouts working unchanged:
#   1. $RT_TMP_DIR                     — explicit override
#   2. /c/temp                         — pre-existing Git-Bash layout, if present
#   3. ${TMPDIR:-/tmp}/rt              — portable default (POSIX)

rt_tmp_dir() {
    if [ -n "${RT_TMP_DIR:-}" ]; then
        printf '%s' "$RT_TMP_DIR"
    elif [ -d /c/temp ]; then
        printf '%s' /c/temp
    else
        printf '%s' "${TMPDIR:-/tmp}/rt"
    fi
}

# Callers override these individually (the hook tests do); otherwise they derive
# from the resolved temp dir.
rt_inspected_log() {
    printf '%s' "${RT_INSPECTED_LOG:-$(rt_tmp_dir)/rt-inspected.log}"
}

rt_build_status_marker() {
    printf '%s' "${RT_BUILD_STATUS_MARKER:-$(rt_tmp_dir)/rt-build-status}"
}
