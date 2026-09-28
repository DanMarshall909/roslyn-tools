# ADR-0071: Duplicate extraction diagnostics use an explicit logger

- Status: Accepted
- Date: 2026-09-29
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Extends: [ADR-0070](0070-host-owned-output-context.md)

## Context

The duplicate replacement path in Core wrote skipped-match warnings through
`RtLog` and a match summary through `RtConsole`. An extract-method application
operation therefore depended on ambient process output even though the
operation already returned a typed mutation result.

## Decision

Pass an `ILogger` from `ExtractMethodOperation` through `MethodExtractor` to
`MethodExtractorDuplicates`. The shared host supplies its normal
`ILogger<ExtractMethodOperation>`; direct Core callers may omit it and receive
the null logger. Keep the warning and summary text on the CLI's diagnostic
stderr channel. Add
a source guard against ambient output in the duplicate replacement path and
exercise warning and summary delivery with an explicit capturing logger.

## Consequences

- Duplicate extraction in Core no longer reaches process-global output.
- Host logging still sends diagnostics to stderr for CLI invocation; MCP and
  future hosts can select their own logging sink.
- Direct Core calls without a logger perform the transformation silently.
  The solution and typed operation result are unchanged.
- `SolutionWriter` and `SolutionChangeWriter` still own ambient output and
  need separate slices.
