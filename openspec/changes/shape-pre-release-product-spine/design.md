# Design: pre-release product spine

## Design principles

1. **Ship a workflow, not a catalogue.** A user should be able to move from an
   unfamiliar change to evidence, impact, a reviewed plan, and verification without
   guessing which of dozens of commands to compose.
2. **Truth precedes convenience.** Workspace state, trust, provenance, confidence,
   limitations, and mutation state are part of results rather than optional logs.
3. **A smaller public façade may reuse a larger internal engine.** Consolidating the
   supported surface does not require deleting useful Roslyn services.
4. **Typed jobs remain typed.** Do not replace focused request schemas with a generic
   command string or untyped refactor endpoint.
5. **Preview scope removes unsafe architecture.** `0.1-preview` avoids distributed
   mutation and daemon dependencies instead of claiming they are already solved.
6. **One pilot per shape.** Prove one symbol query, one finding rule, one impact query,
   and one plan-only mutation end to end before converting siblings.
7. **The existing hardening change owns platform truth.** This design links to its
   gates; it does not create weaker copies.
8. **Usefulness precedes token cost.** Validate the user job, semantic evidence,
   and complete workflow before optimizing output bytes or call count.

## Authority and dependency boundaries

| Concern | Authoritative work | This change's responsibility |
|---|---|---|
| Repository trust and evaluator isolation | `critical-platform-hardening` P0.1/P0.5 | Define the narrower trusted-local preview policy and consume provenance. |
| Mutation containment, recovery, and dispatch truth | `critical-platform-hardening` P0.0-P0.4 | Remove apply from supported MCP preview and define plan-only product behaviour. |
| Host-neutral operation contracts | RT-007, ADR-0007, hardening P1.1/P1.2 | Add maturity/cost/prerequisite metadata and select the public operations. |
| MCP protocol and bounded structured output | hardening P1.3, RT-012 | Name the required product results and preview admission journeys. |
| Hermetic build and release | hardening P1.6-P1.8, RT-012 | Define the truthful preview package and surface being admitted. |
| Rules, recipes, discovery, impact, and verify | This change | Define new product contracts and migration paths. |

If the two changes disagree, the stricter safety or provenance requirement wins.

## Observed baseline

The live pre-release catalogue measured on 2026-08-31 reports:

| Measure | Value |
|---|---:|
| Ordinary commands | 56 |
| Read-only commands | 23 |
| Mutating commands | 33 |
| Entries without advertised JSON output | 41 |
| Installed MCP tools including ancillary operations | 61 |

On a working tree containing 37 changed C# paths, `rt suggest` reported three changed
files because display limiting occurred before the context count. It recommended an
`outline` command for each displayed file rather than classifying the semantic change
or constructing a workflow. This is useful evidence for the target design: discovery
must operate on semantic facts and distinguish total context from displayed items.

The [2026-10-01 pre-freeze audit](pre-freeze-surface-audit.md) supersedes those
historical counts for current-surface decisions: source-built HEAD has 60
catalogue entries (25 read/analyze/CI, 34 refactor including `trust`, and one
lifecycle entry). MCP discovery under the current trust profile has 60 tools over
three pages. The audit records live workflow failures, provisional command
dispositions, and response byte costs. The existing command-admission guard is
an interim growth control. Final surface ratification follows the representative
task-corpus gate.

## Target workflow

```text
explicit trust / no-evaluation decision
                 |
                 v
          doctor + capabilities
                 |
                 v
      search / outline / describe / source
                 |
                 v
      relationships + diagnostics + impact
                 |
                 v
          check + baseline comparison
                 |
                 v
          prepare immutable change plan
                 |
                 v
                  verify plan
                 |
                 `--> review-only completion (supported preview)

