# Architecture overview

This is the umbrella description of the `rt` target architecture. It links the per-concern docs together and names the contracts, output and error pipelines, the catalog model, the safety net, and the dogfood loop. Update it whenever an architectural decision is taken (alongside an ADR under [adr/](adr/) — see [the rule](#updating-this-doc)).

Companion documents:

- [pattern-core-service.md](pattern-core-service.md) — the canonical Core-service + thin-host-adapters pattern with worked examples.
- [target-command-surface-architecture.md](target-command-surface-architecture.md) — target shape for command wiring (catalog, capabilities, binders).
- [consistency-roadmap.md](consistency-roadmap.md) — EP-013 execution plan sequencing the EP-007/009/011/012 work that lands this architecture.
- [command-application-port-migration.md](command-application-port-migration.md) — explicit R0-R5/M0-M4 conversion instructions for the complete command catalog.
- [command-port-migration-groups.tsv](command-port-migration-groups.tsv) — machine-readable 56-command fan-out manifest.
- [per-command-checklist.md](per-command-checklist.md) — the per-command checklist; about half is architecture-test-enforced today, the rest is human-judgement.
- [adr/](adr/) — Architecture Decision Records.

## Layered cake

```
┌─ Host adapters (thin) ─────────────────────────────┐
│  CLI: argument parsing + terminal rendering        │
│  MCP: JSON-RPC/stdio session + ToolResult mapping  │
│  IDE: editor context + native UX (future)          │
└───────────────────────────┬────────────────────────┘
                            ▼
┌─ Typed local-RPC boundary ─────────────────────────┐
│ descriptor identity + typed arguments + correlation│
│ cancellation + structured result/error             │
└───────────────────────────┬────────────────────────┘
                            ▼
┌─ Daemon execution host ────────────────────────────┐
│ workspace lifetime, command execution, diagnostics,│
│ cancellation, readiness and upgrade lifecycle      │
└───────────────────────────┬────────────────────────┘
                            ▼
┌─ Service layer (pure) ──────────────────────────────┐
│  src/RoslynTools/Services/  <Foo>Service           │
│  - No System.CommandLine / Console / IO            │
│  - Receives domain request objects                 │
│  - Returns Result<T, RtError> or RtResult<T>       │
└────────────┬────────────────────────────────────────┘
             ▼
┌─ Core (Roslyn + workspace + primitives) ────────────┐
│  src/RoslynTools/Core/                              │
│  - WorkspaceLoader (IWorkspaceSessionFactory)       │
│  - SymbolLocator (Result-returning Try* methods)    │
│  - SymbolResolutionService (DI-injected)            │
│  - SolutionWriter, MethodExtractor, etc.            │
│  - Result<T, RtError>, RtError, RtErrorKind         │
└─────────────────────────────────────────────────────┘
```

**Rule of thumb when adding code:** "Could I unit-test this without any host references in scope?" If no, it's in the wrong layer.

The daemon-centred execution boundary is the target architecture for
workspace-backed commands. File-only and lightweight meta operations may declare
in-process placement. CLI and MCP use the same host-neutral command definition,
application port, and recovery policy; new transport work targets typed local RPC or
the explicitly declared in-process path. See
[ADR-0005](adr/0005-daemon-centred-local-execution.md) and
[ADR-0007](adr/0007-host-neutral-command-contracts.md).

For daemon-owned MCP tools, the stdio adapter lazily starts the sibling `rt`
daemon when RPC is unavailable, retries once, and retains typed in-process
recovery if startup fails. MCP-started daemons stop after ten idle minutes by
default; each request refreshes that deadline. Explicit `rt daemon start`
instances remain resident. See
[ADR-0006](adr/0006-mcp-lazy-daemon-lifetime.md).

## Five contracts that bind the layers

### 1. Application definition + port — command registration and execution

Every ordinary command has one host-neutral application definition and exactly one
typed `ICommandApplicationPort`. The definition carries identity, `RtCommandInfo`,
request/response types, alternative request variants, visibility, safety, execution
placement, and suggested tools.
It contains no CLI builder or protocol type. `IRtCommand` remains the CLI projection;
the generic MCP adapter projects the same definition into its tool surface. During
migration only, descriptors without ports enter the isolated compatibility bridge.
See [ADR-0003](adr/0003-shared-command-descriptors-and-application-ports.md),
[ADR-0007](adr/0007-host-neutral-command-contracts.md), and the
[migration playbook](command-application-port-migration.md).

### 2. Category interfaces — `IInspectCommand` / `IAnalyzeCommand` / `IRefactorCommand`

Exactly one per command. Inspect = read-only, single-symbol; Analyze = read-only, codebase-wide; Refactor = mutates files. Drives the `rt <group> <command>` UX and the dogfood matrix.

### 3. `IStructuredOutputCommand` ⇔ `RtCommandInfo.JsonOutput = true`

US-100 biconditional. Architecture test in `CategoryInterfaceContracts` walks the registry and asserts the equivalence. App startup also enforces.

### 4. Capability interfaces (US-114, in flight)

Each reusable flag is a real capability interface backed by a central catalog: `IHasSolutionOption.SolutionPath`, `ISupportsDryRun.DryRun`, `IHasSymbolTarget.Symbol`, `ISupportsApplySkipOptions.{ApplyIds, SkipIds}`, etc. One shared binder per capability reads the catalog option from `ParseResult`. Commands consume typed request/context objects, not raw `Option<T>` variables. Services receive domain requests — `Limit`, `Symbol`, `TargetType` — not `ISupportsTopOption`. Architecture tests forbid `new Option<T>("--known-flag")` and raw `ParseResult.GetValueForOption` outside the shared binders.

### 5. Result / Error contract

Two coexistent types:

- **`Result<T, RtError>`** for routine, expected user-facing failures (symbol-not-found, ambiguous-match, malformed-input). Total `Match(onOk, onErr)`. Bridge `.Unwrap()` for incremental migration. EP-010 is rolling this out.
- **`RtResult<T>`** for the renderer envelope (status + diagnostics + exit code). Different beast — output side.

`RtErrorPresenter.Surface(ctx, error)` is the single CLI sink for `RtError` — no per-command if/else ladder. MCP handlers `Match` the same Result and return `ToolResult(isError: true, message)`. `App.TryHandleUserErrorAsync` has a typed catch ladder (no substring matching) for genuinely uncatchable exceptions — and its long-term target is to narrow once all bridge callers have drained (US-132 gated on US-131).

## The output pipeline

```
Service returns RtResult<T>  →  RtResultRenderer.Write<T>(result)
                                  │
                                  ├─ T : IRtOutput?  → RtSerializer.Write
                                  │                    (polymorphic IRtJsonOutput / IRtMarkdownOutput / IRtLlmOutput)
                                  └─ raw value       → StructuredOutput.Write
                                                       (always uses JsonEncoders.Select(RtContext.RelaxJson))
```

One method (`Write<T>`). One encoder helper. `--relax-json` opts out of strict escaping (default off → strict-by-default for downstream parsers). `WriteText` stays as the plain-text sibling.

Output shapes are typed records, not dicts. Compact by default (`{ fqn, kind, signatures: [...] }`); `--with-locations` opt-in restores line-bearing detail. Single typed result record where byte-identity allows (outline); two-shape stays where the JSON key order can't be reconciled (describe).

Every inspection result may also carry a `_suggestions` array — five pre-filled follow-on commands with symbol FQN and `--solution` path baked in. Populated at the adapter layer (CLI handler, MCP handler) by `SuggestionHelper`; never inside the core service (which doesn't know the host format or the `--solution` path). The field is an init property per DTO, `null` when JSON output is inactive. See [ADR-0002](adr/0002-suggestions-output-contract-and-proactive-session-start.md) for the full contract and the fan-out backlog (US-136, US-137).

## The catalog (single source of truth)

| Surface | Lives in |
|---|---|
| Command/tool identity, visibility, safety, execution placement, request/result types, suggested tools | Host-neutral application definition |
| Command name + category + examples + mutates + JSON-output flag | `RtCommandInfo` within the definition; CLI projection must match |
| Reusable flag name + aliases + default + parse validation | Central option catalog (US-114) |
| Capability interface ↔ catalog flag binding | Shared binders (US-114) |
| Help, examples, known-issues | Same `RtCommandInfo` — `rt dump-help` reads it; `docs/COMMANDS.md` is generated, never hand-edited |
| MCP tool schema | Generated from the typed request contract, host-neutral property metadata, and alternative request variants |

If two surfaces describe the same flag, the catalog wins.

## Safety net

- **Architecture tests** under `tests/RoslynTools.Tests/Architecture/`. Biconditional invariants, no-direct-console, no-static-WorkspaceLoader, no-RtServiceProvider, no direct `Option<T>` for catalog flags (US-114).
- **Stryker mutation gate** at pre-commit. 95% break threshold for files in scope. `RT_MUTATION_ALLOW_UNSCOPED=1` is the documented escape hatch for behaviour-preserving refactors of files outside the pilot — used with documented rationale in the commit body. Mutation scope expands incrementally from `src/RoslynTools/Core/` outward.
- **rt-first hook gate** (`PreToolUse` in `.claude/settings.json`). Blocks `Read`/`Edit` on existing `.cs` files until you've run an `rt` command against them. Forces dogfooding.
- **Session-start hook** (`UserPromptSubmit` in `.claude/settings.json`). Fires `rt prime --json` once per session (PPID-stamped `/tmp` marker prevents re-runs). Proactive orientation — agents know the gate rule and hot commands before the first tool call, not just after a violation. See [ADR-0002](adr/0002-suggestions-output-contract-and-proactive-session-start.md).
- **Parallel-safe by default**. FeedbackStore mutex, wrapper rebuild mutex, deploy-skill graceful-skip, `RT_ISOLATED_OBJ=1` per-PID MSBuild obj — agents can fan out across worktrees without corrupting shared state.

## The dogfood loop

```
Agent runs rt on a real refactor
  → rt hits a gap or bug
    → rt feedback add captures it (mutex-safe)
      → rt feedback report previews duplicate GitHub issues
        → human approves create or attach
        → already-handled? → status: processed, discard
      → GitHub issue executes via the repository workflow → /tdd
        → red → green → refactor → coverage → mutation
          → commit + push
            → Stryker gate enforces
              → next agent benefits
```

The whole tool is its own customer. The architecture exists to make that loop tight: contracts caught at compile time, drift caught at test time, races impossible by construction, and the agents that maintain rt use rt to maintain rt.

## Open tensions worth naming

- **Portable distribution is staged.** Self-contained CLI/MCP archives, verified user-scoped installers, stable
  launchers, and registry metadata provide the baseline described in
  [ADR-0004](adr/0004-portable-mcp-distribution.md). Official MCP C# SDK hosting, the full spawned protocol matrix, and
  MCP Bundle adoption remain open under rt-012.

- **Daemon-centred execution is a migration.** The daemon still supports CLI argument arrays and rendered frames,
  while migrated MCP tools use typed local RPC with lazy daemon startup and one retry. Unmigrated tools retain direct
  hosting, and request draining plus broader cancellation semantics remain to be designed and proven. See
  [ADR-0005](adr/0005-daemon-centred-local-execution.md).

- **Host projects remain physically coupled.** `RoslynTools.Mcp` still references the CLI executable because the
  descriptor registry, CLI-derived schema metadata, and compatibility bridge live there. Full port coverage deletes
  the bridge and permits both hosts to reference `RoslynTools` directly, as specified by
  [ADR-0007](adr/0007-host-neutral-command-contracts.md).

- **EP-009 (capability flags) is the biggest open architectural lift** — US-114 is 20 ACs, multi-week. Until it lands, commands still wire `Option<T>` ad hoc and the catalog is partly notional.
- **Application-port migration is incremental.** Sixteen of the 56 category commands are port-backed, with `rename`
  still partial; 40 conversion/completion slices remain. `suggest` and feedback are additional typed meta operations.
  Descriptors without ports still enter `CliCommandBridge`. rt-007 and the
  [56-row migration manifest](command-port-migration-groups.tsv) track draining it.
- **The Result/Throw bridges are designed to persist** across stories. Don't drain the SymbolLocator bridges all at once — `one caller/tool at a time`, then delete the bridge.
- **`CompactDescription` vs verbose dict** for describe stays two-shape (JSON key order is irreducible). Outline has been unified. Trade-off accepted in US-135.
- **`_suggestions` fan-out is incomplete.** Only `describe` (CLI+MCP) and `find-refs` (MCP) carry suggestions today. Remaining inspect/analyze commands are tracked in US-136 (outline envelope) and US-137 (fan-out). Body-call related-symbol extraction and frequency ranking are deferred in US-135.
- **`rt prime` hot-command list is hardcoded.** It will drift as the catalog grows. The deliberate trade-off (speed vs currency) is documented in [ADR-0002](adr/0002-suggestions-output-contract-and-proactive-session-start.md).
- **Static class migration is ~57% done.** Instance + DI is the new default; static is a migration target. EP-008 hardens the workspace boundary; `RtServiceProvider.Get<T>` was eliminated in US-072.
- **Pragmatism over purity** — patterns applied only when `rt analyze find-duplicates` shows real pain. Trust the detector, not the textbook.

## Updating this doc

When an architectural decision is taken (a new layer contract, a renamed pattern, a deprecation, a doctrine change):

1. **Write an ADR** under [adr/](adr/) using the template at [adr/README.md](adr/README.md). The ADR captures the context, decision, and consequences in detail.
2. **Update this overview** to reflect the new state — add or amend a contract, rewrite a pipeline diagram, name a new tension. Cross-link the ADR.
3. **Update the most specific companion doc** if one applies (`pattern-core-service.md`, `target-command-surface-architecture.md`, etc.).
4. **Commit all three together** with a single `docs(adr): NNNN <title>` commit so the ADR + overview update + companion edit travel as one unit.

If the decision retires a doctrine (e.g. "don't use static classes for new code" → "static classes are fine for X"), mark the prior ADR as `Superseded by ADR-NNNN` and link forward. Never edit accepted ADRs in place beyond status changes.
