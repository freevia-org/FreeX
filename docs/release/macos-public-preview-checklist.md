# macOS Public Preview Human Validation Checklist

Use this checklist for each `macOS App Preview` public-preview candidate after hosted evidence has been downloaded and the Windows-runnable public-preview preflight has passed. It complements [test-distribution.md](test-distribution.md), [macos-signing-notarization.md](macos-signing-notarization.md), and [../planning/macos-accessibility-evidence.md](../planning/macos-accessibility-evidence.md).

After filling a release-specific copy, validate it from Windows before promotion. Name the completed runtime-specific copies beside the downloaded hosted artifacts as `completed-macos-public-preview-checklist-osx-arm64.md` and `completed-macos-public-preview-checklist-osx-x64.md`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-MacOsHumanValidationChecklist.ps1 -ChecklistPath artifacts/macos-preview/completed-macos-public-preview-checklist-osx-arm64.md -ExpectedRuntime osx-arm64 -ExpectedRunId <run-id> -ExpectedRunAttempt <run-attempt>
```

After both runtime checklists pass, run the combined promotion preflight so the hosted evidence bundle and both human checklists are tied to the same GitHub Actions run identity:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-MacOsPublicPreviewPromotion.ps1 -ArtifactRoot artifacts/macos-preview -ChecklistRoot artifacts/macos-preview -ExpectedRunId <run-id> -ExpectedRunAttempt <run-attempt>
```

Do not mark a macOS artifact public-preview eligible unless every required section below is `Pass`, or the release owner explicitly accepts a non-blocking issue in the final decision. If Developer ID signing, accepted notarization, stapling, Gatekeeper launch, Finder `.fxl` open, keyboard-only validation, or VoiceOver validation is missing, mark the build `Internal-only`.

## Candidate Summary

| Field | Value |
| --- | --- |
| Validation date |  |
| Tester |  |
| Mac model |  |
| Processor family | Apple Silicon / Intel |
| macOS version and build |  |
| Runtime under test | `osx-arm64` / `osx-x64` |
| Workflow run id / attempt |  |
| Source branch or commit |  |
| Artifact wrapper name | `freex-<run-id>-<run-attempt>-<runtime>-macos-app` |
| Diagnostics artifact name | `freex-<run-id>-<run-attempt>-<runtime>-macos-diagnostics` |
| Inner app ZIP | `freex-<runtime>-macos-app.zip` |
| ZIP SHA-256 |  |
| Evidence file | `freex-<runtime>-macos-evidence.txt` |
| Signing mode |  |
| Notarization status |  |
| Stapler status |  |
| Final decision | Pass / Fail / Internal-only |
| Decision owner |  |

## Prerequisites

- Download the GitHub Actions artifact wrapper in a browser when Gatekeeper behavior is in scope, and do not clear quarantine attributes before the first Finder launch.
- Unzip the artifact wrapper into a clean folder, then verify the inner app ZIP with its checksum file before extracting `FreeX.app` with Finder/Archive Utility or `ditto -x -k freex-<runtime>-macos-app.zip .`.
- Keep the original artifact wrapper, inner ZIP, checksum, evidence file, packaging smoke log, launch smoke file, notarization log, tester instructions, and diagnostics artifact with the release record.
- Use a real macOS user session. Hosted smoke logs are supporting evidence, not a replacement for Finder, Gatekeeper, keyboard-only, or VoiceOver observations.
- Record screenshots or exact prompt text for Gatekeeper prompts, default-handler changes, failed opens, accessibility issues, and crash dialogs.
- Review diagnostics files for private information before attaching them to an issue or release record.

## Hosted Evidence Copy-Forward

Run these commands from the directory containing the unzipped `FreeX.app` and the inner ZIP files, then paste the command output or attach the transcript.

```bash
shasum -a 256 -c freex-<runtime>-macos-app.zip.sha256
codesign --verify --deep --strict --verbose=2 FreeX.app
codesign -dv --verbose=4 FreeX.app
spctl -a -vv --type execute FreeX.app
xcrun stapler validate FreeX.app
```

