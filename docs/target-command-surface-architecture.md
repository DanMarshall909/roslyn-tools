# Target Command Surface Architecture

This document describes the target shape for `rt` command wiring. The goal is to maximize consistency,
discoverability, and ease of change without adding a framework-heavy CQRS layer.

## Goals

- One authoritative command catalog drives CLI help, `dump-help`, MCP schemas, docs, and suggestion/routing.
- One authoritative flag catalog drives reusable option names, aliases, help text, defaults, and validation.
- CLI and MCP adapters translate transport-specific input into small request objects.
- Services only do or answer the domain thing; they do not know how input arrived.
- Architecture tests ratchet migrated commands toward the target and print actionable failures.

## Target Flow

```text
CLI args / MCP input
  -> command catalog metadata
  -> shared flag catalog + binder
  -> small adapter request
  -> workspace/session loading when needed
  -> domain service call
  -> RtResult / output DTO
  -> shared CLI renderer or MCP response
```

## Layer Responsibilities

### Core / Services

- Own Roslyn and domain logic.
- Receive plain domain values or domain request objects.
- Return domain results or output DTOs.
- Must not reference `System.CommandLine`, `ParseResult`, CLI flag catalogs, MCP schema types, or transport-shaped flag interfaces.

### CLI Adapter

- Own `System.CommandLine.Command` construction.
- Declare command metadata and reusable flags.
- Bind common flags through the central flag binder.
- Compose small `*CliRequest` objects where they reduce noise.
- Map adapter requests to service calls.
- Write results through the shared renderer.

### MCP Adapter

- Own MCP tool schema/input binding.
- Reuse the command/flag metadata where it maps cleanly.
- Call the same domain services as CLI adapters.
- Return structured content plus text fallback once the MCP protocol upgrade lands.

#### MCP Adapter Flag Reuse

MCP tool schemas derive their `inputSchema` properties from the same `RtFlagCatalog` entries the CLI binder uses — names, descriptions, required/optional status, validation. A future MCP-side binder reads `JsonElement` arguments and produces the same typed capability/request objects the CLI binder produces, so the service call signature is identical from either host.

Today's native MCP handlers (`OutlineHandler`, `SuggestHandler`, `FindReferencesHandler`, `RenameSymbolHandler`) hand-roll their `inputSchema` via `JsonSchemaBuilder`. The migration to catalog-driven schemas is the responsibility of **US-095** (inspect natives), **US-096** (analyze natives), and **US-110** (modern `CallToolResult` shape with `outputSchema`) — not US-107 or US-114.

Composes with the [per-command checklist](per-command-checklist.md): once a flag capability lands in the catalog, both CLI and MCP surfaces inherit the same name/description/validation automatically.

## Shared Building Blocks

### Command Catalog

The command catalog is the source of truth for:

- command name and aliases
- category
- examples
- `WhenToUse`
- `SeeAlso`
- supported output formats
- mutability/read-only shape derived from interfaces

Target: generated docs, `dump-help --json`, `rt suggest`, and MCP tool discovery all read from the same catalog data.

### Flag Catalog

The flag catalog is the source of truth for reusable flags:

- `--solution`
- `--dry-run`
- `--json` / `--format`
- `--scope`
- `--top`
- `--symbol`
- `--type`
- `--file`
- `--kind`
- `--decisions`
- `--apply` / `--skip`
- any future flag used by two or more commands

The catalog owns names, aliases, descriptions, defaults, required/optional status, validation, and deprecation aliases.

### Flag Capability Naming

Every reusable flag has a capability interface named `ISupports<FlagPascalCase>Option`. Examples: `ISupportsSolutionOption` for `--solution`, `ISupportsDryRunOption` for `--dry-run`, `ISupportsTopOption` for `--top`.

Capability interfaces are **not empty markers**. They expose the typed value the shared binder populates: `string? SolutionPath`, `bool DryRun`, `int? Top`, etc. This is the key difference from US-107's category markers (`IInspectCommand` etc.), which are deliberately empty.

