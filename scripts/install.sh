#!/usr/bin/env bash
set -euo pipefail

version="${ROSLYN_TOOLS_VERSION:-latest}"
base_url="${ROSLYN_TOOLS_BASE_URL:-https://github.com/DanMarshall909/roslyn-tools/releases/download}"
install_root="${XDG_DATA_HOME:-$HOME/.local/share}/roslyn-tools"
bin_dir="${XDG_BIN_HOME:-$HOME/.local/bin}"
uninstall=false

usage() {
    cat <<'EOF'
Usage: install.sh [--version VERSION] [--install-root PATH] [--bin-dir PATH] [--base-url URL] [--uninstall]

Installs the self-contained rt and roslyn-tools-mcp launchers for the current user.
EOF
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --version) version="$2"; shift 2 ;;
        --install-root) install_root="$2"; shift 2 ;;
        --bin-dir) bin_dir="$2"; shift 2 ;;
        --base-url) base_url="$2"; shift 2 ;;
        --uninstall) uninstall=true; shift ;;
        --help|-h) usage; exit 0 ;;
        *) echo "install: unknown argument: $1" >&2; usage >&2; exit 2 ;;
    esac
done

case "$install_root" in
    ""|/|"$HOME") echo "install: refusing unsafe install root '$install_root'" >&2; exit 2 ;;
esac

if [ "$uninstall" = true ]; then
    rm -f "$bin_dir/rt" "$bin_dir/roslyn-tools-mcp"
    rm -rf "$install_root"
    echo "Removed Roslyn Tools from $install_root" >&2
    exit 0
fi

case "$(uname -s)-$(uname -m)" in
    Linux-x86_64) rid="linux-x64" ;;
    Linux-aarch64|Linux-arm64) rid="linux-arm64" ;;
    Darwin-x86_64) rid="osx-x64" ;;
    Darwin-arm64) rid="osx-arm64" ;;
    *) echo "install: unsupported platform $(uname -s)/$(uname -m)" >&2; exit 1 ;;
esac

if [ "$version" = latest ]; then
    release_url="https://api.github.com/repos/DanMarshall909/roslyn-tools/releases/latest"
    version="$(curl -fsSL "$release_url" | sed -n 's/.*"tag_name"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -1)"
    [ -n "$version" ] || { echo "install: could not resolve latest release" >&2; exit 1; }
fi

case "$version" in
    *[!A-Za-z0-9._-]*|"") echo "install: invalid version label '$version'" >&2; exit 2 ;;
esac

archive="roslyn-tools-${version}-${rid}.tar.gz"
download_url="${base_url%/}/${version}/${archive}"
temp_dir="$(mktemp -d)"
trap 'rm -rf "$temp_dir"' EXIT

curl -fsSL "$download_url" -o "$temp_dir/$archive"
curl -fsSL "$download_url.sha256" -o "$temp_dir/$archive.sha256"
(
    cd "$temp_dir"
    sha256sum -c "$archive.sha256"
)

target="$install_root/versions/$version/$rid"
staging="$install_root/versions/.${version}-${rid}.tmp.$$"
mkdir -p "$staging" "$bin_dir"
tar -xzf "$temp_dir/$archive" -C "$staging"
[ -x "$staging/rt" ] || { echo "install: archive does not contain rt" >&2; exit 1; }
[ -x "$staging/roslyn-tools-mcp" ] || { echo "install: archive does not contain roslyn-tools-mcp" >&2; exit 1; }

rm -rf "$target"
mkdir -p "$(dirname "$target")"
mv -f "$staging" "$target"
ln -sfn "$target/rt" "$bin_dir/rt"
ln -sfn "$target/roslyn-tools-mcp" "$bin_dir/roslyn-tools-mcp"

echo "Installed Roslyn Tools $version ($rid)." >&2
echo "Ensure $bin_dir is on PATH." >&2
cat <<'EOF'
{
  "mcpServers": {
    "roslyn-tools": {
      "command": "roslyn-tools-mcp"
    }
  }
}
EOF