| Required check | Expected public-preview evidence | Actual evidence | Status | Attachment |
| --- | --- | --- | --- | --- |
| Checksum | `<zip-name>: OK`; hash matches `zip_sha256` in evidence |  | Pass / Fail |  |
| Artifact channel | `artifact_channel=distribution-candidate` |  | Pass / Fail |  |
| Distribution readiness | `distribution_readiness=distribution_candidate_ready` |  | Pass / Fail |  |
| Signing | `codesign_mode=developer-id` and `codesign_verified=true` |  | Pass / Fail |  |
| Notarization | `notarization_status=accepted` |  | Pass / Fail |  |
| Stapling | `stapler_validated=true`; `xcrun stapler validate` succeeds |  | Pass / Fail |  |
| Gatekeeper assessment | `spctl` accepts `FreeX.app` as Developer ID software |  | Pass / Fail |  |
| Hosted launch smoke | Native runtime reports `macos_launch_smoke=passed` or documented architecture skip only for the opposite runtime |  | Pass / Fail |  |
| LaunchServices/Open-With smoke | Evidence contains the hosted LaunchServices and Open-With smoke pass markers |  | Pass / Fail |  |
| Command-key smoke | Evidence contains `command_key_smoke=passed` and required `cmd_*_menu_gesture=true` markers |  | Pass / Fail |  |
| Diagnostics artifact | Matching `macos-diagnostics` artifact is present and retained |  | Pass / Fail |  |

## Gatekeeper First Launch

The primary launch path for this section is Finder double-click on `FreeX.app`. Terminal `open FreeX.app` may be recorded as supporting evidence, but it does not replace Finder launch evidence.

| Step | Expected result | Actual result | Status | Evidence |
| --- | --- | --- | --- | --- |
| Confirm quarantine is still present before first launch, if the artifact was browser-downloaded | `xattr -p com.apple.quarantine FreeX.app` returns a value, or tester records that the download path did not preserve quarantine |  | Pass / Fail / N/A |  |
| Double-click `FreeX.app` in Finder | macOS allows launch without Control-click, right-click Open, or security override |  | Pass / Fail |  |
| Record Gatekeeper prompt | Prompt, if shown, identifies the downloaded Developer ID app and allows opening; no "damaged", "unidentified developer", or malware block appears |  | Pass / Fail |  |
| App reaches first usable window | FreeX opens to a usable workbook window with no crash or hang |  | Pass / Fail |  |
| Quit and relaunch from Finder | Second Finder launch opens without a new Gatekeeper block |  | Pass / Fail |  |

Failure rule: a public-preview candidate fails this section if a tester must remove quarantine, disable Gatekeeper, use `xattr -d`, use Control-click/right-click Open as a trust workaround, or override a blocked app in System Settings.

## Finder And File Association

Use a representative `.fxl` workbook with recognizable cell contents or sheet names. If changing the default `.fxl` handler is not acceptable on the tester machine, record that constraint and complete the Open With row; the public-preview release owner must decide whether the missing default-handler proof blocks promotion.

| Step | Expected result | Actual result | Status | Evidence |
| --- | --- | --- | --- | --- |
| Verify `.fxl` appears as a FreeX-supported document type | Finder shows `FreeX.app` as an available app for `.fxl` files |  | Pass / Fail |  |
| Set default `.fxl` handler, if permitted | Finder Get Info > Open with > FreeX > Change All succeeds |  | Pass / Fail / Skipped |  |
| Double-click `.fxl` in Finder | FreeX launches or activates and opens the selected workbook, not a blank replacement workbook |  | Pass / Fail |  |
| Confirm workbook identity | Window title, visible sheet, cell value, or recent-file entry matches the double-clicked `.fxl` file |  | Pass / Fail |  |
| Right-click `.fxl` > Open With > FreeX | FreeX opens the file when it is not already running |  | Pass / Fail |  |
| Repeat while FreeX is already running | The selected file opens in the existing app session without losing unsaved work |  | Pass / Fail |  |
| Optional spreadsheet file Open With | Representative `.xlsx` or `.csv` opens through Open With when included in the candidate scope |  | Pass / Fail / N/A |  |

## Workbook Smoke

