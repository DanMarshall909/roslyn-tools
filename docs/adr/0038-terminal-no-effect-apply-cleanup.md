# ADR-0038: Resolve no-effect apply attempts before artifact cleanup

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123), [#140](https://github.com/DanMarshall909/roslyn-tools/issues/140)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

The internal apply authority now verifies plan artifacts before asking for an
effect guard. A broker or guarded preflight can still decline without admitting
any repository effect. Previously the authority released the guard and drained
its pre-effect activity but left the operation `Prepared`, with artifacts and a
plan pin and no cleanup inventory. A cancellation after staging had a durable
`CancelledNoEffect` outcome but did not route through the available journaled
physical cleanup service.

## Decision

After a failed broker acquisition or guarded preflight, the authority first
releases any session. One ledger replacement then drains its pre-effect activity
and, if still `Prepared`, records `RejectedUnsafeCoordination` with an inventory
of all admitted staging and backup artifacts. Cancellation may win before that
replacement; the ledger then drains the activity while retaining the durable
`CancelledNoEffect` outcome and its inventory. Cancellation after the atomic
rejection cannot replace the terminal state. No effect intent exists on either
path.

When a trusted `JournaledNoEffectCleanup` capability is configured, the
authority invokes it under the same reservation after the terminal no-effect
record is durable. The cleanup service verifies physical removals and empty
operation roots, records `CleanupVerified`, and only then releases the plan pin.
If the guard cannot be released, the atomic resolution cannot be persisted, or
physical cleanup fails, the authority leaves the pin and remaining evidence in
place. A failed cleanup reports `CleanupRequired` while
the durable terminal outcome remains queryable.

## Limits and follow-up

This is an internal Linux x64 integration exercised with a simulated effect
broker. A host that does not configure cleanup retains the terminal inventory
and pin for explicit cleanup. The default production broker remains unavailable;
no public Apply is enabled. Cross-owner restart reconciliation and cleanup after
uncertain guard release or first-effect handoff remain #140 follow-up. This
decision does not prove the live repository original, a physical effect guard,
or effectful rollback and recovery.
