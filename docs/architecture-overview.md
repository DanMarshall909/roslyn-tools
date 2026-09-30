# Architecture overview

This is the umbrella description of the `rt` target architecture. It names the layer boundaries, execution and safety contracts, output model, capability catalogue, repository-trust boundary, mutation protocol, quality gates, and dogfood loop. Update it whenever an architectural decision changes one of those contracts, alongside the relevant ADR.

Companion documents:

- [critical-architecture-review-2026-08.md](critical-architecture-review-2026-08.md) — critical baseline, evidence, risks, and remediation order.
- [critical platform hardening OpenSpec](../openspec/changes/critical-platform-hardening/proposal.md) — executable P0/P1 requirements and tasks.
- [pattern-core-service.md](pattern-core-service.md) — the canonical Core-service + thin-host-adapters pattern with worked examples.
- [target-command-surface-architecture.md](target-command-surface-architecture.md) — target shape for command wiring, catalogues, capabilities, and binders.
- [consistency-roadmap.md](consistency-roadmap.md) — execution plan sequencing the command-surface migrations.
- [command-application-port-migration.md](command-application-port-migration.md) — explicit conversion instructions for the complete command catalogue.
- [command-port-migration-groups.tsv](command-port-migration-groups.tsv) — machine-readable migration manifest.
- [per-command-checklist.md](per-command-checklist.md) — command-level admission checklist.
- [adr/](adr/) — architecture decision records.

## Architectural position

Third-party integrations are extracted into `RoslynTools.Adapters`, which
depends on Core and Hosting and is composed by CLI, MCP, and DaemonHost.
Core and Hosting retain inward dependencies and own neutral contracts.
[ADR-0079](adr/0079-third-party-adapter-boundary.md) records the package policy
and staged extraction; RT-020 tracks its verification.

`rt` is a semantic C# operation engine with multiple host adapters. The CLI is not the application layer, the MCP server is not a second implementation, and the daemon is not the owner of truth.

The target shape is:

```text
┌─ Host adapters ─────────────────────────────────────────────────────┐
│ CLI: bind arguments + render terminal output                        │
│ MCP: protocol lifecycle + bind JSON + project typed result          │
│ Daemon: trust-gated typed RPC + lifecycle control                    │
│ IDE: editor context + native UX (future)                             │
└──────────────────────────────┬───────────────────────────────────────┘
                               │ typed request + explicit request context
                               ▼
┌─ Shared operation router / application definitions ────────────────┐
│ identity, request/result types, validation, safety, maturity, limits │
│ follow-ons, placement policy, provenance/confidence policy           │
└──────────────────────────────┬───────────────────────────────────────┘
                               ▼
┌─ Application operations ────────────────────────────────────────────┐
│ orchestration, cancellation, trust-aware workspace acquisition       │
│ mutation prepare/apply policy, expected failures, resource budgets   │
└──────────────┬──────────────────────────────────────┬────────────────┘
               ▼                                      ▼
┌─ Workspace boundary ─────────────────┐  ┌─ Domain services / Core ──┐
│ complete / partial / failed          │  │ Roslyn analysis/refactoring│
│ trust: trusted / untrusted           │  │ symbol resolution           │
│ evaluator: no-eval / in-proc / worker│  │                              │
│ revision + diagnostics + provenance  │  │ deterministic transformations│
└──────────────────────────────────────┘  └─────────────────────────────┘
                               │
                               ▼
┌─ Shared typed result/error/provenance envelope ─────────────────────┐
└──────────────────────────────┬───────────────────────────────────────┘
                               ▼
                     host-specific projection

Optional daemon transport sits around the typed request/result boundary only. It uses
an authenticated per-user endpoint or remains disabled; endpoint-name secrecy is not
authorization. It is not an additional semantic layer and does not own workspaces or
mutations.
```

**Placement rule:** could the behaviour be tested without CLI, MCP, daemon, console, or process-global state in scope? If not, host concerns have leaked inward.

## Contracts that bind the layers

### CI rule enforcement

`rt ci check` uses the typed `CiCheckOperation` in-process for CLI and MCP. The
read-only `ICiCommand` marker extends the exactly-one-category invariant. Version 1
policies compose architecture and complexity checks over one workspace; findings
are never truncated or suppressed by architecture baselines. CLI compliance maps
to exit 0/1, while execution errors remain nonzero and MCP retains typed errors
separately from the result's `passed` value. See [ADR-0016](adr/0016-ci-rule-enforcement.md)
and [CI usage](ci.md).

### Bounded external semantic review

`rt analyze solid-review` is an optional provider-neutral ranking stage over deterministic
`solid` candidates. It executes in-process through one typed application port shared by
CLI and MCP. Preview is the default; repository trust and a separate
`--transmit-source` option are both required before bounded sanitized source can leave
the machine. Each request contains one declaration and principle-relevant evidence,
while the versioned result preserves the complete raw analysis beside advisory
classifications, independent material-harm and accepted-exception probabilities, typed
false-positive reasons, deterministic policy reasons, evidence completeness,
provider/model/question provenance, and categorized failures. Weak or contradictory
signals abstain to `uncertain`. V1 deliberately has no cache or CI enforcement.
See [ADR-0018](adr/0018-bounded-external-semantic-review.md), the
[usage guide](solid-semantic-review.md), and the
[V1 evaluation](evaluations/solid-semantic-review-v1.md).

### 1. Host-neutral application definition and port

Every ordinary stable command has one host-neutral application definition and exactly one typed `ICommandApplicationPort`. The definition carries:

- canonical identity and aliases;
- request and response types plus alternative request variants;
- `RtCommandInfo` and examples;
- visibility and maturity (`stable`, `preview`, or `experimental`);
- safety and execution placement;
- resource limits and paging policy;
- provenance and confidence policy; and
- suggested follow-on tools.

It contains no CLI builder, parser object, protocol type, renderer, console call, or daemon-specific semantics. `IRtCommand` is the CLI projection; the MCP adapter projects the same definition. Every discoverable MCP descriptor must have an application port. The MCP adapter invokes that port directly and has no CLI argument or console-capture fallback. Descriptors without ports remain outside the MCP surface until migrated.

`change-signature` uses one typed request for its four CLI leaves and the MCP
`change_signature` tool. Its required `operation` discriminator selects
`rename-param`, `reorder`, `add-param`, or `remove-param`; alternative request
variants generate the MCP `oneOf` schema. Enum literals in that schema follow
their JSON wire names. See [ADR-0078](adr/0078-change-signature-operation-discriminator.md).

The `rt` CLI assembly owns `IRtCommand` and its category/output markers, the
CLI option helper, and the `dump-help` command-surface projection. The reusable
`RoslynTools` project has no `System.CommandLine` package or compiled assembly
reference; its application definitions and ports remain host-neutral. The moved
internal CLI types retain their historical `RoslynTools.Core` namespace for
source compatibility. [ADR-0042](adr/0042-cli-command-surface-ownership.md)
records this assembly boundary.

The reusable `ApplicationCommandCatalog` projects registered typed ports from
their application definitions. MCP discovery, dispatch, and exact-name
configuration validation use this catalogue directly, without constructing CLI
commands. The CLI descriptor registry consumes the same typed descriptors and
adds CLI path and option metadata; it constructs legacy maintainer descriptors
locally. The Core-owned product capability catalogue uses the same typed
inventory for doctor and other capability reporting. Shared host foundation
registration, application infrastructure, catalogue construction, and Core
service scanning live in `RoslynTools.Hosting`. Its suggestion catalogue
combines typed public definitions with shared host metadata for `config`,
`trust`, feedback, and the richer CLI `semantic-diff` projection. CLI-specific
composition remains in the CLI assembly. Removing MCP's CLI project reference
is complete: MCP composes these shared services through its own entry point,
then adds protocol services. The daemon composes the same shared application
ports through `RoslynTools.DaemonHost`, with its own SDK guard and pipe server;
the CLI binary still launches that server and owns the lifecycle commands.
Neither MCP nor DaemonHost references the CLI assembly. The MCP project
references Core and Hosting. The
general MCP test fixture and protocol tests use this production composition;
the test project retains CLI only for explicit descriptor and metadata
comparisons. See
[ADR-0044](adr/0044-typed-application-catalogue-for-mcp.md) and
[ADR-0053](adr/0053-mcp-owned-host-composition.md) and
[ADR-0056](adr/0056-daemon-owned-host-composition.md).
CLI-only feedback operations remain registered only in the CLI process; direct
daemon RPC for their legacy names returns `command_not_found`.

