# ADR-0101: Describe doctor modes in one product contract

- Status: Accepted
- Date: 2026-10-02
- Related issue: [RT-016](../../openspec/changes/shape-pre-release-product-spine/tasks.md)

## Context

`doctor --installation-only` checks the installed launchers, SDK, and MCP
protocol without loading a solution. The default doctor mode can acquire a
trusted semantic workspace and execute repository build inputs. A single
command-level product profile previously described both as requiring trust and
a semantic workspace while omitting possible code execution.

## Decision

Keep one `doctor` command and result envelope. Add optional mode profiles to
the host-neutral `CommandProductMetadata` record. Each profile has a stable
name, description, canonical request-property selector and default state,
effects, trust and workspace requirements, prerequisites, and an example.
The command-level effects are the conservative union of its modes; a result's
safety reports the effects actually reached. Project modes into CLI structured
help, generated Markdown, and MCP `_meta` from this shared record. Keep the
MCP directory request required in both modes.

## Consequences

- Agents can select installation mode without assuming repository trust or a
  loadable workspace, and can see that default mode may run repository code.
- The strict command-surface schema and command census include the mode shape
  and verify CLI/MCP parity. No second doctor identity or boolean request
  variant is introduced.
- Existing command-level consumers receive a conservative profile. Consumers
  needing a precise pre-invocation profile should select the matching mode.
