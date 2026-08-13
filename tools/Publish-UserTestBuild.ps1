param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot = "artifacts\releases",
    [string]$Version = "",
    [ValidateSet("SingleFile", "Folder", "Msix", "Velopack")]
    [string]$PublishMode = "SingleFile",
    [string]$MsixCertificatePath = $env:FREEX_MSIX_CERTIFICATE_PATH,
    [string]$MsixCertificatePassword = $env:FREEX_MSIX_CERTIFICATE_PASSWORD,
    [string]$MsixTimestampUrl = $env:FREEX_MSIX_TIMESTAMP_URL,
    [switch]$AllowUnsignedMsix
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Assert-SafeArtifactToken {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]*$') {
        throw "$Label must contain only letters, numbers, dots, and hyphens, and must not contain path separators."
    }
}

function Assert-SafeTimestampUrl {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return
    }

    $timestampUri = $null
    if (-not [System.Uri]::TryCreate($Value, [System.UriKind]::Absolute, [ref]$timestampUri) -or
        $timestampUri.Scheme -notin @("http", "https")) {
        throw "MsixTimestampUrl must be an absolute http or https URL."
    }
}

function Assert-MsixCertificatePath {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return
    }

    if (-not (Test-Path -LiteralPath $Value -PathType Leaf)) {
        throw "MsixCertificatePath must reference an existing certificate file."
    }
}

function Assert-MsixSigningOptions {
    param(
        [string]$CertificatePath,
        [string]$CertificatePassword,
        [string]$TimestampUrl
    )

    if ([string]::IsNullOrWhiteSpace($CertificatePath) -and
        (-not [string]::IsNullOrWhiteSpace($CertificatePassword) -or -not [string]::IsNullOrWhiteSpace($TimestampUrl))) {
        throw "MSIX signing options require MsixCertificatePath."
    }
}

function Assert-MsixPublishSigningMode {
    param(
        [string]$PublishMode,
        [string]$CertificatePath,
        [bool]$AllowUnsigned
    )

    if ($PublishMode -eq "Msix" -and
        [string]::IsNullOrWhiteSpace($CertificatePath) -and
        -not $AllowUnsigned) {
        throw "MSIX packages require MsixCertificatePath; pass -AllowUnsignedMsix only for local packaging validation."
    }
}

function Import-MsixSigningCertificate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CertificatePath,
        [string]$CertificatePassword
    )

    $importArguments = @{
        FilePath = $CertificatePath
        CertStoreLocation = "Cert:\CurrentUser\My"
        Exportable = $false
    }
    if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
        $importArguments.Password = ConvertTo-SecureString -String $CertificatePassword -AsPlainText -Force
    }

    $certificates = @(Import-PfxCertificate @importArguments)
    $signingCertificate = $certificates |
        Where-Object { $_.HasPrivateKey } |
        Select-Object -First 1

    if ($null -eq $signingCertificate) {
        throw "MSIX signing certificate import did not produce a certificate with a private key."
    }

    return $signingCertificate
}

function Remove-MsixSigningCertificate {
    param([object]$Certificate)

    if ($null -eq $Certificate -or [string]::IsNullOrWhiteSpace($Certificate.Thumbprint)) {
        return
    }

    $storePath = Join-Path "Cert:\CurrentUser\My" $Certificate.Thumbprint
    if (Test-Path -LiteralPath $storePath) {
        Remove-Item -LiteralPath $storePath -Force
    }
}

function Get-MsBuildPropertyValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [string]$PropertyName
    )

    $output = @(dotnet msbuild $ProjectPath -nologo "-getProperty:$PropertyName")
    if ($LASTEXITCODE -ne 0) {
        throw "Could not evaluate MSBuild property '$PropertyName' from $ProjectPath."
    }

    $value = $output |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1

    if ($null -eq $value) {
        return ""
    }

    return $value.Trim()
}

