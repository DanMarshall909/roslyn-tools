# Pre-freeze command-surface audit

Status: decision evidence, **not** the final `0.1-preview` surface freeze. Audited
2026-10-01 at clean checkout `31f81b7ccffea652160152b8032fae2b00a24656`.
The source-built CLI was authoritative;
the installed `rt` launcher still pointed to `88d15b6` and was used only as a
consumer baseline. Commands below are the 60 entries from source-built
`rt dump-help --json`; `prime`, `suggest`, feedback, and daemon controls are
additional surfaces.

## Decision order and method

Judge each capability by the user job it completes, semantic correctness,
distinct value, trust/completeness requirements, and whether an agent can reach
and use it without guessing. Then measure calls, response bytes, and elapsed time.
Do not retain a low-value command merely because it is cheap or discard a useful
operation merely because its current renderer is verbose. Preserve distinct typed
requests when combining discovery names or engines.

The audit used `rt prime`, root and per-command help, `dump-help --compact` and
`--json`, `suggest` with supported and missing workflow requests, live CLI
inspection and a clean-commit rename dry-run, and spawned MCP discovery. The
existing [disposition matrix](surface-disposition.md) supplies a destination for
every command; the recommendations below challenge it where live use warrants.
They are judgments from representative agent workflows, not usage telemetry or
proof that every implementation already meets its destination contract.

## Complete current-surface disposition

| Current commands | User job and recommendation |
|---|---|
| `doctor`, `search-symbols`, `outline`, `describe`, `source`, `relationships` | **Retain and improve** the orient/find/understand path. Add caller, callee, implementation, and hierarchy edges to one bounded relationship model. Fix false Partial provenance and stale next-action names before relying on absence results. |
| `semantic-diff`, `dependency-graph` | **Retain distinct** semantic-change and graph results. State parse-only/rename and missing method-body-edge limits; the graph cannot yet be an impact oracle. |
| `check-architecture`, `circular-deps`, `complexity`, `find-magic-strings`, `list-smells`, `solid`, `find-undocumented`, `find-unused` | **Consolidate** as individually selectable, evidence-based `check` rules and one finding/baseline contract. Replace broad SOLID claims; `find-unused` remains candidate evidence and must not license deletion from a partial workspace. |
| `language-features`, `list-imports`, `static-class-inventory` | **Consolidate** descriptive results under `inventory`, with static-type advice experimental. Do not turn inventory into a violation. |
| `find-duplicates`, `find-pattern` | **Consolidate** as structural-search modes, preserving their distinct query shapes. |
| `analyze-interface` | **Consolidate** member-use evidence into relationships and interface impact. Keep the useful grouping engine. |
| `check` | **Retain** CI exit-code enforcement over the same rule engine as exploratory checks. |
| `config` | **Retain locally** for installation and policy repair, outside ordinary MCP discovery. |
| `solid-review` | **Demote** to explicit external advisory review with source-transmission consent; never mix it with deterministic findings. |
| `rename`, `change-signature`, `move`, `move-method`, `move-slice` | **Candidate plan-only core.** Rename is the first exact-plan pilot. Group move discovery while retaining separate symbol, method, and transactional slice contracts. Signature change needs overload/default/API fixtures. |
| `extract-method`, `extract-interface` | **Conditional plan-only candidates.** Retain only after demand and control-flow/interface-impact fixtures establish a complete plan/verify path; do not promise both solely because they exist today. |
| `delete-unreferenced` | **Demote to experimental/high risk** until complete semantic input, dynamic/external-use limitations, and safe candidate language are demonstrated. |
| `add-dependency`, `add-dynamic-dependency`, `add-null-checks`, `add-xml-doc`, `convert-console-logging`, `convert-to-record`, `extract-class`, `inline-method`, `make-async`, `make-instance`, `make-static`, `migrate-to-ioptions`, `narrow-dependency`, `replace-datetime`, `replace-xunit-asserts`, `transform-parameters` | **Retain as experimental recipes/engines** where demand exists. Each has a framework, behavior, dispatch, or follow-up assumption that prevents a supported universal mutation promise. `replace-datetime` is the first `use-time-provider` recipe pilot. |
| `replace-call`, `replace-new` | **Consolidate** behind one symbol-bound rewrite engine with typed invocation/constructor variants; require semantic target binding. |
| `add-global-import`, `add-static-import`, `remove-unused-usings` | **Consolidate** as import recipe modes; retain compiler-confirmed removal and review-first behavior. |
| `extract-string-constant`, `fix-namespace`, `implement-interface`, `make-sealed` | **Prune from ordinary discovery.** Prefer a standard code action or parent move/interface workflow; throwing stubs and solution-only derivation evidence do not make a complete product job. Retain an internal engine only if a supported caller needs it. |
| `trust` | **Move to the local control plane.** It changes user-scoped trust, not repository source; correct its `writes repository` help/effect classification. |
| `prepare-operation` | **Hide from normal help and suggestions.** This opaque durable-coordination primitive cannot prepare a change plan and currently misroutes rename-plan requests. Keep it internal/maintainer-only until a complete lifecycle is admitted. |

