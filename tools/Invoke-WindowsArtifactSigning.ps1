#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Authenticode-signs Windows release artifacts with Azure Artifact Signing.

.DESCRIPTION
    This is an explicit release operation. It never runs as part of an ordinary
    build. Authentication is provided by DefaultAzureCredential in the Artifact
    Signing client (for example, Azure CLI locally or workload identity in CI).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [Alias("File")]
    [string[]]$Files,

    [string]$MetadataPath = (Join-Path $PSScriptRoot "signing/metadata.json"),

    [string]$SignToolPath,

    [string]$DlibPath,

    [string]$TimestampUrl = "http://timestamp.acs.microsoft.com",

    [ValidateRange(1, 5)]
    [int]$MaxAttempts = 3,

    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

if (-not (Test-ToolIsWindows)) {
    throw "Azure Artifact Signing with SignTool is supported only on Windows."
}

function Resolve-UniqueSigningTool {
    param(
        [Parameter(Mandatory = $true)][string]$DisplayName,
        [Parameter(Mandatory = $true)][string]$FileName,
        [string]$ExplicitPath,
        [string[]]$SearchRoots,
        [switch]$RequireX64Path
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        return (Resolve-Path -LiteralPath $ExplicitPath -ErrorAction Stop).Path
    }

    $command = Get-Command $FileName -ErrorAction SilentlyContinue
    if ($command -and $command.Source) {
        return $command.Source
    }

    $matches = @(
        @(
            foreach ($root in $SearchRoots | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_ -PathType Container) }) {
                Get-ChildItem -LiteralPath $root -Filter $FileName -File -Recurse -ErrorAction SilentlyContinue |
                    Where-Object { -not $RequireX64Path -or $_.FullName -match '[\\/]x64[\\/]' }
            }
        ) | Sort-Object FullName -Descending -Unique
    )

    if ($matches.Count -eq 0) {
        throw "$DisplayName was not found. Install Microsoft.Azure.ArtifactSigningClientTools or pass its path explicitly."
    }

    return $matches[0].FullName
}

$metadata = Get-Content -LiteralPath $MetadataPath -Raw -ErrorAction Stop | ConvertFrom-Json
foreach ($propertyName in @("Endpoint", "CodeSigningAccountName", "CertificateProfileName")) {
    if (-not $metadata.PSObject.Properties[$propertyName] -or [string]::IsNullOrWhiteSpace([string]$metadata.$propertyName)) {
        throw "Artifact Signing metadata is missing '$propertyName': $MetadataPath"
    }
}
if ([string]$metadata.Endpoint -match 'REPLACE-WITH|<|>') {
    throw "Replace the Endpoint placeholder in '$MetadataPath' with the exact Account URI from Azure Artifact Signing."
}
if ([string]$metadata.Endpoint -notmatch '^https://[a-z0-9-]+\.codesigning\.azure\.net/?$') {
    throw "Artifact Signing Endpoint must be an https://<region>.codesigning.azure.net Account URI."
}
if ([string]$metadata.CodeSigningAccountName -ne "free-software-signing" -or
    [string]$metadata.CertificateProfileName -ne "freevia-public-signing") {
    throw "Artifact Signing metadata must target free-software-signing/freevia-public-signing."
}

$programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$signToolRoots = @(
    (Join-Path $programFilesX86 "Windows Kits/10/bin"),
    (Join-Path $programFiles "Windows Kits/10/bin"),
    (Join-Path $localAppData "Microsoft/WinGet/Packages")
)
$dlibRoots = @(
    (Join-Path $localAppData "Microsoft/MicrosoftArtifactSigningClientTools"),
    (Join-Path $programFilesX86 "Microsoft/ArtifactSigningClientTools"),
    (Join-Path $programFiles "Microsoft/ArtifactSigningClientTools"),
    (Join-Path $localAppData "Microsoft/WinGet/Packages")
)

$resolvedSignTool = Resolve-UniqueSigningTool -DisplayName "x64 SignTool" -FileName "signtool.exe" -ExplicitPath $SignToolPath -SearchRoots $signToolRoots -RequireX64Path
if (-not $VerifyOnly) {
    $resolvedDlib = Resolve-UniqueSigningTool -DisplayName "x64 Artifact Signing dlib" -FileName "Azure.CodeSigning.Dlib.dll" -ExplicitPath $DlibPath -SearchRoots $dlibRoots
}

$azureCliDirectory = Join-Path $programFiles "Microsoft SDKs/Azure/CLI2/wbin"
if ((Test-Path -LiteralPath $azureCliDirectory -PathType Container) -and
    -not (($env:PATH -split [IO.Path]::PathSeparator) -contains $azureCliDirectory)) {
    $env:PATH = "$azureCliDirectory$([IO.Path]::PathSeparator)$env:PATH"
}

$resolvedFiles = @(
    foreach ($path in $Files) {
        (Resolve-Path -LiteralPath $path -ErrorAction Stop).Path
    }
)
if ($resolvedFiles.Count -eq 0) {
    throw "At least one file is required."
}

foreach ($path in $resolvedFiles) {
    if (-not $VerifyOnly) {
        for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
            Write-Host "Artifact Signing (attempt $attempt/$MaxAttempts): $path"
            & $resolvedSignTool sign /v /fd SHA256 /tr $TimestampUrl /td SHA256 /dlib $resolvedDlib /dmdf $MetadataPath $path
            if ($LASTEXITCODE -eq 0) {
                break
            }
            if ($attempt -eq $MaxAttempts) {
                throw "Artifact Signing failed for '$path' after $MaxAttempts attempts (last exit code $LASTEXITCODE)."
            }
            Start-Sleep -Seconds ([Math]::Pow(2, $attempt))
        }
    }

    & $resolvedSignTool verify /v /pa /all $path
    if ($LASTEXITCODE -ne 0) {
        throw "Authenticode verification failed for '$path' with exit code $LASTEXITCODE."
    }
}

Write-Host "Verified $($resolvedFiles.Count) signed Windows artifact(s)."
