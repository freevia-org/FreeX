#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds unsigned installable packages without replacing the portable release artifacts.

.DESCRIPTION
    Windows packages are per-user Inno Setup executables. Linux packages contain a
    deterministic install/uninstall script around the existing portable zip. macOS
    packages contain ordinary .app bundles plus a helper that copies them to
    ~/Applications. The macOS bundles are intentionally unsigned and unnotarized.

    Pass one app for an individual installer, or all three apps with -Suite for the
    Free Suite package. InputRoot may contain nested GitHub artifact directories.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("FreeX", "FreeW", "FreeP")]
    [string[]]$Apps,

    [Parameter(Mandatory = $true)]
    [ValidateSet("windows", "linux", "macos")]
    [string]$Platform,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^(win|linux|osx)-[A-Za-z0-9]+$')]
    [string]$Runtime,

    [Parameter(Mandatory = $true)]
    [string]$InputRoot,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [switch]$Suite,

    [string]$InnoCompilerPath,

    [switch]$GenerateOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Suite -and (@($Apps | Sort-Object -Unique).Count -ne 3)) {
    throw "The suite installer requires FreeX, FreeW, and FreeP."
}
if (-not $Suite -and $Apps.Count -ne 1) {
    throw "An individual installer requires exactly one app."
}
if (($Platform -eq "windows" -and $Runtime -notlike "win-*") -or
    ($Platform -eq "linux" -and $Runtime -notlike "linux-*") -or
    ($Platform -eq "macos" -and $Runtime -notlike "osx-*")) {
    throw "Runtime '$Runtime' does not match platform '$Platform'."
}

$InputRoot = (Resolve-Path -LiteralPath $InputRoot).Path
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$OutputDir = (Resolve-Path -LiteralPath $OutputDir).Path
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$workRoot = Join-Path $OutputDir ".installer-work"
New-Item -ItemType Directory -Force -Path $workRoot | Out-Null

function Find-UniqueInput {
    param([string]$Pattern)
    $matches = @(Get-ChildItem -LiteralPath $InputRoot -Recurse -File -Filter $Pattern)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one '$Pattern' below '$InputRoot'; found $($matches.Count)."
    }
    return $matches[0].FullName
}

function Write-Sha256 {
    param([string]$Path)
    $name = Split-Path -Leaf $Path
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name" | Set-Content -LiteralPath "$Path.sha256" -NoNewline -Encoding ascii
}

