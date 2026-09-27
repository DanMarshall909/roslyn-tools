# ADR-0039: Resume terminal no-effect cleanup under a local successor reservation

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123), [#140](https://github.com/DanMarshall909/roslyn-tools/issues/140)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

The internal no-effect cleanup service required the reservation token that
admitted every staging and backup artifact. After a terminal cancellation or
guard rejection, a process restart could acquire a new exclusive reservation
but could not finish physical cleanup or release the plan pin. Artifact
removal before its ledger event was already retryable under the original
reservation; the reservation check prevented that retry after restart.

## Decision

A later local reservation may resume cleanup only when the operation has a
terminal no-effect state, no effect journal, and a closed and drained
pre-effect activity fence. The ledger requires every staging and backup event
and cleanup-inventory artifact to come from one earlier reservation. The new
reservation must have a greater durable fence epoch, a different token, and
belong to the same configured protected local reservation root, identified
from its held reservation file. The original
reservation remains valid while held. A foreign root or mixed original
provenance cannot authorize cleanup.

Under the fresh exclusive reservation, the existing handle-relative cleanup
boundary retries admitted removals. A missing artifact can be recorded as
removed only after the trusted root and operation path are checked. The
service verifies empty operation roots before recording `CleanupVerified`.
Before releasing a pin, including a replay after `CleanupVerified`, it scans
the roots again and confirms the durable cleanup record. An unknown artifact,
unsafe root, active guard admission, or failed durability check retains the
inventory and pin for another explicit attempt.

## Limits and follow-up

This is a protected local, same-principal no-effect capability. Reconstructed
ledger, store, and reservation-provider instances exercise the restart path;
the full process-termination matrix remains #146 work. The local root check
does not prove cross-principal or cross-host shared authority; #139 owns that
boundary. An uncertain guard release or first-effect handoff remains unsafe
to reclaim, and stale-plan classification still needs #140 work. The
production effect broker remains disabled, and public Apply is unavailable.
