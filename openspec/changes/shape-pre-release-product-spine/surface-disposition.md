# Current command disposition for `0.1-preview`

This is a target classification, not a claim that any current implementation already
passes the shared provenance, structured-output, cancellation, resource, parity, and
packaging gates. Thin compatibility wrappers may exist temporarily during migration,
but only the destination contract is eligible for preview support.

## Analyze commands

| Current command | Destination | Target maturity | Decision |
|---|---|---|---|
| `analyze-interface` | `impact --kind interface` / relationship member usage | Preview | Consolidate after the shared resolver. |
| `check-architecture` | `check --rule architecture.*` | Preview -> Stable | First config-driven architecture finding family; consolidate into the shared check envelope. |
| `circular-deps` | `check --rule architecture.circular-dependency` | Preview -> Stable | Consolidate into common findings. |
| `complexity` | `check --rule maintainability.complexity` | Preview -> Stable | First finding-envelope pilot. |
| `debug` | Separate trace-development tool | Removed | Unrelated to C# semantic tooling. |
| `dependency-graph` | `dependency-graph` | Stable candidate | Keep distinct graph result. |
| `find-duplicates` | `structural-search --mode duplicates` | Preview | Consolidate with pattern search. |
| `find-magic-strings` | `check --rule maintainability.repeated-string` | Experimental -> Preview | Consolidate as evidence-based rule. |
| `language-features` | `inventory language-features` | Preview -> Stable | Keep descriptive, not a finding. |
| `list-imports` | `inventory imports` | Preview -> Stable | Feed the imports recipe without judging style. |
| `list-smells` | Concrete `check` rules | Preview -> Stable | Split long-method, large-type, and dependency rules. |
| `probe-di` | Trusted `recipe inspect-di` CLI | Experimental | Executes repository code; absent from normal MCP. |
| `semantic-diff` | `semantic-diff` | Stable candidate | Keep with explicit parse-only limitations. |
| `solid` | Concrete `check` rules | Preview | Remove SOLID violation branding. |
| `static-class-inventory` | `inventory static-types` | Experimental -> Preview | Input to a future staticness recipe. |

## CI commands

| Current command | Destination | Target maturity | Decision |
|---|---|---|---|
| `check` | `ci check` | Preview | Enforce checked-in architecture and complexity rules through one typed result and failing CLI exit status. |

## Inspect commands

| Current command | Destination | Target maturity | Decision |
|---|---|---|---|
| `describe` | `describe` | Stable candidate | Keep compact signature contract. |
| `doctor` | `doctor` | Stable candidate | Keep as the workspace and installation preflight. |
| `find-callers` | `relationships callers` | Removed from public surface | Retain a temporary internal wrapper while the canonical relationship contract settles. |
| `find-impl` | `relationships implementations` | Removed from public surface | Retain a temporary internal wrapper while the canonical relationship contract settles. |
| `find-pattern` | `structural-search --mode example` | Preview | Consolidate with duplicates. |
| `find-refs` | `relationships references` | Removed from public surface | Retain a temporary internal wrapper while the canonical relationship contract settles. |
| `find-undocumented` | `check --rule documentation.missing` | Experimental -> Preview | Consolidate; accessibility is rule configuration. |
| `find-unused` | `check --rule usage.unreferenced-candidate` | Preview/high risk | Never imply whole-world proof; no plan from partial workspaces. |
| `outline` | `outline` | Stable candidate | Keep core orientation primitive. |
| `relationships` | `relationships` | Preview | One bounded typed surface for references, callers, and implementations; extend modes here. |
| `search-symbols` | `search-symbols` | Preview | Add deterministic typed candidate discovery before focused inspection. |
| `source` | `source` | Stable candidate | Keep focused-source primitive with byte bounds. |

## Refactor commands

