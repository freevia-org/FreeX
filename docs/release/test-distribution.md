# FreeX Test Distribution Plan

## Phase Status

| Phase | Status | Scope |
| --- | --- | --- |
| 1. Shareable builds | Complete | Framework-dependent user-test builds publish into `artifacts/releases` with version, timestamp, commit, runtime, and mode in the file name. |
| 2. Feedback intake | Complete | The old May 24 user-test and retest reports were retired after their findings were resolved and absorbed into regression coverage/status history; GitHub issues now include a structured user-test report template for new feedback. |
| 3. Local diagnostics | Complete | Test builds record local JSONL usage events and crash reports under `%LOCALAPPDATA%\FreeX\Diagnostics`. Those files are not automatically uploaded; the separate Phase 5 transport may send an opt-in crash event. |
| 4. Hosted release channel | Complete | GitHub Actions publishes latest builds through GitHub Releases with versioned artifacts, a stable latest test build link, and an MSIX package that is signed when release certificate secrets are configured. |
| 5. Crash analytics | Complete | Opt-in Sentry crash upload is wired behind tester consent and `FREEX_SENTRY_DSN`; local diagnostics remain available without network upload. |
| 6. Lightweight usage analytics | Complete | Stabilization-only app usage events are recorded through the existing diagnostics pipeline and safe crash breadcrumbs. |
| 7. Auto-update readiness | Complete | Help exposes the stable latest release page, and Velopack-managed installs can check, download, apply, and restart into an update; plain single-file and MSIX builds retain the manual latest-download path. |
| 8. Accessibility validation | Complete | UIA AutomationProperties audit completed; `GridView`/`SheetGrid` exposes grid, selection, visible cell grid-item, value, and selection-item provider contracts; `TabChrome` name binding is fixed; automated UIA property and `GridViewAutomationPeerTests` guards cover the current contracts. Every public-preview candidate still needs a live keyboard-only smoke pass, screen-reader smoke pass, and UI Automation catalog review recorded in release notes. |

## Phase 4 Release Channel

Stable latest non-prerelease tester downloads:

https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-win-x64.exe

https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-win-x64.msix

https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-macos-arm64.zip

https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-macos-x64.zip

GitHub's `releases/latest` redirect remains on the latest non-prerelease tester build.

Latest verified tester release:

