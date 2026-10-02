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
every command. The [per-command ledger](per-command-evidence-ledger.md) records
individual evidence boundaries, and the generated
[machine census](command-census.jsonl) captures the compact CLI/MCP metadata.
The recommendations below challenge the matrix where live use warrants.
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
| 25 inspect/analyze/CI entries | All require explicit repository trust; 23 say Semantic workspace and `check`/`check-architecture` say CompleteSemantic. | `solid-review` declares ExternalWrite; `check` and `check-architecture` declare RepositoryCodeExecution. Local CLI `semantic-diff` two-file mode and `config` work without a solution, Git repository, or trust; `doctor --installation-only` reports no workspace requested. Their current whole-command prerequisites overstate those modes. MCP two-file semantic diff now returns typed `Unsupported` until contained file reading exists (ADR-0100). `find-pattern` did require a solution in a no-repository trial. |
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

The default persistent MCP host previously returned an unrelated absolute
workspace-target error for `semantic_diff` with two files, while a host without
the persistent trust registry could pass the same request to a direct file
reader. Absolute paths alone do not satisfy the accepted contained-read
boundary. [ADR-0100](../../../docs/adr/0100-refuse-uncontained-mcp-semantic-diff-files.md)
now gives every MCP file-mode request a typed `Unsupported` result before
daemon forwarding, MSBuild registration, or file reading. Git mode now checks
the selected directory against the active trust scope before creating its Git
client, including when the MCP host was started for a different repository.
The separate direct daemon RPC audit found that the receiver did not enforce
application-port placement: a trusted `solution` could dispatch the in-process
semantic-diff port with unrelated file paths or a different Git directory. The
dispatcher now returns `command_not_found` for every `InProcess` port before
deserialization or workspace admission; focused RPC tests cover both modes and
malformed arguments. Eligible `DaemonPreferred` read-only ports still execute.

A source/operation cross-check of all 25 read/analyze/CI entries found five
envelope-backed results with explicit provenance (`doctor`, `search-symbols`,
`outline`, `relationships`, `find-unused`), two trusted semantic results with
source revision but a narrower bare shape (`describe`, `source`), and fifteen
legacy bare results after compatibility workspace loading (`analyze-interface`,
`check`, `check-architecture`, `circular-deps`, `complexity`, `dependency-graph`,
`find-duplicates`, `find-magic-strings`, `find-pattern`, `find-undocumented`,
`language-features`, `list-imports`, `list-smells`, `solid`,
`static-class-inventory`). The remaining `config`, `semantic-diff`, and
`solid-review` use local-config, direct parse/Git, and optional provider paths
respectively. This explains why matching advertised metadata does not mean
matching result semantics.

In particular, `check` and `check-architecture` advertise CompleteSemantic,
yet their current compatibility workspace path does not carry the authoritative
input-set completeness assessment into their results. `find-unused` does assess
completeness and suppress deletion suggestions when absence proof fails. The
check gap is a static contract finding, not proof that a current run falsely
passes; final admission needs a healthy/partial fixture and an explicit
completeness-bearing result. The parse-only two-file `semantic-diff`,
installation-only `doctor`, and preview-only `solid-review` paths also confirm
that effects/trust/workspace claims need mode-level projection.
Doctor's result now reports `RepositoryCodeExecution` after trusted workspace
acquisition starts, including an acquisition failure. Installation-only and
pre-acquisition results omit it; the scalar command catalogue still overstates
installation-only workspace requirements and understates full-mode effects.
In a DoctorOperation-only Stryker run with 15 in-process tests, every mutant on
the new safety decision and effect names was killed. The whole file scored
56.21% (86 killed, 60 survived, 7 no coverage), so broader doctor behavior
coverage remains separate from this mode-safety correction.
`circular-deps` builds namespace edges from textual `using` directives, so its
cycle findings are syntax-derived. The architecture test that would ratchet
category marker contracts is currently skipped; turn it back on before relying
on category labels for generated discovery.

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

