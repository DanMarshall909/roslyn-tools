# Per-command evidence ledger (2026-10-01)

This is a dated decision ledger for the pre-freeze audit, not a maintained
command catalogue or proof of preview readiness. The canonical command names,
options, and examples remain `rt dump-help --json`; the proposed destinations
and target maturity are in [the disposition matrix](surface-disposition.md).
The [audit](pre-freeze-surface-audit.md) explains the measurements, source
review, representative workflows, and safety findings behind these rows.

**Evidence key:** `C` = directly checked source-built CLI catalogue/help;
`M` = directly checked source-built MCP `tools/list` projection; `R` = an
exercised CLI/MCP route recorded in the audit. `D` identifies a design risk,
proposed contract, or earlier audit assessment that still needs a
command-specific fixture or source citation. `D` is **not** a fourth type of
verified evidence. For example, `C/M/D` proves catalogue and projection
coverage, while the third-column risk is a review item rather than a tested
runtime behavior. Where the audit reports a source finding without a cited
command-specific code path, this ledger keeps it as `D`; it does not silently
promote it to verified source evidence. The recommendation is a product
decision, not a claim that the current result already satisfies that
destination. The most material unresolved contract is shown, rather than
repeating the shared trust, completeness, and output-schema gaps on every row.
In particular, all 60 MCP output schemas currently admit a generic object or
array without result fields, and all shared MCP tools inherit catalogue
metadata that can be too coarse for their modes.

Every `CLI → MCP` pair below was reconciled against one `dump-help --json`
catalogue and a direct `initialize`/paged `tools/list` session with the
source-built MCP host under this user's current trust profile. Hyphens become
underscores in MCP names. `—` means that host deliberately has no route.

## Inspect (10 CLI entries)

| CLI → MCP | User job and pre-freeze decision | Distinct evidence / unresolved contract |
|---|---|---|
| `config` → — | Repair local configuration; retain as local control. | `C/R`: no-repository JSON mode works; advertised semantic workspace and trust prerequisites are too broad. |
| `describe` → `describe` | Read a symbol signature; retain focused inspect. | `C/M/D`: trusted semantic result has a source revision but a narrower bare shape than the common provenance envelope. |
| `doctor` → `doctor` | Diagnose installation, trust, and workspace; retain preflight. | `C/M/R`: CLI installation-only mode needs no workspace, while MCP requires `directory`; installation probe and semantic probe must be distinct contracts. |
| `find-pattern` → `find_pattern` | Find syntax examples; consolidate as structural search. | `C/M/R`: no-repository attempt required a solution; distinguish syntax matching from semantic symbol search. |
| `find-undocumented` → `find_undocumented` | Find missing API docs; consolidate as configured finding rule. | `C/M/D`: legacy bare result lacks common completeness/provenance; accessibility should be rule configuration. |
| `find-unused` → `find_unused` | Inspect unreferenced candidates; consolidate as high-risk finding rule. | `C/M/R`: completeness-aware live result suppressed deletion suggestions on Partial input; external/reflection use remains outside local proof. |
| `outline` → `outline` | Orient within a file/type; retain focused inspect. | `C/M/R`: live bounded request carried large workspace evidence and Partial state; next action still names retired `find_refs`. |
| `relationships` → `relationships` | Find references/callers/implementations; retain and extend typed modes. | `C/M/R`: implementation limit returned real candidates but no revision-safe continuation; ambiguous caller result was 85,889 bytes. |
| `search-symbols` → `search_symbols` | Resolve candidate symbols before inspection; retain. | `C/M/R`: candidate limit did not bound workspace evidence; false Partial blocked reliable absence interpretation. |
| `source` → `source` | Read focused source for a resolved symbol; retain. | `C/M/D`: bare result carries revision, but needs the common bounded provenance and completeness contract. |

## Analyze (14 CLI entries)

