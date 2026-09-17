## ADDED Requirements

### Requirement: Trusted read-only calls reuse a current immutable workspace
Within one MCP or daemon process lifetime, the system SHALL reuse one loaded Roslyn
workspace for sequential doctor, outline, describe, search_symbols, source, and
relationships calls with the same cache identity while freshness remains proven.

#### Scenario: sequential semantic operations share one load
- **WHEN** two named read-only semantic operations target the same trusted solution without a relevant input change
- **THEN** the system opens the solution exactly once and both operations query the same immutable snapshot revision

#### Scenario: cache restart or eviction is transparent
- **WHEN** an entry is evicted or the owning process restarts
- **THEN** the next operation performs a cold load and returns the same contract and semantic result as a clean direct load

### Requirement: Cold loads coalesce and query leases are concurrent
The system SHALL coalesce acquisition for one cold cache identity and SHALL NOT hold
the acquisition gate while a read-only semantic query uses its immutable lease.

#### Scenario: concurrent cold operations open once
- **WHEN** concurrent read-only operations acquire the same uncached identity
- **THEN** exactly one `OpenSolutionAsync` call runs and every caller receives a lease for the published snapshot

#### Scenario: read-only queries overlap safely
- **WHEN** two callers hold leases for the same published snapshot
- **THEN** both may query it concurrently and neither caller can dispose the owning workspace

### Requirement: Cache identity isolates worktrees, solutions, and trust bindings
The cache key SHALL include the canonical absolute solution, canonical worktree root,
repository trust identity, and semantic environment identity. Trust SHALL be
reauthorized before every acquisition.

#### Scenario: linked worktrees remain isolated
- **WHEN** two registered linked worktrees share a Git common directory but have different worktree roots or content
- **THEN** they receive distinct cache entries and neither observes the other worktree's snapshot

#### Scenario: solutions remain isolated
- **WHEN** two solution paths exist under one trusted worktree
- **THEN** each solution receives its own entry and revision

#### Scenario: revoked or mismatched trust cannot reuse an entry
- **WHEN** current trust authorization no longer admits the requested canonical solution and worktree binding
- **THEN** acquisition fails closed before returning a cached lease

### Requirement: Relative solutions never depend on process current directory
A relative solution path SHALL resolve only beneath an explicit configured trusted
repository root. A multi-root persistent-trust host SHALL require an absolute solution
path. Resolution SHALL reject traversal, symlink, or canonicalization outside the
trusted root.

#### Scenario: explicit-root relative path is contained
- **WHEN** a caller supplies `src/App.slnx` under an explicit trusted root
- **THEN** the system resolves it from that root regardless of the server process current directory

#### Scenario: another checkout cannot be selected relatively
- **WHEN** a relative path would escape the explicit trusted root or a persistent multi-root request supplies a relative solution
- **THEN** the request is rejected before trust admission, SDK selection, workspace loading, or cache lookup

#### Scenario: ordinary CLI resolution remains invocation-relative
- **WHEN** a CLI command supplies a relative solution or relies on ordinary solution discovery from its invocation directory
- **THEN** the CLI resolves that input into the typed read-only request before daemon dispatch, direct execution, or pre-dispatch fallback and preserves existing mutation behavior

#### Scenario: an explicit symlink root uses one canonical identity
- **WHEN** a single-root host explicitly trusts a symlink whose physical target contains the requested solution
- **THEN** the configured root and requested solution are compared using the same canonical representation and the contained solution is admitted

### Requirement: Relevant input changes refresh before semantic conclusions
Before reusing an entry, the system SHALL prove a deterministic content fingerprint
covering source and semantic build inputs, analyzer/generator inputs, configuration,
and relevant environment identity. It SHALL reload when the fingerprint differs or
cannot be proven; Git HEAD alone SHALL NOT establish freshness.

#### Scenario: unstaged or untracked source change refreshes
- **WHEN** a source file is modified or a relevant untracked source file is added without changing Git HEAD
- **THEN** the next acquisition opens the solution again and exposes a new matching snapshot revision

