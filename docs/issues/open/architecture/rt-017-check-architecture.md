# rt-017: Add executable architecture boundary verification

## Type

Task

## Problem

RT can inspect dependency graphs and cycles, but it cannot validate a repository's intended layer boundaries as a repeatable CI rule. Teams must infer violations from several tools, and source evidence for project, package, framework, SDK, namespace, type, and technology leaks is inconsistent.

## Acceptance criteria

- Add a typed, read-only `check-architecture` CLI/MCP operation.
- Load a stable JSON configuration from inside the consuming repository.
- Validate project-reference direction, forbidden namespaces/types, SDK/framework/package placement, and configurable technology leaks.
- Emit deterministic machine-readable violations with project, file, line, dependency, rule, and source evidence.
- Annotate optional baseline matches without hiding any violation or any newly introduced violation.
- Fail clearly for invalid configuration, paths outside the repository, missing configured projects, and incomplete semantic compilation.
- Prove descriptor/schema parity and trusted/untrusted MCP behavior.

## Constraints

- The operation is read-only and must not add or remove project references or rewrite configuration.
- Repository evaluation remains guarded by RT's existing explicit-trust boundary.
- The CLI and MCP must invoke the same typed application port.
