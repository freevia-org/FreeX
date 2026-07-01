param(
    [string[]]$JsonRoots = @()
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

function Test-IsExcludedPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $relativePath = Get-RepositoryRelativePath $Path
    $segments = $relativePath -split '[\\/]'
    return $segments -contains "bin" -or
        $segments -contains "obj" -or
        $segments -contains ".worktrees" -or
        $segments -contains ".claude"
}

function Get-RepositoryRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not [System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    $root = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($root.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    }

    return $Path
}

function Get-TrackedRepositoryFiles {
    $gitOutput = & git -C $repoRoot ls-files --deduplicate
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate tracked files with git ls-files."
    }

    foreach ($relativePath in $gitOutput) {
        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            continue
        }

        $relativePath
    }
}

$jsonPaths = New-Object System.Collections.Generic.List[string]
if ($JsonRoots.Count -eq 0) {
    foreach ($trackedPath in (Get-TrackedRepositoryFiles)) {
        $resolvedTrackedPath = Join-Path $repoRoot $trackedPath
        if ((Test-IsExcludedPath $trackedPath) -or -not (Test-Path -LiteralPath $resolvedTrackedPath -PathType Leaf)) {
            continue
        }

        if ([System.IO.Path]::GetExtension($trackedPath) -ieq ".json") {
            $jsonPaths.Add($resolvedTrackedPath)
        }
    }
}
else {
    foreach ($jsonRoot in $JsonRoots) {
        $resolvedJsonRoot = Resolve-RepoPath $jsonRoot
        if (-not (Test-Path -LiteralPath $resolvedJsonRoot)) {
            throw "JSON path was not found: $resolvedJsonRoot"
        }

        $rootItem = Get-Item -LiteralPath $resolvedJsonRoot
        if ($rootItem -is [System.IO.FileInfo]) {
            if ($rootItem.Extension -ieq ".json") {
                $jsonPaths.Add($rootItem.FullName)
            }

            continue
        }

        Get-ChildItem -LiteralPath $rootItem.FullName -Filter "*.json" -File -Recurse |
            Where-Object { -not (Test-IsExcludedPath $_.FullName) } |
            ForEach-Object { $jsonPaths.Add($_.FullName) }
    }
}

$jsonFiles = @($jsonPaths | Sort-Object -Unique | ForEach-Object { Get-Item -LiteralPath $_ })
if ($jsonFiles.Count -eq 0) {
    if ($JsonRoots.Count -eq 0) {
        throw "No tracked JSON files were found."
    }

    throw "No JSON files were found under: $($JsonRoots -join ', ')"
}

$failedFiles = New-Object System.Collections.Generic.List[string]
foreach ($jsonFile in $jsonFiles) {
    try {
        Get-Content -LiteralPath $jsonFile.FullName -Raw | ConvertFrom-Json | Out-Null
    }
    catch {
        $failedFiles.Add($jsonFile.FullName)
        Write-Error "$($jsonFile.FullName): $($_.Exception.Message)" -ErrorAction Continue
    }
}

if ($failedFiles.Count -gt 0) {
    throw "JSON validation failed for $($failedFiles.Count) file(s)."
}

Write-Host "Validated $($jsonFiles.Count) JSON file(s)."