#### Scenario: build or analyzer input change refreshes
- **WHEN** a solution, project, props/targets, global.json, analyzer/generator, configuration, metadata reference, or relevant environment input changes
- **THEN** the next acquisition reloads before returning a semantic lease

#### Scenario: newly applicable assembly candidates refresh
- **WHEN** a DLL, executable, or Windows metadata file appears beneath the project tree where default Content or None items can make it precede an existing reference hint path
- **THEN** the next acquisition reloads before returning symbols from the newly resolved reference graph

#### Scenario: explicit external None assembly candidates refresh
- **WHEN** an explicitly declared external None item can participate in reference resolution and its contents or existence changes
- **THEN** the next acquisition reloads before returning symbols from the newly resolved reference graph

#### Scenario: custom assembly search paths fail closed
- **WHEN** authored, imported, or environment configuration overrides assembly or design-time assembly search paths or allowed reference-assembly extensions
- **THEN** the loaded entry is non-reusable unless the full precedence-sensitive candidate set and missing transitions are proven

#### Scenario: configured rulesets fail closed
- **WHEN** authored, imported, or environment configuration selects a code-analysis ruleset, ruleset search directory, or directly resolved ruleset
- **THEN** the loaded entry is non-reusable unless the effective ruleset and recursive external include graph are proven

#### Scenario: signing inputs fail closed
- **WHEN** authored, imported, environment, loaded compiler-option, or semantically resolved source/generated assembly-attribute inputs select a strong-name key file, key container, delay signing, or public signing
- **THEN** the loaded entry is non-reusable unless all effective signing inputs and external key state are proven

#### Scenario: SDK-generated Git revision changes refresh
- **WHEN** the effective worktree HEAD changes without an authored source change, including an empty commit or ref update
- **THEN** the next acquisition observes the worktree-private HEAD and common ref storage and reloads before returning SDK-generated semantic output

#### Scenario: effective Git configuration changes refresh
- **WHEN** effective local, worktree, global, system, included, or conditional Git configuration changes semantic build metadata such as the repository URL
- **THEN** the next acquisition reloads before returning generated semantic output

#### Scenario: Source Link configuration discovery is covered
- **WHEN** Source Link's program-data, system, XDG, user-profile, repository, or recursively included configuration changes independently of native Git's selected configuration
- **THEN** the next acquisition reloads, or reuse is disabled when reader parity cannot be proven

#### Scenario: minimal Source Link repository metadata remains observable
- **WHEN** a repository supplies a valid `HEAD` and repository configuration without an object database
- **THEN** those inputs are fingerprinted and their changes refresh generated semantic output

#### Scenario: invalid nested Git marker does not hide the repository
- **WHEN** a project path encounters a definitively invalid `.git` candidate before a valid ancestor repository
- **THEN** Git semantic inputs are resolved from the valid ancestor while the invalid candidate remains fingerprinted for later transitions

#### Scenario: Git is additive rather than authoritative
- **WHEN** Git HEAD is unchanged but an unstaged or untracked semantic input changes
- **THEN** the authored and filesystem input fingerprint still refreshes; Git HEAD is never used as the sole freshness key

#### Scenario: unproven Git resolution fails closed
- **WHEN** environment redirection, unsupported ref storage, unreadable or ambiguous worktree Git metadata, or failed effective configuration capture prevents generated Git-derived metadata from being proven
- **THEN** the loaded entry is non-reusable rather than serving generated assembly information from an assumed HEAD

#### Scenario: newly applicable analyzer configuration refreshes
- **WHEN** an applicable ancestor `.editorconfig` or `.globalconfig` appears after a snapshot is loaded
- **THEN** the next acquisition fingerprints that missing-to-present transition and reloads

#### Scenario: implicit restore and evaluated build inputs are conservative
- **WHEN** implicit NuGet imports, restore assets, generated build inputs, or an evaluated import graph cannot be completely enumerated
- **THEN** the loaded entry is non-reusable unless those inputs and their missing-to-present transitions are included in the fingerprint

#### Scenario: effective restore paths and package imports are covered
- **WHEN** imported configuration or the workspace loader redirects restore outputs, or a package props/targets file imports an external build input
- **THEN** the effective restore inputs and recursively imported literal dependencies are fingerprinted, or the entry is non-reusable

