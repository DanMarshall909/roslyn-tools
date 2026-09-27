# ADR-0028: Internal pre-effect activity fence

- Status: Accepted
- Date: 2026-09-27
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123), [#140](https://github.com/DanMarshall909/roslyn-tools/issues/140)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

ADR-0027 journals simulated repository effects but has no durable boundary for
control work before the first effect. Cancellation could otherwise report
`CancelledNoEffect` while an earlier staging or guard-acquisition action remains
in flight, or the apply owner could begin an effect before that action drains.

## Decision

The protected operation ledger stores an operation-scoped control generation,
closed/open state, and bounded active admission set. Under its cross-process
mutation lock, a held repository reservation admits one pre-effect activity and
records its token, generation, and reservation fence. The same reservation must
durably complete that admission. A forged or reused completion cannot drain it.
An abandoned admission remains visible and blocks later mutation until explicit
future reconciliation proves it safe.

Cancellation before the first effect and the first effect intent compete under
that same ledger lock. Cancellation atomically closes new pre-effect admission,
advances the control generation, and records `CancelledNoEffect` while allowing
already admitted activity to drain. The first effect intent requires no active
admissions and closes the fence in the same durable update. An intent that wins
prevents a later no-effect cancellation. The internal apply authority admits and
drains broker acquisition and final preflight before claiming the effect owner;
if cancellation wins during preflight it returns the retained cancellation record
without a repository effect. A terminal record whose directory durability was
uncertain must be flushed successfully before a later mutation is admitted.
The ledger also retains whether control activity was ever admitted. A visible
admission removal whose directory flush failed must be re-flushed, including
when a later attempt targets the same operation, before mutation is admitted.
If Apply observes a cancellation that won after its last durable write, it
confirms the cancelled record's directory durability before returning success.

Existing version-1 records without this optional fence field remain readable.
Records already beyond `Prepared` are treated as closed, so they cannot gain new
pre-effect activity through an absent legacy field.

## Limits and follow-up

This is an internal admission and cancellation foundation. The production effect
broker still fails closed. There is no staging/backup artifact journal or verified
cleanup yet; #140 remains open. The broker interface does not yet admit each
individual control-root mutation and guard action through a non-bypassable
generation check. Public cancellation, action authorization, restart quiescence,
and recovery remain under #123. An active admission left by a dead owner is
conservatively blocking, not proof that work is still running or safe to clean.
