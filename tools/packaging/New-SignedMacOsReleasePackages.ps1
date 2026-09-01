#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Creates Developer ID signed and notarized macOS app packages and a Free Suite PKG.

.DESCRIPTION
    This fail-closed release helper consumes the portable macOS ZIP produced for
    each requested app, delegates ordinary .app construction to
    New-AppInstallers.ps1, signs each bundle with the hardened runtime, submits it
    to Apple's notary service, staples and verifies the ticket, and recreates the
    app installer ZIP. When -Suite is specified it also creates a signed,
    notarized, and stapled installer PKG containing all three apps.

    The PKCS#12 supplied in MACOS_CODESIGN_CERTIFICATE_P12 must contain both the
    Developer ID Application identity named by MACOS_DEVELOPER_ID_APPLICATION and
    the corresponding Developer ID Installer identity. The latter is derived by
    replacing the identity's "Developer ID Application:" prefix with
    "Developer ID Installer:". This preserves the existing six-secret contract.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("FreeX", "FreeW", "FreeP")]
    [string[]]$Apps,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidateSet("osx-x64", "osx-arm64")]
    [string]$Runtime,

    [Parameter(Mandatory = $true)]
    [string]$InputRoot,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [switch]$Suite
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path (Split-Path -Parent $PSScriptRoot) "ToolScriptSupport.ps1")

if (-not (Test-ToolIsMacOS)) {
    throw "Signed macOS release packaging must run on macOS."
}

$Apps = @($Apps | Sort-Object -Unique)
if ($Apps.Count -eq 0) {
    throw "At least one app is required."
}
if ($Suite -and ($Apps.Count -ne 3 -or @($Apps | Where-Object { $_ -notin @("FreeX", "FreeW", "FreeP") }).Count -ne 0)) {
    throw "The signed Free Suite PKG requires FreeX, FreeW, and FreeP."
}

$requiredEnvironmentVariables = @(
    "MACOS_CODESIGN_CERTIFICATE_P12",
    "MACOS_CODESIGN_CERTIFICATE_PASSWORD",
    "MACOS_DEVELOPER_ID_APPLICATION",
    "MACOS_NOTARY_APPLE_ID",
    "MACOS_NOTARY_TEAM_ID",
    "MACOS_NOTARY_PASSWORD"
)
foreach ($variableName in $requiredEnvironmentVariables) {
    $value = [Environment]::GetEnvironmentVariable($variableName)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Signed macOS release packaging requires environment variable '$variableName'."
    }
}

$applicationIdentity = $env:MACOS_DEVELOPER_ID_APPLICATION.Trim()
if ($applicationIdentity -notmatch '^Developer ID Application:\s*(?<subject>.+)$') {
    throw "MACOS_DEVELOPER_ID_APPLICATION must be an exact 'Developer ID Application: ...' identity."
}
$installerIdentity = "Developer ID Installer: $($Matches.subject)"

foreach ($commandName in @("security", "codesign", "ditto", "xcrun", "pkgbuild", "pkgutil", "spctl")) {
    if (-not (Get-Command $commandName -ErrorAction SilentlyContinue)) {
        throw "Required macOS release tool '$commandName' was not found."
    }
}

$InputRoot = (Resolve-Path -LiteralPath $InputRoot).Path
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$OutputDir = (Resolve-Path -LiteralPath $OutputDir).Path
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$installerScript = Join-Path $PSScriptRoot "New-AppInstallers.ps1"
$workRoot = Join-Path $OutputDir ".signed-macos-work-$Runtime"
$certificatePath = Join-Path $workRoot "developer-id-identities.p12"
$keychainPath = Join-Path $workRoot "release-signing.keychain-db"
$keychainPassword = [Guid]::NewGuid().ToString("N")
$originalKeychains = @()

function Invoke-CheckedNativeCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage Exit code: $LASTEXITCODE."
    }
}

function Write-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    $name = Split-Path -Leaf $Path
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name" | Set-Content -LiteralPath "$Path.sha256" -NoNewline -Encoding ascii
}

