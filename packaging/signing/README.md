# Code signing

Without a signature, Windows SmartScreen shows "Windows protected your PC – unknown publisher" when the
installer is run. [SignPath Foundation](https://signpath.org) signs open-source projects for free with
its own certificate ("SignPath Foundation" as the publisher name until the project has its own).

## One-time setup (by the project owner)

1. Apply at <https://signpath.org/apply> with the repository `Metamorfolog/file_action`. They check that
   the project is open source (MIT), maintained, and built by CI from public sources – all true here
   (`.github/workflows/build.yml` builds every release).
2. Once accepted, in SignPath:
   - create a project with the slug **`fileaction`**,
   - connect it to the GitHub repository (trusted build system: GitHub.com),
   - add an artifact configuration for a single PE file (the Inno Setup installer `.exe`),
   - create a signing policy with the slug **`release-signing`**,
   - create a CI user and copy its API token.
3. In the GitHub repository settings:
   - **Variables → Actions**: `SIGNPATH_ORGANIZATION_ID` = your SignPath organization id,
   - **Secrets → Actions**: `SIGNPATH_API_TOKEN` = the CI user's token.

That's all: the next release build uploads the installer to SignPath, waits for the signature, checks it
with `Get-AuthenticodeSignature` and publishes the signed installer. Until the variable exists, the
signing steps are skipped and releases work as before.

SmartScreen also builds reputation over downloads, so the warning may still appear for a short while
after the first signed release.