| CLI → MCP | User job and pre-freeze decision | Distinct evidence / unresolved contract |
|---|---|---|
| `analyze-interface` → `analyze_interface` | See member use before interface edits; fold into impact/relationships. | `C/M/D`: useful grouping engine returns a legacy bare result; preserve member-level evidence in the consolidated route. |
| `check-architecture` → `check_architecture` | Enforce project boundaries; consolidate as architecture finding rules. | `C/M/R/D`: advertises CompleteSemantic but compatibility result lacks authoritative input-set assessment; live check was blocked by a fixture binding. |
| `circular-deps` → `circular_deps` | Find namespace cycles; consolidate as architecture finding rule. | `C/M/R/D`: edges come from textual `using` directives; live JSON is a legacy bare shape despite `jsonOutput=false`. |
| `complexity` → `complexity` | Find high-complexity code; consolidate as maintainability rule. | `C/M/R`: `--top 5` returned 286,429 bytes because type/namespace rollups were not bounded. |
| `dependency-graph` → `dependency_graph` | Inspect graph edges; retain distinct graph result. | `C/M/R`: focused graph returned nodes/edges in a legacy shape; do not imply complete impact from missing method-body edges. |
| `find-duplicates` → `find_duplicates` | Find duplicated structure; consolidate as structural-search mode. | `C/M/D`: legacy bare result; keep clone search distinct from example-pattern query semantics. |
| `find-magic-strings` → `find_magic_strings` | Find repeated literals; consolidate as configured finding rule. | `C/M/D`: legacy result and advertised `jsonOutput=false` despite common JSON help; require evidence and thresholds. |
| `language-features` → `language_features` | Inventory syntax/language features; consolidate under inventory. | `C/M/D`: descriptive legacy result should not become a violation merely through grouping. |
| `list-imports` → `list_imports` | Inventory imports; consolidate under inventory. | `C/M/R`: static-import JSON result had no completeness evidence; metadata incorrectly denies JSON output. |
| `list-smells` → `list_smells` | Find maintainability concerns; split into concrete check rules. | `C/M/D`: broad label and legacy result need named rule/evidence contracts. |
| `semantic-diff` → `semantic_diff` | Compare semantic changes; retain distinct result. | `C/M/R`: two-file no-repository mode succeeds, while catalogue says Semantic/trust; docs-only Git diff returned bare `[]`, without scope/status. |
| `solid` → `solid` | Review design findings; replace broad SOLID label with concrete rules. | `C/M/D`: legacy bare result and broad principle branding exceed current evidence. |
| `solid-review` → `solid_review` | Optional external semantic triage; demote to explicit advisory mode. | `C/M/D`: whole-command ExternalWrite label obscures preview-only versus source-transmission modes; consent/effects need per-mode projection. |
| `static-class-inventory` → `static_class_inventory` | Inventory static types/coupling; consolidate under inventory. | `C/M/D`: legacy result is evidence for a possible recipe, not a direct staticness recommendation. |

## CI (1 CLI entry)

| CLI → MCP | User job and pre-freeze decision | Distinct evidence / unresolved contract |
|---|---|---|
| `check` → `check` | Fail CI for configured rules; retain enforcement route over shared findings. | `C/M/D`: CompleteSemantic is advertised but compatibility result lacks authoritative input-set proof; RepositoryCodeExecution deserves visible trust context despite MCP read-only hint. |

## Refactor (34 CLI entries)

Every repository-writing refactor in this section is currently experimental.
The 33 repository refactors use the prepared change-set path, but preview and
apply prepare separately rather than consuming one revision-bound immutable
plan. `trust` is the exception: it changes local user authorization. The
current common MCP output schema does not describe changed-file, project, or
verification manifests; each admitted variant needs its own review contract.

