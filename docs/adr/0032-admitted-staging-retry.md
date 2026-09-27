# ADR-0032: Retry an admitted staging artifact under its held reservation

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123), [#140](https://github.com/DanMarshall909/roslyn-tools/issues/140)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

An interruption after `CreationAdmitted` can leave an artifact on disk without
`Verified` in the durable ledger. Retrying an exclusive create then fails with
`EEXIST`, leaving the original pre-effect activity unresolved even while its
reservation is still held. A fresh action must not silently adopt a file that
was already present before its creation admission.

## Decision

The internal writer first reads the protected ledger. When the last staging
event is the exact effect's `CreationAdmitted`, it confirms that event under the
ledger lock with the original active admission and held reservation. This
confirmation reflushes ledger metadata and may occur after no-effect
cancellation closed new admission. It does not issue another creation token.

Only this confirmed retry may open an existing artifact. The Linux boundary
opens it relative to held directory handles without following links and
uses a nonblocking open so a FIFO cannot stall type validation. It requires
a private, singly linked regular file owned by the service user, the
expected byte length, and the reviewed content hash. It flushes the file and
its directory before recording verification. If the admitted file is absent,
the still-held reservation and original creation token permit its first
creation. A fresh admission refuses `EEXIST`, even when the existing bytes
match. Changed, short, linked, or symlinked files remain unresolved and are
never overwritten by this retry.

## Limits and follow-up

This is a same-reservation retry contract. A new reservation cannot take over
the old activity or use its creation token. Process restart after loss of the
original reservation requires a separate recovery authority that proves the
former writer and its write capabilities quiescent, reconciles the physical
artifact, and records a new fenced observation. This slice does not clean up a
bad artifact, reconcile a verified artifact on replay, or enable public Apply.
The production repository effect broker remains unavailable.
