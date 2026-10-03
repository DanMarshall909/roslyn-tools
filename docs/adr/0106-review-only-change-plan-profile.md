# ADR-0106: Version public review-only plans without changing durable v1

- Status: Accepted contract; implementation pending
- Date: 2026-10-03
- Related OpenSpec: [pre-release product spine](../../openspec/changes/shape-pre-release-product-spine/proposal.md), [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

[ADR-0022](0022-canonical-immutable-change-plan-payload.md) defines the implemented
`rt.change-plan.v1` immutable payload and canonical bytes. That durable model requires
guarded dependencies, real physical path/root identities, coordination, staging/backup
roots and recovery policy. [ADR-0023](0023-protected-durable-change-plan-storage.md)
requires a trusted physical-protection lease before durable storage effects. Public
preview planning must return exact reviewable bytes without inventing these proofs.
Using another incompatible payload shape under the same v1 identifier would change the
meaning of existing canonical identity and consumers.

## Decision

Keep one canonical change-plan family with explicit version/profile admission:

| Pair | Contract |
|---|---|
| Existing `rt.change-plan.v1`, implicit durable profile | Preserve the entire existing model, byte projection, envelope, reader, store/intake/apply requirements and conformance vectors. Do not add a profile field or optional-null authority escape to v1. |
| `rt.change-plan.v2`, mandatory `profile: "review-only"` | Public immutable prepare/verify target. Carry exact review data without execution permission, physical write guards, staging/backup identity, protected-storage authority or restart-safe apply claims. |

The existing v1 conformance hash remains
`5061e76ee8e4b5e5fc17dbce4815aa22f2b3b7e55485efc3c2ad3a066aff5826`.
`CanonicalChangePlan` remains the sole canonical writer/hash owner and
`CanonicalChangePlanReader` the reader/canonical-validation owner. The v2 implementation
adds typed review entry points; existing durable entry points continue to admit only
v1. Reject unsupported version/profile pairs. Never perform automatic profile fallback,
review-to-durable conversion or authority fabrication. Future execution support needs a
separately accepted version/profile and authority contract binding exact reviewed bytes.

The public v2 payload retains plan identity, creation/expiry, repository revision and
source fingerprint, ordered operation and stable selected symbol, complete expected
content/semantic/build inputs, exact original/replacement bytes and effects, derived
diff and affected paths, stable document/project bindings, diagnostics, assumptions,
confidence, limitations, workspace provenance and required verification actions.
Dirty, unaffected and external inputs participate in its binding. Sets, sequences,
paths, wire tokens, explicit nulls/defaults, UTF-8 escaping and UTC timestamps have one
explicit canonical projection with independently fixed vectors. Reuse `ChangePlanDiff`
through a typed review overload or private immutable projection without fabricating
durable effects, guards or physical metadata. Preserve existing v1 diff bytes and
establish independent effect-shape, rename-plus-move, linked-context and
Unicode/escaping vectors before freezing the v2 oracle. The exact envelope
retains payload bytes and a lowercase SHA-256 digest of payload bytes only.

### Claims and authority

Unkeyed SHA-256 supplies integrity and byte identity; it neither authenticates a
principal nor authorizes acquisition, verification or a filesystem effect. A valid
canonical payload with a matching digest can still contain false workspace/input
claims: a caller can replace both payload and hash. Imported review plan/input/workspace
types must therefore remain separate from trusted host-issued acquisition receipts and verification-stage authority. Parsing and
canonical integrity checks cannot construct a trusted receipt or a Verified result.

The host must freshly acquire authorized Complete workspace evidence, validate the
actual complete closure used throughout verification and reject drift before publishing
Verified, or prove an equivalent immutable acquisition boundary. Rehydrate only frozen
effects/document bindings; do not rerun a transformation from saved arguments. Trusted
local policy mandates the six stages: compiler and analyzer deltas, semantic/public API
delta, unresolved references/call sites including generated callers, affected projects
and conservative candidate tests. The profile contains that exact locally supported set
with required=true. Imported names/capabilities/required flags cannot waive, rename or
disable a stage, dispatch arbitrary capabilities, or supply completion authority.
Partial/failed/disabled stages, stale inputs, cancellation or resource bounds cannot
produce Verified. Missing, expired, incompatible, corrupt and stale results remain
explicit and actionable. Preparation, verification and their failure paths write no
repository files, including workspace evaluation, generators and analyzers.

### Public apply and policy status

Omit the optional direct public CLI apply path from the target preview. Public v2 plans
support prepare and verify only; MCP and daemon expose no apply. Later public migration
must reject apply-shaped requests and prove discovery/output parity. Existing hidden
durable v1 maintainer contracts stay separate and retain every authority/physical
protection requirement. This decision does not remove current command paths by itself.

Stateless Base64 envelope return and a 30-minute lifetime, 16 MiB decoded-envelope
limit, 32,768 input/binding records, 4,096 effects or diagnostics, and 64 KiB
identity/argument scalars are proposed implementation policy only. They are not
ratified, measured or fixed public defaults. A decoded-envelope limit does not imply
that Base64 plus review output fits actual CLI/MCP response/token bounds: 16 MiB
becomes 22,369,624 Base64 bytes before outer JSON, projection duplication and MCP
overhead. Bound encoded input before decoding, decoded bytes before parsing,
item/scalar/depth growth and actual complete host responses. Public transport must
preserve complete exact bytes within enforced bounds or use an explicit
repository-external artifact handoff policy; it must not truncate a usable plan.
Retention requires genuine protection authority and cannot borrow the v1 store through
caller-supplied claims. Numeric runtime reservations are unaffected by these proposals.

## Consequences

- Existing durable v1 byte vectors and proof boundaries remain unchanged.
- Public review can evolve honestly without a second incompatible v1 serializer.
- Detailed v2 model/reader vectors and public transport require implementation and
  independent evidence; this ADR supplies no runtime acceptance proof.
- The initial model-only slice cannot complete M5.1, the rename CLI/MCP vertical
  slice, full verify-plan consequences or M5 exits. All mandatory requirements remain
  open, including exact-byte cross-process handoff, no-write boundaries, staleness,
  cancellation, resource bounds and host equivalence.
- Optional public apply omission keeps the supported prepare/verify product independent
  of the later durable kernel; it does not weaken hidden durable contracts.

## Alternatives considered

- **Another v1 shape with a review flag or nullable durable proofs.** Rejected because it
  changes existing canonical meaning and permits an authority downgrade.
- **A second host-specific review serializer.** Rejected because adapters could disagree
  about reviewed bytes; use the existing canonical owners.
- **Authenticate review claims with an unkeyed hash.** Rejected because anybody can hash
  supplied bytes; principal identity and trusted evidence require separate authority.
- **Persist public review through a fabricated durable protection lease.** Rejected
  because lexical location and caller claims cannot prove the protected store boundary.

## References

- Related ADRs: [ADR-0022](0022-canonical-immutable-change-plan-payload.md),
  [ADR-0023](0023-protected-durable-change-plan-storage.md),
  [ADR-0082](0082-maintainer-only-durable-prepare.md)
- Related docs: [architecture overview](../architecture-overview.md)
- Product requirements: [revision-bound plans](../../openspec/changes/shape-pre-release-product-spine/specs/product-spine/spec.md#requirement-selected-mutations-prepare-immutable-revision-bound-plans)
- Open implementation: [Milestone 5](../../openspec/changes/shape-pre-release-product-spine/tasks.md#milestone-5-establish-revision-bound-planning-and-verification)
