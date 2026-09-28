# Issue #139: local service authority continuity spike

- Date: 2026-09-28
- Parent: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123)
- Owner: [#139](https://github.com/DanMarshall909/roslyn-tools/issues/139)
- Result: **retain this local authority candidate for further design; do not
  admit it as a production store**

## Bounded question

Can a separately provisioned service authority give two unprivileged request
principals one repository identity and recovery barrier, and reject an old,
missing, or unreachable registry without minting another ID? This experiment
uses the accepted first-release shape: the service is the sole checkout writer;
the two clients may request enrollment and status but cannot write the checkout
or authority roots. A second live service, conflicting enrollment, admission
through a barrier, or fresh ID after a selective registry restore would reject
the candidate. The experiment stops after one local guest, one registry
restore, and one process exit between the registry and anchor publications.

The observed candidate passed those cases under the exact guest assumptions
below. The separate anchor volume was **retained** while the registry was
restored. A whole-VM snapshot that rolls both volumes back together remains
uncovered and would defeat this high-water check. This is a bounded prototype,
not #139 acceptance or a production coordination mode.

## Guest and authority topology

| Property | Observed value |
| --- | --- |
| Guest | Alpine Linux 3.24.2 x86_64, kernel `6.18.52-0-virt`; QEMU TCG |
| Registry/checkout volume | `/dev/vda` mounted at `/proof`, ext4 `rw,relatime` |
| Independent selective-restore anchor | `/dev/vdb` mounted at `/anchor`, ext4 `rw,relatime` |
| Trusted service | UID/GID 2001, sole group `service`; only effect writer |
| Request principals | UID/GID 2002 `client1` and 2003 `client2`, each in its sole group |
| Checkout and Git common directory | `/proof/service/checkout` and `.git`, service-owned `0755` |
| Registry root | `/proof/service/control`, service-owned `0700` |
| Anchor root | `/anchor/authority`, service-owned `0700` |
| Socket directory | `/proof/socket`, service-owned `0711`; socket `0666` for authenticated connect |
| Ancestors | `/proof` and `/anchor` root-owned `0755`; `/proof/service` service-owned `0755` |

Guest root and the hypervisor host are trusted administrators; they can change
both volumes and are outside the request-principal threat model. The request
principals had no guest root, `sudo`, Docker socket, or write access to the
checkout, control root, or anchor root. This does not make the development
host's logged-in Docker-group account an eligible request principal.

The disposable [authority-v5.py](authority-v5.py) takes client UID from Linux
`SO_PEERCRED` on a connected Unix stream socket. It has one private decision
point for provisioning, enrollment, lookup, synthetic admission, and setting
the recovery barrier. The server opens the checkout and its `.git` directory
with `O_PATH | O_DIRECTORY | O_NOFOLLOW`, derives device/inode identities from
held handles, checks service ownership and protected canonical ancestors, and
pins those identities during separate provisioning. An alias through a
root-owned parent symlink reaches the same physical objects; an unprovisioned
checkout is refused.

Provisioning and serving hold a nonblocking `flock` on a service-owned `0600`
file beneath the protected control root. A second service process was refused
before it could replace the Unix socket, while the first continued answering.
The [observed lock identities and modes](v5-v6-lock-stat.log) are retained.
This is a singleton among cooperating processes of the trusted service UID;
it is not a cross-host lock or an exclusion barrier against a compromised
service principal.

The registry and second-volume anchor contain the same random instance nonce
and monotonic sequence. The anchor also contains a SHA-256 digest of the
canonical registry bytes. The server writes each file through a temporary
file, `fsync`s that file, atomically replaces the path, and `fsync`s its parent
directory. It verifies both records on every request. A mismatch, absence, or
unreachable server returns `RejectedUnsafeCoordination` with a nonzero client
exit. The digest is **not a MAC** and the nonce is **not a secret**: this design
depends on OS separation from the request principals, not cryptographic
protection against a compromised service or administrator.

Linux documents [`SO_PEERCRED`](https://man7.org/linux/man-pages/man7/unix.7.html)
for connected Unix sockets and the need to
[`fsync` a parent directory](https://man7.org/linux/man-pages/man2/fsync.2.html)
for a durable directory entry. The calls in the prototype were observed; no
physical power-loss or disk-cache honesty test was performed.

## Replay

The scripts and the [full v5 trace](v5-trace.log) are retained here. Use
the Alpine 3.24.2 virt ISO with SHA-256
`3ab424762af704b2c2a9e57df1dc37f982af260071504d977f2fb96822e7130b`.
Boot a disposable guest with two virtio disks, create ext4 on each, mount them
as above, and install `e2fsprogs`, `git`, and `python3`. For example:

```sh
qemu-system-x86_64 -accel tcg -m 1024 -smp 2 -nographic -no-reboot -boot d \
  -cdrom alpine-virt-3.24.2-x86_64.iso \
  -drive file=registry.qcow2,format=qcow2,if=virtio \
  -drive file=anchor.qcow2,format=qcow2,if=virtio \
  -nic user,model=virtio-net-pci
```

Create users 2001–2003, the directories and permissions in the table, one
service-created Git checkout at `/proof/service/checkout` with `src/A.cs`
containing `class A;\n`, and another service-owned Git directory at
`/proof/service/other`. As guest root, create `/proof/alias` as a symlink to
`/proof/service`; the clients cannot change `/proof`. Copy `authority-v5.py`
to `/proof/authority.py` and [run-v5.sh](run-v5.sh) to `/proof/run-v5.sh`, then
run `sh /proof/run-v5.sh` as guest root. The runner explicitly provisions
before serving and stops the server after its checks. It logs the result and
exit code for each call. It does not exercise a repository effect.

For the interrupted publication, copy
[authority-v6.py](authority-v6.py) to `/proof/authority-v6.py` and
[run-v6-crash.sh](run-v6-crash.sh) to `/proof/run-v6-crash.sh` in the same
guest. The v6 script uses fresh registry, anchor, lock, and socket paths. Run
`sh /proof/run-v6-crash.sh` as guest root. It starts the service with
`RT_PROOF_CRASH_AFTER_REGISTRY=1`, which exits with code 73 after flushing the
new registry entry but before advancing the anchor, then restarts the service
without that variable. The [exact trace](v6-trace.log) shows
`RejectedUnsafeCoordination` (exit 2) for the interrupted enrollment and
both later requests. The service process exited 73; the flushed
[registry](v6-registry-after-crash.json) had sequence 1 while the retained
[anchor](v6-anchor-after-crash.json) had sequence 0. The tracked `src/A.cs`
still matched [its baseline hash](v6-baseline.sha256).

## Result and limits

The [v5 trace](v5-trace.log) records `RepositoryNotEnrolled` before explicit
enrollment; two concurrent clients observing one ID and generation; denial of
an unprovisioned checkout, conflicting alias, and duplicate live service;
continued response by the incumbent; direct write denial; a
synthetic `RecoveryRequired` barrier visible to the other client before
admission; rejection after the [registry snapshot](v5-registry-before-rollback.json)
was restored while the [sequence-2 anchor](v5-anchor-after-barrier.json)
remained; continued rejection after service restart, missing registry, and
unreachable service. The server did not automatically reprovision. The v6
interrupted-publication cut also failed closed after restart without changing
repository bytes.

**Decision:** a single service principal with authenticated local requesters
and an independently retained high-water anchor is a plausible first-release
coordination primitive. Continue #139 with production authority design,
protected-root and singleton-lock validation, recovery of interrupted
two-record publication,
non-reused live/retired identity capacity, fenced transfer and retirement, and
conformance tests. A joint rollback of both guest volumes, compromised
service/admin, host migration, two-host operation, and physical power loss are
not covered. The prototype has no real repository effect or product lifecycle
integration. Keep production Apply unavailable and #141 blocked on #139.
