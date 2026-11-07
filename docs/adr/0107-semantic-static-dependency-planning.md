# ADR 0107: Semantic static dependency planning

## Status

Accepted, 2026-10-15, for the internal first slice of RT-022; extended 2026-11-07
for bounded constructor introduction and explicit existing-field reuse. Broader
introduction cases, explicit parameter bindings, property presets and public host
admission remain separate delivery gates.

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
The selected class must not declare its own destructor/finalizer: runtime
finalization cannot be confined to the declared dependency lifetime. Refuse such
types before constructing a candidate; no general finalizer or runtime lifetime
proof is attempted. An unrelated nested type's finalizer does not make the
selected class finalizable.
Every construction must belong to a known ordinary synchronous caller, receive
an explicitly selected existing parameter of the exact dependency type, and use
the consumer as a confined local. Unsupported type uses, generated/shared contexts,
reflection/runtime activation and escaped values refuse introduction. Name
collisions refuse rather than invent names or caller values.

Introduce a private readonly field, append the constructor parameter and its
caller arguments, and assign the field before the original constructor body.
Preserve original argument evaluation order and verify the actual candidate field,
constructor, assignment and every caller binding. Original consumer escape checks
also cover implicit functions and state machines. Explicit `base` receivers
belong to the selected consumer when their containing-instance kind and enclosing
semantic owner identify it, even when their static type is `System.Object`.
Apply that ownership to inherited calls, method groups, deferred functions and
state-machine capture guards; unrelated nested types' receivers retain their own
ownership. Independently check every
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

Source and document paths establish exact Roslyn identity and unambiguous paths
within the acquired solution. They do not prove filesystem inode or symlink
uniqueness, or expand this no-write preparation contract into durable apply.

### Bounded explicit receiver reuse (2026-11-07)

When `ExistingReceiverFieldDocumentationId` is supplied, resolve that exact
instance field on the selected source class in the acquired original project
before new-field closure analysis and before empty-inventory success. Its
declared type must equal the mapping's exact receiver type. Inherited fields,
other owners, properties, static fields, unknown IDs and merely convertible
types refuse atomically; they never fall back to a new field. Preferred field
and parameter names are unused hints in this mode, including colliding hints.

Method-only reuse makes no constructor or caller edits and requires neither
caller bindings nor the new-field closure's constructor/type confinement proof.
Existing constructors, chains and caller wiring remain untouched. The original
selected-site admission and selected-rewrite deferred/state-machine guards still
apply. Every selected document must be authored and have an absolute exact
unique Roslyn path across the original solution's document contexts, including
linked/shared project contexts. Bypassing constructor closure does not bypass
that edit-path admission.

A selected constructor call has a separate deliberately narrow availability
proof: an ordinary unchained block constructor's first statement must assign
the selected field on this instance directly from an ordinary parameter of
exactly the field's type. Every selected constructor call must follow that
statement, with no competing writes or ref/out/ref-alias escape of the field
in that constructor. Conditional, nested or later assignment, a selected call
in the assignment RHS, helper execution before assignment and explicit
initializers/chaining refuse. This is not general dataflow or an
interprocedural availability proof. Explicit selection of only a safe method
may leave unsupported constructor source sites inventoried as skipped.

Reuse retains the original acquisition and inventory, shared invocation rewrite,
argument/target/receiver binding checks and final candidate verification. Its
closure contains only mapped calls at original anchors. Resolution reports
`ReusedExistingReceiver=true`, the exact field ID and actual name, and
`ParameterName=null`; no fields, parameters, assignments or caller arguments are
added. Refusal blocks every selected original site while preserving unselected
sites, and returns no candidate changes, closure, resolution or effects. A
freshly acquired valid candidate is a no-op, but even empty source inventory
must validate the explicit field. Version 1's mapping fingerprint remains
mapping-only. Receiver availability, lifetime, non-null and adapter behavior
remain explicit declared preconditions, separate from compiler structural proof.

## Consequences

Both planners remain internal and unwired from public hosts. The bounded case
introduces no framework registrations, guessed dependencies or writer effects.
Authored partial declarations and supported constructor chains for new dependency
introduction require subsequent evidence under issue 153, as do version 2 explicit
parameter/dependency bindings and the characterized UTC property preset. Later
multi-dependency planning must share one original closure; this bounded reuse
policy does not implement that extension.
Existing recipe consolidation and shared host exposure must pass admission
policy; MCP remains plan-only. This contract introduces no durable apply or
recovery mechanism.
