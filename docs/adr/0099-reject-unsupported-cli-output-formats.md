# ADR-0099: Reject unsupported explicit CLI output formats

- Status: Accepted
- Date: 2026-10-01
- Related issue: [RT-016](../issues/open/architecture/rt-016-pre-release-product-spine.md)

## Context

The CLI registers `--json`, `--llm`, `--markdown`, and `--yaml` globally, while
command metadata declares a narrower set of supported formats. A source-built
`complexity --top 5 --llm` completed with exit code 0 and no stdout or stderr:
the LLM renderer found no LLM-level fields. Other unsupported combinations
could emit a different or oversized representation. A successful empty result
is misleading to a caller that relies on the exit code.

## Decision

Resolve the selected public command instance to its canonical `RtCommandInfo`
before invoking its handler. If an explicitly selected effective format is
absent from a nonempty `SupportedFormats` declaration, return exit code 1,
empty stdout, and a stderr diagnostic naming the command, flag, and supported
formats. Run this check before repository trust selection, decision-file
loading, or workspace acquisition. Preserve the existing precedence when
multiple format flags are present: Markdown, YAML, JSON, then LLM.

The default Text mode is not an explicit format request. Commands without a
format declaration and meta commands outside the public registry keep their
existing behavior. Per-command help continues to show the supported `Output`
formats; inherited global flag descriptions now say they apply when supported.

## Consequences

- Real subprocess tests cover grouped, root, and alias invocations, three
  unsupported flags, a second command, and a supported higher-precedence flag.
  A nonexistent solution proves rejection happens before workspace loading.
- The public command catalogue remains the source of truth; no parallel
  hand-maintained support list is needed. Generic MCP schemas continue to use
  the same command metadata and are unaffected by this CLI-only validation.
- Commands with no `SupportedFormats` declaration are not covered by this
  guard. Their output contracts remain a separate inventory decision.

## References

- [Pre-freeze surface audit](../../openspec/changes/shape-pre-release-product-spine/pre-freeze-surface-audit.md)
- [Product spine tasks](../../openspec/changes/shape-pre-release-product-spine/tasks.md)
