# FreeX Tester Release Checklist

Use this checklist before promoting a tester build beyond internal validation. Paste the completed gate summary into the `Tester Release` workflow `release_notes` input when the build is intended as a public-preview candidate, set `public_preview_candidate` to true, and complete every accessibility evidence input.

This FreeX-specific checklist is complemented by the suite-wide
[public-preview readiness gate](public-preview-readiness.md), including crash
analytics, feedback intake, per-app and suite installers, legal materials, and
the explicitly deferred certificate gate.

## Required Release Gate

- Repository preflight, build, and test completed in the release workflow.
- Test result artifact was uploaded, even for failed release-gate attempts.
- Versioned `.exe`, latest `.exe`, versioned MSIX, latest MSIX, Velopack installer/portable/feed artifacts, and checksum artifacts were uploaded.
- Stable latest checksum assets were included for both the `.exe` and MSIX packages.
- Release ran from `main` or an isolated `codex/daily-tester-release-*` branch for a frozen verified candidate; no overlapping tester-release dispatch was active.
- MSIX package was signed with the release certificate when signing secrets were configured; otherwise unsigned MSIX publication was accepted for this internal tester build.
- GitHub release was published with the expected tester stream from `release/progress.json`.
- Latest `.exe` and MSIX download links were checked from the published release.
- If the workflow was dispatched with `prerelease=true`, direct release assets were checked from that release because GitHub's `releases/latest` redirect remains on the latest non-prerelease build.

## Public-Preview Accessibility Gate

- Keyboard-only smoke validation recorded for workbook open/save, grid navigation/editing, ribbon tab traversal, context menus, dialogs, sheet tabs, and Help.
- Screen-reader smoke validation recorded for first launch, workbook grid focus, formula bar edits, dialog titles/default buttons, warning messages, and accessibility checker results.
- UI Automation catalog review recorded for stable names, automation IDs, invoke patterns, and focus order on newly changed controls.
- Known accessibility issues are listed with affected workflow, severity, and planned follow-up.
- Workflow accessibility inputs were set for keyboard-only, screen-reader, UI Automation catalog, and known-issues review evidence.

## Promotion Decision

- If every accessibility gate item is complete, mark the release notes as public-preview eligible.
- If any accessibility gate item is skipped or incomplete, mark the build as internal-only and do not promote it as a public-preview candidate.

