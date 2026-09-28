#!/bin/sh
set -eu
record() {
    label=$1
    shift
    set +e
    output=$("$@" 2>&1)
    status=$?
    set -e
    printf '%s rc=%s %s\n' "$label" "$status" "$output"
}
wait_ready() {
    log=$1
    count=0
    while ! grep -q '^ready$' "$log"; do
        sleep 1
        count=$((count + 1))
        [ "$count" -le 10 ] || return 1
    done
}
cd /proof/service/checkout
sha256sum src/A.cs > /proof/v6-baseline.sha256
sha256sum /proof/authority-v6.py
su service -s /bin/sh -c 'python3 /proof/authority-v6.py provision' >/proof/v6-provision.log
printf 'provisioned separately\n'
su service -s /bin/sh -c 'RT_PROOF_CRASH_AFTER_REGISTRY=1 python3 /proof/authority-v6.py serve' >/proof/v6-server.log 2>&1 &
server_job=$!
wait_ready /proof/v6-server.log
record interrupted-enroll su client1 -s /bin/sh -c 'python3 /proof/authority-v6.py call enroll /proof/service/checkout'
set +e
wait "$server_job"
crash_status=$?
set -e
printf 'server-exit rc=%s\n' "$crash_status"
python3 -c 'import json; print("registry-seq",json.load(open("/proof/service/control/registry-v6.json"))["sequence"],"anchor-seq",json.load(open("/anchor/authority/anchor-v6.json"))["sequence"])'
sha256sum -c /proof/v6-baseline.sha256
su service -s /bin/sh -c 'python3 /proof/authority-v6.py serve' >/proof/v6-server-restart.log 2>&1 &
wait_ready /proof/v6-server-restart.log
record restart-client1 su client1 -s /bin/sh -c 'python3 /proof/authority-v6.py call lookup /proof/service/checkout'
record restart-client2 su client2 -s /bin/sh -c 'python3 /proof/authority-v6.py call enroll /proof/alias/checkout'
pid=$(ps w | awk '$2 == "service" && $0 ~ /authority-v6.py serve/ { print $1 }')
[ -n "$pid" ]
kill "$pid"
sleep 1
record offline-reprovision su service -s /bin/sh -c 'python3 /proof/authority-v6.py provision'
printf 'done\n'