| Step | Expected result | Actual result | Status | Evidence |
| --- | --- | --- | --- | --- |
| Create a new workbook | Blank workbook appears and accepts keyboard focus |  | Pass / Fail |  |
| Enter values and formulas | Typed values and a simple formula commit correctly |  | Pass / Fail |  |
| Save and Save As | Native menu save routes create or update the expected file |  | Pass / Fail |  |
| Close dirty workbook | Close prompt offers Save, Discard, and Cancel with clear labels |  | Pass / Fail |  |
| Reopen saved workbook | Saved values, formulas, sheet names, and simple formatting survive reopen |  | Pass / Fail |  |
| Recent files | Opened or saved workbook appears in the recent-file route when expected |  | Pass / Fail |  |

## Command-Key Menu Behavior

Use the native macOS menu bar where possible. Windows-style `Ctrl` shortcuts can be recorded as extra compatibility notes, but they do not satisfy Command-key evidence.

| Command | Expected result | Actual result | Status | Evidence |
| --- | --- | --- | --- | --- |
| Menu labels | Native menu items show the expected Command-key gestures for File, Edit, Format, View, Sheet, and Help surfaces present in the candidate |  | Pass / Fail |  |
| `Cmd+N` | Creates a new workbook |  | Pass / Fail |  |
| `Cmd+O` | Opens the native file picker or open route |  | Pass / Fail |  |
| `Cmd+S` | Saves the active workbook or routes to Save As for an unsaved workbook |  | Pass / Fail |  |
| `Cmd+Shift+S` | Opens Save As |  | Pass / Fail |  |
| `Cmd+W` | Closes the active workbook and honors dirty-workbook confirmation |  | Pass / Fail |  |
| `Cmd+Q` | Quits FreeX and honors dirty-workbook confirmation |  | Pass / Fail |  |
| `Cmd+A` | Selects current region, then whole sheet on repeated use if that behavior is active |  | Pass / Fail |  |
| `Cmd+F` and Find Next menu route | Find opens and Find Next advances through matches |  | Pass / Fail |  |
| `Cmd+B`, `Cmd+I`, `Cmd+U` | Bold, Italic, and Underline change selected cells and survive save/reopen where applicable |  | Pass / Fail |  |
| `Cmd+PageUp` / `Cmd+PageDown` or hardware equivalent | Sheet switching works, or tester records the hardware limitation and alternate route used |  | Pass / Fail / N/A |  |

## Keyboard-Only Accessibility

Complete this section with the mouse or trackpad set aside after launch. Record any focus trap, unreachable command, missing default button, or ambiguous warning as a failure unless the release owner accepts it as non-blocking.

| Flow | Expected result | Actual result | Status | Evidence |
| --- | --- | --- | --- | --- |
| First launch and initial focus | App opens with a visible, usable keyboard focus target |  | Pass / Fail |  |
| Grid navigation and editing | Arrow keys, Tab, Shift+Tab, Enter, Escape, and F2/direct entry work without pointer input |  | Pass / Fail |  |
| Formula box edits | Formula box can be reached, edited, committed, and canceled from the keyboard |  | Pass / Fail |  |
| Native menus | File/Edit/Format/View/Sheet/Help menu items can be reached and invoked by keyboard |  | Pass / Fail |  |
| Toolbar or command surface | Primary toolbar commands can be reached without pointer input when present |  | Pass / Fail |  |
| Sheet tabs | F6/Shift+F6, arrow keys, context-menu key or Shift+F10, rename, add, hide/unhide, and delete flows are reachable as candidate scope allows |  | Pass / Fail |  |
| Dialogs | Open, Save As, Find, Replace, Go To, Format Cells, warnings, and confirmations have predictable focus order and default/cancel behavior |  | Pass / Fail |  |
| Context menus | Grid and sheet-tab context menus open and can be navigated from the keyboard |  | Pass / Fail |  |
| Help and feedback routes | Help, feedback, update, About, and Legal Notices routes are reachable from the keyboard |  | Pass / Fail |  |
| Dirty close and Quit | Save/Discard/Cancel choices are reachable and do not trap focus |  | Pass / Fail |  |

## VoiceOver Smoke

Turn on VoiceOver in the tester session and capture the spoken text when practical. The expected result is not perfect prose; it is that a user can understand location, control purpose, selected state, warning meaning, and safe next action.

