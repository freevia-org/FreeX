# Windows Artifact Signing

FreeX's Windows release is not Store-only. The canonical channel publishes a
self-contained portable executable and an Inno Setup executable directly on
GitHub Releases. Microsoft Store signing would cover a Store-submitted MSIX,
but it does not sign these direct-download files. The Windows GitHub assets
therefore use Azure Artifact Signing with the Public Trust profile when signing
is explicitly enabled.

The Azure resources are:

- tenant: `073e2caa-267e-4a85-8970-e6129ec806a9` (`Freevia.org`)
- subscription: `cdc114ef-0580-49c2-a5e0-9e43d63b9fd0`
- resource group: `rg-signing`
- account: `free-software-signing`
- certificate profile: `freevia-public-signing`
- timestamp authority: `http://timestamp.acs.microsoft.com`

Signing is an explicit packaging option, never an ordinary build target. The
portable executable is signed before Inno Setup embeds it. Inno then signs the
generated uninstaller and final setup executable. SHA-256 files, SBOMs, and
release manifests are generated only after signing.

## One-time local setup

1. In Azure Portal, open `free-software-signing` and copy **Account URI** from
   Overview. Replace the placeholder `Endpoint` in
   `tools/signing/metadata.json` with that exact regional URI. Do not infer the
   region: an endpoint mismatch normally fails with HTTP 403.
2. Install the current client bundle on Windows:

   ```powershell
   winget install -e --id Microsoft.Azure.ArtifactSigningClientTools
   ```

   The client requires a supported x64 SignTool (Windows SDK
   10.0.2261.755 or newer), the matching x64
   `Azure.CodeSigning.Dlib.dll`, .NET 8, and the Visual C++ runtime. The script
   auto-discovers the normal installed paths; `-SignToolPath` and `-DlibPath`
   are available for a nonstandard or pinned tool directory.
3. Authenticate the user that has **Artifact Signing Certificate Profile
   Signer**:

   ```powershell
   az login --tenant 073e2caa-267e-4a85-8970-e6129ec806a9
   az account set --subscription cdc114ef-0580-49c2-a5e0-9e43d63b9fd0
   ```

The metadata contains resource names, not credentials. Authentication is
resolved by the Artifact Signing client at execution time.

## Sign one existing file

```powershell
pwsh -NoProfile -File tools/Invoke-WindowsArtifactSigning.ps1 `
  -Files artifacts/release/FreeX-v0.8.200-win-x64.exe
```

The command signs with SHA-256, adds an RFC 3161 SHA-256 timestamp, then runs
`signtool verify /pa /all`. It fails before contacting Azure while the endpoint
placeholder remains. To verify without creating another signature, add
`-VerifyOnly`.

## Build signed portable and installer assets

```powershell
$metadata = (Resolve-Path tools/signing/metadata.json).Path

pwsh -NoProfile -File tools/Publish-SisterAppTesterPackages.ps1 `
  -App FreeX -Version 0.8.200 -Runtimes win-x64 `
  -WindowsPackageMode SingleFile -Configuration Release `
  -OutputDir artifacts/release `
  -ArtifactSigningMetadataPath $metadata

pwsh -NoProfile -File tools/packaging/New-AppInstallers.ps1 `
  -Apps FreeX -Platform windows -Version 0.8.200 -Runtime win-x64 `
  -InputRoot artifacts/release -OutputDir artifacts/release `
  -ArtifactSigningMetadataPath $metadata
```

Use the same installer option for the suite after all three child installers
have been signed. The suite bootstrapper embeds those signed children and signs
the final outer setup last. Do not Authenticode-sign ZIP, NUPKG, SBOM, JSON, or
checksum files.

## CI with GitHub OIDC

Do not use Quinn Smith's interactive identity in CI and do not store a PFX or
client secret. Create a dedicated Entra application or managed identity with a
GitHub federated credential, then grant only that identity **Artifact Signing
Certificate Profile Signer** at the `freevia-public-signing` profile scope.
That Azure setup is intentionally not automated by this repository.

The Windows package job needs `permissions: id-token: write` and `contents:
read`, followed by `azure/login@v3` using repository secrets
`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID`. Prefer a
protected release environment and restrict the federated subject to the release
workflow/ref. After login, install the Artifact Signing client tools on the
Windows runner and pass the same `-ArtifactSigningMetadataPath` option to the
two packaging commands above. Pin every third-party action to a reviewed commit
before enabling the lane.

CI must fail closed: signed publication should be enabled only after the exact
Account URI is committed, the workload identity exists, its profile-scoped
signer role is verified, and a signed dry run passes `signtool verify /pa /all`.
The current human signer role does not authorize the GitHub runner.

## Store packages

A future Store-only MSIX lane can be submitted unsigned for Microsoft to sign,
using the exact Partner Center package identity. Direct signing of the existing
tester MSIX is separate: its manifest `Publisher` must exactly match the
Artifact Signing certificate subject DN. Do not guess or copy the current
tester/PFX-derived publisher value. Store migration does not remove the need to
sign portable or Inno Setup files while those remain public downloads.
