# Cross-session throttle for heavy verification work (Release builds, test runs, repository preflight).
#
# Many agent sessions share one machine, and per-session parallelism cannot bound the AGGREGATE load:
# a build that takes minutes on a quiet box takes tens of minutes when a dozen sessions build at once,
# and MSB4166 / OutOfMemory crashes follow. This file implements a machine-wide slot pool so heavy
# commands queue instead of thrash. It is dot-sourced by tools/Invoke-Throttled.ps1 and by the repo's
# own heavy entry points (tools/Test-RepositoryPreflight.ps1, tools/Test-BranchForIntegration.ps1).
#
# Mechanism: one NAMED MUTEX PER SLOT (Global\<namespace>-<pool>-<n>). A mutex is released by the OS when
# its owner process dies, so a killed or crashed holder can never deadlock the slot: the next waiter gets
# AbandonedMutexException, which counts as acquired. A counting semaphore would leak a permit instead.
#
# Pools: Build and Preflight share the "heavy" pool (FREEX_THROTTLE_BUILD_SLOTS, default 2); Test uses the
# "test" pool (FREEX_THROTTLE_TEST_SLOTS, default 2).
#
# Bypasses (no slot taken, command runs immediately):
#   FREEX_THROTTLE_DISABLE=1   explicit opt-out
#   GITHUB_ACTIONS=true        hosted CI runners are not shared
#   FREEX_THROTTLE_HELD set    re-entrancy: a descendant of a throttled command never takes a second slot,
#                              so parallel preflight children and nested wrappers cannot deadlock
# FREEX_THROTTLE_NAMESPACE (default FreeXThrottle) isolates a private pool; the self-test uses it.
# Nothing here kills a process or changes what the wrapped command does.

function Get-FreeXThrottleSetting {
    param([Parameter(Mandatory = $true)][string]$Name, [string]$Default = '')
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) { return $Default }
    return $value.Trim()
}

function Test-FreeXThrottleBypassed {
    if ((Get-FreeXThrottleSetting 'FREEX_THROTTLE_DISABLE') -eq '1') { return $true }
    if ((Get-FreeXThrottleSetting 'GITHUB_ACTIONS') -eq 'true') { return $true }
    if (-not [string]::IsNullOrWhiteSpace((Get-FreeXThrottleSetting 'FREEX_THROTTLE_HELD'))) { return $true }
    return $false
}

function Get-FreeXThrottlePool {
    param([Parameter(Mandatory = $true)][ValidateSet('Build', 'Test', 'Preflight')][string]$Kind)

    if ($Kind -eq 'Test') {
        $name = 'test'; $settingName = 'FREEX_THROTTLE_TEST_SLOTS'
    }
    else {
        $name = 'heavy'; $settingName = 'FREEX_THROTTLE_BUILD_SLOTS'
    }

    $slots = 2
    $parsed = 0
    if ([int]::TryParse((Get-FreeXThrottleSetting $settingName), [ref]$parsed) -and $parsed -ge 1 -and $parsed -le 64) {
        $slots = $parsed
    }
    return [pscustomobject]@{ Name = $name; Slots = $slots }
}

function Get-FreeXThrottleStateDirectory {
    param([Parameter(Mandatory = $true)][string]$Namespace)
    $path = Join-Path ([System.IO.Path]::GetTempPath()) "freex-throttle-$Namespace"
    if (-not (Test-Path -LiteralPath $path)) {
        [void](New-Item -ItemType Directory -Path $path -Force)
    }
    return $path
}

function New-FreeXThrottleMutex {
    param([Parameter(Mandatory = $true)][string]$Name)
    try {
        return [System.Threading.Mutex]::new($false, "Global\$Name")
    }
    catch [System.UnauthorizedAccessException] {
        # Another account created the Global object; fall back to this logon session.
        return [System.Threading.Mutex]::new($false, "Local\$Name")
    }
}

function Test-FreeXProcessAlive {
    param([int]$ProcessId)
    return $null -ne (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)
}

# Live waiters for a pool, oldest first. A waiter file is live when its owner process exists and has
# touched it recently, so a crashed waiter cannot block the queue.
function Get-FreeXThrottleWaiters {
    param([string]$StateDirectory, [string]$Pool)
    $now = [DateTime]::UtcNow
    $waiters = @()
    foreach ($file in @(Get-ChildItem -LiteralPath $StateDirectory -Filter "waiter-$Pool-*.txt" -File -ErrorAction SilentlyContinue)) {
        $parts = [System.IO.Path]::GetFileNameWithoutExtension($file.Name).Split('-')
        $waiterPid = 0
        if (-not [int]::TryParse($parts[-1], [ref]$waiterPid)) { continue }
        if (($now - $file.LastWriteTimeUtc).TotalSeconds -gt 30 -or -not (Test-FreeXProcessAlive $waiterPid)) {
            Remove-Item -LiteralPath $file.FullName -Force -ErrorAction SilentlyContinue
            continue
        }
        $waiters += [pscustomobject]@{ Pid = $waiterPid; Ticket = [long]($parts[-2]); Path = $file.FullName }
    }
    return @($waiters | Sort-Object Ticket, Pid)
}

