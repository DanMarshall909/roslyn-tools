# ADR-0012: Review-first mutation defaults during hardening

- Status: Accepted
- Date: 2026-08-30
- Temporarily narrows: the apply-by-default option described by [ADR-0007](0007-host-neutral-command-contracts.md)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

The current mutation path does not yet have the complete durable plan store, operation
ledger, restart reconciliation, external-writer protection, and recovery kernel
required by the critical-platform-hardening P0 gate. Preserving legacy apply-by-default
behavior while converting commands to canonical ports would make the new architecture
easier to invoke without first making its writes safe.

Review and dry-run are distinct: review analyzes without preparing a transformation;
dry-run prepares the exact change without writing. Not every command has a meaningful
review-only mode, but every mutation can avoid an implicit write.

## Decision

During the current hardening phase, every apply-capable command defaults to `Review`
when it has a review-only operation, otherwise to `DryRun`. `Apply` requires an
explicit caller selection and remains in-process until its separately approved safety
and transport gates pass. The shared application definition and policy own this
default; CLI, MCP, daemon, and migration adapters cannot weaken it independently.

Command conversion work must not preserve an apply-by-default legacy behavior. A
machine-readable manifest value of `apply-default` describes as-built containment debt,
not a target: a conversion that touches that command changes both runtime metadata and
the manifest to `review-default` in the same verified slice. The default path must be
tested to perform no repository write.

Apply-by-default may return only through a later approved OpenSpec and ADR after the
durable plan, ledger, recovery, failure-injection, and host-parity gates demonstrate
that an implicit apply is an accepted product policy. Completing an application-port
migration alone is not that evidence.

The recovery gate includes revocable effect admission, not only a ledger fencing
number. A mutation worker does not hold bypassable repository-write authority: each
one-use journalled intent is admitted by an OS-enforced broker/fence at the actual
conditional filesystem operation. Effect admission and fence closure are linearizable.
If direct write handles exist, an expired owner's epoch cannot advance until verified
process termination closes them; otherwise the operation remains nonterminal
`OwnerQuiescencePending` behind an intervention barrier and cannot be acknowledged as
`RecoveryRequired`.

## Consequences

- Migration groups M0 through M4 cannot land a new implicit-write default.
- Existing `apply-default` manifest rows remain visible as containment debt until their
  runtime definitions are changed and verified.
- Automation can still request apply explicitly, but omission is non-mutating.
- Explicit apply cannot pass the safety gate with ledger-only fencing or an unverified
  stale writer.
- Tests distinguish review, dry-run, explicit apply, and no-op and assert that the
  default path leaves the repository byte-identical.

## Alternatives considered

- Preserve legacy defaults during migration: rejected because it carries an unsafe
  behavior into the canonical surface before the mutation kernel exists.
- Make every default `Review`: rejected because some transformations have no useful
  analysis-only result; `DryRun` is the non-writing fallback.
- Remove apply entirely: rejected because explicit apply remains useful for controlled
  workflows and is separately gated by execution and recovery policy.

## References

- Related ADRs: [ADR-0007](0007-host-neutral-command-contracts.md), [ADR-0008](0008-daemon-transparent-passthrough-phase.md)
- Related docs: [architecture overview](../architecture-overview.md), [command migration playbook](../command-application-port-migration.md)
