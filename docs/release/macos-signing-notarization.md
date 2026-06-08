# macOS Signing And Notarization Runbook

This runbook explains how the `macOS App Preview` GitHub Actions workflow produces internal preview artifacts by default and how to run it as a guarded distribution-candidate validation when Developer ID signing and notarization evidence are required. It also records how to retrieve the hosted app artifacts from GitHub Actions and how distribution-candidate dispatches publish guarded GitHub Release assets after both runtime artifacts pass evidence validation.

## Current Workflow Contract

The workflow is `.github/workflows/macos-app.yml`. It runs on `workflow_dispatch`, `push` to `main`, and `pull_request` to `main`; pull request events intentionally fall back to ad-hoc signing even when secrets are present. `workflow_dispatch` includes a `distribution_candidate` input that defaults to `false`. Default hosted runs are `artifact_channel=internal-preview`, where notarization may be skipped with explicit evidence. A dispatch run with `distribution_candidate=true` is `artifact_channel=distribution-candidate` and fails unless Developer ID signing, accepted notarization, and stapler validation all complete. Only those distribution-candidate dispatches run the release publication job, which uses job-level `actions: read` and `contents: write` permissions after the matrix succeeds.

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

Each evidence file records `artifact_channel`, `distribution_candidate`, `distribution_contract`, and `distribution_readiness` so internal previews and distribution candidates can be separated without relying on artifact names alone.

## Artifact Retrieval

Internal-preview runs produce downloadable GitHub Actions artifacts only. In GitHub, open Actions > `macOS App Preview` > the completed run, then download:

- `freex-<run-id>-<run-attempt>-osx-arm64-macos-app`
- `freex-<run-id>-<run-attempt>-osx-x64-macos-app`

Quick retrieval checklist:

1. Pick `osx-arm64` for Apple Silicon Macs or `osx-x64` for Intel Macs.
2. Download the matching Actions artifact wrapper from the completed workflow run.
3. Preserve each `freex-<run-id>-<run-attempt>-<runtime>-macos-app` wrapper directory under the artifact root, then unzip the wrapper contents there so stale or mixed-run downloads can be detected.
4. Keep `freex-<runtime>-macos-evidence.txt` and the smoke/notarization logs with any tester report.

With the GitHub CLI, the same artifacts can be retrieved with:

```bash
gh run download <run-id> -n freex-<run-id>-<run-attempt>-osx-arm64-macos-app -D artifacts/macos-preview/freex-<run-id>-<run-attempt>-osx-arm64-macos-app
gh run download <run-id> -n freex-<run-id>-<run-attempt>-osx-x64-macos-app -D artifacts/macos-preview/freex-<run-id>-<run-attempt>-osx-x64-macos-app
```

Keep those wrapper directory names intact under `artifacts/macos-preview`. The Windows evidence validator uses them to detect duplicate stale downloads, mixed `osx-arm64`/`osx-x64` runs, and optional expected run identity checks.

Signed, notarized, and internal ad-hoc outputs use the same artifact names. Unzip the GitHub artifact wrapper first, then verify the inner `freex-<runtime>-macos-app.zip` with the matching `.zip.sha256`. For an internal preview, expect `artifact_channel=internal-preview`, `distribution_readiness=internal_preview_not_for_distribution`, and `codesign_mode=ad-hoc` or notarization evidence such as `notarization_status=skipped_missing_credentials` or `skipped_not_developer_id_signed`. For a distribution candidate, require `artifact_channel=distribution-candidate`, `distribution_readiness=distribution_candidate_ready`, `codesign_mode=developer-id`, `notarization_status=accepted`, and `stapler_validated=true`.

For `distribution_candidate=true`, the post-matrix publication job downloads both runtime artifacts, revalidates the evidence markers above, prepares stable asset names such as `FreeX-latest-macos-arm64.zip`, `FreeX-latest-macos-x64.zip`, `FreeX-latest-macos-distribution-candidate-manifest.json`, and matching evidence/log/instruction assets, then creates a prerelease GitHub Release for that run.

