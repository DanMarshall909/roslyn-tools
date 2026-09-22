# solid-semantic-review Specification

## Purpose

Define a bounded, opt-in, advisory semantic-review stage for deterministic SOLID
candidates without changing or weakening the existing analyzer contract.

## ADDED Requirements

### Requirement: Existing deterministic analysis remains unchanged

The system SHALL preserve the behavior and serialized contract of `rt analyze solid`.
Semantic review SHALL be exposed through the adjacent `solid-review` operation and SHALL
retain every raw deterministic finding in a versioned envelope.

#### Scenario: ordinary SOLID analysis
- **WHEN** a caller invokes `rt analyze solid` with any supported principle or format
- **THEN** no provider is resolved, no credential is read, no network request occurs,
  and the existing output contract is unchanged

#### Scenario: semantic ranking does not suppress a candidate
- **WHEN** a raw candidate is prefiltered, skipped, uncertain, or likely a false positive
- **THEN** it remains present in the raw result and its review disposition is additive

### Requirement: Source transmission is explicit and previewable

The system SHALL require repository trust before preparing candidate source and SHALL
default `solid-review` to preview. Source SHALL be transmitted only when the caller also
supplies `--transmit-source`. Before transmission, the plan SHALL identify candidate
symbols, source byte counts, provider, requested model, and estimated request count.

#### Scenario: preview has no external effect
- **WHEN** a trusted caller invokes `solid-review` with or without `--dry-run` and without
  `--transmit-source`
- **THEN** the system returns the exact review plan without reading a provider credential
  or making a network request

#### Scenario: untrusted repository
- **WHEN** a caller invokes `solid-review` without explicit or startup repository trust
- **THEN** source preparation and transmission are rejected before any provider request

#### Scenario: live opt-in
- **WHEN** a trusted caller invokes `solid-review --transmit-source`
- **THEN** only candidates listed as eligible in the plan are transmitted

#### Scenario: conflicting transmission modes
- **WHEN** a caller supplies both `--dry-run` and `--transmit-source`
- **THEN** the request is rejected before source preparation or provider access

#### Scenario: candidate limit
- **WHEN** deterministic analysis returns more raw candidates than the configured
  limit
- **THEN** stable ordering selects at most that limit for source/evidence preparation
  and reports the remaining candidates without preparing or transmitting them

### Requirement: Each provider request is bounded and treats source as data

Each semantic request SHALL contain one candidate declaration/member and only the
structural evidence relevant to its principle. Comments SHALL be removed and literal
contents SHALL be replaced before transmission. Oversized or unresolved candidates SHALL
not be silently truncated or transmitted.

#### Scenario: injected comment
- **WHEN** candidate comments contain instructions addressed to the provider
- **THEN** the instructions are absent from transmitted state and cannot alter questions

#### Scenario: injected string literal
- **WHEN** a string or character literal contains provider-like instructions or a secret
- **THEN** its value is replaced by a typed placeholder before hashing and transmission

#### Scenario: disabled source and directive messages
- **WHEN** a declaration contains disabled preprocessor text or directive messages
- **THEN** those trivia contents are absent from the transmitted candidate source

#### Scenario: oversized declaration
- **WHEN** a candidate declaration exceeds the source byte limit
- **THEN** raw evidence remains available and the review is marked skipped with an explicit
  oversized-source reason

#### Scenario: oversized structural evidence
- **WHEN** a candidate's serialized structural evidence exceeds its byte limit
- **THEN** the candidate is marked skipped, its raw finding remains available locally,
  and no provider request is made

#### Scenario: one-candidate isolation
- **WHEN** two findings occur in different declarations or members
- **THEN** each eligible finding produces a separate provider request with no unrelated
  declaration source

### Requirement: Semantic output is advisory and provenance-bound

Every assessment SHALL use `likely violation`, `uncertain`, or `likely false positive`
language and SHALL record principle, location, raw evidence, full probabilities,
confidence, evidence completeness, provider, resolved model, question-set version,
request ID, repository revision, and source/evidence hash.

#### Scenario: complete Choice response
- **WHEN** the provider returns all expected Choice probabilities and confidence
- **THEN** the assessment records them without relabelling the result as proof

#### Scenario: model alias resolves
- **WHEN** the requested model is an alias and the provider reports a concrete model
- **THEN** both the requested and resolved model identities remain observable

#### Scenario: changed source
- **WHEN** candidate source or structural evidence changes
- **THEN** the source/evidence hash changes and the old assessment is not applied

### Requirement: Incomplete evidence is conservative

The system SHALL expose evidence completeness independently from provider confidence.
An ISP fat-interface candidate without exact consumer/member-usage evidence SHALL be
incomplete and SHALL NOT be promoted to `likely violation` solely from member count.

#### Scenario: ISP consumer usage unavailable
- **WHEN** exact consumer/member usage cannot be established
- **THEN** the assessment is incomplete and its displayed classification is `uncertain`

#### Scenario: cohesive wide interface fixture
- **WHEN** a wide interface is consumed cohesively by each known consumer
- **THEN** the evidence records that usage and permits an accepted-exception assessment

