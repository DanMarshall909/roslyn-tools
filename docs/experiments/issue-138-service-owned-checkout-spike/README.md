# Issue #138: service-owned checkout exclusion spike

- Date: 2026-09-28
- Parent: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123)
- Owner: [#138](https://github.com/DanMarshall909/roslyn-tools/issues/138)
- Result: **revise the enrollment requirement; continue the broker proof**

## Bounded question and decision rule

On one disposable Linux x64/ext4 guest, can a distinct, unprivileged editor
principal mutate a freshly created service-owned checkout or protected control
root at five paused points of a two-file rename and rollback? The spike stops
after the five pauses and two negative controls for previously open access. One
successful editor mutation during the five pauses rejects this static ownership
candidate. Complete denial only permits the larger effect-boundary proof; it
does not admit a supported Apply platform.

The answer for this guest was 70 denials across five pauses. Two negative
controls then showed that `chown` and `mv` of an editor-origin file do **not**
revoke a previously opened writable descriptor or mapping. Enrollment must
construct fresh service-owned inodes that the editor has never been able to
open for writing. Importing an existing editor-owned checkout by changing its
ownership or moving its files is excluded. A production broker and its
admission/fencing proof remain unbuilt.

## Environment and trust boundary

| Property | Observed value |
| --- | --- |
| Guest | Alpine Linux 3.24.2 virt x86_64; Linux `6.18.52-0-virt` |
| Boot image | `alpine-virt-3.24.2-x86_64.iso`, SHA-256 `3ab424762af704b2c2a9e57df1dc37f982af260071504d977f2fb96822e7130b` |
| Hypervisor | QEMU x86_64 TCG, 1 GiB guest RAM, virtio block image in host `/dev/shm` |
| Guest filesystem | `/dev/vda` mounted at `/proof`, ext4 `rw,relatime` |
| Service principal | UID/GID 2001, sole group `service` |
| Editor principal | UID/GID 2002, sole group `editor` |
| Control root | `/proof/service/control`, service-owned `0700` |
| Checkout and Git common directory | `/proof/service/checkout` and its `.git`, service-owned `0755` |
| Source directory and files | `src` service-owned `0755`; three source files service-owned `0644` |
| Ancestors | `/proof` root-owned `0755`; `/proof/service` service-owned `0755` |
| Git base | `30fcec6b3d684412237a15e1e19c4ff6d4067fb1` |
| Kernel link setting | `fs.protected_hardlinks = 1` |

Guest root and the hypervisor host were trusted administrators outside the
editor threat model. The editor account had no guest `sudo` or Docker socket.
The development host itself has root-equivalent Docker access and does not
qualify as the proposed editor principal. This VM was an isolated experiment,
not a deployment support matrix or physical power-loss test.

To replay the guest shape, verify the Alpine ISO checksum from the official
[release directory](https://dl-cdn.alpinelinux.org/alpine/v3.24/releases/x86_64/),
create a disposable virtio disk, and boot it with:

```sh
qemu-system-x86_64 -accel tcg -m 1024 -smp 2 -nographic -no-reboot -boot d \
  -cdrom alpine-virt-3.24.2-x86_64.iso \
  -drive file=effect-ext4.qcow2,format=qcow2,if=virtio \
  -nic user,model=virtio-net-pci
```

In the guest, create ext4 on `/dev/vda`, mount it at `/proof`, add the two
UIDs above, and install `git`, `e2fsprogs`, and `python3`. Create the protected
directory modes in the table. As `service`, initialize a Git repository at
`/proof/service/checkout`, create and commit the three listed source files,
then run `probe-service.sh` as `service`. Copy `attack.sh` and `map-worker.py`
to the paths hardcoded in those files. At each checkpoint, run
`su editor -s /bin/sh -c '/proof/attack.sh <stage>'`, save its output, then
create `/proof/service/control/go-<stage>` to advance. Run the two negative
controls only after the outcome checkpoint. This recipe reconstructs the
one-off guest experiment; it is not an automated broker conformance harness.

## Procedure and observations

The checkout and all three tracked source files were created by the service
principal, with initial bytes `class A;\n`, `class B;\n`, and
`class Dependency;\n`. The service ran [probe-service.sh](probe-service.sh): it
recorded the base revision and [three SHA-256 values](baseline.sha256), paused,
verified all inputs, moved `A.cs` to `A2.cs`, paused, moved `B.cs` to `B2.cs`,
paused, restored both names, verified all inputs again, flushed and published
[`RolledBack`](outcome), and paused once more. The first pause is called
`guard` in the script solely to match the issue's checkpoint vocabulary: **no
effect broker or dynamic guard was acquired**. Static ownership and directory
permissions were the candidate boundary tested here.

At each of `guard`, `preflight`, `first-effect`, `rollback`, and `outcome`, guest
root launched [attack.sh](attack.sh) as UID 2002. It tried fresh writes and
pre-opening against both affected files and the read dependency, unlink,
creation, mode/mtime edits, checkout replacement, Git common-directory and
control-root writes, and hard-link/symlink routes. Each attempt exited with
denial. The exact logs are
[guard](guard-attack.log), [preflight](preflight-attack.log),
[first effect](first-effect-attack.log), [rollback](rollback-attack.log), and
[outcome](outcome-attack.log). Each has one identity line and 14 `DENIED`
lines, with no `BYPASS` line. The initial interactive BusyBox `rm` attempt
waited for confirmation and returned no result; it was changed to `rm -f`
before these five final logs were recorded.

The [preflight](preflight.log) and [rollback](rollback.log) logs verify all
three hashes. Before the negative controls, `git status --porcelain` as the
service principal was empty and the base commit was unchanged. The control
root had no forged file. The final `RolledBack` marker is evidence of this
script's own completion only; it is not a durable product lifecycle claim.

Afterward, two separate negative controls deliberately violated the
fresh-inode premise:

1. The editor opened `borrowed.cs` for append in its own directory and held
   file descriptor 3. Guest root changed the file owner to service and moved
   it into `checkout/src`. A fresh editor append returned permission denied,
   as [captured](direct-write-error.log), but `printf` through descriptor 3
   appended `via-open-fd` to the file.
2. The editor opened and mapped a 4096-byte `mapped.cs` using
   [map-worker.py](map-worker.py). Guest root changed the owner to service and
   moved it into `checkout/src`. A fresh editor append returned permission
   denied, as [captured](map-direct-error.log), but writing through the old
   mapping changed its first four bytes to `EDIT`.

These controls are a counterexample to ownership-transfer enrollment. They
do not show a bypass against files created from the beginning by the service.

## What this does and does not establish

Static service ownership excluded the tested new access and path mutations
under the exact guest assumptions above. The result supports pursuing a
freshly provisioned service-owned checkout. It leaves open how enrollment
proves that no editor-writable handle, mapping, inode alias, or path to the
checkout was inherited. A broker must also prove durable one-use intent,
shared repository identity, fencing epoch, lease, complete intermediate
state, actual effect-boundary checks, guarded rollback, and stale-owner
rejection. This script tested none of those. It used controlled pauses rather
than concurrent racing and did not simulate process crash or power loss.

**Decision:** revise the proposed support candidate to require fresh
service-created inodes and protected ancestry from enrollment onward; continue
the full adversarial proof in #138. Keep production Apply unavailable.