function Get-InnoCompiler {
    if ($InnoCompilerPath) {
        return (Resolve-Path -LiteralPath $InnoCompilerPath).Path
    }
    if ($env:INNO_SETUP_COMPILER -and (Test-Path -LiteralPath $env:INNO_SETUP_COMPILER)) {
        return (Resolve-Path -LiteralPath $env:INNO_SETUP_COMPILER).Path
    }
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    foreach ($candidate in @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    throw "Inno Setup 6 compiler not found. Set INNO_SETUP_COMPILER or pass -InnoCompilerPath."
}

function Escape-InnoValue([string]$Value) {
    return $Value.Replace('"', '""')
}

function New-WindowsInstaller {
    $packageName = if ($Suite) { "FreeSuite" } else { $Apps[0] }
    $displayName = if ($Suite) { "Free Suite" } else { $Apps[0] }
    $outputBase = "$packageName-v$Version-$Runtime-setup"
    $scriptPath = Join-Path $workRoot "$outputBase.iss"
    $appIds = @{
        FreeX = '7766603C-625B-4CB8-90E8-E0D9A7B7C2B1'
        FreeW = '139C8F07-C618-4922-AB53-ED766018A70A'
        FreeP = '8BD58832-E0DF-49B2-862A-3235D947EA83'
        FreeSuite = 'A9D9734A-4103-48E2-A110-8DA3583046B8'
    }
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('[Setup]')
    $lines.Add("AppId={{$($appIds[$packageName])}")
    $lines.Add("AppName=$displayName")
    $lines.Add("AppVersion=$Version")
    $lines.Add("AppPublisher=FreeX contributors")
    if ($Suite) {
        # The suite is deliberately a non-owning bootstrapper. Each child setup
        # retains the same AppId, destination, upgrade and uninstall identity as
        # installing that app separately.
        $lines.Add('CreateAppDir=no')
        $lines.Add('Uninstallable=no')
    } else {
        $lines.Add("DefaultDirName={localappdata}\Programs\$displayName")
        $lines.Add("DefaultGroupName=$displayName")
        $lines.Add('DisableProgramGroupPage=yes')
    }
    $lines.Add('PrivilegesRequired=lowest')
    $lines.Add('ArchitecturesAllowed=x64compatible')
    $lines.Add('ArchitecturesInstallIn64BitMode=x64compatible')
    $lines.Add('Compression=lzma2/ultra64')
    $lines.Add('SolidCompression=yes')
    $lines.Add('CloseApplications=yes')
    $lines.Add('RestartApplications=no')
    $lines.Add("OutputDir=$(Escape-InnoValue $OutputDir)")
    $lines.Add("OutputBaseFilename=$outputBase")
    $lines.Add("SetupIconFile=$(Escape-InnoValue (Join-Path $repoRoot "shared\Free.Shared.Shell\Resources\$($Apps[0]).ico"))")
    if (-not $Suite) { $lines.Add("UninstallDisplayIcon={app}\$($Apps[0]).exe") }
    $lines.Add('WizardStyle=modern')
    $lines.Add('')
    $lines.Add('[Files]')
    foreach ($app in $Apps) {
        if ($Suite) {
            $childName = "$app-v$Version-$Runtime-setup.exe"
            $source = Find-UniqueInput $childName
            $lines.Add("Source: `"$(Escape-InnoValue $source)`"; DestDir: `"{tmp}`"; DestName: `"$childName`"; Flags: deleteafterinstall")
        } else {
            $source = Find-UniqueInput "$app-v$Version-$Runtime.exe"
            $lines.Add("Source: `"$(Escape-InnoValue $source)`"; DestDir: `"{app}`"; DestName: `"$app.exe`"; Flags: ignoreversion")
        }
    }
    if (-not $Suite) {
        $lines.Add('')
        $lines.Add('[Icons]')
        $app = $Apps[0]
        $lines.Add("Name: `"{autoprograms}\$app`"; Filename: `"{app}\$app.exe`"")
    }
    $lines.Add('')
    $lines.Add('[Run]')
    if ($Suite) {
        foreach ($app in $Apps) {
            $childName = "$app-v$Version-$Runtime-setup.exe"
            $lines.Add("Filename: `"{tmp}\$childName`"; Parameters: `"/SILENT /CURRENTUSER /NORESTART`"; StatusMsg: `"Installing $app...`"; Flags: waituntilterminated")
        }
    } else {
        $app = $Apps[0]
        $lines.Add("Filename: `"{app}\$app.exe`"; Description: `"Launch $app`"; Flags: nowait postinstall skipifsilent")
    }
    $lines | Set-Content -LiteralPath $scriptPath -Encoding utf8

    if ($GenerateOnly) { return $scriptPath }
    $compiler = Get-InnoCompiler
    & $compiler $scriptPath
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }
    $result = Join-Path $OutputDir "$outputBase.exe"
    if (-not (Test-Path -LiteralPath $result)) { throw "Installer output missing: $result" }
    Write-Sha256 $result
    return $result
}

function Write-UnixScript {
    param([string]$Path, [string[]]$Lines)
    ($Lines -join "`n") + "`n" | Set-Content -LiteralPath $Path -NoNewline -Encoding utf8NoBOM
    if (-not $IsWindows) { & chmod +x $Path }
}

function New-LinuxInstaller {
    $packageName = if ($Suite) { "FreeSuite" } else { $Apps[0] }
    $stage = Join-Path $workRoot "$packageName-$Runtime"
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path (Join-Path $stage 'payload') | Out-Null
    foreach ($app in $Apps) {
        $inputName = if ($Suite) { "$app-v$Version-$Runtime-installer.zip" } else { "$app-v$Version-$Runtime.zip" }
        $zip = Find-UniqueInput $inputName
        Copy-Item -LiteralPath $zip -Destination (Join-Path $stage "payload\$app.zip")
    }
    if ($Suite) {
        $common = @('#!/usr/bin/env bash','set -euo pipefail','here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"','prefix="${1:-$HOME/.local}"','temp_root="$(mktemp -d)"','trap ''rm -rf "$temp_root"'' EXIT')
        $install = @($common)
        $uninstall = @($common)
        foreach ($app in $Apps) {
            $install += "mkdir -p `"`$temp_root/$app`""
            $install += "unzip -q `"`$here/payload/$app.zip`" -d `"`$temp_root/$app`""
            $install += "`"`$temp_root/$app/install.sh`" `"`$prefix`""
            $uninstall += "mkdir -p `"`$temp_root/$app`""
            $uninstall += "unzip -q `"`$here/payload/$app.zip`" -d `"`$temp_root/$app`""
            $uninstall += "`"`$temp_root/$app/uninstall.sh`" `"`$prefix`""
        }
    } else {
        $install = @('#!/usr/bin/env bash','set -euo pipefail','here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"','prefix="${1:-$HOME/.local}"','mkdir -p "$prefix/lib" "$prefix/bin" "$prefix/share/applications" "$prefix/share/icons/hicolor/scalable/apps"')
        $uninstall = @('#!/usr/bin/env bash','set -euo pipefail','prefix="${1:-$HOME/.local}"')
        $app = $Apps[0]
        $lower = $app.ToLowerInvariant()
        $appId = "io.github.tony-xmelon.$lower"
        Copy-Item -LiteralPath (Join-Path $repoRoot "shared\Free.Shared.Shell\Resources\$app.svg") -Destination (Join-Path $stage "payload\$app.svg")
        $install += "rm -rf `"`$prefix/lib/$lower`""
        $install += "mkdir -p `"`$prefix/lib/$lower`""
        $install += "unzip -q `"`$here/payload/$app.zip`" -d `"`$prefix/lib/$lower`""
        $install += "chmod +x `"`$prefix/lib/$lower/$app`""
        $install += "ln -sfn `"`$prefix/lib/$lower/$app`" `"`$prefix/bin/$lower`""
        $install += "cp `"`$here/payload/$app.svg`" `"`$prefix/share/icons/hicolor/scalable/apps/$appId.svg`""
        $install += "cat > `"`$prefix/share/applications/$appId.desktop`" <<EOF"
        $install += '[Desktop Entry]'
        $install += 'Type=Application'
        $install += "Name=$app"
        $install += "Exec=`"`$prefix/bin/$lower`" %F"
        $install += "Icon=$appId"
        $install += 'Terminal=false'
        $install += 'Categories=Office;'
        $install += 'EOF'
        $install += 'update-desktop-database "$prefix/share/applications" >/dev/null 2>&1 || true'
        $uninstall += "rm -rf `"`$prefix/lib/$lower`""
        $uninstall += "rm -f `"`$prefix/bin/$lower`""
        $uninstall += "rm -f `"`$prefix/share/applications/$appId.desktop`""
        $uninstall += "rm -f `"`$prefix/share/icons/hicolor/scalable/apps/$appId.svg`""
        $uninstall += 'update-desktop-database "$prefix/share/applications" >/dev/null 2>&1 || true'
    }
    $install += 'echo "Installed successfully. Ensure $prefix/bin is on PATH."'
    $uninstall += 'echo "Removed successfully."'
    Write-UnixScript (Join-Path $stage 'install.sh') $install
    Write-UnixScript (Join-Path $stage 'uninstall.sh') $uninstall
    @("# $packageName installer", "", "Run ``./install.sh`` for a per-user install under ``~/.local``.", "Pass another prefix as the first argument if required.") |
        Set-Content -LiteralPath (Join-Path $stage 'README.md') -Encoding utf8
    $result = Join-Path $OutputDir "$packageName-v$Version-$Runtime-installer.zip"
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $result -Force
    Write-Sha256 $result
    return $result
}

function New-MacInstaller {
    $packageName = if ($Suite) { "FreeSuite" } else { $Apps[0] }
    $stage = Join-Path $workRoot "$packageName-$Runtime"
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    if ($Suite) {
        New-Item -ItemType Directory -Force -Path (Join-Path $stage 'payload') | Out-Null
        foreach ($app in $Apps) {
            $child = Find-UniqueInput "$app-v$Version-$Runtime-apps.zip"
            Copy-Item -LiteralPath $child -Destination (Join-Path $stage "payload\$app.zip")
        }
        $common = @('#!/usr/bin/env bash','set -euo pipefail','here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"','destination="${1:-$HOME/Applications}"','temp_root="$(mktemp -d)"','trap ''rm -rf "$temp_root"'' EXIT')
        $installLines = @($common)
        $uninstallLines = @($common)
        foreach ($app in $Apps) {
            $installLines += "mkdir -p `"`$temp_root/$app`""
            $installLines += "unzip -q `"`$here/payload/$app.zip`" -d `"`$temp_root/$app`""
            $installLines += "`"`$temp_root/$app/install.sh`" `"`$destination`""
            $uninstallLines += "mkdir -p `"`$temp_root/$app`""
            $uninstallLines += "unzip -q `"`$here/payload/$app.zip`" -d `"`$temp_root/$app`""
            $uninstallLines += "`"`$temp_root/$app/uninstall.sh`" `"`$destination`""
        }
        Write-UnixScript (Join-Path $stage 'install.sh') $installLines
        Write-UnixScript (Join-Path $stage 'uninstall.sh') $uninstallLines
        @("# Free Suite macOS bootstrapper", "", "This delegates to the same individual app bundles and installation destinations.", "All included app bundles are currently unsigned and unnotarized.") |
            Set-Content -LiteralPath (Join-Path $stage 'README.md') -Encoding utf8
        $suiteResult = Join-Path $OutputDir "$packageName-v$Version-$Runtime-apps.zip"
        if ($IsMacOS -and (Get-Command ditto -ErrorAction SilentlyContinue)) {
            & ditto -c -k --sequesterRsrc $stage $suiteResult
            if ($LASTEXITCODE -ne 0) { throw "ditto failed with exit code $LASTEXITCODE." }
        } else {
            Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $suiteResult -Force
        }
        Write-Sha256 $suiteResult
        return $suiteResult
    }
    foreach ($app in $Apps) {
        $portable = Find-UniqueInput "$app-v$Version-$Runtime.zip"
        $bundle = Join-Path $stage "$app.app"
        $macos = Join-Path $bundle 'Contents\MacOS'
        $resources = Join-Path $bundle 'Contents\Resources'
        New-Item -ItemType Directory -Force -Path $macos, $resources | Out-Null
        Expand-Archive -LiteralPath $portable -DestinationPath $macos -Force
        Copy-Item -LiteralPath (Join-Path $repoRoot "shared\Free.Shared.Shell\Resources\$app.icns") -Destination (Join-Path $resources "$app.icns")
        $bundleId = "io.github.tony-xmelon.$($app.ToLowerInvariant())"
        $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>$app</string>
<key>CFBundleIdentifier</key><string>$bundleId</string>
<key>CFBundleName</key><string>$app</string>
<key>CFBundleDisplayName</key><string>$app</string>
<key>CFBundleIconFile</key><string>$app.icns</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>$Version</string>
<key>CFBundleVersion</key><string>$Version</string>
<key>LSMinimumSystemVersion</key><string>12.0</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
"@
        $plist.TrimStart() | Set-Content -LiteralPath (Join-Path $bundle 'Contents\Info.plist') -Encoding utf8NoBOM
        if (-not $IsWindows) { & chmod +x (Join-Path $macos $app) }
    }
    $installLines = @('#!/usr/bin/env bash','set -euo pipefail','here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"','destination="${1:-$HOME/Applications}"','mkdir -p "$destination"')
    $uninstallLines = @('#!/usr/bin/env bash','set -euo pipefail','destination="${1:-$HOME/Applications}"')
    foreach ($app in $Apps) {
        $installLines += "rm -rf `"`$destination/$app.app`""
        $installLines += "cp -R `"`$here/$app.app`" `"`$destination/$app.app`""
        $uninstallLines += "rm -rf `"`$destination/$app.app`""
    }
    $installLines += 'echo "Installed successfully to $destination."'
    $uninstallLines += 'echo "Removed successfully from $destination."'
    Write-UnixScript (Join-Path $stage 'install.sh') $installLines
    Write-UnixScript (Join-Path $stage 'uninstall.sh') $uninstallLines
    @("# $packageName macOS bundle", "", "These apps are currently unsigned and unnotarized.", "Run ``./install.sh`` to copy them to ``~/Applications``, or drag each app there manually.") |
        Set-Content -LiteralPath (Join-Path $stage 'README.md') -Encoding utf8
    $result = Join-Path $OutputDir "$packageName-v$Version-$Runtime-apps.zip"
    if ($IsMacOS -and (Get-Command ditto -ErrorAction SilentlyContinue)) {
        & ditto -c -k --sequesterRsrc $stage $result
        if ($LASTEXITCODE -ne 0) { throw "ditto failed with exit code $LASTEXITCODE." }
    } else {
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $result -Force
    }
    Write-Sha256 $result
    return $result
}

$result = switch ($Platform) {
    windows { New-WindowsInstaller }
    linux { New-LinuxInstaller }
    macos { New-MacInstaller }
}
Write-Host "Produced $result"
