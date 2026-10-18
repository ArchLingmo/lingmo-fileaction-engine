#!/usr/bin/env bash
# Builds <out>/FileAction-<version>-linux-x64.tar.xz from a published app folder and fills in the AUR PKGBUILD.
#   packaging/make-linux-package.sh <version> <publish-dir> [out-dir] [source-url-or-file]
# The last argument overrides where the PKGBUILD downloads the tarball from (CI uses the local file to test it).
set -euo pipefail
version="$1"
publish="$2"
out="${3:-dist}"
root="$(cd "$(dirname "$0")/.." && pwd)"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

mkdir -p "$stage/fileaction/app" "$stage/fileaction/share" "$out"
cp -a "$publish/." "$stage/fileaction/app/"
"$publish/fileaction" integrate --export "$stage/fileaction/share" > /dev/null
cp "$root/src/FileAction.Linux/Assets/fileaction.png" "$stage/fileaction/share/fileaction.png"

cp "$root/LICENSE" "$root/THIRD-PARTY-NOTICES.md" "$stage/fileaction/share/"

tarball="FileAction-$version-linux-x64.tar.xz"
XZ_OPT="-9e -T0" tar -C "$stage" --owner=0 --group=0 -cJf "$out/$tarball" fileaction
sha="$(sha256sum "$out/$tarball" | cut -d' ' -f1)"
source="${4:-$tarball::https://github.com/Metamorfolog/file_action/releases/download/v$version/$tarball}"

license="LicenseRef-AllRightsReserved"
if [ -f "$root/LICENSE" ] && grep -q "MIT License" "$root/LICENSE"; then license="MIT"; fi

sed -e "s|@VERSION@|$version|" \
    -e "s|@SOURCE@|$source|" \
    -e "s|@SHA256@|$sha|" \
    -e "s|@LICENSE@|$license|" \
    "$root/packaging/aur/PKGBUILD.in" > "$out/PKGBUILD"
echo "$out/$tarball"
echo "$out/PKGBUILD"
