# ADR-0104: Admit represented tasks before ranking recommendations

- Status: Accepted
- Date: 2026-10-02
- Related issue: [RT-016](../issues/open/architecture/rt-016-pre-release-product-spine.md)

## Context

BM25 ranks lexical similarity. Its score is not a probability that a command
completes a task: unsupported foreign-language or email tasks can score higher
than supported C# declaration comparison. Examples and incidental words in a
mutator's description can also suggest a write for a read request.

## Decision

Keep existing explicit workflows and missing-capability guards ahead of general
discovery. In the fallback, rank the entire catalogue, then apply a fixed admission
threshold: all distinct substantive task terms must be represented by canonical
names, aliases, established synonyms, or the first sentence of `WhenToUse`, the
primary capability description. Later explanatory sentences and examples influence
rank but cannot establish admission. A one-term task also needs a name or synonym anchor.
Recognized read clauses exclude repository mutators, including inflected verbs
and the inner operation of a show-how-to request. Inventory questions and noun
requests also count as reads when their first substantive subject is a C# type,
member, symbol, or namespace. Conversational requests to show how to perform a
mutation retain the requested operation.
Read classification shares the request's filler and operand rules; punctuation
inside a recognized C# path does not create a task clause.

Ignore request filler and explicit C# path or identifier inputs, preserving task
constraints, including integer and decimal values. Measurement remains a substantive
action: reading source does not satisfy a request to measure it.
Rename operand slots, named targets, and type/member markers identify
operands independently of capitalization. A generic qualified name needs an uppercase
letter in its final segment; ambiguous lowercase dotted text needs an explicit kind or named marker.
Recognized SQL, JavaScript, TypeScript, Python, and Java source suffixes remain
constraints regardless of casing when a dotted input has no explicit operand marker.
An explicit kind, named target, or rename slot takes precedence: `Widget.SQL` can
name a C# method. Suffix recognition is bounded; an unknown suffix can also be a
legal C# identifier and does not universally establish a foreign file. Generic
`of` or `from` followed by an unqualified word does not identify a symbol. A word
already representing catalogue capability remains a task concept unless it is
explicitly named or occupies a rename operand slot.

Apply the display limit after admission. Failed nonempty discovery returns shared
typed `Unsupported`, empty recommendations and related entries, no workflow, a
task label bounded to 160 Unicode scalars plus an optional ellipsis, and a manual
next step. Empty input, nonpositive limits, and recognized standalone prohibitions
retain `NoMatch`.

Keep lexical command-token repair separate: unknown-command middleware calls
`SuggestCommandName`, retaining its three hints without task-admission rules.
Canonical command metadata remains the only capability catalogue.

## Consequences

- CLI and MCP share the same fallback outcome. No new public flag or numeric
  score threshold is introduced.
- This is conservative lexical admission, not general language understanding or
  proof of capability absence. Valid unfamiliar phrasing and compound jobs can
  receive `Unsupported`; explicit workflows and canonical metadata must improve
  recall without relaxing constraint coverage.
  In noun request framing, a subject before an action can require rephrasing:
  `They need class rename` is unsupported; `They need to rename a class` expresses
  the operation directly. This classifier does not establish general language understanding.
- BM25 ordering and existing typed workflows remain distinct contracts. General
  ordered workflow generation, semantic Git context, and full step metadata remain
  open in RT-016.
- Representative corpus bytes measure discovery responses only. They do not
  establish complete workflow cost or preview readiness.
