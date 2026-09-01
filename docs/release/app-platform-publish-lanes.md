# App Platform Publish Lanes

Every tester release uses one predictable app/version tag and independently runnable platform jobs. The release publisher does not make a Windows package wait for macOS or Linux: it only gathers the packages selected in that dispatch.

## Canonical Release Contract

| App | Tag | Windows | Linux | macOS |
| --- | --- | --- | --- | --- |
| FreeX | `freex-v<version>` | portable `.exe` and `-setup.exe` | portable `.zip` and `-installer.zip` | portable `.zip` and `-apps.zip` |
| FreeW | `freew-v<version>` | portable `.exe` and `-setup.exe` | portable `.zip` and `-installer.zip` | portable `.zip` and `-apps.zip` |
| FreeP | `freep-v<version>` | portable `.exe` and `-setup.exe` | portable `.zip` and `-installer.zip` | portable `.zip` and `-apps.zip` |
| Free Suite | `free-suite-v<version>` | suite `-setup.exe` | suite `-installer.zip` per architecture | suite `-apps.zip` per architecture |

Every release asset has an adjacent `.sha256` file. The original self-contained Windows executables and Linux/macOS archives remain available beside the installers. Installer hashes are calculated after the installer is built. Windows Artifact Signing is an explicit, opt-in release operation; ordinary builds and the hosted lane remain unsigned until its dedicated workload identity is configured. See [Windows Artifact Signing](windows-artifact-signing.md). macOS app bundles remain unsigned and unnotarized until their separate signing lane is enabled.

Each platform payload also carries an SPDX 2.2 SBOM generated with the pinned `Microsoft.Sbom.DotNetTool` 4.1.5 tool and a JSON inventory manifest recording the complete 40-character source commit. Before artifacts cross a workflow job boundary, their canonical checksum files, SBOMs, and inventory are regenerated and compared. The final app or suite release manifest covers every selected runtime payload, installer, checksum, SBOM, runtime manifest, and bundled legal notice. A version tag is immutable: a tag that already exists at another commit causes the lane to fail instead of replacing its assets.

The Free Suite release is created only for `app=all`. Its package is a bootstrapper over the exact individual installers: Windows invokes the three per-app setups, while Linux and macOS invoke the embedded per-app install scripts. Consequently each app keeps one installation destination and one upgrade/uninstall identity whether installation started from the suite or an individual download.

Hosted runners exercise install, bounded launch, and uninstall without UI assertions. Suite lanes additionally install suite-to-individual and individual-to-suite transitions against an ephemeral per-user destination. A failed Windows child installer is propagated as a failed suite installation; the bootstrapper cannot report success after a child failure.

## Tester Installation

Use the matching app name (`FreeX`, `FreeW`, or `FreeP`) and release version in the commands below. Always download the artifact and its adjacent `.sha256` file first.

| Platform | Select | Verify | Deploy and run |
| --- | --- | --- | --- |
| Windows | `win-x64-setup.exe` for installation, or `win-x64.exe` for portable use | `Get-FileHash <download> -Algorithm SHA256` and compare it to the adjacent `.sha256` file; signed releases also pass `signtool verify /pa /all <download>` | The installer is per-user and needs no elevation. The standalone executable remains self-contained. Check the signature before treating the publisher as verified. |
| Linux | `linux-<architecture>-installer.zip`, or the portable archive | `sha256sum -c <download>.sha256` | Extract the installer bundle and run `./install.sh`; it defaults to `~/.local`, accepts another prefix as its first argument, and includes `uninstall.sh`. |
| macOS | `osx-<architecture>-apps.zip`, or the portable archive | `shasum -a 256 -c <download>.sha256` | Extract the app package and run `./install.sh` or drag the `.app` to `~/Applications`. Current app packages are explicitly unsigned and unnotarized. |

Linux and macOS packages must be extracted before first launch; do not run them from inside the zip file. The canonical workflow repeats these same instructions in every GitHub release body.

For all three applications together, use the matching artifact on the `free-suite-v<version>` release. Installing an individual app after the suite (or running the suite after an individual install) updates the same app installation rather than creating another copy. Uninstall remains per app; the Windows suite bootstrapper intentionally does not register a fourth uninstaller.

## Dispatching A Lane

Use the `App Tester Release` workflow (`.github/workflows/app-tester-release.yml`) for normal tester publication. It first verifies that the immutable dispatch SHA already has successful canonical CI and CodeQL runs. It then packages the requested platform lane on its native GitHub runner, executes package-content and install/transition/uninstall checks, and creates or updates the matching app/version release. Source tests and repository preflight are not repeated during publication.

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

Run the workflow from the exact commit that passed CI and CodeQL. A later `main` commit does not
invalidate an already-dispatched immutable candidate; publish the verified SHA or deliberately test
a newer SHA. This prevents unrelated merges from restarting hours of completed validation.

The workflow uses `tools/Publish-SisterAppTesterPackages.ps1` as the package contract. It accepts `-Runtimes` for local, independent package verification and now supports FreeX, FreeW, and FreeP:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Publish-SisterAppTesterPackages.ps1 -App FreeP -Version 0.8.151 -Runtimes win-x64
```

Pass `-ArtifactSigningMetadataPath tools/signing/metadata.json` to this command
and to `tools/packaging/New-AppInstallers.ps1` only from an authenticated,
signing-enabled release environment. The portable executable is signed before
installer construction, and Inno Setup signs its generated uninstaller and the
final setup executable before checksums are written.

## Existing Specialized Workflows

`tester-release.yml`, `linux-release.yml`, `macos-app.yml`, `freew-release.yml`, `freew-linux.yml`, and `freep-release.yml` remain useful as focused validation or platform-preview lanes. They are not the canonical cross-platform release surface. New tester releases should use `app-tester-release.yml`, so a tester does not need to chase separate Windows, Linux, and macOS release pages for the same app/version.

Before dispatching a release, freeze the intended commit and run the app's applicable test gate. The canonical workflow repeats that gate in hosted CI before it publishes any package.