The explicit method-staticness direction slice now routes the first query to
`make-static` at `--top 1` (963 JSON stdout bytes); the reverse request routes
to `make-instance` (982 bytes). “Remove static from a method” and “Make a static
method non-static” also route to `make-instance` (983 bytes each). One matching
recipe is returned without an invented plan or target method. The live control
queries “Find static imports used across the solution,” “Add static using
imports,” and “Convert a static class to an instance class” still rank
`list-imports`, `add-static-import`, and `static-class-inventory` first.
The post-guard live probe showed “Convert an instance method to static and
list callers” still ranked the opposite `make-instance` at `--top 1` through
general BM25 fallback. This compound request now returns named
`Unsupported(method-staticness-workflow)` with manual steps; no single mutation
is presented as the complete task. Repeated independent boundary reviews found
that an open-ended second-action verb list could miss “give me a list of
callers,” call-site preservation phrasing, and a second sentence, while treating
harmless punctuation or “and nothing else” as a second task. The route now
recognizes a bounded single-conversion request with polite phrasing, stated
motives such as performance and testability, and the known test-project scope.
Additional or unrecognized surrounding clauses return the named missing
workflow. “Convert an instance method to static, please” remains one task;
“Convert an instance method to static. List callers” and an explicit caller
constraint do not receive a bare mutation. Method-to-property/field requests
return `Unsupported(method-kind-conversion)`, and an explicit missing plan
request keeps its earlier named outcome.
A further independent review found that a supplied method identity between the
direction words still fell through to BM25: “Convert OrderService.Validate
from instance to static” ranked `make-instance`. The route now recognizes
qualified, named, generic, and signature forms, while an unparsed explicit
method conversion returns the missing workflow instead of ranking the opposite
mutation. No concrete method is selected by the recommendation.
The named-method sweep also caught `make` and `remove static` forms that had
fallen back to opposite-direction ranking. Those now preserve direction.
Property/field staticness requests instead name
`Unsupported(member-staticness-conversion)`; method-to-property/field requests
retain `method-kind-conversion`. At that checkpoint, creating or deleting a
static member still fell through general discovery rather than being mislabeled
an existing-member conversion.
A further review found that a positive method conversion followed by “do not
change callers” or a static-class task could fall through to opposite-direction
ranking. Whole-query exclusions now apply only to the standalone excluded
request; the recognized conversion plus its later clause returns
`Unsupported(method-staticness-workflow)`. A property/field conversion followed
by a static-class or static-import clause now preserves the source member kind
and returns `Unsupported(member-staticness-conversion)`. The same result holds
when class/import inventory or static-member deletion precedes a later positive
conversion; positional exception checks had let these requests fall back to a
method recipe.
An independent false-positive sweep found that “Make a static property
thread-safe,” “Mark a static field obsolete,” “Change the name of a static
property,” “Convert a static field to a property,” and “Make a static class
with properties injectable” were incorrectly called property/field staticness
conversions. The guard now requires an explicit staticness direction after the
member within the same task clause; these requests retain general discovery.
A later sweep showed that “in a static method” and “for static analysis” are
context, not a conversion target. Explicit “add/remove static modifier on a
field/property” requests now name the missing member-staticness capability.
The boundary table also covers named and qualified member identities, mixed
property/field names, plural fields, `set`/`apply` verbs, static keyword
variants, and a later positive conversion after an unrelated or negated
member action. Standalone negated member conversions retain discovery.
An unparsed method conversion followed by “convert to static class/imports”
also stays on the missing method workflow route; an earlier whole-query
exclusion had let BM25 recommend the opposite method mutation.
The reverse order with class/import inventory before a qualified unparsed
method conversion now receives the same missing workflow outcome.
A static-class `from static to instance`
variant still uses general catalogue discovery and ranks `make-instance` first;
class migration discovery therefore remains an explicit next safety task.
The final 166-query source-built `suggest --top 1 --json` corpus returned the
expected bounded routes and retained controls. A single `make-static` response
was 962–963 stdout bytes, `make-instance` 982–983 bytes, missing method
staticness workflow 492 bytes, and missing property/field staticness 310 bytes.
These per-call byte counts do not establish end-to-end token savings. General
discovery still ranked `add-static-import` for “Add a static property” and
`make-static` for “Remove a static field” at that checkpoint. Explicit static
property/field creation and ordinary deletion now return
`Unsupported(static-member-creation)` or `Unsupported(static-member-deletion)`
with empty results and no inferred mutation recipe. Standalone negations return
`NoMatch`; a later positive member change and separate inventory request keep
their own routes. Source-built CLI probes covered 70 creation, deletion,
negation, import, modifier, method, and attribute-context queries. A creation
response was 336 stdout bytes versus 1,349 for the earlier unrelated import
recommendation; this is a per-call comparison, not an end-to-end token claim.
Access modifiers and explicit C# types, including nullable, generic, and tuple
forms, before the member kind retain the
creation/deletion route. Attribute, accessor, initializer, reference, and access
changes on static members are
not classified as member creation or deletion. They still fall through general
ranking and can surface unrelated commands; the task-aware relevance threshold
remains open.
The negated control “Do not convert an instance method to static; list its
callers” now returns `Unsupported(method-relationship-target)` with empty
results and asks for a named method. A standalone negation returns `NoMatch`;
an exact separate `find references to method OrderService.Validate` clause
returns one `relationships references` read step. Source-built `rt suggest`
confirmed all three outcomes; positive conversion with a caller constraint
still names `method-staticness-workflow`. Caller-specific routing remains open.
“Make a static class injectable” previously ranked `make-static`; it now returns
`Unsupported(static-class-migration)` with empty results and a preliminary
`static-class-inventory` pointer. Static-class-to-instance variants take the
same route in source-built `rt suggest`; inventory-only requests still select
`static-class-inventory`, and compound positive method conversion still names
`method-staticness-workflow`. A later class migration after an unrelated class
action also keeps the missing migration outcome. A complete class migration
remains open.
Independent boundary review found that direct “make ... an instance class”,
“migrate ... to an instance class”, and “usable with dependency injection”
still received unrelated recommendations. The repaired route covers those
forms plus explicit replacement and DI wording. It excludes negated migration
requests and keeps an injectable target in a separate class action from being
borrowed by an earlier static-class action.
A second boundary review exposed named-source “from a static class” requests and
an injectable-service destination, plus contextual “from an instance class”,
`but`-joined actions, and negated targets. The route now requires an explicit
source-to-destination relationship or direct `make` target within the same
action, preserving non-migration context.
A third boundary review found that member qualifiers, `this static class`, and
a direct `instance` target still fell through. It also found that `with an
instance class` and a prohibition with `ever` falsely signaled migration. The
current classifier strips only recognized source qualifiers, reserves task
words from the optional class-name slot, and treats `with` as a replacement
destination only. Broader task-aware workflow parsing remains open.
A fourth review found quoted type names and comma-separated member qualifiers
missing, while a generic optional name slot treated "return" and "accept" as
type names and a destination prefix treated an instance method as a class
migration. Bare names now require type-like casing unless explicitly named or
quoted; destination matching rejects method, field, property, and event kinds.
The next review found that all-caps task words such as `TO`, `INJECTABLE`, and
`RETURN` passed the type-casing check. Bare class names now exclude these task
words regardless of casing, while quoted and explicitly named identifiers
remain eligible.

