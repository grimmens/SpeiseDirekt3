#!/usr/bin/env bash
# Builds SpeiseDirekt3/wwwroot/css/tailwind.css (the back office's Tailwind CSS) with the standalone Tailwind CLI.
#   bash tools/tailwind/build.sh           write the CSS (run it after changing classes in markup, then commit the CSS)
#   bash tools/tailwind/build.sh --check   build to a temp file; exit 1 if the committed CSS differs (CI)
# Windows: tools/tailwind/build.ps1 does the same. Run this file with bash, never as ./build.sh.
# The CLI (pinned version, SHA-256 checked) is downloaded once from the official GitHub release into
# ${XDG_CACHE_HOME:-~/.cache}/speisedirekt3/tailwind/<version>/, never into the repository.
# The repository checks files out with CRLF (.gitattributes); this line re-runs the script without CRs if it has any. #
if [ -z "${SD_TAILWIND_DIR:-}" ]; then SD_TAILWIND_DIR=$(cd "$(dirname "$0")" && pwd); export SD_TAILWIND_DIR; if grep -q $'\r' "$0"; then exec bash <(tr -d '\r' < "$0") "$@"; fi; fi #

set -euo pipefail

version=3.4.17
here=$SD_TAILWIND_DIR
repo=$(cd "$here/../.." && pwd)
output=$repo/SpeiseDirekt3/wwwroot/css/tailwind.css

check=0
case "${1:-}" in
    "") ;;
    --check) check=1 ;;
    *) echo "usage: bash tools/tailwind/build.sh [--check]" >&2; exit 2 ;;
esac

# Platform → release asset and its SHA-256 (sha256sums.txt of the v3.4.17 release)
os=$(uname -s)
arch=$(uname -m)
case "$arch" in
    x86_64|amd64) arch=x64 ;;
    aarch64|arm64) arch=arm64 ;;
    *) echo "Unsupported CPU architecture: $arch" >&2; exit 1 ;;
esac
windows=0
case "$os" in
    Linux) asset=tailwindcss-linux-$arch; cache=${XDG_CACHE_HOME:-$HOME/.cache}/speisedirekt3/tailwind ;;
    Darwin) asset=tailwindcss-macos-$arch; cache=${XDG_CACHE_HOME:-$HOME/.cache}/speisedirekt3/tailwind ;;
    # Git Bash, MSYS2, Cygwin: the cache build.ps1 uses; %LOCALAPPDATA% (C:\Users\...) as a POSIX path for the shell tools
    MINGW*|MSYS*|CYGWIN*) windows=1; asset=tailwindcss-windows-$arch.exe; cache=$(cygpath -u "${LOCALAPPDATA:-$HOME/.cache}")/speisedirekt3/tailwind ;;
    *) echo "Unsupported operating system: $os (on Windows use tools/tailwind/build.ps1)" >&2; exit 1 ;;
esac
case "$asset" in
    tailwindcss-linux-x64) sha256=7d24f7fa191d2193b78cd5f5a42a6093e14409521908529f42d80b11fde1f1d4 ;;
    tailwindcss-linux-arm64) sha256=69b1378b8133192d7d2feb12a116fa12d035594f58db3eff215879e4ad8cf39b ;;
    tailwindcss-macos-x64) sha256=6cbdad74be776c087ffa5e9a057512c54898f9fe8828d3362212dfe32fc933a3 ;;
    tailwindcss-macos-arm64) sha256=a1d0c7985759accca0bf12e51ac1dcbf0f6cf2fffb62e6e0f62d091c477a10a3 ;;
    tailwindcss-windows-x64.exe) sha256=67f1c5e3f5a03406a7bf5badf5ada09b79f3ae78ec43450c15f7e983068da346 ;;
    tailwindcss-windows-arm64.exe) sha256=76f516476784c00f1562160b5758e3d8f0e6c48957efb26b5b50fbdfd76aa382 ;;
esac

# From stdin: given a file name with a backslash, sha256sum escapes its output line and puts a "\" before the hash
sha256_of() {
    if command -v sha256sum > /dev/null; then sha256sum < "$1" | cut -d ' ' -f 1
    else shasum -a 256 < "$1" | cut -d ' ' -f 1
    fi
}

# The Windows CLI is a native program: hand it Windows paths (C:/Users/...). Git Bash would convert /c/... arguments
# itself, but not with MSYS_NO_PATHCONV=1, and Cygwin never does.
native_path() {
    if [ "$windows" = 1 ]; then cygpath -m "$1"; else printf '%s\n' "$1"; fi
}

# Download once, verify every time
dir=$cache/$version
cli=$dir/$asset
if [ ! -f "$cli" ] || [ "$(sha256_of "$cli")" != "$sha256" ]; then
    mkdir -p "$dir"
    url=https://github.com/tailwindlabs/tailwindcss/releases/download/v$version/$asset
    echo "Downloading $url" >&2
    tmp=$(mktemp "$dir/$asset.XXXXXX")
    trap 'rm -f "$tmp"' EXIT
    curl -fsSL --retry 3 -o "$tmp" "$url"
    actual=$(sha256_of "$tmp")
    if [ "$actual" != "$sha256" ]; then
        echo "SHA-256 mismatch for $asset: expected $sha256, got $actual" >&2
        exit 1
    fi
    chmod +x "$tmp"
    mv -f "$tmp" "$cli"
    trap - EXIT
fi

# The browserslist data inside the pinned CLI is as old as the CLI; its warning is noise here
export BROWSERSLIST_IGNORE_OLD_DATA=1

config=$(native_path "$here/tailwind.config.js")
input=$(native_path "$here/input.css")

if [ "$check" = 1 ]; then
    tmp=$(mktemp "${TMPDIR:-/tmp}/tailwind-check.XXXXXX")
    trap 'rm -f "$tmp"' EXIT
    "$cli" -c "$config" -i "$input" -o "$(native_path "$tmp")" --minify
    if ! cmp -s "$tmp" "$output"; then
        echo "SpeiseDirekt3/wwwroot/css/tailwind.css is not what the markup needs (classes changed?)." >&2
        echo "Run 'bash tools/tailwind/build.sh' (Windows: tools/tailwind/build.ps1) and commit the CSS." >&2
        exit 1
    fi
    echo "SpeiseDirekt3/wwwroot/css/tailwind.css is up to date." >&2
else
    "$cli" -c "$config" -i "$input" -o "$(native_path "$output")" --minify
fi
