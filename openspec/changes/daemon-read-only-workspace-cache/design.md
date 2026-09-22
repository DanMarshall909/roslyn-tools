## Context

The CLI, MCP host, and daemon share typed application ports and one singleton
`WorkspaceLoader` per process. Each read-only operation currently asks that loader
for a workspace, then disposes the returned workspace; the loader therefore opens
the same solution again for the next request. The persistent daemon is currently a
transparent transport and intentionally rejects every mutation port.

ADR-0008 parked all workspace ownership until a separate proposal defined identity,
freshness, trust, lifetime, concurrency, and failure behaviour. This change supplies
that narrower decision for six existing read-only semantic operations only. The
preview trust registry remains authoritative and the existing completeness and
confidence policy still evaluates every acquired snapshot.

## Goals / Non-Goals

**Goals:**

- Reuse one immutable Roslyn `Solution` and its owning `Workspace` across sequential
  read-only calls while its complete semantic-input fingerprint remains unchanged.
- Coalesce concurrent cold or stale acquisition for one identity into one
  `OpenSolutionAsync` call and permit concurrent leases after acquisition.
- Bind identity to the canonical solution path, canonical worktree root, repository
  identity, and semantic environment.
- Refresh conservatively on source/build/configuration/analyzer/generator/environment
  change, or when freshness cannot be proved.
- Bound retained entries and defer workspace disposal until the final active lease
  ends.
- Preserve public invocation shapes while exposing the snapshot revision in the
  existing source identity or a small additive `sourceRevision` field.

**Non-Goals:**

- Daemon-owned mutation, prepared-plan execution, retry after indeterminate dispatch,
  workflow sessions, cross-machine state, durable cache persistence, incremental
  workspace mutation, or a general repository index.
- Claiming completeness from a partial workspace, changing confidence policy, or
  making the preview trust registry satisfy the stronger future isolation ADR.
- Hot daemon upgrade/draining, multi-user scheduling, or the broader daemon roadmap.

## Decisions

### Use a separate read-only lease seam

The six operations depend on an `IReadOnlyWorkspaceSessions` abstraction that returns
an immutable solution lease with its source revision. Mutation operations continue to
depend on `IWorkspaceSessionFactory` and retain their existing load/apply path. This
makes the safety boundary structural rather than a runtime flag.

Alternative: cache inside the existing loader for every caller. Rejected because
mutation operations could accidentally consume a cached snapshot and because current
callers dispose the returned workspace directly.

### Resolve and authorize identity before cache lookup

Acquisition canonicalizes the solution, revalidates the active trust scope, and
derives `(repository identity, canonical worktree root, canonical solution path,
semantic environment identity)`. The worktree root remains part of the key even when
linked worktrees share one Git common directory. Relative solutions are accepted only
when an explicit trusted root exists and are resolved beneath that root; persistent
multi-root MCP/daemon requests must be absolute. Escapes and unresolved trust fail
before cache access.

Ordinary CLI invocations preserve their invocation-directory semantics by resolving
relative or omitted solutions before entering the persistent multi-root application
boundary. Explicit single-root trust canonicalizes the configured root itself before
performing containment, so a trusted symlink alias and its physical solution share
one identity.

Alternative: key by Git HEAD or common directory. Rejected because dirty/untracked
content and linked worktrees can differ at one HEAD.

### Prove freshness with a conservative semantic-input fingerprint

Each loaded entry stores a deterministic content fingerprint over:

- the solution, project, source, additional, analyzer-config, props/targets,
  configuration, and generated semantic files beneath the worktree;
- every loaded document outside the worktree plus analyzer/generator and metadata
  reference inputs needed by the snapshot;
- project-tree and explicitly declared external assembly candidates that can take precedence during reference resolution;
- applicable `global.json` and directory build/package configuration; and
- the allow-listed environment and registered MSBuild/SDK identity that influence
  evaluation.

