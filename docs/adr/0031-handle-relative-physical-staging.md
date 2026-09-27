# ADR-0031: Handle-relative physical staging artifact

- Status: Accepted
- Date: 2026-09-28
- Related issues: [#123](https://github.com/DanMarshall909/roslyn-tools/issues/123), [#140](https://github.com/DanMarshall909/roslyn-tools/issues/140)
- Related OpenSpec: [critical platform hardening](../../openspec/changes/critical-platform-hardening/proposal.md)

## Context

ADR-0030 admits a named artifact creation, but the ledger alone neither creates
the artifact nor proves its bytes were durably written. The first physical
staging slice needs an ordering and filesystem boundary that can be exercised
without granting repository mutation authority.

## Decision

An internal writer acquires a configured Linux x64 staging root before it
appends an exact staging intent. It opens the staging and repository directories
component by component without following symlinks, holds the staging directory
handle, checks that it belongs to the service user and is private, and compares
kernel device and mount identities with the repository. The root must already
exist; this operation does not provision it.

With the repository reservation and pre-effect activity held, the writer
durably appends `Intent` and `CreationAdmitted` before it creates the file. It
creates operation directories relative to held directory handles and the
admitted file with exclusive creation and no symlink following. It flushes new
directory entries, including an entry opened after `EEXIST` that another writer
may have just created, and file bytes. It reads the file through the held handle, checks
its hash against the reviewed bytes, then records `Verified`. A failure after
creation admission leaves the admitted event and activity unresolved for
recovery. A missing or unsafe root fails before artifact intent.

## Limits and follow-up

This is an internal staging primitive and is not wired into public Apply.
Its configured root is trusted service configuration; a plan cannot choose a
different root. The handle-relative checks do not themselves establish a
separate, non-bypassable writer principal or protect writable ancestors from
another process with the same privileges. In particular, a caller that can
control a rootful Docker socket can bypass same-host discretionary access
controls. A supported deployment needs a service-owned root under protected
ancestors and a deployment proof that the editor lacks root-equivalent access.

The current worker does not reconcile an existing file after a crash, inventory
backup or guard artifacts, clean up staged files, or enforce one-use tokens at a
cross-process OS effect boundary. #140 and #123 remain open. No repository
effect authority, rollback, recovery, or public Apply is enabled.
