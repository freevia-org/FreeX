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

function Invoke-RepositoryPreflight {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][string]$Label,
        [hashtable]$Parameters = @{}
    )

    $resolvedScriptPath = Resolve-ToolRepoPath -Path $ScriptPath -RepoRoot $repoRoot
    if (-not (Test-Path -LiteralPath $resolvedScriptPath -PathType Leaf)) {
        throw "$Label preflight script was not found: $resolvedScriptPath"
    }

    Write-Host "Running $Label preflight..."
    & $resolvedScriptPath @Parameters
}

function Resolve-RepositoryPreflightEntry {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [hashtable]$Parameters = @{}
    )

    $resolvedScriptPath = Resolve-ToolRepoPath -Path $ScriptPath -RepoRoot $repoRoot
    if (-not (Test-Path -LiteralPath $resolvedScriptPath -PathType Leaf)) {
        throw "$Label preflight script was not found: $resolvedScriptPath"
    }
    [pscustomobject]@{
        Label = $Label
        ScriptPath = $resolvedScriptPath
        Parameters = $Parameters
    }
}

if ($Mode -in @("All", "Static")) {
    # These checks are independent, read-only validators (JSON/XML linting, workflow/manifest
    # contracts, .NET SDK/reference/solution consistency, cross-platform source scans, generated
    # docs, conflict-marker sweeps): none of them writes shared state or depends on another
    # having already run, and none of them shells out to `dotnet build`/`dotnet test` (the
    # generated-docs check's own two build-based generators already serialize themselves inside
    # tools/Test-GeneratedDocs.ps1 to avoid MSBuild lock contention on shared projects). Dispatch
    # is dominated by per-process/full-repo-scan cost rather than CPU, so running them in
    # parallel materially cuts wall-clock time on this machine.
    $staticEntries = @(
        Resolve-RepositoryPreflightEntry -Label "JSON files" -ScriptPath $JsonFilesScriptPath
        Resolve-RepositoryPreflightEntry -Label "XML files" -ScriptPath $XmlFilesScriptPath
        Resolve-RepositoryPreflightEntry -Label "GitHub workflows" -ScriptPath $GitHubWorkflowsScriptPath
        Resolve-RepositoryPreflightEntry -Label "test-gate contract" -ScriptPath $TestGateContractScriptPath
        Resolve-RepositoryPreflightEntry -Label ".NET SDK readiness" -ScriptPath $DotNetSdkReadinessScriptPath
        Resolve-RepositoryPreflightEntry -Label ".NET project references" -ScriptPath $DotNetProjectReferencesScriptPath
        Resolve-RepositoryPreflightEntry -Label "solution projects" -ScriptPath $SolutionProjectsScriptPath
        Resolve-RepositoryPreflightEntry -Label "CodeQL production solution" -ScriptPath $CodeQlSolutionScriptPath
        Resolve-RepositoryPreflightEntry -Label "cross-platform portability" -ScriptPath $CrossPlatformPortabilityScriptPath
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
        Resolve-RepositoryPreflightEntry -Label "generated docs" -ScriptPath $GeneratedDocsScriptPath
        Resolve-RepositoryPreflightEntry -Label "Git conflict markers" -ScriptPath $ConflictMarkersScriptPath
    )

    $staticResults = $staticEntries | ForEach-Object -ThrottleLimit $ThrottleLimit -Parallel {
        $entry = $_
        $params = $entry.Parameters
        try {
            $output = & $entry.ScriptPath @params 2>&1
            [pscustomobject]@{
                Label = $entry.Label
                Success = $true
                Output = @($output | ForEach-Object { $_.ToString() })
                ErrorMessage = $null
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
    foreach ($result in $staticResults) {
        $resultsByLabel[$result.Label] = $result
    }
    $staticFailures = [System.Collections.Generic.List[string]]::new()
    foreach ($entry in $staticEntries) {
        $result = $resultsByLabel[$entry.Label]
        Write-Host "Running $($entry.Label) preflight..."
        foreach ($line in $result.Output) {
            Write-Host $line
        }
        if (-not $result.Success) {
            $staticFailures.Add("$($entry.Label) preflight failed: $($result.ErrorMessage)")
        }
    }
    if ($staticFailures.Count -gt 0) {
        throw ("Repository static preflight failed:`n - " + ($staticFailures -join "`n - "))
    }
}

if ($Mode -in @("All", "Platform")) {
    Invoke-RepositoryPreflight -ScriptPath $ToolScriptsScriptPath -Label "PowerShell tools"
    Invoke-RepositoryPreflight -ScriptPath $MacOsAppReadinessScriptPath -Label "macOS app readiness"
    Invoke-RepositoryPreflight -ScriptPath $LinuxPackagingScriptsScriptPath -Label "Linux packaging scripts"
}

Write-Host "Repository preflight checks passed. Mode: $Mode."
