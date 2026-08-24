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
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$InputRoot = (Resolve-Path -LiteralPath $InputRoot).Path
if ($Suite -and @($Apps | Sort-Object -Unique).Count -ne 3) { throw 'Suite smoke requires all three apps.' }
if (-not $Suite -and $Apps.Count -ne 1) { throw 'Individual smoke requires one app.' }
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) "free-release-install-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $scratch | Out-Null

function Find-One([string]$Name) {
    $matches = @(Get-ChildItem -LiteralPath $InputRoot -Recurse -File -Filter $Name)
    if ($matches.Count -ne 1) { throw "Expected exactly one '$Name'; found $($matches.Count)." }
    $matches[0].FullName
}
function Run([string]$File, [string[]]$Arguments) {
    $parameters = @{ FilePath = $File; ArgumentList = $Arguments; Wait = $true; PassThru = $true }
    if ($IsWindows) { $parameters.WindowStyle = 'Hidden' }
    $process = Start-Process @parameters
    if ($process.ExitCode -ne 0) { throw "'$File' exited with $($process.ExitCode)." }
}
function Assert-OneInstalled([string]$Root, [string]$App) {
    $matches = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter "$App.exe" -ErrorAction SilentlyContinue)
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
        $savedLocal = $env:LOCALAPPDATA
        $savedRoaming = $env:APPDATA
        $env:LOCALAPPDATA = Join-Path $scratch 'LocalAppData'
        $env:APPDATA = Join-Path $scratch 'RoamingAppData'
        New-Item -ItemType Directory -Force -Path $env:LOCALAPPDATA, $env:APPDATA | Out-Null
        try {
            if ($Suite) {
                # Individual -> suite -> individual exercises shared installer identity and destination.
                $first = $Apps[0]
                Run (Find-One "$first-v$Version-$Runtime-setup.exe") @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
                Run (Find-One "FreeSuite-v$Version-$Runtime-setup.exe") @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
                foreach ($app in $Apps) { Run (Find-One "$app-v$Version-$Runtime-setup.exe") @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') }
            } else {
                Run (Find-One "$($Apps[0])-v$Version-$Runtime-setup.exe") @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
            }
            $programRoot = Join-Path $env:LOCALAPPDATA 'Programs'
            foreach ($app in $Apps) {
                $data = Join-Path $env:LOCALAPPDATA $app
                New-Item -ItemType Directory -Force -Path $data | Out-Null
                $marker = Join-Path $data 'release-smoke-user-data.txt'
                'preserve' | Set-Content -LiteralPath $marker -Encoding ascii
                $exe = Assert-OneInstalled $programRoot $app
                Launch-Bounded $exe
                $uninstaller = @(Get-ChildItem -LiteralPath (Split-Path -Parent $exe) -File -Filter 'unins*.exe')
                if ($uninstaller.Count -ne 1) { throw "Expected one $app uninstaller; found $($uninstaller.Count)." }
                Run $uninstaller[0].FullName @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
                if (Test-Path -LiteralPath $exe) { throw "$app executable remained after uninstall." }
                if (-not (Test-Path -LiteralPath $marker)) { throw "$app uninstall removed user data." }
            }
        } finally { $env:LOCALAPPDATA = $savedLocal; $env:APPDATA = $savedRoaming }
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
                $exe = Join-Path $destination "lib\$($app.ToLowerInvariant())\$app"
                if (-not (Test-Path -LiteralPath $exe)) { throw "Installed Linux executable missing: $exe" }
            } else {
                $exe = Join-Path $destination "$app.app\Contents\MacOS\$app"
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
