#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('App','Suite')][string]$Scope,
    [Parameter(Mandatory)][ValidateSet('FreeX','FreeW','FreeP')][string[]]$Apps,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidatePattern('^(win|linux|osx)-(x64|arm64)$')][string]$Runtime,
    [Parameter(Mandatory)][string]$InputRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")
$Apps = @($Apps | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Scope -eq 'App' -and $Apps.Count -ne 1) { throw 'App scope requires exactly one app.' }
if ($Scope -eq 'Suite' -and @($Apps | Sort-Object -Unique).Count -ne 3) { throw 'Suite scope requires all three apps.' }
$InputRoot = (Resolve-Path -LiteralPath $InputRoot).Path

function Find-One([string]$Name) {
    Find-ToolReleaseArtifact -InputRoot $InputRoot -Name $Name
}

function Assert-Pe([System.IO.FileInfo]$File, [string]$Description) {
    $stream = $File.OpenRead()
    try {
        if ($stream.ReadByte() -ne 0x4d -or $stream.ReadByte() -ne 0x5a) { throw "$Description is not a Windows PE executable: $($File.Name)" }
    } finally { $stream.Dispose() }
    $signature = Get-AuthenticodeSignature -LiteralPath $File.FullName
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "$Description does not have a valid Authenticode signature: $($File.Name) ($($signature.Status))"
    }
}

function Assert-CleanZip([System.IO.FileInfo]$File) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($File.FullName)
    try {
        $debugEntries = @($archive.Entries | Where-Object {
            $_.FullName -match '(?i)(^|/)(bin|obj|debug)(/|$)' -or $_.FullName -match '(?i)\.(pdb|dbg)$'
        })
        if ($debugEntries.Count -gt 0) {
            throw "Debug artifact found in '$($File.Name)': $($debugEntries[0].FullName)"
        }
    } finally { $archive.Dispose() }
}

$prefix = if ($Scope -eq 'Suite') { "FreeSuite-v$Version-$Runtime" } else { "$($Apps[0])-v$Version-$Runtime" }
if ($Runtime -like 'win-*') {
    if ($Scope -eq 'App') {
        $portable = Find-One "$prefix.exe"
        if ($portable.Length -eq 0) { throw "Standalone executable missing or empty: $($portable.Name)" }
        Assert-Pe $portable 'Standalone executable'
    }
    $installer = Find-One "$prefix-setup.exe"
    if ($installer.Length -eq 0) { throw "Windows installer missing or empty: $($installer.Name)" }
    Assert-Pe $installer 'Windows installer'
} elseif ($Runtime -like 'linux-*' -or $Scope -eq 'App') {
    if ($Scope -eq 'App') {
        $portable = Find-One "$prefix.zip"
        Assert-CleanZip $portable
    }
    $suffix = if ($Runtime -like 'linux-*') { 'installer' } else { 'apps' }
    $installer = Find-One "$prefix-$suffix.zip"
    Assert-CleanZip $installer
} else {
    $installer = Find-One "$prefix.pkg"
    if ($installer.Length -eq 0) { throw "macOS suite package missing or empty: $($installer.Name)" }
    & pkgutil --check-signature $installer.FullName
    if ($LASTEXITCODE -ne 0) { throw "macOS suite package signature validation failed: $($installer.Name)" }
}

Write-Host "Release package content gate passed for $Scope $Runtime."
