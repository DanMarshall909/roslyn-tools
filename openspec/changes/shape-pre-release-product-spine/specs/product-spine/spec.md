# Pre-release product spine specification

## ADDED Requirements

### Requirement: preview product scope is explicit and enforceable

The packaged `0.1-preview` product SHALL describe itself as a structured C#
understanding and change-planning tool for explicitly trusted local repositories.
Stable capabilities SHALL be read-only. MCP-exposed mutation capabilities SHALL be
plan-only and SHALL NOT apply repository changes. The supported preview package SHALL
NOT depend on, start, or advertise the daemon.

#### Scenario: client discovers the supported MCP surface

- **GIVEN** the packaged `0.1-preview` MCP server
- **WHEN** a client requests `tools/list`
- **THEN** every exposed tool declares maturity, effects, default mode, prerequisites,
  trust/workspace requirements, cost, cancellation, and resource limits
- **AND** no exposed tool can apply repository changes
- **AND** experimental, maintainer-only, incomplete, and removed tools are absent
- **AND** daemon availability does not change the discovered surface

#### Scenario: maintainer capabilities are installed

- **GIVEN** a package containing feedback or trace-development utilities
- **WHEN** an ordinary MCP client discovers tools
- **THEN** feedback administration, trace debugging, daemon lifecycle, and generated-
  documentation operations are absent
- **AND** a maintainer can invoke them only through an explicit maintainer surface

### Requirement: command admission is catalogue-derived

Final preview-surface admission SHALL follow a review of every live CLI and MCP
capability against representative user jobs, including overlap, usefulness,
completeness, effects, audience, and measured call/output cost. Usefulness and
safety SHALL take precedence over output size. An interim guard against new
standalone public commands SHALL NOT be treated as final surface ratification.

Every public capability SHALL have one host-neutral definition containing its request
schema, result schema, maturity, effects, conditional mutability, idempotency, trust
and workspace requirements, prerequisites, cost, time/item/byte limits, known
limitations, examples, and next-action metadata. CLI help, generated documentation,
MCP discovery, `doctor`, and `suggest` SHALL project from that definition.

#### Scenario: command lacks safety metadata

- **WHEN** a command is proposed for stable or preview discovery without complete
  maturity, effects, prerequisites, workspace, and resource metadata
- **THEN** command admission fails in CI
- **AND** the command remains experimental or undiscoverable

#### Scenario: final public surface is proposed

- **WHEN** the preview command set is submitted for final freeze
- **THEN** every live command and MCP tool has an evidence-reviewed disposition
- **AND** representative supported tasks have an ordered, invocable workflow
- **AND** unavailable tasks return an honest unsupported outcome instead of
  recommending unrelated or unsafe commands
- **AND** measured output and call costs preserve required completeness and safety
  evidence

#### Scenario: conditionally mutating command is described

- **GIVEN** a capability that can inspect by default and mutate only through a
  separately authorized lifecycle
- **WHEN** its metadata is projected to CLI and MCP
- **THEN** the default operation is not described as mutating
- **AND** the apply operation is represented separately with its actual effects and
  authorization requirements
- **AND** no static read-only annotation conceals a write-capable request mode

### Requirement: supported results share one semantic envelope

Every supported preview operation SHALL return a versioned result envelope carrying
operation outcome, source revision/fingerprint, workspace provenance, data,
confidence, diagnostics, limitations, next actions, safety state, timing, and paging
metadata. CLI JSON and MCP `structuredContent` SHALL serialize the same semantic DTO.
Text output SHALL be a bounded view rather than a separate result contract.

#### Scenario: CLI and MCP invoke the same operation

- **GIVEN** equivalent requests submitted through CLI and MCP
- **WHEN** the operation succeeds, partially succeeds, or fails
- **THEN** their semantic data, outcome, diagnostics, provenance, confidence,
  limitations, safety, and next-action ordering are equivalent
- **AND** MCP returns schema-valid `structuredContent`
- **AND** diagnostics remain structured even when human text suppresses their display

#### Scenario: result exceeds the default item limit

- **WHEN** a supported operation produces more items than its default limit
- **THEN** the response reports the total independently of displayed/returned count
- **AND** returns a revision-bound continuation token or explicit truncation state
- **AND** never labels the truncated count as the total context

### Requirement: workspace evaluation requires explicit trust and truthful provenance

Semantic workspace evaluation SHALL occur only for an explicitly approved local
repository in `0.1-preview`. An absent approval SHALL return a typed `TrustRequired`
outcome before MSBuild, analyzers, generators, compiled assemblies, or repository code
execute. Every semantic result SHALL carry the `Complete | Partial | Failed` workspace
contract defined by `critical-platform-hardening`.

