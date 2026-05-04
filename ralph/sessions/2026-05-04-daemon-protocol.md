# Session: 2026-05-04 — Daemon Protocol

**Goal:** Design the rt daemon: workspace caching, git-state invalidation, and composable pipe chains.
**Solution:** RoslynTools.slnx

---

## Design constraints established before simulation

1. **Single global daemon.** Exactly one `rt` daemon process at a time, machine-wide. Not per-solution.
2. **Cache key:** `solutionPath + gitHeadSha + (dirty ? gitIndexMtime : "clean")`. Sent by client on every request; daemon reloads if any field mismatches (including solution path change).
3. **Git is the invalidation oracle.** No file watcher. Works because `.rt-solution` is checked in — its changes appear in git state automatically.
4. **State lives in `~/.rt/`** — single `daemon.json`, no per-solution subdirectories, no repo pollution.
5. **Transport: named pipe** (`\\.\pipe\rt-daemon` on Windows, fixed name). One pipe, one daemon.
6. **Protocol: newline-delimited JSON** (request → streaming responses → exit).
7. **Auto-start:** if no daemon is running when `rt` is invoked, start one in background and wait for it to signal ready. Transparent to the user.
8. **Opt-out:** `RT_NO_DAEMON=1` or `rt --no-daemon` falls back to in-process (current behaviour).

---

## Wire format

### Client → Daemon (one JSON line per request)

```json
{
  "requestId": "a1b2c3d4",
  "args": ["inspect", "find-refs", "--symbol", "WorkspaceLoader", "--solution", "RoslynTools.slnx"],
  "gitState": {
    "head": "8f44e7f3a",
    "dirty": false,
    "indexMtime": null
  },
  "stdin": null
}
```

`stdin` is base64-encoded bytes when the client is itself receiving piped input (enables `rt find-refs Foo | rt rename Bar` chains through the daemon).

### Daemon → Client (streaming, newline-delimited)

```json
{ "requestId": "a1b2c3d4", "type": "stderr", "data": "Finding references to 'RoslynTools.Core.WorkspaceLoader'...\n" }
{ "requestId": "a1b2c3d4", "type": "stdout", "data": "{\"symbol\":\"RoslynTools.Core.WorkspaceLoader\",\"references\":[...]}\n" }
{ "requestId": "a1b2c3d4", "type": "exit", "code": 0 }
```

Types: `stderr` | `stdout` | `exit`. Client forwards stderr → its own stderr, stdout → its own stdout, then exits with `code`.

### Daemon state file: `~/.rt/daemon.json`

```json
{
  "pid": 18432,
  "pipeName": "rt-daemon",
  "solution": "C:/code/roslyn-tools/RoslynTools.slnx",
  "cachedHead": "8f44e7f3a",
  "cachedDirty": false,
  "cachedIndexMtime": null,
  "startedAt": "2026-05-04T09:45:00Z",
  "rtVersion": "0.9.0"
}
```

---

## Step 1 — rt daemon start [SIMULATED]

```
rt daemon start --solution RoslynTools.slnx
```

```
Starting daemon for RoslynTools.slnx...
Loading workspace (first load may take a few seconds)...
  Loaded 3 projects, 0 diagnostics.
Daemon ready. PID 18432, pipe: rt-a9f3c17b
State written to C:\Users\DanMarshall\.rt\daemon.json
```

Exit 0. Subsequent `rt` invocations in this directory will connect to this daemon transparently.

---

## Step 2 — rt daemon status [SIMULATED]

```
rt daemon status
```

```
Daemon: RUNNING (PID 18432)
Solution: C:\code\roslyn-tools\RoslynTools.slnx
Cache:    HEAD=8f44e7f3a  dirty=false  (clean)
Uptime:   0h 2m 14s
Pipe:     \\.\pipe\rt-daemon
```

Exit 0.

---

## Step 3 — rt inspect find-refs via daemon [REAL, but currently in-process]

```
rt inspect find-refs --symbol WorkspaceLoader --solution RoslynTools.slnx
```

**Current (in-process) timing:** ~3.8s (workspace load + query)

**Expected via daemon (workspace already cached):**
```
Finding references to 'RoslynTools.Core.WorkspaceLoader'...
```
```json
{
  "symbol": "RoslynTools.Core.WorkspaceLoader",
  "fullyQualifiedName": "RoslynTools.Core.WorkspaceLoader",
  "references": [
    { "file": "src/RoslynTools.Cli/Program.cs", "line": 1, "column": 5, "snippet": "using RoslynTools.Core;" },
    { "file": "src/RoslynTools.Cli/Bootstrap.cs", "line": 12, "column": 9, "snippet": "services.AddSingleton<WorkspaceLoader>();" }
  ],
  "count": 2
}
```

**Expected timing via daemon:** ~200ms (no workspace load, just query).

---

## Step 4 — pipe chain: find-refs → rename [SIMULATED]

```
rt inspect find-refs --symbol WorkspaceLoader --json | rt refactor rename --new-name SolutionLoader --dry-run
```

The second command reads the symbol stream from stdin when stdin is not a TTY. It extracts the `fullyQualifiedName` field and uses it as the rename target — no `--symbol` arg needed.

