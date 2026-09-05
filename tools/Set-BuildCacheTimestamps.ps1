[CmdletBinding()]
param(
    [string]$MarkerPath = "obj-build-cache-commit.txt",

    [string]$Remote = "origin"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Restoring a cached obj/ tree into a fresh CI checkout is only safe if file timestamps still mean
# what MSBuild thinks they mean. A checkout stamps EVERY source file with the checkout time, and the
# cache is unpacked afterwards, so without this script every restored output looks newer than every
# source and MSBuild silently skips compiling genuinely-changed code -- a green build that tested the
# previous commit. (Verified directly: backdating one edited source made MSBuild skip it and the new
# symbol was absent from the assembly, with exit code 0.)
#
# So instead of trusting checkout timestamps, this rewrites them into three tiers that encode what
# actually changed, according to git:
#
#   OLD (2001)  every tracked file                  -> older than the cache, so it is skipped
#   MID (2011)  every restored obj/ file            -> the cache itself
#   NEW (now)   every file differing from the cached commit -> newer, so it is rebuilt
#
# The crucial property is that the NEW tier comes from `git diff` against the commit the cache was
# built from, so it is exact: it does not care WHICH project consumes a file. That matters here
# because 30 csproj files pull sources in from outside their own directory via
# <Compile Include="../..."> -- any scheme that mapped changed files to projects by directory would
# wrongly skip those projects. MSBuild already knows the real include list for every project; this
# only has to make the timestamps honest, and MSBuild's own dependency cascade then rebuilds
# dependents (a rebuilt project's newer output invalidates everything referencing it).
#
# Anything uncertain falls back to a clean build by deleting obj/, which is only ever slow, never
# wrong: a missing output always forces a rebuild.

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    # The cache deliberately holds obj/ only: bin/ is ~2 GB against obj/'s ~0.1 GB, and 22 jobs of
    # bin would blow GitHub's 10 GB cache budget. The consequence is that bin/ is rebuilt by copying
    # out of obj/ during the build, so those copies carry the CURRENT time. If the cached obj/ were
    # backdated, every dependent would see a reference newer than its own output and recompile --
    # measured: a full 30-assembly rebuild, i.e. a correct cache that saved nothing. So the cache
    # tier is placed in the near future, ahead of the copies the build is about to make, and the
    # changed-file tier ahead of that. Only the ORDER matters: old < build-time copies < cache < changed.
    $oldTime = [datetime]::UtcNow.AddDays(-2)
    $midTime = [datetime]::UtcNow.AddHours(1)
    $newTime = [datetime]::UtcNow.AddHours(2)

    function Get-ObjectDirectories {
        return @(Get-ChildItem -Path $repoRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq "obj" -and $_.FullName -notmatch "[\\/]\.git[\\/]" })
    }

    function Invoke-CleanBuildFallback {
        param([Parameter(Mandatory = $true)][string]$Reason)

        # Re-wrap: PowerShell unrolls a returned array, so an empty result arrives as $null and
        # .Count would throw under StrictMode -- inside the very fallback that has to keep working.
        $directories = @(Get-ObjectDirectories)
        foreach ($directory in $directories) {
            Remove-Item -LiteralPath $directory.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
        Write-Host "Build cache: clean build ($Reason); removed $($directories.Count) obj directory/directories."
    }

    $resolvedMarkerPath = Join-Path $repoRoot $MarkerPath
    if (-not (Test-Path -LiteralPath $resolvedMarkerPath -PathType Leaf)) {
        Invoke-CleanBuildFallback -Reason "no cache marker at '$MarkerPath'"
        return
    }

    $cachedCommit = (Get-Content -LiteralPath $resolvedMarkerPath -Raw).Trim()
    if ($cachedCommit -notmatch '^[0-9a-fA-F]{40}$') {
        Invoke-CleanBuildFallback -Reason "cache marker does not contain a commit id"
        return
    }

    # The cached commit is usually not in a depth-1 checkout; fetching just that one commit is enough
    # to diff two trees.
    & git cat-file -e "$cachedCommit^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        & git fetch --no-tags --depth=1 $Remote $cachedCommit 2>&1 | Out-Null
        & git cat-file -e "$cachedCommit^{commit}" 2>$null
        if ($LASTEXITCODE -ne 0) {
            Invoke-CleanBuildFallback -Reason "cached commit $($cachedCommit.Substring(0, 9)) is unavailable"
            return
        }
    }

    $headCommit = (& git rev-parse HEAD).Trim()
    $diffOutput = @(& git diff --name-status -z --no-renames $cachedCommit $headCommit)
    if ($LASTEXITCODE -ne 0) {
        Invoke-CleanBuildFallback -Reason "git diff against $($cachedCommit.Substring(0, 9)) failed"
        return
    }

    # -z output is NUL separated: status, path, status, path, ...
    $diffFields = @(($diffOutput -join "`n") -split "`0" | Where-Object { $_ -ne "" })
    $changedPaths = [System.Collections.Generic.List[string]]::new()
    $deletedPaths = [System.Collections.Generic.List[string]]::new()
    for ($index = 0; $index + 1 -lt $diffFields.Count; $index += 2) {
        $status = $diffFields[$index]
        $path = $diffFields[$index + 1]
        if ($status -like "D*") { $deletedPaths.Add($path) } else { $changedPaths.Add($path) }
    }

    $trackedFiles = @((& git ls-files -z) -join "`n" -split "`0" | Where-Object { $_ -ne "" })
    if ($trackedFiles.Count -eq 0) {
        Invoke-CleanBuildFallback -Reason "git ls-files returned nothing"
        return
    }

    # Tier 1: everything tracked is old.
    $stampedOld = 0
    foreach ($relativePath in $trackedFiles) {
        $fullPath = Join-Path $repoRoot $relativePath
        if ([System.IO.File]::Exists($fullPath)) {
            [System.IO.File]::SetLastWriteTimeUtc($fullPath, $oldTime)
            $stampedOld++
        }
    }

    # Tier 2: the restored cache sits between old sources and changed ones.
    $stampedMid = 0
    foreach ($directory in Get-ObjectDirectories) {
        foreach ($file in @(Get-ChildItem -LiteralPath $directory.FullName -Recurse -File -Force -ErrorAction SilentlyContinue)) {
            [System.IO.File]::SetLastWriteTimeUtc($file.FullName, $midTime)
            $stampedMid++
        }
    }
    if ($stampedMid -eq 0) {
        Write-Host "Build cache: no restored obj content; this run performs a full build."
    }

    # Tier 3: anything that differs from the cached commit must rebuild.
    $stampedNew = 0
    foreach ($relativePath in $changedPaths) {
        $fullPath = Join-Path $repoRoot $relativePath
        if ([System.IO.File]::Exists($fullPath)) {
            [System.IO.File]::SetLastWriteTimeUtc($fullPath, $newTime)
            $stampedNew++
        }
    }

    # A deletion leaves nothing to stamp, so nothing would look newer than the cache and the owning
    # project would be skipped while still compiled against the removed file. Touch the nearest
    # project file instead, which forces that project (and, through MSBuild, its dependents) to
    # rebuild.
    $touchedProjects = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($relativePath in $deletedPaths) {
        $directory = Split-Path -Parent (Join-Path $repoRoot $relativePath)
        while ($directory -and $directory.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            $projectFiles = @(Get-ChildItem -LiteralPath $directory -Filter "*.csproj" -File -ErrorAction SilentlyContinue)
            if ($projectFiles.Count -gt 0) {
                foreach ($projectFile in $projectFiles) {
                    if ($touchedProjects.Add($projectFile.FullName)) {
                        [System.IO.File]::SetLastWriteTimeUtc($projectFile.FullName, $newTime)
                        $stampedNew++
                    }
                }
                break
            }
            $directory = Split-Path -Parent $directory
        }
    }

    Write-Host ("Build cache: reusing obj from {0} (HEAD {1}); {2} tracked file(s) marked old, {3} cached file(s) kept, {4} changed file(s) marked for rebuild, {5} deletion(s) forced {6} project rebuild(s)." -f `
        $cachedCommit.Substring(0, 9), $headCommit.Substring(0, 9), $stampedOld, $stampedMid, $changedPaths.Count, $deletedPaths.Count, $touchedProjects.Count)
}
finally {
    Pop-Location
}