Architecture tests lock the production project graph: Core has no project
reference; Hosting depends only on Core; MCP and DaemonHost depend only on Core
and Hosting; CLI depends on Core, Hosting, and DaemonHost. Compiled-reference
checks reject host assemblies in Core and shared Hosting, while package checks
keep CLI parser and terminal-renderer dependencies in the CLI project. A
cross-host test compares the name, request/response types, safety, and visibility
of every typed application descriptor across CLI, MCP, and daemon composition.
New host adapters must extend this ratchet. See
[ADR-0057](adr/0057-host-dependency-and-contract-ratchets.md). The daemon
pipe client, process launcher, RPC dispatcher, protocol, and legacy routing
catalogue now live in shared Hosting. A placement test keeps those types and
named-pipe source out of Core; see
[ADR-0058](adr/0058-daemon-transport-hosting-ownership.md).
Services use injected logging for diagnostics and typed errors for invalid
input; a source test rejects direct `RtConsole` and `RtLog` calls in service
code. Host composition owns the console logger; standalone service constructors
use silent defaults and the source test rejects concrete console logger creation
in Services. The remaining legacy cross-project move helper returns preview
lines to its caller instead of rendering them directly; the source ratchet also
rejects `RtResultRenderer` calls in Services. See
[ADR-0060](adr/0060-service-diagnostics-without-ambient-console.md),
[ADR-0061](adr/0061-host-owned-console-logger.md), and
[ADR-0062](adr/0062-legacy-move-preview-result.md).
CLI process output implementations (`RtResultRenderer`, `RtSerializer`, and
`StructuredOutput`) now compile into the CLI adapter, with an assembly
placement test; shared result and format contracts remain in Core. See
[ADR-0063](adr/0063-cli-owned-process-output.md).
Services leave process exit status to the host. The add-dependency service
returns failure outcomes without setting `Environment.ExitCode`, and a source
test prevents this behavior from returning to Services. See
[ADR-0064](adr/0064-service-exit-status-is-host-owned.md).
The five text, JSON, YAML, Markdown, and LLM format renderers also compile in
CLI with `RtSerializer`; an assembly placement test covers them. Core retains
shared output contracts and JSON encoder selection. See
[ADR-0065](adr/0065-cli-owned-format-renderers.md).
Workspace cache telemetry reports cache events and trace failures through
callbacks wired to the host's logger; Core no longer calls the ambient CLI
logger from that component. See
[ADR-0066](adr/0066-workspace-cache-diagnostics-through-host.md).
Reusable logger extensions and the CLI console bridge now share a pure
diagnostic template formatter in Core; the logger extensions do not call the
console bridge. See [ADR-0067](adr/0067-pure-diagnostic-template-formatting.md).
Host composition gives `WorkspaceLoader` its logger and warning preference.
Core groups workspace diagnostics but no longer reads CLI warning state or
uses ambient logging for them. See
[ADR-0068](adr/0068-workspace-load-diagnostics-via-host-logging.md).
Symbol lookup takes an explicit suggestion option. The typed `DescribeRequest`
carries the CLI's `--no-suggest` choice through local and daemon execution;
Core no longer reads that CLI flag. See
[ADR-0069](adr/0069-explicit-symbol-suggestion-policy.md).
The process-global output context now lives in shared Hosting rather than
Core; its internal namespace and host behavior are preserved. See
[ADR-0070](adr/0070-host-owned-output-context.md).
Typed inspection envelopes keep the full acquisition account through the
application port. CLI projects bounded `rt.result.v2` workspace evidence when
the account exceeds sample limits, including for JSON and LLM output; small
accounts stay on `rt.result.v1`. `--full-workspace-evidence` retrieves the
complete `rt.result.v1` account. The compact projection retains
the state, counts, limitations, and priority failure samples; see
[ADR-0081](adr/0081-bounded-cli-workspace-evidence.md).
Duplicate extraction warnings and the match summary now pass through an
injected logger; Core no longer renders those diagnostics directly. See
[ADR-0071](adr/0071-extraction-diagnostics-through-logger.md).
Solution writers now report diff, result, and warning lines through a Core
sink whose console implementation lives in Hosting. See
[ADR-0072](adr/0072-solution-writer-report-sink.md).
The ambient console and logging bridges and their console-specific types now
compile in shared Hosting. Core retains only the pure diagnostic template
formatter. A placement test and the direct-console source gate lock this
boundary; see [ADR-0073](adr/0073-host-owned-ambient-console.md).
MCP replaces the shared console report sink with a no-op sink so solution
write reports cannot enter its JSON-RPC stream; typed mutation results remain
structured tool responses. See
[ADR-0074](adr/0074-mcp-solution-report-boundary.md).
The authenticated `gh` process adapter for feedback commands now compiles in
CLI while the injectable issue-client contract stays in reusable Services.
See [ADR-0075](adr/0075-cli-owned-github-issue-process-adapter.md).
Semantic diff now requests directory-scoped git clients through a reusable
factory contract; the concrete git process adapter and factory compile in
shared Hosting for CLI and MCP. See
[ADR-0076](adr/0076-host-owned-semantic-diff-git-client.md).
A syntax-tree ratchet now rejects ambient host adapters and host-framework
imports across Core and Services, plus process-launch types in Services. It
complements the project-reference, compiled-assembly, placement, and console
gates; see [ADR-0077](adr/0077-reusable-host-source-ratchet.md).

Host diagnostics follow the same boundary. An application operation may consume a
small typed probe or bootstrap port, but process launching, installed-file discovery,
SDK selection, and process-global registration stay in host adapter assemblies. For
example, `DoctorOperation` consumes `IDoctorEnvironmentProbe` and
`IDoctorWorkspaceBootstrap`; the shared host implementations perform the installed MCP
handshake and defer MSBuild registration until repository trust authorizes solution
loading. The probe reports absent, valid, or invalid `global.json` request state plus
its path and SDK-selection diagnostic, and follows initialize, initialized-notification,
then discovery ordering. Unit tests replace both ports without starting a host process
or registering MSBuild globally. The MCP adapter likewise invokes installation-only
doctor requests without registering MSBuild, so a self-contained installation can
diagnose an absent SDK rather than failing before the doctor operation runs.

MSBuild instance identity, exact-selection values, and compatibility errors are
shared in the reusable assembly for CLI, daemon, and MCP hosts. The daemon SDK
guard lives in DaemonHost. Process-global `MsBuildBootstrap` lives in the shared
`RoslynTools.Hosting` assembly, referenced by all three hosts; Core does not depend
on this host assembly. See [ADR-0045](adr/0045-shared-msbuild-instance-identity.md)
and [ADR-0046](adr/0046-shared-msbuild-host-bootstrap.md). The installed
doctor probe and workspace bootstrap also live in this assembly; see
[ADR-0047](adr/0047-shared-doctor-host-services.md).
The shared host foundation registers workspace loading and caching, durable
state, trust, and doctor ports; the CLI invokes it before its own registrations.
See [ADR-0048](adr/0048-shared-host-foundation-registration.md).
The same host assembly registers daemon RPC, symbol/document resolution, and
prepared-change writers; see [ADR-0049](adr/0049-shared-application-infrastructure-registration.md).
It also constructs the application catalogue and scans Core operations at the
same positions in the CLI registration sequence; see
[ADR-0050](adr/0050-shared-application-composition.md).
Host suggestion metadata and the typed-source suggestion catalogue are described
in [ADR-0051](adr/0051-shared-host-command-metadata.md) and
[ADR-0052](adr/0052-typed-source-suggestion-catalogue.md).

See [ADR-0003](adr/0003-shared-command-descriptors-and-application-ports.md), [ADR-0007](adr/0007-host-neutral-command-contracts.md), [ADR-0010](adr/0010-revision-bound-continuation-tokens.md), and the [migration playbook](command-application-port-migration.md).

### 2. Explicit request context

Every operation receives explicit, absolute request context:

- request ID and, for apply, a caller-known structured operation ID from a bounded authority-issued epoch;
- repository root and solution path;
- configuration and SDK/target-framework selection;
- requested trust constraint, authority-derived effective repository trust, and evaluator mode/policy;
- cancellation and resource budget; and
- protected authenticated-principal and action context for authorization;
- source/revision identity where applicable.

Operations do not infer repository identity from process current directory and do not use global console, `Environment.ExitCode`, or arbitrary process environment as mutable request state. This is required for concurrent repositories, host parity, reproducibility, and future isolation.

### Host configuration boundary

CLI and MCP startup share a typed host configuration resolver. It reads optional
schema-version-1 JSON from `~/.rt/config.json` and from `.rt/config.json` at the resolved
Git worktree root. It does not search arbitrary parent `.rt` directories. Both files are
strict: malformed JSON, unknown members, unsupported versions, invalid values, and
invalid exact MCP tool names fail normal host startup with the responsible path.
The loader, effective host configuration, and MCP descriptor policy live in shared
Hosting; Core has no host configuration implementation. An architecture placement
test guards this boundary; see [ADR-0059](adr/0059-host-configuration-hosting-ownership.md).
`rt config` is a local host inspection command that reports selected paths, effective
values, and their sources without MSBuild registration or daemon routing.

