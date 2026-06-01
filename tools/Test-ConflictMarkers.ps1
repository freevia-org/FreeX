param(
    [string]$ProjectRoot = ".",
    [string[]]$SearchRoots = @(),
    [string[]]$TextExtensions = @(".cs", ".csproj", ".props", ".targets", ".slnx", ".xaml", ".xml", ".resx", ".json", ".md", ".ps1", ".yml", ".yaml", ".config", ".ruleset")
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

function Get-RelativeRepoPath {
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

function Test-IsIgnoredPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $segments = $Path -split '[\\/]'
    return $segments -contains "bin" -or
        $segments -contains "obj" -or
        $segments -contains ".git" -or
        $segments -contains ".worktrees" -or
        $segments -contains ".claude"
}

function Add-CandidateFile {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[System.IO.FileInfo]]$Files,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.HashSet[string]]$SeenPaths,
        [Parameter(Mandatory = $true)][System.IO.FileInfo]$File,
        [Parameter(Mandatory = $true)][System.Collections.Generic.HashSet[string]]$Extensions
    )

    if (-not $Extensions.Contains($File.Extension)) {
        return
    }

    $key = $File.FullName.ToUpperInvariant()
    if ($SeenPaths.Add($key)) {
        $Files.Add($File)
    }
}

$resolvedProjectRoot = Resolve-RepoPath $ProjectRoot
if (-not (Test-Path -LiteralPath $resolvedProjectRoot -PathType Container)) {
    throw "Project root was not found: $resolvedProjectRoot"
}

$normalizedExtensions = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)
foreach ($extension in $TextExtensions) {
    if ([string]::IsNullOrWhiteSpace($extension)) {
        continue
    }

    $normalized = if ($extension.StartsWith(".")) { $extension } else { ".$extension" }
    $normalizedExtensions.Add($normalized) | Out-Null
}

if ($normalizedExtensions.Count -eq 0) {
    throw "At least one text extension must be provided."
}

$candidateFiles = New-Object System.Collections.Generic.List[System.IO.FileInfo]
$seenCandidatePaths = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)

if ($SearchRoots.Count -eq 0) {
    $trackedPaths = @(git -C $resolvedProjectRoot ls-files)
    if ($LASTEXITCODE -ne 0) {
        throw "git ls-files failed for project root: $resolvedProjectRoot"
    }

    foreach ($trackedPath in $trackedPaths) {
        if ([string]::IsNullOrWhiteSpace($trackedPath)) {
            continue
        }

        $resolvedTrackedPath = Join-Path $resolvedProjectRoot ($trackedPath.Replace("/", [System.IO.Path]::DirectorySeparatorChar))
        if (Test-Path -LiteralPath $resolvedTrackedPath -PathType Leaf) {
            Add-CandidateFile -Files $candidateFiles -SeenPaths $seenCandidatePaths -File (Get-Item -LiteralPath $resolvedTrackedPath) -Extensions $normalizedExtensions
        }
    }
} else {
    foreach ($searchRoot in $SearchRoots) {
        $resolvedSearchRoot = Resolve-RepoPath $searchRoot
        if (-not (Test-Path -LiteralPath $resolvedSearchRoot)) {
            continue
        }

        $rootItem = Get-Item -LiteralPath $resolvedSearchRoot
        if ($rootItem -is [System.IO.FileInfo]) {
            $relativePath = Get-RelativeRepoPath -RootPath $resolvedProjectRoot -Path $rootItem.FullName
            if (-not (Test-IsIgnoredPath $relativePath)) {
                Add-CandidateFile -Files $candidateFiles -SeenPaths $seenCandidatePaths -File $rootItem -Extensions $normalizedExtensions
            }

            continue
        }

        Get-ChildItem -LiteralPath $rootItem.FullName -File -Recurse |
            Where-Object {
                $relativePath = Get-RelativeRepoPath -RootPath $resolvedProjectRoot -Path $_.FullName
                -not (Test-IsIgnoredPath $relativePath)
            } |
            Sort-Object FullName |
            ForEach-Object {
                Add-CandidateFile -Files $candidateFiles -SeenPaths $seenCandidatePaths -File $_ -Extensions $normalizedExtensions
            }
    }
}

if ($candidateFiles.Count -eq 0) {
    throw "No text files were found for Git conflict marker validation."
}

$conflictMarkerPattern = '^(<<<<<<<|=======|>>>>>>>)($|[ <].*)'
$failedMatches = New-Object System.Collections.Generic.List[string]
foreach ($candidateFile in $candidateFiles) {
    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadLines($candidateFile.FullName)) {
        $lineNumber++
        if ($line -match $conflictMarkerPattern) {
            $failedMatches.Add("$($candidateFile.FullName):$lineNumber")
            Write-Error "$($candidateFile.FullName):$lineNumber contains a Git conflict marker." -ErrorAction Continue
        }
    }
}

if ($failedMatches.Count -gt 0) {
    throw "Git conflict marker validation failed for $($failedMatches.Count) marker(s)."
}

Write-Host "Validated $($candidateFiles.Count) text file(s) for Git conflict markers."
