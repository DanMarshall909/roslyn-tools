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

## Catalogue contract cross-check

The metadata below comes from source-built `rt dump-help --json`. It describes
what the catalogue **advertises**, not proof of every runtime effect. The
per-command destinations and reasons are in the matrix above and in
`surface-disposition.md`.

| Catalogue group | Advertised common contract | Material exceptions and live discrepancies |
|---|---|---|
| 25 inspect/analyze/CI entries | All require explicit repository trust; 23 say Semantic workspace and `check`/`check-architecture` say CompleteSemantic. | `solid-review` declares ExternalWrite; `check` and `check-architecture` declare RepositoryCodeExecution. `semantic-diff` two-file mode and `config` work without a solution, Git repository, or trust; `doctor --installation-only` reports no workspace requested. Their current whole-command prerequisites overstate those modes. `find-pattern` did require a solution in a no-repository trial. |
| 34 refactor entries | All are Public/Experimental, conditionally mutable, and declare repository read/write plus explicit trust; 33 say Semantic and, independently, 33 lack advertised JSON output. | `delete-unreferenced` requires CompleteSemantic and advertises no JSON. `trust` says Semantic and is the one entry advertising JSON, but its user-scoped local-state job is mislabelled as repository write/semantic work. A rename dry-run returned JSON despite its `jsonOutput=false` metadata. |
| `prepare-operation` | Public/Experimental, local-state read/write, no workspace or trust, JSON output. | The stored-plan coordination primitive has no plan producer yet and should not be a public task recommendation. |

Six read-only entries (`circular-deps`, `find-magic-strings`,
`find-undocumented`, `find-unused`, `list-imports`, `list-smells`) and 33
refactors advertise `jsonOutput=false` even though common help offers `--json`.
Live `circular-deps`, `list-imports`, and rename dry-run calls produced JSON;
the first two used legacy result shapes without the common provenance envelope.
Twenty-one of 25 inspect/analyze/CI and 31 of 34 refactor metadata records have
no listed limitations, while
their help or implementations acknowledge incomplete follow-up or semantic
risks. Confirm individual request/result schemas and actual effect boundaries
before final admission; one command-level flag cannot describe every mode.

## MCP projection cross-check

A direct `initialize`/paged `tools/list` session against the source-built MCP
host (`2025-06-18`, no startup arguments, current persistent-trust profile)
returned exactly 60 unique tools in three pages. Fifty-eight match CLI catalogue
names after hyphen-to-underscore normalization. CLI-only `config` and `trust`
are local control-plane commands; MCP-only `prime` and `suggest_command` are
discovery entry points. No shared tool had a missing top-level required input
relative to its CLI required options in this audit; variant schemas such as
`rename` and `change_signature` still need scenario-level validation. The
converse is not true: MCP `doctor` requires an absolute `directory` even though
CLI `doctor --installation-only` does not. `tools/call doctor {}` fails input
deserialization; supplying `directory` and `installationOnly: true` runs.

| MCP family | Tools | Compact tool-definition bytes |
|---|---:|---:|
| `prime`, `suggest_command` | 2 | 1,684 |
| Inspect | 9 | 13,279 |
| Analyze | 14 | 17,159 |
| Refactor | 33 | 49,622 |
| CI | 1 | 890 |
| Lifecycle | 1 | 2,929 |

All 60 output schemas are the generic object-or-array union, so they do not
identify operation result fields. The 58 shared tools project the same advertised
maturity/effects/audience/workspace metadata as CLI; that agreement does not
establish that the metadata itself is correct. Thirty-three refactor tools,
`solid_review`, and `prepare_operation` advertise non-read effects; the other
25 are read-only. `suggest_command` is marked Experimental despite being the
primary discovery route. `doctor` is on page two while the internal
`prepare_operation` is also discoverable. `check` and `check_architecture` have
MCP `readOnlyHint=true` but also declare RepositoryCodeExecution, which needs
prominent trust messaging even though the protocol hint describes write effects.
Across the 24 shared inspect/analyze/CI tools, only 17 of 117 input properties
have descriptions and only `relationships.mode` has an enum. This makes
machine-readable discovery weaker than the command help, even for read-only work.

