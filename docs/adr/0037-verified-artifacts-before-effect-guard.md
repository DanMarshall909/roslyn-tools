# ADR-0037: Verify plan artifacts before acquiring an effect guard

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123), [#140](https://github.com/DanMarshall909/roslyn-tools/issues/140)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

The internal staging and original-backup writers could durably create and verify
exact-plan artifacts, but the internal apply authority did not invoke them. Its
effect broker could be acquired while both journals were empty. The atomic
first-effect handoff rejected a pending artifact but did not require every
planned artifact to have been verified.

## Decision

The internal apply authority receives trusted staging and backup boundaries.
After loading the exact pinned plan and taking the repository reservation, it
opens one pre-effect activity and uses the journaled writers to prepare every
replacement and original artifact in plan effect order. Physical creation,
durability, and hash verification precede effect-broker acquisition. Missing
artifact authority fails closed. The unavailable production effect broker is
rejected before creating control artifacts.

The ledger's first-effect handoff requires one ordered `Verified` staging event
for every replacement and one ordered `Verified` backup event for every original.
Each verified event must belong to the current pre-effect admission, control
generation, and reservation fence. Empty, partial, or earlier-activity evidence
cannot authorize the first effect. A failed artifact creation after durable
admission retains its active pre-effect fence and blocks broker acquisition.
After preparation, the authority rereads the durable fence and drains a
cancellation that is already visible. It passes the activity token to the
trusted effect broker; that broker must check generation revocation at the
actual guard-acquisition boundary.

## Limits and follow-up

This is an internal Linux x64 artifact path and simulated effect-broker
integration. The backup bytes come from the immutable plan; a later physical
broker must prove the live original under complete guards before any effect.
The ledger checks durable verification evidence at handoff; the eventual broker
must protect or recheck physical artifact bytes at its actual effect boundary.
The internal reread alone cannot close a concurrent cancellation race; the
production broker's revocable guard authority remains to be proved in #138.

A guard/preflight rejection after artifact verification still retains the
operation's artifacts and plan pin. Terminal no-effect resolution, same-reservation
cleanup routing, and cross-owner restart reconciliation are follow-up work in
#140. Successful effects also need the downstream verified rollback, recovery,
and cleanup lifecycle. The production effect broker remains unavailable and no
public Apply is enabled.