Files are content-hashed, not identified by Git HEAD or timestamp alone. Directory
enumeration and reads are fail-closed: an unreadable, disappearing, unsupported, or
otherwise unprovable input makes the entry stale and forces a clean reload. The
fingerprint follows literal imports and declared inputs outside the worktree and
walks ancestor directories for applicable build/package/SDK configuration. A dynamic
import, wildcard, or skipped generated import graph that cannot be enumerated marks
the entry non-reusable, so later calls reload instead of claiming a hit. The same
fail-closed rule applies to unsupported imported-file roles and project-context
properties encountered after leaving the owning project file. Project paths
declared by the solution are discovered before load, including external projects and
their literal imports. A loaded project outside the trusted worktree is conservatively
non-reusable because its implicit SDK compile glob can acquire a new file not present
in the loaded snapshot. Repository inputs are captured before load, after load, and
again after the full snapshot capture. The full capture compares every loaded source,
additional, and analyzer-configuration document with a stable physical read. A change
during that interval or a loaded/physical text mismatch retries the load before
publication. The same comparison guards warm hits. Newly added relevant files under
the worktree are included, so unstaged and untracked source changes refresh. Repository
enumeration does not prune conventional output or dependency directory names unless
their exclusion from compile evaluation is proven; standard SDK globs can otherwise
acquire new source files beneath those names. This includes nested directories named
`bin` or `obj` that are not a project's proven effective output paths. The exact
project-local default `bin` and `obj` directories may be excluded when the project
does not customize the corresponding output/default-item properties. A direct project
customization keeps that directory observable; an imported customization that cannot
be applied without full evaluation makes the entry non-reusable. An output directory
is excluded only when every solution project whose default item glob can reach it
proves that exclusion; a nested project's own exclusion cannot hide the directory
from an ancestor project. SDK artifacts-output properties that can redirect restore
imports are non-reusable unless their effective paths are enumerated. Applicable
ancestor `.editorconfig` and `.globalconfig` candidates are included even while
missing so later creation invalidates the snapshot.
For supported SDK projects, Git is an additional compilation input because generated
assembly information can embed the source revision. Discovery fingerprints the
nearest repository marker, worktree-private Git directory and `HEAD`, common-directory
loose ref candidates, and packed refs for every project/worktree start point. Linked
worktrees therefore observe their own HEAD while sharing only their actual common ref
store. It also hashes Git's effective configuration view and the SDK Source Link
reader's actual program-data, system, XDG, user-profile, repository, and recursive
include inputs because generated assembly metadata can consume values such as the
repository URL. Native-Git configuration overrides disable reuse where they would make
that reader parity unprovable. Minimal Source Link metadata containing `HEAD` and
repository config remains valid without an object database. A definitively invalid
inner `.git` candidate does not hide the nearest valid ancestor repository; unreadable
or ambiguous candidates, configuration capture, ref storage, or environment redirection
disable reuse. Missing markers, refs, and configuration roots remain fingerprinted for
missing-to-present changes. This supplements the authored/untracked content fingerprint
and never substitutes Git HEAD for it.
Implicit SDK/NuGet restore imports, generated build inputs, evaluated import graphs,
and filesystem-dependent MSBuild conditions are reusable only when their complete
input set and missing-to-present transitions can be enumerated. Otherwise the entry
is deliberately non-reusable and reloads on the next call.
Only explicitly supported in-box `Microsoft.NET.Sdk` declarations participate in
reuse. An unknown SDK name fails closed because its implicit imports and evaluation
inputs are not proven by the built-in scanner. SDK-qualified `Import` elements remain
non-reusable even for a known SDK because this slice does not prove their resolved
physical import locations. Stock test-platform task declarations remain reusable only
when their exact registered task assembly is fingerprinted; authored or
environment-provided task implementations, including Source Link's Git task-assembly
selector, and direct or indirect compile-pipeline scheduling overrides make the entry
non-reusable. Those SDK-import and scheduling rules apply equally to registered system
hooks and every transitively imported hook dependency; only exact verified stock
test-platform scheduling declarations are exempt. Project-level `InitialTargets` is also
treated as unproven scheduling and therefore disables reuse in every authored or hook
graph. Environment-derived MSBuild properties are captured with MSBuild's
case-insensitive name semantics on every host; ambiguous case variants also fail closed.
Custom assembly search paths or allowed-reference extensions and configured or directly
resolved rulesets are non-reusable because this slice does not prove their
precedence-sensitive candidate sets or recursive external include graphs.
Strong-name signing configuration is also non-reusable when it is selected through
authored, imported, environment, loaded compiler-option, or semantically resolved
source/generated assembly-attribute inputs because the external key-file and key-container
state is not proven here.
Effective restore locations include loader-supplied global properties and applicable
imported configuration rather than assuming the conventional project-local `obj`
directory. A literal project-local `ProjectAssetsFile` override is tracked directly;
dynamic or unproven imported overrides are non-reusable because their effective value
cannot be proven without full evaluation. The generated NuGet assignment rooted at
`MSBuildThisFileDirectory` is enumerated explicitly. Package props/targets remain part
of the traversed import graph, including literal dependencies outside the package
directory. The effective project extensions directory is also enumerated for MSBuild's
implicit `<project-file>.*.props` and `<project-file>.*.targets` imports.
An earlier project or imported `NuGetPackageRoot` assignment makes reuse fail closed
rather than allowing a later generated conditional fallback to be treated as the
effective package root.
Explicit SDK import-path properties such as `DirectoryBuildTargetsPath` and the
`CustomBefore`/`CustomAfter` hooks are non-reusable unless their effective imports and
missing-to-present transitions are traversed recursively.
Default MSBuild user and system wildcard hook directories are fingerprinted as
missing-or-present inputs. User-hook props/targets and recursive literal dependencies
are traversed; unproven user-hook behavior is non-reusable. The installed system-hook
set is bound to the registered SDK identity and content-hashed after the process has an
authoritative MSBuild path; later system-hook additions, removals, or content changes
make the entry non-reusable. Literal dependencies of unchanged system hooks are still
traversed; property-selected dependencies and build-defined hook-root overrides are
non-reusable unless evaluated explicitly. Baseline hooks still apply the normal
property-function, task, item, output/restore override, and selector fail-closed
checks. Narrow structural allowances cover only identified stock SDK constructs;
they do not transitively exempt selector targets or imported files from validation.
Stock .NET Framework and legacy conflict-resolution imports are skipped only when the
authored project/build graph proves that they cannot apply to a modern SDK project.
Ambiguous, legacy, task-assembly-overridden, or package-conflict-manifest configurations
are non-reusable because their evaluated task and filesystem inputs are not proven.
Effective roots, tools version, and
environment-controlled enable flags are part of the environment identity. Environment
properties that redirect implicit imports, output/default-item discovery, restore
locations, artifacts, or legacy SDK tasks make the entry non-reusable unless their
effective inputs are explicitly enumerated. Standard
cross-targeting, Visual Studio-version wildcard, and default `v<tools-version>`
`Custom.Before`/`Custom.After` locations for Common, C#, and Visual Basic targets are
included.
Supported Razor SDK implicit inputs include both `.razor` and `.cshtml` files.
MSBuild property and item names are matched case-insensitively. Item-list/metadata
expressions and percent-escaped paths are non-reusable unless evaluated with MSBuild
semantics, including restore-location properties. Raw relative item and existence paths
declared by imported or generated NuGet build files require an actual owning-project
context; when that context is not carried explicitly, discovery fails closed instead
of guessing it from the generated-file directory. `MSBuildProjectExtensionsPath` is
treated as the explicit extensions-path authority rather than being textually conflated
with a later `BaseIntermediateOutputPath` assignment.
MSBuild or CLR property functions are non-reusable unless all external inputs are
proven. Filesystem-dependent expressions outside `Condition` attributes are therefore
fail-closed, and literal `Exists` inputs preserve the file-versus-directory existence
state used by MSBuild evaluation. Conditional or target ancestry on restore-path
assignments and authored target tasks with unenumerated inputs are likewise
non-reusable.