An eight-query read-workflow corpus used exact external-language tasks rather
than catalogue `whenToUse` text. Each row is one source-built
`suggest "<task>" --top 3 --json` call; bytes are stdout.

| Exact task | Actual top three | Bytes | Useful first route? |
|---|---|---:|---|
| Understand the members of WorkspaceLoader before editing it | `outline`, `extract-interface`, `dependency-graph` | 3,706 | Yes for outline; later suggestions are unrelated. |
| Find every implementation of IRtCommand before changing the interface | `extract-interface`, `analyze-interface`, `implement-interface` | 2,742 | No; misses `relationships implementations` and suggests mutations. |
| Find NuGet package dependencies in the core layer | `find-pattern`, `find-undocumented`, `find-duplicates` | 6,127 | No package-inventory route or honest unsupported outcome. |
| Find unused methods that might be invoked by reflection | `find-unused`, `remove-unused-usings`, `find-pattern` | 4,130 | Candidate inspection first; reflection limitation must remain visible. |
| Assess the semantic effect of the last commit | `semantic-diff`, `prepare-operation`, `check-architecture` | 4,058 | Semantic diff first; internal primitive should not follow. |
| Find static imports used across the solution | `list-imports`, `add-static-import`, `make-static` | 2,898 | Inventory first; two mutators are irrelevant. |
| Find violations of configured project layering rules | `solid`, `check`, `find-pattern` | 2,530 | No; misses `check-architecture`. |
| Find baseline deltas for architecture violations | `check-architecture`, `solid`, `check` | 2,528 | Architecture command first, but baseline comparison is incomplete. |

