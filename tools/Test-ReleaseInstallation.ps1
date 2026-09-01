#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('windows','linux','macos')][string]$Platform,
    [Parameter(Mandatory)][ValidateSet('FreeX','FreeW','FreeP')][string[]]$Apps,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Runtime,
    [Parameter(Mandatory)][string]$InputRoot,
    [switch]$Suite
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")
$InputRoot = (Resolve-Path -LiteralPath $InputRoot).Path
if ($Suite -and @($Apps | Sort-Object -Unique).Count -ne 3) { throw 'Suite smoke requires all three apps.' }
if (-not $Suite -and $Apps.Count -ne 1) { throw 'Individual smoke requires one app.' }
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) "free-release-install-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $scratch | Out-Null

function Find-One([string]$Name) {
    (Find-ToolReleaseArtifact -InputRoot $InputRoot -Name $Name).FullName
}
function Run([string]$File, [string[]]$Arguments) {
    $parameters = @{ FilePath = $File; ArgumentList = $Arguments; Wait = $true; PassThru = $true }
    if (Test-ToolIsWindows) { $parameters.WindowStyle = 'Hidden' }
    $process = Start-Process @parameters
    if ($process.ExitCode -ne 0) { throw "'$File' exited with $($process.ExitCode)." }
}
function Assert-OneInstalled([string]$Root, [string]$App) {
    $matches = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter "$App.App.Host.exe" -ErrorAction SilentlyContinue)
    if ($matches.Count -ne 1) { throw "Expected one installed $App executable; found $($matches.Count)." }
    $matches[0].FullName
}
function Launch-Bounded([string]$Executable) {
    $process = Start-Process -FilePath $Executable -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 3
    if ($process.HasExited -and $process.ExitCode -ne 0) { throw "Installed app '$Executable' exited with $($process.ExitCode)." }
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
}

try {
    if ($Platform -eq 'windows') {
        $programRoot = Join-Path $scratch 'Programs'
        $dataRoot = Join-Path $scratch 'UserData'
        New-Item -ItemType Directory -Force -Path $programRoot, $dataRoot | Out-Null
        if ($Suite) {
            # Individual -> suite -> individual exercises shared installer identity and destination.
            $first = $Apps[0]
            Run (Find-One "$first-v$Version-$Runtime-setup.exe") @('--silent','--installto',(Join-Path $programRoot $first))
            Run (Find-One "FreeSuite-v$Version-$Runtime-setup.exe") @('--silent','--installto',$programRoot)
            foreach ($app in $Apps) { Run (Find-One "$app-v$Version-$Runtime-setup.exe") @('--silent','--installto',(Join-Path $programRoot $app)) }
        } else {
            $app = $Apps[0]
            Run (Find-One "$app-v$Version-$Runtime-setup.exe") @('--silent','--installto',(Join-Path $programRoot $app))
        }
        foreach ($app in $Apps) {
            $data = Join-Path $dataRoot $app
            New-Item -ItemType Directory -Force -Path $data | Out-Null
            $marker = Join-Path $data 'release-smoke-user-data.txt'
            'preserve' | Set-Content -LiteralPath $marker -Encoding ascii
            $exe = Assert-OneInstalled $programRoot $app
            Launch-Bounded $exe
            $installRoot = Join-Path $programRoot $app
            $updater = Join-Path $installRoot 'Update.exe'
            if (-not (Test-Path -LiteralPath $updater)) { throw "Expected the $app Velopack updater at $updater." }
            Run $updater @('uninstall','--silent')
            for ($attempt = 0; $attempt -lt 20 -and (Test-Path -LiteralPath $exe); $attempt++) {
                Start-Sleep -Milliseconds 500
            }
            if (Test-Path -LiteralPath $exe) { throw "$app executable remained after uninstall." }
            if (-not (Test-Path -LiteralPath $marker)) { throw "$app uninstall removed user data." }
        }
    } elseif ($Platform -eq 'macos' -and $Suite) {
        $package = Find-One "FreeSuite-v$Version-$Runtime.pkg"
        $expanded = Join-Path $scratch 'expanded-pkg'
        Run (Get-Command pkgutil -ErrorAction Stop).Source @('--expand-full',$package,$expanded)
        foreach ($app in $Apps) {
            $matches = @(Get-ChildItem -LiteralPath $expanded -Recurse -Directory -Filter "$app.app")
            if ($matches.Count -ne 1) { throw "Expected one signed $app.app payload in the suite package; found $($matches.Count)." }
        }
    } else {
        $bash = (Get-Command bash -ErrorAction Stop).Source
        $packageName = if ($Suite) { 'FreeSuite' } else { $Apps[0] }
        $suffix = if ($Platform -eq 'linux') { 'installer' } else { 'apps' }
        $archive = Find-One "$packageName-v$Version-$Runtime-$suffix.zip"
        $expanded = Join-Path $scratch 'expanded'
        $destination = Join-Path $scratch 'installed'
        Expand-Archive -LiteralPath $archive -DestinationPath $expanded
        Run $bash @((Join-Path $expanded 'install.sh'), $destination)
        foreach ($app in $Apps) {
            if ($Platform -eq 'linux') {
                $exe = Join-Path $destination "lib/$($app.ToLowerInvariant())/$app"
                if (-not (Test-Path -LiteralPath $exe)) { throw "Installed Linux executable missing: $exe" }
            } else {
                $exe = Join-Path $destination "$app.app/Contents/MacOS/$app"
                if (-not (Test-Path -LiteralPath $exe)) { throw "Installed macOS executable missing: $exe" }
            }
        }
        if ($Suite) {
            foreach ($app in $Apps) {
                $individualSuffix = if ($Platform -eq 'linux') { 'installer' } else { 'apps' }
                $individual = Find-One "$app-v$Version-$Runtime-$individualSuffix.zip"
                $individualRoot = Join-Path $scratch "individual-$app"
                Expand-Archive -LiteralPath $individual -DestinationPath $individualRoot
                Run $bash @((Join-Path $individualRoot 'install.sh'), $destination)
            }
        }
        Run $bash @((Join-Path $expanded 'uninstall.sh'), $destination)
    }
    Write-Host "Release installation smoke passed for $Platform $Runtime ($($Apps -join ','))."
} finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
