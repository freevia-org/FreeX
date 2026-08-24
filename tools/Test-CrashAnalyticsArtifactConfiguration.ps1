#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ArtifactPath,
    [string]$DsnEnvironmentVariable = "FREE_FAMILY_SENTRY_DSN",
    [Parameter(Mandatory = $true)][string]$ExpectedEnvironment,
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$dsn = [Environment]::GetEnvironmentVariable($DsnEnvironmentVariable)
if ([string]::IsNullOrWhiteSpace($dsn)) {
    throw "Crash analytics endpoint is absent from environment variable '$DsnEnvironmentVariable'."
}

$resolvedArtifact = (Resolve-Path -LiteralPath $ArtifactPath).Path
[object[]]$candidateFiles = if ([IO.File]::Exists($resolvedArtifact)) {
    @([IO.FileInfo]::new($resolvedArtifact))
} else {
    @(Get-ChildItem -LiteralPath $resolvedArtifact -Recurse -File |
        Where-Object { $_.Extension -in @('.exe', '.dll') })
}
if ($candidateFiles.Count -eq 0) {
    throw "No executable or assembly files were found in the artifact path."
}

function Test-BinaryContains {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes, [Parameter(Mandatory = $true)][string]$Value)

    $utf8 = [Text.Encoding]::UTF8.GetString($Bytes)
    if ($utf8.Contains($Value)) { return $true }
    $utf16 = [Text.Encoding]::Unicode.GetString($Bytes)
    return $utf16.Contains($Value)
}

[object[]]$configuredFiles = @()
foreach ($file in $candidateFiles) {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    if ((Test-BinaryContains $bytes $dsn) -and
        (Test-BinaryContains $bytes $ExpectedEnvironment)) {
        $configuredFiles += $file.FullName
    }
}

if ($configuredFiles.Count -eq 0) {
    throw "The artifact does not contain the configured crash endpoint and environment. Validate the publish-time FreeFamilySentryDsn and FreeFamilySentryEnvironment properties."
}

$result = [ordered]@{
    schemaVersion = 1
    artifactPath = $resolvedArtifact
    endpointConfigured = $true
    environment = $ExpectedEnvironment
    matchingBinaryCount = $configuredFiles.Count
}
$json = $result | ConvertTo-Json -Depth 3
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $parent = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        [IO.Directory]::CreateDirectory($parent) | Out-Null
    }
    [IO.File]::WriteAllText($OutputPath, $json, [Text.UTF8Encoding]::new($false))
}

Write-Host "Crash analytics artifact configuration is present (endpoint value withheld)."
Write-Output $json
