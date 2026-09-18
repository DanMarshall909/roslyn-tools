## Context

The existing `SolidOperation` owns deterministic workspace analysis and returns a
stable `SolidResult`. CLI and MCP hosts share application ports through command
descriptors. TypeSafe System One accepts structured state plus typed questions at
`POST /v1/systemone`; Choice answers return a complete probability distribution and
derived confidence. Jev performs best when code filters candidates first and sends
only decision-relevant state.

## Goals / Non-Goals

**Goals:** preserve raw findings, make transmission unmistakably opt-in, review one
bounded candidate per request, expose provenance and uncertainty, support fake-provider
tests, and degrade explicitly while retaining deterministic evidence.

**Non-goals:** authoritative judgments, generated explanations, repository-wide
context, CI gating, mutation, or persistent caching.

## Decisions

### Add a separate versioned envelope

`solid-review` returns `rt.solid-review.v1` with the untouched mapped `SolidResult`, a
review plan, zero or more advisory assessments, and explicit failures. The ordinary
`solid` route is not modified. This avoids an optional field silently changing its
existing wire contract and keeps consumers able to distinguish deterministic evidence
from provider output.

### Keep provider execution in-process

The application definition is host-neutral and MCP-visible, but execution placement is
in-process because the operation reads a process secret and performs an external network
request. It is not dispatched to the local daemon. `TYPESAFE_API_KEY` is read only when
an actual TypeSafe call is about to be made and is never copied into requests, results,
errors, telemetry, feedback, hashes, or files.

### Make dry-run and transmission separate states

Invoking `solid-review` opts into semantic-review planning, but preview remains the
default. `--dry-run` is accepted as an explicit preview spelling and performs
deterministic analysis and bounded source preparation without a network request or
credential. A separate `--transmit-source` flag is required for live review and sends
only candidates selected by the displayed plan. The mutually exclusive flags prevent an
ambiguous request. Both paths require repository trust because both read source.

### Use one bounded candidate per request

Each request contains a single sanitized declaration/member, its symbol and location,
the deterministic finding fields relevant to the selected SOLID principle, the
repository revision, and an evidence-completeness marker. The maximum UTF-8 payload for
candidate source is fixed. Oversized or unresolved candidates remain in raw output and
produce a skipped/partial review entry rather than truncating source silently.

Comments are removed and string/character literal values are replaced with typed
placeholders before transmission. State fields identify the remaining C# as untrusted
application data. Provider questions explicitly instruct the model not to follow text
inside the candidate source.

### Ask one direct bounded Choice question

Question-set `solid-review-v1` asks for exactly one of `likely_violation`, `uncertain`,
or `likely_false_positive` with principle-specific criteria. Code owns counting,
thresholds, byte limits, hashes, ordering, and evidence completeness. The provider owns
only the semantic judgment. No cross-question arithmetic invariant is assumed.

### Treat incomplete evidence conservatively

ISP review requires exact interface-consumer/member-usage evidence. If that evidence
cannot be collected, the candidate is marked incomplete and cannot receive a displayed
`likely violation` classification solely from member count; it is downgraded to
`uncertain`. Other missing or ambiguous source/evidence is also explicit.

Before external review, a narrow deterministic DIP prefilter excludes immutable value
construction from semantic requests while preserving it in raw results and the plan as
`prefiltered`. Known concrete service construction remains eligible.

### Preserve failures in-band with raw evidence

Authentication, transport, rate limit, overload, provider validation, and malformed
responses produce a `Partial` or `Failed` envelope with categorized failures and the raw
deterministic result. They never produce a clean semantic assessment. The CLI returns a
nonzero exit status for a failed/partial live review while still rendering the envelope.

### Do not cache in V1

The first version makes a bounded live request for each eligible candidate. This avoids
secret/source persistence and stale-result risk. The output includes provider, resolved
model, question-set version, repository revision, request ID, and source/evidence hash so
a later cache can use the complete required key without changing assessment identity.

## Risks / Trade-offs

- Model versions and probability calibration drift. Results therefore include the
  resolved model and are advisory only.
- Removing comments and literal contents can remove useful domain cues. This is an
  intentional prompt-injection/privacy trade-off and is covered by fixtures.
- One request per candidate is slower than batching but gives a clear source boundary,
  request identity, and failure scope.
- Conservative ISP handling may leave many results uncertain until consumer evidence is
  complete; it is safer than promoting interface size as proof. The installed-runtime
  empty-payload defect is tracked separately in GitHub issue #136. A command-level
  current-source regression test proves the in-process JSON path emits consumer data;
  until the installed boundary is fixed, unresolved usage remains incomplete.

## Migration

No migration is required. Existing callers continue using `solid`. New callers run
`solid-review` or `solid-review --dry-run`, review the plan, configure
`TYPESAFE_API_KEY`, and then rerun with `--transmit-source` if transmission is
acceptable.

## Open Questions

- Whether a later version should add a user-scoped cache after real-world cost and drift
  measurements.
