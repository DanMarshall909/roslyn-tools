# Architecture Decision Records

This directory holds the Architecture Decision Records (ADRs) for `rt`. Each ADR captures a single architectural decision: what was chosen, why, what alternatives were rejected, and what the consequences are. The architecture overview at [../architecture-overview.md](../architecture-overview.md) is the running synthesis; ADRs are the historical log.

Current pre-release work is tracked by the [product-spine OpenSpec](../../openspec/changes/shape-pre-release-product-spine/proposal.md) and [RT-016](../issues/open/architecture/rt-016-pre-release-product-spine.md).

## When to write an ADR

Write an ADR when a decision:

- introduces or removes a layer contract (e.g. a new capability interface, a renamed registry pattern)
- changes the error-handling doctrine (e.g. typed ladder, Result-vs-throw boundary)
- changes the output pipeline (e.g. renderer collapse, encoder selection, DTO unification)
- adopts or retires a doctrine (e.g. "static classes are migration targets", "rt-first gate on Read/Edit")
- accepts a non-obvious trade-off worth justifying for future readers (e.g. byte-identity gate blocking a refactor)

If the decision only affects a single file or is purely tactical, don't bother — the commit message is enough.

## Format

Use the template below. Files are `NNNN-kebab-case-title.md`, numbered sequentially from `0001`. Once a record is `Accepted`, do not edit it — supersede with a new record.

```markdown
# ADR-NNNN: <title>

- Status: Proposed | Accepted | Deprecated | Superseded by ADR-MMMM
- Date: YYYY-MM-DD (use the system-provided currentDate verbatim)
- Stories: US-NNN, US-MMM (cross-link the work that drove the decision)

## Context

What forced this decision? What was the existing state? What problem appeared?

## Decision

What did we decide? State it plainly — one or two sentences. Then the mechanics:
how the layer, contract, or pipeline works after this decision.

## Consequences

- What gets easier?
- What gets harder?
- What invariants now hold (and where are they enforced — startup check, arch test, code review)?
- What's deliberately deferred (cross-link follow-up stories)?

## Alternatives considered

For each, one line: what it was, why it was rejected.

## References

- Commits: <SHA list>
- Related ADRs: <links>
- Related docs: <links>
```

## When updating after a decision

Per the architecture-overview doc's "Updating this doc" section:

1. Write the ADR here.
2. Update [../architecture-overview.md](../architecture-overview.md) to reflect the new state and cross-link the ADR.
3. Update the most specific companion doc if one applies.
4. Commit all three together as `docs(adr): NNNN <title>`.

## Index

