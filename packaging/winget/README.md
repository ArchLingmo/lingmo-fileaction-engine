# winget

FileAction is published to [winget](https://learn.microsoft.com/windows/package-manager/) as
`Metamorfolog.FileAction`, so it installs with:

```
winget install FileAction
```

- `1.9.0/` holds the first manifest, submitted by hand
  ([microsoft/winget-pkgs#456786](https://github.com/microsoft/winget-pkgs/pull/456786)).
  `winget validate --manifest packaging/winget/1.9.0` checks it.
- Later versions are submitted by the **Submit to winget** job in `.github/workflows/build.yml` after
  each release, with Microsoft's `wingetcreate update`. It needs a repository secret `WINGET_TOKEN`:
  a classic personal access token with the `public_repo` scope (wingetcreate forks
  microsoft/winget-pkgs and opens the pull request with it). Without the secret the job skips.
- The installer is per-user Inno Setup (`Scope: user`), so winget's silent install needs no admin rights.
