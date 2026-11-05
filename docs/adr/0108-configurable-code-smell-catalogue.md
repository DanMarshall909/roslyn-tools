# ADR-0108: Configurable code smell catalogue

- Status: Accepted target; verification pending in issue #158
- Date: 2026-11-05

## Context

The existing size detectors and SOLID review embed their rule definitions in
code. Users need an editable, extensible catalogue with enough metadata to
interpret MCP findings, while retaining deterministic candidate generation,
bounded external review and explicit source-transmission authority.

## Decision

Use a separate versioned `code-smells.json` at the selected repository root,
with an explicit-path option and an embedded packaged default. A selected file
defines a complete catalogue. Validate it once before workspace/provider work;
return its effective rules, hash and full response contract with results.
Keep host policy and credentials outside this file.

A new typed application operation owns `code-smells`/`code_smells`. The CLI and
generic MCP descriptor adapter use that same operation. Existing `list-smells`
and `solid-review` contracts remain compatible. Reuse existing deterministic
detectors, source sanitation, TypeSafe transport and advisory policy instead of
adding a separate provider or bespoke MCP handler.

Rules may reuse supported Roslyn selectors or apply custom semantic questions to
bounded class/method declarations. Rule data never executes code. Effective rule
questions and exception choices travel together to the provider and its parser;
the reported metadata must describe the decisions actually used. The response
schema and protocol vocabulary remain runtime contracts, not arbitrary output
instructions supplied by a repository.

Source scope, exact declaration identity, preparation/request caps, completeness,
trust and explicit transmission controls remain application responsibilities.
Preserve raw evidence, skipped candidates, original provider signals and final
policy reasons. Configuration must never widen authority or manufacture evidence.

## Consequences

Adding a semantic rule does not require rebuilding RT. New deterministic
algorithms still require implementation. Embedded defaults work outside a source
checkout; an explicit catalogue replaces them rather than creating ambiguous
multi-file merge semantics. Schema/CLI/MCP parity and provider choice validation
need boundary tests in addition to in-memory operation tests.

See [the user guide](../code-smells.md) and
[accepted behavior and retained evidence](../code-smells-contract.md).