function ConvertTo-MsBuildVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DisplayVersion
    )

    $numericParts = [regex]::Matches($DisplayVersion, '\d+') | ForEach-Object { [int64]$_.Value }
    if ($numericParts.Count -eq 0) {
        throw "Assembly version metadata requires a numeric version, but '$DisplayVersion' contains no numeric parts."
    }

    $major = $numericParts[0]
    $minor = if ($numericParts.Count -gt 1) { $numericParts[1] } else { 0 }
    $patch = if ($numericParts.Count -gt 2) { $numericParts[2] } else { 0 }
    return "$major.$minor.$patch"
}

function Get-MsixManifestPublisher {
    param([Parameter(Mandatory = $true)][object]$Certificate)

    $publisher = [string]$Certificate.Subject
    if ([string]::IsNullOrWhiteSpace($publisher)) {
        throw "MSIX signing certificate subject is empty; cannot derive manifest Publisher."
    }

    return $publisher
}

Assert-SafeArtifactToken -Value $RuntimeIdentifier -Label "RuntimeIdentifier"
Assert-SafeTimestampUrl -Value $MsixTimestampUrl
Assert-MsixCertificatePath -Value $MsixCertificatePath
Assert-MsixSigningOptions -CertificatePath $MsixCertificatePath -CertificatePassword $MsixCertificatePassword -TimestampUrl $MsixTimestampUrl
Assert-MsixPublishSigningMode -PublishMode $PublishMode -CertificatePath $MsixCertificatePath -AllowUnsigned ([bool]$AllowUnsignedMsix)

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\FreeX.App.Host\FreeX.App.Host.csproj"
$testerReleaseSmokeProjectPath = Join-Path $repoRoot "tools\FreeX.Validation.Wpf\FreeX.Validation.Wpf.csproj"

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
    $Version = Get-MsBuildPropertyValue -ProjectPath $projectPath -PropertyName "InformationalVersion"
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = Get-MsBuildPropertyValue -ProjectPath $projectPath -PropertyName "Version"
    }

    if ([string]::IsNullOrWhiteSpace($Version)) {
        throw "Could not read app version metadata from $projectPath"
    }
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

