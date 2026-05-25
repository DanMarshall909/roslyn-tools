# Per-Command Consistency Checklist

> See [consistency-roadmap.md](consistency-roadmap.md) for **EP-013**, the umbrella plan that sequences the stories making most of this checklist compiler- or architecture-test-enforced. The rows annotated *(enforced — US-NNN)* below will be deleted as that work lands.

When you touch an `rt` command for *any* reason — feature, bug fix, refactor, MCP handler — scan this checklist before opening a PR. The point is to knock several pending stories out in one pass while the file is already open and the test harness already warm.

The list is divided into:

1. **Universal checklist** — every command, every change.
2. **MCP exposure** — native handler conventions (EP-012).
3. **Test coverage** — quality bar.
4. **Per-command pending work** — open backlog stories that touch a specific command.
5. **Cross-cutting work** — not pinned to a command; here so you don't think it's missing.

If an item doesn't apply (e.g. `Info.Mutates` for a read-only command), tick it anyway — the goal is "verified", not "changed".

---

## 1. Universal checklist (every command)

> **US-107 has landed.** The category interfaces (`IInspectCommand`, `IAnalyzeCommand`, `IRefactorCommand`) and capability interfaces (`IMutatingCommand`, `IBatchMutationCommand`, `ITraceAnalyzeCommand`) now drive architecture-test ratchets in `tests/RoslynTools.Tests/Architecture/CategoryInterface*.cs` and `tests/RoslynTools.Tests.Mcp/Architecture/McpHandlerNameContract.cs`. Rows marked **(enforced — US-NNN)** below fail the architecture-test suite if violated. Future work removes them from this manual list once the corresponding tracking story lands.

- [x] `IRtCommand` contract satisfied — static `RtCommandInfo Info` + `Command Build()` *(enforced — App.cs static ctor)*
- [x] Command implements exactly one category interface — `IInspectCommand` / `IAnalyzeCommand` / `IRefactorCommand` (or `ITraceAnalyzeCommand` for trace-scoped analyze) *(enforced — US-107, App.cs static ctor + `CategoryInterfaceCoverage`)*
- [x] `Info.Category` matches the category interface (`"inspect"`/`"analyze"`/`"refactor"`) *(enforced — US-107, `CategoryInterfaceContracts`; field slated for removal — US-108)*
- [x] `Info.Mutates` matches the category interface (refactor = true, inspect/analyze = false) *(enforced — US-107, `CategoryInterfaceContracts`; field slated for removal — US-108)*
- [x] `IRefactorCommand.Build()` registers a `--dry-run` option *(enforced — US-107, `CategoryInterfaceContracts`; known exception `change-signature` tracked by US-112)*
- [ ] Options constructed via `CommandOptionFactory` where applicable (US-085)
- [ ] `Info.Examples` reflect actual current invocations (not stale syntax)
- [ ] `Info.JsonOutput` accurate for inspect/analyze (currently soft-ratcheted via skipped test in `CategoryInterfaceContracts.with_every_inspect_or_analyze_command_jsonoutput_is_true` — owned by US-082 / EP-007 umbrella; unskip once 8 outliers gain JSON output)
- [ ] `Info.SeeAlso` lists related commands (US-088, US-089)
- [ ] `RtCommandInfo` metadata in canonical shape (US-084)
- [ ] File structure follows the playbook (US-086)
- [ ] If JSON-output: result type uses the shared result contract (US-060, US-100)
- [ ] Service lives in `src/RoslynTools/Services/` — no `System.CommandLine`, no `Console` in the service layer
- [ ] Service auto-registered via `Bootstrap.RegisterServices` (implements `IRtService`)
- [ ] Exit codes flow via `System.CommandLine` middleware, not `Environment.ExitCode` (US-093)
- [ ] No parameterless service constructor that hand-rolls concrete dependencies (US-103)
- [ ] Diagnostics/progress → stderr; results → stdout
- [ ] Routine errors return non-zero exit + stderr (no unhandled exceptions)

## 2. MCP exposure (EP-012)

- [ ] Native `IMcpHandler` exists if this is an inspect/analyze read command (US-095, US-096)
- [ ] If native: schema declares all *required* fields explicitly
- [ ] If native: response is camelCase JSON via `JsonNamingPolicy.CamelCase`
- [ ] If native and mutating: `dry-run` defaults to true (or follows the explicit decision in US-097)
- [x] Native `ToolName` matches the command name with hyphens replaced by underscores (so it overrides the auto-bridge by name) *(enforced — US-107, `McpHandlerNameContract.with_every_imcphandler_the_toolname_matches_a_known_cli_command_name`; documented meta-handler exceptions live in `KnownMetaHandlerNames`)*
- [x] No two native handlers share a `ToolName` *(enforced — US-107, `McpHandlerNameContract.with_every_imcphandler_the_toolname_is_unique`)*
- [ ] Registered in `McpServiceRegistration.AddMcpServices`
- [ ] `tools/list` snapshot test still passes (US-094)
- [ ] If the CLI command has a hidden-from-auto-bridge alias (meta-commands), the native handler covers the visible name

**Canonical reference**: commit `6d767b1` (feat(mcp): wire find_refs and rename as native MCP handlers). Read those handlers before writing a new one — same pattern, same JSON conventions.

## 3. Test coverage

