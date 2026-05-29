param(
    [string]$ProjectRoot = ".",
    [string]$SolutionPath = "FreeX.slnx"
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

function Normalize-RelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return $Path.Replace("\", "/")
}

function Get-RelativeRepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $rootPath = [System.IO.Path]::GetFullPath($repoRoot)
    if (-not $rootPath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $rootPath += [System.IO.Path]::DirectorySeparatorChar
    }

    $rootUri = New-Object System.Uri($rootPath)
    $pathUri = New-Object System.Uri([System.IO.Path]::GetFullPath($Path))
    return [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString())
}

$resolvedProjectRoot = Resolve-RepoPath $ProjectRoot
if (-not (Test-Path -LiteralPath $resolvedProjectRoot -PathType Container)) {
    throw "Project root was not found: $resolvedProjectRoot"
}

$resolvedSolutionPath = Resolve-RepoPath $SolutionPath
if (-not (Test-Path -LiteralPath $resolvedSolutionPath -PathType Leaf)) {
    throw "Solution file was not found: $resolvedSolutionPath"
}

[xml]$solutionXml = Get-Content -LiteralPath $resolvedSolutionPath -Raw
$solutionProjectPaths = @(
    $solutionXml.Solution.Folder.Project |
        ForEach-Object { Normalize-RelativePath ([string]$_.Path) } |
        Sort-Object -Unique
)

if ($solutionProjectPaths.Count -eq 0) {
    throw "No project entries were found in $resolvedSolutionPath"
}

$discoveredProjectPaths = @(
    Get-ChildItem -LiteralPath $resolvedProjectRoot -Filter "*.csproj" -File -Recurse |
        ForEach-Object { Normalize-RelativePath (Get-RelativeRepoPath $_.FullName) } |
        Where-Object { $_.StartsWith("src/") -or $_.StartsWith("tests/") } |
        Sort-Object -Unique
)

$missingFromSolution = @(
    $discoveredProjectPaths |
        Where-Object { $solutionProjectPaths -notcontains $_ }
)

$missingOnDisk = @(
    $solutionProjectPaths |
        Where-Object {
            $projectPath = Join-Path $repoRoot $_
            -not (Test-Path -LiteralPath $projectPath -PathType Leaf)
        }
)

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

if ($missingFromSolution.Count -gt 0 -or $missingOnDisk.Count -gt 0) {
    throw "Solution project validation failed."
}

Write-Host "Validated $($solutionProjectPaths.Count) solution project entry(s)."
