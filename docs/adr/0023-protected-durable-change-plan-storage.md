# ADR-0023: Protected durable change-plan storage

- Status: Accepted
- Date: 2026-09-23
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

[ADR-0022](0022-canonical-immutable-change-plan-payload.md) defines the exact
`rt.change-plan.v1` bytes and hash, but an in-memory envelope cannot carry a review
across CLI, MCP, or daemon process exit. Apply and recovery need to load the same
reviewed bytes by repository ID, plan ID, and plan hash. A repository-controlled file
must not be able to replace those bytes, and ordinary retention must not remove them
while dispatch or an accepted operation still depends on them.

The store also needs typed fail-closed outcomes. Missing, expired, malformed,
schema-incompatible, hash-mismatched, or weakly protected state cannot be treated as a
cache miss followed by plan recomputation.

## Decision

Core rehydrates persisted envelopes through `CanonicalChangePlanReader`. The reader
constructs the immutable domain model for every v1 field and effect shape, reserializes
the payload, compares its exact bytes, verifies the claimed SHA-256 digest in constant
time, and finally compares the complete canonical envelope bytes. Alternate JSON
formatting, unknown or omitted semantic content, reordered properties, incompatible
wire tokens, and changed bytes therefore fail verification rather than acquiring the
reviewed plan identity.

`DurableChangePlanStore` is the principal-private storage engine for
`ExclusivePrincipal`. Every operation must hold a trusted host-issued protection lease
that proves the store root is owned by the authenticated principal, is physically
outside and non-overlapping with the repository (including mount aliases), and keeps
that physical binding stable through the operation. The authority safely resolves or
provisions the root and returns its lease before the engine creates directories,
changes permissions, or writes metadata. The built-in unavailable authority fails
without touching the proposed store path; repository input cannot select or implement
the authority. The lexical path must also contain no symlink or reparse boundary. POSIX
directories use mode `0700` and files use `0600`; Windows paths receive protected
owner-only ACLs and are revalidated on read. Repository text never becomes a path
component: repository IDs are digested, while canonical plan IDs and hashes form the
remaining key.

One versioned record atomically binds the key, exact envelope bytes, persistence and
retention timestamps, and pins. Mutations take a cross-process exclusive store lock and
sample expiry at that locked transition. They write a complete same-directory temporary
record, flush its content and post-permission metadata, replace the prior record, and
durably flush the containing directory. Newly created directories and their parent
entries are flushed as well. POSIX uses `fsync`; Windows uses a write-through replacement
and directory metadata flush. The ordered durability operations are injectable for
failure tests. Repeating persistence for the same key and bytes preserves the original
record, timestamps, and pins; different bytes under an existing key are a conflict.
Interrupted garbage collection renames a record to a tombstone before deletion, and
later collection removes surviving tombstones.

Configured limits bound plan count, individual envelope bytes, total record bytes,
plan lifetime, pins per plan, pin-reference bytes, and dispatch-lease lifetime. A
dispatch pin can be atomically replaced by a non-expiring operation-ledger pin that
retains the source dispatch identity, so only an exact adoption replay succeeds. Expired
unadopted dispatch pins can be released by collection. An operation pin retains the
record after plan and normal-retention expiry until an explicit release representing
verified terminal cleanup or a later authorized acknowledgement. Invalid record or pin
metadata is retained as failed evidence rather than deleted.

The local provider refuses `SharedAuthority` plans. Those plans require a separately
implemented authority-owned provider that proves its service identity, access control,
continuity, quotas, and atomic relationship with the shared operation ledger. A caller
cannot select the shared domain on this private store and thereby self-assert that
authority.

This decision implements the exact-byte parser and the fail-closed durable storage
engine. A production protection authority that issues the physical identity/ownership
lease is still required before the engine is usable; the default refuses persistence.
Mutation preparation does not yet publish plans through it, and apply does not yet
consume it. Full dispatch identity and quotas, durable receipt/ledger transitions,
cleanup bundles, the shared-authority provider, and recovery journaling remain required
before restart-safe apply is available.

## Version/profile scope

[ADR-0106](0106-review-only-change-plan-profile.md) separates future public v2 review
claims from this existing v1 durable reader/store contract. Public review transport
cannot reuse this store by inventing a protection lease, convert review claims into
v1, or relax physical isolation, authenticated ownership, pinning or authority checks.
Unkeyed envelope SHA-256 verification establishes byte integrity/identity, not principal
authentication or apply authorization. Imported workspace claims remain separate from
trusted acquisition receipts; no public plan/verify implementation is implied here.

## Consequences

- Review and a later process can exchange one immutable, verified envelope without
  rebuilding a mutation from command arguments.
- Store corruption and protection drift produce typed unavailability evidence and leave
  the original record in place for diagnosis or recovery.
- Capacity and lease exhaustion fail before another plan or pin is committed.
- A missing physical protection authority refuses the store rather than relying on a
  lexical path or permission bits as proof of isolation.
- Garbage collection can reclaim only unpinned records beyond their retention boundary;
  it cannot infer terminal cleanup from plan expiry.
- The private store is a prerequisite for dispatch and ledger work, not an operation
  ledger or a recovery guarantee by itself.

## Alternatives considered

- **Store plans under `.rt/` in the repository.** Rejected because repository content
  and collaborators could replace control-plane evidence.
- **Serialize the domain model again on load.** Rejected because reflection-based
  deserialization would not prove the persisted bytes were the reviewed canonical
  projection.
- **Delete unreadable or corrupt records during collection.** Rejected because
  corruption may be the only remaining evidence for a pinned or partially accepted
  operation.
- **Treat a constructor flag as shared-authority proof.** Rejected because a local
  caller-controlled flag cannot establish cross-principal ownership or continuity.

## References

- Related ADRs: [ADR-0013](0013-fenced-plan-unavailability-resolution.md),
  [ADR-0014](0014-bounded-recovery-attempt-history.md),
  [ADR-0022](0022-canonical-immutable-change-plan-payload.md)
- Related docs: [architecture overview](../architecture-overview.md)
- Specification: [prepared mutation plans are complete and immutable](../../openspec/changes/critical-platform-hardening/specs/platform-hardening/spec.md#requirement-prepared-mutation-plans-are-complete-and-immutable)
