# macOS Hosted App Production Runbook

Use this runbook when a maintainer without local macOS hardware needs GitHub-hosted macOS runners to produce FreeX macOS app artifacts. It covers the production path through `.github/workflows/macos-app.yml`, where to retrieve each artifact and evidence file, how to hand work to human Mac testers, and which decisions remain manual.

This is not a push-triggered workflow. A commit pushed to `main` does not start `macOS App Preview`. The workflow currently runs only for:

- `workflow_dispatch`, started manually from GitHub Actions or `gh workflow run`.
- `pull_request` targeting `main`, for PR evidence only.

## Choose The Run Mode

| Need | Trigger | `distribution_candidate` | Output posture |
| --- | --- | --- | --- |
| PR sanity evidence | Pull request to `main` | Not available | Internal preview only. PR runs use ad-hoc signing even if secrets exist, and they do not publish release assets. |
| Internal macOS preview artifact | Manual dispatch | `false` | Hosted app, diagnostics, and aggregate readiness artifacts. Signing may be ad-hoc and notarization may be skipped with explicit evidence. Not public distribution. |
| Hosted production candidate | Manual dispatch from the intended release commit, normally `main` | `true` | Requires Developer ID signing, accepted notarization, stapling, Gatekeeper assessment, runtime smoke evidence, aggregate readiness, guarded release-assets publication, and human validation before public-preview promotion. |

For production, use the third row. Do not treat a PR run or a default manual dispatch as production evidence, even if the app zip exists.

## Production Prerequisites

- Repository `Actions` permission to run `macOS App Preview`.
- The intended release commit is already on `main`, or a release owner has explicitly selected another ref.
- All six macOS signing/notarization repository secrets from [macos-signing-notarization.md](macos-signing-notarization.md) are configured:
  - `MACOS_CODESIGN_CERTIFICATE_P12`
  - `MACOS_CODESIGN_CERTIFICATE_PASSWORD`
  - `MACOS_DEVELOPER_ID_APPLICATION`
  - `MACOS_NOTARY_APPLE_ID`
  - `MACOS_NOTARY_TEAM_ID`
  - `MACOS_NOTARY_PASSWORD`
- A Windows, Linux, or macOS checkout with PowerShell available for evidence preflight. Local macOS hardware is not required to produce or preflight hosted artifacts.
- An authenticated GitHub browser session or `gh auth login` for artifact downloads. GitHub Actions artifact archives require authentication even when workflow metadata is visible.

Apple account setup, certificate export, and GitHub secret entry are human setup tasks. The hosted workflow can consume the secrets, but it cannot create or approve Apple credentials.

## Dispatch The Hosted Production Run

From the GitHub UI:

1. Open GitHub > FreeX > Actions > `macOS App Preview`.
2. Select `Run workflow`.
3. Choose the intended ref, normally `main`.
4. Set `distribution_candidate` to `true`.
5. Start the workflow and record the run id, run attempt, run number, ref, and commit SHA from the run page.

From GitHub CLI:

```powershell
gh workflow run macos-app.yml --ref main -f distribution_candidate=true
gh run list --workflow macos-app.yml --branch main --limit 5
```

Wait for the run you dispatched. If you rerun failed jobs, the artifact names use the new run attempt, so record the final successful attempt.

## Required Green Jobs

A production candidate run must complete these jobs successfully:

- `macOS app bundle (osx-arm64)` on `macos-15`
- `macOS app bundle (osx-x64)` on `macos-15-intel`
- `Aggregate macOS preview evidence`
- `Publish macOS distribution candidate`

If either runtime fails, inspect the runtime diagnostics artifact from that same run attempt. A failed or partial run is not production-ready.

## Optional macOS TFM Compile Validation

When `validate_macos_tfm=true` is enabled in `.github/workflows/macos-app.yml`, a maintainer may run it manually as a companion check for the future `net10.0-macos` compile path.

This validation is not the app artifact lane. It is useful for answering whether hosted macOS 26 arm64 and Intel runners can install the pinned macOS workload set and compile the macOS-specific target, but it does not replace the current `net10.0` publish that produces the `osx-arm64` and `osx-x64` bundles.

From the GitHub UI:

1. Open GitHub > FreeX > Actions > `macOS App Preview`.
2. Select `Run workflow`.
3. Choose the ref to validate.
4. Enable `validate_macos_tfm`.
5. Start the workflow and record the run id, run attempt, run number, ref, and commit SHA.

From GitHub CLI:

```powershell
gh workflow run macos-app.yml --ref main -f validate_macos_tfm=true
gh run list --workflow macos-app.yml --branch main --limit 5
```

Interpret the result separately from production artifact readiness:

