# Current command disposition for `0.1-preview`

This is the reviewed **recommended destination** after the two-repository task
corpus, not the final product-surface freeze or a
claim that any current implementation already passes the shared provenance,
structured-output, cancellation, resource, parity, and packaging gates. The
[pre-freeze audit](pre-freeze-surface-audit.md) reviews the live 60-entry catalogue
for user usefulness first and agent output cost second. Scenario-level admission
and the final preview whitelist remain open. Thin compatibility wrappers may
exist temporarily during migration, but only the destination contract is eligible
for preview support.

## Analyze commands

| Current command | Destination | Target maturity | Decision |
|---|---|---|---|
| `analyze-interface` | `impact --kind interface` / relationship member usage | Preview | Consolidate after the shared resolver. |
| `check-architecture` | `check --rule architecture.*` | Preview -> Stable | First config-driven architecture finding family; carry authoritative input-set completeness in the shared check envelope before claiming a whole-solution pass. |
| `circular-deps` | `check --rule architecture.circular-dependency` | Preview -> Stable | Consolidate into common findings; current namespace edges come from textual `using` directives, so state that evidence limit. |
| `complexity` | `check --rule maintainability.complexity` | Preview -> Stable | First finding-envelope pilot; bound the complete response, including type and namespace rollups. |
| `debug` | Separate trace-development tool | Removed | Unrelated to C# semantic tooling. |
| `dependency-graph` | `dependency-graph` | Stable candidate | Keep distinct graph result. |
| `find-duplicates` | `structural-search --mode duplicates` | Preview | Consolidate with pattern search. |
| `find-magic-strings` | `check --rule maintainability.repeated-string` | Experimental -> Preview | Consolidate as evidence-based rule. |
| `language-features` | `inventory language-features` | Preview -> Stable | Keep descriptive, not a finding. |
| `list-imports` | `inventory imports` | Preview -> Stable | Feed the imports recipe without judging style. |
| `list-smells` | Concrete `check` rules | Preview -> Stable | Split long-method, large-type, and dependency rules. |
| `probe-di` | Trusted `recipe inspect-di` CLI | Experimental | Executes repository code; absent from normal MCP. |
| `semantic-diff` | `semantic-diff` | Stable candidate | Keep with explicit parse-only/rename limits; two-file mode is no-evaluation and needs mode-specific trust/workspace metadata. |
| `solid` | Concrete `check` rules | Preview | Remove SOLID violation branding. |
| `solid-review` | Optional external semantic triage | Experimental | Preserve deterministic findings; classify actual external/source-transmission effects per mode and require explicit consent. |
| `static-class-inventory` | `inventory static-types` | Experimental -> Preview | Input to a future staticness recipe. |

## CI commands

| Current command | Destination | Target maturity | Decision |
|---|---|---|---|
| `check` | `ci check` | Preview | Enforce checked-in architecture and complexity rules with one typed result and failing exit status; carry input-set completeness before a clean pass is proof. |

## Inspect commands

| Current command | Destination | Target maturity | Decision |
|---|---|---|---|
| `config` | Local host configuration inspection | Preview | Keep as the MSBuild-free repair path; live no-repository mode needs truthful no-workspace/no-trust metadata and remains outside MCP discovery. |
| `describe` | `describe` | Stable candidate | Keep compact signature contract. |
| `doctor` | `doctor` | Stable candidate | Keep as workspace and installation preflight; describe installation-only no-evaluation separately and make it easy to find in MCP discovery. |
| `find-callers` | `relationships callers` | Removed from public surface | Retain a temporary internal wrapper while the canonical relationship contract settles. |
| `find-impl` | `relationships implementations` | Removed from public surface | Retain a temporary internal wrapper while the canonical relationship contract settles. |
| `find-pattern` | `structural-search --mode example` | Preview | Consolidate with duplicates. |
| `find-refs` | `relationships references` | Removed from public surface | Retain a temporary internal wrapper while the canonical relationship contract settles. |
| `find-undocumented` | `check --rule documentation.missing` | Experimental -> Preview | Consolidate; accessibility is rule configuration. |
| `find-unused` | `check --rule usage.unreferenced-candidate` | Preview/high risk | Never imply whole-world proof; no plan from partial workspaces. |
| `outline` | `outline` | Stable candidate | Keep core orientation primitive; remove synthetic tuple summaries and bound provenance bytes. |
| `relationships` | `relationships` | Preview | One typed surface for references, callers, and implementations; bound ambiguity, add revision-safe continuation, and extend modes here. |
| `search-symbols` | `search-symbols` | Preview | Keep deterministic typed candidates; bound complete output including repeated workspace evidence. |
| `source` | `source` | Stable candidate | Keep focused-source primitive with byte bounds and common completeness/provenance result. |

