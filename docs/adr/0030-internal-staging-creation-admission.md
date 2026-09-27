# ADR-0030: Internal staging creation admission

- Status: Accepted
- Date: 2026-09-27
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123), [#140](https://github.com/DanMarshall909/roslyn-tools/issues/140)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

ADR-0029 records an exact staging intent before artifact creation, but a broad
pre-effect activity admission alone cannot decide whether a worker paused after
admission may begin a new artifact after cancellation closes the generation.
The artifact action and no-effect closure need a durable order.

## Decision

The internal staging journal now orders `Intent`, `CreationAdmitted`, and
`Verified` for each replacement effect. With a held repository reservation and
an open, matching pre-effect admission, a worker appends `CreationAdmitted`
under the operation ledger lock. The event carries a stable creation token and
the artifact path, expected content hash, admission, generation, and
reservation fence inherited from the intent, while the operation identity
binds it to the exact plan hash. An uncertain directory flush can be retried
without issuing a second token. Cancellation uses the same ledger lock and
closes new creation admission.

An admitted action may record its matching verification after cancellation,
using its original token while its broad activity remains active. An unresolved
admitted action prevents that activity from draining; the repository mutation
barrier therefore remains until its outcome is resolved. Restart validation
rejects missing, reordered, mismatched, or orphaned admission events. The
first repository effect remains blocked by any unverified staging action.

## Limits and follow-up

This is a ledger admission contract, not an OS-enforced artifact permit. No
production control-root broker or staged writer consumes the token, and the
ledger does not inspect a physical artifact; its verification input is still
a trusted broker observation contract. The token must be one-use in a future
non-bypassable broker at the actual filesystem boundary. A separate protected
principal/service and parent authority are required before the broker can
exclude same-principal editor processes from control artifacts. #140 still
needs protected physical staging, backup and guard actions, durable inventory,
fault injection at their boundaries, cleanup and restart reconciliation. The
provisional journal event bound must be reserved at intake before production
staging can rely on it. No public Apply is enabled.