Direct safe calls tested whether some intended routes actually answer their
task. `outline src/RoslynTools/Core/WorkspaceLoader.cs --summary --json`
returned `Partial` with four summaries (the requested type and three synthetic
`ValueTuple` entries), 20,391 bytes in 22.15 s. `relationships implementations
IRtCommand --kind type --limit 5 --json` returned five real implementers,
`Partial`, and a limit-reached note without revision-safe continuation: 21,734
bytes in 22.31 s. `find-unused --scope methods --json` returned low-confidence
candidates with no deletion suggestions on the same Partial workspace: 29,656
bytes in 44.16 s. `semantic-diff --from HEAD~1 --to HEAD --json` returned `[]`
for a docs-only commit in 0.44 s/2 bytes without a status or scope envelope.
`list-imports --kind static --json` returned `{"imports":[]}` in 12.88 s/20
bytes without completeness evidence. The checked-in architecture configuration
defines project-reference layers, not package-inventory rules, so its current
route cannot answer the NuGet inventory request.

The follow-up discovery slice routes explicit NuGet package inventory and
wholesale non-Microsoft core dependency extraction requests to named
`Unsupported` outcomes with manual next steps. The measured ranking and bytes
above remain the pre-fix baseline; symbol and project dependency queries keep
their existing recommendation path. This closes the misleading-suggestion
case, not the missing inventory or extraction capability.

A subsequent discovery slice gives explicit compiler/analyzer diagnostic
comparison requests a named `Unsupported` outcome with a manual two-revision
build comparison. The original mutator ranking in the task corpus remains
the baseline; the diagnostic-delta command itself is still future work.

Another focused slice routes explicit configured project/layer boundary checks
to one `check-architecture` workflow using the checked-in default config. CLI
and MCP suggestions carry the same operation; MCP requires an absolute solution
path. The stop condition names missing config, trust, errors, and the current
check result's missing workspace-completeness evidence. Dependency and rule
inventories, unconfigured architecture, SOLID, baseline-delta, mutation, and
custom-config requests remain on their earlier discovery path. Compound
wholesale dependency extraction stays `Unsupported`. The corpus ranking above is pre-fix
evidence, not a current ranking.

Explicit proposed change-impact requests now return named `Unsupported` rather
than ranking `move` as if it assessed impact. The response points to manual
reference/project-dependency inspection and build/test verification while
stating that absent source edges cannot prove safety across reflection,
generated code, or external consumers. On the same CycleEngine query, current
source-built JSON stdout changed from 2,705 to 409 bytes. This is one
suggestion-response measurement; the impact operation and end-to-end workflow
remain open.