Daemon scalars resolve in this order: built-in defaults, global file, worktree file,
environment, then an applicable explicit host argument. MCP policy composes instead of
replacing: configured allowlists intersect, denylists accumulate, omission is
unrestricted at that layer, an empty allowlist permits no tools, and denial wins. The
same MCP availability predicate governs discovery and dispatch. Configuration never
grants repository trust and never hides ordinary CLI commands. See
[ADR-0020](adr/0020-layered-host-configuration.md).

### 3. Repository trust and workspace completeness

For the implemented acquisition boundary and its deliberate preview restrictions, see
[Workspace acquisition evidence](workspace-acquisition-preview.md) and
[ADR-0021](adr/0021-authoritative-workspace-acquisition-evidence.md). Production acquisition
evaluates the declared project and target-framework graph before Roslyn loading, reconciles it
with the exact design-time snapshot, and can certify `Complete` when every required input and
stage is accounted for.
The [unused-symbol liveness policy](find-unused-liveness.md) independently retains external
dispatch and module-initializer entry points.

Loading an MSBuild solution can evaluate repository-controlled build logic and can involve analyzers or generators. A local checkout is not automatically trusted.

Effective trust comes from the OS/service-protected authenticated-principal-scoped
registry defined by [ADR-0011](adr/0011-authoritative-trust-and-evaluator-isolation.md).
It binds canonical repository identity to verified physical identity. A request trust
value can constrain or assert an expected state but cannot elevate it. Only deliberately
invoked authenticated grant/revoke operations through a trusted surface change the
audited record; absent, revoked, unreadable, mismatched, stale-binding, or
non-interactively bootstrapped decisions are `Untrusted`.

Trust state and hot audit storage are bounded by authority-wide and per-principal
count/byte quotas, with grant rate/concurrency limits. Grant reserves worst-case
revocation and audit-compaction capacity before persisting `Trusted`; exhaustion
returns `TrustRegistryCapacityExceeded` and leaves the binding untrusted, while revoke,
status, termination, and reconciliation remain available. Completed quiescent
revocations compact behind a monotonic trust-decision watermark into a fixed-size
authenticated accumulator before detailed records are removed. Compacted absence is
still `Untrusted`, stale restored grants at or below the watermark fail closed, and
only a later deliberate quota-admitted grant can establish trust again. Startup or
restore without continuity proof refuses grants and evaluates as untrusted.

Grant, revoke, and `InProcess` evaluator admission share a linearizable authority gate
for the principal and verified repository binding. Admission atomically revalidates
the grant and acquires a revocable lease for the current monotonic trust generation
plus a held verified-root capability before repository-controlled code can run. The
actual solution/build open consumes its handle-relative or immutable snapshot view,
never a re-resolved ambient path; an evaluator that cannot preserve the physical
binding through startup is refused in-process. Revocation closes admission, advances
the generation, persists `Untrusted`, and cancels or terminates old-generation evaluations
and descendants. It reports completion only after verified quiescence; otherwise its
durable state is `RevocationPending`, new in-process admissions and replacement grants
remain fenced, and missing lease state fails closed into reconciliation.

Every in-process evaluator host and process it can create is placed before admission in
a non-breakaway OS descendant domain owned by an external supervisor that executes no
repository code. Repository code cannot detach, re-parent, disable, or escape it. On
revocation the supervisor cancels cooperatively, then terminates the per-admission
domain or the whole containing host if attribution is not provable; durable revocation
status completes only after the OS reports the revoked generation empty. Platforms
without non-escape, external kill, and empty-domain proof refuse `InProcess`.

Workspace acquisition and provenance carry two independent values. Effective
repository trust is **Trusted** after an authoritative explicit scoped decision or
**Untrusted** when that decision is absent or revoked. Evaluator mode is one of:

- **NoEvaluation** — only operations proven not to invoke repository-controlled code may run; for untrusted repositories, every host read also uses the root-contained safe-reader boundary below.
- **InProcess** — repository-controlled evaluation may run in the request host and is valid only for a trusted repository.
- **IsolatedWorker** — evaluation runs in a constrained worker even when the repository is trusted.

An untrusted repository can use `NoEvaluation` or `IsolatedWorker`, never
`InProcess`. A trusted repository may still request `IsolatedWorker`; choosing
isolation does not change or imply its trust state. The isolated worker runs under a
distinct least-privilege identity with OS-enforced process/PID, process-handle, and IPC
isolation. It cannot impersonate the host user, inspect/control host processes, reach
undeclared IPC, or inherit ambient handles; only bounded schema-validated
operation-scoped supervisor channels are available and every child remains contained.
The worker also receives an OS-enforced allow-listed filesystem namespace containing
only the read-only repository, declared read-only SDK/package/tool inputs, and separate
disposable writable output, cache, and temporary locations outside the checkout. Host roots,
user profiles, credentials, SSH material, undeclared package caches, and devices are
not visible. Checkout inputs are opened relative to the verified root with no-follow
and final-handle identity/link checks; unproven multiply linked files are rejected
before reading, and accepted content uses private non-host-aliasing inodes or an
equivalently proven immutable alias-contained mount. The worker has bounded resources,
no inherited secrets, and no network
during any phase that executes untrusted repository code. Required packages and tools
are acquired beforehand by a separate trusted broker that executes no repository code,
accepts only reviewed lockfile-pinned coordinates from allow-listed registries, and
verifies hashes/signatures before mounting them read-only. Missing inputs never trigger
restore inside the worker. Repository write attempts, undeclared host reads, and all
network egress are denied and recorded; platforms that cannot prove identity,
process/handle/IPC, filesystem, and egress isolation refuse untrusted evaluation.
Admission also reserves aggregate host capacity. Descendant-inherited OS limits cap
process/task and thread creation, process-table entries, handles/descriptors, CPU,
memory, time, and IPC/result bytes, while disposable filesystems enforce byte and
inode/file-count quotas. Every terminal or reconciliation path performs bounded
descendant termination and writable-namespace teardown; unverified cleanup quarantines
the namespace and its capacity rather than making either reusable.

Long-lived CLI, MCP, and daemon hosts never evaluate an untrusted repository in-process.

The current `0.1-preview` containment is narrower than the accepted durable authority
design. `rt trust add <repository>` records a reviewed Git common-directory identity in
the current user's local application-data directory. CLI, MCP, and daemon boundaries
enter the same persistent scope and revalidate each requested solution against Git's
current registered-worktree inventory. A root-commit object retained at grant time is a
best-effort continuity anchor, so an unrelated repository recreated at the same path
fails closed. It is repository-self-asserted continuity, not OS handle, inode, or file-ID
identity: a replacement containing the same Git object can still satisfy it.
On Unix, existing symlink components in repository roots and requested solution paths
are resolved to their final physical paths before Git identity and containment checks;
broken links, loops, and links escaping the trusted worktree fail closed. This closes
lexical-alias confusion in the preview but does not provide handle-relative, race-free
TOCTOU protection or the OS-verified identity required by the durable design.

The invocation-scoped CLI `--trust-repository` option and MCP startup
`--trust-repository <absolute-repository-root>` remain compatible. MCP tool payloads
cannot grant or broaden trust, and trust-management ports are never MCP-discoverable.
An MCP host advertises MCP-eligible mutation operations only when startup trust exists
or the persistent registry has at least one currently valid record; each call still
revalidates its own target and returns `TrustRequired` for any other repository. This is
a practical trusted-local preview path, not the durable protected physical-binding,
audit, quota, revocation, lease, or evaluator-isolation guarantee specified by ADR-0011
and platform hardening. For in-process recovery, the adapter deserializes an absolute
solution or workspace target and revalidates it before SDK selection; request payloads
therefore cannot make an untrusted `global.json` participate in process-global setup.
The persistent daemon boundary follows the same ordering: it resolves the effective
request target, proves current registry trust, and only then allows its SDK-context
guard to inspect that target. Malformed or untrusted targets return `TrustRequired`
without SDK inspection; invocation-scoped trusted CLI execution remains in-process.

They also never treat `NoEvaluation` as permission for ambient reads. Untrusted
file-only operations use a shared handle-relative reader rooted at the verified
checkout, with no-follow/reparse-safe component opens, post-open filesystem identity
checks, declared regular-file/directory inputs, and conservative hard-link handling.
Symlinks, junctions, mounts, reparse points, alternate streams, special files, or
unproven external aliases are denied; if the boundary is unavailable, the operation
runs in the allow-listed worker or is refused. Redirected content cannot reach
results, diagnostics, logs, or caches.

Each workspace request declares a semantic-input manifest: requested projects and
transitive references; selected target frameworks, configurations, and compilation
contexts; source/additional/analyzer-config/generated documents; project, metadata,
framework, SDK, package, and imported build inputs; and operation-required evaluator,
generator, or analyzer stages. Acquisition records each entry as loaded/resolved,
failed, policy-blocked, or explicitly excluded, with its diagnostic completeness
impact.

