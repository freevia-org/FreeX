# FreeX Test Distribution Plan

## Phase Status

| Phase | Status | Scope |
| --- | --- | --- |
| 1. Shareable builds | Complete | Framework-dependent user-test builds publish into `artifacts/releases` with version, timestamp, commit, runtime, and mode in the file name. |
| 2. Feedback intake | Complete | The old May 24 user-test and retest reports were retired after their findings were resolved and absorbed into regression coverage/status history; GitHub issues now include a structured user-test report template for new feedback. |
| 3. Local diagnostics | Complete | Test builds record local JSONL usage events and crash reports under `%LOCALAPPDATA%\FreeX\Diagnostics`. No network upload is performed. |
| 4. Hosted release channel | Complete | GitHub Actions publishes latest builds through GitHub Releases with versioned artifacts, a stable latest test build link, and an MSIX package that is signed when release certificate secrets are configured. |
| 5. Crash analytics | Complete | Opt-in Sentry crash upload is wired behind tester consent and `FREEX_SENTRY_DSN`; local diagnostics remain available without network upload. |
| 6. Lightweight usage analytics | Complete | Stabilization-only app usage events are recorded through the existing diagnostics pipeline and safe crash breadcrumbs. |
| 7. Auto-update readiness | Complete | Help now exposes the stable latest release page while full in-app update packaging remains deferred. |
| 8. Accessibility validation | Complete | UIA AutomationProperties audit completed; `GridView`/`SheetGrid` exposes grid, selection, visible cell grid-item, value, and selection-item provider contracts; `TabChrome` name binding is fixed; automated UIA property and `GridViewAutomationPeerTests` guards cover the current contracts. Every public-preview candidate still needs a live keyboard-only smoke pass, screen-reader smoke pass, and UI Automation catalog review recorded in release notes. |

## Phase 4 Release Channel

Latest tester downloads:

https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-win-x64.exe

https://github.com/tony-xmelon/FreeX/releases/latest/download/FreeX-latest-win-x64.msix

The `Tester Release` GitHub Actions workflow runs repository preflight, restore, build, the default test lane, and the UI test lane before publishing a framework-dependent single-file Windows x64 `.exe` plus an MSIX package. It preserves `default-tests.trx` and `ui-tests.trx` results for every run, including failed release-gate attempts, then uploads both versioned artifacts produced by `tools/Publish-UserTestBuild.ps1` and stable latest assets:

- `FreeX-latest-win-x64.exe`
- `FreeX-latest-win-x64.exe.sha256`
- `FreeX-latest-win-x64.msix`
- `FreeX-latest-win-x64.msix.sha256`

Release dispatches must run from `main` because the workflow publishes stable latest assets, and a workflow-level `tester-release` concurrency group prevents overlapping dispatches from moving `latest` backward.

The hosted MSIX publish path signs the package when `FREEX_MSIX_CERTIFICATE_BASE64` is configured, with optional `FREEX_MSIX_CERTIFICATE_PASSWORD` and `FREEX_MSIX_TIMESTAMP_URL` inputs. Until a release certificate is available, the workflow passes `-AllowUnsignedMsix` and publishes an unsigned MSIX for tester continuity. `tools/Publish-UserTestBuild.ps1` derives the manifest `Publisher` from the signing certificate subject when signing is enabled, while direct local unsigned MSIX output still requires explicitly passing `-AllowUnsignedMsix`. Installer trust validation and Store-style submission remain release-gate work.

Default tester versions come from `release/progress.json`: the current `overallCompletion` value maps to a minor-version band, and the GitHub run number becomes the patch number. At 95% completion, default tester releases use the `v0.8.<run>` stream. Manual `release_version` overrides remain available for special validation builds.

Current release gate: do not treat a new tester release as available until the workflow completes successfully through repository preflight, restore, build, default tests, UI tests, test-result artifact collection, release metadata, artifact upload, and GitHub release publication.

Before dispatching a candidate, run `tools/Test-TesterReleaseReadiness.ps1` from the repo root to preflight `release/progress.json`, workflow accessibility inputs, release docs, and checklist alignment. For a public-preview candidate, include `-PublicPreviewCandidate -AccessibilityKeyboardOnly -AccessibilityScreenReader -AccessibilityUiaCatalog -AccessibilityKnownIssues`; otherwise the preflight reports the build as internal-only.

Use [release/tester-release-checklist.md](tester-release-checklist.md) as the operator checklist for release-gate evidence and public-preview accessibility notes. The `Tester Release` workflow exposes `public_preview_candidate` plus four accessibility evidence inputs; public-preview promotion fails unless keyboard-only, screen-reader, UI Automation catalog, and known-issues review inputs are all completed.

## Default Agent Build Verification

Run these commands from the repository root when validating routine agent work or a non-UI build-lane slice:

