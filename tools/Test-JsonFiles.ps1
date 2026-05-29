param(
    [string[]]$JsonRoots = @("docs", "release")
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

$jsonFiles = New-Object System.Collections.Generic.List[System.IO.FileInfo]
foreach ($jsonRoot in $JsonRoots) {
    $resolvedJsonRoot = Resolve-RepoPath $jsonRoot
    if (-not (Test-Path -LiteralPath $resolvedJsonRoot -PathType Container)) {
        throw "JSON root was not found: $resolvedJsonRoot"
    }

    Get-ChildItem -LiteralPath $resolvedJsonRoot -Filter "*.json" -File -Recurse |
        Sort-Object FullName |
        ForEach-Object { $jsonFiles.Add($_) }
}

if ($jsonFiles.Count -eq 0) {
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
