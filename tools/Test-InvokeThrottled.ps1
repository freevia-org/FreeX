param()

$ErrorActionPreference = "Stop"

# Contract self-test for tools/Invoke-Throttled.ps1 + tools/ThrottleSupport.ps1 (the cross-session
# build/test throttle). Pester-free; runs real child processes against a PRIVATE slot namespace
# (FREEX_THROTTLE_NAMESPACE) so it never contends with the machine's real pool. It only ever kills
# processes it started itself, by PID.

$toolRoot = $PSScriptRoot
$throttle = Join-Path $toolRoot "Invoke-Throttled.ps1"
$pwshPath = [Environment]::ProcessPath
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("freex-throttle-selftest-" + [Guid]::NewGuid().ToString("N"))
[void](New-Item -ItemType Directory -Path $work -Force)

$managed = @('FREEX_THROTTLE_NAMESPACE', 'FREEX_THROTTLE_BUILD_SLOTS', 'FREEX_THROTTLE_TEST_SLOTS',
    'FREEX_THROTTLE_DISABLE', 'FREEX_THROTTLE_HELD', 'GITHUB_ACTIONS')
$saved = @{}
foreach ($name in $managed) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
$started = New-Object System.Collections.Generic.List[System.Diagnostics.Process]

function Set-ThrottleEnv {
    param([string]$Namespace, [int]$Slots, [string]$Disable = '', [string]$GitHubActions = '')
    [Environment]::SetEnvironmentVariable('FREEX_THROTTLE_NAMESPACE', $Namespace)
    [Environment]::SetEnvironmentVariable('FREEX_THROTTLE_BUILD_SLOTS', [string]$Slots)
    [Environment]::SetEnvironmentVariable('FREEX_THROTTLE_TEST_SLOTS', [string]$Slots)
    [Environment]::SetEnvironmentVariable('FREEX_THROTTLE_DISABLE', $Disable)
    [Environment]::SetEnvironmentVariable('FREEX_THROTTLE_HELD', '')
    [Environment]::SetEnvironmentVariable('GITHUB_ACTIONS', $GitHubActions)
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "Invoke-Throttled contract violated: $Message" }
}

