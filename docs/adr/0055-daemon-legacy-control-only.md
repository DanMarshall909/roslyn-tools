# ADR-0055: Daemon legacy envelope is control only

- Status: Accepted
- Date: 2026-09-28
- Related issue: [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)
- Supersedes: The remaining argv receiver described in [ADR-0054](0054-cli-typed-daemon-dispatch-only.md)

## Context

ADR-0054 removed CLI root forwarding of application argv, but the daemon
receiver still deserialized the old envelope, changed the process working
directory, injected forwarded stdin, and executed `App.RunAsync`. A client with
access to the pipe could therefore still turn that envelope into CLI execution
inside a long-lived host. The receiver also logged a prefix of the raw request.

## Decision

The legacy envelope accepts only exact `ping`, `stop`, and `daemon stop`
control arrays. All other arrays receive a nonzero exit response. Application
operations enter the daemon only through the structured typed RPC dispatcher.
The control client can send only `stop`, without accepting arbitrary argv or
stdin. Retain the old envelope fields for stopping a previously started orphan
daemon, but ignore them on the current receiver. Log request length, not raw
payload. Remove the unused pipe console and git-state acquisition path.

## Consequences

- The daemon receiver has no route from legacy argv to CLI parsing, console
  capture, repository working-directory changes, or application execution.
- An older client attempting application argv against a new daemon receives an
  explicit failure. A new client can still stop an older orphan daemon.
- Typed RPC and its existing authentication, trust, and SDK guards remain the
  application boundary. A separate tracer must give the daemon its own host
  composition and ratchet that boundary for #53.

## Alternatives considered

- Keep accepting read-only CLI argv for compatibility: rejected because it
  preserves a second, untyped application route into the long-lived process.