A command's `*CliRequest` record implements every flag capability the command exposes. The binder reads catalog options from `ParseResult` and constructs the request via `FromFlags(...)`. Services receive plain domain values; they do not see capability interfaces.

### Flag Binders

Shared binders parse catalog-owned flags and validate common semantics. Examples:

- `--top >= 0`
- `--apply` and `--skip` are mutually exclusive
- solution path normalization / auto-detection
- output format normalization
- deprecated alias warnings

Migrated command files should not hand-roll `new Option<T>("--known-flag")` or direct `ParseResult.GetValueForOption` parsing for catalog-owned flags.

### Adapter Requests

Request objects clarify boundaries when a command has enough input shape to justify them. They are adapter-layer types, not service-layer transport leaks.

Good uses:

- `RenameCliRequest`
- `ListSmellsCliRequest`
- reusable shapes such as `SymbolQueryRequest`, `ScopedTopQueryRequest`, or `SolutionDryRunTypeRequest`

Avoid:

- forcing a request object where direct parameters are clearer
- pushing `ISupportsTopOption` or similar transport interfaces into services
- a mediator framework
- source generation until request boilerplate clearly becomes the bottleneck

## Ratchet Strategy

Use marker files while the migration is partial. Each marker file should list migrated commands and the enforced contract.

Current example:

- `docs/flag-capability-migrated-commands.txt`
- `tests/RoslynTools.Tests/Architecture/FlagCapabilityMigrationTests.cs`

As coverage grows, ratchets should transition from marker-mode to all-command enforcement.

## Ticket Alignment

| Ticket | Alignment | Notes |
|---|---|---|
| US-084 | Strong | Metadata catalog and machine-readable `dump-help` are central to discoverability. |
| US-085 | Needs reshaping | Factory-only standardization is weaker than the target. Fold into or supersede with US-114 where possible. |
| US-086 | Strong | Command file structure and ratchets support ease of change. |
| US-088 | Strong | `SeeAlso` schema feeds discoverability and command routing. |
| US-089 | Strong | Seeded `SeeAlso` data improves navigation and `rt suggest`. |
| US-093 | Strong | Central exit-code routing belongs in shared command infrastructure. |
| US-094 | Strong | Native MCP handler ratchet keeps CLI/MCP surfaces aligned. |
| US-095 | Strong | Inspect native handlers should reuse command/service metadata patterns. |
| US-096 | Strong | Analyze native handlers should reuse command/service metadata patterns. |
| US-097 | Strong | Mutation policy is a command-surface concern; dry-run/default apply semantics must be centralized. |
| US-098 | Strong | Dropping MCP -> CLI dependency clarifies adapter boundaries. |
| US-099 | Strong | A command-surface project may become the natural home for command metadata and flag contracts. |
| US-100 | Strong | Renderer consolidation is required for consistent output. |
| US-103 | Related | Service DI cleanup supports the service boundary, but it is not a command-surface feature. |
| US-107 | Shipped foundation | Category/capability interfaces are the first type-system ratchet. |
| US-108 | Strong | Deriving category/mutability from interfaces reduces duplicated truth. |
| US-109 | Strong | Unified format/output pipeline completes renderer consistency. |
| US-110 | Strong | MCP protocol/schema modernization lets command metadata become discoverable to clients. |
| US-114 | Strongest fit | Central flag catalog + binders + adapter requests are the current target for flag consistency/reuse. |

## Recommended Sequencing

1. Keep US-107 as the shipped foundation.
2. Treat US-114 as the replacement direction for US-085 rather than doing a broad factory-only migration.
3. Continue the flag-capability migration in small ratcheted batches.
4. Land reusable request shapes only after two or more migrated commands prove the same shape.
5. Land US-084/US-088/US-089 so command metadata becomes more discoverable.
6. Land US-100/US-109 so output is as standardized as input.
7. Land US-099/US-098 before heavy MCP migration if the CLI/MCP project boundary keeps causing coupling.

## Non-Goals

- No mediator framework.
- No full CQRS framework.
- No source generator until the hand-written request/binder pattern is clearly repetitive across many commands.
- No service dependency on CLI/MCP concepts.
- No big-bang migration across every command.