## Refactor commands

| Current command | Destination | Target maturity | Decision |
|---|---|---|---|
| `add-dependency` | `recipe inject-constructor-dependency` | Experimental | Constructor/API/DI consequences require impact and verify; reject `--apply --dry-run` ambiguity. |
| `add-dynamic-dependency` | `recipe preserve-reflection-type` | Trusted experimental | Runtime/trimming semantics. |
| `add-global-import` | `recipe imports --kind global` | Experimental | Consolidate import planners. |
| `add-null-checks` | Standard code action or `recipe add-null-guards` | Experimental | Can change behaviour. |
| `add-static-import` | `recipe imports --kind static` | Experimental | Consolidate import planners; separately review `--simplify-all-static-imports` because it rewrites existing qualifiers. |
| `add-test` | Future narrow xUnit scaffold recipe, if demanded | Removed from public surface | Delete rt-specific mode; current command mixes unrelated jobs. |
| `add-xml-doc` | `recipe apply-documentation-map` | Experimental | Generated content needs explicit review. |
| `change-signature` | `prepare-change-signature` | Preview plan-only | Keep rename/reorder/add/remove parameter variants typed; default values, forced removal, and external/API impact need separate plan evidence. |
| `convert-console-logging` | `recipe migrate-console-logging` | Experimental | Framework and injection assumptions. |
| `convert-static-class` | Rebuild later as staged recipe | Removed until complete | Advertised interface/DI stages are skipped. |
| `convert-to-record` | `recipe convert-to-record` | Experimental | Equality, serialization, and ABI risk. |
| `extract-class` | `recipe extract-class` | Experimental | State, coupling, DI, multi-file effects, and output-path containment require review. |
| `extract-interface` | `prepare-extract-interface` if admitted | Conditional preview plan-only | Require demand, interface-impact fixtures, and output-path containment before promising it. |
| `extract-method` | `prepare-extract-method` if admitted | Conditional preview plan-only | Require demand and control-flow fixtures before promising it. |
| `extract-string-constant` | Standard code action | Remove from ordinary discovery | Low-value top-level command; retain an internal engine only for a proved caller. |
| `fix-namespace` | Standard code action or move recipe | Remove from ordinary discovery | Physical-path and reference effects belong in a complete move workflow. |
| `implement-interface` | Standard code action or interface recipe | Remove from ordinary discovery | Throwing stubs are incomplete behaviour. |
| `inline-method` | `recipe inline-method` | Experimental | Evaluation order and side-effect risk. |
| `make-async` | `recipe async-propagation` | Experimental | Direct callers are insufficient for general propagation. |
| `make-instance` | `recipe change-method-staticness --to instance` | Experimental | May leave unresolved call sites. |
| `make-sealed` | Standard code action | Remove from ordinary discovery | External derivation cannot be proven locally; current contradictory dry-run/apply flags can apply. |
| `make-static` | `recipe change-method-staticness --to static` | Experimental | Requires state and dispatch analysis. |
| `migrate-to-ioptions` | `recipe migrate-ioptions` | Experimental | Current workflow leaves read-site work. |
| `move` | `prepare-move-symbol` | Preview plan-only | Review namespace, project, physical-file, and automatic consumer project-reference effects with explicit manifests. |
| `move-method` | `prepare-move-symbol --kind method` | Preview/high risk | Group move discovery but keep the static-method contract distinct from a slice transaction. |
| `move-slice` | `prepare-move-slice` | Preview plan-only/high risk | Complete slice manifest, explicit project-reference/friend-assembly edits and justifications, and one transactional apply. |
| `narrow-dependency` | `recipe segregate-interface` | Experimental | DI follow-up and consumer partitioning. |
| `rename` | `prepare-rename` | Preview plan-only | First single-symbol change-plan pilot; batch CSV, exact-set related-edit selection, file collisions, and stale plans require separate evidence. |
| `replace-call` | Symbol-bound rewrite recipe engine | Experimental | Replacement target must resolve semantically; preserve typed argument filters. |
| `replace-datetime` | `recipe use-time-provider` | Experimental | Framework and constructor-injection assumptions. |
| `replace-new` | Symbol-bound rewrite recipe engine | Experimental | Current matching must become semantic; argument filters and possible matched-argument removal need explicit review. |
| `remove-unused-usings` | `recipe imports --remove-unused` | Experimental | Keep compiler-confirmed detection and review-by-default application; reject contradictory dry-run/apply flags. |
| `replace-xunit-asserts` | `recipe migrate-xunit-to-shouldly` | Experimental | Framework-specific. |
| `delete-unreferenced` | `prepare-delete-unreferenced` if admitted | Experimental/high risk | Require complete workspace, explicit external/dynamic-use limitations, a review protocol, and refusal conditions; a loaded solution cannot prove all use absent. |
| `transform-parameters` | `recipe introduce-parameter-object` | Experimental | Signature, call-site, and public API risk. |
| `trust` | Local repository trust control plane | Preview | Move out of the refactor category and correct its user-scoped effect label; explicit add/remove is the authorization boundary and is never MCP-discoverable. |

