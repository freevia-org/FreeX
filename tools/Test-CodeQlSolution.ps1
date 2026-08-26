param(
    [string]$SolutionPath = "FreeSuite.CodeQL.slnx"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedSolutionPath = Join-Path $repoRoot $SolutionPath
if (-not (Test-Path -LiteralPath $resolvedSolutionPath -PathType Leaf)) {
    throw "CodeQL solution was not found: $resolvedSolutionPath"
}

function ConvertTo-NormalizedRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $normalizedRoot = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $normalizedPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $normalizedPath.StartsWith($normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the repository root: $Path"
    }

    return $normalizedPath.Substring($normalizedRoot.Length).Replace('\', '/')
}

function Test-IsProductionProject {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    if ($RelativePath -notmatch '^(shared|src|freew|freep)/') {
        return $false
    }

    return $RelativePath -notmatch '(?i)(^|/)(tests?|tools?|TestSupport)(/|$)' -and
        $RelativePath -notmatch '(?i)(^|/)[^/]*Tests?(/|$)' -and
        $RelativePath -notmatch '(?i)(Tests?|TestSupport|VisualEvidence|Validation)\.csproj$'
}

[xml]$solution = Get-Content -LiteralPath $resolvedSolutionPath -Raw
$listedProjects = @(
    $solution.SelectNodes("//*[local-name()='Project']") |
        ForEach-Object { ([string]$_.Path).Replace('\', '/') } |
        Sort-Object -Unique
)
$productionProjects = @(
    foreach ($rootName in @("shared", "src", "freew", "freep")) {
        Get-ChildItem -LiteralPath (Join-Path $repoRoot $rootName) -Recurse -File -Filter "*.csproj"
    }
) | ForEach-Object { ConvertTo-NormalizedRelativePath $_.FullName } |
    Where-Object { Test-IsProductionProject $_ } |
    Sort-Object -Unique

$missing = @($productionProjects | Where-Object { $listedProjects -notcontains $_ })
$unexpected = @($listedProjects | Where-Object { $productionProjects -notcontains $_ })
$missingOnDisk = @($listedProjects | Where-Object { -not (Test-Path -LiteralPath (Join-Path $repoRoot $_) -PathType Leaf) })

if ($missing.Count -gt 0 -or $unexpected.Count -gt 0 -or $missingOnDisk.Count -gt 0) {
    $messages = @(
        $missing | ForEach-Object { "Production project missing from CodeQL solution: $_" }
        $unexpected | ForEach-Object { "Non-production project present in CodeQL solution: $_" }
        $missingOnDisk | ForEach-Object { "CodeQL solution references missing project: $_" }
    )
    throw "CodeQL solution validation failed:`n - $($messages -join "`n - ")"
}

Write-Host "Validated $($listedProjects.Count) production project entry(s) in $SolutionPath."