#### Scenario: implicit project-extension imports are covered
- **WHEN** a matching `<project-file>.*.props` or `<project-file>.*.targets` appears in the effective project extensions directory
- **THEN** the addition changes the fingerprint and its dependency graph is traversed or the entry is non-reusable

#### Scenario: conditional restore assignments fail closed
- **WHEN** a restore-path assignment depends on a containing property group or choice condition whose effective value is not proven
- **THEN** the loaded entry is non-reusable instead of fingerprinting only the last textual assignment

#### Scenario: execution-phase restore assignments fail closed
- **WHEN** a restore-path assignment appears beneath a target rather than project evaluation
- **THEN** it is not treated as the effective path and the loaded entry is non-reusable

#### Scenario: an explicit project assets path is covered
- **WHEN** project configuration assigns a literal `ProjectAssetsFile` outside the conventional intermediate-output directory
- **THEN** that effective assets file is fingerprinted, while a dynamic or unproven imported override makes the entry non-reusable

#### Scenario: filesystem-dependent build evaluation is conservative
- **WHEN** project or imported build XML contains a filesystem-dependent property, item, import, or condition whose inputs cannot be completely enumerated
- **THEN** the loaded entry is non-reusable rather than treating the unchanged XML as a freshness proof

#### Scenario: unproven property functions fail closed
- **WHEN** project or imported build XML evaluates an MSBuild or CLR property function whose external inputs are not completely proven
- **THEN** the loaded entry is non-reusable instead of treating unchanged XML as a freshness proof

#### Scenario: unsupported build tasks fail closed
- **WHEN** an authored target invokes a task whose complete semantic inputs are not enumerated
- **THEN** the loaded entry is non-reusable even when the task uses no property function

#### Scenario: conventional directory names do not hide SDK source additions
- **WHEN** a standard SDK compile glob can acquire a new source file beneath `artifacts`, `node_modules`, `TestResults`, or another non-universal exclusion
- **THEN** that addition changes the fingerprint or the loaded entry is non-reusable

#### Scenario: nested output directory names are not universal exclusions
- **WHEN** a source file appears beneath a nested directory named `bin` or `obj` that is not proven to be an effective project output directory
- **THEN** that addition changes the fingerprint or the loaded entry is non-reusable

#### Scenario: proven default project outputs do not create false invalidations
- **WHEN** generated files change beneath an exact project-local default `bin` or `obj` directory and the corresponding output/default-item properties are not customized
- **THEN** those generated changes do not invalidate the semantic snapshot

#### Scenario: customized output exclusions fail closed
- **WHEN** direct or imported build configuration changes output paths or default-item exclusions so the default `bin` or `obj` status cannot be assumed
- **THEN** the affected directories remain observable or the loaded entry is non-reusable

#### Scenario: overlapping project globs prevent global output exclusion
- **WHEN** one project has a default output directory beneath another solution project's source-glob root
- **THEN** that directory is excluded only if every potentially consuming project proves the exclusion; otherwise relevant additions change the fingerprint or the entry is non-reusable

#### Scenario: artifacts output redirects implicit imports
- **WHEN** SDK artifacts-output configuration can move project extensions or restore imports away from the conventional project-local `obj` directory
- **THEN** the effective implicit imports are fingerprinted or the loaded entry is non-reusable

#### Scenario: SDK implicit import path overrides fail closed
- **WHEN** build configuration supplies `DirectoryBuildPropsPath`, `DirectoryBuildTargetsPath`, or a `CustomBefore`/`CustomAfter` Microsoft Common or Directory Build hook
- **THEN** the effective import and its recursive dependency graph are fingerprinted, including missing-to-present transitions, or the loaded entry is non-reusable

#### Scenario: default MSBuild wildcard hooks are fingerprinted
- **WHEN** an applicable default user or system `ImportBefore`/`ImportAfter` hook is added, removed, or changes without a project edit
- **THEN** its effective root, missing-to-present transition, and file content change the fingerprint; user-hook dependency graphs are traversed, while a system-hook set that diverges from the registered SDK baseline becomes non-reusable