# Runs the wrapper to completion; returns exit code and merged output.
function Invoke-Wrapper {
    param([string[]]$WrapperArguments)
    $output = & $pwshPath -NoProfile -File $throttle @WrapperArguments 2>&1 | Out-String
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

# Starts a wrapper that HOLDS a slot until $ReleaseFile appears (or 120 s). Returns the wrapper process;
# the held command writes its own PID to $Marker once it runs (i.e. once the slot is held).
function Start-Holder {
    param([string]$Name, [string]$Kind = 'Build')
    $marker = Join-Path $work "$Name.marker"
    $release = Join-Path $work "$Name.release"
    $script = Join-Path $work "$Name.ps1"
    Set-Content -LiteralPath $script -Value @"
Set-Content -LiteralPath '$marker' -Value `$PID
`$limit = [DateTime]::UtcNow.AddSeconds(120)
while (-not (Test-Path -LiteralPath '$release') -and [DateTime]::UtcNow -lt `$limit) { Start-Sleep -Milliseconds 200 }
"@
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($pwshPath)
    foreach ($argument in @('-NoProfile', '-File', $throttle, '-Kind', $Kind, '--', $pwshPath, '-NoProfile', '-File', $script)) {
        $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $started.Add($process)
    $limit = [DateTime]::UtcNow.AddSeconds(60)
    while (-not (Test-Path -LiteralPath $marker) -and [DateTime]::UtcNow -lt $limit) {
        if ($process.HasExited) { throw "Holder '$Name' exited early with code $($process.ExitCode)." }
        Start-Sleep -Milliseconds 200
    }
    Assert-True (Test-Path -LiteralPath $marker) "holder '$Name' never acquired its slot"
    return [pscustomobject]@{
        Process = $process; Release = $release; Marker = $marker
        ChildPid = [int](Get-Content -LiteralPath $marker -TotalCount 1)
    }
}

function Stop-Holder {
    param($Holder)
    Set-Content -LiteralPath $Holder.Release -Value 'go'
    [void]$Holder.Process.WaitForExit(30000)
}

try {
    # 1. Exit code and output pass-through (including a code that is not 0/1) with the real pool settings.
    Set-ThrottleEnv -Namespace "selftest-$([Guid]::NewGuid().ToString('N'))" -Slots 2
    $result = Invoke-Wrapper @('-Kind', 'Build', '--', $pwshPath, '-NoProfile', '-Command', "Write-Output 'throttle-marker -m:1 -v:q'; exit 7")
    Assert-True ($result.ExitCode -eq 7) "exit code 7 was not passed through (got $($result.ExitCode))."
    Assert-True ($result.Output.Contains('throttle-marker -m:1 -v:q')) "command output or its switches were altered: $($result.Output)"
    $result = Invoke-Wrapper @('-Kind', 'Test', '--', $pwshPath, '-NoProfile', '-Command', 'exit 0')
    Assert-True ($result.ExitCode -eq 0) "exit code 0 was not passed through (got $($result.ExitCode))."
    $result = Invoke-Wrapper @('-Kind', 'Build', '--', 'freex-no-such-command-xyz')
    Assert-True ($result.ExitCode -eq 127) "a missing command should exit 127 (got $($result.ExitCode))."

    # 2. A slot is released when the command fails: with ONE slot, a failing run must not block the next.
    Set-ThrottleEnv -Namespace "selftest-$([Guid]::NewGuid().ToString('N'))" -Slots 1
    $result = Invoke-Wrapper @('-Kind', 'Build', '--', $pwshPath, '-NoProfile', '-Command', 'exit 9')
    Assert-True ($result.ExitCode -eq 9) "failing command exit code lost (got $($result.ExitCode))."
    $result = Invoke-Wrapper @('-Kind', 'Build', '-MaxWaitMinutes', '0.25', '--', $pwshPath, '-NoProfile', '-Command', 'exit 0')
    Assert-True ($result.ExitCode -eq 0 -and -not $result.Output.Contains('gave up waiting')) "slot leaked after a failing command: $($result.Output)"

    # 3. Slot limit: with two slots held a third acquirer WAITS (and, with a tiny budget, escapes with a warning).
    Set-ThrottleEnv -Namespace "selftest-$([Guid]::NewGuid().ToString('N'))" -Slots 2
    $holderA = Start-Holder -Name 'limitA'
    $holderB = Start-Holder -Name 'limitB'
    $result = Invoke-Wrapper @('-Kind', 'Build', '-MaxWaitMinutes', '0.04', '--', $pwshPath, '-NoProfile', '-Command', 'exit 0')
    Assert-True ($result.Output.Contains('waiting for a heavy slot') -and $result.Output.Contains('gave up waiting')) "third acquirer did not wait for a slot: $($result.Output)"
    # The test pool is independent of the heavy pool.
    $result = Invoke-Wrapper @('-Kind', 'Test', '-MaxWaitMinutes', '0.25', '--', $pwshPath, '-NoProfile', '-Command', 'exit 0')
    Assert-True ($result.ExitCode -eq 0 -and -not $result.Output.Contains('gave up waiting')) "Test pool was blocked by the heavy pool: $($result.Output)"
    # Nested invocations never take a second slot (no self-deadlock with every slot held by ancestors).
    Stop-Holder $holderB
    $result = Invoke-Wrapper @('-Kind', 'Build', '-MaxWaitMinutes', '0.25', '--', $pwshPath, '-NoProfile', '-File', $throttle, '-Kind', 'Build', '-MaxWaitMinutes', '0.04', '--', $pwshPath, '-NoProfile', '-Command', 'exit 0')
    Assert-True ($result.ExitCode -eq 0 -and -not $result.Output.Contains('gave up waiting')) "nested invocation took a second slot: $($result.Output)"
    # Once a holder releases, the next acquirer proceeds without waiting out its budget.
    Stop-Holder $holderA
    $result = Invoke-Wrapper @('-Kind', 'Build', '-MaxWaitMinutes', '0.25', '--', $pwshPath, '-NoProfile', '-Command', 'exit 0')
    Assert-True ($result.ExitCode -eq 0 -and -not $result.Output.Contains('gave up waiting')) "slot was not released by a finished holder: $($result.Output)"

    # 4. Killed holder: kill the wrapper process (and its command) BY PID; its slot must free without a clean release.
    Set-ThrottleEnv -Namespace "selftest-$([Guid]::NewGuid().ToString('N'))" -Slots 1
    $holder = Start-Holder -Name 'killed'
    $result = Invoke-Wrapper @('-Kind', 'Build', '-MaxWaitMinutes', '0.04', '--', $pwshPath, '-NoProfile', '-Command', 'exit 0')
    Assert-True ($result.Output.Contains('gave up waiting')) "the single slot was not actually held: $($result.Output)"
    Stop-Process -Id $holder.Process.Id -Force
    Stop-Process -Id $holder.ChildPid -Force -ErrorAction SilentlyContinue
    [void]$holder.Process.WaitForExit(30000)
    $result = Invoke-Wrapper @('-Kind', 'Build', '-MaxWaitMinutes', '0.5', '--', $pwshPath, '-NoProfile', '-Command', 'exit 0')
    Assert-True ($result.ExitCode -eq 0 -and -not $result.Output.Contains('gave up waiting')) "a killed holder deadlocked its slot: $($result.Output)"

    # 5. Bypasses: FREEX_THROTTLE_DISABLE=1 and GITHUB_ACTIONS=true run immediately even with every slot held.
    Set-ThrottleEnv -Namespace "selftest-$([Guid]::NewGuid().ToString('N'))" -Slots 1
    $holder = Start-Holder -Name 'bypass'
    Set-ThrottleEnv -Namespace ([Environment]::GetEnvironmentVariable('FREEX_THROTTLE_NAMESPACE')) -Slots 1 -Disable '1'
    $result = Invoke-Wrapper @('-Kind', 'Build', '-MaxWaitMinutes', '0.04', '--', $pwshPath, '-NoProfile', '-Command', 'exit 5')
    Assert-True ($result.ExitCode -eq 5 -and -not $result.Output.Contains('waiting for') -and -not $result.Output.Contains('gave up')) "FREEX_THROTTLE_DISABLE=1 did not bypass: $($result.Output)"
    Set-ThrottleEnv -Namespace ([Environment]::GetEnvironmentVariable('FREEX_THROTTLE_NAMESPACE')) -Slots 1 -GitHubActions 'true'
    $result = Invoke-Wrapper @('-Kind', 'Build', '-MaxWaitMinutes', '0.04', '--', $pwshPath, '-NoProfile', '-Command', 'exit 6')
    Assert-True ($result.ExitCode -eq 6 -and -not $result.Output.Contains('waiting for') -and -not $result.Output.Contains('gave up')) "GITHUB_ACTIONS=true did not bypass: $($result.Output)"
    Stop-Holder $holder

    Write-Host "Invoke-Throttled contract passed (exit codes, release on failure, slot limit, killed holder, bypasses)."
}
finally {
    # Let any still-running held command end on its own (its release file) before removing the work dir.
    foreach ($marker in @(Get-ChildItem -LiteralPath $work -Filter "*.marker" -File -ErrorAction SilentlyContinue)) {
        Set-Content -LiteralPath ([System.IO.Path]::ChangeExtension($marker.FullName, "release")) -Value 'go' -ErrorAction SilentlyContinue
    }
    foreach ($process in $started) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    }
    foreach ($name in $managed) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
