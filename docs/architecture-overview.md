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

`rt` is a semantic C# operation engine with multiple host adapters. The CLI is not the application layer, the MCP server is not a second implementation, and the daemon is not the owner of truth.

The target shape is:

```text
┌─ Host adapters ─────────────────────────────────────────────────────┐
│ CLI: bind arguments + render terminal output                        │
│ MCP: protocol lifecycle + bind JSON + project typed result          │
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

Optional daemon transport sits around the typed request/result boundary only.
It is not an additional semantic layer and does not own workspaces or mutations.
```

**Placement rule:** could the behaviour be tested without CLI, MCP, daemon, console, or process-global state in scope? If not, host concerns have leaked inward.

## Contracts that bind the layers

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

It contains no CLI builder, parser object, protocol type, renderer, console call, or daemon-specific semantics. `IRtCommand` is the CLI projection; the MCP adapter projects the same definition. During migration only, descriptors without complete ports enter an isolated compatibility bridge. Apply-capable bridge commands are not part of the stable MCP or daemon surface.

See [ADR-0003](adr/0003-shared-command-descriptors-and-application-ports.md), [ADR-0007](adr/0007-host-neutral-command-contracts.md), and the [migration playbook](command-application-port-migration.md).

### 2. Explicit request context

Every operation receives explicit, absolute request context:

- request ID and, for apply, caller-generated operation ID;
- repository root and solution path;
- configuration and SDK/target-framework selection;
- repository trust state and evaluator mode/policy;
- cancellation and resource budget; and
- source/revision identity where applicable.

Operations do not infer repository identity from process current directory and do not use global console, `Environment.ExitCode`, or arbitrary process environment as mutable request state. This is required for concurrent repositories, host parity, reproducibility, and future isolation.

### 3. Repository trust and workspace completeness

Loading an MSBuild solution can evaluate repository-controlled build logic and can involve analyzers or generators. A local checkout is not automatically trusted.

Workspace requests carry two independent values. Repository trust state is
**Trusted** after an explicit scoped decision or **Untrusted** when that decision is
absent or revoked. Evaluator mode is one of:

- **NoEvaluation** — only operations proven not to invoke repository-controlled code may run.
- **InProcess** — repository-controlled evaluation may run in the request host and is valid only for a trusted repository.
- **IsolatedWorker** — evaluation runs in a constrained worker even when the repository is trusted.

An untrusted repository can use `NoEvaluation` or `IsolatedWorker`, never
`InProcess`. A trusted repository may still request `IsolatedWorker`; choosing
isolation does not change or imply its trust state. The isolated worker receives an
OS-enforced allow-listed filesystem namespace containing only the read-only
repository, declared read-only SDK/package/tool inputs, and separate disposable
writable output, cache, and temporary locations outside the checkout. Host roots,
user profiles, credentials, SSH material, undeclared package caches, and devices are
not visible. The worker has bounded resources, no inherited secrets, and no network
unless explicitly enabled. Repository write attempts and undeclared host reads are
denied and recorded.

Long-lived CLI, MCP, and daemon hosts never evaluate an untrusted repository in-process.

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

### 4. Category and capability contracts

A command implements exactly one category marker:

- `IInspectCommand` — read-only, focused inspection;
- `IAnalyzeCommand` — read-only, broader analysis; or
- `IRefactorCommand` — prepares or applies source changes.

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

### 6. Mutation prepare/apply/recover contract

A boolean `dryRun` branch is not a mutation safety model. Stable mutations use:

```text
Prepare(request) -> PreparedChangeSet
Apply(repositoryId, planId, planHash, operationId, requestHash, expectedRevision, replayEpoch?) -> ApplyResult
GetOperation(repositoryId, operationId, requestHash) -> OperationStatus
Recover(repositoryId, operationId, action) -> RecoveryResult
```

The immutable prepared plan contains all path and project operations, expected revision and hashes, encoding/newline/file-mode policy, symlink/hard-link/root-containment policy, exact diff, diagnostics, assumptions, verification, expiry, and recovery data. Source/destination paths are repository-contained; staging, backups, and control artifacts use a user-private operation root outside and non-overlapping with the repository, on the target volume when required. Before prepare returns, its canonical bytes are atomically persisted outside repository control. Preview and apply consume that same repository-scoped, hash-verified plan across process restart. Once an intake receipt or operation ledger references a plan, garbage collection pins its exact bytes through every nonterminal state, including `Recovering`, through `RecoveryRequired`, and while a `CancelledNoEffect` control bundle awaits verified cleanup; it releases them only after verified terminal cleanup, durable expiry, or explicit recovery acknowledgement.

Apply creates a durable operation record, initializes its journal before staging, stages only in the outside-repository control root, acquires an exclusive canonical-repository transaction reservation, claims a renewable owner lease and monotonically increasing fencing epoch, validates every precondition, arbitrates cancellation against a complete journalled first-effect permit in one durable compare-and-swap, journals changes, revalidates the expected intermediate repository state at every side-effect boundary, applies deterministically, verifies postconditions, and records one truthful state. Interrupted pre-effect staging cleanup is idempotent and never touches the repository.

- `Received` or authoritative `NotRecorded` during dispatch resolution;
- `Applying`, `Recovering`, or bounded `Reconciling` while nonterminal;
- `Applied`;
- `RolledBack`;
- `RecoveryRequired` as a durable intervention state;
- `RecoveredApplied` or `RecoveredRolledBack` after verified manual recovery;
- `RecoveryAcknowledged` as an administrative terminal state that does not claim cleanliness;
- `CancelledNoEffect` as a terminal accepted cancellation with proof that no repository effect began;
- stale/conflict rejection; or
- expiry.

