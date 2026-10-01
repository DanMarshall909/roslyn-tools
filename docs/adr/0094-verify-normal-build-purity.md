# ADR-0094: Verify normal restore, build, and test purity

- Status: Accepted
- Date: 2026-10-01
- Related issue: [RT-016](../issues/open/architecture/rt-016-pre-release-product-spine.md)
- Extends: [ADR-0093](0093-remove-implicit-debug-build-writes.md)

## Context

Removing the three implicit Debug hooks stopped observed documentation and
profile writes, but a clean Git status alone could miss rewrites of ignored
files or user-profile deployment. The developer profile also contains persisted
repository trust, so tests must not depend on that ambient state.

## Decision

Run `node scripts/check-build-purity.mjs` in a dedicated Linux CI job. For each
plain `dotnet restore`, Debug `dotnet build --no-restore`, and
`dotnet test --no-build` phase, compare checkout file content, timestamps, and
type with a before snapshot and compare the disposable profile apart from the
SDK certificate-revocation-list cache. Report phase and path for created,
removed, or modified entries, including when the
command fails. Keep the SDK's one-time certificate and NuGet migration bootstrap
outside the measured phases. Route NuGet packages and other declared caches and
temporary files into a disposable cache. Exempt normal build outputs and reject
any tracked file beneath an exempt directory.

The spawned MCP catalogue test grants repository trust at host startup when it
expects the full catalogue. Untrusted startup tests continue to exercise the
restricted surface without relying on the developer's trust registry.

## Consequences

- The detector self-tests prove that an ignored same-content rewrite, a profile
  skill write, and a write before command failure produce a failing result. A
  real Linux run left the monitored checkout and profile paths unchanged across
  all three phases, with 252 MCP and 3,338 Core tests passing.
- `bin`, `obj`, and other declared output directories, disposable caches, and
  temp files and the SDK's certificate-revocation-list cache are outside the
  snapshot. This is side-effect evidence, not proof that every workspace build
  input is known or that builds are reproducible. Before/after snapshots do not
  detect a transient write that is completely removed before a phase exits.
- Windows special-folder resolution and macOS filesystem behavior need native
  evidence under the P1.8 OS matrix before claiming cross-platform purity.

## References

- [Agent playbook](../agent-playbook.md#build-purity)
- [Platform-hardening OpenSpec](../../openspec/changes/critical-platform-hardening/specs/platform-hardening/spec.md)