```

Optional direct public CLI apply is omitted from this preview target.

Every arrow is a typed next action. Guidance may recommend a step but never executes
it automatically.

## Release profiles

### Stable preview profile

The default CLI and MCP package exposes only read-only semantic operations that pass
all common admission gates. It may expose plan-only mutation requests because planning
writes no repository file.

### Experimental CLI profile

Opt-in recipes are CLI-only and plan-only. They are absent from normal MCP discovery,
carry no stable compatibility promise, and cannot make the stable preview claim
weaker. Optional direct public apply is omitted; existing hidden durable maintainer
contracts remain separate.

### Maintainer profile

Feedback administration, generated documentation, trace debugging, daemon controls,
and development diagnostics are accessible only through an explicit maintainer entry
point or separate package.

## Target public capabilities

| Family | Capability | Preview maturity | Purpose |
|---|---|---|---|
| Workspace | `doctor` | Stable candidate | Explain installed/runtime/workspace truth and why an operation can run. |
| Guidance | `suggest` | Preview then stable | Recommend an ordered, non-executing workflow from a goal or Git context. |
| Symbols | `search-symbols` | Stable candidate | Discover symbols without already knowing an FQN. |
| Symbols | `outline` | Stable candidate | Summarise files, directories, globs, or namespaces. |
| Symbols | `describe` | Stable candidate | Return compact semantic signatures. |
| Symbols | `source` | Stable candidate | Return focused source with explicit size bounds. |
| Symbols | `relationships` | Stable candidate | Return references, callers, callees, implementations, overrides, base, or derived edges. |
| Analysis | `diagnostics` | Stable candidate | Return compiler/analyzer diagnostics and before/after deltas. |
| Analysis | `semantic-diff` | Stable candidate | Describe declaration changes between Git/file states with limitations. |
| Analysis | `dependency-graph` | Stable candidate | Return a bounded graph, distinct from finding lists. |
| Analysis | `impact` | Preview then stable | Compose changes, relationships, projects, diagnostics, API risk, and candidate tests. |
| Analysis | `check` | Preview then stable | Execute selected evidence-based rules with common finding identities. |
| Analysis | `inventory` | Preview | Return non-judgemental inventories such as language features or imports. |
| Change | `prepare-*` | Preview | Produce an immutable, revision-bound plan for an admitted refactor. |
| Change | `verify-plan` | Preview | Report diagnostic, semantic, project, and candidate-test consequences without writing. |

Admitted `rename`, `change-signature`, and move variants retain separate typed
preparation schemas. `extract-method`, `extract-interface`, and
`delete-unreferenced` are conditional on the final pre-freeze whitelist and their
safety fixtures. A generic `prepare` router may group admitted operations in CLI
help, but it must preserve their discriminated contracts.

## Shared capability metadata

The host-neutral command definition gains one canonical metadata object:

```json
{
  "maturity": "Stable|Preview|Experimental|MaintainerOnly",
  "effects": "None|PlanOnly|RepositoryWrite|ExternalWrite|RepositoryCodeExecution",
  "defaultMode": "Inspect|Plan|Apply",
  "idempotency": "Idempotent|RevisionBound|OperationBound|Unknown",
  "workspaceRequirement": "None|ParseOnly|CompleteSemantic|PartialAllowed",
  "trustRequirement": "None|ExplicitLocalTrust|IsolatedEvaluation",
  "cost": "Small|Workspace|SolutionWide|ExternalProcess",
  "supportsCancellation": true,
  "defaultTimeoutMs": 30000,
  "defaultItemLimit": 100,
  "maximumOutputBytes": 1048576,
  "prerequisites": [],
  "knownLimitations": []
}
```

CLI help, generated documentation, MCP annotations/schema, `doctor`, and `suggest`
project from the same value. Conditional effects such as an explicit apply flag are
represented in request validation and response safety state rather than being hidden
by a single static read-only annotation.
Mode-dependent commands may also publish mode profiles with a canonical request
selector, default state, effects, trust and workspace requirements, prerequisites,
and an example. `doctor` uses this for installation-only and trusted-workspace
checks while its command-level effects remain the conservative union. CLI and MCP
project these profiles from the same definition; result safety records effects
actually reached.

## Shared result envelope

Every supported operation returns one versioned semantic envelope before host
projection:

```json
{
  "schemaVersion": "rt.result.v1",
  "operation": "relationships",
  "outcome": "Succeeded|Partial|Failed|TrustRequired|Ambiguous|Unsupported|Stale",
  "source": {
    "repositoryRoot": "...",
    "revision": "...",
    "workingTreeFingerprint": "..."
  },
  "workspace": {
    "state": "Complete|Partial|Failed|NotRequired",
    "solution": "...",
    "configuration": "Debug",
    "targetFrameworks": [],
    "loadedProjects": [],
    "failedProjects": [],
    "diagnostics": []
  },
  "data": {},
  "confidence": "ProvenWithinWorkspace|High|Moderate|Low|NotApplicable",
  "limitations": [],
  "nextActions": [],
  "page": null,
  "timing": {},
  "safety": {}
}
```

CLI JSON and MCP `structuredContent` serialize the same semantic DTO. Text is a view,
not a second contract. Structured diagnostics are always retained; a human display
flag may suppress their rendering but never remove them from structured output.

## Workspace truth and `doctor`

`doctor` combines product capability status with workspace acquisition evidence. It
does not replace the hardening change's completeness algorithm.

It reports:

- installed version, source, launchers, and available runtime;
- supported CLI/MCP protocol and schema versions;
- selected repository, solution/project entry point, configuration, and TFM;
- SDK, MSBuild, compiler, analyzer, and generator policy;
- explicit trust state and evaluator mode;
- `Complete | Partial | Failed | NotLoaded` workspace status and every load diagnostic;
- stable/preview/experimental/maintainer capability counts;
- time, memory, item, and output limits;
- daemon state only in maintainer builds, never as a stable dependency;
- actionable, copyable remediation steps.

For `0.1-preview`, an unapproved repository returns `TrustRequired` before MSBuild,
analyzers, generators, or `probe-di`-style code can run. The user-facing approval flow
must explain that solution loading may execute repository-controlled build logic.

## Symbol discovery and relationships

One symbol resolver serves every capability. Candidate ranking is deterministic:

1. exact stable symbol identity;
2. exact fully qualified name and signature;
3. exact simple name with matching kind;
4. suffix/namespace/project-filtered match;
5. explicitly requested fuzzy match.

Candidates carry symbol kind, full signature, project, namespace, declaring path,
accessibility, generic arity, and a stable identity suitable for the next request.
Locations assist humans but are not identities.

`relationships` uses a bounded graph query with modes:

- references;
- callers and callees;
- implementations and implemented-interface members;
- overrides and overridden member;
- base and derived types.

Temporary internal wrappers may preserve implementation coverage during preview, but MCP
exposes the canonical typed relation operation.

## Diagnostics, checks, and inventories

### Diagnostics

`diagnostics` returns compiler diagnostics by project/document and, when a before state
is supplied, classifies each as introduced, unchanged, or removed. Analyzer execution
requires the same explicit trust/evaluator policy as workspace loading.

### Findings

`check` rules return:

```json
{
  "findingId": "stable fingerprint",
  "ruleId": "architecture.circular-dependency",
  "severity": "Info|Warning|Error",
  "confidence": "High|Moderate|Low",
  "evidence": [],
  "locations": [],
  "limitations": [],
  "workspaceState": "Complete",
  "baselineState": "Introduced|Unchanged|Removed|NotCompared",
  "suggestedActions": []
}
```

The first baseline implementation writes a versioned JSON snapshot and compares a
later result. Direct Git-ref execution follows only after stable rule identities are
proven. Line movement, formatting, and path-normalisation differences do not create a
new finding identity.

`solid` does not survive as a public rule family. Its implementations become concrete
rules such as `design.too-many-dependencies`, `design.large-public-surface`, or
`design.concrete-constructor-dependency`, each documenting its heuristic basis.

`find-unused` becomes `usage.unreferenced-candidate`. It may support review, but it
cannot produce a deletion plan unless the workspace is complete and the symbol policy
permits a closed-workspace conclusion. External/public/dynamic activation limitations
remain visible even then.

Inventories such as language-feature density are descriptive rather than findings and
retain separate typed inventory rows under one family.

## Change impact

`impact` starts from either a stable symbol identity, a semantic diff, a prepared plan,
or a bounded set of changed paths. It composes existing primitives rather than
reimplementing their semantics.

The result groups:

- changed declarations and public surface classifications;
- callers, callees, implementations, overrides, base, and derived relationships;
- affected projects and downstream project dependencies;
- compiler/analyzer diagnostic deltas;
- source-compatibility risks and explicitly deferred binary-compatibility facts;
- conservative candidate test projects with evidence;
- generated-code, reflection, DI, markup, and external-consumer limitations;
- ordered next actions.

Candidate tests are suggestions, never proof that unlisted tests are unaffected.

## Workflow-aware guidance

`suggest` accepts either a goal or Git context. It returns zero or more ordered
workflows. A confidence threshold permits an honest `Unsupported` outcome rather than
forcing an irrelevant command match.

Each step includes:

- capability and populated typed arguments;
- reason for the step;
- maturity, effects, trust, cost, and prerequisites;
- expected output and stop condition;
- dependency on earlier step output;
- no-execute marker.

Initial recipes cover understanding a symbol, extending an interface, changing a DTO,
investigating a diagnostic, preparing a rename, and reviewing working-tree impact.
Displayed result limits do not change total context counts.

## Revision-bound change planning

The accepted version/profile decision is
[ADR-0106](../../../docs/adr/0106-review-only-change-plan-profile.md). It changes the
public planning target, not current implementation status:

| Canonical pair | Consumer and compatibility |
|---|---|
| Existing `rt.change-plan.v1`, implicit durable profile | Keep the existing immutable payload, envelope, reader, store/intake/apply proofs and conformance bytes unchanged. Do not add a profile field to v1. |
| `rt.change-plan.v2`, mandatory `profile: "review-only"` | Future public prepare/verify input through the same canonical writer/reader owners, with a separate typed review reader. Reject unknown pairs and never convert review claims into durable authority. |

A plan-only mutation will return immutable review fields:

```json
{
  "schemaVersion": "rt.change-plan.v2",
  "profile": "review-only",
  "planId": "...",
  "repository": {
    "root": "...",
    "solutionPath": "...",
    "baseRevision": "...",
    "sourceFingerprint": "...",
    "pathCaseSensitivity": "sensitive"
  },
  "createdAtUtc": "...",
  "expiresAtUtc": "...",
  "operations": [{"sequence": 1, "name": "rename", "stableInput": "...", "arguments": []}],
  "expectedInputs": [],
  "effects": [],
  "affectedPaths": [],
  "diff": "...",
  "diagnostics": [],
  "assumptions": [],
  "confidence": {"level": "...", "basis": "...", "evidence": []},
  "limitations": [],
  "workspace": {"state": "Complete", "manifestDiscoveryState": "Closed"},
  "verificationActions": []
}
```

This is a field illustration, not a complete valid plan, conformance vector or current
CLI output. The model implementation must establish independent exact-byte vectors.
The payload retains exact original/replacement bytes, derived diff and affected paths,
stable document/project bindings, and content/semantic/build input identities, including
dirty, unaffected and external inputs. Reuse `ChangePlanDiff` through a typed review seam
or private immutable projection; never fabricate durable `ChangePlanEffect` guards or
physical preconditions to call its existing overload. Preserve v1 diff bytes and fix
independent create/delete/replace/move, rename-plus-move, linked-context and
Unicode/escaping vectors before freezing the new byte oracle. A rename that also moves
a file freezes one move effect containing edited destination bytes; an unchanged rename returns a distinct
NoChanges outcome without a fabricated effect.

The canonical envelope retains exact payload bytes plus their lowercase SHA-256 hash.
This unkeyed digest establishes byte integrity/identity, not authentication or authority;
a caller can replace payload and digest together. Imported review inputs and workspace
values are untrusted claims in separate types from trusted host acquisition receipts and verified-stage results. The live host must
validate the complete admitted closure and replay frozen effects into the exact planned
snapshot; it must not rerun Renamer from saved arguments or elevate imported Complete
claims into trusted receipts.

Stateless Base64 envelope return is proposed implementation policy, not an approved
public transport default. The eventual CLI/MCP transport must preserve complete exact
review bytes within actual response/token limits or use an explicitly authorized
repository-external artifact handoff. A proposed 16 MiB decoded envelope does not imply
that its Base64 form and review projection fit normal response bounds: 16 MiB becomes
22,369,624 Base64 bytes before outer JSON, projection duplication and MCP overhead.
Bound encoded input before decoding, decoded bytes before parsing, item/scalar/depth
growth, and the actual complete host response. Proposed 30-minute lifetime, 16 MiB envelope, 32,768 input/binding records, 4,096 effects or
diagnostics, and 64 KiB identity/argument scalars are unmeasured policy candidates, not
ratified or fixed public defaults. Final limits need independent boundedness evidence;
limits must return explicit outcomes rather than a truncated usable plan. Protected
retention cannot borrow v1 store authority or fabricate a protection lease.

Planning and verification write no repository path, including during acquisition,
generator/analyzer evaluation, stale rejection, cancellation and resource failure.
`verify-plan` must report all required consequences over the exact frozen snapshot:
compiler diagnostic delta, analyzer diagnostic delta, semantic/public API delta,
unresolved references/call sites including generated callers, affected projects, and
conservative candidate tests with workspace state and limitations. Trusted local policy
mandates all six stages. The profile contains the exact locally supported set with
required=true; imported action names/capabilities/flags cannot remove, rename or disable
stages, dispatch arbitrary capabilities, or supply completion authority. Validate the
actual complete closure used throughout verification and reject drift before publishing
Verified, or prove equivalent immutable acquisition. Only every required stage
completing over a nonstale Complete workspace permits Verified; syntax-only API
comparison or missing/disabled/failed stages do not. Missing, expired, incompatible,
corrupt and stale inputs remain distinct; stale means prepare and review a new plan,
not silent recomputation.

The target preview omits optional direct public CLI apply. Public review plans terminate
at preparation and verification, with no MCP/daemon apply or daemon dependency. Current
apply-shaped public requests still require later migration/rejection evidence. Existing
hidden durable v1 maintainer contracts and the later prepare/apply/status/recover kernel
remain owned by `critical-platform-hardening`; this decision grants them no new authority
and relaxes none of their proofs. A future apply profile requires a separately accepted
compatibility/authority decision and exact reviewed-byte binding.

The initial model-only slice cannot complete M5.1, the public rename vertical slice,
full verification or M5 exit criteria. All live input, no-write, cross-process, CLI/MCP,
staleness, cancellation and resource acceptance evidence remains required.

## Rules and recipes

Rules are read-only functions producing common findings. Recipes are typed,
opinionated planners that may compose several internal transformations.

```text
rt check --rule architecture.circular-dependency
rt inventory language-features
rt recipe list
rt recipe plan use-time-provider
```

Recipes are experimental in this change. They reuse the change-plan envelope and
verification pipeline; they do not each invent `--review`, `--dry-run`, and `--apply`
semantics. General third-party loading is deferred because it expands the trust and
compatibility surface.

## CLI and output simplification

Before compatibility is promised:

- choose one canonical CLI grammar; categories are metadata rather than duplicated
  root and grouped command identities;
- remove cryptic aliases unless a demonstrated workflow justifies one;
- replace multiple output booleans with `--format text|json` for the supported
  contract; additional renderers may remain experimental views;
- remove the deprecated no-op `--strict-json` and avoid relaxed JSON in stable
  automation output;
- use one preview default and a separate exact-plan apply operation rather than
  inconsistent `--dry-run`, `--review`, and `--apply` combinations.

## Migration slices

Each slice owns one seam, one public behaviour, and one narrow gate:

1. **Pre-freeze workflow audit, surface census, and admission:** measure useful
   task completion before token cost; review every command and then ratify the
   final disposition. Metadata-only admission causes no operation behaviour change.
2. **Containment:** finish the existing dirty branch and hide apply paths.
3. **Envelope pilot:** migrate `outline` through identical CLI/MCP structured output.
4. **Workspace pilot:** add `doctor` by projecting the existing completeness model.
5. **Resolver pilot:** migrate `describe`, then search and relationships.
6. **Finding pilot:** adapt complexity to one rule, baseline it, then fan out rules.
7. **Impact pilot:** interface change from semantic diff through projects and tests.
8. **Plan pilot:** rename plan plus stale rejection and verify-plan.
9. **Recipe pilot:** one framework-specific codemod plans through the shared contract.
10. **Release:** packaged spawned-host and native-platform journeys.

Shared descriptors, DI registration, schemas, documentation generation, and
architecture allowlists are edited by one coordinator. Parallel conversions begin
only after the representative pilot is green.

## Documentation reconciliation

- ADR-0002 is amended or superseded so follow-ons and suggestions originate in
  host-neutral application metadata, not adapter-specific response decoration.
- RT-012's older daemon-as-execution-owner decision is superseded for preview by
  ADR-0008 and this release topology.
- The current mutation-containment branch is verified and integrated independently;
  this plan never assumes its dirty state is complete.
- OpenSpec owns accepted behaviour, ADRs own enduring design decisions, issues/PRs own
  execution, and generated status links to them rather than duplicating narrative.

## Alternatives rejected

### Stabilise all current commands

Rejected because it turns every experiment into a compatibility and safety obligation
before shared contracts are trustworthy.

### Delete every non-core implementation

Rejected because many services are valuable recipe/rule engines. Demoting their
public maturity preserves useful code without promising universal semantics.

### Keep the daemon as transparent passthrough in preview

Rejected for the supported profile because a pass-through adds lifecycle and transport
failure modes without caching benefit. It may remain an internal experiment.

### Finish distributed durable mutation before any release

Rejected as a preview prerequisite. Disabling supported apply removes the uncertain
transport path and permits useful read/plan feedback. Durable recovery remains
mandatory before stable or distributed apply.

### One generic MCP command executor

Rejected because it destroys typed schemas, discoverability, authorization, safety
classification, and independent command admission.