#### Scenario: unapproved repository is analysed

- **GIVEN** a repository without explicit evaluation approval
- **WHEN** a user requests semantic symbol analysis
- **THEN** `rt` does not evaluate the workspace
- **AND** returns `TrustRequired`
- **AND** explains that workspace loading may execute repository-controlled build logic
- **AND** provides the exact deliberate opt-in path without granting trust itself

#### Scenario: one required project fails to load

- **GIVEN** an approved solution where one required project cannot load
- **WHEN** a semantic operation completes with remaining projects
- **THEN** the result is `Partial`
- **AND** identifies every failed project and diagnostic
- **AND** downgrades affected confidence
- **AND** suppresses proof-like unused-code conclusions and deletion plans

### Requirement: doctor explains product and workspace reality

`doctor` SHALL return a versioned capability/workspace report including installed
version/source, launcher health, protocol/schema support, selected repository and
solution/project, configuration/TFM, SDK/MSBuild/compiler, analyzer/generator policy,
trust/evaluator state, workspace completeness and diagnostics, maturity counts,
resource limits, and actionable remediation.

#### Scenario: multiple solutions are present

- **GIVEN** a directory containing more than one supported solution entry point
- **WHEN** `doctor` or another semantic capability omits solution selection
- **THEN** `rt` returns deterministic candidates
- **AND** does not silently choose one
- **AND** each candidate can be passed directly to a subsequent request

#### Scenario: required SDK is unavailable

- **WHEN** the selected repository requires an unavailable SDK
- **THEN** `doctor` reports the requested version and `global.json` path
- **AND** marks semantic workspace readiness as failed
- **AND** distinguishes the unavailable SDK from repository trust or protocol failure

### Requirement: symbols are discoverable before their exact identity is known

`search-symbols` SHALL support partial or exact name, kind, project, namespace,
accessibility, generic arity, and signature filters. Results SHALL use deterministic
ranking and carry stable symbol identity, full signature, kind, project, namespace,
and declaring path. Shared resolution SHALL return candidates for every ambiguous or
unresolved symbol request.

#### Scenario: two projects contain the same simple type name

- **GIVEN** two types named `Result` in different projects
- **WHEN** a user searches for or supplies the simple symbol `Result`
- **THEN** both fully qualified candidates are returned with stable identities
- **AND** neither candidate is silently selected
- **AND** the response explains which additional filters disambiguate them

#### Scenario: one exact symbol identity is supplied

- **WHEN** a stable symbol identity from a prior response is supplied to another
  capability against the same source revision
- **THEN** that symbol is selected without fuzzy matching
- **AND** a revision mismatch produces a typed stale/re-resolution outcome

### Requirement: focused source is byte-bounded and revision-addressable

`source` SHALL cap the UTF-8 bytes returned across source-text fields by a caller-
selectable budget no greater than the capability's declared maximum. Subject
declarations SHALL consume that budget in deterministic declaration order before any
requested related declarations. Every response SHALL report the total and returned
source bytes, the exact byte range returned for each declaration, the source revision,
and explicit truncation state. A truncated response SHALL provide an opaque
continuation bound to the same symbol request, related-source selection, and source
revision. Chunking SHALL NOT split a UTF-8 scalar value.

#### Scenario: focused source exceeds the requested byte budget

- **GIVEN** a symbol whose normalized declaration source exceeds the requested source-
  byte budget
- **WHEN** focused source is requested
- **THEN** the combined UTF-8 bytes in every returned source-text field do not exceed
  the requested budget
- **AND** the result reports total bytes independently from returned bytes
- **AND** each returned declaration reports its total bytes and half-open returned byte
  range
- **AND** the result is marked truncated and contains a continuation token

#### Scenario: caller continues a truncated source response

- **GIVEN** a continuation token from a truncated focused-source response
- **WHEN** the caller repeats the same request with that token against the same source
  revision
- **THEN** the next byte range begins exactly where the prior response ended without a
  gap, overlap, or split UTF-8 scalar value
- **AND** subject declarations remain ordered before requested related declarations
- **AND** a changed source revision returns a typed stale outcome instead of reading a
  different range
- **AND** a token used with different symbol or related-source arguments is rejected as
  invalid input

### Requirement: semantic relationships use one bounded graph contract

The relationships capability SHALL support references, callers, callees,
implementations, implemented members, overrides, overridden members, base types, and
derived types. Traversal SHALL be explicitly bounded by depth, items, time, and source
revision and SHALL disclose edges it cannot observe.