| # | Title | Status | Date |
|---|---|---|---|
| [0001](0001-core-service-thin-host-adapters.md) | Core service + thin host adapters | Accepted | 2026-06-17 |
| [0002](0002-suggestions-output-contract-and-proactive-session-start.md) | _suggestions output contract and proactive session start | Accepted | 2026-06-19 |
| [0003](0003-shared-command-descriptors-and-application-ports.md) | Shared command descriptors and application ports | Accepted | 2026-07-30 |
| [0004](0004-portable-mcp-distribution.md) | Portable MCP distribution | Accepted | 2026-08-16 |
| [0005](0005-daemon-centred-local-execution.md) | Daemon-centred local execution | Superseded in part by [0008](0008-daemon-transparent-passthrough-phase.md) | 2026-08-16 |
| [0006](0006-mcp-lazy-daemon-lifetime.md) | MCP lazy daemon lifetime | Superseded in part by [0008](0008-daemon-transparent-passthrough-phase.md) | 2026-08-26 |
| [0007](0007-host-neutral-command-contracts.md) | Host-neutral command contracts and complete application-port migration | Accepted; daemon boundary updated by [0008](0008-daemon-transparent-passthrough-phase.md) | 2026-08-26 |
| [0008](0008-daemon-transparent-passthrough-phase.md) | Transparent daemon passthrough phase | Superseded in part by [0009](0009-authenticated-daemon-endpoints.md), [0013](0013-fenced-plan-unavailability-resolution.md), [0015](0015-operation-lifecycle-authorization.md), and [0017](0017-bounded-read-only-workspace-reuse.md) | 2026-08-27 |
| [0009](0009-authenticated-daemon-endpoints.md) | Authenticated local daemon endpoints | Accepted | 2026-08-30 |
| [0010](0010-revision-bound-continuation-tokens.md) | Revision-bound continuation tokens | Accepted | 2026-08-30 |
| [0011](0011-authoritative-trust-and-evaluator-isolation.md) | Authoritative repository trust and evaluator isolation | Accepted | 2026-08-30 |
| [0012](0012-review-first-mutation-defaults.md) | Review-first mutation defaults during hardening | Accepted; trust category superseded by [0103](0103-separate-local-trust-control.md) | 2026-08-30 |
| [0013](0013-fenced-plan-unavailability-resolution.md) | Fenced plan-unavailability resolution | Accepted | 2026-08-30 |
| [0014](0014-bounded-recovery-attempt-history.md) | Bounded recovery-attempt history | Accepted | 2026-08-31 |
| [0015](0015-operation-lifecycle-authorization.md) | Operation-lifecycle authorization | Accepted | 2026-08-31 |
| [0016](0016-ci-rule-enforcement.md) | CI rule enforcement | Accepted | 2026-09-09 |
| [0017](0017-bounded-read-only-workspace-reuse.md) | Bounded read-only workspace reuse | Accepted | 2026-09-16 |
| [0018](0018-bounded-external-semantic-review.md) | Bounded external semantic review | Accepted | 2026-09-18 |
| [0019](0019-parse-integrity-gates-absence-claims.md) | Parse integrity gates absence claims | Accepted | 2026-09-04 |
| [0020](0020-layered-host-configuration.md) | Layered host configuration | Accepted | 2026-09-23 |
| [0021](0021-authoritative-workspace-acquisition-evidence.md) | Authoritative workspace acquisition evidence | Accepted | 2026-09-23 |
| [0022](0022-canonical-immutable-change-plan-payload.md) | Canonical immutable change-plan payload | Accepted | 2026-09-23 |
| [0023](0023-protected-durable-change-plan-storage.md) | Protected durable change-plan storage | Accepted | 2026-09-23 |
| [0024](0024-durable-operation-intake-ledger.md) | Durable operation intake and lifecycle ledger | Accepted | 2026-09-25 |
| [0025](0025-restart-safe-dispatch-intake-handoff.md) | Restart-safe dispatch and intake handoff | Accepted | 2026-09-25 |
| [0026](0026-public-no-effect-durable-prepare.md) | Public no-effect durable Prepare boundary | Superseded by [0082](0082-maintainer-only-durable-prepare.md) | 2026-09-26 |
| [0027](0027-internal-journaled-apply-barrier.md) | Internal journaled apply barrier | Accepted | 2026-09-27 |
| [0028](0028-internal-pre-effect-activity-fence.md) | Internal pre-effect activity fence | Accepted | 2026-09-27 |
| [0029](0029-internal-staging-intent-journal.md) | Internal staging intent journal | Accepted | 2026-09-27 |
| [0030](0030-internal-staging-creation-admission.md) | Internal staging creation admission | Accepted | 2026-09-27 |
| [0031](0031-handle-relative-physical-staging.md) | Handle-relative physical staging artifact | Accepted | 2026-09-28 |
| [0032](0032-admitted-staging-retry.md) | Admitted staging retry under a held reservation | Accepted | 2026-09-28 |
| [0033](0033-no-effect-cleanup-inventory.md) | No-effect cleanup inventory at the terminal transition | Accepted | 2026-09-28 |
| [0034](0034-journaled-no-effect-staging-cleanup.md) | Journaled same-reservation no-effect staging cleanup | Accepted | 2026-09-28 |
| [0035](0035-journaled-original-backup-and-cleanup.md) | Journaled original backup and no-effect cleanup | Accepted | 2026-09-28 |
| [0036](0036-atomic-pre-effect-guard-handoff.md) | Atomic pre-effect guard handoff | Accepted | 2026-09-28 |
| [0037](0037-verified-artifacts-before-effect-guard.md) | Verified plan artifacts before effect guard | Accepted | 2026-09-28 |
| [0038](0038-terminal-no-effect-apply-cleanup.md) | Terminal no-effect apply cleanup | Accepted | 2026-09-28 |
| [0039](0039-local-restart-no-effect-cleanup.md) | Local restart no-effect cleanup | Accepted | 2026-09-28 |
| [0040](0040-durable-pre-effect-guard-history.md) | Durable pre-effect guard history | Accepted | 2026-09-28 |
| [0041](0041-local-interrupted-pre-effect-reconciliation.md) | Local interrupted pre-effect reconciliation | Accepted | 2026-09-28 |
| [0042](0042-cli-command-surface-ownership.md) | CLI command surface belongs to the CLI assembly | Accepted | 2026-09-28 |
| [0043](0043-mcp-port-only-dispatch.md) | MCP dispatch requires an application port | Accepted | 2026-09-28 |
| [0044](0044-typed-application-catalogue-for-mcp.md) | Typed application catalogue powers MCP | Accepted | 2026-09-28 |
| [0045](0045-shared-msbuild-instance-identity.md) | Shared MSBuild instance identity for host adapters | Accepted | 2026-09-28 |
| [0046](0046-shared-msbuild-host-bootstrap.md) | Shared MSBuild host bootstrap | Accepted | 2026-09-28 |
| [0047](0047-shared-doctor-host-services.md) | Shared doctor host services | Accepted | 2026-09-28 |
| [0048](0048-shared-host-foundation-registration.md) | Shared host foundation registration | Accepted | 2026-09-28 |
| [0049](0049-shared-application-infrastructure-registration.md) | Shared application infrastructure registration | Accepted | 2026-09-28 |
| [0050](0050-shared-application-composition.md) | Shared application catalogue and Core operation registration | Accepted | 2026-09-28 |
| [0051](0051-shared-host-command-metadata.md) | Shared metadata for host-only suggestions | Accepted | 2026-09-28 |
| [0052](0052-typed-source-suggestion-catalogue.md) | Typed-source suggestion catalogue | Accepted | 2026-09-28 |
| [0053](0053-mcp-owned-host-composition.md) | MCP-owned host composition | Accepted | 2026-09-28 |
| [0054](0054-cli-typed-daemon-dispatch-only.md) | CLI application commands enter daemon through typed ports | Accepted | 2026-09-28 |
| [0055](0055-daemon-legacy-control-only.md) | Daemon legacy envelope is control only | Accepted | 2026-09-28 |
| [0056](0056-daemon-owned-host-composition.md) | Daemon-owned host composition | Accepted | 2026-09-28 |
| [0057](0057-host-dependency-and-contract-ratchets.md) | Host dependency and typed-contract ratchets | Accepted; transport placement completed by [0058](0058-daemon-transport-hosting-ownership.md) | 2026-09-28 |
| [0058](0058-daemon-transport-hosting-ownership.md) | Daemon transport belongs to shared Hosting | Accepted | 2026-09-28 |
| [0059](0059-host-configuration-hosting-ownership.md) | Host configuration belongs to shared Hosting | Accepted | 2026-09-28 |
| [0060](0060-service-diagnostics-without-ambient-console.md) | Service diagnostics use injected logging and typed validation | Accepted | 2026-09-28 |
| [0061](0061-host-owned-console-logger.md) | Console logger belongs to host composition | Accepted | 2026-09-28 |
| [0062](0062-legacy-move-preview-result.md) | Legacy move preview returns data to its caller | Superseded by ADR-0085 | 2026-09-29 |
| [0063](0063-cli-owned-process-output.md) | CLI process output implementations belong to CLI | Accepted | 2026-09-29 |
| [0064](0064-service-exit-status-is-host-owned.md) | Service outcomes do not set process exit status | Accepted | 2026-09-29 |
| [0065](0065-cli-owned-format-renderers.md) | Format renderers belong to the CLI adapter | Accepted | 2026-09-29 |
| [0066](0066-workspace-cache-diagnostics-through-host.md) | Workspace cache diagnostics flow through host composition | Accepted | 2026-09-29 |
| [0067](0067-pure-diagnostic-template-formatting.md) | Diagnostic template formatting is independent of the console bridge | Accepted | 2026-09-29 |
| [0068](0068-workspace-load-diagnostics-via-host-logging.md) | Workspace load diagnostics use host logging and warning policy | Accepted | 2026-09-29 |
| [0069](0069-explicit-symbol-suggestion-policy.md) | Symbol lookup receives suggestion policy per request | Accepted | 2026-09-29 |
| [0070](0070-host-owned-output-context.md) | Ambient output context belongs to shared hosting | Accepted | 2026-09-29 |
| [0071](0071-extraction-diagnostics-through-logger.md) | Duplicate extraction diagnostics use an explicit logger | Accepted | 2026-09-29 |
| [0072](0072-solution-writer-report-sink.md) | Solution writers report through a host-owned sink | Accepted | 2026-09-29 |
| [0073](0073-host-owned-ambient-console.md) | Ambient console and logging bridges belong to shared hosting | Accepted | 2026-09-29 |
| [0074](0074-mcp-solution-report-boundary.md) | MCP solution write reports stay out of the protocol stream | Accepted | 2026-09-29 |
| [0075](0075-cli-owned-github-issue-process-adapter.md) | The GitHub issue process adapter belongs to CLI | Accepted | 2026-09-29 |
| [0076](0076-host-owned-semantic-diff-git-client.md) | Semantic diff obtains git clients from shared hosting | Accepted | 2026-09-29 |
| [0077](0077-reusable-host-source-ratchet.md) | Reusable source rejects host adapters and service process clients | Accepted | 2026-09-29 |
| [0078](0078-change-signature-operation-discriminator.md) | One typed change-signature operation across CLI and MCP | Accepted | 2026-09-29 |
| [0079](0079-third-party-adapter-boundary.md) | Third-party adapters stay outside reusable layers | Accepted | 2026-09-29 |
| [0080](0080-official-mcp-sdk-transport.md) | Official MCP SDK owns the stdio protocol lifecycle | Accepted | 2026-09-29 |
| [0081](0081-bounded-cli-workspace-evidence.md) | Bound workspace evidence in CLI inspection results | Accepted | 2026-09-30 |
| [0082](0082-maintainer-only-durable-prepare.md) | Keep durable Prepare on the maintainer surface | Accepted | 2026-10-01 |
| [0083](0083-honest-unsupported-plan-suggestions.md) | Return honest unsupported outcomes for unavailable plan tasks | Accepted | 2026-10-01 |
| [0084](0084-pre-stage-containment-for-prepared-writes.md) | Check prepared mutation paths before staging | Accepted | 2026-10-01 |
| [0085](0085-retire-legacy-direct-write-move-paths.md) | Retire legacy direct-write move paths | Accepted | 2026-10-01 |
| [0086](0086-fail-closed-on-git-change-acquisition.md) | Fail closed on Git change acquisition | Accepted | 2026-10-01 |
| [0087](0087-prove-git-content-absence-before-semantic-diff.md) | Prove Git content absence before semantic diff | Accepted | 2026-10-01 |
| [0088](0088-bound-working-tree-content-reads.md) | Bound working-tree content reads | Accepted | 2026-10-01 |
| [0089](0089-honest-working-tree-read-platform-support.md) | Report unsupported working-tree read platforms | Accepted | 2026-10-01 |
| [0090](0090-route-working-tree-review-with-platform-admission.md) | Route working-tree review with platform admission | Accepted | 2026-10-01 |
| [0091](0091-exact-baseline-semantic-diff.md) | Discover semantic-diff paths from the actual baseline | Accepted | 2026-10-01 |
| [0092](0092-route-unambiguous-working-tree-baselines.md) | Route unambiguous working-tree baselines through suggestion | Accepted | 2026-10-01 |
| [0093](0093-remove-implicit-debug-build-writes.md) | Remove implicit source and profile writes from Debug builds | Accepted | 2026-10-01 |
| [0094](0094-verify-normal-build-purity.md) | Verify normal restore, build, and test purity | Accepted | 2026-10-01 |
| [0095](0095-bound-shared-symbol-ambiguity.md) | Bound shared symbol ambiguity responses | Accepted | 2026-10-01 |
| [0096](0096-bound-legacy-symbol-ambiguity.md) | Bound legacy symbol ambiguity errors | Accepted | 2026-10-01 |
| [0097](0097-bound-member-overload-ambiguity.md) | Bound member overload ambiguity errors | Accepted | 2026-10-01 |
| [0098](0098-scope-complexity-top-rollups.md) | Scope complexity rollups to top methods | Accepted | 2026-10-01 |
| [0099](0099-reject-unsupported-cli-output-formats.md) | Reject unsupported explicit CLI output formats | Accepted | 2026-10-01 |
| [0100](0100-refuse-uncontained-mcp-semantic-diff-files.md) | Refuse uncontained MCP semantic-diff file reads | Accepted | 2026-10-01 |
| [0101](0101-doctor-mode-discovery-metadata.md) | Describe doctor modes in one product contract | Accepted | 2026-10-02 |
| [0102](0102-config-host-discovery-profile.md) | Publish config as a host-only product | Accepted | 2026-10-02 |
| [0103](0103-separate-local-trust-control.md) | Separate local trust control from repository refactoring | Accepted | 2026-10-02 |