Alternative: filesystem watchers plus Git dirty state. Rejected because watcher loss,
timestamp granularity, untracked inputs, and external analyzer/configuration inputs
can serve stale semantics.

### Serialize acquisition, not queries

Each key owns an asynchronous refresh gate. The first caller loads and publishes one
entry; concurrent cold callers await that same gate and lease the published entry.
Warm callers validate under the gate, then release it before semantic work. A stale
replacement retires the old entry, but disposal waits for its active lease count to
reach zero. Eviction and shutdown acquire the same per-key gate, under a serialized
retirement coordinator, before removing or retiring an entry; they therefore cannot
orphan a cold or refreshed publication or retire an entry between validation and
lease creation. A caller that was already queued on a slot removed by eviction
rechecks publication under that slot's gate and retries against the replacement
instead of leasing the retired entry. A queued caller that observes shutdown retires
any entry it removes before rejecting the acquisition.

### Use bounded least-recently-used retention

The process retains at most four current entries by default. Eviction removes the key
immediately, retires its workspace, and defers disposal while leases remain active.
Daemon/MCP shutdown retires all entries. Active leases may temporarily keep retired
workspaces alive, but no retired entry can receive a new lease.
Best-effort trimming does not wait behind another identity's in-flight load or
refresh after the protected lease has been created. A busy oldest identity is skipped
so another idle candidate can be retired. Capacity may be exceeded temporarily;
later acquisitions request another trim, and a failed cold publication removes its
empty slot so entries are not stranded above the bound. Caller cancellation is
observed, and an acquisition that cannot return its lease disposes that lease before
failing.