| CLI → MCP | User job and pre-freeze decision | Distinct evidence / unresolved contract |
|---|---|---|
| `add-dependency` → `add_dependency` | Inject constructor dependency; retain experimental recipe. | `C/M/D`: constructor/DI impact remains; binder can apply with both `--apply` and `--dry-run`. |
| `add-dynamic-dependency` → `add_dynamic_dependency` | Preserve reflection/trimming reachability; retain trusted experimental recipe. | `C/M/D`: runtime semantics cannot be proved from local syntactic match alone. |
| `add-global-import` → `add_global_import` | Add global import; consolidate as import recipe mode. | `C/M/D`: scope and project-wide binding effects need exact plan evidence. |
| `add-null-checks` → `add_null_checks` | Add guards; retain experimental recipe or standard code action. | `C/M/D`: guards can change behavior; plan needs explicit affected branches. |
| `add-static-import` → `add_static_import` | Add static import; consolidate as import recipe mode. | `C/M/D`: `--simplify-all-static-imports` also rewrites existing qualifiers and needs separate effect preview. |
| `add-xml-doc` → `add_xml_doc` | Apply documentation content; retain experimental recipe. | `C/M/D`: generated content requires review rather than a blind mutation promise. |
| `change-signature` → `change_signature` | Edit parameters and callers; candidate plan-only core. | `C/M/D`: four child grammars are absent from parent catalogue; default, force-remove, and named-argument variants need distinct manifests. |
| `convert-console-logging` → `convert_console_logging` | Migrate console logging; retain experimental recipe. | `C/M/D`: framework/DI assumptions; MCP `DryRun=true` default differs from CLI `apply:true` behavior. |
| `convert-to-record` → `convert_to_record` | Convert type representation; retain experimental recipe. | `C/M/D`: equality, serialization, and binary API changes need explicit impact evidence. |
| `delete-unreferenced` → `delete_unreferenced` | Delete candidate unused declarations; demote high risk. | `C/M/R`: complete semantic input correctly refused on Partial workspace, but `--json` failure emitted only stderr; reflection/external uses remain. |
| `extract-class` → `extract_class` | Split class; retain experimental recipe. | `C/M/D`: output-dir path containment, multi-file state/DI impact, and exact manifest are unproved. |
| `extract-interface` → `extract_interface` | Extract contract; conditional plan-only candidate. | `C/M/D`: output-dir containment and interface/API impact fixtures required before admission. |
| `extract-method` → `extract_method` | Extract code block; conditional plan-only candidate. | `C/M/D`: control-flow preservation needs fixtures and exact result/plan contract. |
| `extract-string-constant` → `extract_string_constant` | Introduce constant; prune ordinary discovery. | `C/M/D`: narrow code-action job does not justify a top-level product route. |
| `fix-namespace` → `fix_namespace` | Align namespace/path; prune ordinary discovery. | `C/M/D`: physical path and reference effects belong in complete move workflow. |
| `implement-interface` → `implement_interface` | Add interface members; prune ordinary discovery. | `C/M/D`: throwing stubs do not complete behavioral implementation. |
| `inline-method` → `inline_method` | Inline method; retain experimental recipe. | `C/M/D`: evaluation order and side effects need verification. |
| `make-async` → `make_async` | Start async propagation; retain experimental recipe. | `C/M/D`: direct-caller changes do not establish full call-chain correctness. |
| `make-instance` → `make_instance` | Convert static to instance; retain experimental staticness recipe variant. | `C/M/D`: unresolved call sites may remain; suggestion ranking reverses it with `make-static` for static-conversion request. |
| `make-sealed` → `make_sealed` | Seal a type; prune ordinary discovery. | `C/M/D`: external derivation is unknowable locally and conflicting `--apply --dry-run` can apply. |
| `make-static` → `make_static` | Convert instance to static; retain experimental staticness recipe variant. | `C/M/D`: state/dispatch analysis and all call-site effects are required. |
| `migrate-to-ioptions` → `migrate_to_ioptions` | Migrate options binding; retain experimental recipe. | `C/M/D`: current workflow explicitly leaves read-site follow-up. |
| `move` → `move` | Move type/namespace/project; candidate plan-only core. | `C/M/D`: automatic consumer project references and physical-file effects need exact manifest. |
| `move-method` → `move_method` | Move static method; group move discovery, retain distinct contract. | `C/M/D`: method binding/dispatch differs from type or slice transaction. |
| `move-slice` → `move_slice` | Move a coherent multi-type slice; candidate high-risk plan-only core. | `C/M/D`: explicit reference/friend-assembly justifications and one transaction required; task-language search ranks single `move` first. |
| `narrow-dependency` → `narrow_dependency` | Segregate interface consumers; retain experimental recipe. | `C/M/D`: DI follow-up and consumer partitioning need impact evidence. |
| `remove-unused-usings` → `remove_unused_usings` | Remove compiler-confirmed unused imports; consolidate as import recipe mode. | `C/M/D`: solution-wide default needs scope bound and conflicting `--apply --dry-run` can apply. |
| `rename` → `rename` | Rename symbol; first immutable-plan pilot. | `C/M/R`: clean dry-run changed no files but returned no exact hunks/revision preconditions; CSV, related edits, collisions require separate admission. |
| `replace-call` → `replace_call` | Rewrite bound invocation; consolidate with constructor rewrite engine. | `C/M/D`: replacement target must resolve semantically; retain typed argument filters. |
| `replace-datetime` → `replace_datetime` | Use `TimeProvider`; first framework recipe pilot. | `C/M/D`: framework and constructor-injection assumptions; solution-wide default needs bound. |
| `replace-new` → `replace_new` | Rewrite constructor use; consolidate with call rewrite engine. | `C/M/D`: current match needs semantic target binding; selected argument can be removed. |
| `replace-xunit-asserts` → `replace_xunit_asserts` | Convert xUnit assertions; retain framework-specific recipe. | `C/M/D`: solution-wide default needs bound; MCP default dry-run differs from CLI apply semantics. |
| `transform-parameters` → `transform_parameters` | Introduce parameter object; retain experimental recipe. | `C/M/D`: public signature and call-site impact require exact plan/verification. |
| `trust` → — | Inspect/grant/revoke local repository trust; move to local control plane. | `C/R/D`: current refactor/RepositoryWrite/Semantic labels are wrong for user-scoped authorization state. |

