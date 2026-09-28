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
wait_for_ready() {
    log=$1
    count=0
    while ! grep -q '^ready$' "$log"; do
        sleep 1
        count=$((count + 1))
        [ "$count" -le 10 ] || return 1
    done
    [ -S /proof/socket/authority-v5.sock ]
}
stop_server() {
    pid=$(ps w | awk '$2 == "service" && $0 ~ /authority.py serve/ { print $1 }')
    [ -n "$pid" ]
    kill "$pid"
    sleep 1
}
printf 'kernel '; uname -srmo
printf 'mounts\n'; grep -E ' /proof | /anchor ' /proc/mounts
printf 'service '; id service
printf 'client1 '; id client1
printf 'client2 '; id client2
stat -c '%d %i %a %u:%g %n' /proof /proof/service /proof/service/checkout /proof/service/checkout/.git /proof/service/control /anchor /anchor/authority
sha256sum /proof/authority.py
su service -s /bin/sh -c 'python3 /proof/authority.py provision' >/proof/v5-provision.log
printf 'provisioned separately\n'
su service -s /bin/sh -c 'python3 /proof/authority.py serve' >/proof/v5-server.log 2>&1 &
wait_for_ready /proof/v5-server.log
record before-enrollment su client1 -s /bin/sh -c 'python3 /proof/authority.py call lookup /proof/service/checkout'
record unprovisioned-checkout su client2 -s /bin/sh -c 'python3 /proof/authority.py call enroll /proof/service/other'
su client1 -s /bin/sh -c 'python3 /proof/authority.py call enroll /proof/service/checkout' >/proof/v5-enroll1.log &
p1=$!
su client2 -s /bin/sh -c 'python3 /proof/authority.py call enroll /proof/alias/checkout' >/proof/v5-enroll2.log &
p2=$!
wait "$p1"
wait "$p2"
printf 'enroll-client1 '; cat /proof/v5-enroll1.log
printf 'enroll-client2 '; cat /proof/v5-enroll2.log
repository_id=$(python3 -c 'import json; print(json.load(open("/proof/v5-enroll1.log"))["repositoryId"])')
record conflicting-alias su client1 -s /bin/sh -c 'python3 /proof/authority.py call enroll /proof/alias/checkout wrong-id'
record duplicate-server su service -s /bin/sh -c 'python3 /proof/authority.py serve'
record incumbent-still-active su client2 -s /bin/sh -c 'python3 /proof/authority.py call lookup /proof/service/checkout'
record checkout-write su client1 -s /bin/sh -c 'touch /proof/service/checkout/src/injected.cs'
record control-write su client2 -s /bin/sh -c 'touch /proof/service/control/forged'
record anchor-write su client1 -s /bin/sh -c 'touch /anchor/authority/forged'
cp /proof/service/control/registry-v5.json /proof/v5-snapshot-registry.json
record barrier su service -s /bin/sh -c "python3 /proof/authority.py call barrier ignored $repository_id"
record barrier-visible su client2 -s /bin/sh -c 'python3 /proof/authority.py call lookup /proof/service/checkout'
record admit-blocked su client1 -s /bin/sh -c 'python3 /proof/authority.py call admit /proof/service/checkout'
cp /proof/v5-snapshot-registry.json /proof/service/control/registry-v5.json
record stale-client1 su client1 -s /bin/sh -c 'python3 /proof/authority.py call lookup /proof/service/checkout'
record stale-client2 su client2 -s /bin/sh -c 'python3 /proof/authority.py call enroll /proof/alias/checkout'
python3 -c 'import json; print("registry-seq",json.load(open("/proof/service/control/registry-v5.json"))["sequence"],"anchor-seq",json.load(open("/anchor/authority/anchor-v5.json"))["sequence"])'
stop_server
su service -s /bin/sh -c 'python3 /proof/authority.py serve' >/proof/v5-server-restart.log 2>&1 &
wait_for_ready /proof/v5-server-restart.log
record restart-client1 su client1 -s /bin/sh -c 'python3 /proof/authority.py call lookup /proof/service/checkout'
record restart-client2 su client2 -s /bin/sh -c 'python3 /proof/authority.py call lookup /proof/service/checkout'
mv /proof/service/control/registry-v5.json /proof/service/control/registry-v5.missing
record missing-registry su client1 -s /bin/sh -c 'python3 /proof/authority.py call enroll /proof/service/checkout'
record reprovision-refused su service -s /bin/sh -c 'python3 /proof/authority.py provision'
mv /proof/service/control/registry-v5.missing /proof/service/control/registry-v5.json
stop_server
record unreachable-client1 su client1 -s /bin/sh -c 'python3 /proof/authority.py call lookup /proof/service/checkout'
record unreachable-client2 su client2 -s /bin/sh -c 'python3 /proof/authority.py call enroll /proof/service/checkout'
printf 'done\n'
