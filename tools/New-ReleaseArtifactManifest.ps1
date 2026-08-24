#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('App','Suite')][string]$Scope,
    [Parameter(Mandatory)][ValidateSet('FreeX','FreeW','FreeP')][string[]]$Apps,
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$')][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$CommitSha,
    [Parameter(Mandatory)][string[]]$Runtimes,
    [Parameter(Mandatory)][string]$InputRoot,
    [Parameter(Mandatory)][string]$OutputPath,
    [switch]$RequireSourceManifests,
    [switch]$RequireRuntimeManifests,
    [switch]$StageLegalBundle,
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Runtimes = @($Runtimes | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Runtimes.Count -eq 0) { throw 'At least one runtime is required.' }
if ($Scope -eq 'App' -and $Apps.Count -ne 1) { throw 'App scope requires exactly one app.' }
if ($Scope -eq 'Suite' -and @($Apps | Sort-Object -Unique).Count -ne 3) { throw 'Suite scope requires all three apps.' }
$InputRoot = (Resolve-Path -LiteralPath $InputRoot).Path
$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
if (-not $RepositoryRoot) { $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path }
else { $RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path }

function Find-One([string]$Name) {
    $matches = @(Get-ChildItem -LiteralPath $InputRoot -Recurse -File -Filter $Name)
    if ($matches.Count -ne 1) { throw "Expected exactly one '$Name' below '$InputRoot'; found $($matches.Count)." }
    $matches[0]
}

function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

function Validate-Checksum([System.IO.FileInfo]$Payload) {
    $checksum = Find-One "$($Payload.Name).sha256"
    $actual = Hash $Payload.FullName
    $expectedLine = "$actual  $($Payload.Name)"
    $text = (Get-Content -LiteralPath $checksum.FullName -Raw).Trim()
    if ($text -cne $expectedLine) { throw "Checksum mismatch or non-canonical checksum content for '$($Payload.Name)'." }
    [ordered]@{ name = $checksum.Name; kind = 'checksum'; size = $checksum.Length; sha256 = (Hash $checksum.FullName) }
}

$entries = [System.Collections.Generic.List[object]]::new()
foreach ($runtime in $Runtimes) {
    if ($runtime -notmatch '^(win|linux|osx)-(x64|arm64)$') { throw "Unsupported runtime '$runtime'." }
    $prefix = if ($Scope -eq 'Suite') { "FreeSuite-v$Version-$runtime" } else { "$($Apps[0])-v$Version-$runtime" }
    $payloadNames = if ($Scope -eq 'Suite') {
        if ($runtime -like 'win-*') { @("$prefix-setup.exe") }
        elseif ($runtime -like 'linux-*') { @("$prefix-installer.zip") }
        else { @("$prefix-apps.zip") }
    } else {
        if ($runtime -like 'win-*') { @("$prefix.exe", "$prefix-setup.exe") }
        elseif ($runtime -like 'linux-*') { @("$prefix.zip", "$prefix-installer.zip") }
        else { @("$prefix.zip", "$prefix-apps.zip") }
    }
    foreach ($name in $payloadNames) {
        $file = Find-One $name
        $kind = if ($name -match 'setup|installer|apps') { 'installer' } else { 'portable' }
        $entries.Add([ordered]@{ name = $file.Name; kind = $kind; runtime = $runtime; size = $file.Length; sha256 = (Hash $file.FullName) })
        $entries.Add((Validate-Checksum $file))
    }
    $sbom = Find-One "$prefix.spdx.json"
    $entries.Add([ordered]@{ name = $sbom.Name; kind = 'sbom'; runtime = $runtime; size = $sbom.Length; sha256 = (Hash $sbom.FullName) })
    $entries.Add((Validate-Checksum $sbom))

    if ($RequireSourceManifests) {
        if ($Scope -eq 'App') {
            $sourceManifests = @(Find-One "$prefix-manifest.json")
        } else {
            $sourceManifests = @()
            foreach ($app in $Apps) { $sourceManifests += Find-One "$app-v$Version-$runtime-manifest.json" }
        }
        foreach ($source in $sourceManifests) {
            $sourceData = Get-Content -LiteralPath $source.FullName -Raw | ConvertFrom-Json
            if ($sourceData.commitSha -cne $CommitSha.ToLowerInvariant() -or $sourceData.version -cne $Version) {
                throw "Source manifest '$($source.Name)' does not belong to commit $CommitSha and version $Version."
            }
            if ($Scope -eq 'App') {
                $expectedNames = @($payloadNames | ForEach-Object { $_; "$_.sha256" }) + @("$prefix.spdx.json", "$prefix.spdx.json.sha256")
                $sourceArtifacts = @($sourceData.artifacts)
                if ($sourceArtifacts.Count -ne $expectedNames.Count) {
                    throw "Source manifest '$($source.Name)' has an unexpected artifact count."
                }
                foreach ($expectedName in $expectedNames) {
                    $expectedFile = Find-One $expectedName
                    $record = @($sourceArtifacts | Where-Object { $_.name -ceq $expectedName })
                    if ($record.Count -ne 1 -or $record[0].sha256 -cne (Hash $expectedFile.FullName) -or [long]$record[0].size -ne $expectedFile.Length) {
                        throw "Source manifest '$($source.Name)' does not match '$expectedName'."
                    }
                }
            }
            $entries.Add([ordered]@{ name = $source.Name; kind = 'source-manifest'; runtime = $runtime; size = $source.Length; sha256 = (Hash $source.FullName) })
        }
    }
    if ($RequireRuntimeManifests) {
        $runtimeManifest = Find-One "$prefix-manifest.json"
        $runtimeData = Get-Content -LiteralPath $runtimeManifest.FullName -Raw | ConvertFrom-Json
        if ($runtimeData.commitSha -cne $CommitSha.ToLowerInvariant() -or $runtimeData.version -cne $Version) {
            throw "Runtime manifest '$($runtimeManifest.Name)' does not belong to commit $CommitSha and version $Version."
        }
        $entries.Add([ordered]@{ name = $runtimeManifest.Name; kind = 'runtime-manifest'; runtime = $runtime; size = $runtimeManifest.Length; sha256 = (Hash $runtimeManifest.FullName) })
        if ($Scope -eq 'Suite') {
            foreach ($app in $Apps) {
                $childManifest = Find-One "$app-v$Version-$runtime-manifest.json"
                $childData = Get-Content -LiteralPath $childManifest.FullName -Raw | ConvertFrom-Json
                if ($childData.commitSha -cne $CommitSha.ToLowerInvariant() -or $childData.version -cne $Version) {
                    throw "Child manifest '$($childManifest.Name)' does not belong to commit $CommitSha and version $Version."
                }
                $entries.Add([ordered]@{ name = $childManifest.Name; kind = 'source-manifest'; runtime = $runtime; size = $childManifest.Length; sha256 = (Hash $childManifest.FullName) })
            }
        }
    }
}