$assemblyVersion = ConvertTo-MsBuildVersion -DisplayVersion $Version
$informationalVersion = "$assemblyVersion+$commitId"
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
$artifactExeHashPath = "$artifactExePath.sha256"
$artifactMsixHashPath = "$artifactMsixPath.sha256"
$zipPath = Join-Path $artifactRoot "$artifactName.zip"
$zipHashPath = "$zipPath.sha256"

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
if ($PublishMode -eq "SingleFile" -and (Test-Path -LiteralPath $artifactExePath)) {
    Remove-Item -LiteralPath $artifactExePath -Force
}
if ($PublishMode -eq "SingleFile" -and (Test-Path -LiteralPath $artifactExeHashPath)) {
    Remove-Item -LiteralPath $artifactExeHashPath -Force
}
if ($PublishMode -eq "Msix" -and (Test-Path -LiteralPath $artifactMsixPath)) {
    Remove-Item -LiteralPath $artifactMsixPath -Force
}
if ($PublishMode -eq "Msix" -and (Test-Path -LiteralPath $artifactMsixHashPath)) {
    Remove-Item -LiteralPath $artifactMsixHashPath -Force
}
if ($PublishMode -eq "Folder" -and (Test-Path -LiteralPath $zipPath)) {
    Remove-Item -LiteralPath $zipPath -Force
}
if ($PublishMode -eq "Folder" -and (Test-Path -LiteralPath $zipHashPath)) {
    Remove-Item -LiteralPath $zipHashPath -Force
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

$publishArgs = @(
    "publish", $projectPath,
    "-c", $Configuration,
    "-r", $RuntimeIdentifier,
    "--self-contained", "false",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:Version=$assemblyVersion",
    "-p:InformationalVersion=$informationalVersion",
    "-o", $publishDir
)

if ($PublishMode -eq "SingleFile") {
    $publishArgs += @(
        "-p:PublishSingleFile=true",
        "-p:FreeXTesterReleaseEnglishOnly=true",
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

$smokeReportName = "tester-release-smoke.json"
$smokeReportPath = Join-Path $publishDir $smokeReportName
$smokeToolDir = Join-Path $artifactRoot ".$artifactName-smoke-tool"
if (Test-Path -LiteralPath $smokeToolDir) {
    Remove-Item -LiteralPath $smokeToolDir -Recurse -Force
}
dotnet publish $testerReleaseSmokeProjectPath -c $Configuration -r $RuntimeIdentifier --self-contained false -p:DebugType=None -p:DebugSymbols=false -o $smokeToolDir
if ($LASTEXITCODE -ne 0) {
    throw "Tester-release smoke tool publish failed with exit code $LASTEXITCODE"
}
$smokeToolPath = Join-Path $smokeToolDir "FreeX.Validation.Wpf.exe"
if (-not (Test-Path -LiteralPath $smokeToolPath -PathType Leaf)) {
    throw "Tester-release smoke tool was not published at $smokeToolPath"
}
$smokeProcess = Start-Process `
    -FilePath $smokeToolPath `
    -ArgumentList @("--tester-release-smoke", $smokeReportName) `
    -WorkingDirectory $publishDir `
    -WindowStyle Hidden `
    -Wait `
    -PassThru
if ($smokeProcess.ExitCode -ne 0) {
    throw "Published app tester-release smoke failed with exit code $($smokeProcess.ExitCode)."
}
if (-not (Test-Path -LiteralPath $smokeReportPath -PathType Leaf)) {
    throw "Published app tester-release smoke did not create $smokeReportPath"
}

$smokeReport = Get-Content -LiteralPath $smokeReportPath -Raw | ConvertFrom-Json
if ($smokeReport.Success -ne $true -or $smokeReport.BorderPixelSnapPassed -ne $true) {
    throw "Published app tester-release smoke reported failure: $($smokeReport.Errors -join '; ')"
}
Write-Host "Published app smoke passed: $($smokeReport.ActionableRibbonCommandCount) ribbon commands, $($smokeReport.RibbonHandlerCount) handlers, pixel-snapped borders."
Remove-Item -LiteralPath $smokeReportPath -Force
Remove-Item -LiteralPath $smokeToolDir -Recurse -Force

$runtimeUrl = "https://dotnet.microsoft.com/download/dotnet/10.0"

if ($PublishMode -eq "Velopack") {
    # Velopack packs the published folder as-is (no single-file rename); the apphost stays FreeX.App.Host.exe.
    # Ensure the Velopack CLI is available.
    $vpk = Get-Command vpk -ErrorAction SilentlyContinue
    if ($null -eq $vpk) {
        dotnet tool install -g vpk
        if ($LASTEXITCODE -ne 0) { throw "Failed to install the Velopack CLI (vpk)." }
        $vpk = Get-Command vpk -ErrorAction SilentlyContinue
        if ($null -eq $vpk) { throw "vpk not found on PATH after install; ensure the dotnet global tools dir is on PATH." }
    }

    # Clean the output directory first so local re-runs of the same version do not fail with
    # "there is a release ... equal or greater to the current version". CI starts from an empty
    # workspace, so this only matters for repeated local packs. (Delta packages would require
    # seeding prior releases here; not wired yet — full packages only.)
    $vpkOut = Join-Path $artifactRoot "velopack-$RuntimeIdentifier"
    if (Test-Path -LiteralPath $vpkOut) {
        Remove-Item -LiteralPath $vpkOut -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $vpkOut | Out-Null

    # packId is "FreeXApp" (not "FreeX") on purpose: Velopack installs to %LocalAppData%\<packId>,
    # and the app already uses %LocalAppData%\FreeX for its own data (Logs/Diagnostics/Recovery).
    # A matching id would make Velopack rename/own that data dir — wiping user data on uninstall and
    # failing reinstall when the dir is locked. Distinct id keeps install and data fully separate.
    # packTitle stays "FreeX" so the display name (Start menu, Programs & Features) is unchanged.
    & vpk pack `
        --packId "FreeXApp" `
        --packVersion $assemblyVersion `
        --packDir $publishDir `
        --mainExe "FreeX.App.Host.exe" `
        --outputDir $vpkOut `
        --packTitle "FreeX" `
        --channel "win"
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed with exit code $LASTEXITCODE" }

    Write-Host "Created Velopack artifacts in $vpkOut"
    Get-ChildItem -LiteralPath $vpkOut | ForEach-Object { Write-Host "  $($_.Name)" }
    exit 0
}

if ($PublishMode -eq "SingleFile") {
    Move-Item -LiteralPath $defaultExePath -Destination $artifactExePath
    $hash = Get-FileHash -LiteralPath $artifactExePath -Algorithm SHA256
    Set-Content -LiteralPath $artifactExeHashPath -Value "$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $artifactExePath)" -Encoding ASCII
    Remove-Item -LiteralPath $publishDir -Recurse -Force
    Write-Host "Created $artifactExePath"
    Write-Host "Created $artifactExeHashPath"
    exit 0
}

if (Test-Path -LiteralPath $launchExePath) {
    Remove-Item -LiteralPath $launchExePath -Force
}

Move-Item -LiteralPath $defaultExePath -Destination $launchExePath

if ($PublishMode -eq "Msix") {
    $assetsDir = Join-Path $publishDir "Assets"
    New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null
    $pngBytes = [Convert]::FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=")
    [IO.File]::WriteAllBytes((Join-Path $assetsDir "Square44x44Logo.png"), $pngBytes)
    [IO.File]::WriteAllBytes((Join-Path $assetsDir "Square150x150Logo.png"), $pngBytes)

    $msixVersion = ConvertTo-MsixPackageVersion -DisplayVersion $Version
    $msixExeName = Split-Path -Leaf $launchExePath
    $importedSigningCertificate = $null

    try {
        $msixPublisher = "CN=FreeXLocal"
        if (-not [string]::IsNullOrWhiteSpace($MsixCertificatePath)) {
            $importedSigningCertificate = Import-MsixSigningCertificate -CertificatePath $MsixCertificatePath -CertificatePassword $MsixCertificatePassword
            $msixPublisher = Get-MsixManifestPublisher -Certificate $importedSigningCertificate
        }

        $msixPublisherAttribute = ConvertTo-ToolXmlAttribute -Value $msixPublisher

    $manifestPath = Join-Path $publishDir "AppxManifest.xml"
    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap rescap">
  <Identity Name="FreeX.Tester" Publisher="$msixPublisherAttribute" Version="$msixVersion" />
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

        $signArgs = @("sign", "/fd", "SHA256", "/sha1", $importedSigningCertificate.Thumbprint, "/s", "My")
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
    } finally {
        Remove-MsixSigningCertificate -Certificate $importedSigningCertificate
    }

    $hash = Get-FileHash -LiteralPath $artifactMsixPath -Algorithm SHA256
    Set-Content -LiteralPath $artifactMsixHashPath -Value "$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $artifactMsixPath)" -Encoding ASCII
    Write-Host "Created $artifactMsixPath"
    Write-Host "Created $artifactMsixHashPath"
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

Trademark notice:
  FreeX is not affiliated with, endorsed by, or sponsored by Microsoft.
  Microsoft Excel is a trademark of Microsoft Corporation.

Legal and privacy notices:
  In the app: Help > Legal Notices
  https://github.com/tony-xmelon/FreeX/blob/main/docs/legal/legal-notices.md
  https://github.com/tony-xmelon/FreeX/blob/main/docs/legal/privacy.md
  https://github.com/tony-xmelon/FreeX/blob/main/THIRD_PARTY_NOTICES.md
"@
Set-Content -LiteralPath $readmePath -Value $readme -Encoding ASCII

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

if (-not (Test-Path -LiteralPath $zipPath)) {
    throw "Compress-Archive did not create $zipPath"
}

$hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
Set-Content -LiteralPath $zipHashPath -Value "$($hash.Hash.ToLowerInvariant())  $(Split-Path -Leaf $zipPath)" -Encoding ASCII

Write-Host "Created $publishDir"
Write-Host "Created $zipPath"
Write-Host "Created $zipHashPath"
