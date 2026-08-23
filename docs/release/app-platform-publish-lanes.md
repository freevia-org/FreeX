# App Platform Publish Lanes

Every tester release uses one predictable app/version tag and independently runnable platform jobs. The release publisher does not make a Windows package wait for macOS or Linux: it only gathers the packages selected in that dispatch.

## Canonical Release Contract

| App | Tag | Windows | Linux | macOS |
| --- | --- | --- | --- | --- |
| FreeX | `freex-v<version>` | portable `.exe` and `-setup.exe` | portable `.zip` and `-installer.zip` | portable `.zip` and `-apps.zip` |
| FreeW | `freew-v<version>` | portable `.exe` and `-setup.exe` | portable `.zip` and `-installer.zip` | portable `.zip` and `-apps.zip` |
| FreeP | `freep-v<version>` | portable `.exe` and `-setup.exe` | portable `.zip` and `-installer.zip` | portable `.zip` and `-apps.zip` |
| Free Suite | `free-suite-v<version>` | suite `-setup.exe` | suite `-installer.zip` per architecture | suite `-apps.zip` per architecture |

Every release asset has an adjacent `.sha256` file. The original self-contained Windows executables and Linux/macOS archives remain available beside the installers. Installer hashes are calculated after the installer is built. While release certificates are pending, Windows installers are unsigned and macOS app bundles are unsigned and unnotarized; the workflow does not pretend otherwise or require signing credentials.

The Free Suite release is created only for `app=all`. Its package is a bootstrapper over the exact individual installers: Windows invokes the three per-app setups, while Linux and macOS invoke the embedded per-app install scripts. Consequently each app keeps one installation destination and one upgrade/uninstall identity whether installation started from the suite or an individual download.

## Tester Installation

Use the matching app name (`FreeX`, `FreeW`, or `FreeP`) and release version in the commands below. Always download the artifact and its adjacent `.sha256` file first.

| Platform | Select | Verify | Deploy and run |
| --- | --- | --- | --- |
| Windows | `win-x64-setup.exe` for installation, or `win-x64.exe` for portable use | `Get-FileHash <download> -Algorithm SHA256` and compare it to the adjacent `.sha256` file | The installer is per-user and needs no elevation. The standalone executable remains self-contained. Both are unsigned until the Windows certificate is available. |
| Linux | `linux-<architecture>-installer.zip`, or the portable archive | `sha256sum -c <download>.sha256` | Extract the installer bundle and run `./install.sh`; it defaults to `~/.local`, accepts another prefix as its first argument, and includes `uninstall.sh`. |
| macOS | `osx-<architecture>-apps.zip`, or the portable archive | `shasum -a 256 -c <download>.sha256` | Extract the app package and run `./install.sh` or drag the `.app` to `~/Applications`. Current app packages are explicitly unsigned and unnotarized. |

Linux and macOS packages must be extracted before first launch; do not run them from inside the zip file. The canonical workflow repeats these same instructions in every GitHub release body.

For all three applications together, use the matching artifact on the `free-suite-v<version>` release. Installing an individual app after the suite (or running the suite after an individual install) updates the same app installation rather than creating another copy. Uninstall remains per app; the Windows suite bootstrapper intentionally does not register a fourth uninstaller.

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
