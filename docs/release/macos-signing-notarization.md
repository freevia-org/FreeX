# macOS Signing And Notarization Runbook

This runbook turns the `macOS App Preview` GitHub Actions workflow from an internal ad-hoc signed preview into a Developer ID signed and notarized macOS validation artifact. It is still not a public release channel until the hosted evidence below is captured and a release-asset publication path exists.

## Current Workflow Contract

The workflow is `.github/workflows/macos-app.yml`. It runs on `workflow_dispatch`, `push` to `main`, and `pull_request` to `main`; pull request events intentionally fall back to ad-hoc signing even when secrets are present.

The workflow builds two app artifacts:

- `osx-arm64` on `macos-latest`
- `osx-x64` on `macos-15-intel`

Each runtime uploads an Actions artifact named `freex-<run-id>-<run-attempt>-<runtime>-macos-app` with:

- `freex-<runtime>-macos-app.zip`
- `freex-<runtime>-macos-app.zip.sha256`
- `freex-<runtime>-macos-evidence.txt`
- `freex-<runtime>-macos-packaging-smoke.log`
- `freex-<runtime>-macos-launch-smoke.txt`
- `freex-<runtime>-macos-notarization.log`
- `freex-<runtime>-macos-tester-instructions.md`

The matching diagnostics artifact is always uploaded when available, even if the workflow fails before the primary artifact is complete.

## Required Apple Inputs

Developer ID signing and notarization require:

- Apple Developer Program or Apple Developer Enterprise Program team membership.
- A Developer ID Application certificate that includes the private key.
- The exact codesigning identity string reported by `security find-identity -v -p codesigning`.
- An Apple ID that can submit notarization requests for the team.
- The Apple Developer Team ID.
- An app-specific password for the Apple ID used by `xcrun notarytool`.

Apple's current notarization documentation uses `notarytool`; the older `altool` path is not part of this workflow. The workflow submits the zipped `FreeX.app`, waits for an accepted notarization result, staples the ticket to the app bundle, validates stapling, then recreates the zip.

## GitHub Secrets

Configure these repository secrets before running a signed hosted validation:

| Secret | Value |
| --- | --- |
| `MACOS_CODESIGN_CERTIFICATE_P12` | Base64 text of the exported Developer ID Application `.p12` containing the certificate and private key. |
| `MACOS_CODESIGN_CERTIFICATE_PASSWORD` | Password used when exporting the `.p12`. |
| `MACOS_DEVELOPER_ID_APPLICATION` | Exact signing identity, for example `Developer ID Application: Team Name (TEAMID)`. |
| `MACOS_NOTARY_APPLE_ID` | Apple ID used for notarization. |
| `MACOS_NOTARY_TEAM_ID` | Apple Developer Team ID. |
| `MACOS_NOTARY_PASSWORD` | App-specific password for the Apple ID. |

Useful local preparation commands on a Mac:

```bash
security find-identity -v -p codesigning
base64 -i DeveloperIDApplication.p12 | pbcopy
```

Paste the base64 output into `MACOS_CODESIGN_CERTIFICATE_P12` exactly as generated.

## Hosted Validation Steps

1. Configure all six secrets above in the GitHub repository.
2. Run `macOS App Preview` from `main` with `workflow_dispatch`, or let a trusted non-PR `push` to `main` run it.
3. Confirm both matrix jobs complete, or inspect the diagnostics artifact for the failed runtime.
4. Download each `freex-<run-id>-<run-attempt>-<runtime>-macos-app` artifact.
5. Verify `freex-<runtime>-macos-evidence.txt` contains:
   - `codesign_verified=true`
   - `codesign_mode=developer-id`
   - `notarization_status=accepted`
   - `stapler_validated=true`
   - `zip_sha256=<hash>`
6. Verify `freex-<runtime>-macos-notarization.log` reports an accepted notary submission.
7. Verify `freex-<runtime>-macos-launch-smoke.txt` contains `macos_launch_smoke=passed` for the native runner architecture, or records `smoke_status=skipped_host_arch_mismatch` only for the cross-architecture runtime.
8. Run the checksum from the artifact directory:

```bash
shasum -a 256 -c freex-<runtime>-macos-app.zip.sha256
```

The expected result is `<zip-name>: OK`.

## Public Distribution Blockers

Do not present the macOS artifact as a public release until all of these are true:

- Hosted evidence exists for both `osx-arm64` and `osx-x64`.
- Developer ID signing, accepted notarization, and stapling evidence are present.
- LaunchServices and packaging smoke evidence is attached to the release record.
- A release-channel path exists; the current workflow uploads Actions artifacts only and has `contents: read`.
- The tester instructions no longer require internal-only Control-click or right-click open guidance for notarization failures.
- Human macOS validation covers Finder open, Gatekeeper launch, checksum verification, basic workbook open/save, and any accessibility checks required for the candidate.

## Reference Links

- Apple Developer ID support: https://developer.apple.com/support/developer-id/
- Apple notarization overview: https://developer.apple.com/documentation/security/notarizing_macos_software_before_distribution
- Apple custom notarization workflow: https://developer.apple.com/documentation/security/notarizing_macos_software_before_distribution/customizing_the_notarization_workflow