The complete list response was 86,449 bytes, including 886 bytes of
JSON-RPC/page serialization outside compact tool definitions.
`suggest_command` returned 7,662 bytes for one request,
with substantially overlapping text and structured data. Keep compatibility
with clients that read text while testing whether the text view can be shorter
and whether preview-only discovery can hide experimental/maintainer operations.
These figures describe this trusted local profile, not a fresh anonymous install.

## Ancillary and local-control surface

Root help and the 60-entry catalogue omit usable `prime`, `suggest`,
`dump-help`, and `maintainer`. `maintainer --help` exposes feedback and the
experimental daemon controls, neither of which appears in normal MCP discovery.
`dump-help --help` exits successfully without printing help. The CLI `suggest`
query is optional for Git-context suggestions, while MCP `suggest_command`
requires it; those are different invocation contracts and need explicit
documentation. `prime` needs no workspace evaluation and emitted 613 bytes of
text (835 JSON). It and `suggest_command` occupy the first two MCP discovery
positions, but the latter advertises Experimental maturity.

`config --json` works outside a repository in 742 bytes, reinforcing its local
preflight role. `trust status . --json` reads user-scoped trust, while explicit
add/remove change that local authorization state; the catalogue's repository
write/Semantic classification is wrong. `prepare-operation` appears in root help
and MCP page two despite requiring 13 opaque coordination inputs and offering
no refactor-plan producer, status, or apply path. It belongs in maintainer
discovery until an end-to-end plan lifecycle exists.

Feedback `add` writes local state, `list` reads it, `process` writes it, and
`report` can invoke `gh` for external persistence behind explicit confirmation
options. Their internal application descriptors already carry Maintainer
audience and local/external effects; those descriptors are absent from normal
catalogue/MCP discovery, and runtime boundaries still need validation. A local
`feedback list --status pending` returned 206 items and 520,065 bytes, with
identical text, JSON, and LLM output and no limit/paging option. The daemon's
start/stop/status/logs are likewise outside the catalogue; start may load a
workspace, while stop/status/logs are local controls. A status probe on this
machine returned a stopped/stale diagnostic on stderr (184 bytes, exit 0), even
with `--json`. These controls need an explicit maintainer contract and bounded
outputs, but they should not consume the normal preview tool budget.

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
As a control, using each catalogue entry's own `whenToUse` text as the query
ranked that command first in all 60 cases (60 calls, 201,375 aggregate stdout
bytes). That confirms catalogue self-retrieval, not user-task discoverability;
the external-language corpus above is the relevant acceptance test.

Additional task-language probes found `change-signature` absent from the top
three for both “Remove a method parameter and update all callers” and “Rename a
method parameter in named arguments”; the former ranked `relationships`,
`remove-unused-usings`, and `transform-parameters`, while the latter ranked
`rename`, `extract-method`, and `move-method`. “Convert an instance method to
static” ranked `make-instance` ahead of `make-static`. A request to apply an
already reviewed immutable plan ranked `prepare-operation`, `trust`, and
`convert-to-record`, although no immutable plan lifecycle exists yet.

| Invoked task route on this repository | Actual outcome and cost |
|---|---|
| `doctor --installation-only --json` using source-built CLI | Failed installation check, 2,767 stdout bytes in 0.65 s: CLI/build host/SDK healthy, but the source-built MCP launcher and handshake unhealthy. Workspace was correctly `NotRequested`, trust `NotRequired`. This is useful installation evidence, not a semantic-workspace failure. |
| `search-symbols Execute --limit 5 --json` | Exit 0, `Partial` because of the existing input-set issue, 21,134 bytes in 22.77 s. The requested candidate bound did not bound workspace evidence. |
| `relationships callers ExecuteAsync --include-tests --json` | Ambiguous-symbol failure, 85,889 stdout bytes in 21.92 s; the unbounded candidate message overwhelms the actionable disambiguation. |
| `check-architecture --config .rt/architecture.json --json` | Exit 1, 247 bytes in 24.8 s: an unresolved `FixtureCases` binding in a test fixture blocked the current solution-wide check. The route is invocable, but this run did not answer whether the configured boundaries pass. |
| `dependency-graph --type RoslynTools.Commands.RenameCommand --depth 1 --json` | Exit 0, 1,272 bytes in 13.5 s; returned a focused legacy `nodes`/`edges` result. |
| `complexity --top 5 --json` | Exit 0, 286,429 bytes in 14.46 s: five methods plus 1,108 type and 49 namespace rollups. `--top` limits methods, not the complete response. |

