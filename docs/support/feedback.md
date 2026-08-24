# Feedback And Support

Feedback for FreeX, FreeW, and FreeP is accepted through the repository's
[GitHub issue forms](https://github.com/tony-xmelon/FreeX/issues/new/choose).
Use the user-test report for a crash or defect and the family feedback form for
a suggestion, usability concern, or general comment.

The in-app Feedback command opens the structured user-test form with app,
version, operating system, and architecture in the proposed title. Select the
actual installation type in the required form field; the app cannot reliably
infer whether an executable came from an individual installer, suite installer,
or manually copied portable package.

## A Useful Defect Report

Include:

- app, version, platform, architecture, and installation type;
- concise reproduction steps, expected behavior, and actual behavior;
- whether the problem is repeatable; and
- a minimal synthetic sample when the issue depends on a file.

The apps can write `events.jsonl` and `CrashReports/*.json` in their local
diagnostics directory. A diagnostics attachment is optional. Review every file
before uploading it because exception messages and stack traces can contain
sensitive values. Do not upload private documents, filenames, paths, account
details, or credentials.

## Crash Analytics Is Not A Support Ticket

An opt-in remote crash event, where supported and configured, helps aggregate
failures but does not create a conversation with the reporter. File an issue if
you need a response, want to provide reproduction steps, or need to know whether
a problem is understood.

## Security And Privacy

Report security vulnerabilities privately using [SECURITY.md](../../SECURITY.md).
Do not put secrets or personal data in a public issue. Questions about local and
remote diagnostics are covered by the [privacy notice](../legal/privacy.md).

GitHub is an external service and its own account and privacy terms apply to
information submitted through an issue.