$legalSources = [System.Collections.Generic.List[object]]::new()
if ($StageLegalBundle) {
    $legalRelativePaths = @('LICENSE','THIRD_PARTY_NOTICES.md','THIRD_PARTY_LICENSES.md','docs/legal/legal-notices.md','docs/legal/privacy.md')
    $thirdParty = @(Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'docs/legal/licenses') -Recurse -File | Sort-Object FullName)
    $legalFiles = @($legalRelativePaths | ForEach-Object { Get-Item -LiteralPath (Join-Path $RepositoryRoot $_) }) + $thirdParty
    $stage = Join-Path $outputDirectory '.legal-stage'
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    foreach ($file in $legalFiles) {
        $relative = [System.IO.Path]::GetRelativePath($RepositoryRoot, $file.FullName).Replace('\','/')
        $destination = Join-Path $stage $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
        $legalSources.Add([ordered]@{ path = $relative; sha256 = (Hash $file.FullName) })
    }
    $legalName = "FreeFamily-v$Version-legal.zip"
    $legalPath = Join-Path $outputDirectory $legalName
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $legalPath -Force
    $legalHash = Hash $legalPath
    "$legalHash  $legalName" | Set-Content -LiteralPath "$legalPath.sha256" -NoNewline -Encoding ascii
    $legalFile = Get-Item -LiteralPath $legalPath
    $entries.Add([ordered]@{ name = $legalName; kind = 'legal-bundle'; size = $legalFile.Length; sha256 = $legalHash })
    $legalChecksum = Get-Item -LiteralPath "$legalPath.sha256"
    $entries.Add([ordered]@{ name = $legalChecksum.Name; kind = 'checksum'; size = $legalChecksum.Length; sha256 = (Hash $legalChecksum.FullName) })
}

$manifest = [ordered]@{
    schemaVersion = 1
    scope = $Scope.ToLowerInvariant()
    apps = @($Apps)
    version = $Version
    commitSha = $CommitSha.ToLowerInvariant()
    runtimes = @($Runtimes)
    generatedUtc = [DateTime]::UtcNow.ToString('O')
    artifacts = @($entries | Sort-Object name)
    legalSources = @($legalSources | Sort-Object path)
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
Write-Host "Validated release inventory and wrote $OutputPath"