- [ ] Tests under `tests/RoslynTools.Tests/<Category>/` (or `tests/RoslynTools.Tests.Mcp/Handlers/` for MCP)
- [ ] Test method names: snake_case `with_<scenario>_<outcome>`
- [ ] Tests resolve dependencies from `Bootstrap.RegisterServices` (no hand-constructed services)
- [ ] Integration tests use the real `.slnx` fixture, not mocks of the Roslyn API surface
- [ ] MCP handler tests parse the returned JSON and assert on structured fields (not `string.Contains`)
- [ ] At least one missing-required-field error test per MCP handler
- [ ] Line + branch coverage ≥ project thresholds
- [ ] If file is in `stryker-config.json` `mutate` globs: mutation score ≥ 95

---

## 4. Per-command pending work

Open stories that touch a specific command. Universal stories (US-060, US-084, US-085, US-086, US-088, US-089, US-093, US-094, US-100, US-103) apply to *every* command and are not duplicated below — see section 1.

### `rt inspect` — read-only

| Command | Open stories | Notes |
|---|---|---|
| `describe` | US-095 | Native MCP handler |
| `find-callers` | US-095 | Native MCP handler |
| `find-impl` | US-095 | Native MCP handler |
| `find-pattern` | US-095 | Native MCP handler |
| `find-refs` | — | ✓ Native handler in `6d767b1` |
| `find-undocumented` | US-096 | Native MCP handler (umbrella story includes this) |
| `find-unused` | US-095 | Native MCP handler |
| `outline` | — | ✓ Native handler (POC) |
| `source` | — | None pending |

### `rt analyze` — cross-cutting scans

| Command | Open stories | Notes |
|---|---|---|
| `analyze-interface` | US-096 | Native MCP handler |
| `circular-deps` | US-096 | Native MCP handler |
| `complexity` | US-096 | Native MCP handler |
| `debug` | — | None pending |
| `dependency-graph` | US-096 | Native MCP handler |
| `find-duplicates` | US-096 | Native MCP handler |
| `find-magic-strings` | US-096 | Native MCP handler |
| `list-imports` | US-096 | Native MCP handler |
| `list-smells` | US-096 | Native MCP handler |
| `solid` | US-101 | Decompose `SolidAnalyzer` into per-principle analyzers |
| `static-class-inventory` | US-096 | Native MCP handler |

### `rt refactor` — mutations

| Command | Open stories | Notes |
|---|---|---|
| `add-dependency` | — | None |
| `add-global-import` | — | None |
| `add-null-checks` | — | None |
| `add-static-import` | — | None |
| `add-test` | — | None |
| `add-xml-doc` | — | None |
| `change-signature` | *(new — EP-012)* | Fix MCP schema: today the auto-bridge emits empty `inputSchema`. Convert to native handler. |
| `convert-console-logging` | — | None |
| `convert-static-class` | US-081 | Finish interface generation |
| `convert-to-record` | — | None |
| `extract-class` | — | None |
| `extract-interface` | — | None |
| `extract-method` | US-079 | `MethodExtractor` → `Result<T, RtError>` |
| `extract-string-constant` | — | None |
| `fix-namespace` | — | None |
| `implement-interface` | — | None |
| `inline-method` | — | None |
| `make-async` | — | None |
| `make-instance` | — | None |
| `make-sealed` | — | None |
| `make-static` | — | None |
| `migrate-to-ioptions` | — | None |
| `move` | *(new — EP-012)* | Fix MCP schema: declare required fields (or one-of `type` vs `namespace`). |
| `move-method` | — | None |
| `narrow-dependency` | — | None |
| `rename` | — | ✓ Native handler in `6d767b1` |
| `replace-call` | — | None |
| `replace-datetime` | — | None |
| `replace-new` | — | None |
| `replace-xunit-asserts` | — | None |
| `safe-delete` | — | None |
| `transform-parameters` | — | None |

### `rt suggest` — meta

| Command | Open stories | Notes |
|---|---|---|
| `suggest` | US-087, US-080, *(new — EP-012)* | Build the BM25 top-level command (US-087); replace hand-curated synonyms (US-080); align native MCP `ToolName` from `suggest_command` to `suggest` |

---

## 5. Cross-cutting (not pinned to a command)

These don't fit the per-command rows but might land in the same pass:

- **US-067** — `/execute` skill nested-branch pattern revisit
- **US-073** — Migrate solution to .NET 10
- **US-078** — `WorkspaceLoader.LoadAsync` → `Result<T, RtError>`
- **US-083** — Bring `src/RoslynTools.Cli/` and `src/RoslynTools.Mcp/` into Stryker mutation pilot
- **US-090** — Unknown-command fallback in `App.cs` (BM25 "did you mean")
- **US-091** — Diff renderer enrichment in `SolutionWriter.PrintDiff`
- **US-092** — Stable fix IDs + `--apply`/`--skip` for batch mutation commands (affects all batch mutators uniformly via `BatchMutationRunner`)
- **US-097** — Decide MCP shape for mutating refactor commands (the dry-run-by-default policy)
- **US-098** — Drop `RoslynTools.Mcp` → `RoslynTools.Cli` project reference
- **US-099** — Extract `IRtCommand` registry into a new `RoslynTools.Commands` project
- **US-102** — Complete `IWorkspaceSessionFactory` migration — remove `LoadWorkspaceAsync` compat surface