Workspace acquisition is **Complete** only when every required manifest input is
available at the declared revision and every required stage succeeds in the declared
evaluator mode. An exclusion counts as complete only under a validated
operation-specific irrelevance premise. It is **Partial** when usable semantic scope
remains but any required input is missing, unresolved, failed, or blocked, and
**Failed** when no usable model or minimum declared scope remains. Results also carry
requested/loaded/failed projects, all diagnostics, SDK/compiler/configuration, trust
state/evaluator mode, source identity, and revision. Suppressing warnings from a
human renderer never removes the manifest, diagnostics, or completeness decision
from structured provenance.

Partial workspaces cannot produce proof-strength conclusions, automatic deletion advice, or mutation suggestions. Confidence is a shared structure with level, basis, evidence, and limitations—not an analyser-local adjective.

Beneath the workspace assessment sits a document-level precondition: absence of references
is evidence only where the source was understood. A document carrying a syntax error makes
no absence claim at all, and a symbol is reported as an unused method only where the
declaration site really declares one — Roslyn recovers from syntax it cannot read by
guessing at the nearest declaration it knows, and a guessed symbol has no references by
construction. The completeness assessment names every project that failed to compile and
states what its diagnostic budget left out, so a project missing from that list can never
be read as a project that compiled. See [ADR-0019](adr/0019-parse-integrity-gates-absence-claims.md).

### 4. Category and capability contracts

A command implements exactly one category marker:

- `IInspectCommand` — read-only, focused inspection;
- `IAnalyzeCommand` — read-only, broader analysis; or
- `IRefactorCommand` — prepares or applies source changes.

`IControlPlaneCommand` is a narrow subtype of `IRefactorCommand` for explicit,
local, user-authoritative control-plane actions such as `rt trust add/remove`.
Its verb is itself the apply decision, so repository-source `--dry-run` and
review-first gates exclude only this marker. Control-plane commands remain
in-process and must not become MCP- or daemon-invocable through catalog inclusion.

Reusable options are capabilities backed by a central catalogue and shared binders, for example `IHasSolutionOption`, `ISupportsDryRun`, and `IHasSymbolTarget`. Services receive domain requests, not parser types. Known flags are not recreated command by command.

The current capability migration is intentionally incremental, but no new command may deepen the ad hoc option model.

### 5. Result and error contract

Two types serve different layers:

- **`Result<T, RtError>`** represents routine expected application outcomes such as symbol-not-found, ambiguous match, invalid input, stale plan, or operation conflict.
- **`RtResult<T>`** is the host-rendering envelope for status, diagnostics, and exit behaviour.

Expected failures are typed and total. Host adapters project them through shared presenters. Raw exception text, stack traces, and host-specific error ladders are not the application contract.

Every structured operation response carries, where applicable:

- schema version and request ID;
- canonical repository ID, operation ID, and operation/transport status;
- typed data;
- typed diagnostics;
- workspace and source provenance;
- confidence and limitations;
- safety state and affected paths;
- next actions; and
- paging/continuation state.

Continuation state is opaque, integrity protected, and bound to one canonical request,
repository/workspace identity, source and semantic-input revision, projection/filter/
total-order policy, cursor, authorization context, and bounded expiry under
[ADR-0010](adr/0010-revision-bound-continuation-tokens.md). Each page is reauthorized
and comes from the exact quota-bounded retained snapshot, or fails with a typed stale,
expired, invalid, or unauthorized continuation error. No page silently reevaluates a
newer checkout under an older cursor.

### 6. Mutation prepare/apply/recover contract

A boolean `dryRun` branch is not a mutation safety model. Stable mutations use:

```text
EnrollRepository(enrollmentRequest) -> RepositoryIdentity
Prepare(request) -> PreparedChangeSet
GetOperationEpoch(repositoryId) -> OperationEpochToken
Apply(repositoryId, planId, planHash, operationEpochToken, operationId, requestHash, expectedRevision, replayEpoch?) -> ApplyResult
GetOperation(repositoryId, operationId, requestIdentity?, operationEpochToken?) -> OperationStatus
Recover(repositoryId, operationId, action) -> RecoveryResult
```

Until the durable mutation kernel and its failure/recovery gates pass,
[ADR-0012](adr/0012-review-first-mutation-defaults.md) requires every apply-capable
command to default to non-writing `Review`, or `DryRun` when review-only analysis is
not meaningful. `Apply` requires explicit caller selection and remains in-process.
Existing `apply-default` catalogue entries are containment debt, not a migration
target; port conversion alone cannot preserve or justify them.

Repository identity and mutation coordination span OS principals and hosts. An OS-
or service-protected registry outside repository control maps checkout/Git-common-dir filesystem identity
to one non-reused repository ID and records mode, owner/authority, active recovery
barrier, and retirement state; no per-user store can shadow it. Every would-be
mutation principal/host resolves this registry before intake; machine-local state is
valid only when other hosts cannot write the filesystem. First-time enrollment is an
explicit authorized operation, never an apply side effect: under a handle-derived
physical-identity fence, a continuity-complete registry atomically advances its
protected authority-wide repository sequence and persists the complete binding,
coordination mode, initial no-barrier state, and audit entry. Concurrent enrollment
returns the same record; crash-created sequence gaps are allowed but reuse is not. A
coordination authority itself is provisioned separately by an administrator in a
demonstrably fresh OS/service namespace with a non-rollback continuity anchor;
enrollment cannot reprovision it. A
healthy absent mapping returns `RepositoryNotEnrolled`; missing, inconsistent,
rolled-back, or unreachable authority state blocks both enrollment and mutation as
`RejectedUnsafeCoordination` instead of treating the registry as empty. Normal
retention never removes an active barrier.

Repository admission is bounded authority-wide and per principal by live count/bytes,
enrollment rate/concurrency, and retired-binding index count/bytes. Before sequence
advancement, enrollment reserves its live/audit capacity and one eventual fixed-size
retired-binding entry; exhaustion returns `RepositoryCapacityExceeded` without
stranding existing status, recovery, transfer, or retirement. Retirement folds bulky
detail into a fixed-size cumulative authenticated `RetiredRepositoryAccumulator` and consumes the
reserved protected index entry containing domain-separated keyed repository/binding digests, final
generations/watermarks, and audit root. The continuity-protected index has no deletion
or false-negative path; ambiguity rejects enrollment, and a full index blocks new
enrollment rather than allowing unbounded growth or identity reuse.

Every apply plan records a
`CoordinationDomainId` and proves either `ExclusivePrincipal` ownership (no other
non-administrative principal or host in the threat model can write/replace the
checkout, Git common directory, and their
replace-capable ancestors; its payloads may remain private while admission and active
barriers remain in the registry) or a `SharedAuthority` outside repository control
used by every authorized principal and adapter. The shared authority owns the intake
fence, transaction reservation, active state, `RecoveryRequired` barrier, fencing
epochs, bounded outcome retention, the retirement watermark/accumulator, and pinned
records. Without either proof the repository is inspect-only and apply
returns `RejectedUnsafeCoordination` with no
repository effect. Ownership/authority changes are revalidated at every boundary;
transfer is explicit, preserves repository identity, and enters an authority-wide
`TransferPending` fence that blocks new epochs, preparation, dispatch-plan leases,
intake, and replay. It
waits for or reconciles every active operation under the existing reservation, resolves
or acknowledges recoverable barriers, proves every effect fence closed/drained (or its
worker terminated with write capabilities closed), treats `OwnerQuiescencePending` as
non-acknowledgeable, and proves dispatch-lease/intake/reservation/nonterminal state quiescent,
carries retained/pinned state, then atomically increments `CoordinationGeneration`,
installs the new owner/writer set, and rotates credentials. A crash leaves old
ownership, completed new ownership, or the durable fence, never two writable domains.

The immutable prepared plan contains all path and project operations, expected revision
and hashes, coordination-domain/ownership proof, encoding/newline/file-mode policy,
symlink/hard-link/root-containment policy, exact diff, diagnostics, assumptions,
verification, expiry, and recovery data. `PlanHash` is SHA-256 over one versioned
host-neutral canonical plan payload with the `PlanHash` field omitted entirely; shared
vectors keep every adapter byte-identical, and verification reserializes that payload
before comparing the digest. [ADR-0022](adr/0022-canonical-immutable-change-plan-payload.md)
defines the implemented `rt.change-plan.v1` byte contract: immutable copied collections,
explicit sequence numbers, normalized repository-relative paths, exact replacement
and original bytes, guarded read dependencies, resolved-path and platform-identity
preconditions for repository, control, staging, backup, dependencies, and effects, a
diff derived from the exact bytes, fixed property order and wire tokens, canonical
diagnostic ordering, explicit nulls/defaults, and UTC timestamps. Its first version
accepts writable regular file effects and rejects long paths, symbolic links,
hard-linked regular files, read-only entries, alternate data streams, conflicting
ancestors, and shared file identities. Guarded directory dependencies retain their
observed directory link count as a precondition.
The envelope retains the exact canonical bytes, compares those bytes exactly, and
compares the claimed digest in constant time. Source/destination paths are repository-contained;
staging, backups, and control artifacts use a protected operation root outside and
non-overlapping with the repository, on the target volume when required. Before
prepare returns, its canonical envelope is atomically persisted outside repository
control in a user-private store for `ExclusivePrincipal` or authority-controlled
storage for `SharedAuthority`. Preview and apply consume that same repository-scoped,
hash-verified plan across process restart.

