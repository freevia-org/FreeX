param(
    [ValidateSet("All", "Static", "Platform")]
    [string]$Mode = "All",

    [string]$JsonFilesScriptPath = "tools/Test-JsonFiles.ps1",
    [string]$XmlFilesScriptPath = "tools/Test-XmlFiles.ps1",
    [string]$ToolScriptsScriptPath = "tools/Test-ToolScripts.ps1",
    [string]$GitHubWorkflowsScriptPath = "tools/Test-GitHubWorkflows.ps1",
    [string]$TestGateContractScriptPath = "tools/Test-TestGateContract.ps1",
    [string]$DotNetSdkReadinessScriptPath = "tools/Test-DotNetSdkReadiness.ps1",
    [string]$DotNetProjectReferencesScriptPath = "tools/Test-DotNetProjectReferences.ps1",
    [string]$SolutionProjectsScriptPath = "tools/Test-SolutionProjects.ps1",
    [string]$CodeQlSolutionScriptPath = "tools/Test-CodeQlSolution.ps1",
    [string]$CrossPlatformPortabilityScriptPath = "tools/Test-CrossPlatformPortability.ps1",
    [string]$MacOsAppReadinessScriptPath = "tools/Test-MacOsAppReadiness.ps1",
    [string]$GeneratedDocsScriptPath = "tools/Test-GeneratedDocs.ps1",
    [string]$ConflictMarkersScriptPath = "tools/Test-ConflictMarkers.ps1",
    [string]$LocalOnlyEvidencePathsScriptPath = "tools/Test-LocalOnlyEvidencePaths.ps1",
    [string]$LinuxPackagingScriptsScriptPath = "tools/Test-LinuxPackagingScripts.ps1",

    [ValidateRange(1, 32)]
    [int]$ThrottleLimit = 8,

    # Local pre-merge scoping (tools/Test-BranchForIntegration.ps1 only). When set, a check runs only
    # if the diff against this base ref can change its outcome (eng/preflight-scopes.json); anything
    # unmapped, or any doubt, runs everything. CI, tester-release and a bare run never set it, so
    # they always run every check. -Full forces the same locally.
    [string]$ScopeBaseRef = "",

    [switch]$Full,

    # Wall-clock budget for a single check. A check that exceeds it has its whole process tree
    # killed and is retried once; a second timeout fails the gate naming that check. The budget
    # is deliberately generous: it exists to bound a check that would otherwise never return, not
    # to police slow ones. On a machine saturated by parallel builds, in a worktree with nothing
    # built yet, the generated-docs check (two cold scratch builds) has been measured at just over
    # fifteen minutes without being stuck.
    [ValidateRange(1, 86400)]
    [int]$CheckTimeoutSeconds = 1800
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

# The static checks below run through ForEach-Object -Parallel, which does not exist in Windows
# PowerShell 5.1 and fails there with "Parameter set cannot be resolved using the specified named
# parameters". Callers do reach this from 5.1 (RepositoryPreflightTests runs it through the default
# host), so re-launch under the canonical pwsh rather than failing. Returns immediately under pwsh.
Invoke-ToolCanonicalPwshHost -ScriptPath $PSCommandPath -ForwardedArguments $PSBoundParameters.Keys.ForEach({
    $value = $PSBoundParameters[$_]
    if ($value -is [switch]) { "-$_" } else { "-$_"; [string]$value }
})

function Resolve-RepositoryPreflightEntry {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [hashtable]$Parameters = @{},
        # Isolated entries run as their own `pwsh -File` child process instead of inside a shared
        # runspace. Required for scripts that mutate process-wide state (environment variables,
        # current directory) or that rely on exit codes, so they cannot disturb the other checks.
        [switch]$Isolated,
        # Lower runs first. Dispatch order only affects wall-clock (start the long checks first);
        # output is always printed in declared order.
        [int]$Priority = 50
    )

    $resolvedScriptPath = Resolve-ToolRepoPath -Path $ScriptPath -RepoRoot $repoRoot
    if (-not (Test-Path -LiteralPath $resolvedScriptPath -PathType Leaf)) {
        throw "$Label preflight script was not found: $resolvedScriptPath"
    }
    [pscustomobject]@{
        Label = $Label
        ScriptPath = $resolvedScriptPath
        Parameters = $Parameters
        Isolated = [bool]$Isolated
        Priority = $Priority
    }
}