### Keep daemon dispatch fail-closed

Doctor, outline, describe, search_symbols, source, and relationships become
`DaemonPreferred`; mutation definitions remain `InProcess`, and the dispatcher keeps
rejecting mutation ports. Direct fallback occurs only for
`UnavailableBeforeDispatch`. `IndeterminateAfterDispatch` never invokes the operation
again in-process. The existing request and response shapes remain unchanged except
for additive snapshot revision fields.

Installation-only doctor requests remain local diagnostics and bypass repository,
SDK, and workspace admission. Relative outline file, directory, and glob targets are
normalized by the CLI from its invocation directory before daemon dispatch. The CLI
likewise writes relative or discovered solution paths into the typed read-only request
before choosing daemon, direct, or pre-dispatch fallback execution, so every route
observes the same invocation-relative identity. Mutation requests remain untouched.
Other hosts resolve relative file, directory, and glob targets from the explicit
solution/repository request context; namespace targets remain names rather than paths.
CLI doctor requests without an explicit solution remain local and retain directory-
local candidate diagnostics without trust or SDK workspace admission. Invocation-
scoped explicit trust also remains in-process; it is never forwarded as persistent
daemon authority.

### Emit bounded operational observations

The cache reports `load`, `hit`, `refresh`, and `evict` observations containing a
hashed cache identity, snapshot revision, and monotonic process-local load count.
Tests inject an observer; the daemon writes the same bounded metadata to its existing
diagnostic log. No source text, raw request, trust credential, or environment value is
logged.

## Risks / Trade-offs

- [Fingerprinting can consume noticeable I/O on large repositories] → hash only
  declared semantic input classes, reuse the immutable snapshot after validation,
  measure the representative fixture, and reload rather than weaken the proof.
- [An external writer can change files immediately after validation] → acquisition
  defines the snapshot linearization point; a lease always queries one immutable
  Roslyn solution and exposes that revision, while the next acquisition refreshes.
- [Eviction races a long query] → retirement prevents new leases and reference
  counting defers disposal until the last query releases.
- [Trust is revoked while a lease is active] → every acquisition reauthorizes;
  this slice does not claim the future revocable evaluator containment described by
  ADR-0011, and records that residual preview limitation unchanged.

## Migration Plan

1. Add the lease/cache/fingerprint seam and deterministic RED tests.
2. Move only the six read-only operations to leases and daemon-preferred placement.
3. Add path-admission, fallback, mutation-boundary, revision, and telemetry tests.
4. Run one spawned MCP/daemon fixture through the five semantic calls, modify source,
   and verify one cold load followed by one refresh.
5. Roll back by reverting this slice; cache eviction or process restart already
   produces the pre-change cold-load behaviour without changing results.

## Open Questions

None for this bounded slice. Broader evaluator isolation, hot upgrade draining,
durable sessions, and daemon-owned mutation remain separate decisions.