Explicit references to one complete named C# symbol now produce one populated
`relationships references` read workflow through CLI and MCP; MCP requires an
absolute solution path. Exact `type`, `method`, `field`, `property`, and `event`
kind cues are carried to the read step. Constructor, namespace, parameter,
member, and specific type-subtype cues stay on general discovery because the
resolver cannot select their requested subjects exactly. The source-built
`Find all references to WorkspaceLoader` suggestion was 881 JSON stdout bytes.
Its exact suggested CLI call succeeded
but returned a `Partial`, truncated page of 100 out of 289 references without a
continuation token (60,774 JSON stdout bytes). The workflow therefore stops
short of an exhaustive claim. These are per-call response measurements, not
end-to-end token savings. Generic, ambiguous, project/package-reference,
qualified-scope, unsupported-kind, and proposed-impact requests retain their
previous routes; the specialized step does not silently discard filters it
cannot express.

### Second-repository read workflow: Flo

The source-built `rt` was run from the already trusted
`/home/dan/code/Flo` checkout at
`1a787eed47d59fe9d99c60ede7f5d367d0f36758` without changing trust,
using read-only calls against `Flo.sln`. Its primary worktree was heavily dirty
throughout, so this is a workflow sample rather than a clean-status mutation
check. Flo's agent
guidance was read first.

| Read route | Result usefulness and completeness | Seconds | Stdout bytes |
|---|---|---:|---:|
| Outline `CycleEngine.cs --summary` | One requested type summary; `Partial` workspace. The task data was 109 bytes inside the response. | 11.23 | 20,328 |
| Search `CycleEngine --kind NamedType --limit 5` | One candidate; `Partial`. | 10.44 | 20,713 |
| Describe `CycleEngine --kind type` | Nine useful signatures; bare output has no completeness field. | 9.62 | 2,611 |
| Relationships `implementations ICodexClient --limit 10` | Three implementations; `Partial`. | 10.35 | 21,501 |
| Complexity `--top 5 --threshold 10` | Five methods plus 83 type and 16 namespace rollups; bare output has no completeness field. | 7.24 | 25,605 |
| Dependency graph `CycleEngine --depth 1` | Fourteen nodes and thirteen edges; bare output has no completeness field. | 7.54 | 1,307 |

For the outline, 14,471 bytes were workspace evidence and `--llm` reduced
20,328 bytes to 20,327. Typed reads reported three failed workspace inputs
among 1,993, so the `Partial` state must constrain absence conclusions.
Eight Flo-language suggestion probes cost 26,281 bytes in 5.9 seconds total:
project/package dependencies ranked duplicates first, diagnostics comparison
ranked mutators, and move-impact wording ranked `move` ahead of analysis.
Implementation lookup, architecture boundaries, and duplicate-code requests
had useful first routes. Each `suggest "<query>" --top 3 --json` call exited 0
without stderr:

| Exact Flo task query | Top three | Bytes |
|---|---|---:|
| Find project and package dependencies across Flo solution layers | `find-duplicates`, `check-architecture`, `find-undocumented` | 2,900 |
| Find callers and implementations of ICodexClient before changing it | `relationships`, `find-pattern`, `find-duplicates` | 5,805 |
| Assess impact of moving CycleEngine to another project | `move`, `check-architecture`, `add-global-import` | 2,626 |
| Inspect CycleEngine structure without reading its source file | `source`, `outline`, `find-pattern` | 4,027 |
| Compare compiler diagnostics before and after current changes | `remove-unused-usings`, `convert-console-logging`, `replace-xunit-asserts` | 3,093 |
| Check architecture boundaries between Application and Infrastructure | `check-architecture`, `check`, `add-null-checks` | 2,220 |
| Find repeated code in the Application layer | `find-duplicates`, `find-magic-strings`, `find-unused` | 2,766 |
| Find source of an ambiguous ExecuteAsync symbol | `source`, `search-symbols`, `find-pattern` | 2,844 |

