#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Authenticode-signs Windows release artifacts with Azure Artifact Signing.

.DESCRIPTION
    This is an explicit release operation. It never runs as part of an ordinary
    build. Authentication is provided by DefaultAzureCredential in the Artifact
    Signing client (for example, Azure CLI locally or workload identity in CI).
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory = $true)]
    [Alias("File")]
    [string[]]$Files,

    # `pwsh -File` binds only the first value after -Files to an array parameter; the rest arrive
    # as unnamed arguments. Velopack's multi-file sign template (`{{file...}}`) expands to
    # `-Files "a" "b" "c"`, so collect the rest here and treat them as part of -Files.
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalFiles,

    [string]$MetadataPath = (Join-Path $PSScriptRoot "signing/metadata.json"),

    [string]$SignToolPath,

    [string]$DlibPath,

    [string]$TimestampUrl = "http://timestamp.acs.microsoft.com",

    [ValidateRange(1, 5)]
    [int]$MaxAttempts = 3,

    # Files signed at the same time within one call. Signing is a network round trip to Azure, so this
    # is bounded by the service rather than the runner; failed files already retry with backoff.
    [ValidateRange(1, 32)]
    [int]$ThrottleLimit = 8,

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
$userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
$dotnetToolStore = Join-Path $userProfile ".dotnet/tools/.store/vpk"
$signToolRoots = @(
    (Join-Path $programFilesX86 "Windows Kits/10/bin"),
    (Join-Path $programFiles "Windows Kits/10/bin"),
    $dotnetToolStore,
    (Join-Path $localAppData "Microsoft/WinGet/Packages")
)
$dlibRoots = @(
    (Join-Path $localAppData "Microsoft/MicrosoftArtifactSigningClientTools"),
    $dotnetToolStore,
    (Join-Path $programFilesX86 "Microsoft/ArtifactSigningClientTools"),
    (Join-Path $programFiles "Microsoft/ArtifactSigningClientTools"),
    (Join-Path $localAppData "Microsoft/WinGet/Packages")
)

$resolvedSignTool = Resolve-UniqueSigningTool -DisplayName "x64 SignTool" -FileName "signtool.exe" -ExplicitPath $SignToolPath -SearchRoots $signToolRoots -RequireX64Path
$resolvedDlib = $null
if (-not $VerifyOnly) {
    $resolvedDlib =Resolve-UniqueSigningTool -DisplayName "x64 Artifact Signing dlib" -FileName "Azure.CodeSigning.Dlib.dll" -ExplicitPath $DlibPath -SearchRoots $dlibRoots
}

$azureCliDirectory = Join-Path $programFiles "Microsoft SDKs/Azure/CLI2/wbin"
if ((Test-Path -LiteralPath $azureCliDirectory -PathType Container) -and
    -not (($env:PATH -split [IO.Path]::PathSeparator) -contains $azureCliDirectory)) {
    $env:PATH = "$azureCliDirectory$([IO.Path]::PathSeparator)$env:PATH"
}

$resolvedFiles = @(
    foreach ($path in @($Files) + @($AdditionalFiles | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        (Resolve-Path -LiteralPath $path -ErrorAction Stop).Path
    }
)
if ($resolvedFiles.Count -eq 0) {
    throw "At least one file is required."
}

# One file's sign (with retries) and verify. Output is collected rather than written so that files
# signed concurrently do not interleave their SignTool transcripts; the caller replays each one whole.
# Every file still gets exactly the same SignTool sign and verify commands as before, and a failure
# is still reported per file after every in-flight file has finished.
$signOneFile = {
    param($Path, $SignTool, $Dlib, $Metadata, $Timestamp, $Attempts, $SkipSigning)

    # SignTool writes progress to stderr; merging it must not abort on Windows PowerShell's Stop preference.
    $ErrorActionPreference = "Continue"
    $transcript = New-Object System.Collections.Generic.List[string]
    $failure = $null

    if (-not $SkipSigning) {
        for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
            $transcript.Add("Artifact Signing (attempt $attempt/${Attempts}): $Path")
            foreach ($line in @(& $SignTool sign /v /fd SHA256 /tr $Timestamp /td SHA256 /dlib $Dlib /dmdf $Metadata $Path 2>&1)) {
                $transcript.Add([string]$line)
            }
            if ($LASTEXITCODE -eq 0) {
                break
            }
            if ($attempt -eq $Attempts) {
                $failure = "Artifact Signing failed for '$Path' after $Attempts attempts (last exit code $LASTEXITCODE)."
                break
            }
            Start-Sleep -Seconds ([Math]::Pow(2, $attempt))
        }
    }

    if ($null -eq $failure) {
        foreach ($line in @(& $SignTool verify /v /pa /all $Path 2>&1)) {
            $transcript.Add([string]$line)
        }
        if ($LASTEXITCODE -ne 0) {
            $failure = "Authenticode verification failed for '$Path' with exit code $LASTEXITCODE."
        }
    }

    [pscustomobject]@{ Path = $Path; Transcript = $transcript.ToArray(); Failure = $failure }
}

# Velopack hands this script a whole batch (--signParallel) per call. The Azure signing round trip is
# network latency, not CPU, so a batch signs concurrently; one file, or Windows PowerShell, signs in line.
$results = if ($resolvedFiles.Count -gt 1 -and $PSVersionTable.PSVersion.Major -ge 7) {
    # A script block cannot cross into a -Parallel runspace, so it travels as text and is rebuilt there.
    $signOneFileText = $signOneFile.ToString()
    $resolvedFiles | ForEach-Object -ThrottleLimit $ThrottleLimit -Parallel {
        & ([scriptblock]::Create($using:signOneFileText)) $_ $using:resolvedSignTool $using:resolvedDlib $using:MetadataPath $using:TimestampUrl $using:MaxAttempts ([bool]$using:VerifyOnly)
    }
} else {
    foreach ($path in $resolvedFiles) {
        & $signOneFile $path $resolvedSignTool $resolvedDlib $MetadataPath $TimestampUrl $MaxAttempts ([bool]$VerifyOnly)
    }
}

$failures = @()
foreach ($result in @($results)) {
    foreach ($line in $result.Transcript) {
        Write-Host $line
    }
    if ($null -ne $result.Failure) {
        $failures += $result.Failure
    }
}
if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}

Write-Host "Verified $($resolvedFiles.Count) signed Windows artifact(s)."
