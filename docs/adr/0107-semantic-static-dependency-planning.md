# ADR 0107: Semantic static dependency planning

## Status

Accepted, 2026-10-15, for the internal first slice of RT-022; extended 2026-11-07
for bounded constructor introduction. Broader introduction cases, explicit
parameter bindings, property presets and public host admission remain separate
delivery gates.

## Context

Replacing a static callee with an injected receiver changes more than a member
name. A textual match can select a user lookalike, change argument binding or
leave an undefined receiver. Compilation alone cannot establish that an
arbitrary reader has the BCL API's behavior.

## Decision

Keep the versioned mapping and plan contracts in Core and preparation in a
shared service. A mapping supplies exact source and target documentation and
assembly identities, an existing field, explicit project/type/site selection,
the source fingerprint, and caller-declared behavioral preconditions with
contract evidence. No third-party adapter or host framework enters Core.

The initial grammar maps ordinary static method invocations to ordinary
instance methods, retaining the original ordered arguments. Verify the actual
receiver, target, parameter associations, conversions and nested argument
bindings in the candidate. Unsupported contexts or declarations block the whole
selection; callers may explicitly narrow site selection. Report every inventoried
source reference with a stable disposition and complete changed document text.

Use authoritative acquisition of the original solution for scope evidence.
Inventory authored and generated references, refuse selected generated sites,
and refuse candidate-induced generated output changes. Candidate compilation
does not create a new acquisition receipt. Reacquisition for a transformed
solution uses a fresh workspace and manifest.

Return the existing `PreparedChangeSet` without invoking a writer. Source and
mapping fingerprints bind the proposal to its inputs. Structural proof remains
separate from receiver lifetime/non-null assumptions and adapter exception,
encoding, cancellation and resource-ownership behavior. The engine requires
these declarations; it does not certify supplied evidence or arbitrary adapter
equivalence.

### Bounded constructor introduction

Keep introduction separate from the original existing-field contract while sharing
the exact invocation rewriter and candidate-binding checks. One acquired original
solution owns the selection, source anchors, constructor/value closure and receipt.
Verify that original authority remains unchanged after constructing the candidate;
a compiling candidate cannot replace or upgrade its acquisition evidence.

The initial supported closure is one internal sealed, nongeneric, top-level class
with one authored nonpartial declaration and one ordinary unchained instance
constructor. It has no custom base type, interfaces or activation attributes.
Every construction must belong to a known ordinary synchronous caller, receive
an explicitly selected existing parameter of the exact dependency type, and use
the consumer as a confined local. Unsupported type uses, generated/shared contexts,
reflection/runtime activation and escaped values refuse introduction. Name
collisions refuse rather than invent names or caller values.

Introduce a private readonly field, append the constructor parameter and its
caller arguments, and assign the field before the original constructor body.
Preserve original argument evaluation order and verify the actual candidate field,
constructor, assignment and every caller binding. Original consumer escape checks
also cover implicit functions and state machines. Independently check every
selected replacement: an originally static call must not acquire a consumer
receiver inside a deferred function or unsupported async/iterator execution.
Eager initial query sources remain eligible when their generated selectors do not
capture the consumer. A refusal marks every selected source call blocked and
returns no partial candidate, document changes, closure or resolution.

The introduction result identifies all original closure anchors and candidate
symbols, complete document diffs and the actual receiver resolution. Version 1
`MappingFingerprint` retains its mapping-only meaning; it is not advertised as a
hash of the full request. Full dependency/binding plan identity belongs to the
separately versioned explicit-binding extension.

## Consequences

Both planners remain internal and unwired from public hosts. The bounded case
introduces no framework registrations, guessed dependencies or writer effects.
Exact dependency reuse through introduction, authored partial declarations and
supported constructor chains require subsequent evidence under issue 153, as do
explicit parameter/dependency bindings and the characterized UTC property preset.
Existing recipe consolidation and shared host exposure must pass admission
policy; MCP remains plan-only. This contract introduces no durable apply or
recovery mechanism.
