param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot = "artifacts\releases",
    [string]$Version = "",
    [ValidateSet("SingleFile", "Folder", "Msix")]
    [string]$PublishMode = "SingleFile",
    [string]$MsixCertificatePath = $env:FREEX_MSIX_CERTIFICATE_PATH,
    [string]$MsixCertificatePassword = $env:FREEX_MSIX_CERTIFICATE_PASSWORD,
    [string]$MsixTimestampUrl = $env:FREEX_MSIX_TIMESTAMP_URL
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\FreeX.App.Host\FreeX.App.Host.csproj"
$appInfoPath = Join-Path $repoRoot "src\FreeX.App.Host\AppInfo.cs"
$appInfo = Get-Content -LiteralPath $appInfoPath -Raw

function ConvertTo-MsixPackageVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DisplayVersion
    )

    $numericParts = [regex]::Matches($DisplayVersion, '\d+') | ForEach-Object { [int64]$_.Value }
    if ($numericParts.Count -eq 0) {
        throw "MSIX packaging requires a numeric version, but '$DisplayVersion' contains no numeric parts."
    }

    $msixParts = @(0L, 0L, 0L, 0L)
    for ($i = 0; $i -lt [Math]::Min(4, $numericParts.Count); $i++) {
        if ($numericParts[$i] -lt 0) {
            throw "MSIX version part '$($numericParts[$i])' is outside the 0-65535 range."
        }

        $msixParts[$i] = $numericParts[$i]
    }

    for ($i = 3; $i -gt 0; $i--) {
        if ($msixParts[$i] -gt 65535) {
            $carry = [Math]::Floor($msixParts[$i] / 65536)
            $msixParts[$i] = $msixParts[$i] % 65536
            $msixParts[$i - 1] += $carry
        }
    }

    if ($msixParts[0] -gt 65535) {
        throw "MSIX version part '$($msixParts[0])' is outside the 0-65535 range."
    }

    return ($msixParts | ForEach-Object { [string]$_ }) -join "."
}

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
$modeSlug = $PublishMode.ToLowerInvariant()
$artifactName = "freex-$versionSlug-$buildStamp-$commitId-$RuntimeIdentifier-$modeSlug"
if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    $artifactRoot = $OutputRoot
} else {
    $artifactRoot = Join-Path $repoRoot $OutputRoot
}
$publishDir = if ($PublishMode -eq "SingleFile") {
    Join-Path $artifactRoot ".$artifactName-publish"
} else {
    Join-Path $artifactRoot $artifactName
}
$artifactExePath = Join-Path $artifactRoot "$artifactName.exe"
$artifactMsixPath = Join-Path $artifactRoot "$artifactName.msix"

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
if ($PublishMode -eq "SingleFile" -and (Test-Path -LiteralPath $artifactExePath)) {
    Remove-Item -LiteralPath $artifactExePath -Force
}
if ($PublishMode -eq "Msix" -and (Test-Path -LiteralPath $artifactMsixPath)) {
    Remove-Item -LiteralPath $artifactMsixPath -Force
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

$publishArgs = @(
    "publish", $projectPath,
    "-c", $Configuration,
    "-r", $RuntimeIdentifier,
    "--self-contained", "false",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-o", $publishDir
)

if ($PublishMode -eq "SingleFile") {
    $publishArgs += @(
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:IncludeAllContentForSelfExtract=true"
    )
} else {
    $publishArgs += @(
        "-p:PublishSingleFile=false"
    )
}

dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$launchExeName = "$artifactName.exe"
$defaultExePath = Join-Path $publishDir "FreeX.App.Host.exe"
$launchExePath = Join-Path $publishDir $launchExeName
if (-not (Test-Path -LiteralPath $defaultExePath)) {
    throw "Expected apphost was not published at $defaultExePath"
}

if (Test-Path -LiteralPath $launchExePath) {
    Remove-Item -LiteralPath $launchExePath -Force
}

Move-Item -LiteralPath $defaultExePath -Destination $launchExePath

$runtimeUrl = "https://dotnet.microsoft.com/download/dotnet/10.0"

if ($PublishMode -eq "SingleFile") {
    Move-Item -LiteralPath $launchExePath -Destination $artifactExePath
    $hash = Get-FileHash -LiteralPath $artifactExePath -Algorithm SHA256
    Set-Content -LiteralPath "$artifactExePath.sha256" -Value "$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $artifactExePath)" -Encoding ASCII
    Remove-Item -LiteralPath $publishDir -Recurse -Force
    Write-Host "Created $artifactExePath"
    Write-Host "Created $artifactExePath.sha256"
    exit 0
}

if ($PublishMode -eq "Msix") {
    $assetsDir = Join-Path $publishDir "Assets"
    New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null
    $pngBytes = [Convert]::FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=")
    [IO.File]::WriteAllBytes((Join-Path $assetsDir "Square44x44Logo.png"), $pngBytes)
    [IO.File]::WriteAllBytes((Join-Path $assetsDir "Square150x150Logo.png"), $pngBytes)

    $msixVersion = ConvertTo-MsixPackageVersion -DisplayVersion $Version
    $msixExeName = Split-Path -Leaf $launchExePath

    $manifestPath = Join-Path $publishDir "AppxManifest.xml"
    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap rescap">
  <Identity Name="FreeX.Tester" Publisher="CN=FreeXLocal" Version="$msixVersion" />
  <Properties>
    <DisplayName>FreeX</DisplayName>
    <PublisherDisplayName>FreeX</PublisherDisplayName>
    <Logo>Assets\Square150x150Logo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Resources>
    <Resource Language="en-us" />
  </Resources>
  <Applications>
    <Application Id="FreeX" Executable="$msixExeName" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="FreeX" Description="FreeX tester build" BackgroundColor="transparent" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png" />
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@
    Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding UTF8

    $makeAppxCommand = Get-Command makeappx.exe -ErrorAction SilentlyContinue
    $makeAppxPath = if ($null -ne $makeAppxCommand) { $makeAppxCommand.Source } else { $null }
    if ($null -eq $makeAppxPath) {
        $kitRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
        if (Test-Path -LiteralPath $kitRoot) {
            $makeAppxPath = Get-ChildItem -LiteralPath $kitRoot -Recurse -Filter makeappx.exe |
                Sort-Object FullName -Descending |
                Select-Object -First 1 -ExpandProperty FullName
        }
    }
    if ($null -eq $makeAppxPath) {
        throw "makeappx.exe was not found. Install the Windows SDK to create unsigned local MSIX packages."
    }

    & $makeAppxPath pack /d $publishDir /p $artifactMsixPath /o
    if ($LASTEXITCODE -ne 0) {
        throw "makeappx pack failed with exit code $LASTEXITCODE"
    }
    if (-not (Test-Path -LiteralPath $artifactMsixPath)) {
        throw "makeappx did not create $artifactMsixPath"
    }

    if (-not [string]::IsNullOrWhiteSpace($MsixCertificatePath)) {
        if (-not (Test-Path -LiteralPath $MsixCertificatePath)) {
            throw "MSIX signing certificate was not found at $MsixCertificatePath"
        }

        $signToolCommand = Get-Command signtool.exe -ErrorAction SilentlyContinue
        $signToolPath = if ($null -ne $signToolCommand) { $signToolCommand.Source } else { $null }
        if ($null -eq $signToolPath) {
            $kitRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
            if (Test-Path -LiteralPath $kitRoot) {
                $signToolPath = Get-ChildItem -LiteralPath $kitRoot -Recurse -Filter signtool.exe |
                    Sort-Object FullName -Descending |
                    Select-Object -First 1 -ExpandProperty FullName
            }
        }
        if ($null -eq $signToolPath) {
            throw "signtool.exe was not found. Install the Windows SDK to sign MSIX packages."
        }

        $signArgs = @("sign", "/fd", "SHA256", "/f", $MsixCertificatePath)
        if (-not [string]::IsNullOrWhiteSpace($MsixCertificatePassword)) {
            $signArgs += @("/p", $MsixCertificatePassword)
        }
        if (-not [string]::IsNullOrWhiteSpace($MsixTimestampUrl)) {
            $signArgs += @("/tr", $MsixTimestampUrl, "/td", "SHA256")
        }
        $signArgs += $artifactMsixPath

        & $signToolPath @signArgs
        if ($LASTEXITCODE -ne 0) {
            throw "signtool sign failed with exit code $LASTEXITCODE"
        }
        Write-Host "Signed $artifactMsixPath"
    } else {
        Write-Host "Created unsigned local MSIX; pass -MsixCertificatePath to sign it."
    }

    $hash = Get-FileHash -LiteralPath $artifactMsixPath -Algorithm SHA256
    Set-Content -LiteralPath "$artifactMsixPath.sha256" -Value "$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $artifactMsixPath)" -Encoding ASCII
    Write-Host "Created $artifactMsixPath"
    Write-Host "Created $artifactMsixPath.sha256"
    exit 0
}

if ($PublishMode -eq "Folder") {
    $launcherPath = Join-Path $publishDir "FreeX.cmd"
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
echo FreeX needs the Microsoft .NET 10 Desktop Runtime.
echo.
echo Install the Desktop Runtime for Windows from:
echo %RUNTIME_URL%
echo.
echo After installation, run FreeX.cmd again.
echo.
choice /M "Open the .NET 10 download page now"
if errorlevel 2 exit /b 1
start "" "%RUNTIME_URL%"
exit /b 1
"@
    Set-Content -LiteralPath $launcherPath -Value $launcher -Encoding ASCII
}

$readmePath = Join-Path $publishDir "README.txt"
$runCommand = if ($PublishMode -eq "SingleFile") { $launchExeName } else { "FreeX.cmd" }
$runtimeGuidance = if ($PublishMode -eq "SingleFile") {
    @"
This is a framework-dependent single-file Windows build. It is small to share
and should run as a standalone .exe when the Microsoft .NET 10 Desktop Runtime
is installed.

If the runtime is missing, the .NET app host shows a Microsoft runtime prompt
and download link. Install the Desktop Runtime for Windows from:

  $runtimeUrl
"@
} else {
    @"
This is a framework-dependent Windows folder build. It is smaller to share, but
it requires the Microsoft .NET 10 Desktop Runtime. The launcher checks for
Microsoft.WindowsDesktop.App 10.x and offers to open the runtime download page:

  $runtimeUrl

If the runtime is already installed, the launcher starts $launchExeName.
"@
}

$readme = @"
FreeX user test build

Version:
  $Version

Build:
  $artifactName

Run:
  $runCommand

$runtimeGuidance

Local diagnostics:
  FreeX writes local tester diagnostics and crash reports to:
    %LOCALAPPDATA%\FreeX\Diagnostics

  These files stay on the tester's machine unless they choose to attach them
  to an issue report. To disable local diagnostics for a run, start FreeX
  with FREEX_DIAGNOSTICS=0 in the environment.
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
