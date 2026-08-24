#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('windows','linux','macos')][string]$Platform,
    [Parameter(Mandatory)][string[]]$Apps,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Runtime,
    [Parameter(Mandatory)][string]$InputRoot,
    [switch]$Suite
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$Apps = @($Apps | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Apps.Count -eq 0 -or @($Apps | Where-Object { $_ -notin @('FreeX','FreeW','FreeP') }).Count -gt 0) { throw 'Apps must contain only FreeX, FreeW, or FreeP.' }
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
function Find-MakeAppx {
    $command = Get-Command makeappx.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }
    $kitRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    if (Test-Path -LiteralPath $kitRoot) {
        return Get-ChildItem -LiteralPath $kitRoot -Recurse -Filter makeappx.exe |
            Sort-Object FullName -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
    throw 'makeappx.exe was not found; Windows MSIX smoke requires the Windows SDK.'
}

try {
    if ($Platform -eq 'windows') {
        $packageName = if ($Suite) { 'FreeSuite' } else { $Apps[0] }
        $package = Find-One "$packageName-v$Version-$Runtime.msix"
        $expanded = Join-Path $scratch 'msix-expanded'
        Run (Find-MakeAppx) @('unpack','/p',$package,'/d',$expanded,'/o')
        $manifestPath = Join-Path $expanded 'AppxManifest.xml'
        if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'MSIX package did not contain AppxManifest.xml.' }
        [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
        foreach ($app in $Apps) {
            $executable = Join-Path $expanded "$app.exe"
            if (-not (Test-Path -LiteralPath $executable)) { throw "MSIX package did not contain $app.exe." }
            $application = @($manifest.Package.Applications.Application | Where-Object { $_.Id -eq $app })
            if ($application.Count -ne 1 -or $application[0].Executable -ne "$app.exe") {
                throw "MSIX manifest did not declare the expected $app application entry."
            }
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
