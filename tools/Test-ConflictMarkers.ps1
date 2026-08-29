param(
    [string]$ProjectRoot = ".",
    [string[]]$SearchRoots = @(),
    [string[]]$TextExtensions = @(".cs", ".csproj", ".props", ".targets", ".slnx", ".xaml", ".xml", ".resx", ".json", ".md", ".ps1", ".yml", ".yaml", ".config", ".ruleset")
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

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

$resolvedProjectRoot = Resolve-ToolRepoPath -Path $ProjectRoot -RepoRoot $repoRoot
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
$conflictMarkerPattern = '^(<<<<<<<|=======|>>>>>>>)($|[ <].*)'

if ($SearchRoots.Count -eq 0) {
    $pathSpecs = @($normalizedExtensions | Sort-Object | ForEach-Object { "*$_" })
    $trackedPaths = @(& git -C $resolvedProjectRoot ls-files -- $pathSpecs)
    if ($LASTEXITCODE -ne 0) {
        throw "git ls-files failed for project root: $resolvedProjectRoot"
    }

    if ($trackedPaths.Count -eq 0) {
        throw "No text files were found for Git conflict marker validation."
    }

    # Let Git scan its tracked-file index in one native process. Reading and matching more than
    # 14,000 files individually through PowerShell added roughly a minute to every preflight.
    $failedMatches = @(& git -C $resolvedProjectRoot grep -n -I -E -- $conflictMarkerPattern -- $pathSpecs 2>&1)
    $gitGrepExitCode = $LASTEXITCODE
    if ($gitGrepExitCode -gt 1) {
        throw "git grep failed while validating conflict markers (exit code $gitGrepExitCode): $($failedMatches -join [Environment]::NewLine)"
    }
    if ($gitGrepExitCode -eq 0) {
        foreach ($match in $failedMatches) {
            Write-Error "$match contains a Git conflict marker." -ErrorAction Continue
        }
        throw "Git conflict marker validation failed for $($failedMatches.Count) marker(s)."
    }

    Write-Host "Validated $($trackedPaths.Count) text file(s) for Git conflict markers."
    exit 0
} else {
    foreach ($searchRoot in $SearchRoots) {
        $resolvedSearchRoot = Resolve-ToolRepoPath -Path $searchRoot -RepoRoot $repoRoot
        if (-not (Test-Path -LiteralPath $resolvedSearchRoot)) {
            continue
        }

        $rootItem = Get-Item -LiteralPath $resolvedSearchRoot -Force
        if ($rootItem -is [System.IO.FileInfo]) {
            $relativePath = Get-ToolRelativePath -RootPath $resolvedProjectRoot -Path $rootItem.FullName
            if (-not (Test-ToolExcludedPath -Path $relativePath -RepoRoot $resolvedProjectRoot -ExcludedDirectoryNames @("bin", "obj", ".git", ".worktrees", ".claude"))) {
                Add-CandidateFile -Files $candidateFiles -SeenPaths $seenCandidatePaths -File $rootItem -Extensions $normalizedExtensions
            }

            continue
        }

        Get-ChildItem -LiteralPath $rootItem.FullName -File -Recurse -Force |
            Where-Object {
                $relativePath = Get-ToolRelativePath -RootPath $resolvedProjectRoot -Path $_.FullName
                -not (Test-ToolExcludedPath -Path $relativePath -RepoRoot $resolvedProjectRoot -ExcludedDirectoryNames @("bin", "obj", ".git", ".worktrees", ".claude"))
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
