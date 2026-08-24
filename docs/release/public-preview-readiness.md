# Free-Family Public-Preview Readiness

This is the release-owner gate for FreeX, FreeW, and FreeP. It distinguishes
work that can be completed while signing certificates are pending from the
final signing and notarization gate. It is a technical checklist, not a legal
opinion or a guarantee that a release is fit for a particular purpose.

## Certificate-Independent Gate

Complete these items before freezing a public-preview candidate:

- [ ] The repository preflight, Release build, and applicable non-UI tests pass
  for the candidate commit.
- [ ] Each shipped app/platform lane launches from its actual release artifact,
  rather than only from a development build.
- [ ] Windows provides both retained standalone single-file executables and
  installable packages for FreeX, FreeW, FreeP, and the complete suite.
- [ ] Every installer has a stable product/package identity, clean upgrade and
  uninstall behavior, per-app selection where applicable, and no data-directory
  collision between products.
- [ ] Installing the suite and installing an individual app produce the same
  application files and do not create duplicate/conflicting update ownership.
- [ ] Each artifact has a versioned name, immutable checksum, release manifest,
  and a documented installation/update/uninstall path.
- [ ] The unsigned-preview warning is prominent in release notes and download
  instructions. Checksum validation is documented. Users are never instructed
  to disable SmartScreen or Gatekeeper globally.
- [ ] Local crash files are created for a controlled fault in every shipped app
  and renderer; the app still fails safely if diagnostics storage is unavailable.
- [ ] Any remote crash backend is enabled only after explicit consent and only
  when configured. A controlled non-sensitive test event arrives in the correct
  project with app, version, environment, platform, and architecture tags.
- [ ] Consent-off and missing-configuration tests prove that no remote event is
  sent. Redaction is checked with synthetic user-name and profile-path values.
- [ ] Each final publish directory or standalone executable passes the offline
  crash-analytics configuration check; its output is retained without exposing
  the DSN.
- [ ] Production alert routing, issue ownership, retention, release health, and
  symbol/debug-file handling are configured in the crash backend.
- [ ] Help/Feedback and Copy Diagnostics work in every shipped app and renderer.
  The feedback destination identifies the app automatically or the issue form
  requires the reporter to select it.
- [ ] A test issue can be submitted, triaged, and closed without exposing private
  data. Private vulnerability reporting is enabled and tested by a maintainer.
- [ ] The project license, legal notice, privacy notice, third-party notices, and
  required license/source-offer materials are present and accessible in every
  package. Release-specific legal review is recorded without claiming guaranteed
  non-infringement.
- [ ] Keyboard-only, screen-reader, update/rollback, crash-recovery, and clean
  machine smoke evidence is attached to the release decision.

## Crash-Reporting Contract

The shared diagnostics store writes bounded app-specific local event and crash
files. Remote Sentry reporting must remain a separate, optional layer for every
app and renderer. Each app owns its DSN/configuration and consent state; a
missing DSN, declined consent, or disabling app-specific environment override
leaves the app in local-only mode. Consent is default-off and is stored per
product. A DSN alone does not enable upload, and public packaging must not use a
test/runtime override to bypass the user's choice. The presence of the Sentry
package or a local crash file does not by itself prove remote reporting works.

For public-preview evidence, use a separate non-production test DSN or a clearly
tagged release-health environment and send only the built-in synthetic test event. Verify
the event in the backend, then remove the test path from the candidate. Do not
trigger a destructive crash against a real user document.

Each app now exposes **Help > Test Crash Reporting**. The command is available
only through an analytics instance that already passed both configuration and
consent gates. It sends an informational event tagged `freeapp.test_report=true`;
it does not throw an exception, open a document, or attach local diagnostic
files. Use this command for backend acceptance evidence instead of deliberately
crashing an installed app. A disabled result is expected when consent or the
release endpoint is absent.

