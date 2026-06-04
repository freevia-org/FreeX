# Privacy Notice

FreeX is a local Windows desktop app. Workbooks are opened, edited, and saved on
the user's machine unless the user explicitly chooses an external sharing path.

## Local Diagnostics

Tester builds write local usage events and crash files under:

`%LOCALAPPDATA%\FreeX\Diagnostics`

These files stay on the user's machine unless the user chooses to attach them to
an issue report or otherwise share them. Local diagnostics can be disabled for a
run by starting FreeX with `FREEX_DIAGNOSTICS=0` in the environment.

## Crash Reporting

Remote crash reporting uses Sentry only when a Sentry DSN is configured and the
user has enabled crash reporting for the tester build. Crash reports include app
version, runtime, operating system, session ID, exception type, exception
message, and stack trace.

FreeX does not intentionally collect workbook contents, formulas, filenames, or
file paths in crash reports. Exception messages and stack traces can sometimes
include sensitive values, so users should review local diagnostics before
sharing them manually.

## Issue Reports

The "report issue" and "copy diagnostics" flows are designed to include safe app
metadata only. Users should not include workbook contents, formulas, file paths,
or private data unless they choose to share them.

## Network Behavior

FreeX has no Microsoft 365 account integration and no proprietary Microsoft
cloud-service dependency. Online help, update checks, issue reporting, and crash
analytics may open external network destinations only through the explicit
feature paths that describe that behavior in the app.
