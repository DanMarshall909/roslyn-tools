# TypeSafe semantic review for SOLID candidates

- Status: accepted
- Decision owner: repository owner
- Tracking: https://github.com/DanMarshall909/roslyn-tools/issues/135

## Why

`rt analyze solid` deliberately uses deterministic, inexpensive Roslyn heuristics. A
dogfood sample showed that those heuristics surface useful candidates but also rank
closed syntax classifiers, composition boundaries, and immutable value construction
as if they were equally actionable. Nine bounded TypeSafe reviews cost less than one
US cent and separated several likely false positives from a plausible dependency
inversion concern.

## What Changes

- Add an adjacent, explicit `rt analyze solid-review` command. The existing
  `rt analyze solid` command and its output remain unchanged.
- Preserve the complete deterministic result in a versioned review envelope and add
  advisory semantic assessments without suppressing candidates.
- Introduce a provider-neutral semantic-review port with TypeSafe System One as the
  first provider.
- Add a source-free dry run that reports the exact candidate symbols, transmitted
  byte counts, provider, model, and request count.
- Require repository trust before source is read for review and require an explicit
  non-dry-run invocation before any source is transmitted.
- Bound every request to one declaration or member and principle-relevant structural
  evidence. Comments and literal contents are treated as untrusted data.
- Keep semantic review out of CI enforcement and source mutation.

## Capabilities

### New Capabilities

- `solid-semantic-review`: bounded, opt-in, provider-neutral semantic ranking of
  deterministic SOLID candidates.

### Modified Capabilities

None. `solid` remains the deterministic source of candidate evidence and retains its
existing contract.

## Impact

The change adds one public analyze command/application port, semantic-review domain
contracts, a TypeSafe HTTP adapter, output models, acceptance fixtures, and focused
documentation. It uses the platform HTTP and JSON libraries and adds no package
dependency. It does not cache source or provider results in V1.

## Non-goals

- Proving a SOLID violation or replacing Roslyn analysis.
- Automatically changing source or failing CI.
- Uploading a repository, project, file, or unrelated declarations.
- General analyzer cleanup or changing existing `solid` thresholds.
- Provider-result caching in V1.