The 25 read/analyze/CI entries, 34 refactor entries (including `trust`), and one
lifecycle entry above account for all 60 catalogue entries. `add-test`,
`convert-static-class`, and `debug` are absent from normal discovery; trusted
experimental `probe-di` is also outside the ordinary catalogue. The existing
matrix records their dispositions. `prime`, `suggest`, feedback
administration, and daemon controls require their own audience/discovery review.

## Live workflow and output evidence

Twelve source-built `suggest <query> --top 3 --json` calls establish a small
discovery corpus (one call each, roughly 0.46-0.52 seconds). The top three are
listed in rank order; byte counts are stdout only.

| User request (abbreviated) | Expected route or outcome | Actual top three | Bytes |
|---|---|---|---:|
| Definition, callers, usages | Search, focused source, relationships | `relationships`, `find-duplicates`, `find-undocumented` | 5,917 |
| Impact and safe rename plan | Impact, prepare rename, verify | `prepare-operation`, `rename`, `make-async` | 3,687 |
| Remove non-Microsoft core dependencies | Dependency and architecture evidence | `remove-unused-usings`, `delete-unreferenced`, `make-static` | 3,329 |
| Compare compiler diagnostics | Diagnostics delta or unsupported | `change-signature`, `rename`, `remove-unused-usings` | 3,555 |
| Move three types together | Transactional slice move | `move`, `move-slice`, `move-method` | 2,664 |
| Check project architecture boundary | Architecture check | `check-architecture`, `check`, `add-null-checks` | 2,219 |
| Measure method complexity | Complexity finding | `complexity`, `move-method`, `inline-method` | 2,451 |
| Find missing XML docs | Missing-documentation check | `find-undocumented`, `add-xml-doc`, `find-unused` | 2,595 |
| Find circular namespace dependencies | Circular dependency check | `circular-deps`, `fix-namespace`, `move` | 2,567 |
| Find all symbol references | Relationships | `relationships`, `find-pattern`, `find-duplicates` | 5,806 |
| Verify a prepared plan without applying | Verify-plan or unsupported | `prepare-operation`, `change-signature`, `rename` | 4,421 |
| Nonsensical words | Typed unsupported | No ranked commands; untyped empty result | 37 |

Atomic supported jobs generally rank the intended first command. Multi-step
and unavailable jobs have no honest workflow/unsupported result and sometimes
recommend a mutator or the opaque `prepare-operation` primitive.

| Operation | Observed result |
|---|---|
| Rename dry-run from clean `31f81b7ccffea652160152b8032fae2b00a24656` | Exit 0, `applied=false`, two changed paths, 3,375 stdout bytes, unchanged HEAD/status. It supplied no exact hunks, immutable plan bytes, or revision/content preconditions. |

