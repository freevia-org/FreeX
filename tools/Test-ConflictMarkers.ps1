param(
    [string[]]$SearchRoots = @(".github", "docs", "release", "src", "tests", "tools"),
    [string[]]$TextExtensions = @(".cs", ".csproj", ".props", ".targets", ".xaml", ".xml", ".resx", ".json", ".md", ".ps1", ".yml", ".yaml", ".config", ".ruleset")
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

function Test-IsIgnoredPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $segments = $Path -split '[\\/]'
    return $segments -contains "bin" -or
        $segments -contains "obj" -or
        $segments -contains ".git"
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
foreach ($searchRoot in $SearchRoots) {
    $resolvedSearchRoot = Resolve-RepoPath $searchRoot
    if (-not (Test-Path -LiteralPath $resolvedSearchRoot)) {
        continue
    }

    $rootItem = Get-Item -LiteralPath $resolvedSearchRoot
    if ($rootItem -is [System.IO.FileInfo]) {
        if ($normalizedExtensions.Contains($rootItem.Extension) -and -not (Test-IsIgnoredPath $rootItem.FullName)) {
            $candidateFiles.Add($rootItem)
        }

        continue
    }

    Get-ChildItem -LiteralPath $rootItem.FullName -File -Recurse |
        Where-Object {
            $normalizedExtensions.Contains($_.Extension) -and -not (Test-IsIgnoredPath $_.FullName)
        } |
        Sort-Object FullName |
        ForEach-Object { $candidateFiles.Add($_) }
}

if ($candidateFiles.Count -eq 0) {
    throw "No text files were found under: $($SearchRoots -join ', ')"
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
