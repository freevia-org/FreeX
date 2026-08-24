# Public-Preview Release Notes Template

Copy this template for a candidate release. Replace every placeholder or mark it
not applicable with a reason. Do not publish the instructional comments as if
they were completed facts.

## Candidate

- Apps and version: `<FreeX / FreeW / FreeP / suite, version>`
- Release date (UTC): `<date>`
- Full commit SHA: `<40-character SHA>`
- Release/tag: `<versioned tag and URL>`
- Release decision: `<unsigned public preview / internal testing / no-go>`
- Support status: `Public preview; no guaranteed support lifetime or response time.`

## Trust and Signing Status

> **Preview trust notice:** `<State separately whether Windows executables and
> installers are signed. State separately whether macOS apps are signed,
> notarized, and stapled. If pending, say “unsigned” and “unnotarized.” Do not
> describe an unsigned artifact as trusted, certified, or production-ready.>`

Verify SHA-256 checksums before launch. Do not disable SmartScreen, Gatekeeper,
antivirus, or other operating-system protections globally. `<Link to the
versioned artifact manifest and checksum instructions.>`

## Downloads

Use versioned links. A repository-wide `releases/latest` redirect may point to a
different app.

| App/scope | Platform/runtime | Install type | Filename | SHA-256 file | SBOM | Signing/notarization |
| --- | --- | --- | --- | --- | --- | --- |
| `<app>` | `<runtime>` | `<portable / individual installer / suite installer>` | `<name>` | `<name.sha256>` | `<name.spdx.json>` | `<status>` |

- Release manifest: `<filename, SHA-256, and link>`
- Legal bundle: `<filename, SHA-256, and link>`

## Install, Update, Uninstall, and Rollback

- Prerequisites: `<OS/runtime requirements>`
- Install: `<artifact-specific steps>`
- Update/upgrade: `<supported transition and user-data behavior>`
- Uninstall: `<steps and data retained>`
- Standalone executable behavior: `<where settings and diagnostics are stored>`
- Rollback: `<tested rollback path, or state that rollback is withdrawal plus a
  forward-fix and give the superseding-release/status link>`

Do not instruct users to overwrite or downgrade user data unless that exact path
was tested for this candidate. Link the completed clean-machine evidence from
the [operations runbook](public-preview-operations.md).

## Privacy and Network Behavior

- Local diagnostics: `<enabled behavior and product-specific disable control>`
- Remote crash reporting: `<default-off consent behavior and whether a release
  endpoint is configured>`
- External services: `<feedback, online help, update checks, Sentry if enabled>`
- Responsible operator/contact: `<required before remote reporting is enabled>`
- Crash service region, retention, and privacy link: `<required if enabled;
  otherwise state not enabled>`

Opening a local document does not by itself authorize its upload. Crash reports
and public issue attachments can still contain sensitive values; link the
[privacy notice](../legal/privacy.md) and explain how to review attachments.

## What to Test

- `<candidate-specific workflows>`
- `<install/suite transition workflows>`
- `<accessibility workflows>`
- `<offline and recovery workflows>`

## Known Limitations and Accepted Risks

| Area | Affected app/platform | User impact | Workaround | Owner/status |
| --- | --- | --- | --- | --- |
| `<area>` | `<scope>` | `<impact>` | `<safe workaround or none>` | `<owner/link>` |

Do not convert an untested item into a statement of support. List any deferred
signing, accessibility, privacy-configuration, dependency-license, update, or
rollback gate explicitly.

## Feedback, Security, and Incident Status

- Feedback/support: [suite feedback process](../support/feedback.md)
- Security reports: [private vulnerability-reporting policy](../../SECURITY.md)
- Current release status/corrections: `<versioned status link>`
- Superseded or withdrawn versions: `<links and required user action, or none>`

Do not attach confidential documents, credentials, or unreviewed diagnostics to
a public issue.

## Evidence Summary

- Decision record: `<location and hash>`
- Automated verification: `<links>`
- Clean-machine acceptance: `<links>`
- Keyboard-only and screen-reader checks: `<links>`
- Artifact re-download/checksum verification: `<links>`
- Crash consent-off and synthetic-event evidence: `<links/status>`
- License/notice review: `<reviewer and record; not a legal-compliance claim>`