The canonical payload and envelope verification are implemented foundations.
[ADR-0023](adr/0023-protected-durable-change-plan-storage.md) adds exact-byte envelope
rehydration plus the fail-closed `ExclusivePrincipal` storage engine: a required trusted
host lease proving and safely provisioning a physically repository-external owner-private
root before any engine filesystem mutation, digest-only repository path components,
durable complete-record replacement, typed
corruption/schema/hash/expiry failures, count and byte quotas, bounded dispatch pins,
exact source-bound adoption by an operation-ledger pin, and tombstone-based collection.
The engine samples time under its mutation lock, flushes file and directory metadata,
and retains corrupt or pinned evidence. Its built-in unavailable protection authority
refuses use, as does the engine for `SharedAuthority`; a production physical-protection
authority, an authority-owned shared provider, preparation/apply integration, full
dispatch leases, cleanup bundles, and restart recovery remain required
before `rt` can claim this cross-process apply flow.

Under [ADR-0013](adr/0013-fenced-plan-unavailability-resolution.md), a quota-bounded
`DispatchPlanLease` validates and pins the exact plan under the caller-known operation
and canonical request identity before any request bytes are sent. `Received` atomically
adopts that pin. Garbage collection retains it through authoritative dispatch
resolution and every nonterminal state, including `Recovering`, through
`OwnerQuiescencePending` and `RecoveryRequired`, and while a `CancelledNoEffect`
control bundle awaits verified
cleanup. Any terminal no-effect operation, including `Expired`, keeps the adopted plan,
journal, artifact inventory, quota, and cleanup capability pinned while
`CleanupPending`; `RejectedPlanUnavailable` keeps its immutable identity/hash, any
remaining verified bytes, failure evidence, and the rest of that cleanup bundle without
reconstruction. Durable lease/replay expiry or epoch sealing releases only an
unadopted pin for which no operation/control artifact exists; an adopted pin releases
only after verified terminal cleanup or explicit recovery acknowledgement.

Apply creates a durable operation record, initializes its journal before staging, stages only in the outside-repository control root, acquires an exclusive canonical-repository transaction reservation, claims a renewable owner lease and monotonically increasing fencing epoch, validates every precondition, arbitrates cancellation against a complete journalled first-effect permit in one durable compare-and-swap, journals changes, revalidates the expected intermediate repository state at every side-effect boundary, applies deterministically, verifies postconditions, and records one truthful state. A first-effect permit is eligibility to request one effect, not reusable write authority: a non-bypassable OS-enforced broker/fence revalidates authority at the actual conditional filesystem boundary and serializes effect admission with fence closure. Interrupted pre-effect staging cleanup is idempotent and never touches the repository.

Pre-effect staging/backup/control-root writes, guard acquisition/release, and final
preflight also use a non-bypassable `PreEffectActivityFence` bound to an owner control
generation. Cancellation or another no-effect resolution closes new effect and
pre-effect admission and advances that generation atomically. Admitted activity must
drain, all guards must release, and an exclusive rescan must prove the operation root
empty before cleanup can be marked verified; otherwise the terminal no-effect result
remains `CleanupPending` with its plan, journal, artifacts, quota, and cleanup capability
pinned.

The internal guarded apply path follows [ADR-0036](adr/0036-atomic-pre-effect-guard-handoff.md):
failed or cancelled preflight releases its guard before draining the activity.
Successful preflight hands that guard to `Applying`, the owner lease, the closed
pre-effect fence, and the first effect intent in one durable ledger replacement.
If the replacement is visible but its flush fails, no effect runs and the
unresolved intent remains for reconciliation. A physical guard and public Apply
still require the platform authority work tracked in #138 and #140.
The internal ledger now records admission, acquisition intent, and confirmed
inactivity as a [durable guard history](adr/0040-durable-pre-effect-guard-history.md).
Acquisition intent precedes the broker call. No-effect drain requires inactivity
after an attempted acquisition; a missing history or uncertain release keeps the
admission and plan pin. Restart loading binds an unresolved handoff to the first
effect intent's fence. A completed null broker result or successful session
disposal is the internal proof of inactivity; #138 still needs a physical broker.

- `Received` or authoritative `NotRecorded` during replayable dispatch resolution;
- `Applying`, `Recovering`, `OwnerQuiescencePending`, or bounded `Reconciling` while nonterminal;
- `Applied`;
- `RolledBack`;
- `RecoveryRequired` as a durable intervention state;
- `RecoveredApplied` or `RecoveredRolledBack` after verified manual recovery;
- `RecoveryAcknowledged` as an administrative terminal state that does not claim cleanliness;
- `CancelledNoEffect` as a terminal accepted cancellation with proof that no repository effect began;
- `RejectedPlanUnavailable` as a terminal no-effect fence when the leased exact plan cannot be verified before the first effect;
- `RejectedUnsafeCoordination` when cross-principal/cross-host mutation coordination cannot be proven;
- stale/conflict rejection; or
- expiry.

The ledger keeps a logically append-only transition history as well as the current state. An incomplete rollback is `RecoveryRequired`; it is never presented as clean failure or rollback. That state is committed to the coordination authority and cross-principal/cross-host registry before reservation release and blocks a different apply from any principal or host until fenced recovery verifies `RecoveredApplied` or `RecoveredRolledBack`, or an authorized user explicitly records `RecoveryAcknowledged`. Acknowledgement performs no repository effect and records actor, reason, observed hashes, unresolved warnings, and the absence of a cleanliness guarantee.

Repeated recovery history follows [ADR-0014](adr/0014-bounded-recovery-attempt-history.md).
Original operation admission reserves a fixed-size authenticated attempt accumulator,
monotonic attempt sequence and compaction watermark, bounded hot attempt count/bytes,
one in-flight journal segment, a plan-bounded current checkpoint and active
recovery-artifact set, and the next truthful transition. Before a new effect, only fully quiesced old attempts can be folded into the
accumulator and release their slot. The current checkpoint retains the exact actionable
path/journal/intent evidence and active-artifact inventory needed by the next attempt;
arbitrary diagnostics are capped and digested. An unresolved owner/effect or failed continuity proof leaves the
barrier active and returns `RecoveryAttemptUnavailable` without a repository effect.
Sequences are never reused, so compaction bounds storage without making history
rewritable or stranding later recovery outcomes.

An expired `Applying` owner is never left or reported as healthy live work. Startup and
status lookup first close and drain the effect fence or prove worker termination and
write-capability closure. Proven quiescence permits one reconciler to advance the epoch,
transition through `Reconciling`, and use the durable plan, journal, backups, and observed hashes to
prove `Applied`, prove `RolledBack`, or require explicit recovery. If the fenced
reconciler instead proves the intact journal has no `FirstEffectStarted` permit and no
repository effect was possible, it reruns final preflight and may record the applicable
`CancelledNoEffect`, `RejectedStale`, `RejectedUnsafeCoordination`, or `Expired`
outcome without reverting external drift; ambiguous evidence is `RecoveryRequired`.
Until quiescence is proven, the operation remains `OwnerQuiescencePending` behind the
authority-wide intervention barrier rather than falsely advancing ownership.
An expired
`Recovering` owner is reconciled under the same reservation, lease, and fencing
rules and ends as `RecoveredApplied`, `RecoveredRolledBack`, or
`RecoveryRequired`.