#### Scenario: interface method relationships are requested

- **GIVEN** a complete workspace and an interface method
- **WHEN** callers and implementations are requested
- **THEN** the result distinguishes direct references, interface dispatch, and concrete
  implementations
- **AND** reports generated, reflection, DI, markup, and external-consumer limitations

### Requirement: diagnostics and change impact close the semantic workflow

`diagnostics` SHALL return compiler/analyzer diagnostics scoped by solution, project,
document, or changed source and SHALL classify before/after diagnostics when a baseline
is supplied. `impact` SHALL compose semantic changes, relationships, project
dependencies, diagnostic deltas, source-compatibility risks, and conservative candidate
test projects without presenting incomplete evidence as proof.

#### Scenario: interface contract changes in the working tree

- **GIVEN** a complete workspace with a changed interface method
- **WHEN** the user requests working-tree impact
- **THEN** `rt` reports changed declarations, implementations, callers, affected
  projects, introduced/removed diagnostics, source-compatibility risks, and candidate
  test projects
- **AND** explains the evidence for every candidate test project
- **AND** recommends ordered inspection and planning steps
- **AND** executes none of those steps automatically

#### Scenario: impact crosses an unobservable runtime boundary

- **GIVEN** a symbol that may be activated through reflection, DI scanning, generated
  code, markup, or an external consumer
- **WHEN** impact is calculated
- **THEN** the boundary is listed as a limitation
- **AND** absence of a source edge is not reported as proof of no impact

### Requirement: checks use stable evidence-based findings and baselines

`check` SHALL execute named rules that return a common finding envelope containing a
stable finding ID, rule ID, severity, confidence, evidence, locations, limitations,
workspace state, baseline state, and suggested actions. Rule names SHALL describe the
detected evidence rather than claim broad design-principle violations. The first
baseline format SHALL be a versioned JSON snapshot.

#### Scenario: existing debt and one new finding coexist

- **GIVEN** a saved compatible check baseline
- **AND** the working tree introduces one additional finding
- **WHEN** the current result is compared with the baseline
- **THEN** the new finding is `Introduced`
- **AND** retained findings are `Unchanged`
- **AND** absent prior findings are `Removed`
- **AND** harmless line movement or formatting does not change finding identity

#### Scenario: a SOLID heuristic is projected

- **WHEN** an existing SOLID detector finds a class with many dependencies
- **THEN** the public rule identifies the concrete evidence such as
  `design.too-many-dependencies`
- **AND** reports its configurable threshold and heuristic limitations
- **AND** does not label the class as a proven SOLID violation

#### Scenario: unreferenced candidate comes from partial input

- **GIVEN** a partial workspace
- **WHEN** `usage.unreferenced-candidate` finds no loaded source references
- **THEN** the finding is confidence-qualified
- **AND** no delete plan or proof-like safe recommendation is produced

### Requirement: workflow suggestions are ordered, contextual, and honest

`suggest` SHALL accept a natural-language goal or use Git context when the query is
omitted. It SHALL return ordered workflows with populated typed arguments, rationale,
maturity, effects, trust, cost, prerequisites, expected outputs, dependencies, and stop
conditions. It SHALL apply a confidence threshold and return `Unsupported` rather than
forcing an irrelevant command match.

#### Scenario: query is omitted in a changed working tree

- **GIVEN** a working tree with ten changed C# files
- **WHEN** `suggest` is invoked without a query and the display limit is three
- **THEN** the response reports ten total changed C# files
- **AND** returns at most three displayed items where appropriate
- **AND** classifies changed symbols semantically
- **AND** recommends an impact workflow rather than only one outline per file

#### Scenario: user asks to extend an interface

- **WHEN** the user asks how to extend an interface safely
- **THEN** `suggest` returns an ordered workflow covering symbol resolution,
  implementations, impact, planning, and verification
- **AND** every step is directly invokable using the preceding structured output
- **AND** no mutation is executed automatically

#### Scenario: requested capability is absent

- **WHEN** no command or workflow exceeds the configured relevance threshold
- **THEN** `suggest` returns `Unsupported`
- **AND** names the missing capability
- **AND** does not substitute an unrelated command merely to return a result

#### Scenario: package inventory or wholesale core dependency extraction is requested

- **WHEN** the user asks for NuGet package inventory or all non-Microsoft dependencies to be extracted from core layers
- **THEN** CLI and MCP `suggest` return `Unsupported` with a named missing capability and an actionable manual next step
- **AND** do not recommend symbol dependency graphs or source extraction commands as if they completed that task
- **AND** ordinary symbol dependency, project dependency, and interface extraction requests retain their existing routes

