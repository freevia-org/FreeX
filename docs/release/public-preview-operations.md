# Public-Preview Acceptance, Rollback, and Incident Runbook

This runbook covers FreeX, FreeW, and FreeP public-preview candidates. It is an
operator procedure for repository artifacts; it does not authorize publication,
make a legal determination, or replace platform-vendor, privacy, security, or
license advice. Certificate-independent work may be completed while signing
credentials are pending, but unsigned artifacts must remain clearly labeled.

Use this runbook with the
[public-preview readiness gate](public-preview-readiness.md), the
[release decision record](public-preview-decision-record-template.md), and the
[release-notes template](public-preview-release-notes-template.md).

## Roles and Stop Authority

Before acceptance starts, record one person or team for each role. A role can be
shared, but it cannot be left implicit.

| Role | Responsibility |
| --- | --- |
| Candidate owner | Freezes the commit, version, artifact manifest, and evidence location. |
| Acceptance owner | Runs or coordinates clean-machine checks and records failures without editing the evidence. |
| Distribution owner | Controls promotion, withdrawal, replacement, and public status text. |
| Security contact | Receives private vulnerability reports and coordinates security containment. |
| Privacy contact | Assesses suspected unintended collection or disclosure and approves privacy wording. |
| Dependency/license reviewer | Reviews the exact runtime dependency set, notices, source/relinking materials, and distribution scope. |
| Rollback owner | Can stop promotion and execute the recorded rollback or replacement plan. |

The candidate owner, security contact, privacy contact, or rollback owner may
stop promotion. A stopped candidate remains a no-go until a new decision record
identifies the remediation and replacement evidence.

## Freeze the Candidate

1. Record the full commit SHA, version, intended tag, workflow run, and UTC build
   time in a new decision record.
2. Store the artifact manifest, each payload checksum, SBOM, test results, and
   release-note draft together. Hash the decision record if it is stored outside
   the repository.
3. Confirm every manifest entry names the same version and commit. Treat a
   missing, duplicate, unexpected, or differently hashed artifact as a no-go.
4. Confirm the candidate uses versioned artifact names. Do not replace bytes
   under an existing tag or reuse a checksum from an earlier build.
5. Review the intended distribution scope against `LICENSE`,
   `THIRD_PARTY_NOTICES.md`, `THIRD_PARTY_LICENSES.md`, and the exact SBOM. This
   records a review; it does not prove that every obligation is satisfied.
6. Confirm the release-note draft says whether Windows artifacts are signed and
   whether macOS artifacts are signed, notarized, and stapled. Pending
   certificates do not block unsigned-preview acceptance, but they do block any
   statement that the artifacts are signed, trusted, notarized, or
   production-ready.

## Clean-Machine Acceptance

Use disposable machines or VM snapshots that do not contain the repository,
the .NET SDK, developer certificates, prior test builds, or package-manager
caches for the candidate. Record the OS version, architecture, locale, display
scale, machine/VM identifier, network state, and snapshot identifier. Use only
synthetic documents created for release testing.

For every artifact under test:

1. Download or copy the exact staged candidate bytes through the same route
   intended for recipients.
2. Verify the SHA-256 value against the candidate checksum before launch. Also
   verify the artifact appears exactly once in the release manifest.
3. Confirm the unsigned or unnotarized warning is visible before installation
   instructions. Do not disable SmartScreen, Gatekeeper, antivirus, or other
   operating-system protections globally. Record an expected trust warning as
   evidence rather than concealing it.
4. Confirm the project license, legal and privacy notices, third-party notices,
   and bundled license texts are available without network access.
5. Launch offline, create a synthetic file, save, close, reopen, and export one
   supported format. Record any format warning or recovery behavior.
6. Exercise Help, Feedback, Copy Diagnostics, and Legal Notices. Confirm opening
   a link does not attach a document or diagnostic file automatically.
7. With crash-reporting consent off, confirm the app remains usable and no
   remote test event appears. Test remote delivery only with explicit consent,
   an approved non-sensitive endpoint, and the built-in synthetic test event.

### Windows matrix

- Launch each retained standalone FreeX, FreeW, and FreeP executable without an
  installer.
- Clean-install, launch, reinstall/repair where supported, upgrade, and uninstall
  each individual installer.
- Test individual-to-suite, suite-to-individual, and suite-upgrade transitions.
  Confirm each app has one installed executable and one ownership/uninstall path.
- Confirm uninstall removes application files but preserves app-specific user
  data. Confirm FreeX, FreeW, and FreeP settings and diagnostics directories do
  not collide.
- Verify Start menu entries, file associations, install location, Programs and
  Features identity, and non-administrator per-user behavior.

### Linux matrix

- Test every published architecture archive and install-script bundle on the
  supported distribution baseline.
- Inspect the install and uninstall scripts before execution; run them with a
  user-scoped destination and without elevated privileges unless the release
  notes explicitly document a separately reviewed system installation.
- Confirm partial failures are reported, user data is not overwritten, and an
  individual-to-suite transition does not create duplicate app ownership.

### macOS matrix