# All checks below are independent, read-only validators (JSON/XML linting, workflow/manifest
# contracts, .NET SDK/reference/solution consistency, cross-platform source scans, generated
# docs, conflict-marker sweeps, PowerShell-tool/macOS/Linux-packaging self-tests): none of them
# depends on another having run first. They used to run as two serial phases (static, then
# platform), which made the platform phase pure added wall-clock; they now share one parallel
# batch. Dispatch is dominated by per-process/full-repo-scan cost rather than CPU, so running
# them in parallel materially cuts wall-clock time on this machine.
$entries = @()
if ($Mode -in @("All", "Static")) {
    $entries += @(
        Resolve-RepositoryPreflightEntry -Label "JSON files" -ScriptPath $JsonFilesScriptPath
        Resolve-RepositoryPreflightEntry -Label "XML files" -ScriptPath $XmlFilesScriptPath
        Resolve-RepositoryPreflightEntry -Label "GitHub workflows" -ScriptPath $GitHubWorkflowsScriptPath
        Resolve-RepositoryPreflightEntry -Label "test-gate contract" -ScriptPath $TestGateContractScriptPath
        Resolve-RepositoryPreflightEntry -Label ".NET SDK readiness" -ScriptPath $DotNetSdkReadinessScriptPath
        Resolve-RepositoryPreflightEntry -Label ".NET project references" -ScriptPath $DotNetProjectReferencesScriptPath
        Resolve-RepositoryPreflightEntry -Label "solution projects" -ScriptPath $SolutionProjectsScriptPath
        Resolve-RepositoryPreflightEntry -Label "CodeQL production solution" -ScriptPath $CodeQlSolutionScriptPath
        Resolve-RepositoryPreflightEntry -Label "cross-platform portability" -ScriptPath $CrossPlatformPortabilityScriptPath -Priority 0
        Resolve-RepositoryPreflightEntry -Label "default test solution projects" -ScriptPath $SolutionProjectsScriptPath -Parameters @{
            SolutionPath = "FreeX.DefaultTests.slnx"
            ProjectPathPrefixes = @("tests/")
            ExcludedProjectPathPrefixes = @(
                "tests/FreeX.App.Host.Tests/",
                "tests/FreeX.App.UI.Tests/",
                "tests/Free.Shared.Shell.Avalonia.Tests/",
                "tests/Free.Shared.Ribbon.Wpf.Tests/"
            )
        }
        Resolve-RepositoryPreflightEntry -Label "FreeW solution projects" -ScriptPath $SolutionProjectsScriptPath -Parameters @{
            SolutionPath = "FreeW.slnx"
            ProjectPathPrefixes = @("freew/", "tools/FreeW.")
        }
        Resolve-RepositoryPreflightEntry -Label "FreeP solution projects" -ScriptPath $SolutionProjectsScriptPath -Parameters @{
            SolutionPath = "FreeP.slnx"
            ProjectPathPrefixes = @("freep/")
        }
        Resolve-RepositoryPreflightEntry -Label "generated docs" -ScriptPath $GeneratedDocsScriptPath -Priority 1
        Resolve-RepositoryPreflightEntry -Label "Git conflict markers" -ScriptPath $ConflictMarkersScriptPath
        Resolve-RepositoryPreflightEntry -Label "local-only evidence paths" -ScriptPath $LocalOnlyEvidencePathsScriptPath
    )
}

if ($Mode -in @("All", "Platform")) {
    # The platform self-tests set environment variables and spawn synthetic tool processes, so
    # each runs isolated in its own pwsh process rather than inside the shared runspace pool.
    $entries += @(
        Resolve-RepositoryPreflightEntry -Label "PowerShell tools" -ScriptPath $ToolScriptsScriptPath -Isolated -Priority 2
        Resolve-RepositoryPreflightEntry -Label "macOS app readiness" -ScriptPath $MacOsAppReadinessScriptPath -Isolated -Priority 3
        Resolve-RepositoryPreflightEntry -Label "Linux packaging scripts" -ScriptPath $LinuxPackagingScriptsScriptPath -Isolated -Priority 3
    )
}

