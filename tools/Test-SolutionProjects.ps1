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

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$RootPath,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $fullRootPath = [System.IO.Path]::GetFullPath($RootPath)
    if (-not $fullRootPath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $fullRootPath += [System.IO.Path]::DirectorySeparatorChar
    }

    $rootUri = New-Object System.Uri($fullRootPath)
    $pathUri = New-Object System.Uri([System.IO.Path]::GetFullPath($Path))
    return [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString())
}

function Test-IsIgnoredProjectPath {
    param(
        [Parameter(Mandatory = $true)][System.IO.FileInfo]$ProjectFile,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    if ($ProjectFile.Name -like "*_wpftmp.csproj") {
        return $true
    }

    $segments = $RelativePath -split "/"
    return $segments -contains "bin" -or
        $segments -contains "obj" -or
        $segments -contains ".git" -or
        $segments -contains ".worktrees"
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
$solutionRoot = Split-Path -Parent $resolvedSolutionPath
$solutionProjectPaths = @(
    $solutionXml.SelectNodes("//*[local-name()='Project']") |
        ForEach-Object { Normalize-RelativePath ([string]$_.Path) } |
        Sort-Object -Unique
)

if ($solutionProjectPaths.Count -eq 0) {
    throw "No project entries were found in $resolvedSolutionPath"
}

$discoveredProjectPaths = @(
    Get-ChildItem -LiteralPath $resolvedProjectRoot -Filter "*.csproj" -File -Recurse |
        ForEach-Object {
            $relativePath = Normalize-RelativePath (Get-RelativePath -RootPath $resolvedProjectRoot -Path $_.FullName)
            if (-not (Test-IsIgnoredProjectPath -ProjectFile $_ -RelativePath $relativePath)) {
                $relativePath
            }
        } |
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
            $projectPath = Join-Path $solutionRoot $_
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
