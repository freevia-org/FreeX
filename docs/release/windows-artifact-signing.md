# Windows Artifact Signing

FreeX, FreeW, and FreeP use Velopack for their direct-download Windows
installers, not Inno Setup. Each release also publishes a standalone
executable. The Velopack application payload, generated installer, standalone
executable, and repository-owned Free Suite bootstrapper need Authenticode
signatures because GitHub distributes them directly.

Microsoft Store submission is different: Partner Center accepts an unsigned
Store package and Microsoft signs it during certification. Do not apply the
Public Trust profile to a Store-bound MSIX.

The Azure resources are:

- tenant: `073e2caa-267e-4a85-8970-e6129ec806a9` (`Freevia.org`)
- subscription: `cdc114ef-0580-49c2-a5e0-9e43d63b9fd0`
- resource group: `rg-signing`
- account: `free-software-signing`
- certificate profile: `freevia-public-signing`
- account URI: `https://eus.codesigning.azure.net/`
- timestamp authority: `http://timestamp.acs.microsoft.com`

Signing is an explicit packaging option. Ordinary builds do not contact Azure.
Checksums must be generated after signing.

## One-time local setup

Install Azure CLI, the Artifact Signing client, and a current Windows SDK:

```powershell
winget install -e --id Microsoft.AzureCLI
winget install -e --id Microsoft.Azure.ArtifactSigningClientTools
```

Then authenticate the user that has **Artifact Signing Certificate Profile
Signer** and select the correct subscription:

```powershell
az login --tenant 073e2caa-267e-4a85-8970-e6129ec806a9
az account set --subscription cdc114ef-0580-49c2-a5e0-9e43d63b9fd0
```

`tools/signing/metadata.json` contains only public resource coordinates, not a
credential. The signing client obtains a short-lived token when it runs.

## Sign an existing executable

```powershell
pwsh -NoProfile -File tools/Invoke-WindowsArtifactSigning.ps1 `
  -Files artifacts/release/FreeX.exe
```

The script signs with SHA-256, adds an RFC 3161 SHA-256 timestamp, and runs
`signtool verify /pa /all`. Use `-VerifyOnly` to validate without submitting a
new signing operation.

## Build signed direct-download packages

For the standalone tester executable:

```powershell
pwsh -NoProfile -File tools/Publish-UserTestBuild.ps1 `
  -Configuration Release -RuntimeIdentifier win-x64 `
  -OutputRoot artifacts/release -Version 0.8.200 `
  -PublishMode SingleFile `
  -ArtifactSigningMetadataPath tools/signing/metadata.json
```

For the Velopack installer, portable archive, and update feed:

```powershell
pwsh -NoProfile -File tools/Publish-UserTestBuild.ps1 `
  -Configuration Release -RuntimeIdentifier win-x64 `
  -OutputRoot artifacts/release -Version 0.8.200 `
  -PublishMode Velopack `
  -ArtifactSigningMetadataPath tools/signing/metadata.json
```

Velopack invokes the repository signing wrapper through its native
`--signTemplate` option, so it signs the Windows application payload and
generated installer at the correct packaging stages. The wrapper retries
transient Azure failures and verifies every signed file. Do not
Authenticode-sign ZIP, NUPKG, JSON, SBOM, or checksum files.

## CI with GitHub OIDC

CI uses the dedicated `FreeX GitHub Artifact Signing` Entra application; it has
no client secret or PFX. Its only federated subject is
`repo:tony-xmelon/FreeX:environment:public-preview`, and its Azure access is the
**Artifact Signing Certificate Profile Signer** role at the
`freevia-public-signing` profile scope.

The `public-preview` GitHub environment stores `AZURE_CLIENT_ID`,
`AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID`. They identify the OIDC login;
none is a credential. `full-release.yml` grants `id-token: write`, runs the
commit-pinned Azure Login action, and installs the pinned signing client. Its
Windows jobs sign and verify each standalone executable and each app's
Velopack payload/installer before hashing. The suite job then embeds those
final signed app installers, signs and verifies the non-Inno suite bootstrapper,
and only then generates its checksum, SBOM, and manifest.

Every dispatch signs before publication. `prerelease=true` produces a signed
test/prerelease; `prerelease=false` produces a signed non-prerelease release.
Publication fails if OIDC login, signing, timestamping, or final Authenticode
verification fails. The human signer role is not used by GitHub. Azure Artifact
Signing does not sign Linux archives or macOS bundles; those lanes use the
integrity and Apple trust processes documented in
[app-platform-publish-lanes.md](app-platform-publish-lanes.md).
