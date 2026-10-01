# ADR-0095: Bound shared symbol ambiguity responses

- Status: Accepted
- Date: 2026-10-01
- Related issue: [RT-016](../issues/open/architecture/rt-016-pre-release-product-spine.md)
- Extends: [ADR-0081](0081-bounded-cli-workspace-evidence.md)

## Context

`SymbolCandidateService.ResolveAsync` sorted candidates deterministically but
copied every ambiguous match into both `RtError.Message` and
`Context["candidates"]`. A source-built `relationships callers ExecuteAsync
--include-tests --json` call found 130 matches and returned 85,921 stdout
bytes. The repetition obscured the choice an agent needed to make.

## Decision

Show at most 12 candidates, while also limiting the raw UTF-8 candidate
context to 4,096 bytes and the message's candidate display to 1,024 bytes.
Abbreviate an oversized query at a Unicode scalar boundary within 256 UTF-8
bytes. Never cut a displayed candidate identity: if the next complete entry
does not fit either budget, stop before it. Use `\n` as the deterministic
candidate separator.

Keep the `AmbiguousMatch` kind and existing per-candidate field structure;
normalize the line separator to LF, including on Windows. Add
string-valued `candidateCount`, `displayedCandidateCount`,
`candidatesTruncated`, and `symbolTruncated` context fields. The message and
`disambiguateWith` field direct callers to use `search-symbols` filters to
narrow candidates, then pass a stable symbol identity. `SearchAsync` still
returns the full candidate set to its own bounded search operation; an
ambiguity error is not a search result.

## Consequences

- The representative CLI JSON response fell to 4,150 bytes, 95.2% below the
  immediately preceding 85,921-byte run. It reports 130 total matches and six
  displayed identities. This is a per-call byte comparison, not a measured
  token or workflow reduction.
- Service tests cover large ambiguity, complete resolvable listed identities,
  ordinary two-candidate behavior, and oversized Unicode identifiers. MCP
  tests verify typed context projection and an 8,192-byte serialized response
  bound for an 80-match ASCII fixture.
- The byte budgets apply to raw strings. JSON escaping may enlarge wire output;
  the representative CLI and MCP paths are measured separately. `search-symbols`
  has its own result limits and requires filtering when a broad query truncates.
- Legacy `SymbolLocator` ambiguity errors are a separate path and remain
  unbounded. This decision does not imply that every rt ambiguous error is
  bounded yet.

## References

- [Pre-freeze surface audit](../../openspec/changes/shape-pre-release-product-spine/pre-freeze-surface-audit.md)
- [Product spine tasks](../../openspec/changes/shape-pre-release-product-spine/tasks.md)
