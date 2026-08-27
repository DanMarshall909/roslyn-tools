# EP-013 — Command Consistency Roadmap

This is the execution plan for [EP-013](../ralph/backlog-prd.json). The epic owns no stories directly; it sequences existing work under EP-007, EP-009, EP-011, and EP-012 into a coherent landing. The target architecture behind this roadmap is [target-command-surface-architecture.md](target-command-surface-architecture.md).

> **Status (2026-08-27):** Phase 1 has landed. ADR-0003 replaced the native-handler direction with generic descriptors and typed application ports; ADR-0007 defines the full 56-command fan-out and physical host decoupling. The sequencing below reflects those decisions.

## Strategic outcome

When EP-013 closes:

1. **Half the universal [per-command checklist](per-command-checklist.md) is compiler- or architecture-test-enforced.** What survives on the manual list is the genuinely-human content (good `Examples`, accurate `SeeAlso`, behaviour matching the category-implied semantics).
2. **Every ordinary command has one host-neutral definition and typed application port.** Command-specific MCP handlers and the CLI compatibility bridge disappear.
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
| **US-097** | EP-012 | Finalize shared review/dry-run/apply/no-op mutation envelopes and safe daemon retry semantics. |

### Phase 3 — Host-neutral application foundation *(after Phase 2)*

Land the generic contract **before** bulk conversion so fan-out agents do not edit central registries or invent incompatible lifecycle behavior.

| Story | Owner epic | What it lands |
|---|---|---|
| **US-110** | EP-012 | Generic MCP adapter returns `structuredContent` + text fallback and derives `outputSchema` from typed results. |
| **US-143** | EP-012 | Host-neutral definitions, automatic discovery, definition/port/schema/execution-policy ratchets, shared daemon recovery, mutation envelope, and 56-command manifest. US-094 is superseded. |

### Phase 4 — Application-port rollout *(parallel after Phase 3)*

Groups run in parallel using the [application-port playbook](command-application-port-migration.md) and its machine-checkable manifest.

| Story | Owner epic | Commands |
|---|---|---|
| **US-095** | EP-012 | Inspect groups R0, R1, R4, and the inspect members of R2. |
| **US-096** | EP-012 | Analyze groups R0, R2, R3, R4, and R5. |
| **US-097** | EP-012 | Mutation groups M0–M4. |
| **US-104/105** | EP-012 | `change-signature` and `move` schema bugs close through their typed requests, not handlers. |
| **US-106** | EP-012 | Align shared definition/tool identity for `suggest`. |

### Phase 5 — Bridge deletion and physical host decoupling *(serial after Phase 4)*

| Story | Owner epic | What it lands |
|---|---|---|
| **US-098** | EP-012 | Delete `CliCommandBridge`, move shared registration/definitions out of CLI, and remove `RoslynTools.Mcp → RoslynTools.Cli`. |
| **US-099** | EP-012 | Consolidated into US-098; do not move CLI builders or the bridge into a renamed shared project. |

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
Phase 3:       ADR-0007 / US-143 foundation ─ US-110
                                       |
Phase 4:      R0-R5 read-only groups | M0-M4 mutation groups
                           (parallel fan-out)

Phase 5:  bridge deletion → US-098 physical decoupling
Phase 6:  US-084, US-085, US-114, US-086, US-088, US-089, US-093, US-103
          (parallel, ratcheted alongside Phase 4 commands)
```

## Done definition for EP-013

- Every story listed in Phases 1–6 is closed.
- Architecture tests enforce category/metadata parity, exactly one definition and port per ordinary command, request/schema parity, execution/safety validity, and no host-to-host project reference.
- `docs/per-command-checklist.md` is shrunk: every row labelled `(enforced)` has been deleted, not just struck through. What remains is the genuinely-human list.
- A manual MCP handshake shows every visible application definition with input/output schemas; mutation responses expose review/dry-run/applied/no-op; `tools/call` returns structured data plus text fallback.

## Reference

Pattern reference for every Phase-4 story: [ADR-0007](adr/0007-host-neutral-command-contracts.md), [the migration playbook](command-application-port-migration.md), and the [56-command manifest](command-port-migration-groups.tsv).