The reservation is acquired before final whole-plan preflight and held through the
durable terminal result, verified rollback, or explicit recovery handoff. Because it
does not exclude editors/builds, apply also acquires a deterministic OS-enforced
guard set over every reviewed dependency, affected object, and directory entry, or
uses equivalent atomic conditional effects. Both boundaries remain held through
final verification and durable outcome; advisory locks and unconditional atomic
replacement are insufficient, and unsupported platforms block unattended apply.
No second
apply or recovery transaction for the repository can enter final validation while
it is held. At every journalled effect, the owner verifies the reservation, ledger
state, fencing epoch, lease, prior effect outcomes, and every still-relevant plan
dependency. A reconciler acquires the same reservation before advancing the epoch;
it also closes and drains the effect fence or proves worker termination and write-handle
closure before advancing. If neither can be proven, the epoch remains current and the
operation enters nonterminal `OwnerQuiescencePending` behind an authority-wide barrier;
it cannot transition to or acknowledge `RecoveryRequired`, transfer, retire, or admit
queued work while the stale holder may execute. After safe takeover, a resumed stale
owner cannot pass broker admission or perform another write. Drift before the first effect returns stale rejection; drift
after any journalled effect stops the remaining plan and verifies rollback to
`RolledBack` or `RecoveryRequired`, never stale rejection or applied success.
Recovery artifacts stay pinned through `RecoveryRequired`
and `Recovering`; verified recovery releases them only after cleanup, while explicit
acknowledgement follows the recorded retention choice and preserves an immutable
audit summary.

After durable `Received`, cancellation is recorded against the composite operation
identity. If receipt-to-ledger promotion is pending, the same cross-process intake
fence atomically lets either promotion or cancellation win, so cancelled work cannot
be enqueued afterward. Before the first repository effect, cancellation competes
with the owner's effect permit in one store-enforced compare-and-swap. It wins as
`CancelledNoEffect` only when `FirstEffectStarted` is absent; the record includes its
request hash, timing, journal sequence, staged-artifact inventory, no-effect proof,
and cleanup status. The winning transaction also closes pre-effect activity admission
and advances its control generation, so a stale worker cannot create another artifact
or acquire a guard after cleanup. If the effect permit wins, later cancellation is recorded but
verification and rollback remain non-interruptible. Replay returns the existing
cancellation result while its outcome record is retained and
`OperationEpochRetired` after compaction; neither restarts work. Startup/status lookup
performs idempotent control-artifact cleanup outside the repository only after the
pre-effect fence drains and guards release, while retaining the plan until cleanup is
verified.

Before dispatch the caller obtains a bounded, authenticated repository operation-epoch
token and constructs `OperationId` from its monotonic epoch plus a caller-generated
nonce. The operation epoch is distinct from owner fencing epochs. Epoch tokens are
repository/principal-bound, schema-versioned, count/byte/time-limited, and carry
`NotAfter`; the separate one-use `NotRecorded` replay epoch never extends token
validity. The caller also generates the canonical request hash before dispatch.
The hash is SHA-256 over one versioned host-neutral serialization of all semantic
apply fields, including the structured operation ID and
`OperationEpochClaimsHash`; that claims hash covers the token's canonical public
scope, epoch, limits, and `NotAfter`, but not its signature. The request hash omits
`RequestHash` itself, replay and transport attempt metadata, cancellation state, and
authentication proof. Every adapter uses the same canonicalizer and vectors instead
of hashing its wire representation.
`OperationId`, canonical public `OperationEpochClaims`,
`OperationRequestIdentity`, and the versioned apply-request byte projection are
implemented internal foundations with fixed conformance vectors. Token issuance and
authentication, adapter integration, dispatch leases, intake-attempt fencing, and
replay authorization remain required before this identity participates in
mutation dispatch.
Before sending request bytes, the caller registers a quota-bounded
`DispatchPlanLease` that validates the epoch/principal, canonical request identity,
and exact plan envelope/hash, reserves dispatch-resolution outcome capacity, and pins
those bytes through receipt adoption, authoritative status resolution, or epoch
sealing. Failure occurs before dispatch;
the lease alone does not mean the request was accepted.
Canonical repository ID plus operation ID is the composite key for every receipt,
ledger, status, recovery, acknowledgement, and retention lookup; the same operation
ID in another repository is independent. It is not an authorization capability.

Lifecycle access follows [ADR-0015](adr/0015-operation-lifecycle-authorization.md).
Every dispatch resolution, replay, status, cancellation, resume, rollback,
acknowledgement, cleanup, and reconciliation request is reauthorized for the protected
authenticated principal, declared action, current repository coordination generation,
and authorization generation. Submitters need explicit current own-operation
read/cancel rights; recovery effects require a recovery operator, and non-clean
acknowledgement requires a distinct acknowledger or lifecycle administrator. A stale
role/proof or another principal's known operation ID returns `OperationAccessDenied`
without existence-specific evidence and changes no state. Other authorized writers see
only the generic recovery barrier needed to block new work. Endpoint authentication
does not replace this application-level decision, and internal reconciliation uses a
narrow authority-service identity rather than a bypass.
Before accepted work enters an execution queue, a cross-process, store-enforced
intake fence keyed by repository and operation ID protects a durable `Received`
receipt containing the canonical request. Intake validates the token, claims hash,
principal binding, matching dispatch lease, exact plan availability, and reserved
outcome capacity first; token/quota validation, plan-pin adoption, and receipt
creation are atomic with epoch sealing. Startup and status
lookup reconcile any receipt not yet promoted into the operation ledger. While a live
or retained record exists, an authorized matching submitter repeating the same operation ID and request returns the
existing receipt/state/result and reusing it for different work returns conflict to the
incoming attempt without changing the canonical receipt, ledger, pins, or outcome;
conflict is not a terminal operation state. Another principal receives access denied
without the existing/conflict distinction.

[ADR-0024](adr/0024-durable-operation-intake-ledger.md) implements the first protected
durable intake/ledger engine behind that contract. One atomic versioned snapshot binds
the `Received` receipt, exact canonical request bytes and identity, authenticated
submitting principal, current state, and contiguous monotonic transition history to
the composite repository/operation address. Exact concurrent intake converges on one
receipt, an authorized exact replay performs no write, conflicting reuse is a
response-only outcome, another principal receives no existing/conflict distinction,
and corrupt evidence is never overwritten. Complete records are durably replaced
under a cross-process lock through an injectable flush/replace/flush boundary. Like
the plan store, the engine requires a host-issued physical-protection lease and its
default authority fails closed. Production protection, lifecycle authorization,
dispatch-pin adoption, quotas/retention, public status, journaling, effects, and
recovery are still intentionally unwired.

Terminal outcomes remain individually addressable for a bounded replay window. The
authority caps dispatch leases/pinned plan bytes, open operations, simultaneously
open/unretired epochs, epoch issuance, accepted count/bytes, retained outcome bytes,
and pinned recovery/cleanup exceptions;
exhaustion rejects new mutation admission without blocking status or recovery. A
sealed epoch rejects an otherwise unknown operation as `OperationEpochSealed` and
cannot create `Received` or `NotRecorded`; sealing atomically converts outstanding
`NotRecorded` records to terminal `Expired` and invalidates their replay authorization.
Admission reserves the bounded ledger,
outcome, and recovery-metadata capacity an accepted operation may need, so quotas
cannot block truthful terminal/recovery state. After sealing an epoch, the authority
folds eligible outcome
digests into a cumulative accumulator, advances a monotonic `RetiredThroughEpoch`
watermark, and only then deletes individual records. Pinned/live records resolve
first. Every otherwise unknown ID at or below the watermark returns
`OperationEpochRetired`, never `NotRecorded` or executable work. The watermark and
accumulator are constant-size and never move backward. Identity retirement is a
separate `RetirementPending` fence that blocks new mutation, transfer, and dispatch
leases but preserves status/recovery while all epochs seal, leases and active work
quiesce, every effect fence closes/drains or its worker termination is verified,
`OwnerQuiescencePending` clears, and recovery and cleanup
finish (or recovery is explicitly acknowledged), pinned records release, and retained outcomes compact
through the final issued epoch. Only then does one transaction replace the live
mapping with the enrollment-reserved fixed-size retired-binding entry and fold the full
marker into the retired-repository accumulator. A pinned operation therefore
can never lose its only status or recovery key.

### 7. Transport contract

Transport outcome describes only what the caller can prove:

- **NotDispatched** — no request bytes were accepted; policy may permit fallback or retry.
- **Completed** — a valid operation response was received.
- **Indeterminate** — dispatch may have occurred but no valid response is available.

An indeterminate mutation is never automatically retried or executed in-process. The
host already knows the repository/operation identity and queries status. If no receipt
or operation exists, status lookup validates the original token and canonical request
identity, inspects the matching dispatch lease, then uses the intake fence to exclude a
still-arriving request. If the exact plan remains readable and verified, it atomically
records `NotRecorded` with a replay epoch while the operation epoch is open; only then
may the caller explicitly resubmit the identical request under the same identity. If
the lease or plan is unavailable or invalid, it instead records terminal no-effect
`RejectedPlanUnavailable` with no replay authorization. Both outcomes fence a delayed
original, and the rejection must be observed before the caller re-prepares and uses a
new current-epoch operation ID. A sealed or retired epoch never authorizes replay.

