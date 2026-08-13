param(
    [string]$ProjectRoot = ".",
    [string]$SolutionPath = "FreeX.slnx",
    [string[]]$ProjectPathPrefixes = @("src/", "tests/", "tools/", "shared/"),
    [string[]]$ExcludedProjectPathPrefixes = @()
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Test-IsIncludedProjectPath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    foreach ($prefix in $ExcludedProjectPathPrefixes) {
        if ($RelativePath.StartsWith((ConvertTo-ToolNormalizedRelativePath $prefix), [System.StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
    }

    foreach ($prefix in $ProjectPathPrefixes) {
        if ($RelativePath.StartsWith((ConvertTo-ToolNormalizedRelativePath $prefix), [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

$resolvedProjectRoot = Resolve-ToolRepoPath -Path $ProjectRoot -RepoRoot $repoRoot
if (-not (Test-Path -LiteralPath $resolvedProjectRoot -PathType Container)) {
    throw "Project root was not found: $resolvedProjectRoot"
}

$resolvedSolutionPath = Resolve-ToolRepoPath -Path $SolutionPath -RepoRoot $repoRoot
if (-not (Test-Path -LiteralPath $resolvedSolutionPath -PathType Leaf)) {
    throw "Solution file was not found: $resolvedSolutionPath"
}

[xml]$solutionXml = Get-Content -LiteralPath $resolvedSolutionPath -Raw
$solutionRoot = Split-Path -Parent $resolvedSolutionPath
$solutionRootPath = [System.IO.Path]::GetFullPath($solutionRoot)
if (-not $solutionRootPath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
    $solutionRootPath += [System.IO.Path]::DirectorySeparatorChar
}
$solutionProjectPaths = @(
    $solutionXml.SelectNodes("//*[local-name()='Project']") |
        ForEach-Object { ConvertTo-ToolNormalizedRelativePath ([string]$_.Path) } |
        Sort-Object -Unique
)

if ($solutionProjectPaths.Count -eq 0) {
    throw "No project entries were found in $resolvedSolutionPath"
}

$duplicateSolutionProjectPaths = @(
    $solutionXml.SelectNodes("//*[local-name()='Project']") |
        ForEach-Object { ConvertTo-ToolNormalizedRelativePath ([string]$_.Path) } |
        Group-Object { $_.ToUpperInvariant() } |
        Where-Object { $_.Count -gt 1 } |
        ForEach-Object { $_.Group[0] } |
        Sort-Object
)

$escapedSolutionProjectPaths = @(
    $solutionProjectPaths |
        Where-Object {
            $projectPath = if ([System.IO.Path]::IsPathRooted($_)) { $_ } else { Join-Path $solutionRoot $_ }
            $resolvedProjectPath = [System.IO.Path]::GetFullPath($projectPath)
            -not $resolvedProjectPath.StartsWith($solutionRootPath, [System.StringComparison]::OrdinalIgnoreCase)
        }
)

$discoveredProjectPaths = @(
    Get-ToolProjectFiles -Directory (Get-Item -LiteralPath $resolvedProjectRoot) |
        ForEach-Object {
            ConvertTo-ToolNormalizedRelativePath (Get-ToolRelativePath -RootPath $resolvedProjectRoot -Path $_.FullName)
        } |
        Where-Object { Test-IsIncludedProjectPath $_ } |
        Sort-Object -Unique
)

$missingFromSolution = @(
    $discoveredProjectPaths |
        Where-Object { $solutionProjectPaths -notcontains $_ }
)

$missingOnDisk = @(
    $solutionProjectPaths |
        Where-Object {
            $projectPath = Join-Path $solutionRoot $_
            ($escapedSolutionProjectPaths -notcontains $_) -and
                -not (Test-Path -LiteralPath $projectPath -PathType Leaf)
        }
)

if ($duplicateSolutionProjectPaths.Count -gt 0) {
    foreach ($projectPath in $duplicateSolutionProjectPaths) {
        Write-Error "Duplicate solution project entry: $projectPath" -ErrorAction Continue
    }
}

if ($escapedSolutionProjectPaths.Count -gt 0) {
    foreach ($projectPath in $escapedSolutionProjectPaths) {
        Write-Error "Solution project path escapes solution root: $projectPath" -ErrorAction Continue
    }
}

if ($missingFromSolution.Count -gt 0) {
    foreach ($projectPath in $missingFromSolution) {
        Write-Error "Project missing from solution: $projectPath" -ErrorAction Continue
    }
}

if ($missingOnDisk.Count -gt 0) {
    foreach ($projectPath in $missingOnDisk) {
        Write-Error "Solution references missing project: $projectPath" -ErrorAction Continue
    }
}

if ($duplicateSolutionProjectPaths.Count -gt 0 -or $escapedSolutionProjectPaths.Count -gt 0 -or $missingFromSolution.Count -gt 0 -or $missingOnDisk.Count -gt 0) {
    throw "Solution project validation failed."
}

Write-Host "Validated $($solutionProjectPaths.Count) solution project entry(s)."