#### Scenario: segregated consumer clusters
- **WHEN** distinct consumers use disjoint interface member subsets
- **THEN** the exact usage evidence is included in the bounded state

### Requirement: False-positive decisions are explainable and conservatively composed

The provider request SHALL ask independent questions about meaningful design harm and
accepted exceptions in addition to the relative classification. A likely false positive
SHALL name a typed accepted-exception reason. Code SHALL compose these signals and SHALL
route weak or contradictory evidence to `uncertain` rather than treating model confidence
as correctness.

#### Scenario: accepted exception conflicts with violation label
- **WHEN** the provider selects likely violation but independently assigns substantial
  probability to an accepted exception
- **THEN** the displayed classification is `uncertain` and records the conflict reason

#### Scenario: unexplained false-positive label
- **WHEN** the provider selects likely false positive without supporting an accepted
  exception and a concrete exception reason
- **THEN** the displayed classification is `uncertain`

#### Scenario: supported accepted exception
- **WHEN** the false-positive label, accepted-exception signal, and concrete exception
  reason agree above the conservative policy thresholds
- **THEN** the assessment is likely false positive and exposes the reason and raw signals

### Requirement: Low-information DIP candidates are prefiltered safely

The system SHALL exclude demonstrably low-information immutable value construction from
external semantic requests while preserving raw findings. It SHALL retain concrete
service construction that can impede substitution.

#### Scenario: immutable record construction
- **WHEN** a DIP inline-new finding constructs an immutable record value
- **THEN** it is marked prefiltered and no provider request is made for that finding

#### Scenario: concrete service construction
- **WHEN** a member directly constructs a concrete service with behavior or dependencies
- **THEN** the candidate remains eligible for semantic review

### Requirement: Provider failures remain explicit

Live review SHALL classify credential, authentication, rate-limit, overload, network,
validation, and malformed-response failures. It SHALL return raw findings plus a partial
or failed outcome and SHALL NOT represent the failed candidate as cleanly assessed.

#### Scenario: missing credential
- **WHEN** live TypeSafe review begins without `TYPESAFE_API_KEY`
- **THEN** no request is sent and the failed envelope retains raw deterministic findings

#### Scenario: authentication failure
- **WHEN** TypeSafe returns HTTP 401
- **THEN** the result records a redacted authentication failure and exposes no credential

#### Scenario: rate limit or overload
- **WHEN** TypeSafe returns HTTP 429 or 529 after bounded retry handling
- **THEN** the affected candidate is explicitly partial and raw evidence remains available

#### Scenario: malformed provider response
- **WHEN** an answer is missing, has the wrong type, or omits expected probabilities
- **THEN** it is rejected as malformed rather than converted into an assessment

#### Scenario: one request fails among several
- **WHEN** at least one candidate succeeds and another fails
- **THEN** the envelope outcome is partial and distinguishes the successful assessment
  from the failed review

### Requirement: Secrets and review data are not persisted

The TypeSafe credential SHALL be read at the transmission boundary and SHALL NOT appear
in output, logs, feedback, hashes, cache keys, or repository files. V1 SHALL NOT persist
provider responses or candidate source in a cache.

#### Scenario: diagnostic output after provider failure
- **WHEN** a transport exception contains request metadata
- **THEN** the surfaced error is categorized and redacted without headers or credential

#### Scenario: repeated live invocation
- **WHEN** an unchanged candidate is reviewed twice
- **THEN** V1 performs a fresh bounded request rather than using persistent cached state

### Requirement: Hosts share one operation contract

CLI and generic MCP projection SHALL invoke the same provider-neutral application port.
The operation SHALL execute in-process and SHALL remain read-only with respect to the
repository.

#### Scenario: CLI dry run
- **WHEN** CLI flags bind a review request
- **THEN** the shared operation produces the same structured result as direct port use

#### Scenario: MCP descriptor projection
- **WHEN** the command registry projects the operation for a trusted MCP repository
- **THEN** request and response schemas include the explicit dry-run/transmission fields
  and no bespoke MCP handler is required

#### Scenario: daemon is available
- **WHEN** `solid-review` runs while the local daemon is healthy
- **THEN** the external-provider operation remains in-process and is not daemon-dispatched

### Requirement: Evaluation remains non-enforcing

At least thirty labelled fixtures SHALL cover all five SOLID principles, genuine
violations, accepted exceptions, and borderline cases. Evaluation SHALL report raw and
semantic precision/recall plus review burden but SHALL NOT create a CI failure threshold.

#### Scenario: fixture suite executes without network
- **WHEN** acceptance fixtures run in unit or integration tests
- **THEN** they use a fake semantic provider and require no API credential

#### Scenario: live smoke test
- **WHEN** a developer explicitly enables the live smoke test and supplies a credential
- **THEN** only the named fixture source is transmitted and the test is otherwise skipped

#### Scenario: evaluation report
- **WHEN** the complete fixture corpus is evaluated
- **THEN** the report states fixture counts, raw precision/recall, ranked precision/recall,
  uncertain-review burden, provider/model/question version, and limitations