Cancellation is `NotDispatched` only when no request bytes were accepted. Once any
bytes are accepted, cancelling or timing out the response wait is `Indeterminate`,
even before `Received` is durable; it is not an ordinary cancelled operation result.
The caller retains the same ID and status resolution races receipt creation under the
same intake fence. A winning `Received` is cancelled idempotently through the canonical
operation; a winning `NotRecorded` fences the receiver, and an in-flight receiver keeps
status indeterminate. No new apply or operation ID is created automatically.

For the current hardening phase, the daemon is an optional typed transport adapter under [ADR-0008](adr/0008-daemon-transparent-passthrough-phase.md), with fail-closed endpoint authentication under [ADR-0009](adr/0009-authenticated-daemon-endpoints.md). [ADR-0017](adr/0017-bounded-read-only-workspace-reuse.md) permits one narrow stateful exception: a bounded, content-fingerprint-validated lease cache for doctor, outline, describe, search_symbols, source, and relationships. It has no mutation workspace, operation authority, CLI argument bridge for mutations, process-global request context, or raw payload logging. Every connection authenticates the client principal and daemon instance through OS-enforced peer identity/access control or an equivalent protected mutually authenticated local channel. If that boundary cannot be proven, the daemon remains disabled and direct execution may be selected only before dispatch. It must preserve the same typed observable result as direct execution.

The CLI binds application arguments locally, and typed application adapters use
structured daemon RPC to invoke ports. Public read-only commands are typed or
explicitly local. The daemon's legacy envelope accepts only exact `ping` and
`stop` controls; other argument arrays receive an error and cannot enter CLI
parsing or execution. The stop client retains old wire fields solely to stop an
older orphan daemon. The receiver logs request length rather than payload content.
See [ADR-0054](adr/0054-cli-typed-daemon-dispatch-only.md) and
[ADR-0055](adr/0055-daemon-legacy-control-only.md).

MSBuild registration is process-global, so the daemon captures the exact SDK instance
it registers: normalized MSBuild path plus version. Each typed request resolves the SDK
instance for its effective solution directory before invocation. A different path or
version fails closed with structured `daemonRestartRequired` context naming the
registered and requested identities; the caller must restart from a compatible SDK
context. Linked worktrees are compatible only when they resolve to that same instance.
Daemon startup therefore refuses an unscoped `RegisterDefaults` or manual-path
fallback whose selected instance cannot be captured; direct in-process execution
retains those compatibility fallbacks.

The long-lived MCP host applies the same process-global identity invariant when daemon
execution is unavailable. It binds the first trusted in-process request to the exact
normalized MSBuild path and version selected from that request's effective solution
directory, never the server startup directory. Later worktrees may run only when they
select the same identity; mismatch or unresolved selection returns structured
`mcpRestartRequired` context before the application port runs. Persistent trust is
revalidated before SDK selection and registration. The MCP host does not use an
unscoped registration fallback because its identity could not then be proven.

## Output pipeline

```text
Application operation returns typed result/error envelope
                         │
                         ├─ CLI renderer -> text / JSON / YAML / Markdown / LLM view
                         ├─ MCP adapter  -> structuredContent + equivalent text view
                         └─ future IDE   -> native presentation
```

Rendering is downstream of application semantics. Output shapes are typed records, not command-local dictionaries. Strict JSON escaping remains the default unless a caller explicitly selects the relaxed compatibility view.

Human text is not the MCP machine contract. A structured MCP tool returns `structuredContent` that validates against its output schema, with text as an additional view.

Suggestions/follow-ons are application metadata projected by hosts. They are not invented by a renderer, and destructive follow-ons are suppressed when workspace completeness, confidence, or safety policy does not allow them.

## Capability catalogue as the source of truth

| Concern | Canonical source |
|---|---|
| Command/tool identity, aliases, category, visibility, maturity | Application definition |
| Command name, category, examples, mutability, and JSON-output flag | `RtCommandInfo` within the definition; CLI projection must match |
| Request/result types, validation, and alternative request variants | Typed operation contract and host-neutral property metadata |
| Safety, execution placement, limits, paging, trust requirements | Application definition/policy |
| Reusable option names, defaults, aliases | Capability catalogue |
| Capability interface to catalogue flag binding | Shared binders |
| Help, examples, known limitations, follow-ons | Same application metadata |
| MCP input/output schema | Generated from typed contracts, property metadata, and alternative request variants |
| `prime`/agent primer | Curated by the shared typed `PrimeOperation` |
| Generated command documentation | Catalogue projection, verified rather than rewritten by normal build |

If two surfaces describe the same fact, the catalogue wins and CI rejects the drift.

## MCP boundary

The MCP host uses the official C# SDK for stdio framing and the protocol lifecycle. The host projects its shared typed catalogue into SDK tools and maps SDK calls back to application ports; Core and Hosting have no MCP protocol dependency. Spawned-process tests cover legacy `initialize` versions and current `server/discover`. Broader conformance remains open under RT-012. See [ADR-0080](adr/0080-official-mcp-sdk-transport.md).

Stable MCP behaviour includes:

- correct request, response, notification, and parse-error handling;
- explicit supported-version negotiation or selection;
- cancellation and progress for long work;
- complete JSON Schema, including enums, bounds, nullability, nesting, and additional-property policy;
- schema-valid structured results;
- bounded request/output/item/time/concurrency/memory behaviour;
- revision-bound deterministic pagination, or explicit non-resumable projection/truncation; and
- no stable apply-capable CLI bridge.

Protocol version and server version are release facts derived from one source, not hard-coded aspirational values.

## Safety and quality gates

- **Architecture tests** enforce category, definition/port, output, dependency, parser, and host-boundary invariants.
- **Mutation safety tests** inject failures at every stage/write/replace/move/delete/verify/journal/rollback boundary.
- **Workspace tests** cover complete, partial, failed, trusted, isolated, and untrusted cases.
- **Protocol tests** spawn the packaged MCP process and exercise malformed input, notifications, version negotiation, cancellation, large results, schema validation, disconnects, and continued availability.
- **Parity tests** compare typed observable outcomes across direct, CLI, MCP, and transparent-daemon paths.
- **Filesystem tests** run on Windows, Linux, and macOS for encoding/BOM/newlines, permissions, locks, symlinks, hard links, case-only changes, and interrupted recovery.
- **Dependency/supply-chain gates** include locked release restore, vulnerability/dependency review, static security analysis, and controlled action dependencies.
- **Command admission** blocks stable commands without executable examples, validation, typed expected failures, limits, provenance/confidence, cancellation, and proportional fault-injection evidence.

Coverage and mutation-testing claims are executable CI configuration, not only documentation. Skipped tests for known defects remain linked to tracked work and do not count as safety evidence.

## Hermetic build and release

A normal `dotnet restore`, `dotnet build`, or `dotnet test` does not:

- format or rewrite source;
- regenerate tracked documentation;
- deploy skills or binaries into the user profile;
- start or depend on a daemon; or
- modify anything outside declared build/temp outputs.

Formatting, document generation, skill deployment, dogfood analysis, mutation testing, and installation are explicit commands or opt-in targets.

Assembly, NuGet, MCP manifest, archive, and release versions derive from one source. Documentation advertises only artifacts actually published and smoke-tested. Native release jobs install, launch, exercise, upgrade, roll back, and uninstall each supported RID while preserving files they do not own.

## Dogfood loop

```text
Agent or developer runs rt on a real task
  -> typed operation succeeds or returns an evidence-rich limitation
     -> limitation/bug is captured with request context and redacted diagnostics
        -> triage links it to one authoritative issue/spec
           -> TDD: failing regression -> implementation -> refactor
              -> parity + fault injection + coverage/mutation gates
                 -> generated metadata/examples verified
                    -> release/admission evidence recorded
                       -> next user benefits
```

The tool is its own customer, but dogfooding does not replace correctness evidence. A tool that analyses itself can share its own blind spots. External fixtures, adversarial projects, cross-platform filesystems, protocol clients, and fault injection remain mandatory.

## Current migration tensions

- **The canonical operation migration is incomplete.** A subset of commands has typed ports; the remainder still uses compatibility paths. The migration manifest is the current source for counts. Complete the five-command spine before broad fan-out.
- **Daemon application routing is typed.** The legacy envelope accepts only
  exact lifecycle controls; application operations use structured typed RPC.
  Mutation RPC remains disabled during the separate mutation safety work.
