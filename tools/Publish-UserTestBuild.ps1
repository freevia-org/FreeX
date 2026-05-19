param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot = "artifacts\releases",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\Freexcel.App.Host\Freexcel.App.Host.csproj"
$appInfoPath = Join-Path $repoRoot "src\Freexcel.App.Host\AppInfo.cs"
$appInfo = Get-Content -LiteralPath $appInfoPath -Raw

if ([string]::IsNullOrWhiteSpace($Version)) {
    $versionMatch = [regex]::Match($appInfo, 'VersionText\s*=\s*"(?<version>[^"]+)"')
    if (-not $versionMatch.Success) {
        throw "Could not read VersionText from $appInfoPath"
    }

    $Version = $versionMatch.Groups["version"].Value
}

$versionSlug = $Version.ToLowerInvariant()
$versionSlug = $versionSlug -replace '^version\s+', ''
$versionSlug = $versionSlug -replace '[^a-z0-9]+', '-'
$versionSlug = $versionSlug.Trim('-')

if ([string]::IsNullOrWhiteSpace($versionSlug)) {
    throw "Version produced an empty artifact slug."
}

$commitId = git -C $repoRoot rev-parse --short=8 HEAD
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commitId)) {
    throw "Could not determine git commit id."
}

$buildStamp = Get-Date -Format "yyyyMMdd-HHmmss"
$artifactName = "freexcel-$versionSlug-$buildStamp-$commitId-$RuntimeIdentifier"
if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    $artifactRoot = $OutputRoot
} else {
    $artifactRoot = Join-Path $repoRoot $OutputRoot
}
$publishDir = Join-Path $artifactRoot $artifactName

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

dotnet publish $projectPath `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$launchExeName = "$artifactName.exe"
$defaultExePath = Join-Path $publishDir "Freexcel.App.Host.exe"
$launchExePath = Join-Path $publishDir $launchExeName
if (-not (Test-Path -LiteralPath $defaultExePath)) {
    throw "Expected apphost was not published at $defaultExePath"
}

if (Test-Path -LiteralPath $launchExePath) {
    Remove-Item -LiteralPath $launchExePath -Force
}

Move-Item -LiteralPath $defaultExePath -Destination $launchExePath

$launcherPath = Join-Path $publishDir "Freexcel.cmd"
$runtimeUrl = "https://dotnet.microsoft.com/download/dotnet/10.0"
$launcher = @"
@echo off
setlocal
set "APP_DIR=%~dp0"
set "APP_EXE=%APP_DIR%$launchExeName"
set "RUNTIME_URL=$runtimeUrl"

where dotnet >nul 2>nul
if errorlevel 1 goto missing_runtime

dotnet --list-runtimes | findstr /R /C:"^Microsoft.WindowsDesktop.App 10\." >nul 2>nul
if errorlevel 1 goto missing_runtime

start "" "%APP_EXE%"
exit /b 0

:missing_runtime
echo Freexcel needs the Microsoft .NET 10 Desktop Runtime.
echo.
echo Install the Desktop Runtime for Windows from:
echo %RUNTIME_URL%
echo.
echo After installation, run Freexcel.cmd again.
echo.
choice /M "Open the .NET 10 download page now"
if errorlevel 2 exit /b 1
start "" "%RUNTIME_URL%"
exit /b 1
"@
Set-Content -LiteralPath $launcherPath -Value $launcher -Encoding ASCII

$readmePath = Join-Path $publishDir "README.txt"
$readme = @"
Freexcel user test build

Version:
  $Version

Build:
  $artifactName

Run:
  Freexcel.cmd

This is a framework-dependent Windows build. It is smaller to share, but it
requires the Microsoft .NET 10 Desktop Runtime. The launcher checks for
Microsoft.WindowsDesktop.App 10.x and offers to open the runtime download page:

  $runtimeUrl

If the runtime is already installed, the launcher starts $launchExeName.
"@
Set-Content -LiteralPath $readmePath -Value $readme -Encoding ASCII

$zipPath = Join-Path $artifactRoot "$artifactName.zip"
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

if (-not (Test-Path -LiteralPath $zipPath)) {
    throw "Compress-Archive did not create $zipPath"
}

$hashPath = "$zipPath.sha256"
$hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
Set-Content -LiteralPath $hashPath -Value "$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $zipPath)" -Encoding ASCII

Write-Host "Created $publishDir"
Write-Host "Created $zipPath"
Write-Host "Created $hashPath"