Before packaging, verify each of the six publish outputs (FreeX, FreeW, and
FreeP on WPF and Avalonia) without contacting Sentry. Set
`FREE_FAMILY_SENTRY_DSN` only in the release job, then run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Test-CrashAnalyticsArtifactConfiguration.ps1 `
  -ArtifactPath artifacts\publish\FreeX-Wpf `
  -ExpectedEnvironment tester-release `
  -OutputPath artifacts\release-evidence\freex-wpf-crash-analytics.json
```

Repeat for every publish directory or retained standalone executable. The
validator reads the expected endpoint from the named environment variable,
does not make a network request, and never writes the DSN to its output. Run it
on publish output before installer compression; passing proves configuration
was embedded, not that consent was granted or that the backend received an
event. Backend receipt remains a separate manual check using **Help > Test Crash
Reporting** after opting in.

## Feedback Gate

Public issue forms cover defects and general feedback. Release notes and every
app's Help surface should link to the issue-form chooser. The form must capture
app, version, platform, architecture, installation type, and reproduction steps.
Crash analytics does not replace issue intake because an anonymous event has no
reliable follow-up channel.

App feedback commands open `user-test-report.yml` with an encoded title that
identifies app, version, operating system, and architecture. Installation type
remains a required issue-form selection because portable and installed copies
cannot reliably distinguish every packaging route at runtime.

Security reports use the private route in [`../../SECURITY.md`](../../SECURITY.md).
The release owner must confirm that GitHub private vulnerability reporting is
enabled; the presence of `SECURITY.md` does not enable the repository setting.

## License Scope

The current project license authorizes tester binaries for personal evaluation
and testing and restricts redistribution and commercial distribution. Do not
describe an unsigned public preview as granting broader rights. Before a stable
general-availability release, the copyright holder must deliberately select and
publish the intended end-user and redistribution terms for all three apps.

That decision must be reviewed together with the third-party notice bundle and
the LGPL-covered components described in
[`../../THIRD_PARTY_NOTICES.md`](../../THIRD_PARTY_NOTICES.md). Shipping
license text alone does not establish that every source, relinking, notice, or
written-offer obligation has been fulfilled for a particular artifact.

## Packaging Contract

Windows public-preview publication retains these download choices:

| Scope | Portable artifact | Installable artifact |
| --- | --- | --- |
| FreeX only | standalone FreeX executable | per-user FreeX Inno Setup installer |
| FreeW only | standalone FreeW executable | per-user FreeW Inno Setup installer |
| FreeP only | standalone FreeP executable | per-user FreeP Inno Setup installer |
| Entire suite | the three standalone executables remain individually available | per-user Free Suite Inno Setup installer with all three apps |

An installer is additional to, not a replacement for, the standalone executable.
Checksums are generated after all packaging steps that modify an artifact. Until
certificates arrive, installer and portable artifacts may be published only as
explicitly labeled unsigned previews; production workflows must not pretend they
are signed.

Linux and macOS retain the per-app, per-architecture archives and also provide a
suite-level bundle with an install script where no native signed package is yet
available. The script must support a normal user-scoped installation, report
partial failure, avoid overwriting user data, and document uninstall/update
steps. Do not describe a ZIP archive or install script as a signed native
package. macOS packages remain explicitly unsigned and unnotarized until the
Developer ID certificate gate is complete.

## Certificate Gate

Do not make certificate availability a workflow requirement while certificate
issuance is pending. Before promotion beyond unsigned preview:

- Windows executables and installers are signed by the intended publisher,
  RFC 3161 timestamped, and verified on a clean machine. Checksums are regenerated
  after signing.
- macOS applications use the intended Developer ID identity, hardened runtime,
  notarization, stapling, and Gatekeeper verification for every architecture.
- Signing credentials are scoped to protected release jobs and are not exposed
  to pull-request builds or stored in repository files.
- Certificate backup, renewal, revocation, and emergency release procedures have
  named owners.

## Release Decision Record

Record the candidate commit, version, artifact manifest, checksums, test and
human-validation evidence, crash-backend test event identifier, feedback-form
test issue, known limitations, rollback owner, and signing status in the release
notes. A failed or untested item remains visible; it is not converted into a
claim of support.

Use the
[public-preview decision record template](public-preview-decision-record-template.md)
so every candidate is assessed against the same evidence fields.
