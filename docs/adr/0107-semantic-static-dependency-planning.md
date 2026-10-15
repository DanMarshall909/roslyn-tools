# ADR 0107: Semantic static dependency planning

## Status

Accepted, 2026-10-15, for the internal first slice of RT-022. Dependency introduction,
property presets and public host admission remain separate delivery gates.

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

## Consequences

The first slice has no constructor changes, registrations, public command or
host exposure. Dependency introduction must later inventory its complete caller
closure and wiring in one proposal. A separately characterized UTC property
preset must use the shared engine. Existing recipe consolidation and any shared
host exposure must pass admission policy; this contract introduces no durable
apply or recovery mechanism.