```
[daemon] Renaming 'RoslynTools.Core.WorkspaceLoader' → 'SolutionLoader'...
```
```diff
--- a/src/RoslynTools/Core/WorkspaceLoader.cs
+++ b/src/RoslynTools/Core/WorkspaceLoader.cs
@@ -8,7 +8,7 @@ namespace RoslynTools.Core;
-internal sealed class WorkspaceLoader
+internal sealed class SolutionLoader
 {
--- a/src/RoslynTools.Cli/Program.cs
+++ b/src/RoslynTools.Cli/Program.cs
@@ -1,5 +1,5 @@
-using RoslynTools.Core; // WorkspaceLoader
+using RoslynTools.Core; // SolutionLoader
```

> **[ISSUE: rt-008]** written — `rename` command must accept symbol FQN from stdin JSON when piped; currently only accepts positional `<symbol>` argument.

---

## Step 5 — git commit invalidates cache [SIMULATED]

User commits a change. Next `rt` command sends new HEAD sha.

```
rt inspect find-refs --symbol WorkspaceLoader --solution RoslynTools.slnx
```

Daemon receives: `"head": "c7e90a1f"` (new) vs cached `"head": "8f44e7f3a"`.

```
[daemon] Git state changed (HEAD: 8f44e7f3a → c7e90a1f). Reloading workspace...
  Loaded 3 projects, 0 diagnostics.
Finding references to 'RoslynTools.Core.WorkspaceLoader'...
```

Reload is ~3.8s; subsequent commands in same HEAD are fast again.

---

## Step 6 — dirty working tree [SIMULATED]

User edits a file without committing.

```
rt inspect find-refs --symbol WorkspaceLoader
```

Client sends: `"dirty": true, "indexMtime": 1746368400`.

If `indexMtime` matches daemon's cached value → still cache-hit (same edits).
If `indexMtime` differs → daemon reloads.

```
[daemon] Working tree changed (index mtime updated). Reloading workspace...
```

---

## Step 7 — rt daemon stop [SIMULATED]

```
rt daemon stop
```

```
Stopping daemon (PID 18432)...
Daemon stopped. State removed from C:\Users\DanMarshall\.rt\daemon.json
```

Exit 0. Subsequent `rt` commands fall back to in-process (or auto-start a new daemon).

---

## Step 8 — version mismatch (rt binary rebuilt) [SIMULATED]

User rebuilds `rt` after a code change. Client and daemon have different `rtVersion`.

```
rt inspect find-refs --symbol WorkspaceLoader
```

```
[daemon] Version mismatch (daemon: 0.9.0, client: 0.10.0). Restarting daemon...
Starting daemon for RoslynTools.slnx...
  Loaded 3 projects, 0 diagnostics.
Daemon ready. PID 19110, pipe: rt-a9f3c17b
Finding references to 'RoslynTools.Core.WorkspaceLoader'...
```

Version is embedded in the binary at build time (e.g. from `git describe`). Mismatch → daemon kills itself, client starts a new one.

---

## Retrospective

### Real commands used
- `rt inspect find-refs` (Step 3 — run in-process to establish baseline timing)

### Simulated commands → rt candidates

| Command | Value | Feasibility |
|---|---|---|
| `rt daemon start` | Core lifecycle | High — named pipe + background process |
| `rt daemon stop` | Core lifecycle | High |
| `rt daemon status` | Observability | High |
| `rt refactor rename` (stdin) | Pipe chains | Medium — needs stdin reader in rename handler |

### Architecture decisions confirmed

- **Transport:** Named pipe, one per solution hash. Cross-platform: named pipes on Windows, Unix domain sockets on Linux/Mac (same abstraction via `System.IO.Pipes`).
- **State dir:** `~/.rt/` — single `daemon.json`, no repo impact, no per-solution subdirectories.
- **Invalidation:** Client sends git state on every request. Daemon reloads on mismatch. No file watchers.
- **Pipe chains:** stdin JSON consumed when `!Console.IsInputRedirected` is false. Commands extract `fullyQualifiedName` from stream.
- **Version guard:** `rtVersion` in `daemon.json` compared to client version. Mismatch triggers daemon restart.
- **Fallback:** `RT_NO_DAEMON=1` or `--no-daemon` for in-process execution (tests, CI).

### Gaps logged

- 2026-05-04: `rename` must accept piped stdin JSON — see rt-008
- 2026-05-04: `rt daemon start/stop/status` commands do not exist
- 2026-05-04: Client has no IPC layer — currently always runs in-process

### Implementation order

1. **DaemonClient** — connect to named pipe, send request, stream responses to stdout/stderr
2. **DaemonServer** — named pipe listener, request deserialiser, workspace cache with git-state check
3. **`rt daemon` subcommands** — start, stop, status (thin wrappers over DaemonServer lifecycle)
4. **Auto-start** — if no daemon.json or pipe is dead, start daemon and wait for ready signal
5. **Stdin pipe reader** — make mutation commands accept `fullyQualifiedName` from stdin JSON (rt-008)

### Next session
- Implement DaemonServer: named pipe listener + workspace cache
- Implement DaemonClient: pipe connection + request/response streaming
- Wire auto-start into Program.Main (between MsBuildBootstrap and App.RunAsync)