#### Scenario: late MSBuild registration produces a stable hook baseline
- **WHEN** the authoritative registered MSBuild path becomes available during the first workspace load
- **THEN** post-load validation establishes one system-hook baseline and later unchanged acquisitions remain reusable rather than refreshing the same revision

#### Scenario: system hook dependencies remain observable
- **WHEN** an unchanged registered system hook literally imports an external props or targets dependency
- **THEN** that dependency and its recursive graph participate in the fingerprint; property-selected imports are resolved or make the loaded entry non-reusable

#### Scenario: baseline system hooks still fail closed
- **WHEN** a system hook present at baseline uses an unproven property function, task input, project-relative semantic item, output/restore override, package-root override, or hook selector assignment
- **THEN** the loaded entry is non-reusable rather than treating baseline membership as proof of complete inputs

#### Scenario: stock selectors do not exempt dependency bodies
- **WHEN** an identified stock hook selects an SDK targets file whose body or recursively imported dependency uses an unproven semantic input
- **THEN** that dependency graph remains subject to fail-closed validation rather than inheriting a blanket SDK-body exemption

#### Scenario: unknown SDK declarations fail closed
- **WHEN** a project declares an SDK outside the explicitly supported in-box Microsoft .NET SDK family
- **THEN** the loaded entry is non-reusable rather than assuming that SDK's implicit imports and evaluation inputs are covered

#### Scenario: SDK-qualified imports fail closed
- **WHEN** a project, registered system hook, or transitive hook dependency uses an `Import` with an SDK qualifier whose resolved physical import path is not proven by this slice
- **THEN** the loaded entry is non-reusable even when the qualifier names an otherwise supported SDK

#### Scenario: test-platform task implementations remain observable
- **WHEN** the stock test-platform targets declare their registered task assembly
- **THEN** the exact task assembly participates in the fingerprint, while an authored or environment-provided task implementation makes the loaded entry non-reusable

#### Scenario: Source Link task implementation overrides fail closed
- **WHEN** authored, imported, or environment configuration overrides `MicrosoftBuildTasksGitAssemblyFile`
- **THEN** the loaded entry is non-reusable because an arbitrary Git task implementation can change generated revision metadata through unenumerated inputs

#### Scenario: test tasks cannot be scheduled into compilation invisibly
- **WHEN** authored build configuration, a registered system hook, or a transitive hook dependency directly or indirectly changes a task scheduling property or target edge that can affect the compile pipeline
- **THEN** the loaded entry is non-reusable rather than searching only for a literal task name or treating the stock target location as proof that it cannot alter semantic output

#### Scenario: initial targets fail closed
- **WHEN** a project, registered system hook, or transitive hook dependency declares `InitialTargets`, including a property-indirected value
- **THEN** the loaded entry is non-reusable because those targets execute before Roslyn's requested build target and their semantic inputs are not proven

#### Scenario: legacy SDK task inputs fail closed
- **WHEN** the stock .NET Framework facade or legacy package-conflict targets can apply, or authored configuration overrides their task assembly or manifest inputs
- **THEN** the loaded entry is non-reusable unless those evaluated inputs and filesystem transitions are proven

#### Scenario: inapplicable legacy imports stay reusable
- **WHEN** the authored project/build graph proves a modern SDK target framework, or proves that Microsoft Common targets are not imported
- **THEN** the stock conditional .NET Framework and legacy conflict-resolution imports may be excluded from the semantic input graph without weakening freshness

#### Scenario: build-defined hook roots fail closed
- **WHEN** project or imported build XML assigns `MSBuildUserExtensionsPath`, `MSBuildExtensionsPath`, `MSBuildToolsVersion`, or a property that selects a system-hook dependency
- **THEN** the loaded entry is non-reusable unless the effective hook graph is evaluated explicitly

#### Scenario: environment path overrides fail closed
- **WHEN** an environment-derived MSBuild property redirects an implicit import, output/default-item location, restore input, artifact path, or legacy SDK task input
- **THEN** the environment identity changes and the loaded entry is non-reusable unless the effective external input graph is enumerated

