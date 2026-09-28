# Session: 2026-08-26 — Command Application-Port Migration Patterns

**Goal:** Define one canonical CLI/MCP/daemon application-port pattern, classify every command by migration shape, and write explicit fan-out instructions for converting the complete command catalog.
**Solution:** RoslynTools.slnx

---

## Step 1 — Resume adapter-decoupling design [REAL]

`sed -n '1,420p' ralph/sessions/2026-08-16-adapter-decoupling-outline.md`

The previous session selected `outline` as the typed-operation tracer and left live
SDK verification as its next step. That verification and the broader typed-daemon
slice have since landed in commits `85a6413` and `6707c8d`.

---

## Step 2 — Establish the governing contract [REAL]

`sed -n '1,260p' docs/adr/0003-shared-command-descriptors-and-application-ports.md`

ADR-0003 is authoritative for this migration:

- every ordinary command leaf has one `ApplicationCommandDescriptor`;
- CLI, MCP, and daemon adapters bind the same typed request;
- one `ICommandApplicationPort` owns invocation and returns structured success/error;
- MCP uses the generic descriptor adapter;
- no new command-specific MCP handlers are introduced;
- `CliCommandBridge` is compatibility debt and is deleted when all visible commands
  have ports.

The older native-handler wording in EP-012, ADR-0001, and the target-command ticket
alignment is superseded by ADR-0003. The remaining implementation work is already
tracked by [rt-007](../../docs/issues/resolved/architecture/rt-007-shared-command-descriptors.md),
whose final unchecked criterion is full port coverage and bridge deletion.

---

## Step 3 — Measure the migration surface [REAL]

`dotnet src/RoslynTools.Cli/bin/Release/net10.0/rt.dll dump-help --json | jq length`

```text
55
```

`rg -l 'ICommandApplicationPort|CommandApplicationPort<' src/RoslynTools/Services -g '*.cs' | wc -l`

```text
14
```

The starting inventory is 55 public commands and 14 application-operation files.
The exact descriptor-to-port coverage and group membership will be reconciled before
fan-out, because nested leaves and operational commands do not map one-to-one to
`IRtCommand` implementations.

---

## Step 4 — Group commands by conversion shape [REAL]

Read-only inventory is split across inspect/analyze commands, refactor commands, and
the shared hosting boundary. Every category command now appears exactly once in
`docs/command-port-migration-groups.tsv`:

| Group | Shape | Count |
|---|---|---:|
| R0 | Reconcile existing read-only ports | 10 |
| R1 | Solution inventories | 3 |
| R2 | Service-backed reports | 3 |
| R3 | Static analyzer reports | 3 |
| R4 | Structural source analysis | 2 |
| R5 | File-only trace analysis | 1 |
| M0 | Reconcile mutation tracers and complete rename | 3 |
| M1 | Mechanical one-workspace mutations | 19 |
| M2 | Discriminated multi-mode mutations | 3 |
| M3 | Review-first mutations | 2 |
| M4 | High-impact multi-file/filesystem mutations | 6 |

`suggest` and feedback are typed meta operations outside the 55 category commands.
`daemon`, `prime`, and `dump-help` remain host-owned operational commands.

---

## Step 5 — inventory the command directory with rt [REAL - BLOCKED]

`src/RoslynTools.Cli/bin/Release/net10.0/rt daemon start`

```text
[rt-daemon] PID 1618970 listening on /tmp/CoreFxPipe_rt-daemon
Daemon ready. PID 1618970
```

`src/RoslynTools.Cli/bin/Release/net10.0/rt inspect outline src/RoslynTools.Cli/Commands --solution RoslynTools.slnx --summary --json`

```text
[ERROR] The daemon is unavailable. Start it with `rt daemon start` and retry.
```

`rt daemon status` then reported `STOPPED (stale)`. The process had exited without a
clean stop entry in `daemon.log`.

> **[ISSUE: rt-014]** written — manual `rt daemon start` does not remain resident after its launcher exits on Linux.

