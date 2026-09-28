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
CLI args -> CLI binder -----------┐
                                  ├-> typed application request
MCP JSON -> generic MCP adapter --┘             |
                                                v
                                  definition + application port
                                                |
                                      execution placement
                                      /                 \
                                in process          daemon RPC
                                      \                 /
                                       typed result/error
                                      /                 \
                              CLI renderer        MCP projection
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
- Map adapter inputs to the host-neutral application request and call the shared
  execution router.
- Write results through the shared renderer.
- Do not load workspaces or call domain services directly.

### MCP Adapter

- Read MCP tool schema/input binding from the host-neutral application definition.
- Bind JSON to the definition's typed application request.
- Invoke the same `ICommandApplicationPort` as the CLI adapter.
- Return structured content plus a text view of the same result.
- Keep command-specific MCP handlers out of the dispatch path.

#### MCP Adapter Flag Reuse

MCP tool schemas derive their properties, required fields, descriptions, arrays, and
enums from the host-neutral application request. They do not instantiate CLI commands
or consume CLI capability interfaces. The central flag catalog remains the CLI source
for option spelling and parsing; architecture tests prove its projection agrees with
the shared request definition where the surfaces overlap.

All MCP-discoverable commands use typed application ports, and their schemas
come from request contracts. The compatibility bridge tracked by
[rt-007](issues/resolved/architecture/rt-007-shared-command-descriptors.md)
has been removed.

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

The shared definition is host-neutral: it does not contain a
`System.CommandLine.Command` builder. CLI discovery projects definitions into CLI
commands; MCP schema discovery projects the same definitions and request contracts
into tools. See [ADR-0007](adr/0007-host-neutral-command-contracts.md).

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

**Status:** the capability interfaces are shipped in
[`RtCommandFlags.cs`](../src/RoslynTools.Cli/CommandFlags/RtCommandFlags.cs). The
architecture ratchet derives migrated commands from implemented capability interfaces.
The central CLI catalog, full binder coverage, `dump-help` projection, and
`CommandOptionFactory` retirement remain in flight under US-114. MCP schema ownership
has moved to host-neutral application requests under ADR-0007.

### Flag Binders

Shared binders parse catalog-owned flags and validate common semantics. Examples:

- `--top >= 0`
- `--apply` and `--skip` are mutually exclusive
- solution path normalization / auto-detection
- output format normalization
- deprecated alias warnings

Migrated command files should not hand-roll `new Option<T>("--known-flag")` or direct `ParseResult.GetValueForOption` parsing for catalog-owned flags.

### Application Requests

Every ordinary command has one transport-neutral application request. CLI and MCP
each bind their transport into that same request, and the application operation owns
domain validation. A small `*CliRequest` may exist as a short-lived parsing aid, but it
must map immediately to the application request and never enter services or RPC.

Good uses:

- `RenameRequest`
- `ListSmellsRequest`
- reusable domain sub-records for genuinely identical concepts

Avoid:

- forcing a request object where direct parameters are clearer
- pushing `ISupportsTopOption`, `ParseResult`, or similar transport interfaces into
  application ports or services
- a mediator framework
- source generation until request boilerplate clearly becomes the bottleneck

## Ratchet Strategy

Use explicit coverage manifests only while a migration is partial. Each manifest must
list the complete intended surface and the enforced contract. The current flag
capability ratchet derives migrated types directly in
[`FlagCapabilityMigrationTests.cs`](../tests/RoslynTools.Tests/Architecture/FlagCapabilityMigrationTests.cs);
the application-port fan-out uses
[`command-port-migration-groups.tsv`](command-port-migration-groups.tsv).

As coverage grows, ratchets should transition from marker-mode to all-command enforcement.

## Ticket Alignment

| Ticket | Alignment | Notes |
|---|---|---|
| US-084 | Strong | Metadata catalog and machine-readable `dump-help` are central to discoverability. |
| US-085 | Superseded by US-114 | Factory-only standardization is weaker than the target. US-114 (capability interfaces + central catalog + shared binders) is the canonical replacement direction. Capability interfaces shipped 2026-06-18; full US-114 closure pending. |
| US-086 | Strong | Command file structure and ratchets support ease of change. |
| US-088 | Strong | `SeeAlso` schema feeds discoverability and command routing. |
| US-089 | Strong | Seeded `SeeAlso` data improves navigation and `rt suggest`. |
| US-093 | Strong | Central exit-code routing belongs in shared command infrastructure. |
| US-094 | Superseded by US-143 | The former native-handler ratchet is replaced by manifest, definition/port, schema, and generic-adapter contracts. |
| US-095 | Strong, rewritten | Convert inspect commands to typed application ports; do not add command-specific MCP handlers. |
| US-096 | Strong, rewritten | Convert analyze commands to typed application ports; do not add command-specific MCP handlers. |
| US-097 | Strong, rewritten | Centralize review/dry-run/apply mutation states and daemon retry safety in the generic contract. |
| US-098 | Strong | Remove MCP -> CLI after bridge deletion; both hosts reference the host-neutral library. |
| US-099 | Consolidate into US-098 | Do not promote CLI discovery or the bridge into a shared hosting project. |
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
7. Follow the serial-foundation, parallel-conversion, bridge-deletion, then physical-decoupling sequence in
   [the application-port migration playbook](command-application-port-migration.md).

## Non-Goals

- No mediator framework.
- No full CQRS framework.
- No source generator until the hand-written request/binder pattern is clearly repetitive across many commands.
- No service dependency on CLI/MCP concepts.
- No big-bang migration across every command.
