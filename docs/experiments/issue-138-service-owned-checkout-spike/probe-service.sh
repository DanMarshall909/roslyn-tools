#!/bin/sh
set -eu
checkout=/proof/service/checkout
control=/proof/service/control
pause_at() {
    stage=$1
    printf '%s\n' "$stage" > "$control/checkpoint.tmp"
    mv "$control/checkpoint.tmp" "$control/checkpoint"
    while [ ! -e "$control/go-$stage" ]; do sleep 1; done
}
cd "$checkout"
git rev-parse HEAD > "$control/base-revision"
sha256sum src/A.cs src/B.cs src/Dependency.cs > "$control/baseline.sha256"
pause_at guard
sha256sum -c "$control/baseline.sha256" > "$control/preflight.log"
pause_at preflight
mv src/A.cs src/A2.cs
pause_at first-effect
mv src/B.cs src/B2.cs
pause_at rollback
mv src/B2.cs src/B.cs
mv src/A2.cs src/A.cs
sha256sum -c "$control/baseline.sha256" > "$control/rollback.log"
printf 'RolledBack\n' > "$control/outcome.tmp"
sync
mv "$control/outcome.tmp" "$control/outcome"
sync
pause_at outcome
