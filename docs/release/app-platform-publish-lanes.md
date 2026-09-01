# App Platform Publish Lanes

`Full Signed Release` (`.github/workflows/full-release.yml`) is the canonical
release surface for FreeX, FreeW, FreeP, and Free Suite. It pins the dispatch
SHA, requires successful exact-SHA CI and CodeQL, runs the release-only matrix,
and publishes nothing until every selected package and trust gate passes.

## Release inventory

| App | Tag | Windows x64 | Linux x64/ARM64 | macOS Intel/Apple silicon |
| --- | --- | --- | --- | --- |
| FreeX | `freex-v<version>` | signed standalone `.exe` and signed Velopack installer/feed | portable and installer `.zip` | signed, notarized `.app` package and portable archive |
| FreeW | `freew-v<version>` | signed standalone `.exe` and signed Velopack installer/feed | portable and installer `.zip` | signed, notarized `.app` package and portable archive |
| FreeP | `freep-v<version>` | signed standalone `.exe` and signed Velopack installer/feed | portable and installer `.zip` | signed, notarized `.app` package and portable archive |
| Free Suite | `free-suite-v<version>` | signed non-Inno suite bootstrapper | suite installer `.zip` | aggregate package containing the accepted app bundles |

Windows code signing uses Azure Artifact Signing with the Freevia public-trust
profile. The workflow signs app payloads at the Velopack packaging stage,
verifies every Authenticode signature, builds the suite from the final signed
per-app installers, and signs the outer suite bootstrapper. Checksums are
calculated only after the last signature is applied.

Linux has no platform code-signing identity in this repository. Its portable
and installer archives are integrity-protected with adjacent SHA-256 files,
SPDX 2.2 SBOMs, runtime manifests, and a final release manifest tied to the
complete source commit.

macOS uses a Developer ID Application certificate, hardened runtime signing,
Apple notarization, ticket stapling, and validation. The lane fails closed if
credentials are absent or any signing, notarization, stapling, or validation
operation fails. `Full Signed Release` never silently publishes an unsigned or
unnotarized macOS substitute.

Every version tag is immutable. An existing tag at another commit stops the
run before publication.

## Suite semantics

The suite release is created only for `app=all`. The Windows package is a
repository-owned bootstrapper, not Inno Setup. It invokes the same signed
Velopack installers published on the individual app releases and propagates a
child failure as an overall installation failure. Linux and macOS suite
packages reuse the corresponding finalized per-app packages, so suite and
individual installations retain the same app identities.

## Dispatch

| Requested work | Inputs |
| --- | --- |
| One app, one platform | `app=<FreeX|FreeW|FreeP>`, `platform=<windows|linux|macos>` |
| One app, all platforms | `app=<FreeX|FreeW|FreeP>`, `platform=all` |
| All apps, one platform | `app=all`, `platform=<windows|linux|macos>` |
| All apps, all platforms | `app=all`, `platform=all` |

Example full non-prerelease:

```powershell
gh workflow run full-release.yml --ref main `
  -f app=all `
  -f platform=all `
  -f release_version=0.8.185 `
  -f prerelease=false
```

Use `prerelease=true` for a signed test release. The same signing and integrity
requirements apply to both release states.

## Installation verification

- Windows: run Authenticode verification and compare the adjacent SHA-256 file
  before launching the standalone executable or Velopack installer.
- Linux: run `sha256sum -c <download>.sha256`, extract the archive, and use its
  `install.sh` helper.
- macOS: run `shasum -a 256 -c <download>.sha256`; Gatekeeper validation should
  recognize the stapled notarization ticket before installation.

The older focused workflows remain diagnostic or preview lanes. They are not
the canonical all-app publication path and must not be used to claim a complete
signed release.
