#!/bin/sh
set -u
stage=$1
checkout=/proof/service/checkout
target=$checkout/src/A.cs
[ -e "$target" ] || target=$checkout/src/A2.cs
second=$checkout/src/B.cs
[ -e "$second" ] || second=$checkout/src/B2.cs
attempt() {
    label=$1
    shift
    if "$@" >/dev/null 2>&1; then
        printf 'BYPASS %s %s\n' "$stage" "$label"
    else
        printf 'DENIED %s %s\n' "$stage" "$label"
    fi
}
printf 'ACTOR %s ' "$stage"
id
attempt target-append sh -c "printf X >> '$target'"
attempt target-preopen sh -c "exec 3>> '$target'"
attempt second-append sh -c "printf X >> '$second'"
attempt dependency-append sh -c "printf X >> '$checkout/src/Dependency.cs'"
attempt target-unlink rm -f "$target"
attempt create touch "$checkout/src/Injected.cs"
attempt target-chmod chmod 0666 "$target"
attempt target-mtime touch -m "$target"
attempt checkout-replace mv "$checkout" /proof/service/replaced
attempt common-dir-write sh -c "printf X >> '$checkout/.git/config'"
attempt control-forge touch /proof/service/control/forged
attempt hardlink ln "$target" /proof/editor-home/hardlink
ln -s "$target" /proof/editor-home/link
attempt symlink-write sh -c "printf X >> /proof/editor-home/link"
rm -f /proof/editor-home/link
attempt symlink-swap ln -s /proof/editor-home "$checkout/src/swap"