| Output from current source-built tool | Bytes |
|---|---:|
| `prime` | 613 |
| `dump-help --compact` / full / `--json` | 6,867 / 61,284 / 183,183 |
| One `search-symbols` result, both `--json` and `--llm` | 20,074 each |
| `doctor` result | 14,073 |
| MCP discovery of all 60 tools across three pages | 86,449 |
| One MCP `suggest_command` response | 7,662 |

Root `rt --help` lists the 60 catalogue entries but omits the usable `prime`
and `suggest` entry points. Compact help has names and descriptions but omits
maturity, effects, trust, and limits. `dump-help --json` marks 39 of 60 entries
as lacking JSON output, while `rename --help` offers `--json` and its dry-run
returned structured JSON; the meaning of that metadata needs reconciliation.
Several generated examples use Windows paths on Linux. The `outline` next action
still names the retired `find_refs` identity. MCP does put `prime` and
`suggest_command` first, but complete discovery under the current trust profile
still required three pages.

The focused search's workspace object alone was about 14.5 KB versus 714 bytes
of symbol data. It and `doctor` reported `Partial` on this clean checkout because
`workspace:input-set` was failed despite matching expected and observed
fingerprints. Fix that correctness problem before using absence or safe-delete
claims. A compact agent projection may summarize repeated evidence and offer
retrieval of the full manifest; it must retain the state, counts, material
failure, confidence, and limitation. These are per-call byte measurements, not
token counts or complete workflow budgets. MCP text and structured results overlap
substantially; any compaction must respect the supported protocol eras and client
compatibility.

To reproduce the representative CLI measurements, run the source-built
`src/RoslynTools.Cli/bin/Debug/net10.0/rt` from repository root. `search-symbols`
used `inspect search-symbols CommandDescriptorRegistry --solution RoslynTools.slnx`
with either `--json` or `--llm`; `doctor` used
`inspect doctor --solution RoslynTools.slnx --json`. CLI sizes count stdout bytes.
The rename preview used
`refactor rename RoslynTools.Commands.RenameCommand RenameCommandAudit --solution RoslynTools.slnx --dry-run --json`.
`git status --porcelain=v1 --untracked-files=all` was empty before and after;
HEAD was the full commit above at both checkpoints. MCP sizes count complete
newline-delimited JSON-RPC response lines from the source-built MCP host with no
startup arguments, protocol `2025-06-18`, and the current user's trust profile.
The 60-tool count is profile-specific, not a fresh-install claim. MCP
`suggest_command` received `Assess impact and prepare a safe rename plan` with
`top: 3`.

## Missing jobs and final-freeze gate

The current catalogue cannot complete diagnostics/delta, cross-cutting impact,
baseline comparison, immutable plan review, or plan verification. The original
dependency-extraction workflow also needed manual persistence-boundary work;
`suggest` currently cannot route a project/package dependency question to the
available architecture/graph evidence. Treat a specialized dependency inventory
or boundary-extraction planner as a candidate to validate, not an automatic new
standalone command.

Before the final surface freeze:

1. Build a representative task corpus for discovery, diagnostics, interface/API
   impact, dependency boundaries, single/slice move, rename, signature change,
   extraction, deletion, imports, and framework recipes. Include partial,
   ambiguous, unsupported, and dynamic-use cases.
2. For each task, record the expected safe steps, actual `suggest` top-one/top-three
   results, runnable arguments, calls, bytes, elapsed time, completeness, and
   whether the end result answers the user's job. Unsupported jobs must return
   an honest outcome instead of plausible unrelated or unsafe commands.
3. Reconcile the current matrix, exact CLI/MCP catalogue and schema, root help,
   examples, `doctor` maturity counts, and lifecycle/control-plane audience.
   Finalize the plan-only whitelist only after exact diff and verification
   evidence. Keep the existing command-admission guard as an interim growth
   control, not evidence that the final product surface has been ratified.
4. Prioritize usefulness and safety fixes first: truthful workspace state,
   workflow-aware suggestions, hidden internal lifecycle, diagnostics/impact,
   and reviewable plans. Then reduce output bytes and redundant calls with
   bounded agent views and measured per-workflow budgets.
