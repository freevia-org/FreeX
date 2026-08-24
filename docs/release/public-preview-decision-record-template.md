# Public-Preview Release Decision Record

Copy this file for each candidate and keep the completed record with the
release evidence. Do not mark an item complete by relying on a previous build:
all artifact hashes and test evidence must identify the candidate commit below.

## Candidate Identity

- Version:
- Git tag:
- Full commit SHA:
- Release workflow run:
- Artifact manifest filename and SHA-256:
- Release-notes draft location and SHA-256:
- Candidate owner:
- Acceptance owner:
- Distribution owner:
- Security contact:
- Privacy contact:
- Dependency/license reviewer:
- Rollback owner and stop authority:
- Evidence location, access owner, and retention policy:
- Decision date (UTC):

## Automated Verification

- Repository preflight result/link:
- Release build result/link:
- Non-UI test result/link:
- Dependency/security scan result/link:
- Dependency-alert review result/link:
- Private vulnerability-reporting route verification:
- Feedback issue forms and required-label verification:
- Protected public-preview environment, reviewer, and branch-policy evidence:
- SBOM filenames and SHA-256 values:
- Artifact inventory and checksum validation result/link:

## Artifact Installation Evidence

Record a clean install, launch, upgrade, repair/reinstall where supported, and
uninstall for every applicable row. Include the OS build and machine or VM ID.

| Platform | Package | Install | Launch | Upgrade | Uninstall | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| Windows | FreeX standalone executable | N/A |  | N/A | N/A |  |
| Windows | FreeW standalone executable | N/A |  | N/A | N/A |  |
| Windows | FreeP standalone executable | N/A |  | N/A | N/A |  |
| Windows | FreeX installer |  |  |  |  |  |
| Windows | FreeW installer |  |  |  |  |  |
| Windows | FreeP installer |  |  |  |  |  |
| Windows | Free Suite installer |  |  |  |  |  |
| Linux | Per-app and suite artifacts |  |  |  |  |  |
| macOS | Per-app and suite artifacts |  |  |  |  |  |

- Suite/individual installer transition evidence:
- User data retained after upgrade/uninstall:
- Rollback package and tested rollback procedure:
- Clean-machine snapshot/machine identifiers and reset evidence:
- Offline launch and offline Legal Notices evidence:
- Public-facing artifact re-download and checksum verification:

## Diagnostics and Feedback Evidence

- Consent-off test (no remote transmission):
- Missing-configuration test (local only):
- Synthetic redaction test:
- Controlled remote test event ID, app/environment, and deletion/retention status:
- Production alert routing and owner:
- Feedback issue URL and triage outcome:
- Security-reporting private test and outcome:

Do not attach real crash dumps or logs containing user data to this record.

## Human Validation

- Keyboard-only validation:
- Screen-reader validation:
- Clean-machine first launch:
- Update and rollback:
- Crash recovery:
- Known limitations:

## Legal and Policy Review

- Project license reviewed for this distribution scope by:
- Privacy notice owner/contact and effective date reviewed by:
- If remote crash reporting is enabled: responsible operator, Sentry region,
  retention period, privacy link, and configuration approval:
- Third-party notices and corresponding source/relinking materials reviewed by:
- Exact SBOM/runtime dependency set matched to the notice and legal bundle by:
- Trademark/product wording review recorded by:
- Store/repository listing text reviewed by:

This section records review evidence; it is not a representation that automated
checks can guarantee non-infringement or legal compliance.

## Signing Status

- Windows signing certificate: **Pending / Available / Not applicable**
- macOS Developer ID certificate: **Pending / Available / Not applicable**
- Current artifacts: **Unsigned preview / Signed candidate**
- Unsigned-preview warning present in release notes and download instructions:

Certificate availability is not a prerequisite for producing and validating an
unsigned preview candidate. Unsigned artifacts must not be promoted or described
as signed, trusted, notarized, or production-ready.

## Decision

- Decision: **Go for internal testing / Go for unsigned public preview / No-go**
- Open blockers:
- Accepted risks and approver:
- Rollback triggers and tested action:
- Incident contacts and restricted record location:
- Staged-observation period and promotion criteria:
- Withdrawal/correction status URL:
- Final approver:

