# ADR-0096: Bound legacy symbol ambiguity errors

- Status: Accepted
- Date: 2026-10-01
- Related issue: [RT-016](../issues/open/architecture/rt-016-pre-release-product-spine.md)
- Extends: [ADR-0095](0095-bound-shared-symbol-ambiguity.md)

## Context

`SymbolLocator` still serves refactoring and inspection callers outside the
shared `SymbolCandidateService` migration. Its no-store ambiguous-match error
copied every candidate into both the message and context. This path groups
duplicate display names, preserves its own match order, and uses a separate
decisions-file flow with stable option indexes. Replacing the resolver would
change those contracts.

## Decision

Limit the no-store `AmbiguousMatch` presentation to 12 complete candidate
names, 4,096 raw UTF-8 bytes for the semicolon-delimited context, 1,024 bytes
for the displayed candidate summary, and 256 bytes for the input query.
Truncate only the query, at a Unicode scalar boundary. If the next complete
candidate does not fit, stop listing and report the total and displayed counts
and truncation explicitly. Preserve candidate order, error kind, message
prefix, semicolon context delimiter, and existing recovery wording.

The decisions-file path continues to receive the full candidate set and its
original index mapping. The member-signature overload error has a separate
message contract and is outside this decision.

## Consequences

- An 80-match test checks the bounded result and truthful count; an oversized
  Unicode identifier checks the zero-display case and byte limits. Existing
  two-candidate and decision-file tests continue to protect their contracts.
- Raw string limits do not cap JSON-escaped wire size. The context preserves
  only a prefix of complete names; callers can supply a more specific fully
  qualified name to resolve the ambiguity.
- Legacy member-signature overload output remains unbounded until its own
  accepted slice.

## References

- [Product spine tasks](../../openspec/changes/shape-pre-release-product-spine/tasks.md)
