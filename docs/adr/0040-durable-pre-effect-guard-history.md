# ADR-0040: Record guard attempts before acquisition and inactivity before no-effect drain

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123), [#140](https://github.com/DanMarshall909/roslyn-tools/issues/140)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

The internal apply authority kept an admitted pre-effect activity active when
guard acquisition threw before returning a session. After a process interruption,
however, the ledger had no durable distinction between an activity that had not
asked for a guard and one whose guard acquisition or release was uncertain.
Draining either activity from the absence of an effect intent would be unsafe.

## Decision

The ledger records an `ActivityAdmitted` event in the same durable replacement
that admits pre-effect activity. After staging and backup verification, the apply
authority records `AcquisitionIntent` before calling the trusted broker. Broker
acquisition cannot begin if that record fails. A completed null broker result
proves no session remains held; successful session disposal must prove the guard
is inactive and no admitted effect remains in flight. The authority then records
`ConfirmedInactive` before it drains the activity or records terminal no-effect
rejection. An acquisition exception, disposal failure, or failed inactivity
record keeps the admission active and the plan and artifacts pinned.

The journal binds every event to its admission token, control generation, fence
epoch, and reservation token. Restart loading validates sequence, timestamps,
event order, and the active admission set. The only unresolved intent permitted
after first-effect handoff is the one bound to that first effect's fence. A
missing journal on a previously admitted record remains readable, but cannot
be drained or extended as proof of no guard. New admissions reserve space for
their own and all already-active guard outcomes.

## Consequences and follow-up

The durable record now distinguishes no acquisition attempt, a possibly active
guard, confirmed inactivity, and a handed-off effect guard. It does not itself
prove physical guard quiescence; the trusted broker contract supplies that proof.
The production broker remains unavailable. A later #140 slice must reconcile
interrupted owners under a fresh reservation without inferring guard inactivity
from process disappearance. #138 must prove a real broker and supported
platform boundary before effectful Apply can be public; #139 owns shared
authority and durable epoch continuity.

## Alternative considered

Inferring inactivity from an absent effect intent or dead process would permit
cleanup while a guard or admitted effect may still be active. That inference is
rejected.
