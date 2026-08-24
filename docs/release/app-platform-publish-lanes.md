# App Platform Publish Lanes

Every tester release uses one predictable app/version tag and independently runnable platform jobs. The release publisher does not make a Windows package wait for macOS or Linux: it only gathers the packages selected in that dispatch.

## Canonical Release Contract

| App | Tag | Windows | Linux | macOS |
| --- | --- | --- | --- | --- |
| FreeX | `freex-v<version>` | portable `.exe` and `.msix` | portable `.zip` and `-installer.zip` | portable `.zip` and `-apps.zip` |
| FreeW | `freew-v<version>` | portable `.exe` and `.msix` | portable `.zip` and `-installer.zip` | portable `.zip` and `-apps.zip` |
| FreeP | `freep-v<version>` | portable `.exe` and `.msix` | portable `.zip` and `-installer.zip` | portable `.zip` and `-apps.zip` |
| Free Suite | `free-suite-v<version>` | suite `.msix` | suite `-installer.zip` | suite `-apps.zip` per architecture |

Every release asset has an adjacent `.sha256` file. The original self-contained Windows executables and Linux/macOS archives remain available beside the installable packages. Package hashes are calculated after packaging and signing. While release certificates are pending, Windows MSIX packages and macOS app bundles are unsigned/unnotarized; the workflow does not pretend otherwise or require signing credentials.

Each platform payload also carries an SPDX 2.2 SBOM generated with the pinned `Microsoft.Sbom.DotNetTool` 4.1.5 tool and a JSON inventory manifest recording the complete 40-character source commit. Before artifacts cross a workflow job boundary, their canonical checksum files, SBOMs, and inventory are regenerated and compared. The final app or suite release manifest covers every selected runtime payload, installer, checksum, SBOM, runtime manifest, and bundled legal notice. A version tag is immutable: a tag that already exists at another commit causes the lane to fail instead of replacing its assets.

The Free Suite release is created only for `app=all`. Its Windows package is one MSIX containing all three applications; Linux and macOS invoke the embedded per-app install scripts. The standalone executable remains available for each app on Windows.

Hosted runners exercise package extraction and bounded validation without UI assertions. Windows MSIX smoke validates the manifest and every packaged executable; Linux and macOS lanes exercise install, update, and uninstall scripts.

## Tester Installation

Use the matching app name (`FreeX`, `FreeW`, or `FreeP`) and release version in the commands below. Always download the artifact and its adjacent `.sha256` file first.

| Platform | Select | Verify | Deploy and run |
| --- | --- | --- | --- |
| Windows | `win-x64.msix` for installation, or `win-x64.exe` for portable use | `Get-FileHash <download> -Algorithm SHA256` and compare it to the adjacent `.sha256` file | Open the signed MSIX with App Installer. The standalone executable remains self-contained. Unsigned internal packages require the signing certificate to be trusted on the test machine. |
| Linux | `linux-<architecture>-installer.zip`, or the portable archive | `sha256sum -c <download>.sha256` | Extract the installer bundle and run `./install.sh`; it defaults to `~/.local`, accepts another prefix as its first argument, and includes `uninstall.sh`. |
| macOS | `osx-<architecture>-apps.zip`, or the portable archive | `shasum -a 256 -c <download>.sha256` | Extract the app package and run `./install.sh` or drag the `.app` to `~/Applications`. Current app packages are explicitly unsigned and unnotarized. |

Linux and macOS packages must be extracted before first launch; do not run them from inside the zip file. The canonical workflow repeats these same instructions in every GitHub release body.

For all three applications together, use the matching `.msix` artifact on the `free-suite-v<version>` release. The package exposes FreeX, FreeW, and FreeP as separate Start-menu applications under one package identity.

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