- Passing workload install/restore and `net10.0-macos` compile evidence means the hosted runner can compile the future macOS TFM path.
- A workload install/restore failure is an opt-in lane readiness issue, not a regression in the current hosted `net10.0` app bundle.
- A `net10.0-macos` compile failure belongs to the macOS TFM lane or host-only source boundary; do not block the current app artifact lane unless its normal `osx-arm64` or `osx-x64` jobs also fail.
- Uploaded TFM validation logs are evidence only. They are not app zips, release assets, signing evidence, notarization evidence, or human validation evidence.
- Hosted compile evidence cannot prove native AppKit share-sheet runtime behavior. Real macOS human validation is still required before any release note claims native share-sheet support.

## Artifact And Evidence Map

Download artifacts from the completed run summary under `Artifacts`, or use `gh run download`. Keep each downloaded artifact inside its wrapper directory; do not flatten files directly into `artifacts/macos-preview`.

| Wrapper | Produced when | Contents to keep |
| --- | --- | --- |
| `freex-<run-id>-<run-attempt>-osx-arm64-macos-app` | Every completed `osx-arm64` runtime job | `freex-osx-arm64-macos-app.zip`, `.zip.sha256`, evidence, packaging smoke, LaunchServices smoke, Open-With smoke, default-open smoke, notarization log, tester instructions |
| `freex-<run-id>-<run-attempt>-osx-arm64-macos-diagnostics` | Always uploaded when available | Runtime diagnostics files, including evidence collected before failures |
| `freex-<run-id>-<run-attempt>-osx-x64-macos-app` | Every completed `osx-x64` runtime job | `freex-osx-x64-macos-app.zip`, `.zip.sha256`, evidence, packaging smoke, LaunchServices smoke, Open-With smoke, default-open smoke, notarization log, tester instructions |
| `freex-<run-id>-<run-attempt>-osx-x64-macos-diagnostics` | Always uploaded when available | Runtime diagnostics files, including evidence collected before failures |
| `freex-<run-id>-<run-attempt>-macos-tfm-build-arm64-evidence` | Manual dispatch with `validate_macos_tfm=true` | Evidence-only `freex-arm64-macos-tfm-build-evidence.txt` with runner, workload, Xcode, and `net10.0-macos` compile markers |
| `freex-<run-id>-<run-attempt>-macos-tfm-build-x64-evidence` | Manual dispatch with `validate_macos_tfm=true` | Evidence-only `freex-x64-macos-tfm-build-evidence.txt` with runner, workload, Xcode, and `net10.0-macos` compile markers |
| `freex-<run-id>-<run-attempt>-macos-preview-readiness` | After both runtime jobs | `macos-preview-readiness-manifest.json` and `macos-preview-readiness-summary.txt`, tying both runtime artifacts, diagnostics artifacts, digests, hashes, and evidence markers to one run |
| `freex-<run-id>-<run-attempt>-macos-release-assets` | `distribution_candidate=true` only | Stable production-candidate assets such as `FreeX-latest-macos-arm64.zip`, `FreeX-latest-macos-x64.zip`, checksums, evidence/log/instruction files, `FreeX-latest-macos-distribution-candidate-manifest.json`, and candidate instructions |

The publication job also creates a prerelease GitHub Release for the candidate run. Use the release-assets wrapper as the evidence source of truth because it is tied to the run id and run attempt and is validated by the preflight.

## Download Layout

Use a clean artifact root per run attempt. This PowerShell example preserves wrapper names for the validators:

```powershell
$runId = "<run-id>"
$attempt = "<run-attempt>"
$root = "artifacts/macos-preview"

New-Item -ItemType Directory -Force -Path $root | Out-Null

gh run download $runId -n "freex-$runId-$attempt-osx-arm64-macos-app" -D "$root/freex-$runId-$attempt-osx-arm64-macos-app"
gh run download $runId -n "freex-$runId-$attempt-osx-arm64-macos-diagnostics" -D "$root/freex-$runId-$attempt-osx-arm64-macos-diagnostics"
gh run download $runId -n "freex-$runId-$attempt-osx-x64-macos-app" -D "$root/freex-$runId-$attempt-osx-x64-macos-app"
gh run download $runId -n "freex-$runId-$attempt-osx-x64-macos-diagnostics" -D "$root/freex-$runId-$attempt-osx-x64-macos-diagnostics"
gh run download $runId -n "freex-$runId-$attempt-macos-preview-readiness" -D "$root/freex-$runId-$attempt-macos-preview-readiness"
gh run download $runId -n "freex-$runId-$attempt-macos-release-assets" -D "$root/freex-$runId-$attempt-macos-release-assets"
```

If GitHub returns 401 or an artifact-not-found error, confirm that the browser or `gh` session is authenticated, the run attempt is correct, and the requested artifact was produced by a successful job.

## Evidence Preflight Without A Mac