function Submit-Notarization {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    $notaryOutput = @(
        & xcrun notarytool submit $Path `
            --apple-id $env:MACOS_NOTARY_APPLE_ID `
            --team-id $env:MACOS_NOTARY_TEAM_ID `
            --password $env:MACOS_NOTARY_PASSWORD `
            --wait `
            --output-format json 2>&1
    )
    $notaryExitCode = $LASTEXITCODE
    $notaryText = $notaryOutput -join "`n"
    $notaryText | Set-Content -LiteralPath $LogPath -Encoding utf8NoBOM
    if ($notaryExitCode -ne 0) {
        throw "Apple notarization failed for '$(Split-Path -Leaf $Path)' with exit code $notaryExitCode. See '$LogPath'."
    }

    try {
        $notaryResult = $notaryText | ConvertFrom-Json
    } catch {
        throw "Apple notarization did not return valid JSON for '$(Split-Path -Leaf $Path)'. See '$LogPath'."
    }
    if ([string]$notaryResult.status -cne "Accepted") {
        throw "Apple notarization was not accepted for '$(Split-Path -Leaf $Path)'; status '$($notaryResult.status)'. See '$LogPath'."
    }
}

function Test-GatekeeperApp {
    param([Parameter(Mandatory = $true)][string]$AppPath)

    Invoke-CheckedNativeCommand -Command "spctl" -Arguments @(
        "--assess", "--type", "execute", "--verbose=4", $AppPath
    ) -FailureMessage "Gatekeeper rejected '$AppPath'."
}

function New-SignedAppPackage {
    param([Parameter(Mandatory = $true)][string]$App)

    # Resolve early so a partial signed release cannot start with missing input.
    $portableName = "$App-v$Version-$Runtime.zip"
    [void](Find-ToolReleaseArtifact -InputRoot $InputRoot -Name $portableName)

    & $installerScript `
        -Apps $App `
        -Platform macos `
        -Version $Version `
        -Runtime $Runtime `
        -InputRoot $InputRoot `
        -OutputDir $OutputDir
    if ($LASTEXITCODE -ne 0) {
        throw "Unsigned app bundle construction failed for $App with exit code $LASTEXITCODE."
    }

    $packagePath = Join-Path $OutputDir "$App-v$Version-$Runtime-apps.zip"
    $stagePath = Join-Path $workRoot "$App-installer"
    if (Test-Path -LiteralPath $stagePath) {
        Remove-Item -LiteralPath $stagePath -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $stagePath | Out-Null
    Invoke-CheckedNativeCommand -Command "ditto" -Arguments @(
        "-x", "-k", $packagePath, $stagePath
    ) -FailureMessage "Could not expand the $App macOS installer package."

    $appPath = Join-Path $stagePath "$App.app"
    if (-not (Test-Path -LiteralPath $appPath -PathType Container)) {
        throw "The generated $App package does not contain '$App.app'."
    }

    Invoke-CheckedNativeCommand -Command "codesign" -Arguments @(
        "--force", "--deep", "--options", "runtime", "--timestamp",
        "--keychain", $keychainPath, "--sign", $applicationIdentity, $appPath
    ) -FailureMessage "Developer ID signing failed for $App."
    Invoke-CheckedNativeCommand -Command "codesign" -Arguments @(
        "--verify", "--deep", "--strict", "--verbose=2", $appPath
    ) -FailureMessage "Strict code-signature verification failed for $App."

    $submissionPath = Join-Path $workRoot "$App-v$Version-$Runtime-notary.zip"
    if (Test-Path -LiteralPath $submissionPath) {
        Remove-Item -LiteralPath $submissionPath -Force
    }
    Invoke-CheckedNativeCommand -Command "ditto" -Arguments @(
        "-c", "-k", "--sequesterRsrc", "--keepParent", $appPath, $submissionPath
    ) -FailureMessage "Could not prepare the $App notarization submission."

    $notaryLogPath = Join-Path $OutputDir "$App-v$Version-$Runtime-notarization.json"
    Submit-Notarization -Path $submissionPath -LogPath $notaryLogPath
    Invoke-CheckedNativeCommand -Command "xcrun" -Arguments @(
        "stapler", "staple", $appPath
    ) -FailureMessage "Could not staple the notarization ticket to $App."
    Invoke-CheckedNativeCommand -Command "xcrun" -Arguments @(
        "stapler", "validate", $appPath
    ) -FailureMessage "Stapler validation failed for $App."
    Invoke-CheckedNativeCommand -Command "codesign" -Arguments @(
        "--verify", "--deep", "--strict", "--verbose=2", $appPath
    ) -FailureMessage "Post-notarization signature verification failed for $App."
    Test-GatekeeperApp -AppPath $appPath

    @(
        "# $App macOS bundle",
        "",
        "The included $App.app is Developer ID signed, notarized, and stapled.",
        "Run ``./install.sh`` to copy it to ``~/Applications``, or drag the app there manually."
    ) | Set-Content -LiteralPath (Join-Path $stagePath "README.md") -Encoding utf8NoBOM

    Remove-Item -LiteralPath $packagePath -Force
    Remove-Item -LiteralPath "$packagePath.sha256" -Force -ErrorAction SilentlyContinue
    Invoke-CheckedNativeCommand -Command "ditto" -Arguments @(
        "-c", "-k", "--sequesterRsrc", $stagePath, $packagePath
    ) -FailureMessage "Could not create the signed $App macOS installer package."
    Write-Sha256 -Path $packagePath

    return [pscustomobject]@{
        App = $App
        AppPath = $appPath
        PackagePath = $packagePath
        NotarizationLogPath = $notaryLogPath
    }
}

function New-SignedSuitePackage {
    param([Parameter(Mandatory = $true)][object[]]$SignedApps)

    $packageRoot = Join-Path $workRoot "suite-pkg-root"
    $applicationsPath = Join-Path $packageRoot "Applications"
    if (Test-Path -LiteralPath $packageRoot) {
        Remove-Item -LiteralPath $packageRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $applicationsPath | Out-Null
    foreach ($signedApp in $SignedApps) {
        Copy-Item -LiteralPath $signedApp.AppPath -Destination $applicationsPath -Recurse -Force
    }

    $packagePath = Join-Path $OutputDir "FreeSuite-v$Version-$Runtime.pkg"
    Remove-Item -LiteralPath $packagePath -Force -ErrorAction SilentlyContinue
    Invoke-CheckedNativeCommand -Command "pkgbuild" -Arguments @(
        "--root", $packageRoot,
        "--identifier", "io.github.tony-xmelon.freesuite",
        "--version", $Version,
        "--install-location", "/",
        "--sign", $installerIdentity,
        "--keychain", $keychainPath,
        $packagePath
    ) -FailureMessage "Could not create a Developer ID signed Free Suite PKG. Ensure the P12 contains '$installerIdentity'."
    Invoke-CheckedNativeCommand -Command "pkgutil" -Arguments @(
        "--check-signature", $packagePath
    ) -FailureMessage "Free Suite PKG signature verification failed."

    $notaryLogPath = Join-Path $OutputDir "FreeSuite-v$Version-$Runtime-notarization.json"
    Submit-Notarization -Path $packagePath -LogPath $notaryLogPath
    Invoke-CheckedNativeCommand -Command "xcrun" -Arguments @(
        "stapler", "staple", $packagePath
    ) -FailureMessage "Could not staple the notarization ticket to the Free Suite PKG."
    Invoke-CheckedNativeCommand -Command "xcrun" -Arguments @(
        "stapler", "validate", $packagePath
    ) -FailureMessage "Stapler validation failed for the Free Suite PKG."
    Invoke-CheckedNativeCommand -Command "pkgutil" -Arguments @(
        "--check-signature", $packagePath
    ) -FailureMessage "Post-notarization Free Suite PKG signature verification failed."
    Invoke-CheckedNativeCommand -Command "spctl" -Arguments @(
        "--assess", "--type", "install", "--verbose=4", $packagePath
    ) -FailureMessage "Gatekeeper rejected the Free Suite PKG."
    Write-Sha256 -Path $packagePath
}

try {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $workRoot | Out-Null

    try {
        $certificateBytes = [Convert]::FromBase64String($env:MACOS_CODESIGN_CERTIFICATE_P12.Trim())
    } catch {
        throw "MACOS_CODESIGN_CERTIFICATE_P12 is not valid base64 PKCS#12 content."
    }
    [IO.File]::WriteAllBytes($certificatePath, $certificateBytes)

    $originalKeychains = @(& security list-keychains -d user | ForEach-Object { $_.Trim().Trim('"') } | Where-Object { $_ })
    Invoke-CheckedNativeCommand -Command "security" -Arguments @(
        "create-keychain", "-p", $keychainPassword, $keychainPath
    ) -FailureMessage "Could not create the temporary signing keychain."
    Invoke-CheckedNativeCommand -Command "security" -Arguments @(
        "set-keychain-settings", "-lut", "21600", $keychainPath
    ) -FailureMessage "Could not configure the temporary signing keychain."
    Invoke-CheckedNativeCommand -Command "security" -Arguments @(
        "unlock-keychain", "-p", $keychainPassword, $keychainPath
    ) -FailureMessage "Could not unlock the temporary signing keychain."
    Invoke-CheckedNativeCommand -Command "security" -Arguments @(
        "import", $certificatePath,
        "-P", $env:MACOS_CODESIGN_CERTIFICATE_PASSWORD,
        "-A", "-t", "cert", "-f", "pkcs12", "-k", $keychainPath
    ) -FailureMessage "Could not import the Developer ID identities."
    Invoke-CheckedNativeCommand -Command "security" -Arguments @(
        "set-key-partition-list", "-S", "apple-tool:,apple:,codesign:",
        "-s", "-k", $keychainPassword, $keychainPath
    ) -FailureMessage "Could not authorize non-interactive Developer ID signing."
    Invoke-CheckedNativeCommand -Command "security" -Arguments @(
        "list-keychains", "-d", "user", "-s", $keychainPath
    ) -FailureMessage "Could not activate the temporary signing keychain."

    $identityOutput = @(& security find-identity -v $keychainPath 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0 -or $identityOutput.IndexOf($applicationIdentity, [StringComparison]::Ordinal) -lt 0) {
        throw "The P12 does not contain the required application identity '$applicationIdentity'."
    }
    if ($Suite -and $identityOutput.IndexOf($installerIdentity, [StringComparison]::Ordinal) -lt 0) {
        throw "The P12 does not contain the required installer identity '$installerIdentity'."
    }

    $signedApps = @($Apps | ForEach-Object { New-SignedAppPackage -App $_ })
    if ($Suite) {
        New-SignedSuitePackage -SignedApps $signedApps
    }
} finally {
    if ($originalKeychains.Count -gt 0 -and (Get-Command security -ErrorAction SilentlyContinue)) {
        & security list-keychains -d user -s @originalKeychains | Out-Null
    }
    if (Test-Path -LiteralPath $keychainPath) {
        & security delete-keychain $keychainPath | Out-Null
    }
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
}

Write-Host "Produced signed, notarized macOS release packages for $($Apps -join ', ') ($Runtime)."
