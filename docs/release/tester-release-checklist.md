# Full Signed Release Checklist

Use this checklist for prereleases and non-prerelease releases produced by the
`Full Signed Release` workflow. A full release uses `app=all`, `platform=all`,
a new semantic version, and `prerelease=false`.

## Candidate and release gate

- The workflow was dispatched from `main` at the intended immutable commit.
- The same commit has successful exact-SHA CI and CodeQL attestations.
- Every selected release-only test job and the aggregate release gate passed.
- The version is unused, or every existing version tag already targets the
  same immutable commit.
- No other full release for the same version is running.

## Windows trust and packaging

- GitHub OIDC authenticated the dedicated Azure release identity without a PFX
  or client secret.
- FreeX, FreeW, and FreeP standalone executables and Velopack
  payloads/installers were signed by Freevia, timestamped, and passed
  Authenticode verification before checksums were generated.
- The non-Inno Free Suite bootstrapper was signed and verified after
  embedding the final signed per-app installers.
- Signing, timestamping, or signature verification failure stopped publication.

## Linux and macOS integrity

- Every Linux portable and installer archive has a matching SHA-256 checksum,
  SPDX SBOM, runtime manifest, and final release manifest.
- Every macOS application bundle was signed with the configured Developer ID
  Application identity before packaging.
- Apple notarization completed and its ticket was stapled and validated for
  each individual app bundle. The suite contains those same accepted bundles.
- Missing Apple credentials, code-signing failure, notarization failure, or
  stapling/validation failure stopped publication; the full workflow did not
  downgrade to unsigned macOS assets.

## Published inventory

- FreeX, FreeW, FreeP, and Free Suite releases all target the dispatch SHA and
  have the requested prerelease state.
- Every selected runtime includes its expected portable artifact, installer,
  adjacent checksums, SBOMs, and manifests.
- The Windows suite release contains the signed suite bootstrapper; Linux and
  macOS suite releases contain their platform-native aggregate packages.
- The final remote inventory verification passed after publication.

## Public-preview accessibility gate

- Keyboard-only smoke validation was recorded for core document workflows.
- Screen-reader smoke validation was recorded for launch, editing, dialogs,
  warnings, and accessibility results.
- UI Automation names, IDs, patterns, and focus order were reviewed.
- Known accessibility issues are listed with severity and planned follow-up.

If any public-preview accessibility item is incomplete, record the build as
internal-only even when its package signatures and integrity gates pass.
