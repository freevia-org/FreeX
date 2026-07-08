# App Platform Publish Lanes

This matrix is the maintainer map for tester packages. Each app/platform lane must be runnable independently; a Windows package must not depend on a macOS artifact, and Linux/macOS previews must not move the Windows latest pointer.

| App | Windows | Linux | macOS |
| --- | --- | --- | --- |
| FreeX | `.github/workflows/tester-release.yml` publishes the WPF tester release and stable Windows assets. | `.github/workflows/linux-release.yml` publishes Avalonia Linux release assets. | `.github/workflows/macos-app.yml` builds hosted Avalonia app previews; `tester-release.yml` can attach same-commit macOS assets when explicitly requested. |
| FreeW | `.github/workflows/freew-release.yml` runs `freew/build/publish-windows.ps1` for the WPF `win-x64` zip. | `.github/workflows/freew-linux.yml` builds and smoke-tests Avalonia `linux-x64` and `linux-arm64` packages using the dispatch `release_version`. | `tools/Publish-SisterAppTesterPackages.ps1 -App FreeW` publishes Avalonia `osx-x64` and `osx-arm64` tester zips until a hosted macOS FreeW workflow is promoted. |
| FreeP | `tools/Publish-SisterAppTesterPackages.ps1 -App FreeP -Runtimes win-x64` publishes the WPF `win-x64` tester zip. | `tools/Publish-SisterAppTesterPackages.ps1 -App FreeP -Runtimes linux-x64,linux-arm64` publishes Avalonia Linux tester zips. | `tools/Publish-SisterAppTesterPackages.ps1 -App FreeP -Runtimes osx-x64,osx-arm64` publishes Avalonia macOS tester zips. |

For FreeW and FreeP all-platform tester packages from a validated commit:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Publish-SisterAppTesterPackages.ps1 -App FreeW -Version <version>
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Publish-SisterAppTesterPackages.ps1 -App FreeP -Version <version>
```

The script writes versioned zips, `.sha256` files, and a manifest under `artifacts/sister-tester-release-<version>`. Publish those assets to the app-specific GitHub prerelease tag (`freew-v<version>` or `freep-v<version>`) until the remaining hosted release publishers are added.

Before dispatching or publishing a lane, run the app's test gate:

| App | Gate |
| --- | --- |
| FreeX | `tools\Test-TesterReleaseReadiness.ps1`, `dotnet build FreeX.slnx --configuration Release`, `dotnet test FreeX.DefaultTests.slnx --configuration Release --no-build`, and the UI lane when preparing a tester release. |
| FreeW | `dotnet test FreeW.slnx --configuration Release` when time allows; otherwise at minimum core model, core IO, host, and Avalonia focused slices that cover the changed surface. |
| FreeP | `.github/workflows/freep-ci.yml` or `dotnet build FreeP.slnx --configuration Release` followed by `dotnet test FreeP.slnx --configuration Release --no-build`. |
