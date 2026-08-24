# Security Policy

## Supported Releases

During the public-preview period, the project targets security maintenance at
the newest published preview of FreeX, FreeW, and FreeP. Older previews and
tester builds may be used to reproduce a report, but users should update to the
newest fixed build when one is available. This policy does not promise a
particular response or support lifetime.

## Reporting A Vulnerability

Do not disclose a suspected vulnerability, exploit, secret, or sensitive sample
document in a public issue.

Use GitHub's
[private vulnerability-reporting form](https://github.com/tony-xmelon/FreeX/security/advisories/new).

Include, when safe and relevant:

- the affected app (`FreeX`, `FreeW`, or `FreeP`), version, platform, and
  installation type;
- the security impact and prerequisites;
- minimal reproduction steps or a proof of concept;
- whether the issue is already public; and
- a safe way to contact the reporter for follow-up.

Do not upload a real confidential workbook, document, presentation, credential,
or signing key. Use a synthetic reproduction where possible and wait for the
maintainers to coordinate any sensitive transfer.

Maintainers will acknowledge reports when practical, investigate their impact,
and coordinate remediation and disclosure according to the severity and the
availability of a safe release. Please avoid public disclosure until a fix or
mitigation is available, but this request does not create a confidentiality
agreement or guarantee a response deadline.

Ordinary crashes, compatibility problems, and feature requests belong in the
[public feedback process](docs/support/feedback.md).

## Maintainer Handling

Maintainers should preserve the affected artifact identity and use the
[public-preview incident procedure](docs/release/public-preview-operations.md#incident-procedure)
for containment, evidence handling, withdrawal, and replacement. That procedure
does not establish a disclosure deadline or legal notification threshold; the
responsible operator must determine those for the actual incident and
jurisdictions.
