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

#### Scenario: migrated commands suggest a reference lookup

- **WHEN** `outline` emits a result follow-on or the `add-dependency` and `make-sealed` application definitions advertise one
- **THEN** that follow-on names the public `relationships` capability and explains its `references` mode
- **AND** the legacy daemon projection uses the same application-defined follow-on metadata
- **AND** ordinary MCP discovery does not expose the retired `find_refs` identity

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

#### Scenario: compiler diagnostic delta is requested before the comparison route exists

- **WHEN** the user asks to compare compiler or analyzer diagnostics across revisions
- **THEN** CLI and MCP `suggest` return `Unsupported` with `compiler-diagnostic-delta` and a manual next step to build and compare explicit revisions
- **AND** no source mutation is recommended as a substitute
- **AND** current diagnostic, C# declaration comparison, architecture baseline, and diagnostic-fix requests are not reclassified by this guard

#### Scenario: configured project layering violations are requested

- **WHEN** the user explicitly asks to find or check violations of configured project or layer boundaries
- **THEN** CLI and MCP `suggest` return one invokable `check-architecture` read workflow using `.rt/architecture.json`
- **AND** the MCP step is populated only when an absolute solution path is supplied
- **AND** the stop condition calls out missing configuration, trust, errors, and the lack of workspace completeness in the current check result
- **AND** dependency/rule inventories, unconfigured architecture explanation, SOLID checks, baseline deltas, mutations, and custom configuration paths retain their existing discovery routes
- **AND** a compound request to extract all non-Microsoft core dependencies remains `Unsupported`

#### Scenario: proposed change impact is requested before impact exists

- **WHEN** the user explicitly asks what would break or requests an impact assessment for a proposed symbol or project change
- **THEN** CLI and MCP `suggest` return `Unsupported` with `change-impact` and manual reference, project-dependency, build, and test steps
- **AND** no mutation command is recommended as a substitute for impact evidence
- **AND** the response does not claim that missing source edges prove no reflection, generated-code, or external-consumer impact
- **AND** direct move, dependency, reference, configured architecture, plan-verification, and compiler-diagnostic-delta requests retain their existing routes

#### Scenario: references to one named symbol are requested

- **WHEN** the user explicitly asks to find, show, or list references to one complete named C# symbol without a kind cue or with a supported `type`, `method`, `field`, `property`, or `event` cue
- **THEN** CLI and MCP `suggest` return one invokable `relationships references` read step with the selected symbol, supported kind, and solution
- **AND** the MCP step is populated only when an absolute solution path is supplied
- **AND** the stop condition treats ambiguous, `Partial`, or truncated results as incomplete and does not infer that reflection, generated code, or external consumers are absent
- **AND** requests with project, file, test, kind, limit, revision, or solution qualifiers that the step cannot represent do not receive the unqualified workflow
- **AND** constructor, namespace, parameter, member, and C# type-subtype cues that the resolver cannot select exactly retain general discovery rather than silently changing the requested subject
- **AND** unspecified, generic, multi-symbol, project/package-reference, and proposed-change impact requests retain their earlier discovery routes

#### Scenario: method staticness conversion has an explicit direction

