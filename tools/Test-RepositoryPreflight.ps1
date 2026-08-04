param(
    [string]$JsonFilesScriptPath = "tools\Test-JsonFiles.ps1",
    [string]$XmlFilesScriptPath = "tools\Test-XmlFiles.ps1",
    [string]$ToolScriptsScriptPath = "tools\Test-ToolScripts.ps1",
    [string]$GitHubWorkflowsScriptPath = "tools\Test-GitHubWorkflows.ps1",
    [string]$DotNetSdkReadinessScriptPath = "tools\Test-DotNetSdkReadiness.ps1",
    [string]$DotNetProjectReferencesScriptPath = "tools\Test-DotNetProjectReferences.ps1",
    [string]$SolutionProjectsScriptPath = "tools\Test-SolutionProjects.ps1",
    [string]$MacOsAppReadinessScriptPath = "tools\Test-MacOsAppReadiness.ps1",
    [string]$GeneratedDocsScriptPath = "tools\Test-GeneratedDocs.ps1",
    [string]$ConflictMarkersScriptPath = "tools\Test-ConflictMarkers.ps1",
    [string]$LinuxPackagingScriptsScriptPath = "tools\Test-LinuxPackagingScripts.ps1"
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

Invoke-RepositoryPreflight -ScriptPath $JsonFilesScriptPath -Label "JSON files"
Invoke-RepositoryPreflight -ScriptPath $XmlFilesScriptPath -Label "XML files"
Invoke-RepositoryPreflight -ScriptPath $ToolScriptsScriptPath -Label "PowerShell tools"
Invoke-RepositoryPreflight -ScriptPath $GitHubWorkflowsScriptPath -Label "GitHub workflows"
Invoke-RepositoryPreflight -ScriptPath $DotNetSdkReadinessScriptPath -Label ".NET SDK readiness"
Invoke-RepositoryPreflight -ScriptPath $DotNetProjectReferencesScriptPath -Label ".NET project references"
Invoke-RepositoryPreflight -ScriptPath $SolutionProjectsScriptPath -Label "solution projects"
Invoke-RepositoryPreflight -ScriptPath $SolutionProjectsScriptPath -Label "default test solution projects" -Parameters @{
    SolutionPath = "FreeX.DefaultTests.slnx"
    ProjectPathPrefixes = @("tests/")
    ExcludedProjectPathPrefixes = @(
        "tests/FreeX.App.Host.Tests/",
        "tests/FreeX.App.UI.Tests/",
        "tests/Free.Shared.Shell.Avalonia.Tests/",
        "tests/Free.Shared.Ribbon.Wpf.Tests/"
    )
}
Invoke-RepositoryPreflight -ScriptPath $SolutionProjectsScriptPath -Label "FreeW solution projects" -Parameters @{
    SolutionPath = "FreeW.slnx"
    ProjectPathPrefixes = @("freew/", "tools/FreeW.")
}
Invoke-RepositoryPreflight -ScriptPath $SolutionProjectsScriptPath -Label "FreeP solution projects" -Parameters @{
    SolutionPath = "FreeP.slnx"
    ProjectPathPrefixes = @("freep/")
}
Invoke-RepositoryPreflight -ScriptPath $MacOsAppReadinessScriptPath -Label "macOS app readiness"
Invoke-RepositoryPreflight -ScriptPath $LinuxPackagingScriptsScriptPath -Label "Linux packaging scripts"
Invoke-RepositoryPreflight -ScriptPath $GeneratedDocsScriptPath -Label "generated docs"
Invoke-RepositoryPreflight -ScriptPath $ConflictMarkersScriptPath -Label "Git conflict markers"

Write-Host "Repository preflight checks passed."
