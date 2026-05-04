# Stage 1 Plan — Daemon command routing

**Goal:** Route arbitrary rt commands through the daemon via App.RunAsync.
No workspace caching yet — just eliminates per-command process startup (~500ms).

## What changes

### DaemonServer.cs
- Call MsBuildBootstrap.Register() + build DI host ONCE at daemon start (before pipe loop)
- In HandleRequestAsync: replace the "not yet implemented" branch with:
  1. Create DaemonPipeConsole(writer, requestId) — captures RtConsole output to pipe
  2. using RtConsole.UseConsole(pipeConsole) { exitCode = await App.RunAsync(request.Args, services) }
  3. Send DaemonResponse(requestId, "exit", Code: exitCode)

### New file: DaemonPipeConsole.cs
- Implements IRtConsole
- WriteLine(context, message): if context == Diagnostic → type "stderr", else → type "stdout"
- Write(context, message): same routing, no newline appended
- ReadLine(): return null (non-interactive daemon)
- IsOutputRedirected: true

### Program.cs
- --daemon-serve path: call MsBuildBootstrap.Register() before DaemonServer.RunAsync()
- Build and pass IServiceProvider to DaemonServer.RunAsync

### DaemonServer.cs signature
- RunAsync(IServiceProvider services) — receives pre-built DI container

## What does NOT change
- DaemonClient.cs — wire format is identical
- DaemonProtocol.cs — no changes
- DaemonState.cs — no changes
- No workspace caching yet (Stage 2)

## Key constraints
- MsBuildBootstrap.Register() must run before any Roslyn types load — call it in --daemon-serve path before DaemonServer.RunAsync
- Single-threaded request loop — one App.RunAsync at a time (Roslyn workspace not thread-safe)
- App.RunAsync must not call Environment.Exit — verify or wrap

## Verification
- rt daemon start
- rt find-refs --symbol WorkspaceLoader → served from daemon, ~200ms vs ~3.8s
- rt daemon stop
- All existing tests pass