# Acquires a slot, blocking (FIFO among live waiters) until one is free or the wait budget is spent.
# Returns $null when the throttle is bypassed or the wait timed out (the caller then runs unthrottled
# after a warning - the timeout is an escape hatch, not a failure). Otherwise returns a slot object for
# Exit-FreeXThrottle. Acquire and release MUST happen on the same thread.
function Enter-FreeXThrottle {
    param(
        [Parameter(Mandatory = $true)][ValidateSet('Build', 'Test', 'Preflight')][string]$Kind,
        [double]$MaxWaitMinutes = 360,
        [string]$Description = ''
    )

    if (Test-FreeXThrottleBypassed) { return $null }

    $namespace = Get-FreeXThrottleSetting 'FREEX_THROTTLE_NAMESPACE' 'FreeXThrottle'
    $pool = Get-FreeXThrottlePool -Kind $Kind
    $stateDirectory = Get-FreeXThrottleStateDirectory -Namespace $namespace
    $mutexes = @()
    for ($i = 0; $i -lt $pool.Slots; $i++) {
        $mutexes += , (New-FreeXThrottleMutex -Name "$namespace-$($pool.Name)-$i")
    }

    $ticket = [DateTime]::UtcNow.Ticks
    $waiterPath = Join-Path $stateDirectory "waiter-$($pool.Name)-$ticket-$PID.txt"
    $startedWaiting = [DateTime]::UtcNow
    $deadline = $startedWaiting.AddMinutes($MaxWaitMinutes)
    $lastMessage = [DateTime]::MinValue
    $acquired = $null
    $acquiredIndex = -1

    try {
        Set-Content -LiteralPath $waiterPath -Value "$PID $Description" -ErrorAction SilentlyContinue
        while ($true) {
            $waiters = @(Get-FreeXThrottleWaiters -StateDirectory $stateDirectory -Pool $pool.Name)
            $position = 1
            for ($w = 0; $w -lt $waiters.Count; $w++) {
                if ($waiters[$w].Pid -eq $PID -and $waiters[$w].Ticket -eq $ticket) { $position = $w + 1; break }
            }

            # Only the head of the queue competes, which keeps the order FIFO and prevents a late
            # arrival from overtaking a session that has been waiting for an hour.
            if ($position -eq 1) {
                for ($i = 0; $i -lt $mutexes.Count; $i++) {
                    $got = $false
                    try { $got = $mutexes[$i].WaitOne(0) }
                    catch [System.Threading.AbandonedMutexException] { $got = $true }  # holder died: we own it
                    if ($got) { $acquired = $mutexes[$i]; $acquiredIndex = $i; break }
                }
                if ($null -ne $acquired) { break }
            }

            $now = [DateTime]::UtcNow
            if ($now -ge $deadline) {
                [Console]::Error.WriteLine("freex-throttle: gave up waiting for a $($pool.Name) slot after $([math]::Round(($now - $startedWaiting).TotalMinutes, 1)) minute(s); running WITHOUT a slot (raise -MaxWaitMinutes or set FREEX_THROTTLE_DISABLE=1).")
                return $null
            }
            if (($now - $lastMessage).TotalSeconds -ge 30) {
                $holders = @(Get-ChildItem -LiteralPath $stateDirectory -Filter "holder-$($pool.Name)-*.txt" -File -ErrorAction SilentlyContinue |
                    ForEach-Object { (Get-Content -LiteralPath $_.FullName -TotalCount 1 -ErrorAction SilentlyContinue) } |
                    Where-Object { $_ })
                $holderText = if ($holders.Count -gt 0) { ' Holders: ' + ($holders -join ' | ') } else { '' }
                [Console]::Error.WriteLine("freex-throttle: waiting for a $($pool.Name) slot (queue position $position of $($waiters.Count); $($pool.Slots) slot(s) in use; waited $([math]::Round(($now - $startedWaiting).TotalSeconds))s).$holderText")
                $lastMessage = $now
            }
            (Get-Item -LiteralPath $waiterPath -ErrorAction SilentlyContinue).LastWriteTimeUtc = [DateTime]::UtcNow
            Start-Sleep -Milliseconds 1000
        }
    }
    finally {
        Remove-Item -LiteralPath $waiterPath -Force -ErrorAction SilentlyContinue
        # Dispose the slots we did not take (the acquired one is released by Exit-FreeXThrottle).
        for ($i = 0; $i -lt $mutexes.Count; $i++) {
            if ($i -ne $acquiredIndex) { $mutexes[$i].Dispose() }
        }
    }

    $holderPath = Join-Path $stateDirectory "holder-$($pool.Name)-$acquiredIndex.txt"
    Set-Content -LiteralPath $holderPath -Value "pid $PID $Kind since $([DateTime]::UtcNow.ToString('HH:mm:ss'))Z $Description" -ErrorAction SilentlyContinue
    $previousHeld = [Environment]::GetEnvironmentVariable('FREEX_THROTTLE_HELD')
    [Environment]::SetEnvironmentVariable('FREEX_THROTTLE_HELD', $Kind)
    $waitedSeconds = ([DateTime]::UtcNow - $startedWaiting).TotalSeconds
    if ($waitedSeconds -ge 2) {
        [Console]::Error.WriteLine("freex-throttle: acquired $($pool.Name) slot $acquiredIndex after $([math]::Round($waitedSeconds))s.")
    }

    return [pscustomobject]@{
        Mutex        = $acquired
        HolderPath   = $holderPath
        PreviousHeld = $previousHeld
    }
}

# Releases a slot returned by Enter-FreeXThrottle. Safe to call with $null (bypassed / timed out).
function Exit-FreeXThrottle {
    param($Slot)
    if ($null -eq $Slot) { return }
    [Environment]::SetEnvironmentVariable('FREEX_THROTTLE_HELD', $Slot.PreviousHeld)
    Remove-Item -LiteralPath $Slot.HolderPath -Force -ErrorAction SilentlyContinue
    try { $Slot.Mutex.ReleaseMutex() } catch [System.ApplicationException] { } catch [System.Threading.SynchronizationLockException] { }
    $Slot.Mutex.Dispose()
}
