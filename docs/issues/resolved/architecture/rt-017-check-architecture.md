---
tags:
  - architecture
  - analysis
  - mcp
  - ci
ticket: rt-017
severity: High
status: Resolved
area: architecture
owner: ""
reported: 2026-09-08
resolved: 2026-09-08
summary: "Add executable architecture boundary verification"
---

# Add executable architecture boundary verification

## Problem

RT can inspect dependency graphs and cycles, but it cannot validate a repository's intended layer boundaries as a repeatable CI rule. Teams must infer violations from several tools, and source evidence for project, package, framework, SDK, namespace, type, and technology leaks is inconsistent.

## Acceptance criteria

- [x] Add a typed, read-only `check-architecture` CLI/MCP operation.
- [x] Load a stable JSON configuration from inside the consuming repository.
- [x] Validate project-reference direction, forbidden namespaces/types, SDK/framework/package placement, and configurable technology leaks.
- [x] Emit deterministic machine-readable violations with project, file, line, dependency, rule, and source evidence.
- [x] Annotate optional baseline matches without hiding any violation or any newly introduced violation.
- [x] Fail clearly for invalid configuration, paths outside the repository, missing configured projects, and incomplete semantic compilation.
- [x] Prove descriptor/schema parity and trusted/untrusted MCP behavior.

## Constraints

- The operation is read-only and must not add or remove project references or rewrite configuration.
- Repository evaluation remains guarded by RT's existing explicit-trust boundary.
- The CLI and MCP must invoke the same typed application port.

## Resolution

Implemented `rt analyze check-architecture` as one typed, read-only,
daemon-preferred application operation shared by the CLI and generic MCP adapter.
The checked-in JSON policy covers project-reference direction, semantic dependencies,
forbidden namespaces and types, SDK/framework/package placement, technology rules,
and optional visible baselines with stable fingerprints.

Validation fails closed for malformed or outside-root configuration, unmatched or
multiply assigned projects, missing project files, compilation errors, and unresolved
semantic bindings. Roslyn Tools dogfooding checks five projects with zero violations.

Verification on 2026-09-08: `scripts/check-local.ps1 -Full` passed with 1,841
core/CLI tests, 107 MCP tests, 9 intentional skips, a clean build, and formatting
verification.
