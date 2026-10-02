# Command contract status

<!-- GENERATED — node scripts/generate-command-contract-status.mjs; do not edit by hand. -->

This view reads authored authority records. It is not a fresh runtime gate,
live GitHub ticket status, or preview admission verdict.

## Typed-port migration record

[RT-007](issues/resolved/architecture/rt-007-shared-command-descriptors.md#acceptance-criteria) records its archive date as 2026-09-29 and 13/13 acceptance criteria checked.

The [migration manifest](command-port-migration-groups.tsv) records 66 category/control command rows:

| Category | Total | Complete | Reconcile | Convert |
| --- | ---: | ---: | ---: | ---: |
| analyze | 16 | 16 | 0 | 0 |
| ci | 1 | 1 | 0 | 0 |
| control | 1 | 1 | 0 | 0 |
| inspect | 13 | 13 | 0 | 0 |
| refactor | 35 | 35 | 0 | 0 |

These are manifest work states, not supported-preview counts. RT-007 owns the
archived typed-port and host-decoupling evidence, including bridge deletion.

## Canonical-contract admission record

[Platform hardening P1.1](../openspec/changes/critical-platform-hardening/tasks.md#p11-five-command-canonical-spine) records 0/5 tasks checked:

- [ ] Convert `outline`, `find-refs`, `find-unused`, `rename`, and `safe-delete` to one typed request/operation/result/provenance contract.
- [ ] Delete CLI reconstruction, rendered-output parsing, direct service shortcuts, and compatibility bridges for each converted command.
- [ ] Prove CLI, MCP, in-process, and transparent-daemon parity for success, expected error, cancellation, partial workspace, and bounded-output cases.
- [ ] Require absolute request context and the same safety/confidence/resource policy in every host.
- [ ] Do not fan out broad command migration until all five pass the admission matrix.

Typed dispatch and bridge deletion do not prove the broader request/result,
provenance, safety, cancellation, completeness, resource, and host-parity
admission matrix. An unchecked P1.1 bridge task is a broader admission record;
it is not evidence that the archived CLI bridge still exists. Its owner must
reconcile that task with fresh canonical-contract evidence before closing it.

## Evidence owners

- [Descriptor contracts](../tests/RoslynTools.Tests.Mcp/Architecture/CommandDescriptorContracts.cs): discoverable typed ports, bridge absence, generic dispatch, and schemas.
- [Host dependency ratchets](../tests/RoslynTools.Tests.Mcp/Architecture/HostDependencyRatchetTests.cs): source/assembly boundaries and shared typed contracts.
- [Manifest contracts](../tests/RoslynTools.Tests/Architecture/CommandMigrationManifestContracts.cs): command inventory and discovered-port agreement.
- [Product-spine tasks](../openspec/changes/shape-pre-release-product-spine/tasks.md): current product shaping and preview admission.
- [RT-016](issues/open/architecture/rt-016-pre-release-product-spine.md): execution record and links to the owning OpenSpec.

The linked test files describe verification owners; this generator does not
execute them. Regenerate after an authority changes; check for stale output
with `node scripts/generate-command-contract-status.mjs --check`.
