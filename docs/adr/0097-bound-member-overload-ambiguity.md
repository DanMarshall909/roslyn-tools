# ADR-0097: Bound member overload ambiguity errors

- Status: Accepted
- Date: 2026-10-01
- Related issue: [RT-016](../issues/open/architecture/rt-016-pre-release-product-spine.md)
- Extends: [ADR-0096](0096-bound-legacy-symbol-ambiguity.md)

## Context

`SymbolLocator.TryFindMemberBySignatureAsync` returns a typed ambiguity error
when a member name matches multiple overloads. Its error message previously
joined every signature without a count, byte limit, or structured candidate
context. This path has a distinct message prefix used by describe drill-in.

## Decision

Use the legacy ambiguity formatter's 12-candidate, 4,096-byte raw UTF-8
context, 1,024-byte candidate display, and 256-byte query limits. Bound the
containing type name separately to 256 bytes. Keep each listed signature
complete and in the member collection's order. Preserve the
`'<input>' is ambiguous on type '<type>'. Candidates:` prefix, semicolon
signature display, and `AmbiguousMatch` kind. Report total and displayed
counts, truncation, and an explicit parameter-signature recovery route in the
message and typed context.

## Consequences

- The existing two-overload drill-in test still sees both complete signatures.
  An 80-overload fixture checks the bounded list, order, complete signatures,
  and counts. A long Unicode type fixture checks the query, type, and message
  byte limits.
- The shared formatter retains the separate general-lookup prefix and guidance.
  It limits raw strings; JSON escaping can increase wire bytes.
- The decision-file path remains unchanged and continues to offer the full
  selection list where it applies.

## References

- [Product spine tasks](../../openspec/changes/shape-pre-release-product-spine/tasks.md)