- Release: [FreeX (Test Release) v0.8.127](https://github.com/tony-xmelon/FreeX/releases/tag/v0-8-127-2026-06-20-19-57-44-run127-attempt1%2B1790d2ab)
- Tag: `v0-8-127-2026-06-20-19-57-44-run127-attempt1+1790d2ab`
- GitHub Actions run: [27881901112](https://github.com/tony-xmelon/FreeX/actions/runs/27881901112), run number 127 attempt 1, completed successfully
- Target commit: `1790d2abdc7112047382c3f19fcb417eb0227059`
- Release posture: stable latest internal tester release; not a public-preview candidate because live keyboard-only, screen-reader, UIA catalog, and known-issues accessibility gate evidence was not recorded
- Asset check: versioned Windows `.exe`, stable-name Windows `.exe`, versioned MSIX, stable-name MSIX, stable macOS arm64/x64 preview zips, Velopack-style assets, and matching checksum assets were published by the workflow after successful hosted release-gate verification. GitHub marked this non-prerelease as latest, so the stable latest Windows and macOS download links resolve through this release.
- Prior reference point: the older v0.8.114/run 114 release remains a June 12 historical baseline. Current release decisions should use v0.8.127/run 127 unless a later successful tester release supersedes it.

The `Tester Release` GitHub Actions workflow runs repository preflight, restore, build, and the manifest-defined FreeX release gate before publishing a framework-dependent single-file Windows x64 `.exe` plus an MSIX package. The release gate inherits the FreeX commit suites and adds release-only render evidence; it is distinct from commit gates, which exclude visual evidence, packaging, signing, and publication. Windows tester releases are standalone by default: `include_macos_preview=false` means the workflow does not require or query macOS App Preview artifacts. When `include_macos_preview=true`, it finds or uses the requested successful `macOS App Preview` run for the same commit, downloads both runtime app artifacts, and attaches stable macOS internal-preview assets to the same GitHub Release. It uses normal .NET restore/build caching and parallelism for speed and uploads the gate TRX results for every run, including failed release-gate attempts, then uploads both versioned artifacts produced by `tools/Publish-UserTestBuild.ps1` and stable latest assets:

- `FreeX-latest-win-x64.exe`
- `FreeX-latest-win-x64.exe.sha256`
- `FreeX-latest-win-x64.msix`
- `FreeX-latest-win-x64.msix.sha256`

The release also receives the Velopack installer/portable/self-update artifacts produced by `tools/Publish-UserTestBuild.ps1 -PublishMode Velopack` (`vpk pack --packId FreeXApp --packTitle FreeX --channel win`), staged from `artifacts/velopack/*`: the generated installer, portable zip, full `.nupkg`, and the `RELEASES`/assets feed that installed clients poll to self-update. Velopack controls the version-dependent filenames, so release automation discovers and stages the generated files rather than depending on one provisional name. `packId` is deliberately `FreeXApp` (not `FreeX`) so Velopack's per-machine install/data directory never collides with the app's own `%LocalAppData%\FreeX` data directory; `packTitle` stays `FreeX` so the Start Menu/Programs-and-Features display name is unchanged.

When macOS bundling is explicitly enabled, the same release also receives:

- `FreeX-latest-macos-arm64.zip`
- `FreeX-latest-macos-arm64.zip.sha256`
- `FreeX-latest-macos-x64.zip`
- `FreeX-latest-macos-x64.zip.sha256`
- `FreeX-latest-macos-<runtime>-instructions.md`
- `FreeX-latest-macos-<runtime>-evidence.txt`

Release dispatches must run from `main` or an isolated `codex/daily-tester-release-*` branch because the workflow publishes stable latest assets, and a workflow-level `tester-release` concurrency group prevents overlapping dispatches from moving `latest` backward. Use the daily branch path only for a frozen verified candidate when `origin/main` has already advanced with work intentionally deferred to the next release.

The hosted MSIX publish path signs the package when `FREEX_MSIX_CERTIFICATE_BASE64` is configured, with optional `FREEX_MSIX_CERTIFICATE_PASSWORD` and `FREEX_MSIX_TIMESTAMP_URL` inputs. Until a release certificate is available, the workflow passes `-AllowUnsignedMsix` and publishes an unsigned MSIX for tester continuity. `tools/Publish-UserTestBuild.ps1` derives the manifest `Publisher` from the signing certificate subject when signing is enabled, while direct local unsigned MSIX output still requires explicitly passing `-AllowUnsignedMsix`. Installer trust validation and Store-style submission remain release-gate work.

Windows tester steps:

1. Download `FreeX-latest-win-x64.exe` and `FreeX-latest-win-x64.exe.sha256` from the release.
2. Verify the checksum with PowerShell: `Get-FileHash .\FreeX-latest-win-x64.exe -Algorithm SHA256`, then compare it with the `.sha256` file.
3. Run `FreeX-latest-win-x64.exe`. If Windows SmartScreen warns about an unknown publisher, continue only if the checksum matches the GitHub Release asset and the tester expected this internal build.
4. Prefer the `.exe` for normal testing. Use the MSIX only for package/install validation; unsigned MSIX packages may need trusted internal-test machine settings until signing is configured. Use the Velopack `FreeXApp-win-Setup.exe` installer or `FreeXApp-win-Portable.zip` portable build to validate the installed/self-update path; installs through Velopack land in a separate `FreeXApp` data directory from the app's own `%LocalAppData%\FreeX` diagnostics/recovery data.

macOS tester release steps while Developer ID/notarization is pending:

1. Download `FreeX-latest-macos-arm64.zip` for Apple Silicon Macs or `FreeX-latest-macos-x64.zip` for Intel Macs, plus the matching `.sha256`.
2. Verify the checksum from Terminal: `shasum -a 256 -c FreeX-latest-macos-arm64.zip.sha256`, or use the x64 checksum file on Intel.
3. Extract the app with Finder/Archive Utility, or run `ditto -x -k FreeX-latest-macos-arm64.zip .` from Terminal.
4. Open `FreeX.app`. If macOS blocks the unsigned/internal preview, use Control-click or right-click > Open. If needed, open System Settings > Privacy & Security and choose Open Anyway.
5. Do not disable Gatekeeper globally. These macOS builds remain internal previews until Developer ID signing, notarization, and stapling evidence are attached.

Tester-facing warning for both platforms: this is an internal preview while signing certificates are pending. macOS and Windows may warn that the publisher cannot be verified. Only open the build if it was expected from the FreeX team and the checksum matches the release asset.

Default tester versions come from `release/progress.json`: the current `overallCompletion` value maps to a minor-version band, and the GitHub run number becomes the patch number. At 95% completion, default tester releases use the `v0.8.<run>` stream. Manual `release_version` overrides remain available for special validation builds.

Current release gate: do not treat a new tester release as available until the workflow completes successfully through repository preflight, build, the manifest-defined release test gate, test-result artifact collection, release metadata, artifact upload, optional macOS preview artifact bundling when requested, and GitHub release publication. See [testing/test-gates.md](../testing/test-gates.md) for the commit versus release gate contract.

Before dispatching a candidate, run `tools/Test-TesterReleaseReadiness.ps1` from the repo root to preflight `release/progress.json`, workflow accessibility inputs, release docs, and checklist alignment. For a public-preview candidate, include `-PublicPreviewCandidate -AccessibilityKeyboardOnly -AccessibilityScreenReader -AccessibilityUiaCatalog -AccessibilityKnownIssues`; otherwise the preflight reports the build as internal-only.

Use [release/tester-release-checklist.md](tester-release-checklist.md) as the operator checklist for release-gate evidence and public-preview accessibility notes. The `Tester Release` workflow exposes `public_preview_candidate` plus four accessibility evidence inputs; public-preview promotion fails unless keyboard-only, screen-reader, UI Automation catalog, and known-issues review inputs are all completed.

For the full suite release map across FreeX, FreeW, and FreeP, see [app-platform-publish-lanes.md](app-platform-publish-lanes.md). Each app/platform lane is independent so Windows, Linux, and macOS packages can be built or rerun separately.

## Commit Gate Verification

Run the manifest-driven commit gate from the repository root. It selects only the projects assigned
to an app and platform, serializes project execution for UI/resource isolation, and writes a
separate TRX result per project:

0. `pwsh -NoProfile -File tools/Test-RepositoryPreflight.ps1`
1. `pwsh -NoProfile -File tools/Invoke-TestGate.ps1 -Gate commit -App FreeX -Platform windows`
2. `pwsh -NoProfile -File tools/Invoke-TestGate.ps1 -Gate commit -App FreeW -Platform linux`
3. `pwsh -NoProfile -File tools/Invoke-TestGate.ps1 -Gate commit -App FreeP -Platform macos`

CI runs each app's commit gate on Windows, Linux, and macOS. Windows includes desktop WPF coverage;
Linux and macOS include only portable core, contract, and Avalonia projects. The separate
`FreeX.DefaultTests.slnx` and `FreeX.UiTests.slnx` files remain build-grouping aids, not executable
test gates. See [testing/test-gates.md](../testing/test-gates.md) for the complete ownership map.

A separate `macOS App Preview` workflow builds and publishes `src/FreeX.App.Avalonia` on architecture-specific hosted macOS runners for `osx-arm64` and `osx-x64`, wraps the output in `FreeX.app` with `FreeX.icns`, verifies bundle metadata, ad-hoc signs by default, optionally Developer ID signs/notarizes when secrets are configured, self-checks each SHA-256 file with `shasum -a 256 -c`, records `zip_sha256` in evidence, and uploads zipped app artifacts, checksum files, tester instructions, smoke evidence, separate diagnostics artifacts, and a post-matrix aggregate readiness artifact. The Windows-runnable `tools/Test-MacOsAppReadiness.ps1` preflight statically checks the app project, `Info.plist`, icon asset, workflow markers, source wiring, and portable-source hygiene. After hosted artifacts are downloaded and unzipped, the Windows-runnable `tools/Test-MacOsPublicPreviewReadiness.ps1` preflight validates both runtime evidence bundles, checksum files, LaunchServices/Open-With/default-open smoke, startup smoke, command key smoke, hosted dialog smoke, Format Cells roundtrip evidence, diagnostics artifact file sets, tester instructions, distribution-candidate signing/notarization/stapler evidence, and release publication artifacts when required for promotion. File-access grant diagnostics in those artifacts are instrumentation/readiness evidence only; hosted CI must not be treated as proof of real macOS security-scoped access to user-selected workbook files.

Use [release/macos-hosted-app-production.md](macos-hosted-app-production.md) for the maintainer sequence to produce macOS app artifacts on hosted GitHub macOS runners without a local Mac, preserve the app/diagnostics/aggregate/release artifact wrappers, run evidence preflight, and generate the human-validation handoff. Use [release/macos-signing-notarization.md](macos-signing-notarization.md) to configure Developer ID signing secrets and record the expected `codesign_mode=developer-id`, `notarization_status=accepted`, and `stapler_validated=true` evidence before treating a macOS artifact as externally distributable.

### macOS Hosted Artifact Retrieval

GitHub-hosted macOS runners can produce downloadable macOS app artifacts without local macOS hardware. Open GitHub Actions > `macOS App Preview` > the completed run, then download each runtime artifact from the run summary:

- `freex-<run-id>-<run-attempt>-osx-arm64-macos-app`
- `freex-<run-id>-<run-attempt>-osx-arm64-macos-diagnostics`
- `freex-<run-id>-<run-attempt>-osx-x64-macos-app`
- `freex-<run-id>-<run-attempt>-osx-x64-macos-diagnostics`
- `freex-<run-id>-<run-attempt>-macos-preview-readiness`
- `freex-<run-id>-<run-attempt>-macos-release-assets` for distribution-candidate dispatches

GitHub may show public workflow metadata without sign-in, but Actions artifact archive downloads require authentication. Use a browser session signed in to GitHub, `gh run download` after `gh auth login`, or a request with an appropriate `GITHUB_TOKEN`; unauthenticated archive URLs can return HTTP 401 even for successful public runs.

Each download is a GitHub Actions artifact wrapper. Preserve the `freex-<run-id>-<run-attempt>-<runtime>-macos-app`, `freex-<run-id>-<run-attempt>-<runtime>-macos-diagnostics`, and `freex-<run-id>-<run-attempt>-macos-release-assets` wrapper directory names under the artifact root. Also preserve the `freex-<run-id>-<run-attempt>-macos-preview-readiness` aggregate wrapper there, then use the inner app ZIP, checksum, tester instructions, evidence file, diagnostics files, smoke logs, readiness manifest, release manifest, and release instructions. Do not flatten wrapper contents directly into the artifact root; the Windows evidence validator uses wrapper names to detect duplicate stale downloads, split or stale aggregate/release-assets manifests/instructions, mixed runtime runs, and expected run identity mismatches. Internal-preview workflow outputs can also be bundled into the normal `Tester Release` GitHub Release when `include_macos_preview=true`; the tester-release workflow preserves the same-commit guard before attaching `FreeX-latest-macos-arm64.zip`, `FreeX-latest-macos-x64.zip`, matching checksum files, and per-runtime instruction/evidence files. Distribution-candidate dispatches also run a guarded publication job that prepares stable GitHub Release assets and uploads the macOS release-assets artifact.

After both runtime matrix jobs finish, the post-matrix `macos-preview-readiness` job downloads the current run's app and diagnostics artifact wrappers, runs `tools/Test-MacOsPublicPreviewReadiness.ps1 -RequireSeparateDiagnosticsArtifact` with the current `-ExpectedRunId` and `-ExpectedRunAttempt`, then uploads `freex-<run-id>-<run-attempt>-macos-preview-readiness`. That aggregate wrapper contains `macos-preview-readiness-manifest.json` and `macos-preview-readiness-summary.txt` with the run identity, commit, source artifact pattern, app and diagnostics artifact names and digests, per-runtime `zip_sha256`, `artifact_channel`, `distribution_readiness`, `smoke_status`, signing, notarization, stapler, and Gatekeeper markers. Keep it with the runtime wrappers as maintainer evidence that the hosted run produced and validated both architectures from one run. It is not a public distribution channel; distribution candidates still require Developer ID signing, accepted notarization, stapling, Gatekeeper assessment and first-launch evidence, and the guarded release-publication artifact.

Signed and internal ad-hoc outputs use the same artifact names. Treat `codesign_mode=ad-hoc` or a skipped notarization status as internal preview evidence only. External distribution requires `codesign_mode=developer-id`, `notarization_status=accepted`, `stapler_validated=true`, and a release-asset publication path.

Without local macOS hardware, Windows agents can run repository preflight and static macOS readiness checks, while hosted macOS runners can build the bundle, verify metadata and checksums, ad-hoc or Developer ID sign when configured, run native-architecture packaging and launch smoke, exercise LaunchServices, open a `.fxl` document without an app override as CI-verifiable default-open evidence, and capture evidence/logs. Hosted logs can show that file-access grant diagnostics are wired and redacted, but they do not confirm real sandbox security-scoped file access. Human validation of Finder double-click open, Gatekeeper prompts, basic workbook workflows, on-device file-access grant behavior, and candidate accessibility checks still needs a tester on macOS; record that pass with the [macOS public-preview human checklist](macos-public-preview-checklist.md), then validate the completed release-record copy with `tools/Test-MacOsHumanValidationChecklist.ps1` before promotion.

Windows agents can also validate downloaded hosted evidence without a Mac:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-MacOsPublicPreviewReadiness.ps1 -ArtifactRoot artifacts/macos-preview -ExpectedRunId <run-id> -ExpectedRunAttempt <run-attempt> -RequireSeparateDiagnosticsArtifact -RequireAggregateReadinessArtifact
```

`-ExpectedRunId` and `-ExpectedRunAttempt` are optional, but pass them when validating downloaded artifacts from a specific GitHub Actions run. Keep `-RequireSeparateDiagnosticsArtifact -RequireAggregateReadinessArtifact` on downloaded hosted evidence validation so the Windows preflight validates the diagnostics wrappers and the aggregate readiness wrapper beside the app artifacts. For public-preview candidates, run it with the same run identity and wrapper-validation flags plus `-DistributionCandidate -RequireReleasePublicationArtifact` after downloading the matching `freex-<run-id>-<run-attempt>-<runtime>-macos-diagnostics` artifacts, `freex-<run-id>-<run-attempt>-macos-preview-readiness` wrapper, and `freex-<run-id>-<run-attempt>-macos-release-assets` wrapper beside the app artifacts:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-MacOsPublicPreviewReadiness.ps1 -ArtifactRoot artifacts/macos-preview -ExpectedRunId <run-id> -ExpectedRunAttempt <run-attempt> -DistributionCandidate -RequireSeparateDiagnosticsArtifact -RequireAggregateReadinessArtifact -RequireReleasePublicationArtifact
```

When the hosted evidence is ready, the `freex-<run-id>-<run-attempt>-macos-preview-readiness` aggregate wrapper is preserved beside the runtime artifacts, and both runtime-specific human checklists have been completed as `completed-macos-public-preview-checklist-osx-arm64.md` and `completed-macos-public-preview-checklist-osx-x64.md` beside the artifacts, run the combined promotion gate. The wrapper reruns `tools/Test-MacOsPublicPreviewReadiness.ps1` with `-DistributionCandidate -RequireSeparateDiagnosticsArtifact -RequireAggregateReadinessArtifact -RequireReleasePublicationArtifact` before validating the human checklists:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-MacOsPublicPreviewPromotion.ps1 -ArtifactRoot artifacts/macos-preview -ChecklistRoot artifacts/macos-preview -ExpectedRunId <run-id> -ExpectedRunAttempt <run-attempt>
```

### macOS App Preview Tester Instructions

This is a preview validation path, not a public release channel. Use `osx-arm64` for Apple Silicon Macs and `osx-x64` for Intel Macs. GitHub downloads the result as an Actions artifact wrapper; unzip that wrapper first, then use the files inside it:

- `freex-osx-arm64-macos-app.zip` or `freex-osx-x64-macos-app.zip`
- the matching `.zip.sha256`
- `freex-<runtime>-macos-tester-instructions.md`
- `freex-<runtime>-macos-evidence.txt`
- packaging, LaunchServices, Open-With/default-open, and notarization logs

Before opening the app, testers should run this from the directory containing the inner app ZIP and checksum:

```bash
shasum -a 256 -c freex-<runtime>-macos-app.zip.sha256
```

The expected result is `<zip-name>: OK`. After the checksum passes, extract the inner app ZIP with Finder/Archive Utility, or run `ditto -x -k freex-<runtime>-macos-app.zip .` from Terminal, then open `FreeX.app`. This preserves macOS signing metadata from the bundle ZIP. When the app is attached directly to a tester release, use `FreeX-latest-macos-arm64.zip` or `FreeX-latest-macos-x64.zip` and the matching checksum name instead. If macOS Gatekeeper blocks the preview, testers should inspect `codesign_mode`, `notarization_status`, `stapler_validated`, and `zip_sha256` in the evidence file. Ad-hoc signed or non-notarized previews are internal validation artifacts and may require Control-click or right-click > Open on trusted test machines, or System Settings > Privacy & Security > Open Anyway after the first blocked launch. Public distribution still requires Developer ID signing, accepted notarization, stapling, and Gatekeeper evidence.

## Conservative Rerun Fallback

The old serial/no-build-server command shape is no longer the default because it makes routine verification significantly slower. Use it only as a one-time rerun after clearing stale processes when a command fails because of stale build-server, shared-compiler, node-reuse, or output-lock state:

`--disable-build-servers -p:UseSharedCompilation=false -p:NodeReuse=false /nr:false -m:1`

## Phase 3 Diagnostics Contract

FreeX writes a local diagnostics record. Those files stay on the tester machine
unless the tester attaches them to an issue. When the separate opt-in remote
crash transport is active, it sends its own privacy-filtered event; it does not
upload the local JSON/JSONL files.

- `events.jsonl` records app lifecycle events such as `app_start`, `app_ready`, `app_exit`, and `crash`.
- `CrashReports/*.json` records unhandled WPF dispatcher, AppDomain, and unobserved task exceptions.
- Crash exception messages and stack traces can occasionally contain sensitive values; review files before attaching them to an issue.
- Event properties are allowlisted so workbook paths and workbook contents are not written as analytics properties.
- Workbook file-access grant diagnostics, when present, are redacted lifecycle events only: `workbook_file_access_identity` and `workbook_file_access_scope` may include `grantKind` and `payloadRedacted`, and must not include file paths, filenames, workbook contents, formulas, or bookmark payloads.
- Set `FREEX_DIAGNOSTICS=0` before launching FreeX to disable local diagnostics for that run.

## Phase 5 Crash Analytics Contract

Remote crash analytics are off by default. They activate only when all of these are true:

- A Sentry DSN is present through the release build configuration or the `FREEX_SENTRY_DSN` environment override.
- The tester opts in from the first-launch crash report prompt or later through `Options > Trust Center`.
- `FREEX_CRASH_ANALYTICS` is not set to `0`.

An explicit `FREEX_CRASH_ANALYTICS=1` test/runtime override is available for
controlled validation, but public packaging must not use it to bypass the
tester's saved consent choice. A DSN alone does not enable uploads. The
suite-wide contract and validation gate are in
[public-preview-readiness.md](public-preview-readiness.md).

Remote crash reports include app version, runtime, operating system, process architecture, session ID, exception type, message, stack trace, and safe breadcrumbs from allowlisted app events. They do not intentionally collect workbook contents, formulas, filenames, or paths, but exception messages and stack traces can occasionally contain sensitive values.

## Phase 6 Lightweight Usage Analytics Contract

Lightweight usage analytics reuse the same local diagnostics pipeline and, when crash analytics is enabled, the same safe Sentry breadcrumb path. They are meant only to help stabilize tester builds.

- Recorded categories are app lifecycle, command/dialog opened, file import/export type, and crash/session linkage.
- Event properties are allowlisted to include coarse labels such as command name, dialog type, file type, format, scope, status, reason, source, and worksheet count.
- File-access grant evidence is limited to redacted lifecycle metadata such as `grantKind` and `payloadRedacted`; it does not include workbook file paths, filenames, contents, formulas, or bookmark payloads.
- These events do not intentionally collect workbook contents, formulas, filenames, or paths.
- Crash-linked exception messages and stack traces can occasionally contain sensitive values; review local crash reports before sharing them.
- Set `FREEX_DIAGNOSTICS=0` before launching FreeX to disable local usage diagnostics for that run. Remote crash breadcrumbs remain gated by Phase 5 crash analytics consent and `FREEX_SENTRY_DSN`.

## Phase 7 Auto-Update Readiness Contract

`Help > Check for Updates` opens the stable latest release page so testers can manually compare or download the newest build without hunting through GitHub. It records a safe `update_check_opened` diagnostics event with source `help`.

Full in-app updates are now implemented through Velopack: `FreeX.App.Host`'s `Program.Main` runs `VelopackBootstrap.Configure().Run()` before WPF initializes (handling install/update/uninstall hooks, including re-registering Windows file associations), and `VelopackUpdateService` (`FreeX.App.Services/Updates/VelopackUpdateService.cs`) is wired as `IUpdateService` in both the WPF host (`App.xaml.cs`) and the Avalonia app (`FreeX.App.Avalonia/App.cs`, `MainWindow.cs`) to check, download, and apply updates with a restart. This only applies to installs made through the Velopack installer/portable path (`FreeXApp-win-Setup.exe` / `FreeXApp-win-Portable.zip`); the plain `FreeX-latest-win-x64.exe` single-file build and MSIX installs are not Velopack-managed and still rely on the manual `Help > Check for Updates` latest-download loop.

## Phase 8 Accessibility Validation Gate

Before a tester build is promoted beyond internal validation, record an accessibility pass in the release notes. The pass must include:

- Keyboard-only smoke validation for workbook open/save, grid navigation/editing, ribbon tab traversal, context menus, dialogs, sheet tabs, and Help.
- Screen-reader smoke validation for first launch, workbook grid focus, formula bar edits, dialog titles/default buttons, warning messages, and accessibility checker results.
- UI Automation catalog review for stable names, automation IDs, invoke patterns, and focus order on newly changed controls.
- A known-issues section for any accessibility defect deferred from the candidate, with the affected workflow and planned follow-up.

If any required item is skipped, mark the tester build as internal-only and do not publish it as a public-preview candidate.

### Accessibility Gate Audit — 2026-05-28

**Gaps found and fixed in this pass:**

1. **Sheet tab `TabChrome` Grid missing UIA name** — The `ItemsControl` DataTemplate that renders each sheet tab had a focusable `Grid` with no `AutomationProperties.Name`. Keyboard users reaching sheet tabs via F6 received no announcement from Narrator. Fixed: `AutomationProperties.Name="{Binding Name}"` and `AutomationProperties.HelpText` added.

2. **`GridView` (`SheetGrid`) missing UIA name and worksheet patterns** — The custom `FrameworkElement`-derived grid originally exposed a generic FrameworkElement peer with no meaningful control type, name, cell peers, grid pattern, value pattern, or selection pattern. Fixed: `AutomationProperties.Name="Worksheet"` added in XAML, `OnCreateAutomationPeer` returns a custom grid peer, and visible cell peers expose grid-item, value, and selection-item providers.

**Already well-covered:**

- QAT buttons (Save, Undo, Redo): `AutomationProperties.Name` set in XAML.
- System chrome buttons (Minimize, Maximize/Restore, Close): `AutomationProperties.Name` set in XAML.
- `RibbonTooltip.Title` propagates to `AutomationProperties.Name` at runtime for all ribbon buttons lacking an explicit name attribute.
- Formula Bar, Name Box: explicit `AutomationProperties.Name`, `HelpText`, and `AutomationId` set.
- Vertical and Horizontal scroll bars: `AutomationProperties.Name` and `HelpText` set.
- Zoom Slider and Zoom Text: `AutomationProperties.Name` and `HelpText` set.
- Add Sheet button: explicit `AutomationProperties.Name` and `HelpText` set.
- Key dialogs (Accessibility Checker, Spell Check, Color Picker, Workbook Statistics, Chart dialogs, etc.): extensive UIA name/help-text/automation-id coverage verified by `ReviewDialogFocusAccessibilityTests`, the shared `UiAutomationCatalogSnapshotHarness`, and dialog-specific tests.
- F6 shell focus cycle: worksheet → ribbon → formula bar → sheet tabs → status bar traversal proven by `ShellFocusCyclePlannerTests` and live host coverage.
- `KeyboardNavigation.TabNavigation` properties on RibbonTabs and task panes: verified by `MainWindowXamlKeyTipTests`.
- `AutomationInvokeButton` override: Insert Function and Backstage entry-point buttons expose `InvokePattern`.
- `AccessibilityCheckerService`: model-level issues (merged cells, missing alt text, generic alt text, chart titles, hyperlink text, hidden content, contrast) covered by `AccessibilityCheckerServiceTests`.

**UIA catalog automated guards added (`MainWindowUiaPropertiesTests` and `GridViewAutomationPeerTests`):**

- Formula bar, name box, scroll bars, zoom slider — name/help-text/automation-id present.
- `SheetGrid` GridView — `AutomationProperties.Name="Worksheet"` set in XAML.
- Sheet tab `TabChrome` — `AutomationProperties.Name` bound to sheet name.
- `GridView.OnCreateAutomationPeer` override present (source check).
- `GridViewAutomationPeer` exposes visible cells as grid items with values and selection state.

**Known deferred items (not blocking public preview):**

- Pixel-perfect Narrator navigation polish, such as richer row/column header announcements, remains a live screen-reader validation item. The grid and visible-cell UIA provider contracts themselves are implemented and covered by automated tests.
- Status bar statistics text blocks (`Average`, `Count`, `Sum`, etc.) are display-only (not keyboard focus stops) and do not require UIA names for this gate; they are readable via screen reader browse mode from context.
- Remaining Phase 8 items (interactive screen-reader and keyboard smoke passes requiring a live session with Narrator) must be executed before a public-preview build is tagged.

## Velopack Auto-Update Status

Velopack packaging and the in-app update check/download/apply-and-restart flow are implemented (see Phase 7 above and `src/FreeX.App.Host/VelopackBootstrap.cs`, `src/FreeX.App.Services/Updates/VelopackUpdateService.cs`). Remaining follow-up work is release-process hardening (e.g. delta packages across releases; the current `vpk pack` invocation only produces full packages) rather than adding the mechanism itself.

## Tester Report Flow

1. Download the latest user-test build.
2. Run the `.exe`; install the Microsoft .NET Desktop Runtime if Windows prompts for it.
3. Report issues through the GitHub "FreeX user test report" template.
4. Attach `%LOCALAPPDATA%\FreeX\Diagnostics\events.jsonl` or `CrashReports/*.json` only when useful, after checking that the attachment contains no private information.
