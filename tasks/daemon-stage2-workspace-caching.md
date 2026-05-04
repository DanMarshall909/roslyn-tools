# Daemon Stage 2 — Workspace Caching

**Priority: High**

## Goal

Eliminate the ~3.8s workspace load on every `rt` command by caching the `Solution` inside the daemon process. After Stage 2, only the first command (or a git-state change) pays the load cost; subsequent commands respond in ~200ms.

## Why it's blocked

Stage 1 routes commands through `App.RunAsync` in the daemon process — MSBuild/DI is already initialised once. But every command still calls `WorkspaceLoader.LoadAsync(solution)` directly, creating a new `MSBuildWorkspace` and loading all projects on each invocation. Caching requires `WorkspaceLoader` to be an injectable singleton that holds the loaded `Solution` between requests.

**This is why the static-class refactoring must come first.** As long as `WorkspaceLoader` is a static class with no instance state, there is nowhere to store the cached solution.

## Invalidation key

Cache hit if all three match against the daemon's last-loaded state:
- `solutionPath` — absolute path sent by the client
- `gitState.head` — current HEAD sha
- `gitState.dirty + gitState.indexMtime` — if dirty, the `.git/index` mtime

On mismatch: reload, update `DaemonStateFile` fields (`Solution`, `CachedHead`, `CachedDirty`, `CachedIndexMtime`).

## Implementation sketch (after DI refactoring)

```csharp
// Bootstrap.cs
services.AddSingleton<WorkspaceLoader>();   // holds cached Solution

// WorkspaceLoader (instance)
internal sealed class WorkspaceLoader
{
    private Solution? _cached;
    private string? _cachedSolution;
    private string? _cachedHead;
    private bool _cachedDirty;
    private long? _cachedIndexMtime;

    public async Task<(MSBuildWorkspace, Solution)> LoadAsync(
        string? solutionPath, GitState? gitState = null)
    {
        if (IsCacheHit(solutionPath, gitState))
            return _cached!;
        // reload ...
    }
}
```

Commands receive `WorkspaceLoader` via DI; the daemon's singleton retains state across requests.

## Steps

1. Convert `WorkspaceLoader` to an instance class → register as singleton in `Bootstrap`
2. Convert `SymbolLocator` to an instance class (depends on `WorkspaceLoader`)
3. Convert `SolutionWriter` to an instance class
4. Convert service classes to instance classes and register in `Bootstrap`
5. Update commands to resolve services via `IServiceProvider` (already threaded through `App.RunAsync`)
6. Add git-state cache key comparison to `WorkspaceLoader.LoadAsync`
7. Propagate `GitState` from the daemon request into `WorkspaceLoader` calls

## Success criteria

- `rt inspect find-refs --symbol WorkspaceLoader` second invocation (same HEAD, no edits): < 300ms
- Cache invalidates correctly on `git commit` (new HEAD) and on file edit (changed index mtime)
- All existing tests pass
- In-process path (`RT_NO_DAEMON=1`) is unaffected — `WorkspaceLoader` reloads each time (no git state)