Flo HEAD and its seven-worktree list were unchanged across the calls. Its
primary status had one additional modified source path at the end,
`src/Flo/Application/Cycles/CycleTestSourcePolicy.cs`, amid concurrent dirty
work. No refactor or edit call was run, but the status change cannot be
attributed to `rt` or another actor from this evidence. Treat the side-effect
check as inconclusive; no Flo worktree was reset or cleaned.

| Invoked task route on this repository | Actual outcome and cost |
|---|---|
| `doctor --installation-only --json` using source-built CLI | Failed installation check, 2,767 stdout bytes in 0.65 s: CLI/build host/SDK healthy, but the source-built MCP launcher and handshake unhealthy. Workspace was correctly `NotRequested`, trust `NotRequired`. This is useful installation evidence, not a semantic-workspace failure. |
| `search-symbols Execute --limit 5 --json` | Exit 0, `Partial` because of the existing input-set issue, 21,134 bytes in 22.77 s. The requested candidate bound did not bound workspace evidence. |
| `relationships callers ExecuteAsync --include-tests --json` | Ambiguous-symbol failure, 85,889 stdout bytes in 21.92 s; the unbounded candidate message overwhelms the actionable disambiguation. |
| `check-architecture --config .rt/architecture.json --json` | Exit 1, 247 bytes in 24.8 s: an unresolved `FixtureCases` binding in a test fixture blocked the current solution-wide check. The route is invocable, but this run did not answer whether the configured boundaries pass. |
| `dependency-graph --type RoslynTools.Commands.RenameCommand --depth 1 --json` | Exit 0, 1,272 bytes in 13.5 s; returned a focused legacy `nodes`/`edges` result. |
| `complexity --top 5 --json` | Exit 0, 286,429 bytes in 14.46 s: five methods plus 1,108 type and 49 namespace rollups. `--top` limits methods, not the complete response. |

The later complexity slice ([ADR-0098](../../../docs/adr/0098-scope-complexity-top-rollups.md))
measured a new pre-change source-built baseline of 288,729 bytes with 1,117
type rollups. After scoping rollups to the five returned methods' four types
and one namespace and adding explicit scope/total fields, the response was
3,289 bytes (98.9% less). `--top 0` still returned all 6,929 methods, 1,117
type rollups, and 49 namespace rollups. This is a per-call byte comparison on
this checkout; it does not measure token use or a whole workflow.

A source-built `complexity --top 5 --llm` call subsequently exposed an output
contract gap: exit 0 and zero output because the global flag was accepted while
the command only declared Text and JSON. The shared CLI guard in
[ADR-0099](../../../docs/adr/0099-reject-unsupported-cli-output-formats.md)
now rejects an unsupported explicit format before workspace loading and names
the declared alternatives. Commands with no format declaration remain outside
that guard.

A later shared-resolver ambiguity slice ([ADR-0095](../../../docs/adr/0095-bound-shared-symbol-ambiguity.md))
measured the same source-built `relationships callers ExecuteAsync --include-tests
--json` route at 85,921 stdout bytes before the change and 4,150 after it
(95.2% less). The after response reported 130 total matches and six complete
displayed identities with truncation. These are per-call byte counts on this
checkout, not an end-to-end token measure. The separate no-store legacy
`SymbolLocator` ambiguity path was later bounded by
[ADR-0096](../../../docs/adr/0096-bound-legacy-symbol-ambiguity.md), and its
member-signature overload error by
[ADR-0097](../../../docs/adr/0097-bound-member-overload-ambiguity.md).

These are one-run observations on the current source-built checkout, not
benchmark distributions; concurrent workspace activity may affect times. The
three largest responses show that bounding the *whole result* matters more than
shortening a few descriptions. Keep a retrievable full evidence view while
adding explicit item/byte limits and bounded ambiguity choices.

## Mutation workflow and schema variants

The 34 refactor catalogue entries do not map one-to-one to mutation contracts.
All 33 repository refactors ultimately use the prepared change-set path;
`trust` instead changes user-scoped local authorization.
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