- Test every published architecture archive and `.app` bundle on a clean macOS
  account.
- While certificates are pending, record the expected unsigned and unnotarized
  Gatekeeper result. Do not instruct users to disable Gatekeeper globally or
  remove quarantine recursively from broad directories.
- Confirm the install script uses a user-selected destination, preserves user
  data, and reports partial failure. Repeat after signing/notarization becomes
  available; unsigned evidence cannot be reused for the certificate gate.

## Acceptance Result

For each matrix row, record pass, fail, or not applicable plus an evidence link.
“Not tested” is not “not applicable.” A public-preview decision requires:

- all expected artifacts and checksums accounted for;
- successful clean launch and core synthetic-file round trip;
- successful install/upgrade/uninstall transitions for every installable route;
- consent-off, local diagnostics, offline notices, feedback, and recovery checks;
- keyboard-only and screen-reader evidence recorded separately; and
- every exception listed as an open blocker or explicitly accepted risk with an
  owner and rationale.

## Staged Promotion

1. Publish first to a limited preview audience when the distribution service
   supports staged visibility. Do not move a stable/latest pointer yet.
2. Re-download the public-facing bytes and repeat checksum and manifest checks.
3. Verify release-note links, artifact labels, support intake, and the private
   vulnerability-reporting route.
4. Observe crash, feedback, and download signals for the period recorded in the
   decision record. Absence of telemetry is not proof that the build is healthy.
5. Move a stable/latest pointer only after the distribution owner records the
   decision. Per-app releases must use versioned links because a repository-wide
   “latest” redirect may identify a different app.

## Rollback and Replacement

Define the rollback trigger before promotion. Triggers normally include a
checksum or manifest mismatch, broken install/uninstall, data loss or corruption,
unintended network transmission, a security vulnerability, missing required
notices/materials, or a crash that prevents normal launch or recovery.

When a trigger fires:

1. Stop further promotion and movement of stable/latest pointers.
2. Mark the affected release and download instructions as withdrawn or unsafe
   for new installation. Preserve the original tag, manifest, hashes, logs, and
   decision record for investigation; do not silently replace asset bytes.
3. Notify the security and privacy contacts when their domains may be involved.
   Keep vulnerability details and sensitive diagnostics out of public issues.
4. Record affected apps, versions, runtimes, install types, exposure window, and
   known mitigations. Avoid claims about impact until evidence supports them.
5. Test whether a prior installer can safely replace the candidate. Never
   instruct users to downgrade a document or settings format unless backward
   compatibility and user-data backup/restore have been verified.
6. Build a fixed version with a new version/tag, regenerate checksums, SBOMs and
   manifests, and repeat the complete gate. Publish correction notes that point
   to the superseding version and identify whether user action is required.

If safe automated downgrade is unavailable, the rollback is withdrawal plus a
forward-fix release. State that plainly; do not label an untested manual file
replacement as a supported rollback.

## Incident Procedure

Classify the report before responding:

- **Artifact integrity:** hash mismatch, missing/extra payload, wrong commit,
  malicious replacement, or corrupt download.
- **Security:** vulnerability, secret exposure, unsafe update path, or signing
  credential concern. Use the private route in [`../../SECURITY.md`](../../SECURITY.md).
- **Privacy:** suspected upload without consent, redaction failure, unexpected
  data field, wrong endpoint/region, or retention/configuration error.
- **Reliability/data:** data loss, file corruption, uninstall removing user data,
  or unrecoverable startup failure.
- **License/notice:** omitted attribution, license text, source/relinking material,
  or a distribution-scope concern requiring the dependency/license reviewer.

For any potentially material incident:

1. Open a restricted incident record and assign an incident owner, scribe, and
   decision authority. Use UTC timestamps.
2. Preserve the exact artifact, manifest, checksum, SBOM, workflow run, backend
   configuration identifiers, and relevant synthetic reproduction. Do not copy
   real user documents or secrets into the record.
3. Contain the issue: stop promotion, withdraw affected instructions or assets
   when authorized, disable the affected optional service if that can be done
   without changing unrelated user settings, and rotate/revoke credentials only
   through their authorized owner.
4. Determine affected versions and platforms, then choose withdrawal,
   configuration mitigation, or forward-fix. A release certificate being
   unavailable does not justify bypassing the incident gate.
5. Prepare user-facing communication with the affected versions, observable
   symptoms, safe action, data implications known at that time, support/private
   reporting routes, and next update time. Separate confirmed facts from
   investigation status.
6. Repeat clean-machine acceptance for the remediation and record closure,
   remaining uncertainty, and follow-up work.

This repository does not define notification deadlines, regulator contacts,
Sentry retention, or a legal incident-notification threshold. The responsible
operator and qualified advisers must determine those items for the actual
distribution and jurisdictions.

## Evidence Retention

Retain decision records, manifests, checksums, SBOMs, release notes, test results,
clean-machine evidence, incident records, and correction notices according to a
documented operator policy. Restrict evidence containing system paths, account
identifiers, or backend event data. Record the retention location and access
owner in the decision record; the repository does not prescribe a universal
retention period.
