# ADR-0048: Shared host foundation registration

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#56](https://github.com/DanMarshall909/roslyn-tools/issues/56), [#53](https://github.com/DanMarshall909/roslyn-tools/issues/53)

## Context

The CLI `Bootstrap` registered reusable workspace, durability, trust, and
doctor services before its own command and feedback services. MCP invoked the
same CLI method to obtain these foundations, even though their types now live
outside the CLI assembly.

## Decision

Move the foundation registrations, in the same order and with the same
lifetimes and factories, into `HostServiceRegistration` in
`RoslynTools.Hosting`. CLI `Bootstrap` calls that method at its former location
in the registration sequence. Pin the shared method's assembly and key service
descriptors in an architecture test.

## Consequences

- A peer host can register workspace, durable state, trust, and doctor ports
  without referencing CLI code.
- CLI service graph construction remains equivalent because the moved block
  occupies the same point in its registration sequence.
- Daemon/application infrastructure registration moved into Hosting in the
  next #56 slice. Core service scanning, CLI feedback and command descriptors,
  and the suggestion catalogue still live in CLI `Bootstrap`.

## Alternatives considered

- Move the entire CLI bootstrap at once: rejected because it still contains
  CLI-only command, feedback, daemon-client, and suggestion dependencies.
