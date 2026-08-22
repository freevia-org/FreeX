# Privacy Notice

FreeX, FreeW, and FreeP are local desktop apps. Workbooks, documents, and
presentations are opened, edited, and saved on the user's machine unless the
user explicitly chooses an external sharing, update, help, feedback, or other
network-enabled feature.

## Local Diagnostics

Tester builds can write local usage events and crash files under an app-specific,
platform-specific diagnostics directory. The examples below are for FreeX;
FreeW and FreeP use their own product-named directories and corresponding
`FREEW_DIAGNOSTICS` and `FREEP_DIAGNOSTICS` controls:

- Windows: `%LOCALAPPDATA%\FreeX\Diagnostics`
- macOS: `~/Library/Logs/FreeX/` (`events.jsonl`, `CrashReports/*.json`)
- Linux: XDG paths (`~/.config/FreeX`, `~/.local/share/FreeX`) <!-- VERIFY: exact Linux diagnostics subpath/filenames were not confirmed against source; docs/user/linux-install.md documents config/data under these XDG roots but does not spell out the diagnostics file layout. -->

These files stay on the user's machine unless the user chooses to attach them to
an issue report or otherwise share them. Local diagnostics can be disabled for a
run by starting FreeX with `FREEX_DIAGNOSTICS=0` in the environment.

## Crash Reporting

Remote crash reporting uses Sentry only when a Sentry DSN is configured and the
user has enabled crash reporting for the tester build. Crash reports include app
version, runtime, operating system, session ID, exception type, exception
message, and stack trace.

The apps do not intentionally collect document contents, formulas, filenames,
or file paths in crash reports. Exception messages and stack traces can sometimes
include sensitive values, so users should review local diagnostics before
sharing them manually.

## Issue Reports

The "report issue" and "copy diagnostics" flows are designed to include safe app
metadata only. Users should not include workbook contents, formulas, file paths,
or private data unless they choose to share them.

## Network Behavior

The apps do not provide Microsoft 365 account integration and do not depend on
proprietary Microsoft cloud services. Online help, update checks, issue
reporting, and opt-in crash analytics may contact external destinations only
through the feature paths that describe that behavior in the app.