#### Scenario: environment property names follow MSBuild casing
- **WHEN** an MSBuild-relevant environment property uses different casing on a case-sensitive operating system, or duplicate case variants make its effective value ambiguous
- **THEN** fingerprinting applies MSBuild's case-insensitive name semantics and the loaded entry is non-reusable for an unenumerated override or ambiguity

#### Scenario: all standard wildcard hook locations are observed
- **WHEN** a hook appears beneath Microsoft Common cross-targeting `ImportBefore`/`ImportAfter` or as a `Microsoft.VisualStudioVersion.v*.Common.props` wildcard match
- **THEN** the fingerprint changes and the system-hook baseline becomes non-reusable before semantic results are served

#### Scenario: default custom language hooks are observed
- **WHEN** a default `v<tools-version>/Custom.Before.*` or `Custom.After.*` hook for Microsoft Common, C#, or Visual Basic appears or changes
- **THEN** the fingerprint changes and its semantic dependencies are traversed or the loaded entry becomes non-reusable

#### Scenario: earlier package-root overrides fail closed
- **WHEN** project or imported build configuration assigns `NuGetPackageRoot` before a generated NuGet props file supplies its conditional fallback
- **THEN** discovery does not treat the fallback as authoritative and the loaded entry is non-reusable unless effective evaluation context is proven

#### Scenario: implicit Razor view additions are fingerprinted
- **WHEN** a supported Razor SDK project gains an untracked `.cshtml` or `.razor` input discovered by the SDK's default globs
- **THEN** the next acquisition refreshes before returning generated semantic conclusions

#### Scenario: MSBuild expressions and escaping fail closed
- **WHEN** a declared semantic path uses an item-list expression, metadata expression, or percent-escaped MSBuild path that discovery does not evaluate
- **THEN** the loaded entry is non-reusable rather than hashing the expression as a literal filesystem path

#### Scenario: restore paths apply the same expression rules
- **WHEN** `ProjectAssetsFile`, `MSBuildProjectExtensionsPath`, or `BaseIntermediateOutputPath` contains an unproven expression or percent-escaped path
- **THEN** the loaded entry is non-reusable rather than tracking an encoded or otherwise incorrect restore location

#### Scenario: semantic XML names are case-insensitive
- **WHEN** a property, item, import, or output-control name uses casing different from the canonical SDK spelling
- **THEN** discovery applies the same freshness handling as it would for the canonical spelling

#### Scenario: explicit project extensions path takes precedence
- **WHEN** a project assigns both `MSBuildProjectExtensionsPath` and `BaseIntermediateOutputPath`
- **THEN** discovery fingerprints the explicit project extensions location or marks evaluation non-reusable rather than selecting whichever assignment appears last

#### Scenario: imported item paths retain project evaluation context
- **WHEN** an imported build file declares a relative Compile, ProjectReference, analyzer, or other semantic item path
- **THEN** discovery resolves it in the owning project context or marks the entry non-reusable

#### Scenario: generated build inputs do not guess project context
- **WHEN** a generated NuGet props/targets file contains a raw relative semantic item or filesystem existence input and its actual consuming project context is not carried explicitly
- **THEN** the loaded entry is non-reusable rather than resolving the path from the generated-file directory

#### Scenario: literal existence preserves filesystem kind
- **WHEN** build evaluation depends on `Exists` for a literal path
- **THEN** missing, file, and directory states are distinguished using the owning project evaluation context, or the entry is non-reusable

#### Scenario: freshness proof fails closed
- **WHEN** a relevant input cannot be enumerated, opened, or content-hashed consistently
- **THEN** the system does not report a cache hit and performs a clean reload or returns the existing typed load failure

### Requirement: Retention is bounded and disposal respects active leases
The system SHALL bound current cache entries, retire evicted or refreshed entries, and
dispose each owning workspace only after its final active lease ends.

#### Scenario: eviction waits for an active query
- **WHEN** an entry is evicted while a query lease remains active
- **THEN** no new lease uses that entry and its workspace remains undisposed until the active lease is released

#### Scenario: shutdown retires every entry
- **WHEN** the MCP or daemon service provider shuts down
- **THEN** every cached entry is retired and each workspace is disposed immediately or after its last active lease