- **WHEN** the user explicitly asks to convert an instance method to static or a static method to instance/non-static
- **THEN** CLI and MCP `suggest` return only the matching `make-static` or `make-instance` recommendation, even at a display limit of one
- **AND** the opposite mutation is absent from the response
- **AND** the response does not invent an executable plan or a concrete target method
- **AND** a supplied method name or signature does not reverse the selected direction or create a target selection
- **WHEN** the matching command is unavailable
- **THEN** `suggest` returns `Unsupported` naming that command instead of substituting the opposite mutation
- **WHEN** the explicit conversion is coupled to another task
- **THEN** `suggest` returns `Unsupported` with `method-staticness-workflow` and manual next steps instead of recommending one mutation as the whole task
- **AND** punctuation and politeness around a single conversion do not create a second task
- **AND** a stated motivation such as performance and testability does not create a second task
- **AND** an explicit constraint on caller updates does not silently become an unconstrained mutation recommendation
- **AND** a later negation or static-class/import task does not hide an earlier positive method conversion
- **AND** an earlier class or import task does not hide a later positive conversion, including unparsed method syntax
- **AND** a later static-class or static-import task does not hide an earlier explicit but unparsed method conversion
- **AND** an earlier class or import task does not hide a later qualified but unparsed method conversion
- **AND** an unrecognized surrounding clause is reported as `Unsupported` rather than assumed to be part of a single conversion
- **AND** explicit direction with unparsed method syntax is reported as `Unsupported` rather than sent to general command ranking
- **WHEN** the requested target is a property or field rather than a method
- **THEN** `suggest` returns `Unsupported` with `method-kind-conversion` instead of recommending a method staticness recipe
- **WHEN** the source itself is a property or field whose staticness is to change
- **THEN** `suggest` returns `Unsupported` with `member-staticness-conversion` instead of claiming a method workflow
- **AND** later static-class or static-import clauses do not hide that property or field source
- **AND** earlier class, import, or member-deletion clauses do not hide a later property or field conversion
- **AND** changing another quality of an already-static property or field does not claim a staticness conversion
- **AND** static method or static analysis context after a property or field does not claim a staticness conversion
- **AND** adding or removing the static modifier on a property or field reports the member-staticness capability
- **AND** named, qualified, quoted, plural, and mixed property/field conversion requests retain the member kind
- **AND** a negated member conversion remains general discovery unless a later positive conversion is requested
- **AND** another change to a static member earlier in the request does not hide a later member conversion
- **AND** a static-class context request with a property or field qualifier but no migration target retains general discovery
- **AND** creating a static property or field returns `Unsupported(static-member-creation)` with no mutation recommendation
- **AND** ordinary deletion of a static property or field returns `Unsupported(static-member-deletion)` rather than suggesting unproven `delete-unreferenced` or a method recipe
- **AND** standalone negated creation or deletion returns `NoMatch`, while a later positive member change keeps its own missing capability
- **AND** editing references, usages, reads, writes, or access sites of a static property or field, including reference-targeted staticness wording, returns `Unsupported(member-reference-edit)` with empty results rather than suggesting deletion or conversion of the declaration
- **AND** standalone negated use-site edits return `NoMatch`, while named reference searches keep their read workflow
- **AND** static import, inventory, attribute, accessor, initializer, declaration-accessibility, and existing-member modifier requests retain their separate routes
- **AND** an independently named missing plan capability retains its existing `Unsupported` outcome
- **AND** static-import and static-class inventory queries retain general catalogue discovery
- **WHEN** the user explicitly asks to migrate a static class to an instance class or make it injectable
- **THEN** CLI and MCP `suggest` return `Unsupported` with `static-class-migration`, empty results, and no method mutation recipe
- **AND** the reason names the missing complete migration workflow and identifies static-class inventory as preliminary read evidence
- **AND** an earlier or later positive method conversion retains its method-specific route
- **AND** an earlier non-migration class action does not hide a later class migration
- **AND** direct instance-class, migration, replacement, and dependency-injection wording retains that outcome
- **AND** a negated class migration or an injectable target belonging to a separate non-static-class action does not claim class migration
- **AND** a named source before `from a static class` and an injectable-service destination retain the migration outcome
- **AND** an instance class mentioned only as calling context, or a negated target, does not claim a migration
- **AND** field/property qualifiers, demonstratives, and direct instance targets preserve explicit migration intent
- **AND** a class merely described as working with an instance class, or a prohibition with an intervening modifier, retains non-migration discovery
- **AND** quoted source names and comma-separated field/property/method qualifiers retain migration intent
- **AND** returning or accepting another class, or targeting an instance method, field, or property, does not claim class migration
- **WHEN** a method staticness conversion is explicitly negated with no later positive task
- **THEN** a standalone prohibition returns `NoMatch` without a mutation recommendation
- **AND** a request to find callers or usages without an exact method target returns `Unsupported` with `method-relationship-target` and asks for a named method
- **AND** a separate exact named-reference clause uses the existing `relationships references` read workflow without a mutation recommendation
- **AND** any unrecognized follow-on clause does not fall through to mutation ranking
- **AND** a later positive method conversion is not hidden by the earlier prohibition

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