if (-not [string]::IsNullOrWhiteSpace($ScopeBaseRef) -and -not $Full) {
    . (Join-Path $PSScriptRoot "PreflightScopeSupport.ps1")
    $ownPathsByLabel = @{}
    foreach ($scopedEntry in $entries) {
        $ownPathsByLabel[$scopedEntry.Label] = @((Get-ToolRelativePath -RootPath $repoRoot -Path $scopedEntry.ScriptPath).Replace("\", "/"))
    }
    $selection = Get-PreflightScopeSelection -RepoRoot $repoRoot -BaseRef $ScopeBaseRef -Labels @($entries | ForEach-Object { $_.Label }) -OwnPathsByLabel $ownPathsByLabel
    if ($selection.Full) {
        Write-Host "Scoped preflight: running every check ($($selection.Reason))."
    }
    else {
        $skipped = @($entries | Where-Object { -not $selection.RunLabels.Contains($_.Label) } | ForEach-Object { $_.Label })
        Write-Host "Scoped preflight against $($selection.BaseSha.Substring(0, 9)): $($selection.ChangedPathCount) changed path(s); running $($selection.RunLabels.Count) of $(@($entries).Count) check(s)."
        foreach ($label in @($entries | Where-Object { $selection.RunLabels.Contains($_.Label) } | ForEach-Object { $_.Label })) {
            Write-Host "  run:  $label ($($selection.Reasons[$label]))"
        }
        foreach ($label in $skipped) {
            Write-Host "  skip: $label (no changed path can affect it; use -Full to run it anyway)"
        }
        $entries = @($entries | Where-Object { $selection.RunLabels.Contains($_.Label) })
    }
}

if (@($entries).Count -eq 0) {
    Write-Host "Repository preflight: no check is in scope for this change. Mode: $Mode."
    return
}

$pwshSource = (Get-Process -Id $PID).Path

# Start the longest checks first (stable within a priority) so the batch ends when the single
# longest check ends, not when a long check that happened to be queued last finally starts.
$dispatchOrder = @($entries | Sort-Object -Property Priority -Stable)

# Every check runs as its own pwsh child process, watched by a wall-clock timeout.
#
# The checks used to share one ForEach-Object -Parallel runspace pool. On Windows that batch
# intermittently stalled forever with no output: two `git` children started within a second of
# each other (one in-process from the test-gate contract, one inside a generated-docs generator)
# both sat idle in a kernel wait, and nothing above them could notice, so the gate hung until
# someone killed it by hand. A runspace cannot be timed out or killed; a process can. Each child
# also gets a closed stdin and its own hidden console instead of sharing the caller's, which
# removes the shared handles the stalled processes had in common. Whatever the remaining cause,
# a stall is now bounded: the stuck check's process tree is killed, the check is retried once,
# and a second timeout fails the gate naming the check.
$checkRunnerScript = @'
$ProgressPreference = 'SilentlyContinue'
try {
    $checkParameters = ConvertFrom-Json -InputObject $env:FREEX_PREFLIGHT_CHECK_PARAMETERS -AsHashtable
    & $env:FREEX_PREFLIGHT_CHECK_SCRIPT @checkParameters 2>&1 | ForEach-Object { "$_" }
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
'@
$checkRunnerEncodedCommand = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($checkRunnerScript))
$checkWorkingDirectory = (Get-Location -PSProvider FileSystem).ProviderPath
$maxCheckAttempts = 2

function Start-RepositoryPreflightCheck {
    param(
        [Parameter(Mandatory = $true)]$Entry,
        [Parameter(Mandatory = $true)][int]$Attempt
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($pwshSource)
    $startInfo.ArgumentList.Add("-NoProfile")
    $startInfo.ArgumentList.Add("-NonInteractive")
    if ($Entry.Isolated) {
        # Scripts that rely on their own exit code keep the plain `pwsh -File` contract.
        $startInfo.ArgumentList.Add("-File")
        $startInfo.ArgumentList.Add($Entry.ScriptPath)
    }
    else {
        $startInfo.ArgumentList.Add("-OutputFormat")
        $startInfo.ArgumentList.Add("Text")
        $startInfo.ArgumentList.Add("-EncodedCommand")
        $startInfo.ArgumentList.Add($checkRunnerEncodedCommand)
        $startInfo.Environment["FREEX_PREFLIGHT_CHECK_SCRIPT"] = $Entry.ScriptPath
        $startInfo.Environment["FREEX_PREFLIGHT_CHECK_PARAMETERS"] =
            ConvertTo-Json -InputObject $Entry.Parameters -Compress -Depth 8
    }
    $startInfo.WorkingDirectory = $checkWorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $process.StandardInput.Close()
    [pscustomobject]@{
        Entry = $Entry
        Attempt = $Attempt
        Process = $process
        StandardOutput = $process.StandardOutput.ReadToEndAsync()
        StandardError = $process.StandardError.ReadToEndAsync()
        Stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    }
}

function Stop-RepositoryPreflightCheck {
    param([Parameter(Mandatory = $true)]$Check)

    try {
        if (-not $Check.Process.HasExited) {
            $Check.Process.Kill($true)
        }
    }
    catch {
        # The process exited between the check and the kill; nothing is left to stop.
    }
}

function Complete-RepositoryPreflightCheck {
    param(
        [Parameter(Mandatory = $true)]$Check,
        [switch]$TimedOut
    )

    if (-not $TimedOut) {
        $Check.Process.WaitForExit()
    }
    # After a kill a surviving grandchild could still hold the pipes open, so never wait on the
    # readers without a bound.
    $readers = [System.Threading.Tasks.Task[]]@($Check.StandardOutput, $Check.StandardError)
    try {
        [void][System.Threading.Tasks.Task]::WaitAll($readers, 15000)
    }
    catch {
        # A faulted reader is reported below as missing output rather than as a second failure.
    }
    $splitLines = {
        param($reader)
        if (-not $reader.IsCompletedSuccessfully -or [string]::IsNullOrEmpty($reader.Result)) {
            return @()
        }
        return @($reader.Result.TrimEnd("`r", "`n") -split "\r?\n")
    }
    $outputLines = @(& $splitLines $Check.StandardOutput)
    $errorLines = @(& $splitLines $Check.StandardError)

    $entry = $Check.Entry
    if ($TimedOut) {
        $success = $false
        $errorMessage = "timed out after $CheckTimeoutSeconds second(s) on attempt $($Check.Attempt) of $maxCheckAttempts; " +
            "its process tree was killed. The check is hung, or the machine is too loaded for the budget " +
            "(raise -CheckTimeoutSeconds)."
        $outputLines += $errorLines
    }
    else {
        $exitCode = $Check.Process.ExitCode
        $success = ($exitCode -eq 0)
        if ($entry.Isolated -or $success -or $errorLines.Count -eq 0) {
            $errorMessage = "exited with code $exitCode"
            $outputLines += $errorLines
        }
        else {
            $errorMessage = $errorLines -join "`n"
        }
    }
    $Check.Process.Dispose()

    [pscustomobject]@{
        Label = $entry.Label
        Success = $success
        Output = $outputLines
        ErrorMessage = $errorMessage
    }
}

$pendingChecks = [System.Collections.Generic.List[object]]::new()
foreach ($entry in $dispatchOrder) {
    $pendingChecks.Add([pscustomobject]@{ Entry = $entry; Attempt = 1 })
}
$runningChecks = [System.Collections.Generic.List[object]]::new()
$results = [System.Collections.Generic.List[object]]::new()
try {
    while ($pendingChecks.Count -gt 0 -or $runningChecks.Count -gt 0) {
        while ($runningChecks.Count -lt $ThrottleLimit -and $pendingChecks.Count -gt 0) {
            $next = $pendingChecks[0]
            $pendingChecks.RemoveAt(0)
            $runningChecks.Add((Start-RepositoryPreflightCheck -Entry $next.Entry -Attempt $next.Attempt))
        }

        foreach ($check in @($runningChecks)) {
            if ($check.Process.HasExited) {
                [void]$runningChecks.Remove($check)
                $results.Add((Complete-RepositoryPreflightCheck -Check $check))
            }
            elseif ($check.Stopwatch.Elapsed.TotalSeconds -ge $CheckTimeoutSeconds) {
                [void]$runningChecks.Remove($check)
                Stop-RepositoryPreflightCheck -Check $check
                $timedOutResult = Complete-RepositoryPreflightCheck -Check $check -TimedOut
                if ($check.Attempt -lt $maxCheckAttempts) {
                    Write-Warning ("$($check.Entry.Label) preflight $($timedOutResult.ErrorMessage) Retrying once.")
                    $pendingChecks.Insert(0, [pscustomobject]@{ Entry = $check.Entry; Attempt = $check.Attempt + 1 })
                }
                else {
                    $results.Add($timedOutResult)
                }
            }
        }

        if ($runningChecks.Count -gt 0) {
            Start-Sleep -Milliseconds 100
        }
    }
}
finally {
    # Interrupted or failed mid-batch: do not leave check processes behind.
    foreach ($check in $runningChecks) {
        Stop-RepositoryPreflightCheck -Check $check
    }
}

# Preserve deterministic, attributable output: print each check's own output grouped under
# its label in the original declared order (not parallel completion order), then fail the
# whole gate naming every check that failed, if any did.
$resultsByLabel = @{}
foreach ($result in $results) {
    $resultsByLabel[$result.Label] = $result
}
$failures = [System.Collections.Generic.List[string]]::new()
foreach ($entry in $entries) {
    $result = $resultsByLabel[$entry.Label]
    Write-Host "Running $($entry.Label) preflight..."
    foreach ($line in $result.Output) {
        Write-Host $line
    }
    if (-not $result.Success) {
        $failures.Add("$($entry.Label) preflight failed: $($result.ErrorMessage)")
    }
}
if ($failures.Count -gt 0) {
    throw ("Repository preflight failed:`n - " + ($failures -join "`n - "))
}

Write-Host "Repository preflight checks passed. Mode: $Mode."
