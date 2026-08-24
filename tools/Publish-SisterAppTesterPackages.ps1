#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Publish tester packages for FreeX, FreeW, or FreeP across the current tester runtimes.

.DESCRIPTION
    This is the explicit fallback lane for the sister apps while their hosted
    GitHub release publishers are being promoted to the same maturity as FreeX.
    Windows uses the WPF host and a self-contained single-file executable.
    Linux and macOS use the Avalonia host and self-contained zip packages.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Publish-SisterAppTesterPackages.ps1 -App FreeW -Version 0.8.149
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("FreeX", "FreeW", "FreeP")]
    [string]$App,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string[]]$Runtimes = @("win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"),

    [ValidateSet("SingleFile", "FolderZip")]
    [string]$WindowsPackageMode = "SingleFile",

    [ValidateSet("Release")]
    [string]$Configuration = "Release",

    [string]$OutputDir
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# `powershell -File ... -Runtimes linux-x64,linux-arm64` reaches a string[]
# parameter as one comma-delimited value. Normalize both that invocation shape
# and ordinary PowerShell array binding before publishing any runtime.
$Runtimes = @(
    $Runtimes |
        ForEach-Object { $_ -split "," } |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)
if ($Runtimes.Count -eq 0) {
    throw "At least one runtime is required."
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path
$testerReleaseSmokeProjectPath = Join-Path $repoRoot "tools\FreeX.Validation.Wpf\FreeX.Validation.Wpf.csproj"
if (-not $OutputDir) {
    $OutputDir = Join-Path $repoRoot "artifacts\sister-tester-release-$Version"
}

$commitSha = (& git -C $repoRoot rev-parse HEAD).Trim().ToLowerInvariant()
if ($commitSha -notmatch '^[0-9a-f]{40}$') {
    throw "Could not resolve git commit."
}
$informationalSha = $commitSha.Substring(0, 8)

$config = switch ($App) {
    "FreeX" {
        @{
            WpfProject = "src\FreeX.App.Host\FreeX.App.Host.csproj"
            AvaloniaProject = "src\FreeX.App.Avalonia\FreeX.App.Avalonia.csproj"
            AvaloniaValidationProject = "tools\FreeX.Validation.Avalonia\FreeX.Validation.Avalonia.csproj"
            WpfHost = "FreeX.App.Host"
            AvaloniaHost = "FreeX"
            AvaloniaValidationHost = "FreeX.Validation.Avalonia"
        }
    }
    "FreeW" {
        @{
            WpfProject = "freew\FreeW.App.Host\FreeW.App.Host.csproj"
            AvaloniaProject = "freew\FreeW.App.Avalonia\FreeW.App.Avalonia.csproj"
            AvaloniaValidationProject = "freew\TestSupport\Validation.Avalonia\FreeW.Validation.Avalonia.csproj"
            WpfHost = "FreeW.App.Host"
            AvaloniaHost = "FreeW"
            AvaloniaValidationHost = "FreeW.Validation.Avalonia"
        }
    }
    "FreeP" {
        @{
            WpfProject = "freep\FreeP.App.Host\FreeP.App.Host.csproj"
            AvaloniaProject = "freep\FreeP.App.Avalonia\FreeP.App.Avalonia.csproj"
            AvaloniaValidationProject = "freep\TestSupport\Validation.Avalonia\FreeP.Validation.Avalonia.csproj"
            WpfHost = "FreeP.App.Host"
            AvaloniaHost = "FreeP"
            AvaloniaValidationHost = "FreeP.Validation.Avalonia"
        }
    }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
foreach ($runtime in $Runtimes) {
    $isWindowsRuntime = $runtime -like "win-*"
    $isWindowsSingleFile = $isWindowsRuntime -and $WindowsPackageMode -eq "SingleFile"
    $hostKind = if ($isWindowsRuntime) { "wpf" } else { "avalonia" }
    $projectRelative = if ($isWindowsRuntime) { $config.WpfProject } else { $config.AvaloniaProject }
    $hostName = if ($isWindowsRuntime) { $config.WpfHost } else { $config.AvaloniaHost }
    $project = Join-Path $repoRoot $projectRelative
    if (-not (Test-Path -LiteralPath $project)) {
        throw "Could not find project '$project'."
    }

    $publishDir = Join-Path $OutputDir "publish\$App-$runtime-$hostKind"
    $packageType = if ($isWindowsSingleFile) { "singlefile-exe" } else { "zip" }
    $packageExtension = if ($isWindowsSingleFile) { ".exe" } else { ".zip" }
    $packageName = "$App-v$Version-$runtime$packageExtension"
    $packagePath = Join-Path $OutputDir $packageName

    if (Test-Path -LiteralPath $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }
    if (Test-Path -LiteralPath $packagePath) {
        Remove-Item -LiteralPath $packagePath -Force
    }
    New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

    $publishArgs = @(
        "publish", $project,
        "--configuration", $Configuration,
        "--runtime", $runtime,
        "--self-contained", "true",
        "-p:UseAppHost=true",
        "-p:PublishSingleFile=$($isWindowsSingleFile.ToString().ToLowerInvariant())",
        "-p:DebugType=None",
        "-p:DebugSymbols=false",
        "-p:Optimize=true",
        "-p:Version=$Version",
        "-p:InformationalVersion=$Version+$informationalSha",
        "--output", $publishDir
    )
    if ($isWindowsSingleFile) {
        $publishArgs += @(
            "-p:IncludeNativeLibrariesForSelfExtract=true",
            "-p:IncludeAllContentForSelfExtract=true"
        )
    }
    if (-not $isWindowsRuntime) {
        $publishArgs = @(
            "publish", $project,
            "--configuration", $Configuration,
            "--framework", "net10.0",
            "--runtime", $runtime,
            "--self-contained", "true",
            "-p:UseAppHost=true",
            "-p:PublishSingleFile=false",
            "-p:DebugType=None",
            "-p:DebugSymbols=false",
            "-p:Optimize=true",
            "-p:Version=$Version",
            "-p:InformationalVersion=$Version+$informationalSha",
            "--output", $publishDir
        )
        if ($App -eq "FreeP") {
            $publishArgs += "-p:FreePWindowsBuild=false"
        }
    }

    Write-Host ""
    Write-Host "Publishing $App $runtime ($hostKind)"
    Write-Host "dotnet $($publishArgs -join ' ')"
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $App $runtime with exit code $LASTEXITCODE."
    }

    $expectedAppHost = if ($isWindowsRuntime) { Join-Path $publishDir "$hostName.exe" } else { Join-Path $publishDir $hostName }
    if (-not (Test-Path -LiteralPath $expectedAppHost)) {
        throw "Publish output missing expected apphost '$expectedAppHost'."
    }
    $expectedExe = (Resolve-Path -LiteralPath $expectedAppHost).Path
    $smokeExecutable = $expectedExe

    if (-not $isWindowsRuntime) {
        $validationPublishDir = Join-Path $OutputDir "validation\$App-$runtime"
        if (Test-Path -LiteralPath $validationPublishDir) {
            Remove-Item -LiteralPath $validationPublishDir -Recurse -Force
        }
        New-Item -ItemType Directory -Force -Path $validationPublishDir | Out-Null

        $validationProject = Join-Path $repoRoot $config.AvaloniaValidationProject
        $validationPublishArgs = @(
            "publish", $validationProject,
            "--configuration", $Configuration,
            "--framework", "net10.0",
            "--runtime", $runtime,
            "--self-contained", "true",
            "-p:UseAppHost=true",
            "-p:PublishSingleFile=false",
            "-p:DebugType=None",
            "-p:DebugSymbols=false",
            "-p:Optimize=true",
            "-p:Version=$Version",
            "-p:InformationalVersion=$Version+$informationalSha",
            "--output", $validationPublishDir
        )
        if ($App -eq "FreeP") {
            $validationPublishArgs += "-p:FreePWindowsBuild=false"
        }
        & dotnet @validationPublishArgs
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed for $App $runtime validation host with exit code $LASTEXITCODE."
        }

        $smokeExecutable = Join-Path $validationPublishDir $config.AvaloniaValidationHost
        if (-not (Test-Path -LiteralPath $smokeExecutable)) {
            throw "Validation publish output missing expected apphost '$smokeExecutable'."
        }
        $smokeExecutable = (Resolve-Path -LiteralPath $smokeExecutable).Path
    }

    $smokeRan = $false
    if ($isWindowsRuntime -and $App -eq "FreeX") {
        $smokeRan = $true
        $smokeReportPath = Join-Path $OutputDir "$App-$runtime-tester-release-smoke.json"
        $smokeToolDir = Join-Path $OutputDir "smoke\$runtime"
        if (Test-Path -LiteralPath $smokeToolDir) {
            Remove-Item -LiteralPath $smokeToolDir -Recurse -Force
        }
        & dotnet publish $testerReleaseSmokeProjectPath `
            --configuration $Configuration `
            --runtime $runtime `
            --self-contained false `
            -p:DebugType=None `
            -p:DebugSymbols=false `
            -p:Optimize=true `
            --output $smokeToolDir
        if ($LASTEXITCODE -ne 0) {
            throw "$App $runtime tester-release smoke tool publish failed with exit code $LASTEXITCODE."
        }
        $smokeToolPath = Join-Path $smokeToolDir "FreeX.Validation.Wpf.exe"
        $smokeProcess = Start-Process `
            -FilePath $smokeToolPath `
            -ArgumentList @("--tester-release-smoke", $smokeReportPath) `
            -WindowStyle Hidden `
            -Wait `
            -PassThru
        if ($smokeProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $smokeReportPath)) {
            throw "$App $runtime tester-release smoke failed with exit code $($smokeProcess.ExitCode)."
        }

        $smokeReport = Get-Content -LiteralPath $smokeReportPath -Raw | ConvertFrom-Json
        if ($smokeReport.Success -ne $true) {
            throw "$App $runtime tester-release smoke reported failure."
        }
        Remove-Item -LiteralPath $smokeToolDir -Recurse -Force
    }
    elseif (-not $isWindowsRuntime) {
        $smokeRan = $true
        $smokeArguments = @("--packaging-smoke")
        $smokeReportPath = $null
        if ($App -eq "FreeP") {
            $smokeReportPath = Join-Path $OutputDir "$App-$runtime-packaging-smoke.txt"
            $smokeArguments += $smokeReportPath
        }

        & $smokeExecutable @smokeArguments
        if ($LASTEXITCODE -ne 0) {
            throw "$App $runtime packaging smoke failed with exit code $LASTEXITCODE."
        }
        if ($App -eq "FreeP" -and
            (-not (Test-Path -LiteralPath $smokeReportPath) -or
             (Get-Content -LiteralPath $smokeReportPath -Raw) -notmatch "freep_packaging_smoke=passed")) {
            throw "$App $runtime packaging smoke did not produce a passing report."
        }
    }

    if ($smokeRan) {
        Write-Host "Packaged smoke passed for $App $runtime."
    } else {
        Write-Host "$App $runtime has no packaged smoke entry point; the release gate uses its compiled test suite."
    }

    if ($isWindowsSingleFile) {
        $unexpectedPublishFiles = @(
            Get-ChildItem -LiteralPath $publishDir -File |
                Where-Object { $_.FullName -ne $expectedExe -and $_.Extension -ne ".pdb" }
        )
        if ($unexpectedPublishFiles.Count -gt 0) {
            $unexpectedNames = ($unexpectedPublishFiles | ForEach-Object { $_.Name }) -join ", "
            throw "Single-file Windows publish produced runtime sidecars: $unexpectedNames"
        }

        Copy-Item -LiteralPath $expectedExe -Destination $packagePath -Force
    } else {
        Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $packagePath -Force
    }

    $hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $shaPath = "$packagePath.sha256"
    "$hash  $packageName" | Set-Content -LiteralPath $shaPath -NoNewline -Encoding ascii
    Write-Host "Produced $packageName"
}
Write-Host "Source commit: $commitSha"
Write-Host "Run tools/New-ReleaseArtifactManifest.ps1 after installer and SBOM generation to create the complete release manifest."