## Ancillary commands and surface conventions

| Current capability | Destination | Decision |
|---|---|---|
| `suggest_command` | `suggest` workflow guidance | Rebuild from catalogue, Git, and semantic context with read-intent and unsupported-job routing; reconcile MCP required query with CLI's optional Git-context query and label this primary discovery route deliberately. |
| `feedback_add` | Maintainer/dogfood CLI | Keep out of normal MCP; internal descriptor already marks LocalStateWrite, so verify CLI help and runtime projection. |
| `feedback_list` | Maintainer CLI | May expose local paths, traces, and samples; add paging/limits before a 206-item list can consume 520 KB. |
| `feedback_process` | Maintainer CLI | Internal descriptor marks local read/write; verify CLI help and runtime projection. |
| `feedback_report` | Maintainer CLI with explicit external apply | Never ordinary agent discovery. |
| `prime` | Generated `doctor --quick` / capabilities primer | Remove hard-coded command truth. |
| `dump-help` | Generated documentation tool | Maintainer surface; make its own `--help` explain the generated views. |
| `prepare-operation` | Internal durable coordination primitive | Remove from normal help and suggestion discovery until a complete plan lifecycle exists; it does not prepare a refactor plan. |
| daemon lifecycle | Internal performance experiment | Not packaged or advertised for preview. |
| root plus grouped duplicates | One canonical grammar | Remove duplicate product identities before release. |
| cryptic short aliases | None unless justified by evidence | Remove before compatibility exists. |
| `--strict-json` | None | Delete deprecated no-op. |
| output booleans | `--format` | Stable text and JSON; other renderers are views. |

## Immediate containment priority

1. `probe-di` repository-code execution.
2. deletion-candidate and `find-unused` proof overclaims.
3. Every multi-file apply path, especially move, rename, signature change, extraction,
   interface narrowing, and parameter transformation.
4. Feedback operations with incorrect side-effect metadata.
5. Incomplete or misleading commands: `convert-static-class`, mixed-mode `add-test`,
   direct-caller-only async conversion, and workflows that knowingly leave manual
   follow-up.
