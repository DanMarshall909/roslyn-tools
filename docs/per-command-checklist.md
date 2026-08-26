# Per-command consistency checklist

Use this checklist whenever a public `rt` command changes. Architecture tests enforce
the mechanical rules; this list keeps the remaining human review small and explicit.

Companion documents:

- [Command application-port migration](command-application-port-migration.md)
- [55-command migration manifest](command-port-migration-groups.tsv)
- [Target command surface](target-command-surface-architecture.md)
- [Architecture overview](architecture-overview.md)

## 1. Universal command contract

- [x] Implements `IRtCommand` and exactly one category contract:
  `IInspectCommand`, `IAnalyzeCommand`, or `IRefactorCommand` (including derived
  capability contracts such as `ITraceAnalyzeCommand`).
- [x] `RtCommandInfo` name, category, mutability, JSON support, and examples agree with
  the command surface.
- [ ] Reusable flags come from the central flag catalog/binder where migrated; do not
  hand-roll a catalog-owned option or parser.
- [ ] `WhenToUse` names the triggering situation and calls out a built-in action such as
  Read/Edit/Grep when the command replaces it.
- [ ] Examples execute against the current syntax and defaults.
- [ ] `SeeAlso` names the useful next commands.
- [ ] Diagnostics/progress go to stderr; primary results go to stdout.
- [ ] Routine user errors produce a non-zero CLI exit and actionable stderr without an
  unhandled exception.
- [ ] No process-global `Environment.ExitCode`; use host projection.

## 2. Host-neutral application contract

- [ ] One application definition and exactly one typed `ICommandApplicationPort`.
- [ ] Request and response are shared typed records with no CLI/MCP types.
- [ ] Request schema declares required fields and descriptions without instantiating a
  CLI `Command`.
- [ ] Operation owns validation, workspace lifetime, cancellation, and service
  orchestration.
- [ ] Expected failures return `RtError`; nested `Result` values are never serialized as
  successful payloads.
- [ ] Safety is explicitly read-only, apply-default, or review-default.
- [ ] Execution placement is explicitly daemon-preferred or in-process.
- [ ] Suggested follow-on actions are typed data, not rendered command prose.
- [ ] Service/operation contains no `System.CommandLine`, `InvocationContext`,
  `JsonElement`, `RtConsole`, `RtLog`, `RtResultRenderer`, or exit-code mutation.

## 3. Thin adapters

### CLI

- [ ] Owns arguments/options, flag binding, piped-stdin resolution, terminal rendering,
  and exit-code mapping only.
- [ ] Does not load a workspace or invoke a domain service directly.
- [ ] Calls the shared execution router with the typed request.
- [ ] CLI-only formats, progress, colour, and interactive decisions remain here.

### MCP

- [ ] Generic descriptor adapter exposes the port; no command-specific `IMcpHandler`.
- [ ] Normalized tool name is unique and matches the definition (`-` to `_`).
- [ ] Schema and structured output come from shared contracts.
- [ ] Mutation defaults come from shared safety metadata.
- [ ] `tools/list` and generic `tools/call` tests pass.

## 4. Mutation contract

- [ ] Review, dry-run, apply, and no-op are distinct states where applicable.
- [ ] Review analyzes only; dry-run prepares the exact transformation without writing.
- [ ] Response lists every affected path and carries structured diagnostics.
- [ ] Apply writes once after preparation succeeds.
- [ ] Multi-file/filesystem work uses the prepared change-set abstraction; cancellation
  or failure cannot leave a half-applied move/write chain.
- [ ] A mutation is never retried after dispatch becomes indeterminate.

## 5. Tests

- [ ] Operation tests carry the bulk of behavior and edge cases.
- [ ] Request binding/schema test covers names, required fields, defaults, arrays, and
  enums.
- [ ] Expected invalid/not-found/ambiguous/unsupported error test.
- [ ] Cancellation propagation test.
- [ ] Thin CLI exit/rendering test.
- [ ] Generic MCP structured success/error test.
- [ ] Daemon/in-process parity for daemon-preferred commands.
- [ ] Mutations cover all supported review/dry-run/apply/no-op states and prove preview
  performs no write.
- [ ] Line/branch coverage and in-scope Stryker threshold pass.

## 6. Fan-out ownership

Command conversion agents edit only their assigned command, request/result/operation,
direct service dependencies, and focused tests. The integration owner edits shared
definitions/discovery, daemon routing/recovery, schema generation, mutation change-set
abstractions, architecture ratchets, project references, generated snapshots, and
documentation.

Do not edit a central daemon allow-list or descriptor list from every fan-out branch.
The target contract discovers ports automatically.

## 7. Remaining cross-cutting stories

- **US-078** — `WorkspaceLoader.LoadAsync` to typed error.
- **US-083** — expand mutation coverage to CLI and MCP projects.
- **US-092** — stable fix IDs and apply/skip for batch mutations.
- **US-093** — shared CLI exit-code routing.
- **US-098** — remove `RoslynTools.Mcp` to `RoslynTools.Cli` reference after bridge deletion.
- **US-102** — complete workspace-session ownership migration.
- **US-114** — central flag catalog, binders, and full capability ratchet.
- **US-131/US-132** — drain throwing Result bridges and narrow the app error ladder.
