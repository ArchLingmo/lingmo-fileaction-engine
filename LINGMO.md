# Lingmo fork of FileAction

This repository is [FileAction](https://github.com/Metamorfolog/file_action) (MIT, by Metamorfolog, inspired by
[Tangerine](https://tangerineformac.com/) for macOS) with a few additions so the Lingmo desktop can use its
conversions. Upstream is merged regularly, so every Lingmo change is a **new file**; no upstream file is edited.

The Lingmo side (the radial wheel in QML, the drag-and-tap gesture on X11, the Island progress, the file
manager entry and the app actions) lives in [lingmo-fileaction](https://github.com/ArchLingmo/lingmo-fileaction).
This fork only provides the engine, packaged as `lingmo-fileaction-engine`.

## What the fork adds

| File | Why |
|---|---|
| `src/FileAction.Lingmo/FileAction.Lingmo.csproj` | A console project (`lingmo-fileaction-engine`) that references `FileAction.Core` and links four files of `FileAction.Linux` (settings, presets list, tesseract OCR, recent results) instead of copying them. No Avalonia, no window. |
| `src/FileAction.Lingmo/Program.cs` | The command line with machine-readable output (`--json`) for `list`, `run` and `catalog`, cancellation by SIGTERM/SIGINT, exit codes. |
| `PKGBUILD` | Builds the package from this checkout with `dotnet publish` (framework-dependent, `linux-x64`). |
| `LINGMO.md` | This file. |

Nothing else changed: `FileAction.sln` does not list the new project (build it by path), and the Windows app,
the Avalonia app, the tests and the CI are upstream's.

### Why a new console project instead of the Avalonia app's CLI

`FileAction.Linux` already has `fileaction list/run`, but it is one executable with the Avalonia window:
publishing it pulls Avalonia, its Skia/HarfBuzz natives and the Inter font, and the CLI mode has no JSON.
A separate project is smaller (75 MB installed for `linux-x64`, most of it Magick.NET's ImageMagick, PDFium and
SkiaSharp, which Core needs anyway), starts without UI libraries, and adding `--json` there needs no edit to
upstream's `Cli.cs`. Upstream's settings (`~/.config/fileaction/settings.json`: presets,
quality, output folder) are read through the linked `LinuxSettings.cs`, so presets made in upstream's
settings window show up in Lingmo too.

### Settings written by the Lingmo app

The Lingmo settings window (`lingmo-fileaction --settings`) writes upstream's `~/.config/fileaction/settings.json`
directly, key by key, keeping every other key: `OutputDirectory`, `Quality` (`JpegQuality`, `CompressQuality`,
`PdfDpi`, `VideoCrf`, `CompressVideoCrf`, `StripLocation`), `HiddenOperations` and `FavoriteOperations`. No engine
change was needed (`run` already passes `OutputDirectory` and `Quality` to Core; the Lingmo wheel applies the hidden
and favourite lists itself, like upstream's `WheelLayout`). If upstream renames one of these properties, update
`src/prefs.cpp` in lingmo-fileaction.

## Command line

```
lingmo-fileaction-engine list [--json] FILE...
lingmo-fileaction-engine run [--json] ID FILE... [--param KEY=VALUE]... [--out DIR]
lingmo-fileaction-engine catalog [--json]
lingmo-fileaction-engine --version
```

`list --json` prints one document:

```json
{"engine":"1.13.1",
 "files":[{"path":"/home/me/photo.png","name":"photo.png","format":"png","directory":false}],
 "wheels":{"convert":[{"id":"image.to.webp","wheel":"convert","label":"WEBP","subtitle":null,"target":"webp",
                       "preset":false,"available":true,"missing":[],"parameters":[]}, …],
           "tools":[…]}}
```

Operations whose helper program is missing are listed with `"available": false` and `"missing": ["Pandoc"]`, so
the wheel can show them dimmed. `catalog --json` lists every operation and which helper programs are found.

`run --json` prints one JSON object per line, as things happen:

```
{"event":"start","id":"video.to.gif","count":1}
{"event":"progress","value":0.42}
{"event":"done","outputs":["/home/me/clip.gif"],"message":null,"note":null}
{"event":"error","code":"failed|unavailable|nothing|cancelled|usage","message":"…","nothingToDo":false}
```

Exit codes: 0 done, 1 failed, 2 bad arguments, 3 helper program missing, 4 nothing to do (e.g. the image is
already small), 130 cancelled. SIGTERM or SIGINT cancels: Core's cancellation token stops FFmpeg/LibreOffice
and removes the partial `.fa-*.part.*` file. Results go next to the original with a free name
(`photo (2).webp`), never over an existing file (upstream `OutputPaths.Unique`).

Strings come from upstream's `Loc` (English, or Czech on a Czech system). The Lingmo app translates the slice
names by operation id (pt_BR), so the engine needs no new language.

## Runtime dependencies (what Core really calls)

- `dotnet-runtime` (10): Arch's, not bundled.
- `ffmpeg`: every audio and video operation (`ToolLocator`: `ffmpeg` in PATH).
- Bundled in the NuGet packages and linked only against glibc: ImageMagick (Magick.NET), PDFium (PDFtoImage),
  SkiaSharp; archives are SharpCompress (managed). So the system `imagemagick`, `poppler`, `libarchive` and
  `7zip` are **not** used by the engine and are not dependencies. `fontconfig` is (SkiaSharp fonts).
- Optional: `pandoc-cli` (documents), `libreoffice-fresh` (Office formats, `soffice`), `tesseract` (+ data)
  for OCR, a Chromium-based browser for Markdown/HTML → PDF. Missing ones only dim their slices.

## Build and test

```sh
# package (from the workspace: scripts/mkpkg.sh lingmo-fileaction-engine, or here:)
makepkg -f

# by hand
dotnet publish src/FileAction.Lingmo -c Release -r linux-x64 --self-contained false -p:UseAppHost=false -o out
dotnet out/lingmo-fileaction-engine.dll list --json photo.heic

# upstream's tests: 141/141 pass on Arch with ffmpeg installed and without pandoc/LibreOffice
dotnet test tests/FileAction.Tests
```

## Merging upstream

`git fetch upstream && git merge upstream/main`. The four linked files must keep their names
(`LinuxSettings.cs`, `LinuxOperations.cs`, `TesseractOcr.cs`, `Shell/RecentResults.cs`); if upstream renames or
splits one, fix the `<Compile Include>` lines in `FileAction.Lingmo.csproj`. Bump `<Version>` there and the
version in `packaging/components.conf` of the Lingmo workspace with upstream's release number.
