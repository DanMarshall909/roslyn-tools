#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
repo_root="$(dirname "$script_dir")"

fail() {
    echo "distribution-test: $1" >&2
    exit 1
}

require_file() {
    [ -f "$repo_root/$1" ] || fail "missing $1"
}

require_file "scripts/install.sh"
require_file "scripts/install.ps1"
require_file "server.json"
require_file ".github/workflows/release.yml"

grep -q '<SelfContained>true</SelfContained>' "$repo_root/src/RoslynTools.Mcp/RoslynTools.Mcp.csproj" ||
    fail "MCP publish is not self-contained"
grep -q '<IncludeAllContentForSelfExtract>true</IncludeAllContentForSelfExtract>' "$repo_root/src/RoslynTools.Mcp/RoslynTools.Mcp.csproj" ||
    fail "MCP publish does not preserve Roslyn build-host files"
grep -q 'roslyn-tools-mcp' "$repo_root/server.json" ||
    fail "server.json does not advertise the stable MCP launcher"

echo "distribution-test: OK" >&2
