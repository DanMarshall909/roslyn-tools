---
tags:
  - architecture
  - daemon
  - linux
ticket: rt-014
severity: High
status: Open
area: architecture
owner: ""
reported: 2026-08-26
summary: "rt daemon start reports ready on Linux, but the spawned daemon exits with its launcher"
---

# Keep a manually started daemon alive on Linux

## Context

During the command-port migration design session, the published Release apphost was
used to start the daemon:

```text
$ src/RoslynTools.Cli/bin/Release/net10.0/rt daemon start
[rt-daemon] PID 1618970 listening on /tmp/CoreFxPipe_rt-daemon
Daemon ready. PID 1618970
```

The launcher exited successfully, but the daemon process disappeared immediately.
`rt daemon status` then reported `STOPPED (stale)`, and a typed `outline` invocation
reported that the daemon was unavailable. The daemon log contains `daemon ready` but
no clean `daemon stopped` entry.

This contradicts the manual lifecycle contract in ADR-0006: `rt daemon start` should
remain resident until `rt daemon stop`.

## Acceptance Criteria

- [ ] On Linux, `rt daemon start` returns after readiness while the spawned daemon remains alive.
- [ ] A subsequent typed CLI command connects to that daemon successfully.
- [ ] `rt daemon stop` terminates it and removes `~/.rt/daemon.json`.
- [ ] The spawned process is detached without sending daemon stdout or stderr into command result output.
- [ ] A process-level regression test covers start, launcher exit, command invocation, and stop.
