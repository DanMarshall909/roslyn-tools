# PRD: Dogfood Refactor Tool Set

## Problem

Recent dogfooding exposed a class of refactors that `rt` cannot perform safely today. The missing tools force manual edits in high-risk areas: command handler thinning, mixed-responsibility diagnosis, adapter extraction around static seams, and moving tests into nested scenario classes.

## Goals

- Add focused refactor tools that preserve the current `rt` conventions: review/dry-run where useful, deterministic output, and Roslyn-backed edits.
- Make command and test refactors repeatable enough for dogfooding on this repository.
- Prefer small, composable commands over a broad "do everything" refactor.

## Non-Goals

- No AI classification service or external model dependency.
- No automatic architecture rewrite across an entire solution.
- No support for arbitrary syntax moves beyond the explicit scenarios below.

## Stories

### US-001: Responsibility Map

As a maintainer, I want `rt responsibility-map` to classify responsibilities inside a type so I can decide what to extract before editing.

Acceptance criteria:
- Command accepts `--type`, `--solution`, optional `--scope`, and output format flags.
- Reports members grouped by responsibility signals such as command wiring, workspace loading, output rendering, file IO, symbol lookup, and pure transformation.
- Includes file path and line numbers for each member.
- Returns an empty but explicit "no mixed responsibilities found" result when no signals are detected.
- Has tests for at least one mixed command class and one cohesive class.

### US-002: Extract Orchestrator

As a maintainer, I want `rt extract-orchestrator` to move a command handler body into a dedicated orchestrator class so command classes stay thin.

Acceptance criteria:
- Command accepts source method or lambda location, target class name, target namespace, target file, and `--dry-run`.
- Creates the target class when missing.
- Moves handler logic into an async method on the orchestrator.
- Rewrites the original handler to construct/call the orchestrator while preserving command option parsing.
- Refuses when captured locals cannot be represented as method parameters.
- Tests cover command handler lambda extraction and regular method extraction.

### US-003: Extract Adapter

As a maintainer, I want `rt extract-adapter` to create an interface and adapter around a concrete type so static or hard-to-test dependencies become injectable.

Acceptance criteria:
- Command accepts `--type`, `--interface`, `--adapter`, `--members`, `--solution`, and `--dry-run`.
- Detects static source types and either refuses with a clear message or creates an instance adapter wrapping static calls when `--allow-static-wrapper` is supplied.
- Generates interface and adapter files in requested namespaces.
- Can optionally add the adapter dependency to a target consumer using existing dependency-injection helpers.
- Tests cover static refusal, static wrapper generation, and instance adapter generation.

### US-004: Move Method To Nested Class

As a maintainer, I want `rt move-method --to-nested-class` to move test methods into scenario classes without manual block surgery.

Acceptance criteria:
- Command accepts method spec, nested class name, `--create-if-missing`, `--solution`, and `--dry-run`.
- Moves the method into an existing nested class or creates one inside the containing type.
- Preserves attributes, trivia, and method body.
- Supports inverse move from a nested class back to the parent when `--from-nested-class` is supplied.
- Tests cover `[Fact]` methods, existing nested class, created nested class, and dry-run.

### US-005: Introduce Record From Parameters

As a maintainer, I want `rt introduce-record` to replace a long parameter list with a generated record request type.

Acceptance criteria:
- Command accepts method spec, record name, target namespace/file, selected parameter names, `--solution`, and `--dry-run`.
- Generates a positional or property-based record from selected parameters.
- Rewrites method signature and call sites.
- Preserves optional/default parameter behavior or refuses when it cannot.
- Tests cover method declaration, call-site rewrite, constructor target, and unsupported params/ref/out refusal.

### US-006: Architecture Verify

As a maintainer, I want `rt architecture-verify` to validate declared boundary rules so refactors do not regress layering.

Acceptance criteria:
- Command accepts a rules file and `--solution`.
- Supports rules for namespace dependency direction, forbidden type references, and command thin-shell limits.
- Emits deterministic text/json results with file path, line, rule id, and message.
- Exits non-zero when violations exist.
- Tests cover passing rules, failing rules, and invalid rule files.

## Open Questions

- Should the rules file use JSON, YAML, or a simple `.rtarch` line format?
- Should `extract-orchestrator` create constructor dependencies automatically or require callers to run `add-dependency` separately?
- Should `move-method` also move shared setup fields, or only the selected method?

## Suggested Implementation Order

1. `responsibility-map`
2. `move-method --to-nested-class`
3. `architecture-verify`
4. `introduce-record`
5. `extract-adapter`
6. `extract-orchestrator`