| Surface | Expected result | Actual announcement or issue | Status | Evidence |
| --- | --- | --- | --- | --- |
| First launch | VoiceOver identifies the app/window and initial focus |  | Pass / Fail |  |
| Workbook grid focus | Active cell or grid location is announced with useful context |  | Pass / Fail |  |
| Visible cells | Representative cell values can be inspected without losing position |  | Pass / Fail |  |
| Formula box | Current formula/value and edit state are understandable |  | Pass / Fail |  |
| Status text | Selection summary or transient status can be discovered |  | Pass / Fail |  |
| Sheet tabs | Sheet name, selected state, and tab actions are understandable |  | Pass / Fail |  |
| Drawing objects, if present | Object names/status are announced enough to identify the selected object |  | Pass / Fail / N/A |  |
| Dialog titles and buttons | Find, Replace, Go To, Format Cells, warnings, About, and Legal Notices announce titles, fields, default buttons, and destructive actions |  | Pass / Fail |  |
| Gatekeeper or accessibility prompts | System prompts are understandable and do not leave FreeX in a confusing state |  | Pass / Fail / N/A |  |
| Known issues review | `Accessibility Known Issues` is complete; every confusing announcement or missing name has severity, workaround, owner, and public-preview blocking decision |  | Pass / Fail |  |

## Accessibility Known Issues

Use this section to record every accessibility issue found during keyboard-only or VoiceOver validation. If no issues are known for this runtime, keep exactly one explicit `None` row. If any issue is listed, remove the `None` row and fill every field for each issue. The VoiceOver `Known issues review` row and the Public-Preview Decision known-issues row must both reflect this table.

| Issue ID | Affected workflow | Severity | User impact / evidence | Workaround | Owner | Public-preview blocking | Decision / rationale |
| --- | --- | --- | --- | --- | --- | --- | --- |
| None | None | None | No keyboard-only or VoiceOver known issues found during this runtime validation | None | Release owner | No | No known accessibility issues; public preview may proceed |

## Log And Artifact Collection

| Artifact | Required for public preview | Collected path or attachment | Notes |
| --- | --- | --- | --- |
| Completed checklist/report | Yes |  |  |
| GitHub Actions app artifact wrapper | Yes |  |  |
| Inner app ZIP and `.sha256` file | Yes |  |  |
| `freex-<runtime>-macos-evidence.txt` | Yes |  |  |
| macOS release-assets wrapper | Yes |  | Retain `freex-<run-id>-<run-attempt>-macos-release-assets` with the stable public-preview assets for both runtimes. |
| `FreeX-latest-macos-distribution-candidate-manifest.json` | Yes |  | Retain from the macOS release-assets wrapper and compare to the release record. |
| Packaging smoke log | Yes |  |  |
| Launch smoke file | Yes |  |  |
| Notarization log | Yes |  |  |
| Tester instructions | Yes |  |  |
| Diagnostics artifact | Yes |  |  |
| Local diagnostics | If useful or if failures occurred | `~/Library/Logs/FreeX/events.jsonl`; `~/Library/Logs/FreeX/CrashReports/*.json` if present | Review for private information before sharing. |
| Screenshots or recordings | Required for failures and Gatekeeper/default-handler proof |  |  |
| Terminal transcript | Required for checksum/signing/stapler commands |  |  |

## Public-Preview Decision

| Decision item | Result |
| --- | --- |
| Hosted public-preview preflight passed for both runtimes | Pass / Fail |
| This runtime passed human Finder/Gatekeeper validation | Pass / Fail |
| This runtime passed keyboard-only validation | Pass / Fail |
| This runtime passed VoiceOver validation | Pass / Fail |
| Known issues are listed with severity, workaround, owner, and blocking decision | Pass / Fail |
| Release owner accepts this runtime for public preview | Yes / No |

Paste this summary into the release record:

```text
macOS human validation decision: Pass / Fail / Internal-only
Runtime: osx-arm64 / osx-x64
Mac hardware and macOS build:
Workflow run / attempt:
Artifact:
Signing/notarization/stapler evidence:
Finder .fxl default double-click:
Gatekeeper first launch:
Command-key menu behavior:
Keyboard-only accessibility:
VoiceOver smoke:
Diagnostics/log attachments:
Known issues:
Decision owner:
```
