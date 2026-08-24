#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Apps,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^win-(x64|arm64)$')]
    [string]$Runtime,

    [Parameter(Mandatory = $true)]
    [string]$InputRoot,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [switch]$Suite,

    [string]$MsixCertificatePath,

    [string]$MsixCertificatePassword,

    [string]$MsixTimestampUrl,

    [switch]$AllowUnsignedMsix
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Apps = @($Apps | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Apps.Count -eq 0 -or @($Apps | Where-Object { $_ -notin @("FreeX", "FreeW", "FreeP") }).Count -gt 0) {
    throw "Apps must contain only FreeX, FreeW, or FreeP."
}
if ($Suite -and @($Apps | Sort-Object -Unique).Count -ne 3) {
    throw "Suite MSIX requires FreeX, FreeW, and FreeP."
}
if (-not $Suite -and $Apps.Count -ne 1) {
    throw "An individual MSIX requires exactly one app."
}
if ([string]::IsNullOrWhiteSpace($MsixCertificatePath) -and -not $AllowUnsignedMsix) {
    throw "MSIX packages require MsixCertificatePath; pass -AllowUnsignedMsix only for local or internal tester packaging."
}
if (-not [string]::IsNullOrWhiteSpace($MsixTimestampUrl)) {
    $timestampUri = $null
    if (-not [Uri]::TryCreate($MsixTimestampUrl, [UriKind]::Absolute, [ref]$timestampUri) -or
        $timestampUri.Scheme -notin @("http", "https")) {
        throw "MsixTimestampUrl must be an absolute http or https URL."
    }
}
if (-not [string]::IsNullOrWhiteSpace($MsixCertificatePath) -and
    -not (Test-Path -LiteralPath $MsixCertificatePath -PathType Leaf)) {
    throw "MsixCertificatePath must reference an existing certificate file."
}
if ([string]::IsNullOrWhiteSpace($MsixCertificatePath) -and
    (-not [string]::IsNullOrWhiteSpace($MsixCertificatePassword) -or -not [string]::IsNullOrWhiteSpace($MsixTimestampUrl))) {
    throw "MSIX signing options require MsixCertificatePath."
}

$InputRoot = (Resolve-Path -LiteralPath $InputRoot).Path
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$OutputDir = (Resolve-Path -LiteralPath $OutputDir).Path
$packageName = if ($Suite) { "FreeSuite" } else { $Apps[0] }
$prefix = "$packageName-v$Version-$Runtime"
$packagePath = Join-Path $OutputDir "$prefix.msix"
$checksumPath = "$packagePath.sha256"
$workRoot = Join-Path $OutputDir ".msix-work-$packageName-$Runtime"
if (Test-Path -LiteralPath $workRoot) {
    Remove-Item -LiteralPath $workRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path (Join-Path $workRoot "Assets") | Out-Null

function Find-One([string]$Name) {
    $matches = @(Get-ChildItem -LiteralPath $InputRoot -Recurse -File -Filter $Name)
    if ($matches.Count -ne 1) { throw "Expected exactly one '$Name' below '$InputRoot'; found $($matches.Count)." }
    $matches[0].FullName
}

function ConvertTo-MsixVersion([string]$DisplayVersion) {
    $numericParts = @([regex]::Matches($DisplayVersion, '\d+') | ForEach-Object { [int64]$_.Value })
    if ($numericParts.Count -eq 0) { throw "MSIX packaging requires a numeric version." }
    $parts = @(0L, 0L, 0L, 0L)
    for ($i = 0; $i -lt [Math]::Min(4, $numericParts.Count); $i++) { $parts[$i] = $numericParts[$i] }
    for ($i = 3; $i -gt 0; $i--) {
        if ($parts[$i] -gt 65535) {
            $parts[$i - 1] += [Math]::Floor($parts[$i] / 65536)
            $parts[$i] = $parts[$i] % 65536
        }
    }
    if ($parts[0] -gt 65535) { throw "MSIX version is outside the supported range." }
    ($parts | ForEach-Object { [string]$_ }) -join "."
}

function Import-SigningCertificate {
    if ([string]::IsNullOrWhiteSpace($MsixCertificatePath)) { return $null }
    $arguments = @{
        FilePath = $MsixCertificatePath
        CertStoreLocation = "Cert:\CurrentUser\My"
        Exportable = $false
    }
    if (-not [string]::IsNullOrWhiteSpace($MsixCertificatePassword)) {
        $arguments.Password = ConvertTo-SecureString -String $MsixCertificatePassword -AsPlainText -Force
    }
    $certificates = @(Import-PfxCertificate @arguments)
    $certificate = $certificates | Where-Object HasPrivateKey | Select-Object -First 1
    if ($null -eq $certificate) { throw "MSIX signing certificate import did not produce a private-key certificate." }
    $certificate
}

function Remove-SigningCertificate($Certificate) {
    if ($null -eq $Certificate -or [string]::IsNullOrWhiteSpace($Certificate.Thumbprint)) { return }
    $storePath = Join-Path "Cert:\CurrentUser\My" $Certificate.Thumbprint
    if (Test-Path -LiteralPath $storePath) { Remove-Item -LiteralPath $storePath -Force }
}

function Find-WindowsKitTool([string]$Name) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }
    $kitRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path -LiteralPath $kitRoot) {
        return Get-ChildItem -LiteralPath $kitRoot -Recurse -Filter $Name |
            Sort-Object FullName -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
    return $null
}

