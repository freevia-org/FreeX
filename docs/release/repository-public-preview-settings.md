# Public-preview repository settings

These GitHub settings are certificate-independent but cannot be proven by files
committed to the repository. A release owner must configure them before a public
preview and capture the resulting evidence in the release decision record.

Run the read-only audit from an authenticated maintainer workstation:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Test-PublicReleaseRepositorySettings.ps1 -Strict -OutputPath artifacts\release-evidence\repository-settings.json
```

The strict gate requires:

- protection for `main`, or an active branch ruleset, with required review and CI;
- dependency vulnerability alerts;
- private vulnerability reporting;
- a read-only default `GITHUB_TOKEN`;
- a protected `public-preview` deployment environment;
- the issue labels referenced by the feedback forms; and
- `FREE_FAMILY_SENTRY_DSN`, after the privacy and Sentry operator settings are complete.

The repository contains `dependabot.yml`, `CODEOWNERS`, and a CodeQL workflow,
but those files do not enable the corresponding GitHub security settings. The
release owner must also verify that the `public-preview` environment has an
authorized reviewer and that only publishing jobs can use it.

Do not place secret values in release evidence. The audit records only whether
the expected secret name exists.
