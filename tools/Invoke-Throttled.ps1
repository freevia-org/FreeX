#requires -Version 7.0
<#
.SYNOPSIS
Runs a heavy command (build, test, preflight) in the foreground once a machine-wide slot is free.

.DESCRIPTION
  pwsh -NoProfile -File tools/Invoke-Throttled.ps1 -Kind Build [-MaxWaitMinutes 360] -- dotnet build FreeX.slnx --configuration Release -p:UseSharedCompilation=false

Waits for a slot (periodic "waiting" messages on stderr), runs the command with stdout/stderr passed
through unchanged, exits with the command's exact exit code, and always releases the slot. A killed or
crashed holder frees its slot automatically (named mutexes are released by the OS when the owner dies).
Never kills any process and never changes the command's arguments. See tools/ThrottleSupport.ps1 for the
pools, slot counts (FREEX_THROTTLE_BUILD_SLOTS / FREEX_THROTTLE_TEST_SLOTS, default 2 each) and bypasses
(FREEX_THROTTLE_DISABLE=1, GITHUB_ACTIONS=true, nested invocation).

  -Kind             Build | Test | Preflight (Build and Preflight share the heavy pool).
  -MaxWaitMinutes   Wait budget (default 360). When it is spent the command runs WITHOUT a slot after a
                    warning: an escape hatch, not a failure.

This script deliberately has no param() block. PowerShell's parameter binder rewrites the wrapped
command's own switches (it splits "-m:1" into "-m" "1", treats "-v:q" as -Verbose, and rejects "--"
under -File), which would silently change build flags. Instead the wrapper reads the process's raw
command line when it is the -File entry point, so everything after "--" reaches the command verbatim.
#>

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'ThrottleSupport.ps1')

# Raw argv is lossless; $args is not (see above). Fall back to $args when called in-process (& script ...).
$tokens = @($args)
$rawArgv = [Environment]::GetCommandLineArgs()
for ($i = 0; $i -lt $rawArgv.Count; $i++) {
    $candidate = $rawArgv[$i]
    if ([string]::IsNullOrEmpty($candidate) -or $candidate.StartsWith('-')) { continue }
    $fullCandidate = $null
    try { $fullCandidate = [System.IO.Path]::GetFullPath($candidate) } catch { continue }
    if ([string]::Equals($fullCandidate, $PSCommandPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        $tokens = @($rawArgv | Select-Object -Skip ($i + 1))
        break
    }
}

$kind = $null
$maxWaitMinutes = 360.0
$commandLine = @()
for ($i = 0; $i -lt $tokens.Count; $i++) {
    $token = [string]$tokens[$i]
    if ($token -eq '--') {
        $commandLine = @($tokens | Select-Object -Skip ($i + 1))
        break
    }
    if ($token -match '^-(Kind|MaxWaitMinutes)(?::(.*))?$') {
        $name = $Matches[1]
        $value = $Matches[2]
        if ([string]::IsNullOrEmpty($value)) {
            $i++
            if ($i -ge $tokens.Count) {
                [Console]::Error.WriteLine("Invoke-Throttled.ps1: -$name needs a value.")
                exit 2
            }
            $value = [string]$tokens[$i]
        }
        if ($name -eq 'Kind') { $kind = $value }
        elseif (-not [double]::TryParse($value, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$maxWaitMinutes)) {
            [Console]::Error.WriteLine("Invoke-Throttled.ps1: -MaxWaitMinutes must be a number, got '$value'.")
            exit 2
        }
        continue
    }
    if ($token.StartsWith('-')) {
        [Console]::Error.WriteLine("Invoke-Throttled.ps1: unknown option '$token' before '--'. Usage: Invoke-Throttled.ps1 -Kind Build|Test|Preflight [-MaxWaitMinutes N] -- <command ...>")
        exit 2
    }
    $commandLine = @($tokens | Select-Object -Skip $i)
    break
}

if ($kind -notin @('Build', 'Test', 'Preflight')) {
    [Console]::Error.WriteLine("Invoke-Throttled.ps1: -Kind must be Build, Test or Preflight (got '$kind').")
    exit 2
}
if ($commandLine.Count -eq 0) {
    [Console]::Error.WriteLine('Invoke-Throttled.ps1: no command given. Usage: Invoke-Throttled.ps1 -Kind Build|Test|Preflight [-MaxWaitMinutes N] -- <command ...>')
    exit 2
}

$resolved = Get-Command -Name $commandLine[0] -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $resolved) {
    [Console]::Error.WriteLine("Invoke-Throttled.ps1: command not found: $($commandLine[0])")
    exit 127
}
$arguments = @($commandLine | Select-Object -Skip 1)
$description = ($commandLine -join ' ')
if ($description.Length -gt 120) { $description = $description.Substring(0, 120) + '...' }

$exitCode = 0
$slot = $null
try {
    $slot = Enter-FreeXThrottle -Kind $kind -MaxWaitMinutes $maxWaitMinutes -Description $description

    $global:LASTEXITCODE = $null
    try {
        & $resolved @arguments
        if ($null -ne $global:LASTEXITCODE) { $exitCode = [int]$global:LASTEXITCODE }
        elseif (-not $?) { $exitCode = 1 }
    }
    catch {
        [Console]::Error.WriteLine($_.Exception.Message)
        $exitCode = 1
    }
}
finally {
    Exit-FreeXThrottle -Slot $slot
}
exit $exitCode