A source-level effect review found four pre-freeze safety blockers, none of
which was exercised by applying a refactor:

1. `add-dependency`, `make-sealed`, and `remove-unused-usings` accept both
   `--apply` and `--dry-run`, yet `--apply --dry-run` can still choose apply in
   their current binders. Reject contradictory flags or make dry-run dominate.
   `convert-console-logging` and `replace-xunit-asserts` do make dry-run win,
   but their MCP request default `DryRun=true` changes what `apply:true` alone means
   relative to CLI. One shared mode contract is needed.
2. `extract-class --output-dir` and `extract-interface --output-dir` pass an
   arbitrary destination through to the prepared change writer without an
   affected-path containment check. The writer checks content/existence, while
   these extraction paths rely on trust for the selected solution without
   checking each generated output path. A central canonical-path boundary,
   including symlinked ancestors and
   a check near commit, is required before apply-capable admission. This is a
   statically traced path, not an executed outside-repository write.
3. Except for `delete-unreferenced`, refactor operations generally use a
   compatibility workspace load that does not carry authoritative input-set
   completeness. Rename, move, and move-slice therefore need either complete
   acquisition evidence or an explicit reviewed partial-scope contract before
   whole-solution mutation claims. The deletion operation already refuses
   when its absence evidence is incomplete.
4. Preview and apply prepare separately: the dry-run identifies affected paths
   without an immutable plan hash or repository revision. The writer checks
   changed-document content during an apply invocation, but move-only source
   checks establish existence rather than source-content identity. A reviewed
   preview therefore cannot authorize a later exact apply yet.

Two more effect details belong in plan evidence. Global `--decisions <file>` can
write pending YAML on an unresolved ambiguity even during a refactor dry-run;
previews must disclose and check that explicit path. `move --to-project`
automatically inserts consumer `ProjectReference` entries, whereas `move-slice`
requires explicit project-reference authorization and justification. Rename's
requested `--related-edit` IDs are reported as selected even if stale IDs match
no current candidate; a target-file collision can instead warn and skip a
requested file rename. Exact-set validation and revision-bound plans should
precede any apply path.

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

A later discoverability slice replaces the retired `find_refs` follow-on in
the `outline` result and the `add-dependency` and `make-sealed` application
definitions with the public `relationships` capability, naming its
`references` mode. The daemon's pilot
catalogue now projects those follow-ons from the same application definitions
instead of retaining copied entries. The mode is still prose in the follow-on
reason; typed invokable follow-on arguments remain part of the RT-016 workflow
contract work.

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

## Reviewed analysis conclusion

The per-command ledger, generated host census, source review, and two-repository
task corpus support the destinations in `surface-disposition.md` as the current
product recommendation. Keep the orient/find/understand route and distinct
semantic-diff/graph results; consolidate overlapping findings, inventories,
structural search, imports, and rewrite recipes without erasing typed variants;
remove low-value code actions and internal coordination from ordinary discovery.
Only reviewable plan/verify operations should be candidates for the preview
mutation surface, beginning with a single-symbol rename plan. Usefulness,
correctness, and safe reachability decide these moves; response size and call
count then guide bounded renderers and paging.

This completes a decision review of the *current known* surface, not runtime
certification of every mode. The final preview whitelist remains conditional on
the focused effect, trust, completeness, schema, and plan fixtures below. New
ancillary controls also need an authoritative inventory instead of the present
known-route list.

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
| 1 | Correct workspace completeness, trust/effect claims, and unsafe discovery. Reject contradictory dry-run/apply flags, enforce affected-path containment, hide `prepare-operation`, keep external/source-transmission modes explicit, and bound ambiguous candidate responses. | Healthy, partial, untrusted, ambiguous, and out-of-root-path fixtures show truthful outcomes through CLI and MCP; no unsupported workflow is suggested as runnable. |
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