| Current command | Destination | Target maturity | Decision |
|---|---|---|---|
| `add-dependency` | `recipe inject-constructor-dependency` | Experimental | Constructor/API/DI consequences require impact and verify. |
| `add-dynamic-dependency` | `recipe preserve-reflection-type` | Trusted experimental | Runtime/trimming semantics. |
| `add-global-import` | `recipe imports --kind global` | Experimental | Consolidate import planners. |
| `add-null-checks` | Standard code action or `recipe add-null-guards` | Experimental | Can change behaviour. |
| `add-static-import` | `recipe imports --kind static` | Experimental | Consolidate import planners. |
| `add-test` | Future narrow xUnit scaffold recipe, if demanded | Removed from public surface | Delete rt-specific mode; current command mixes unrelated jobs. |
| `add-xml-doc` | `recipe apply-documentation-map` | Experimental | Generated content needs explicit review. |
| `change-signature` | `prepare-change-signature` | Preview plan-only | Core typed plan; external/API risk. |
| `convert-console-logging` | `recipe migrate-console-logging` | Experimental | Framework and injection assumptions. |
| `convert-static-class` | Rebuild later as staged recipe | Removed until complete | Advertised interface/DI stages are skipped. |
| `convert-to-record` | `recipe convert-to-record` | Experimental | Equality, serialization, and ABI risk. |
| `extract-class` | `recipe extract-class` | Experimental | State, coupling, DI, and multi-file risk. |
| `extract-interface` | `prepare-extract-interface` | Preview plan-only | Core typed plan after impact evidence. |
| `extract-method` | `prepare-extract-method` | Preview plan-only | Control-flow and known edge cases. |
| `extract-string-constant` | Standard code action | Experimental | Low-value top-level command. |
| `fix-namespace` | Standard code action or move/import recipe | Experimental | Reference and physical-path effects. |
| `implement-interface` | Standard code action or interface recipe | Experimental | Throwing stubs are incomplete behaviour. |
| `inline-method` | `recipe inline-method` | Experimental | Evaluation order and side-effect risk. |
| `make-async` | `recipe async-propagation` | Experimental | Direct callers are insufficient for general propagation. |
| `make-instance` | `recipe change-method-staticness --to instance` | Experimental | May leave unresolved call sites. |
| `make-sealed` | Standard code action | Experimental | External derivation cannot be proven locally. |
| `make-static` | `recipe change-method-staticness --to static` | Experimental | Requires state and dispatch analysis. |
| `migrate-to-ioptions` | `recipe migrate-ioptions` | Experimental | Current workflow leaves read-site work. |
| `move` | `prepare-move-symbol` | Preview plan-only | Core typed plan with project/path effects. |
| `move-method` | `prepare-move-symbol --kind method` | Preview/high risk | Consolidate; current scope is static methods. |
| `move-slice` | `prepare-move-slice` | Preview plan-only/high risk | Complete slice manifest, explicit project edits, and one transactional apply. |
| `narrow-dependency` | `recipe segregate-interface` | Experimental | DI follow-up and consumer partitioning. |
| `rename` | `prepare-rename` | Preview plan-only | First change-plan pilot. |
| `replace-call` | Symbol-bound rewrite recipe engine | Experimental | Replacement target must resolve semantically. |
| `replace-datetime` | `recipe use-time-provider` | Experimental | Framework and constructor-injection assumptions. |
| `replace-new` | Symbol-bound rewrite recipe engine | Experimental | Current matching must become semantic. |
| `remove-unused-usings` | `recipe imports --remove-unused` | Experimental | Keep compiler-confirmed detection and review-by-default application. |
| `replace-xunit-asserts` | `recipe migrate-xunit-to-shouldly` | Experimental | Framework-specific. |
| `delete-unreferenced` | `prepare-delete-unreferenced` | Preview plan-only/high risk | Candidate language; require complete workspace and explicit limitations. |
| `transform-parameters` | `recipe introduce-parameter-object` | Experimental | Signature, call-site, and public API risk. |
| `trust` | Local repository trust control plane | Preview | Explicit local add/remove verbs are the user authorization boundary; never expose through daemon or MCP discovery. |

## Ancillary commands and surface conventions

| Current capability | Destination | Decision |
|---|---|---|
| `suggest_command` | `suggest` workflow guidance | Rebuild from catalogue, Git, and semantic context. |
| `feedback_add` | Maintainer/dogfood CLI | Remove from normal MCP; correct write classification. |
| `feedback_list` | Maintainer CLI | May expose local paths, traces, and samples. |
| `feedback_process` | Maintainer CLI | Correct write classification. |
| `feedback_report` | Maintainer CLI with explicit external apply | Never ordinary agent discovery. |
| `prime` | Generated `doctor --quick` / capabilities primer | Remove hard-coded command truth. |
| `dump-help` | Generated documentation tool | Maintainer surface. |
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