- **Durable Prepare is public but deliberately no-effect and experimental.** Core has a
  canonical plan, exact-byte reader, fail-closed durable plan and lifecycle stores, and
  the restart-reconcilable handoff in
  [ADR-0025](adr/0025-restart-safe-dispatch-intake-handoff.md). The production host now
  composes that handoff under fixed external local roots and projects one stored-plan-key
  application port through root CLI and trusted MCP surfaces as described by
  [ADR-0026](adr/0026-public-no-effect-durable-prepare.md). It resolves replay,
  conflict, and access denial before plan access, transfers
  `DispatchLease -> IntakePending -> OperationLedger`, converges on one
  `Received -> Prepared` history, and resumes after every implemented persistence
  boundary without changing repository bytes. `Prepared` authorizes no filesystem
  effect. Durable authenticated epoch issuance/quotas, mount-alias-safe handle-relative
  protection, repository enrollment, bounded attempt expiry and `NotRecorded`, public
  status and lifecycle authorization, repository reservation/barriers, a durable
  journal, verified rollback, automatic startup reconciliation, and recovery commands
  remain missing from the public path. [ADR-0027](adr/0027-internal-journaled-apply-barrier.md)
  adds an internal exclusive reservation and per-effect journaled apply authority.
  It loads the exact pinned plan and blocks a second operation while a mutation is
  unresolved. Its production effect broker is deliberately unavailable: no supported
  physical broker yet proves complete external-writer exclusion or conditional
  effects. Simulated fault tests cover the internal ordering and truthful unresolved
  state, but no public Apply or verified rollback/recovery is implied.
  [ADR-0028](adr/0028-internal-pre-effect-activity-fence.md) adds durable internal
  admission, generation closure, and drain evidence for broker acquisition and
  final preflight. A cancellation that wins before first effect intent closes
  admission and retains active work until it drains; an intent that wins requires
  the pre-effect activity set to be empty.
  [ADR-0029](adr/0029-internal-staging-intent-journal.md) adds an internal,
  restart-readable staging intent and verification journal bound to the exact
  plan, operation path, control generation, and reservation. Unverified intent
  blocks the first effect. It creates no staging file; protected artifact
  writes, backups, per-action control-root enforcement, cleanup, and public
  cancellation remain open in #140 and #123.
  [ADR-0030](adr/0030-internal-staging-creation-admission.md) inserts durable
  creation admission between intent and verification. It orders one named
  staging action against cancellation under the ledger lock and keeps an
  unresolved admitted action active across cancellation.
  [ADR-0031](adr/0031-handle-relative-physical-staging.md) adds an internal
  Linux x64 writer that holds a configured private staging root, creates one
  operation-scoped artifact after durable admission, flushes it, and verifies
  its physical bytes before journaling verification. This is not a deployment
  proof for a separate writer principal or repository effect authority; public
  Apply, backup, guard, cleanup, and recovery remain open.
  [ADR-0032](adr/0032-admitted-staging-retry.md) allows the exact admitted
  artifact to be reopened and verified after an interruption while the original
  reservation remains held. Fresh creation refuses an existing file; a new
  reservation cannot inherit the old admission. A same-activity repeat after
  `Verified` rechecks the existing bytes without creating an artifact.
  Cross-owner restart reconciliation and cleanup remain open.
  [ADR-0033](adr/0033-no-effect-cleanup-inventory.md) records an ordered
  `InventoryPending` cleanup event with the same durable no-effect transition
  that closes admission. It includes every admitted artifact, even when an
  earlier staging activity still has to drain.
  [ADR-0034](adr/0034-journaled-no-effect-staging-cleanup.md) adds internal
  Linux x64 cleanup under the same held staging reservation after the activity
  fence drains; every admitted artifact must bear that reservation's fence.
  Mixed-reservation inventory remains pending for shared-authority recovery.
  It journals each flushed artifact removal, removes only an
  empty operation root, flushes the parent of any missing root component,
  confirms durable `CleanupVerified` before releasing the plan pin, and retries
  interrupted boundaries. [ADR-0035](adr/0035-journaled-original-backup-and-cleanup.md)
  adds an internal Linux x64 path for the immutable plan's exact original
  bytes: backup intent and creation admission precede handle-relative creation,
  physical hash verification is journaled, and no-effect cancellation inventories
  both staging and backup artifacts for same-reservation cleanup. It does not
  verify the live repository original or grant repository effect authority.
  Cross-owner cleanup, guard cleanup, and public lifecycle routing remain open.
  [ADR-0037](adr/0037-verified-artifacts-before-effect-guard.md) routes exact-plan
  staging and original backup through the internal apply authority's current
  pre-effect activity before effect-broker acquisition. First-effect handoff
  now requires complete ordered verification evidence bound to that activity
  and reservation. The authority rechecks cancellation before guard acquisition
  and passes the activity token to the broker for boundary-time revocation.
  This remains an internal Linux x64 path with a disabled
  production broker. Restart reconciliation remains required before public Apply.
  [ADR-0038](adr/0038-terminal-no-effect-apply-cleanup.md) closes a completed
  broker refusal or guarded preflight only after guard release. An exception
  before guard acquisition returns a session leaves its activity active,
  artifacts pinned, and the repository mutation barrier in force because guard
  release is unproved. For a completed broker refusal or guarded preflight
  failure, one ledger replacement drains the pre-effect activity and records
  `RejectedUnsafeCoordination` with a durable artifact inventory, or retains
  an earlier `CancelledNoEffect`.
  A configured trusted
  cleanup capability then removes staging and backup artifacts under the same
  reservation, verifies empty operation roots, and releases the plan pin.
  Failure retains the terminal inventory and pin. Active or uncertain owners
  and effectful cleanup remain open; the production broker stays unavailable.
  [ADR-0039](adr/0039-local-restart-no-effect-cleanup.md) lets a fresh
  reservation from the same protected local authority resume a terminal,
  drained no-effect cleanup when all admitted artifacts share one earlier
  reservation. The cleanup service rescans operation roots before releasing
  the pin, including after a durable `CleanupVerified` record. Foreign roots,
  mixed provenance, active guard admission, and uncertain effects remain
  blocked. Shared authority and effectful recovery remain open.
  [ADR-0040](adr/0040-durable-pre-effect-guard-history.md) records guard intent
  before acquisition and confirmed inactivity before no-effect drain. It keeps
  an uncertain or unjournaled activity active and pinned, and rejects extra
  unresolved guard attempts during restart loading. Shared-authority
  reconciliation and a physical effect broker remain open.
  [ADR-0041](adr/0041-local-interrupted-pre-effect-reconciliation.md) lets a
  later reservation from the same protected local authority close and drain an
  interrupted pre-effect owner when its guard history proves no acquisition or
  confirmed inactivity. It records a terminal no-effect outcome and cleanup
  inventory, confirms that record durably before physical cleanup, and verifies
  empty operation roots before pin release. Before `CleanupVerified`, every
  cleanup, including partial and empty artifact journals, binds and scans both
  control roots from the exact retained, pinned plan. A replay with a retained
  plan repeats the root check until pin release is durably marked in the ledger;
  after that marker, replay returns the verified result without physical work,
  including after plan collection. A missing plan before the marker remains
  unresolved. Uncertain guards and mixed
  provenance remain pinned. Control writers and cleanup retain the local
  reservation until physical activity ends, including concurrent disposal.
  The wider process interruption matrix, shared authority, and physical effect
  broker remain open.
- **Workspace completeness remains local and operation-scoped.** Production MSBuild acquisition can close its declared manifest and certify `Complete`; arbitrary `AdhocWorkspace` instances remain unverified, and operations that depend on analyzer output or narrower compilation contexts must extend and validate their own manifest premises. Complete local acquisition does not establish a runtime closed world for reflection, framework activation, generated consumers, or external callers.
- **MCP is experimental until conformance gates pass.** The SDK transport and structured tool results are in place; pagination, cancellation, and other spawned-process conformance gates remain open under RT-012.
- **Capability flags remain partly ad hoc.** Continue the central-catalogue migration; do not create more command-local known options.
- **Output and suggestion fan-out remain incomplete.** Follow-ons, schemas, provenance, confidence, and limitations must be uniform before stable admission.
- **Build/release claims exceed current evidence.** Debug builds currently have side-effecting targets, version metadata claims `1.0.0`, and distribution verification is not yet native end-to-end. Treat release hardening as product correctness.
- **The broader workspace daemon remains parked.** ADR-0017 admits only bounded read-only reuse for six named semantic operations. Generic sessions, incremental mutation, durable/cross-machine state, hot-upgrade draining, and daemon-owned mutation still require the remaining trust, mutation, protocol, and canonical-operation gates plus a separate decision.

## Updating this document

When a decision changes a layer contract, trust boundary, mutation state, output doctrine, enforced invariant, compatibility promise, or accepted non-obvious trade-off:

1. Write or supersede an ADR under [adr/](adr/). Accepted ADR history is not silently rewritten beyond status/link maintenance.
2. Update this overview to reflect the resulting architecture, including current migration tension where implementation lags.
3. Update the most specific companion document and OpenSpec requirement/task where applicable.
4. Add or update executable architecture/acceptance tests for the decision.
5. Commit the decision, overview, specific document, and tests as one coherent change where practical.

Architecture documents distinguish **accepted target**, **implemented evidence**, and **remaining migration**. A target statement is never presented as a runtime guarantee until its gate passes.