#### Scenario: maintenance does not strand a completed acquisition
- **WHEN** bounded eviction encounters another identity whose load or refresh is still in flight
- **THEN** the completed acquisition returns without waiting indefinitely, caller cancellation is observed, and any unreturned lease is released

#### Scenario: busy oldest entry does not block idle eviction
- **WHEN** the oldest eviction candidate is busy while another over-capacity entry is idle
- **THEN** maintenance skips the busy identity and retires an available idle candidate without exceeding the configured retained-entry bound indefinitely

### Requirement: Completeness, trust, and mutation boundaries remain unchanged
Cached acquisition SHALL preserve workspace completeness, confidence, diagnostics,
cancellation, and revision binding. Partial snapshots SHALL remain partial and SHALL
NOT authorize proof-like deletion or mutation. Mutation operations SHALL remain on
their existing in-process safe path.

#### Scenario: partial load stays partial
- **WHEN** a cached workspace has completeness-impacting diagnostics
- **THEN** every operation reports the same partial state and limitations as a clean direct load

#### Scenario: mutation is never dispatched through the cache
- **WHEN** a mutation command is invoked
- **THEN** its placement, daemon rejection, no-retry, and indeterminate-dispatch behaviour are unchanged and no cached read-only lease is used

### Requirement: Fallback is permitted only before dispatch
The MCP/CLI host SHALL execute the direct path only when the daemon transport proves
`UnavailableBeforeDispatch`. It SHALL NOT execute the operation again after
`IndeterminateAfterDispatch`.

#### Scenario: daemon loss before dispatch falls back
- **WHEN** the daemon is unavailable before any request bytes are accepted and cannot be started
- **THEN** the host executes the same read-only application port directly once

#### Scenario: dispatch becomes indeterminate
- **WHEN** request dispatch starts but no terminal daemon response is available
- **THEN** the host surfaces the indeterminate result and does not invoke the direct application port

### Requirement: Existing host-relative diagnostics and targets remain compatible
Daemon preference SHALL preserve request-relative path interpretation and SHALL NOT add semantic-workspace admission to installation-only diagnostics.

#### Scenario: relative outline target uses request context
- **WHEN** outline receives an absolute solution and a relative file, directory, or glob target through the daemon
- **THEN** the target is resolved from the request's explicit repository or solution context rather than the daemon process current directory

#### Scenario: CLI outline target remains invocation-relative
- **WHEN** a CLI invocation supplies a relative file, directory, or glob target from a directory below the solution root
- **THEN** the CLI normalizes that target from its invocation directory before daemon dispatch and direct execution returns the same target as before daemon preference

#### Scenario: installation-only doctor needs no repository admission
- **WHEN** doctor is invoked with `installationOnly` for an absolute untrusted directory
- **THEN** the request reports installation diagnostics without repository trust, SDK compatibility, or workspace admission

#### Scenario: diagnostic doctor discovery remains directory-local
- **WHEN** ordinary CLI doctor has no explicit solution and its requested directory contains zero or multiple candidates
- **THEN** it executes locally and returns the existing unsupported or ambiguous diagnostic envelope without selecting an ancestor solution, requiring trust, or applying SDK workspace admission

#### Scenario: invocation-scoped trust remains local
- **WHEN** a CLI invocation authorizes a repository only with invocation-scoped explicit trust
- **THEN** read-only execution remains in-process and does not transmit that authority to the persistent daemon

### Requirement: Reuse and revision evidence is deterministic
The system SHALL emit bounded cache observations that distinguish load, hit, refresh,
and eviction and bind each observation to a hashed cache identity, source revision,
and monotonic process-local load count. Public semantic results SHALL expose the
snapshot revision through their existing source identity or a small additive field.

#### Scenario: black-box workflow proves reuse
- **WHEN** one real MCP process runs doctor, outline, describe, source, and relationships against a representative five-project trusted solution
- **THEN** instrumentation records one solution load and the returned results reference the same valid snapshot revision

#### Scenario: black-box source edit proves refresh
- **WHEN** the fixture source changes after the warm workflow
- **THEN** the next call records a refresh and load-count increment and returns the new snapshot revision