From the repository root, validate the downloaded hosted evidence before asking Mac testers to spend time on it:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-MacOsPublicPreviewReadiness.ps1 -ArtifactRoot artifacts/macos-preview -ExpectedRunId <run-id> -ExpectedRunAttempt <run-attempt> -DistributionCandidate -RequireSeparateDiagnosticsArtifact -RequireAggregateReadinessArtifact -RequireReleasePublicationArtifact
```

This preflight checks both runtimes and rejects stale or mixed-run downloads. For a production candidate, require these markers in the runtime evidence and release-assets manifest:

- `artifact_channel=distribution-candidate`
- `distribution_candidate=true`
- `distribution_readiness=distribution_candidate_ready`
- `codesign_mode=developer-id`
- `codesign_verified=true`
- `notarization_status=accepted`
- `stapler_validated=true`
- `gatekeeper_assessment_status=accepted`
- `gatekeeper_assessment_source=Notarized Developer ID`
- `zip_sha256=<hash>`
- `smoke_status=passed`

The aggregate readiness artifact proves the hosted workflow revalidated both runtime artifacts and diagnostics wrappers from the same run. It is not a substitute for signing, notarization, release-assets publication, or human Finder/Gatekeeper validation.

## Human Validation Handoff Mode

After the hosted evidence preflight passes, generate a run-specific handoff from the same artifact root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-MacOsPublicPreviewPromotion.ps1 -ArtifactRoot artifacts/macos-preview -ChecklistRoot artifacts/macos-preview -ExpectedRunId <run-id> -ExpectedRunAttempt <run-attempt> -PrepareHumanValidationHandoff
```

Handoff mode intentionally does not require completed checklist files yet. It first validates hosted production evidence, then prints:

- The checklist template path, `docs/release/macos-public-preview-checklist.md`.
- The expected release-assets wrapper name.
- The expected app and diagnostics wrapper names for `osx-arm64` and `osx-x64`.
- The exact completed checklist paths:
  - `artifacts/macos-preview/completed-macos-public-preview-checklist-osx-arm64.md`
  - `artifacts/macos-preview/completed-macos-public-preview-checklist-osx-x64.md`
- The per-runtime checklist validation commands.
- The final promotion command to run after both completed checklists pass.

Copy the checklist template to the printed runtime-specific paths and give each copy, the matching app artifact wrapper, diagnostics artifact wrapper, release-assets wrapper, run id, run attempt, and commit SHA to the Mac tester. Keep the wrapper names unchanged so the completed checklist can be tied back to the same hosted run.

## What Hosted Runners Prove

The hosted workflow and Windows preflight can prove:

- The app project builds on hosted macOS for `osx-arm64` and `osx-x64`.
- `FreeX.app` bundle metadata, icon, executable layout, document types, runtime architecture, checksum files, and zip hashes are valid.
- Developer ID signing, accepted notarization, stapling, and Gatekeeper `spctl` assessment completed when `distribution_candidate=true`.
- Packaging smoke, LaunchServices launch smoke, Open-With smoke, default-open smoke, command-key smoke, and selected workbook smoke markers were captured.
- Diagnostics artifacts, aggregate readiness evidence, and release-assets publication are tied to one run id and attempt.

## What Remains Human-Only

A production candidate still needs a person on real macOS hardware to validate:

- Browser-downloaded artifact behavior with quarantine preserved.
- Finder double-click first launch and the exact Gatekeeper prompt or absence of a prompt.
- `.fxl` default handler setup, Finder double-click open, Open With, drag/drop, and already-running app behavior.
- Basic workbook create, edit, save, Save As, dirty-close, reopen, and recent-file workflows in a real user session.
- Keyboard-only operation and VoiceOver smoke, including known accessibility issues and release-owner blocking decisions.
- Screenshots, prompt text, terminal transcripts, and diagnostics review for anything attached to the release record.
- The final public-preview decision and announcement. The workflow can create candidate assets, but it cannot decide that human validation is acceptable.

Do not work around a production candidate by clearing quarantine, disabling Gatekeeper, removing extended attributes, or instructing testers to use Control-click/right-click Open. Those workarounds are acceptable only for internal previews and must mark the build internal-only.

## Promotion After Human Validation

After each Mac tester completes the matching checklist copy, validate the checklist files from Windows or another checkout:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-MacOsHumanValidationChecklist.ps1 -ChecklistPath artifacts/macos-preview/completed-macos-public-preview-checklist-osx-arm64.md -ExpectedRuntime osx-arm64 -ExpectedRunId <run-id> -ExpectedRunAttempt <run-attempt>
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-MacOsHumanValidationChecklist.ps1 -ChecklistPath artifacts/macos-preview/completed-macos-public-preview-checklist-osx-x64.md -ExpectedRuntime osx-x64 -ExpectedRunId <run-id> -ExpectedRunAttempt <run-attempt>
```

Then run the combined promotion gate:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-MacOsPublicPreviewPromotion.ps1 -ArtifactRoot artifacts/macos-preview -ChecklistRoot artifacts/macos-preview -ExpectedRunId <run-id> -ExpectedRunAttempt <run-attempt>
```

Only after this passes should a release owner treat the hosted macOS artifacts as public-preview eligible.
