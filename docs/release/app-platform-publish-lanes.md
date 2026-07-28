# App Platform Publish Lanes

This matrix is the maintainer map for tester packages. Each app/platform lane must be runnable independently: a Windows package must not depend on a macOS artifact, Linux/macOS previews must not move the Windows latest pointer, and a failed rerun for one platform must not require rebuilding the other platforms unless the operator explicitly chooses an all-platform package run.

In this document, a build lane means the command that creates and validates artifacts. A release publisher means the command or workflow that creates or updates a GitHub Release. Some lanes are hosted publishers today; others intentionally stop at Actions artifacts or local zips that a maintainer uploads to the app-specific prerelease.

| App | Windows | Linux | macOS |
| --- | --- | --- | --- |
| FreeX | `.github/workflows/tester-release.yml` publishes the WPF tester release and stable Windows assets to a FreeX GitHub Release. It is standalone when `include_macos_preview=false`. | `.github/workflows/linux-release.yml` publishes Avalonia `linux-x64` and `linux-arm64` release assets to the draft `freex-linux-v<version>` GitHub Release. | `.github/workflows/macos-app.yml` builds hosted Avalonia `osx-arm64` and `osx-x64` app previews. It creates a macOS prerelease only for `distribution_candidate=true`; otherwise use Actions artifacts, or attach same-commit macOS preview assets from `tester-release.yml` with `include_macos_preview=true`. |
| FreeW | `.github/workflows/freew-release.yml` runs `freew/build/publish-windows.ps1` for the WPF `win-x64` zip and uploads an Actions artifact. | `.github/workflows/freew-linux.yml` builds, smoke-tests, and uploads Avalonia `linux-x64` and `linux-arm64` Actions artifacts using the dispatch `release_version`. | `tools/Publish-SisterAppTesterPackages.ps1 -App FreeW -Runtimes osx-x64,osx-arm64` creates Avalonia macOS tester zips locally until a hosted macOS FreeW workflow is promoted. |
| FreeP | `tools/Publish-SisterAppTesterPackages.ps1 -App FreeP -Runtimes win-x64` creates the WPF `win-x64` tester zip locally. | `tools/Publish-SisterAppTesterPackages.ps1 -App FreeP -Runtimes linux-x64,linux-arm64` creates Avalonia Linux tester zips locally. | `tools/Publish-SisterAppTesterPackages.ps1 -App FreeP -Runtimes osx-x64,osx-arm64` creates Avalonia macOS tester zips locally. |

## Independent Lane Commands

Run these from a validated branch or commit. Replace `<branch>` with `main` or an isolated `codex/daily-tester-release-*` branch, and replace `<version>` with the tester version.

| App | Platform | Command |
| --- | --- | --- |
| FreeX | Windows | `gh workflow run tester-release.yml --ref <branch> -f release_version=<version> -f include_macos_preview=false` |
| FreeX | Linux | `gh workflow run linux-release.yml --ref <branch> -f release_version=<version> -f prerelease=true` |
| FreeX | macOS internal preview | `gh workflow run macos-app.yml --ref <branch> -f distribution_candidate=false` |
| FreeX | macOS distribution candidate | `gh workflow run macos-app.yml --ref <branch> -f distribution_candidate=true` |
| FreeW | Windows | `gh workflow run freew-release.yml --ref <branch> -f release_version=<version>` |
| FreeW | Linux | `gh workflow run freew-linux.yml --ref <branch> -f release_version=<version>` |
| FreeW | macOS | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Publish-SisterAppTesterPackages.ps1 -App FreeW -Version <version> -Runtimes osx-x64,osx-arm64` |
| FreeP | Windows | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Publish-SisterAppTesterPackages.ps1 -App FreeP -Version <version> -Runtimes win-x64` |
| FreeP | Linux | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Publish-SisterAppTesterPackages.ps1 -App FreeP -Version <version> -Runtimes linux-x64,linux-arm64` |
| FreeP | macOS | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Publish-SisterAppTesterPackages.ps1 -App FreeP -Version <version> -Runtimes osx-x64,osx-arm64` |

For FreeW and FreeP all-platform tester packages from a validated commit:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Publish-SisterAppTesterPackages.ps1 -App FreeW -Version <version>
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Publish-SisterAppTesterPackages.ps1 -App FreeP -Version <version>
```

The script writes versioned zips, `.sha256` files, and a manifest under `artifacts/sister-tester-release-<version>`. Its default runtime set is `win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, and `osx-arm64`; pass `-Runtimes` to build one platform independently. Publish those local assets to the app-specific GitHub prerelease tag (`freew-v<version>` or `freep-v<version>`) until the remaining hosted release publishers are added.

## Publication Semantics

- FreeX Windows: hosted workflow creates or updates the normal FreeX tester GitHub Release. It moves GitHub's latest pointer only when `prerelease=false`.
- FreeX Linux: hosted workflow stages a draft GitHub Release named `freex-linux-v<version>` after the Linux promotion gate passes. A maintainer reviews and publishes the draft.
- FreeX macOS: internal preview dispatches upload Actions artifacts only. `distribution_candidate=true` creates a macOS prerelease after Developer ID signing, notarization, stapling, and Gatekeeper evidence pass. The Windows tester workflow may attach same-commit macOS preview zips only when `include_macos_preview=true`; that attachment is optional and does not make Windows depend on macOS.
- FreeW Windows and Linux: hosted workflows upload short-retention Actions artifacts today. They do not create GitHub Releases; for tester distribution, attach the artifact outputs or local `Publish-SisterAppTesterPackages.ps1` outputs to `freew-v<version>`.
- FreeW macOS and all FreeP platforms: local sister-app package script creates the release zips and checksums. A maintainer uploads them to `freew-v<version>` or `freep-v<version>`.

Before dispatching or publishing a lane, run the app's test gate:

| App | Gate |
| --- | --- |
| FreeX | `tools\Test-TesterReleaseReadiness.ps1`, `dotnet build FreeX.slnx --configuration Release`, `dotnet test FreeX.DefaultTests.slnx --configuration Release --no-build`, and the UI lane when preparing a tester release. |
| FreeW | `dotnet test FreeW.slnx --configuration Release` when time allows; otherwise at minimum core model, core IO, host, and Avalonia focused slices that cover the changed surface. |
| FreeP | `.github/workflows/freep-ci.yml` or `dotnet build FreeP.slnx --configuration Release` followed by `dotnet test FreeP.slnx --configuration Release --no-build`. |
