# Maintainer: devalexandre <alexandre@dev2learn.com>
# Lingmo fork only (see LINGMO.md): the FileAction conversions as a command-line engine for the
# Lingmo desktop (lingmo-fileaction drives it). Built from this checkout with `dotnet publish`,
# framework-dependent: the .NET runtime comes from Arch's dotnet-runtime (security updates by pacman).
#
# scripts/mkpkg.sh copies this file alone into packaging/work/<pkg>/ and stamps pkgver; the source
# is then found next to it in the workspace (../../../lingmo-fileaction-engine), or set
# LINGMO_FILEACTION_SRC=/path/to/checkout. Standalone: `makepkg -f` in this directory.
pkgname=lingmo-fileaction-engine
pkgver=1.13.1
pkgrel=1
pkgdesc="FileAction conversion engine for Lingmo: images, audio, video, PDF, documents, spreadsheets, subtitles, archives"
arch=('x86_64')
url="https://github.com/ArchLingmo/lingmo-fileaction-engine"
license=('MIT')
# The image, PDF and archive code (Magick.NET, PDFium, SharpCompress) is bundled and needs only glibc;
# SkiaSharp looks fonts up through fontconfig. FFmpeg does audio and video.
depends=('dotnet-runtime>=10' 'ffmpeg' 'fontconfig' 'glibc' 'gcc-libs')
makedepends=('dotnet-sdk>=10')
optdepends=(
  'pandoc-cli: documents (DOCX, ODT, Markdown, HTML, EPUB, RTF)'
  'libreoffice-fresh: DOC, PPT/PPTX, ODP, ODS and Word/spreadsheets to PDF'
  'tesseract: extract text from images and scanned PDFs (OCR)'
  'tesseract-data-por: Portuguese OCR'
  'chromium: Markdown and web pages to PDF without LibreOffice'
)
options=('!strip' '!debug')

_srcroot() {
  local d
  for d in "${LINGMO_FILEACTION_SRC:-}" "$startdir" "$startdir/../../../lingmo-fileaction-engine"; do
    if [ -n "$d" ] && [ -f "$d/src/FileAction.Lingmo/FileAction.Lingmo.csproj" ]; then
      (cd "$d" && pwd)
      return 0
    fi
  done
  echo "lingmo-fileaction-engine source not found (set LINGMO_FILEACTION_SRC)" >&2
  return 1
}

build() {
  local src dotnet
  src=$(_srcroot)
  # Arch's dotnet-sdk, else a user install (~/.dotnet). /usr/bin/dotnet alone (dotnet-runtime,
  # which lingmo-desktop installs) has no SDK: take the first dotnet that lists one
  dotnet=${DOTNET:-}
  if [ -z "$dotnet" ]; then
    for dotnet in $(command -v dotnet) "$HOME/.dotnet/dotnet"; do
      [ -n "$("$dotnet" --list-sdks 2>/dev/null)" ] && break
    done
  fi
  export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
  "$dotnet" publish "$src/src/FileAction.Lingmo/FileAction.Lingmo.csproj" -c Release \
    -r linux-x64 --self-contained false -p:UseAppHost=false -p:DebugType=none \
    --artifacts-path "$srcdir/artifacts" -o "$srcdir/publish"
}

package() {
  local src
  src=$(_srcroot)
  install -d "$pkgdir/usr/lib/$pkgname" "$pkgdir/usr/bin"
  cp -a "$srcdir/publish/." "$pkgdir/usr/lib/$pkgname/"
  find "$pkgdir/usr/lib/$pkgname" -name '*.pdb' -delete
  chmod 644 "$pkgdir/usr/lib/$pkgname"/*.so
  install -Dm755 /dev/stdin "$pkgdir/usr/bin/$pkgname" <<'SH'
#!/bin/sh
# FileAction engine for Lingmo (lingmo-fileaction-engine list|run|catalog [--json])
exec /usr/bin/dotnet /usr/lib/lingmo-fileaction-engine/lingmo-fileaction-engine.dll "$@"
SH
  install -Dm644 "$src/LICENSE" "$pkgdir/usr/share/licenses/$pkgname/LICENSE"
  install -Dm644 "$src/THIRD-PARTY-NOTICES.md" "$pkgdir/usr/share/licenses/$pkgname/THIRD-PARTY-NOTICES.md"
  install -Dm644 "$src/LINGMO.md" "$pkgdir/usr/share/doc/$pkgname/LINGMO.md"
}
