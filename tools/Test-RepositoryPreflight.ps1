param(
    [string]$JsonFilesScriptPath = "tools\Test-JsonFiles.ps1",
    [string]$XmlFilesScriptPath = "tools\Test-XmlFiles.ps1",
    [string]$ToolScriptsScriptPath = "tools\Test-ToolScripts.ps1",
    [string]$GitHubWorkflowsScriptPath = "tools\Test-GitHubWorkflows.ps1",
    [string]$DotNetProjectReferencesScriptPath = "tools\Test-DotNetProjectReferences.ps1",
    [string]$SolutionProjectsScriptPath = "tools\Test-SolutionProjects.ps1",
    [string]$GeneratedDocsScriptPath = "tools\Test-GeneratedDocs.ps1"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function Invoke-RepositoryPreflight {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $resolvedScriptPath = Resolve-RepoPath $ScriptPath
    if (-not (Test-Path -LiteralPath $resolvedScriptPath -PathType Leaf)) {
        throw "$Label preflight script was not found: $resolvedScriptPath"
    }

    Write-Host "Running $Label preflight..."
    & $resolvedScriptPath
}

Invoke-RepositoryPreflight -ScriptPath $JsonFilesScriptPath -Label "JSON files"
Invoke-RepositoryPreflight -ScriptPath $XmlFilesScriptPath -Label "XML files"
Invoke-RepositoryPreflight -ScriptPath $ToolScriptsScriptPath -Label "PowerShell tools"
Invoke-RepositoryPreflight -ScriptPath $GitHubWorkflowsScriptPath -Label "GitHub workflows"
Invoke-RepositoryPreflight -ScriptPath $DotNetProjectReferencesScriptPath -Label ".NET project references"
Invoke-RepositoryPreflight -ScriptPath $SolutionProjectsScriptPath -Label "solution projects"
Invoke-RepositoryPreflight -ScriptPath $GeneratedDocsScriptPath -Label "generated docs"

Write-Host "Repository preflight checks passed."