No local Mac is needed to produce the downloadable artifacts: hosted macOS runners build the bundle, run native-architecture package/launch smoke, exercise LaunchServices, verify checksums, and collect evidence. A human tester on macOS is still required for Finder open, Gatekeeper behavior, basic workbook workflows, and any candidate accessibility checks.

## Windows Evidence Bundle Preflight

After unzipping both runtime artifact wrappers on a Windows machine, run the evidence bundle validator from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-MacOsPublicPreviewReadiness.ps1 -ArtifactRoot artifacts/macos-preview -ExpectedRunId <run-id> -ExpectedRunAttempt <run-attempt>
```

For a public-preview distribution-candidate run, require the Developer ID, notarization, stapler, and separate diagnostics artifact evidence:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-MacOsPublicPreviewReadiness.ps1 -ArtifactRoot artifacts/macos-preview -ExpectedRunId <run-id> -ExpectedRunAttempt <run-attempt> -DistributionCandidate -RequireSeparateDiagnosticsArtifact
```

`-ExpectedRunId` and `-ExpectedRunAttempt` are optional, but use them when validating artifacts from a specific GitHub Actions run. `tools/Test-MacOsPublicPreviewReadiness.ps1` validates both `osx-arm64` and `osx-x64` bundles without macOS by checking the downloaded evidence files, smoke logs, tester instructions, app ZIP, and `.zip.sha256` checksum. It requires artifact channel/readiness keys, `zip_sha256`, signing and notarization status, stapler status for distribution candidates, startup smoke, LaunchServices launch smoke, Open-With smoke, `format_cells_style_roundtrip=true` with a count of at least two, command key smoke, checksum files, diagnostics artifact file sets, and tester instructions.

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
2. Run `macOS App Preview` from `main` with `workflow_dispatch` and set `distribution_candidate=true`; default dispatch, push, and pull request runs remain internal previews.
3. Confirm both matrix jobs complete, or inspect the diagnostics artifact for the failed runtime.
4. Download each `freex-<run-id>-<run-attempt>-<runtime>-macos-app` artifact from the run summary or with `gh run download`, preserving the wrapper directory names under the artifact root.
5. Verify `freex-<runtime>-macos-evidence.txt` contains:
   - `artifact_channel=distribution-candidate`
   - `distribution_candidate=true`
   - `distribution_readiness=distribution_candidate_ready`
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

Before public-preview promotion, complete the macOS/Avalonia accessibility evidence requirement in [planning/macos-accessibility-evidence.md](../planning/macos-accessibility-evidence.md). Hosted checks cover packaging, signing, notarization, LaunchServices, menu, dialog, and workbook smoke prerequisites; human macOS validation must still record keyboard-only and VoiceOver coverage plus reviewed known accessibility issues.

## Public Distribution Blockers

Do not present the macOS artifact as a public release until all of these are true:

- Hosted evidence exists for both `osx-arm64` and `osx-x64`.
- The evidence marks the artifact as `artifact_channel=distribution-candidate` with `distribution_readiness=distribution_candidate_ready`.
- Developer ID signing, accepted notarization, and stapling evidence are present.
- LaunchServices and packaging smoke evidence is attached to the release record.
- The guarded release publication job has created the GitHub Release assets for both runtimes.
- The tester instructions no longer require internal-only Control-click or right-click open guidance for notarization failures.
- Human macOS validation covers Finder open, Gatekeeper launch, checksum verification, basic workbook open/save, and any accessibility checks required for the candidate.
- The macOS/Avalonia accessibility evidence requirement from [planning/macos-accessibility-evidence.md](../planning/macos-accessibility-evidence.md) is complete, including human keyboard-only and VoiceOver validation with known accessibility issues reviewed.

## Reference Links

- Apple Developer ID support: https://developer.apple.com/support/developer-id/
- Apple notarization overview: https://developer.apple.com/documentation/security/notarizing_macos_software_before_distribution
- Apple custom notarization workflow: https://developer.apple.com/documentation/security/notarizing_macos_software_before_distribution/customizing_the_notarization_workflow
