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
    [string]$LinuxPackagingScriptsScriptPath = "tools/Test-LinuxPackagingScripts.ps1",

    [ValidateRange(1, 32)]
    [int]$ThrottleLimit = 8
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

$pwshSource = (Get-Process -Id $PID).Path

# Start the longest checks first (stable within a priority) so the batch ends when the single
# longest check ends, not when a long check that happened to be queued last finally starts.
$dispatchOrder = @($entries | Sort-Object -Property Priority -Stable)

$results = $dispatchOrder | ForEach-Object -ThrottleLimit $ThrottleLimit -Parallel {
    $entry = $_
    $params = $entry.Parameters
    try {
        if ($entry.Isolated) {
            $output = & $using:pwshSource -NoProfile -File $entry.ScriptPath @params 2>&1
            $exitCode = $LASTEXITCODE
            [pscustomobject]@{
                Label = $entry.Label
                Success = ($exitCode -eq 0)
                Output = @($output | ForEach-Object { $_.ToString() })
                ErrorMessage = "exited with code $exitCode"
            }
        }
        else {
            $output = & $entry.ScriptPath @params 2>&1
            [pscustomobject]@{
                Label = $entry.Label
                Success = $true
                Output = @($output | ForEach-Object { $_.ToString() })
                ErrorMessage = $null
            }
        }
    }
    catch {
        [pscustomobject]@{
            Label = $entry.Label
            Success = $false
            Output = @()
            ErrorMessage = $_.Exception.Message
        }
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