## Lifecycle and MCP-only discovery

| CLI → MCP | User job and pre-freeze decision | Distinct evidence / unresolved contract |
|---|---|---|
| `prepare-operation` → `prepare_operation` | Coordinate a stored operation; hide from ordinary discovery. | `C/M/D`: 13 opaque inputs, no plan producer/status/apply path; task suggestions falsely route plan requests here. |
| — → `prime` | Give a short agent entry point; retain discovery, generate command truth. | `M/R`: works without workspace; 613-byte CLI primer, but root help omits the CLI alias. |
| — → `suggest_command` | Route task language to capabilities; retain and improve primary discovery. | `M/R`: marked Experimental; multi-step/unsupported requests can recommend unrelated mutators or return untyped empty results. |

The last two rows are MCP-only **within the 60-entry catalogue comparison**:
usable CLI `prime` and `suggest` exist outside that catalogue. Feedback, daemon,
and `dump-help` are likewise ancillary CLI surfaces, reviewed in the main
audit and disposition matrix rather than counted as catalogue entries here.

## Coverage and evidence boundary

The section counts are 10 inspect + 14 analyze + 1 CI + 34 refactor + 1
lifecycle = **60 CLI entries**. The table contains 58 explicit shared mappings,
two CLI-only entries (`config`, `trust`), and two MCP-only discovery entries
(`prime`, `suggest_command`) = **60 MCP tools**. This snapshot is specific to
the audited checkout and trust profile. It provides an individual decision and
identified gap for each route, but most rows have catalogue/source evidence
only. The final admission gate still needs scenario fixtures for advertised
versus actual effects, trust/workspace modes, result schemas, completeness,
and end-to-end task outcomes before any name is declared preview-safe.
