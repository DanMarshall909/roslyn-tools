# ADR-0098: Scope complexity rollups to top methods

- Status: Accepted
- Date: 2026-10-01
- Related issue: [RT-016](../issues/open/architecture/rt-016-pre-release-product-spine.md)

## Context

`complexity --top 5 --json` returned five methods but all type and namespace
rollups. On this checkout, the source-built command returned 288,729 stdout
bytes: five methods, 1,117 type rollups, and 49 namespace rollups. The extra
rollups obscured the requested hotspots and made `--top` a weak output bound.

## Decision

Continue computing rollups over every discovered method, before threshold and
top filtering. When `--top N` is positive, return only the complete rollups
for types and namespaces represented by the N returned methods. Preserve the
existing rollup ranking and full-solution aggregate values. `--top 0` retains
every method and rollup.

Expose `rollupScope` as `all` or `returnedMethods`, plus the method count after
threshold but before top and the total type and namespace rollup counts. These
fields let CLI and MCP callers distinguish a focused response from a complete
rollup inventory. The `check` rule uses no top limit and retains its complete
analysis.

## Consequences

- The source-built `--top 5 --json` response fell to 3,289 stdout bytes with
  five methods, four type rollups, and one namespace rollup: 98.9% fewer bytes
  than the preceding 288,729-byte run. This is one response, not a measured
  token or complete-workflow reduction.
- A multi-type fixture proves the omitted type may have a larger aggregate
  while the retained type keeps its full value; `--top 0` still returns both.
  Threshold filtering continues to leave full rollups available when no top
  limit is requested.
- The membership of `typeRollups` and `namespaceRollups` changes for positive
  `--top` values. The command is still pre-release; consumers should use the
  new scope and total fields rather than infer completeness from array size.

## References

- [Pre-freeze surface audit](../../openspec/changes/shape-pre-release-product-spine/pre-freeze-surface-audit.md)
- [Product spine tasks](../../openspec/changes/shape-pre-release-product-spine/tasks.md)
