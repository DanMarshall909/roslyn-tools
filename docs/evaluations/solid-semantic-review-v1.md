# SOLID semantic review V1 evaluation

- Date: 2026-09-18
- Question set: `solid-review-v2`
- Corpus: 30 executable fixtures, six per SOLID principle
- Status: contract-corpus evaluation; no live 30-fixture Jev run was authorized

## Method

The corpus contains 10 likely violations, 11 accepted heuristic exceptions, and nine
borderline or deliberately incomplete cases. The binary precision/recall calculation
excludes the nine uncertain ground-truth cases because they intentionally have no
positive/negative label. Review burden includes all 30 candidates.

The raw heuristic baseline is modeled as flagging every fixture. The semantic contract
result uses the labelled expected classification exercised by the fake provider,
sanitizer, and evidence guard. It is an executable product-contract measurement, not a
claim about live Jev accuracy.

| Stage | Positive queue | TP | FP | FN | Precision | Recall | Uncertain queue | Deprioritized | Human review burden |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Raw deterministic candidates | 30 | 10 | 11 | 0 | 47.6% | 100% | 0 | 0 | 30 |
| Semantic contract ranking | 10 | 10 | 0 | 0 | 100% | 100% | 9 | 11 | 19 |

The apparent perfect semantic row is an oracle/contract upper bound: the fixtures
define the intended ranking and the fake provider returns those labels. Its useful
result is the prospective review-burden shape—11 accepted exceptions can be
deprioritized while nine ambiguous cases remain visible—not evidence that a live model
achieves those metrics.

## False-positive diagnosis policy

Question set `solid-review-v2` addresses a weakness in the original broad Choice: a
relative winner can still be a poor automatic decision, and high Choice confidence only
describes concentration of the supplied options. The request now asks independent
questions about meaningful design harm and accepted exceptions, plus a typed
principle-specific exception reason. Deterministic policy tests establish three safety
properties:

- an accepted-exception signal conflicts with a likely-violation label and routes to
  `uncertain`;
- an unexplained likely-false-positive label routes to `uncertain`;
- an agreed false-positive label, exception signal, and concrete reason remains
  `likely_false_positive` and records why.

These tests validate composition behavior, not live-model accuracy. The `0.60`
automatic-decision thresholds remain provisional until a labelled live run can plot
probabilities against correctness.

## Live evidence available before implementation

Issue #135 records a nine-candidate dogfood spike using Jev 1.13.0 for three OCP, three
DIP, and three ISP candidates. The three OCP cases were ranked as likely closed-set
syntax matching; DIP separated a more plausible concern from composition or operation
boundaries; all ISP cases remained uncertain because exact consumer/member usage was
missing. The total reported cost was below USD 0.01.

That spike was exploratory and lacks a complete labelled confusion matrix, so it is not
combined with the fixture precision/recall table.

## Reproducible live evaluation boundary

`TypeSafeLiveSemanticReviewTests` provides a separately marked live harness. Normal
test runs return before constructing the provider and make zero external calls. A live
run additionally requires both `TYPESAFE_API_KEY` and the explicit
`RUN_TYPESAFE_LIVE_EVAL=1` opt-in. It sends the 30 sanitized fixture snippets as 30
bounded TypeSafe requests and prints expected/actual classifications, confidence,
resolved model, and request IDs.

The live corpus was not transmitted during this change because explicit approval for
that exact bulk run was not granted. Therefore live Jev precision, recall, and cost are
unmeasured and must not be inferred from the contract row. If approved later, record
the resolved model, confusion matrix, uncertain/deprioritized counts, total cost, and
date here without turning the result into a CI threshold.

A separate one-request synthetic smoke passed against Jev 1.13.0 on 2026-09-18. It
verified authentication, Choice parsing, probabilities, confidence, resolved-model
provenance, and the current `x-typesafe-request-id` response header. It sent only a
hard-coded synthetic DIP example and is integration evidence, not an accuracy metric.

## Decision

The V1 tool is worthwhile as optional triage because it preserves full raw evidence,
makes disclosure and request count reviewable, and can reduce review burden without
suppressing candidates. It is not suitable as an authoritative analyzer or required CI
gate until a live labelled evaluation demonstrates stable performance across model and
question-set versions.
