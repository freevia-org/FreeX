# Privacy Notice

FreeX, FreeW, and FreeP are local desktop applications. Workbooks, documents,
and presentations are opened, edited, and saved on the user's device unless the
user deliberately selects a feature that contacts an external service, such as
online help, update checks, feedback, or opt-in crash reporting.

This notice describes the behavior implemented by the applications. A release
operator must update the notice and the release notes before enabling a new
network service or collecting additional data.

## Local Application Data

The applications store settings, recent-file information, autosave/recovery
data, logs, and diagnostics in app-specific user-data directories. These files
are not uploaded merely because they exist.

Local diagnostics use a `Diagnostics` directory for each product:

- Windows: `%LOCALAPPDATA%\<app>\Diagnostics`
- macOS: `~/Library/Logs/<app>`
- Linux: `<LocalApplicationData>/<app>/Diagnostics`; on a typical desktop this
  resolves beneath `~/.local/share`

Here, `<app>` is `FreeX`, `FreeW`, or `FreeP`. The exact Linux base path is the
local-application-data directory returned by the installed .NET runtime and can
vary with the user's environment.

Local diagnostics can be disabled for a run with the product-specific
environment variable `FREEX_DIAGNOSTICS=0`, `FREEW_DIAGNOSTICS=0`, or
`FREEP_DIAGNOSTICS=0`.

## Local Diagnostics Content And Retention

When enabled, `events.jsonl` contains the app version, timestamp, random session
identifier, runtime and operating-system descriptions, process architecture,
event name, and a limited set of event properties such as command, dialog,
format, status, and reason. The diagnostics store accepts only an allow-list of
property names; it is not designed to record document contents or file paths.

`CrashReports/*.json` can contain the same environment metadata, the exception
type and message, stack trace, process identifier, crash source, and up to 25
recent allow-listed events. Exception messages and stack traces can
occasionally contain sensitive values even though the applications do not
intentionally add document contents, formulas, filenames, or file paths.

The local event file is bounded to approximately 2 MiB and is trimmed to retain
newer entries when it grows beyond that limit. At most 50 local crash-report
files are retained. Users may delete these local files at any time while the
application is closed.

## Remote Crash Reporting

Remote crash reporting is separate from local diagnostics. Each app and desktop
renderer supports an optional Sentry transport. It remains off unless all of
the following are true:

1. crash reporting was enabled through the product's explicit, default-off user
   consent setting; and
2. a Sentry DSN was configured for that product, build, or environment.

The products use separate `FREEX_`, `FREEW_`, and `FREEP_` crash-analytics
configuration. A DSN by itself does not enable upload. App-specific environment
controls can disable analytics or provide an explicit test/runtime override;
public release packaging must not use an override to bypass the user's consent
choice. If configuration or consent is missing, local diagnostics can continue
without remote upload. Changes to the consent setting take effect the next time
the application starts.

When remote reporting is enabled, Sentry receives the exception type, message,
and stack trace plus the app release, reporting environment, product, session
identifier, runtime, operating system, process architecture, and crash source.
Default personally identifying information is disabled. Reports can include
recent allow-listed diagnostic events as breadcrumbs. Before an event leaves
the device, the implementation attempts to replace the current user's profile
path and user name in event messages, exception values, and stack-frame file
paths. It also replaces recognized absolute Windows, UNC, file-URI, and Unix
paths and common office-document filenames, and removes complete source-path
fields from stack frames. Automated redaction reduces risk but cannot guarantee that an arbitrary
exception contains no sensitive value.

The Help menu's crash-reporting test sends a fixed informational message plus
the same app/platform metadata and safe breadcrumbs. It does not cause a crash,
read the active document, or upload local diagnostic files. Like real crash
reporting, it is unavailable without both consent and endpoint configuration.

Sentry is an external service. Before enabling remote reporting in a public
build, the release operator must publish the responsible operator's identity and
contact method, the applicable Sentry region and retention period, and a current
link to Sentry's privacy information. Those deployment details are not defined
by this source repository and must not be inferred from the presence of the
Sentry SDK.

## Feedback And Issue Reports

Opening an online feedback or help link sends the user to the destination in a
web browser; it does not automatically attach a document or diagnostics file.
Anything entered in a public GitHub issue, including attachments, becomes
information the user has deliberately shared with GitHub and the project.

Users should review diagnostic files and screenshots before uploading them and
remove personal, confidential, or document-specific information. Security
vulnerabilities and reports containing secrets should use the private process in
[`../../SECURITY.md`](../../SECURITY.md), not a public issue.

## Other Network Behavior

The applications do not provide Microsoft 365 account integration and do not
depend on proprietary Microsoft cloud services. Update checks, online help,
feedback links, and explicitly enabled crash reporting can contact external
destinations. Opening or editing a local document does not by itself authorize
uploading that document.

Questions about this notice can be submitted through the project's
[feedback process](../support/feedback.md). Do not include personal or
confidential data in a public issue.
