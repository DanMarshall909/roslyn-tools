# ADR-0022: Canonical immutable change-plan payload

- Status: Accepted
- Date: 2026-09-23
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md), [pre-release product spine](../../openspec/changes/shape-pre-release-product-spine/proposal.md)

## Context

`PreparedChangeSet` currently contains live Roslyn solution snapshots and process-local
at-most-once state. It can stage and compensate supported filesystem changes in one
process, but it cannot represent the exact reviewed bytes across process restart. A
later apply would otherwise have to rebuild work from command arguments or trust a
serializer whose property order, null handling, and collection order were not part of
the contract.

Issue #123 and the accepted platform-hardening specification require one immutable,
revision-bound plan that CLI, MCP, and any future execution host can identify by the
same bytes and SHA-256 digest. The payload must also carry enough precondition and
policy data for a later apply to reject stale or unsafe work before an effect.

## Decision

The core defines `rt.change-plan.v1` as an immutable `ChangePlanPayload` and projects it
through one manual `CanonicalChangePlan` writer. The schema records repository identity
and revision, source fingerprint, ordered operations and effects, guarded read
dependencies, exact original and replacement bytes, every affected path and its
before/after state, per-path guard requirements, a diff derived from those exact bytes,
diagnostics, assumptions, confidence, limitations, workspace provenance, verification
actions, expiry, protected control roots, path policy, cleanup policy, and recovery
policy.

Repository paths use normalized `/`-separated relative form. Set-like collections are
copied, validated for duplicates, and sorted ordinally; semantic sequences carry an
explicit positive sequence number. Timestamps are UTC with seven fractional digits.
The canonical writer emits UTF-8 JSON in a fixed property order, writes null and default
values explicitly, and uses fixed lower-case wire tokens for enums. It does not use
reflection-based serialization.

The v1 payload is intentionally conservative: effects operate only on writable regular
files. It records lexical and resolved paths plus distinct platform identities for the
repository, control, staging, and backup roots and for existing-path preconditions;
root comparison follows the plan's case policy. It rejects alternate data stream and
other nonportable path forms, detects ancestor and shared-identity conflicts, and
requires explicit UTF-8, BOM, newline, mode, executable, read-only, and link-count
metadata. Long paths, symbolic links, hard-linked regular files, and read-only entries
are rejected. Directory entries may appear as guarded read dependencies, but not as
effects; their observed POSIX link count is a precondition rather than a hard-link
policy violation. Every dependency and affected-path guard must name the mechanism
selected by the coordination mode.

Diagnostics are a set ordered by code, severity, message, and canonical name/value
context. Exact duplicates are rejected so caller enumeration order cannot change the
canonical bytes.

`PlanHash` is the lower-case hexadecimal SHA-256 digest of only those canonical payload
bytes. The hash is absent from the payload. A stored envelope retains the exact payload
bytes beside the claimed hash; verification reserializes the immutable payload, compares
the stored and computed bytes, decodes the claimed digest, and compares the digest in
constant time. A fixed conformance vector protects the byte contract.

This decision establishes the payload and verification boundary. The durable protected
plan store, parser, pinning rules, dispatch lease, ledger, and recovery writer remain
separate implementation slices required before any restart-safe apply claim.

## Version/profile scope

[ADR-0106](0106-review-only-change-plan-profile.md) adds the public review target
`rt.change-plan.v2` with mandatory `profile: "review-only"` under the same canonical
owners. This follow-on decision preserves this ADR's v1 fields, projection and
conformance bytes exactly; no profile field or relaxed guard/root requirement is added
to v1. Imported review claims cannot convert into the durable model or its authority.
For both profiles, unkeyed SHA-256 identifies and checks bytes; principal authentication,
trusted acquisition and effect authority require their separate validated boundaries.
The public v2 model and plan/verify implementation remain pending.

## Consequences

- Review and a future apply can refer to one plan ID, exact payload, and digest without
  recomputing the mutation from command arguments.
- Changing schema fields, property order, normalization, wire tokens, or explicit-null
  behavior changes the conformance digest and therefore requires a new schema version or
  an intentional compatibility decision.
- Exact original and replacement bytes make the payload larger, but they remove
  ambiguity about reviewed diffs, encoding, newline, and content after process restart.
- A plan cannot contain duplicate or conflicting affected paths, path escapes,
  mismatched content hashes, missing guarded dependencies, guard/coordination mismatch,
  overlapping repository/control roots, unsafe file metadata, or a missing required
  verification action.
- Existing `PreparedChangeSet` execution remains transitional. It does not gain durable
  recovery merely because the canonical payload exists.

## Alternatives considered

- **Hash normal `JsonSerializer` output.** Rejected because reflection metadata and
  serializer options do not by themselves define a cross-adapter byte contract.
- **Hash the complete envelope including a provisional hash value.** Rejected because it
  introduces self-reference and permits adapters to disagree about the placeholder.
- **Hash command arguments and recompute at apply time.** Rejected because repository or
  workspace drift could produce different effects from the reviewed work.
- **Store only precondition and postcondition hashes.** Rejected because review,
  compensation, and apply would still need another source for exact original and
  replacement bytes.

## References

- Related ADRs: [ADR-0007](0007-host-neutral-command-contracts.md),
  [ADR-0012](0012-review-first-mutation-defaults.md),
  [ADR-0013](0013-fenced-plan-unavailability-resolution.md),
  [ADR-0023](0023-protected-durable-change-plan-storage.md)
- Related docs: [architecture overview](../architecture-overview.md)
- Specification: [prepared mutation plans are complete and immutable](../../openspec/changes/critical-platform-hardening/specs/platform-hardening/spec.md#requirement-prepared-mutation-plans-are-complete-and-immutable)
