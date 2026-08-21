# App Platform Publish Lanes

Every tester release uses one predictable app/version tag and independently runnable platform jobs. The release publisher does not make a Windows package wait for macOS or Linux: it only gathers the packages selected in that dispatch.

## Canonical Release Contract

| App | Tag | Windows | Linux | macOS |
| --- | --- | --- | --- | --- |
| FreeX | `freex-v<version>` | `FreeX-v<version>-win-x64.exe` | `FreeX-v<version>-linux-{x64,arm64}.zip` | `FreeX-v<version>-osx-{x64,arm64}.zip` |
| FreeW | `freew-v<version>` | `FreeW-v<version>-win-x64.exe` | `FreeW-v<version>-linux-{x64,arm64}.zip` | `FreeW-v<version>-osx-{x64,arm64}.zip` |
| FreeP | `freep-v<version>` | `FreeP-v<version>-win-x64.exe` | `FreeP-v<version>-linux-{x64,arm64}.zip` | `FreeP-v<version>-osx-{x64,arm64}.zip` |

Every release asset has an adjacent `.sha256` file. Windows artifacts are self-contained single-file WPF executables. Linux and macOS artifacts are self-contained Avalonia archives. This is a packaging distinction, not a release-lane distinction: all three platforms belong to the same app/version release.

For installation steps, Chromium/browser status, ChromeOS guidance, and additional OS targets, see [installation-and-browser-support.md](installation-and-browser-support.md).

## Tester Installation

Use the matching app name (`FreeX`, `FreeW`, or `FreeP`) and release version in the commands below. Always download the artifact and its adjacent `.sha256` file first.

| Platform | Select | Verify | Deploy and run |
| --- | --- | --- | --- |
| Windows | `win-x64.exe` | `Get-FileHash .\\<app>-v<version>-win-x64.exe -Algorithm SHA256` and compare it to the `.sha256` file | Move the single `.exe` to the user's chosen program directory and run it. It is self-contained; no .NET runtime or installer is required. Close it before replacing the file for an update. |
| Linux | `linux-x64.zip` for Intel/AMD, `linux-arm64.zip` for 64-bit ARM | `sha256sum -c <app>-v<version>-linux-<architecture>.zip.sha256` | Extract into a stable user directory, run `chmod +x <directory>/<app>` once, then launch `<directory>/<app>`. Replace the extracted directory after closing the app to update. |
| macOS | `osx-x64.zip` for Intel, `osx-arm64.zip` for Apple silicon | `shasum -a 256 -c <app>-v<version>-osx-<architecture>.zip.sha256` | Extract into a stable user directory, run `chmod +x <directory>/<app>` once, then launch `<directory>/<app>`. Replace the extracted directory after closing the app to update. |

Linux and macOS packages must be extracted before first launch; do not run them from inside the zip file. The canonical workflow repeats these same instructions in every GitHub release body.

## Dispatching A Lane

Use the `App Tester Release` workflow (`.github/workflows/app-tester-release.yml`) for normal tester publication. It runs the selected app's release test gate, packages the requested platform lane on its native GitHub runner, and creates or updates the matching app/version release.

| Requested work | Dispatch inputs |
| --- | --- |
| One app, one platform | `app=<FreeX|FreeW|FreeP>`, `platform=<windows|linux|macos>` |
| One app, all platforms | `app=<FreeX|FreeW|FreeP>`, `platform=all` |
| All apps, one platform | `app=all`, `platform=<windows|linux|macos>` |
| All apps, all platforms | `app=all`, `platform=all` |

For example, a complete `0.8.151` tester package run is:

```powershell
gh workflow run app-tester-release.yml --ref <branch> -f app=all -f platform=all -f release_version=0.8.151 -f prerelease=true
```

The workflow uses `tools/Publish-SisterAppTesterPackages.ps1` as the package contract. It accepts `-Runtimes` for local, independent package verification and now supports FreeX, FreeW, and FreeP:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Publish-SisterAppTesterPackages.ps1 -App FreeP -Version 0.8.151 -Runtimes win-x64
```

## Existing Specialized Workflows

`tester-release.yml`, `linux-release.yml`, `macos-app.yml`, `freew-release.yml`, `freew-linux.yml`, and `freep-release.yml` remain useful as focused validation or platform-preview lanes. They are not the canonical cross-platform release surface. New tester releases should use `app-tester-release.yml`, so a tester does not need to chase separate Windows, Linux, and macOS release pages for the same app/version.

Before dispatching a release, freeze the intended commit and run the app's applicable test gate. The canonical workflow repeats that gate in hosted CI before it publishes any package.
