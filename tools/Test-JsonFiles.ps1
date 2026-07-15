param(
    [string[]]$JsonRoots = @()
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

$jsonPaths = New-Object System.Collections.Generic.List[string]
if ($JsonRoots.Count -eq 0) {
    foreach ($trackedPath in (Get-ToolTrackedRepositoryFiles -RepoRoot $repoRoot)) {
        $resolvedTrackedPath = Join-Path $repoRoot $trackedPath
        if ((Test-ToolExcludedPath -Path $trackedPath -RepoRoot $repoRoot) -or -not (Test-Path -LiteralPath $resolvedTrackedPath -PathType Leaf)) {
            continue
        }

        if ([System.IO.Path]::GetExtension($trackedPath) -ieq ".json") {
            $jsonPaths.Add($resolvedTrackedPath)
        }
    }
}
else {
    foreach ($jsonRoot in $JsonRoots) {
        $resolvedJsonRoot = Resolve-ToolRepoPath -Path $jsonRoot -RepoRoot $repoRoot
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
            Where-Object { -not (Test-ToolExcludedPath -Path $_.FullName -RepoRoot $repoRoot) } |
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