foreach ($app in $Apps) {
    $source = Find-One "$app-v$Version-$Runtime.exe"
    Copy-Item -LiteralPath $source -Destination (Join-Path $workRoot "$app.exe")
}

$logoBytes = [Convert]::FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=")
[IO.File]::WriteAllBytes((Join-Path $workRoot "Assets\Square44x44Logo.png"), $logoBytes)
[IO.File]::WriteAllBytes((Join-Path $workRoot "Assets\Square150x150Logo.png"), $logoBytes)

$importedCertificate = $null
try {
    $publisher = "CN=FreeLocal"
    if (-not [string]::IsNullOrWhiteSpace($MsixCertificatePath)) {
        $importedCertificate = Import-SigningCertificate
        $publisher = [string]$importedCertificate.Subject
        if ([string]::IsNullOrWhiteSpace($publisher)) { throw "MSIX signing certificate subject is empty." }
    }
    $xmlPublisher = [System.Security.SecurityElement]::Escape($publisher)
    $msixVersion = ConvertTo-MsixVersion $Version
    $identityName = "$packageName.Tester"
    $displayName = if ($Suite) { "Free Suite" } else { $packageName }
    $applications = ($Apps | ForEach-Object {
        $applicationId = $_
        @"
    <Application Id="$applicationId" Executable="$applicationId.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="$applicationId" Description="$applicationId tester build" BackgroundColor="transparent" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png" />
    </Application>
"@
    }) -join "`n"
    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities" IgnorableNamespaces="uap rescap">
  <Identity Name="$identityName" Publisher="$xmlPublisher" Version="$msixVersion" />
  <Properties>
    <DisplayName>$displayName</DisplayName>
    <PublisherDisplayName>FreeX contributors</PublisherDisplayName>
    <Logo>Assets\Square150x150Logo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Resources><Resource Language="en-us" /></Resources>
  <Applications>
$applications
  </Applications>
  <Capabilities><rescap:Capability Name="runFullTrust" /></Capabilities>
</Package>
"@
    Set-Content -LiteralPath (Join-Path $workRoot "AppxManifest.xml") -Value $manifest -Encoding UTF8

    $makeAppx = Find-WindowsKitTool "makeappx.exe"
    if ($null -eq $makeAppx) { throw "makeappx.exe was not found. Install the Windows SDK to create MSIX packages." }
    if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath -Force }
    & $makeAppx pack /d $workRoot /p $packagePath /o
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $packagePath)) { throw "makeappx failed to create $packagePath." }

    if ($null -ne $importedCertificate) {
        $signtool = Find-WindowsKitTool "signtool.exe"
        if ($null -eq $signtool) { throw "signtool.exe was not found. Install the Windows SDK to sign MSIX packages." }
        $signArguments = @("sign", "/fd", "SHA256", "/sha1", $importedCertificate.Thumbprint, "/s", "My")
        if (-not [string]::IsNullOrWhiteSpace($MsixTimestampUrl)) { $signArguments += @("/tr", $MsixTimestampUrl, "/td", "SHA256") }
        $signArguments += $packagePath
        & $signtool @signArguments
        if ($LASTEXITCODE -ne 0) { throw "signtool failed to sign $packagePath." }
    }

    $hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $packagePath)" | Set-Content -LiteralPath $checksumPath -NoNewline -Encoding ascii
    Write-Host "Created $packagePath"
    Write-Host "Created $checksumPath"
} finally {
    Remove-SigningCertificate $importedCertificate
    if (Test-Path -LiteralPath $workRoot) { Remove-Item -LiteralPath $workRoot -Recurse -Force }
}