The ledger keeps an append-only transition history as well as the current state. An incomplete rollback is `RecoveryRequired`; it is never presented as clean failure or rollback. That state blocks a different apply for the repository until fenced recovery verifies `RecoveredApplied` or `RecoveredRolledBack`, or an authorized user explicitly records `RecoveryAcknowledged`. Acknowledgement performs no repository effect and records actor, reason, observed hashes, unresolved warnings, and the absence of a cleanliness guarantee.

An `Applying` owner that disappears cannot leave a permanent nonterminal record.
Startup and status lookup claim an expired lease exactly once, transition through
`Reconciling`, and use the durable plan, journal, backups, and observed hashes to
prove `Applied`, prove `RolledBack`, or require explicit recovery. An expired
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
after takeover, a resumed stale owner cannot pass the reservation/epoch check or
perform another write. Drift before the first effect returns stale rejection; drift
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
and cleanup status. If the effect permit wins, later cancellation is recorded but
verification and rollback remain non-interruptible. Exact replay returns the existing
cancellation result, and startup/status lookup performs idempotent control-artifact
cleanup outside the repository while retaining the plan until cleanup is verified.

The caller generates `OperationId` and the canonical request hash before dispatch.
Canonical repository ID plus operation ID is the composite key for every receipt,
ledger, status, recovery, acknowledgement, and retention lookup; the same operation
ID in another repository is independent.
Before accepted work enters an execution queue, a cross-process, store-enforced
intake fence keyed by repository and operation ID protects a durable `Received`
receipt containing the canonical request. Startup and status
lookup reconcile any receipt not yet promoted into the operation ledger. Repeating
the same operation ID and request returns the existing receipt/state/result; reusing
it for different work returns conflict.

### 7. Transport contract

Transport outcome describes only what the caller can prove:

- **NotDispatched** — no request bytes were accepted; policy may permit fallback or retry.
- **Completed** — a valid operation response was received.
- **Indeterminate** — dispatch may have occurred but no valid response is available.

An indeterminate mutation is never automatically retried or executed in-process. The
host already knows the repository/operation identity and queries status. If no receipt or operation
exists, status lookup uses the intake fence to exclude a still-arriving request and
durably records `NotRecorded` with a replay epoch. Only then may the caller explicitly
resubmit the identical request with the same operation ID, hash, and epoch; a delayed
original is fenced out.

For the current hardening phase, the daemon is an optional transparent typed transport adapter under [ADR-0008](adr/0008-daemon-transparent-passthrough-phase.md). It has no workspace cache, operation authority, CLI argument bridge for mutations, process-global request context, or raw payload logging. It must preserve the same typed observable result as direct execution.

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
| `prime`/agent primer | Generated from stable catalogue entries |
| Generated command documentation | Catalogue projection, verified rather than rewritten by normal build |

If two surfaces describe the same fact, the catalogue wins and CI rejects the drift.

## MCP boundary

The preferred MCP implementation is the official C# SDK. A handwritten protocol implementation is acceptable only with equivalent spawned-process conformance evidence for every advertised protocol version.

Stable MCP behaviour includes:

- correct request, response, notification, and parse-error handling;
- explicit supported-version negotiation or selection;
- cancellation and progress for long work;
- complete JSON Schema, including enums, bounds, nullability, nesting, and additional-property policy;
- schema-valid structured results;
- bounded request/output/item/time/concurrency/memory behaviour;
- deterministic pagination or projection; and
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
- **Legacy daemon routing remains a safety boundary.** Typed recovery improved migrated commands, but legacy argument-array fallback is not acceptable for mutations and must be removed first.
- **The staged prepared writer is still process-local.** It now preflights and stages supported writes/moves/deletes, backs up originals, suppresses cancellation during commit, and attempts compensating rollback. It does not yet provide a durable plan store, complete path/metadata coverage, a durable journal and status ledger, handle-safe symlink/reparse containment, restart reconciliation, or recovery commands.
- **Workspace loading does not yet expose completeness or trust.** Existing warning suppression and partial-load tolerance cannot support proof-strength analyses until the new contract lands.
- **MCP is experimental until conformance gates pass.** The current handwritten server and text-only result model are transitional.
- **Host projects remain physically coupled.** `RoslynTools.Mcp` still references the CLI project while descriptor/schema and compatibility concerns live there. Full port coverage allows both hosts to depend on the application/core assemblies without an executable-project reference.
- **Capability flags remain partly ad hoc.** Continue the central-catalogue migration; do not create more command-local known options.
- **Output and suggestion fan-out remain incomplete.** Follow-ons, schemas, provenance, confidence, and limitations must be uniform before stable admission.
- **Build/release claims exceed current evidence.** Debug builds currently have side-effecting targets, version metadata claims `1.0.0`, and distribution verification is not yet native end-to-end. Treat release hardening as product correctness.
- **A real workspace daemon is parked.** Reconsider it only after trust, mutation, protocol, and canonical-operation gates pass and measured cold-start cost justifies the added state model.

## Updating this document

When a decision changes a layer contract, trust boundary, mutation state, output doctrine, enforced invariant, compatibility promise, or accepted non-obvious trade-off:

1. Write or supersede an ADR under [adr/](adr/). Accepted ADR history is not silently rewritten beyond status/link maintenance.
2. Update this overview to reflect the resulting architecture, including current migration tension where implementation lags.
3. Update the most specific companion document and OpenSpec requirement/task where applicable.
4. Add or update executable architecture/acceptance tests for the decision.
5. Commit the decision, overview, specific document, and tests as one coherent change where practical.

Architecture documents distinguish **accepted target**, **implemented evidence**, and **remaining migration**. A target statement is never presented as a runtime guarantee until its gate passes.