The design inventory continues from the machine-readable command catalog and source;
the live outline step remains a verification item after rt-014 is fixed.

---

## Step 6 — Fix the shared boundary before fan-out [REAL]

Source inspection showed that the current descriptor still owns a
`Func<System.CommandLine.Command>`, MCP references the CLI executable, MCP schema
generation instantiates CLI commands, and `CliCommandBridge` captures CLI output.
Moving that machinery into another shared project would rename the coupling rather
than remove it.

ADR-0007 therefore refines ADR-0003:

```text
RoslynTools.Cli ─────┐
                     ├──> RoslynTools definitions + typed ports
RoslynTools.Mcp ─────┘
```

The shared definition owns identity, request/result types, safety, execution
placement, visibility, and suggested actions. CLI owns `System.CommandLine`; MCP owns
JSON-RPC. Both use generic adapters and neither references the other.

The serial foundation is US-143. It must land before parallel command conversion
because it owns the shared definition, result/error base, request-derived schema,
daemon recovery, mutation envelope, prepared change-set, suggested-action contracts,
and coverage ratchets.

---

## Step 7 — Write explicit conversion briefs [REAL]

`docs/command-application-port-migration.md` now gives each R0-R5 and M0-M4 group:

- its exact command list;
- canonical request shapes;
- operation and adapter responsibilities;
- execution placement;
- safety/default semantics;
- command-specific traps; and
- required tests.

Fan-out agents own only assigned command contracts, operations, directly related
services, and focused tests. The integration owner retains shared infrastructure,
ratchets, manifests, schema generation, final bridge deletion, and project-reference
changes to avoid merge hotspots.

The active PRD was aligned with the same pattern. US-095/096/097 are group trackers,
US-098 owns bridge deletion and physical host decoupling, US-143 is the serial
foundation, and the stale native-handler instructions in US-104/105/106/110/136/137
were replaced with generic application-port projection. US-057 and US-099 are closed
as superseded rather than left as competing architectures.

The Linux daemon failure is also tracked as US-144 and
[rt-014](../../docs/issues/open/architecture/rt-014-manual-daemon-start-exits-on-linux.md).

---

## Step 8 — Prove catalog and PRD consistency [REAL]

`dotnet src/RoslynTools.Cli/bin/Release/net10.0/rt.dll dump-help --json`

The live catalog was compared to the checked-in TSV by command and category:

```text
55 /tmp/catalog.tsv
55 /tmp/manifest.tsv
```

`comm -3` produced no differences, and the manifest has no duplicate command names.
Group totals are:

```text
3 M0, 19 M1, 3 M2, 2 M3, 6 M4
10 R0, 3 R1, 3 R2, 3 R3, 2 R4, 1 R5
```

`jq` verified that the PRD is valid JSON, story IDs are unique, and every referenced
epic exists. `git diff --check` passed.

---

## Gaps

- **rt-014 / US-144:** a manually started daemon does not remain resident on Linux,
  so the live typed `outline` parity check could not run.
- The migration foundation and 42 conversion/completion slices are specified, not
  implemented in this session.

No simulated rt commands were needed; all recorded commands are real.

---

## Next session

1. Implement US-143 serially and prove it with one read-only tracer and one mutation
   tracer.
2. Re-run the full local gate and the CLI/MCP/daemon parity matrix.
3. Fan out R0-R5 and M0-M4 with one owner per non-overlapping command batch.
4. Integrate in small batches, shrinking compatibility routing after each green gate.
5. Complete US-098 only after coverage reaches every MCP-visible ordinary command.

---

## Retrospective

The useful design move was classifying by execution and mutation semantics rather
than by CLI category alone. That exposed three cross-cutting contracts that must not
be invented independently during fan-out: request-derived schemas, safe daemon
recovery, and prepared multi-file mutation plans.

Real `rt dump-help` supplied the authoritative inventory. The attempted real
`rt outline` also found a lifecycle regression before implementation began, and that
failure is now captured in both the issue system and PRD.
