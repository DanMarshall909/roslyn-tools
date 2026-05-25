# EP-013 — Command Consistency Roadmap

This is the execution plan for [EP-013](../ralph/backlog-prd.json). The epic owns no stories directly; it sequences existing work under EP-007, EP-009, EP-011, and EP-012 into a coherent landing. The target architecture behind this roadmap is [target-command-surface-architecture.md](target-command-surface-architecture.md).

## Strategic outcome

When EP-013 closes:

1. **Half the universal [per-command checklist](per-command-checklist.md) is compiler- or architecture-test-enforced.** What survives on the manual list is the genuinely-human content (good `Examples`, accurate `SeeAlso`, behaviour matching the category-implied semantics).
2. **The per-command pending-work matrix collapses.** Most "native MCP handler" rows resolve in a single inspect/analyze pass.
3. **MCP callers get typed responses with declared output schemas** — modern `CallToolResult` shape (`structuredContent`, `outputSchema` per tool in `tools/list`).
4. **CLI callers get format consistency across all commands** — `--format json|yaml|text` works uniformly, including for refactor/mutating commands.

## Sequencing

Phases gate on dependencies, not calendar time. Within a phase, stories run in parallel.

### Phase 1 — Foundation *(parallel)*

The two foundational pieces every later phase builds on. They don't depend on each other; ship in parallel.

| Story | Owner epic | What it lands |
|---|---|---|
| **US-100** | EP-009 | `RtResultRenderer` has one canonical method; `IStructuredOutputCommand` biconditional with `Info.JsonOutput`; startup validator. |
| **US-107** | EP-009 | `IInspectCommand` / `IAnalyzeCommand` / `IRefactorCommand` category markers; `IMutatingCommand` / `IStructuredOutputCommand` / `IBatchMutationCommand` / `INativeMcpExposable` capability markers; architecture tests assert the category↔`Info` biconditional. |

### Phase 2 — Apply foundation *(parallel after Phase 1)*

| Story | Owner epic | What it lands |
|---|---|---|
| **US-108** | EP-009 | Delete `RtCommandInfo.Category` and `Info.Mutates`; both are derived from the interface now. |
| **US-109** | EP-009 | Extend `--format` pipeline to refactor/mutating commands; result type implementing `IRtOutput` for every command; `RtResultRenderer` is the sole stdout path. |
| **US-097** | EP-012 | Design decision — what shape does a mutating native MCP handler return? `dry-run`-default-true policy; structured change-summary JSON. Unblocks Phase 4 mutating handlers. |

### Phase 3 — MCP modernisation *(after Phase 2)*

Land the protocol shape **before** the bulk of handler conversions, so handlers are written against the new shape and don't need retrofitting.

| Story | Owner epic | What it lands |
|---|---|---|
| **US-110** | EP-012 | Audit current wire format; uplift `protocolVersion` from `2024-11-05` if appropriate; native handlers return `structuredContent` + text fallback; `outputSchema` per tool in `tools/list`. |
| **US-094** | EP-012 | Architectural ratchet test: every `IInspectCommand`/`IAnalyzeCommand` has a registered `IMcpHandler` whose `ToolName` matches the CLI command name. Lock the contract before the bulk rollout. |

### Phase 4 — Native MCP handler rollout *(parallel after Phase 3)*

All five run in parallel. Each follows the canonical pattern from commit `6d767b1` and the [per-command checklist](per-command-checklist.md).

| Story | Owner epic | Commands |
|---|---|---|
| **US-095** | EP-012 | `describe`, `find-callers`, `find-impl`, `find-pattern`, `find-unused` |
| **US-096** | EP-012 | `analyze-interface`, `circular-deps`, `complexity`, `dependency-graph`, `find-duplicates`, `find-magic-strings`, `find-undocumented`, `list-imports`, `list-smells`, `static-class-inventory` |
| **US-104** | EP-012 | `change-signature` (schema-bug fix; native handler is the preferred resolution) |
| **US-105** | EP-012 | `move` (schema-bug fix; native handler is the preferred resolution) |
| **US-106** | EP-012 | `suggest` naming alignment (`ToolName` from `suggest_command` to `suggest`) |

### Phase 5 — Project structure *(independent track, can overlap with any phase)*

| Story | Owner epic | What it lands |
|---|---|---|
| **US-099** | EP-012 | Extract `IRtCommand` registry into a new `RoslynTools.Commands` project. |
| **US-098** | EP-012 | Drop the `RoslynTools.Mcp → RoslynTools.Cli` project reference (depends on US-099). |

### Phase 6 — Universal metadata *(any time; each is a small per-command ratchet)*

These are all "every command must have X" rules. They land alongside each command pass in Phases 4–5, ratcheting up coverage as they go. No hard ordering between them.

| Story | Owner epic | What it lands |
|---|---|---|
| **US-084** | EP-009 | Metadata catalog: `RtCommandInfo` schema + machine-readable `dump-help`. |
| **US-085** | EP-009 | Original `CommandOptionFactory` standardization story; should be reshaped by US-114 rather than pursued as a factory-only migration. |
| **US-114** | EP-009 | Reusable flag capability interfaces + central option registry/binders; supersedes the factory-only direction where possible. |
| **US-086** | EP-009 | Command file structure convention + architecture ratchet. |
| **US-088** | EP-011 | `RtCommandInfo.SeeAlso` schema + startup validator. |
| **US-089** | EP-011 | Seed `SeeAlso` data across the command catalog. |
| **US-093** | (none) | Exit codes via `System.CommandLine` middleware. |
| **US-103** | (none) | No parameterless service constructors with hand-rolled concrete deps. |

## Dependency graph (compact)

```
Phase 1:  US-100  US-107
                \  /  \____________
                 \/                 \
Phase 2:        US-108  US-109     US-097
                          \           |
Phase 3:                  US-110 ──── US-094
                                       |
Phase 4:    US-095   US-096   US-104   US-105   US-106
                          (all parallel)

Phase 5:  US-099 → US-098     (independent)
Phase 6:  US-084, US-085, US-114, US-086, US-088, US-089, US-093, US-103
          (parallel, ratcheted alongside Phase 4 commands)
```

## Done definition for EP-013

- Every story listed in Phases 1–6 is closed.
- `tests/RoslynTools.Tests/Architecture/` contains green ratchet tests for: category↔`Info` biconditional (US-107), JSON-output↔`IStructuredOutputCommand` biconditional (US-100), every inspect/analyze command has a native `IMcpHandler` (US-094), `Info.Category` and `Info.Mutates` fields no longer exist on `RtCommandInfo` (US-108).
- `docs/per-command-checklist.md` is shrunk: every row labelled `(enforced)` has been deleted, not just struck through. What remains is the genuinely-human list.
- A manual MCP handshake against the rebuilt `RoslynTools.Mcp.exe` shows: 53+ tools, every native handler declares an `outputSchema`, mutating-handler responses include `dryRun` and `applied` fields, `tools/list` returns `structuredContent` + text-fallback `content[]` on `tools/call`.

## Reference

Pattern reference for every Phase-4 story: commit `6d767b1` (`feat(mcp): wire find_refs and rename as native MCP handlers`). Canonical files: `FindReferencesHandler.cs`, `RenameSymbolHandler.cs`, `OutlineHandler.cs`.