0. `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Test-RepositoryPreflight.ps1`
1. `dotnet restore FreeX.slnx`
2. `dotnet build FreeX.slnx --configuration Release --no-restore --disable-build-servers -p:UseSharedCompilation=false -p:NodeReuse=false /nr:false -m:1`
3. `dotnet test FreeX.DefaultTests.slnx --configuration Release --no-build --logger "trx;LogFileName=default-tests.trx" --disable-build-servers -p:UseSharedCompilation=false -p:NodeReuse=false /nr:false -m:1`

Default agent verification does not run the UI lane and does not use `dotnet test FreeX.slnx`. Success means the repository preflight validates tracked JSON/XML-backed files, tool scripts, workflows, local .NET SDK readiness against the tester-release SDK band, .NET project references, solution membership, and generated docs; restore exits cleanly; the Release solution build reports zero errors; and the default Release test lane reports zero failed tests. If output files are locked by a stale `dotnet`, `MSBuild`, `VBCSCompiler`, or `testhost` process from another local run, clear the stale process and rerun the same command before treating the build as failed.

## UI Lane Verification

Run the UI lane separately only when a task explicitly requests UI tests, touches WPF app/host behavior or UI test infrastructure, changes UI documentation/inventory, or prepares a tester-release/public-preview candidate:

0. `dotnet test FreeX.UiTests.slnx --configuration Release --no-build --logger "trx;LogFileName=ui-tests.trx" --disable-build-servers -p:UseSharedCompilation=false -p:NodeReuse=false /nr:false -m:1`

Success means the UI Release test lane reports zero failed tests. The `Tester Release` workflow remains a full release gate and still runs both the default and UI test lanes before publishing.

## Phase 3 Diagnostics Contract

FreeX writes tester diagnostics locally only. Files stay on the tester machine unless the tester attaches them to an issue.

- `events.jsonl` records app lifecycle events such as `app_start`, `app_ready`, `app_exit`, and `crash`.
- `CrashReports/*.json` records unhandled WPF dispatcher, AppDomain, and unobserved task exceptions.
- Crash exception messages and stack traces can occasionally contain sensitive values; review files before attaching them to an issue.
- Event properties are allowlisted so workbook paths and workbook contents are not written as analytics properties.
- Set `FREEX_DIAGNOSTICS=0` before launching FreeX to disable local diagnostics for that run.

## Phase 5 Crash Analytics Contract

Remote crash analytics are off by default. They activate only when all of these are true:

- The tester build is launched with `FREEX_SENTRY_DSN` set to the Sentry DSN.
- The tester opts in from the first-launch crash report prompt or later through `Options > Trust Center`.
- `FREEX_CRASH_ANALYTICS` is not set to `0`.

Remote crash reports include app version, runtime, operating system, process architecture, session ID, exception type, message, stack trace, and safe breadcrumbs from allowlisted app events. They do not intentionally collect workbook contents, formulas, filenames, or paths, but exception messages and stack traces can occasionally contain sensitive values.

## Phase 6 Lightweight Usage Analytics Contract

Lightweight usage analytics reuse the same local diagnostics pipeline and, when crash analytics is enabled, the same safe Sentry breadcrumb path. They are meant only to help stabilize tester builds.

- Recorded categories are app lifecycle, command/dialog opened, file import/export type, and crash/session linkage.
- Event properties are allowlisted to include coarse labels such as command name, dialog type, file type, format, scope, status, reason, source, and worksheet count.
- These events do not intentionally collect workbook contents, formulas, filenames, or paths.
- Crash-linked exception messages and stack traces can occasionally contain sensitive values; review local crash reports before sharing them.
- Set `FREEX_DIAGNOSTICS=0` before launching FreeX to disable local usage diagnostics for that run. Remote crash breadcrumbs remain gated by Phase 5 crash analytics consent and `FREEX_SENTRY_DSN`.

## Phase 7 Auto-Update Readiness Contract

`Help > Check for Updates` opens the stable latest release page so testers can manually compare or download the newest build without hunting through GitHub. It records a safe `update_check_opened` diagnostics event with source `help`.

Full in-app updates remain deferred until the manual latest-download loop is proven. The intended implementation path is Velopack, which requires early startup initialization through a custom `Main`, release packaging with the `vpk` tooling, and hosted update packages. Until that packaging work is added there is no background update download, no automatic install, and no restart-on-update behavior.

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

## Future Velopack auto-update work

When tester adoption justifies automatic update prompts, add Velopack packaging as a new distribution phase. That work should package `.nupkg`/release metadata alongside the tester `.exe`, initialize Velopack before WPF startup, check for updates only after user-visible consent, and keep the manual latest-release fallback in Help.

## Tester Report Flow

1. Download the latest user-test build.
2. Run the `.exe`; install the Microsoft .NET Desktop Runtime if Windows prompts for it.
3. Report issues through the GitHub "FreeX user test report" template.
4. Attach `%LOCALAPPDATA%\FreeX\Diagnostics\events.jsonl` or `CrashReports/*.json` only when useful, after checking that the attachment contains no private information.