#### Scenario: explicit C# working-tree review has a platform-limited reader

- **WHEN** the user asks to review uncommitted C# changes on Linux x64
- **THEN** `suggest` returns an invokable `semantic-diff` CLI step
- **AND** includes an MCP step when an absolute solution path identifies a target directory
- **AND** does not execute either step during suggestion
- **WHEN** the same task is requested on a platform without the contained reader
- **THEN** `suggest` returns `Unsupported` with `semantic-diff-working-tree`
- **AND** names ref-vs-ref and two-file comparison as alternatives without emitting a working-tree call
- **WHEN** the user requests unstaged-only changes
- **THEN** `suggest` reports the missing mode without emitting a broader comparison
- **WHEN** the user requests one unambiguous non-HEAD working-tree baseline
- **THEN** `suggest` carries that ref to CLI `--from` and MCP `from` when a target directory is known
- **WHEN** the baseline is ambiguous, partial, or conflicting
- **THEN** `suggest` reports `Unsupported` and points to `semantic-diff --from <ref>` without emitting a HEAD comparison

### Requirement: selected mutations prepare immutable revision-bound plans

Preview mutation capabilities SHALL produce an immutable plan containing plan ID,
schema version, source revision/fingerprint, stable input symbol identity, exact diff,
affected paths, expected input states, diagnostics, assumptions, limitations,
workspace state, verification actions, and expiry. Planning SHALL write no repository
file. MCP SHALL expose planning and verification only.

#### Scenario: rename plan is prepared through MCP

- **GIVEN** a complete approved workspace and an unambiguous symbol
- **WHEN** a rename plan is requested through MCP
- **THEN** the response contains the complete immutable plan and exact diff
- **AND** no repository path changes
- **AND** no apply operation is exposed through MCP

#### Scenario: source changes after plan review

- **GIVEN** a plan bound to current source content
- **AND** an affected input changes after planning
- **WHEN** the plan is verified or an experimental CLI apply is requested
- **THEN** the plan is `Stale`
- **AND** no file is written
- **AND** the result requests preparation and review of a new plan
- **AND** the old plan is not silently recomputed

### Requirement: plan verification reports consequences without applying

`verify-plan` SHALL report compiler/analyzer diagnostic delta, semantic/public API
delta, unresolved references or call sites, affected projects, conservative candidate
tests, workspace state, and limitations against the exact planned solution snapshot.

#### Scenario: planned change introduces a compiler error

- **GIVEN** a prepared change plan
- **WHEN** its planned solution introduces a compiler error
- **THEN** verification reports the introduced diagnostic and affected project
- **AND** the plan is not described as verified
- **AND** repository files remain unchanged

### Requirement: opinionated codemods are recipes rather than stable commands

Framework-specific and multi-stage codemods SHALL be classified as experimental
recipes and SHALL reuse the shared plan and verification contracts. A recipe SHALL NOT
invent independent review/dry-run/apply semantics. Repository-code-executing recipes
SHALL be trusted CLI-only and absent from normal MCP discovery.

#### Scenario: TimeProvider migration is planned

- **WHEN** the experimental `use-time-provider` recipe is requested
- **THEN** it returns a revision-bound plan through the shared envelope
- **AND** identifies constructor/API effects and framework assumptions
- **AND** uses the shared verification pipeline
- **AND** carries no stable compatibility promise

#### Scenario: DI probe is requested through ordinary MCP

- **WHEN** an ordinary MCP client searches for or invokes a DI probing capability
- **THEN** no such public tool is exposed
- **AND** repository assembly execution cannot occur through the normal preview surface

### Requirement: preview release admission tests the packaged product

The preview SHALL remain versioned below `1.0` and SHALL pass native packaged CLI/MCP
journeys on every supported operating system. Tests SHALL cover install, launch,
`doctor`, representative semantic operation, protocol negotiation, tool discovery,
structured success/failure, cancellation, output bounds, and uninstall. Build and test
SHALL not modify source or user installation/configuration state implicitly.

#### Scenario: packaged MCP request is cancelled

- **GIVEN** a packaged MCP process analysing a bounded fixture
- **WHEN** its client cancels a long read request
- **THEN** work terminates within the documented bound
- **AND** the server returns a protocol-correct cancellation outcome
- **AND** a subsequent request succeeds
- **AND** no repository state changes

#### Scenario: preview is installed without a daemon

- **WHEN** a user installs and runs the supported preview package
- **THEN** CLI and MCP operations function without daemon lifecycle setup
- **AND** `doctor` reports the supported direct topology
- **AND** documentation states trusted-local and plan-only limitations