These are one-run observations on the current source-built checkout, not
benchmark distributions; concurrent workspace activity may affect times. The
three largest responses show that bounding the *whole result* matters more than
shortening a few descriptions. Keep a retrievable full evidence view while
adding explicit item/byte limits and bounded ambiguity choices.

## Mutation workflow and schema variants

The 34 refactor catalogue entries do not map one-to-one to mutation contracts.
Twenty-seven expose `--dry-run` without a separate `--apply`, five expose both,
and the parent `change-signature` and `trust` entries expose neither. The four
`change-signature` child grammars (`rename-param`, `reorder`, `add-param`,
`remove-param`) are absent from its parent JSON options/arguments; `add-param`
requires a default value and `remove-param` offers `--force`. Child help says
preview is the default and `--dry-run false` applies, so parent metadata cannot
describe the safety boundary. A future explicit plan/apply protocol should make
the variants and their effects reviewable.

The single-symbol rename pilot does not cover `rename --csv` batch mutation or
the selective `--related-edit` behavior. Likewise, `move` spans type/namespace,
project, and physical-file effects, while `move-slice` can change project
references and `InternalsVisibleTo` with required justifications. Admit each
variant through exact changed-file, project, and reference manifests. Preserve
the typed argument filters in any `replace-call`/`replace-new` consolidation;
constructor replacement can drop a matched argument. The
`add-static-import --simplify-all-static-imports` mode rewrites existing
qualifiers beyond the requested new import. Solution-wide defaults for
`remove-unused-usings`, `replace-datetime`, and `replace-xunit-asserts` need
scope bounds before a plan is approved. `migrate-to-ioptions` explicitly leaves
read-site follow-up and remains an incomplete experimental recipe.

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

### Decision-ranked implementation sequence

| Order | Work | Evidence needed before promotion |
|---:|---|---|
| 1 | Correct workspace completeness, trust/effect claims, and unsafe discovery. Hide `prepare-operation`, keep external/source-transmission modes explicit, and bound ambiguous candidate responses. | Healthy, partial, untrusted, and ambiguous fixtures show truthful outcomes through CLI and MCP; no unsupported workflow is suggested as runnable. |
| 2 | Make discovery task-aware: route navigation, parameter edits, dependency boundaries, diagnostics, and transactional moves through executable steps; expose `prime`/`suggest` clearly; return typed unsupported outcomes. | A repository task corpus records top-one/top-three ranking, valid arguments, completion, calls, elapsed time, and bytes, including failure cases. |
| 3 | Admit one reviewable rename plan/verify contract, then validate signature and move variants separately. Evaluate any experimental direct CLI apply separately. Build diagnostics delta and impact evidence to support plans. | Exact file/project/reference manifest, content/revision preconditions, typed verification, stale-plan refusal, and clean-commit previews per admitted variant; apply checks only for an implemented optional apply path. |
| 4 | Consolidate findings into `check`, descriptive results into `inventory`, clone/example matching into structural search, and import/rewrite recipes behind typed variants. Retire duplicate public names only after routes are equivalent. | Parity fixtures retain each distinct request/result and CI enforcement behavior; callers can still reach the useful engines. |
| 5 | Reduce agent cost after the result contracts are correct: compact provenance, page complete results, limit full-result bytes, and remove duplicated MCP text where client compatibility permits. | Measure complete workflow call counts and response bytes, preserving completeness state, material failures, and retrievable evidence. |

The current command-admission guard may stay in place while this sequence runs.
This audit recommends a destination for every current name; it does not certify
the final preview whitelist or authorize removing compatibility routes before
equivalent tasks pass the corpus.

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
