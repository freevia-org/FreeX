# Shared helpers for impact-based CI coverage selection. Dot-source this file.
#
# THE SAFETY ARGUMENT, in one place, because every other file in this feature leans on it.
#
# A push to main may skip the commit-gate test matrix ONLY when every path that changed between
# the commit and its ANCHOR is "inert". The ANCHOR is the nearest ancestor commit that has a
# successful, completed ci.yml run that covered the FULL matrix (or that predates this feature,
# when every run was full). An inert-only difference means no commit-gate test, preflight check or
# build can observe the change, so the anchor's green result is the commit's green result.
#
# "Inert" is deliberately tiny: whole documentation directories under docs/ that NO tracked
# source, script, project, workflow or manifest refers to. That claim is mechanically enforced by
# Test-CiInertDirectoryReferences (run by tools/Test-TestGateContract.ps1 in the static
# preflight), so the day a test starts reading an "inert" directory the contract fails and the
# directory must leave the list.
#
# Why not select gates per project? Measured on this repository: nearly every commit-gate test
# project reads repository files at runtime (source-contract tests via WorkspaceFileLocator and
# friends: src/, docs/, tools/, .github/, other apps' sources), so a ProjectReference closure does
# not bound what a test can observe. Selecting by closure would be a guess, and this tool's job is
# to never guess. Everything that is not provably inert therefore runs the full matrix.

Set-StrictMode -Version Latest

function ConvertTo-CiImpactPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ''
    }
    return $Path.Trim().Replace('\', '/')
}

function Get-CiImpactSettings {
    param([string]$RepoRoot)

    $manifestPath = Join-Path $RepoRoot 'eng/test-gates.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if (@($manifest.PSObject.Properties.Name) -notcontains 'ciImpact') {
        throw "eng/test-gates.json has no 'ciImpact' section."
    }
    $section = $manifest.ciImpact
    $directories = @($section.inertDocsDirectories | ForEach-Object { [string]$_ })
    foreach ($directory in $directories) {
        if ($directory -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
            throw "ciImpact.inertDocsDirectories entry '$directory' must be a single directory name under docs/."
        }
    }
    return [pscustomobject]@{
        InertDocsDirectories = $directories
        InertPrefixes = @($directories | ForEach-Object { "docs/$_/" })
        Exemptions = @(if (@($section.PSObject.Properties.Name) -contains 'inertReferenceExemptions') { @($section.inertReferenceExemptions) })
        MaxAncestors = if (@($section.PSObject.Properties.Name) -contains 'maxAncestors') { [int]$section.maxAncestors } else { 50 }
    }
}

# True only for a normalized, plain repository-relative path that sits inside an inert directory.
# Anything odd (traversal, absolute, drive letters, quotes, control characters, empty) is NOT inert.
function Test-CiInertPath {
    param(
        [string]$Path,
        [string[]]$InertPrefixes
    )

    $normalized = ConvertTo-CiImpactPath $Path
    if ([string]::IsNullOrWhiteSpace($normalized)) { return $false }
    if ($normalized -match '[\x00-\x1f"''`:*?<>|]') { return $false }
    if ($normalized.StartsWith('/', [System.StringComparison]::Ordinal)) { return $false }
    foreach ($segment in $normalized.Split('/')) {
        if ($segment -eq '' -or $segment -eq '.' -or $segment -eq '..') { return $false }
    }
    foreach ($prefix in $InertPrefixes) {
        # Ordinal, case-sensitive on purpose: 'Docs/archive' is a different directory on Linux and
        # must not be waved through by a case-insensitive Windows habit.
        if ($normalized.StartsWith($prefix, [System.StringComparison]::Ordinal) -and $normalized.Length -gt $prefix.Length) {
            return $true
        }
    }
    return $false
}

function Get-CiImpactClassification {
    param(
        [AllowEmptyCollection()][string[]]$ChangedPaths = @(),
        [string[]]$InertPrefixes
    )

    $nonInert = @($ChangedPaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Where-Object { -not (Test-CiInertPath -Path $_ -InertPrefixes $InertPrefixes) })
    $mode = if ($nonInert.Count -eq 0) { 'inert' } else { 'full' }
    return [pscustomobject]@{
        Mode = $mode
        NonInertPaths = $nonInert
        ChangedPathCount = @($ChangedPaths).Count
    }
}

# Name of the evidence job in ci.yml. Release resolution parses it back with ConvertFrom-.
function Get-CiCoverageJobName {
    param([string]$Mode, [string]$Anchor = '')

    if ($Mode -eq 'inert') {
        return "CI coverage: inert anchor=$Anchor"
    }
    return 'CI coverage: full'
}

function ConvertFrom-CiCoverageJobName {
    param([string]$Name)

    if ($Name -match '^CI coverage: full$') {
        return [pscustomobject]@{ Mode = 'full'; Anchor = '' }
    }
    if ($Name -match '^CI coverage: inert anchor=([0-9a-f]{40})$') {
        return [pscustomobject]@{ Mode = 'inert'; Anchor = $Matches[1] }
    }
    return $null
}

# Reads the coverage claim recorded by one ci.yml run from its jobs listing.
# Returns Mode = full | inert | legacy | invalid. 'legacy' = no coverage job at all, i.e. a run made
# by the pre-selection workflow, which always ran the whole matrix.
function Get-CiRunCoverage {
    param([object]$JobsResponse)

    $jobs = @(@($JobsResponse.jobs) | Where-Object { $null -ne $_ })
    $coverageJobs = @($jobs | Where-Object { ([string]$_.name) -like 'CI coverage*' })
    if ($coverageJobs.Count -eq 0) {
        return [pscustomobject]@{ Mode = 'legacy'; Anchor = '' }
    }
    if ($coverageJobs.Count -ne 1) {
        return [pscustomobject]@{ Mode = 'invalid'; Anchor = '' }
    }
    $job = $coverageJobs[0]
    if ([string]$job.status -ne 'completed' -or [string]$job.conclusion -ne 'success') {
        return [pscustomobject]@{ Mode = 'invalid'; Anchor = '' }
    }
    $parsed = ConvertFrom-CiCoverageJobName ([string]$job.name)
    if ($null -eq $parsed) {
        return [pscustomobject]@{ Mode = 'invalid'; Anchor = '' }
    }
    return $parsed
}

# Decision core, pure so it can be table-tested without git or GitHub.
#   FindFullEvidence : { param($sha) } -> $null or object (the sha has a successful FULL ci run)
#   GetChangedPaths  : { param($anchor) } -> string[] of paths that differ between anchor and head
function Resolve-CiCoverageDecision {
    param(
        [string]$EventName,
        [bool]$ForceFull,
        [string[]]$Ancestors,
        [string[]]$InertPrefixes,
        [scriptblock]$FindFullEvidence,
        [scriptblock]$GetChangedPaths
    )

    function New-Decision([string]$Mode, [string]$Anchor, [string]$Reason, [string[]]$Paths) {
        return [pscustomobject]@{ Mode = $Mode; Anchor = $Anchor; Reason = $Reason; NonInertPaths = @($Paths) }
    }

    if ($EventName -ne 'push') {
        return New-Decision 'full' '' "event '$EventName' always runs the full matrix" @()
    }
    if ($ForceFull) {
        return New-Decision 'full' '' 'full coverage was requested' @()
    }
    if (@($Ancestors).Count -eq 0) {
        return New-Decision 'full' '' 'no ancestor commits are available (first push or shallow history)' @()
    }

    # Every failure to establish a fact is a decision for the FULL matrix, never a guess.
    $anchor = ''
    try {
        foreach ($candidate in $Ancestors) {
            $evidence = & $FindFullEvidence $candidate
            if ($null -ne $evidence) {
                $anchor = $candidate
                break
            }
        }
    }
    catch {
        return New-Decision 'full' '' "could not read ancestor run evidence ($($_.Exception.Message))" @()
    }
    if ([string]::IsNullOrEmpty($anchor)) {
        return New-Decision 'full' '' 'no ancestor has a successful full-coverage ci run (cancelled, failed or missing)' @()
    }

    try {
        $paths = @(& $GetChangedPaths $anchor)
    }
    catch {
        return New-Decision 'full' $anchor "could not compare against anchor $($anchor.Substring(0, 9)) ($($_.Exception.Message))" @()
    }
    $classification = Get-CiImpactClassification -ChangedPaths $paths -InertPrefixes $InertPrefixes
    if ($classification.Mode -eq 'inert') {
        return New-Decision 'inert' $anchor "only inert documentation differs from anchor $($anchor.Substring(0, 9)) ($($classification.ChangedPathCount) path(s))" @()
    }
    $first = $classification.NonInertPaths | Select-Object -First 1
    return New-Decision 'full' $anchor "$($classification.NonInertPaths.Count) non-inert path(s) differ from anchor $($anchor.Substring(0, 9)), e.g. $first" $classification.NonInertPaths
}

# Directories whose tests run ONLY in release-tier gates (eng/test-gates.json), never in a commit
# gate. A docs reference from those files cannot change a commit-gate outcome: release-tier gates
# are attested by release-readiness.yml and full-release.yml on their own SHAs, not by ci.yml.
# A directory only qualifies when no project, props or targets file outside it names its path
# (InternalsVisibleTo uses the dotted assembly name, which has no slash, so it does not count).
function Get-CiReleaseOnlyTestDirectories {
    param([string]$RepoRoot)

    $manifest = Get-Content -LiteralPath (Join-Path $RepoRoot 'eng/test-gates.json') -Raw | ConvertFrom-Json
    $commitDirectories = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $releaseDirectories = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($gate in @($manifest.gates)) {
        $projects = @($gate.projects)
        if (@($gate.PSObject.Properties.Name) -contains 'platformProjects') {
            foreach ($property in @($gate.platformProjects.PSObject.Properties)) {
                $projects += @($property.Value)
            }
        }
        foreach ($project in $projects) {
            $normalized = ConvertTo-CiImpactPath ([string]$project)
            $directory = $normalized.Substring(0, $normalized.LastIndexOf('/'))
            if ([string]$gate.gate -eq 'commit') {
                [void]$commitDirectories.Add($directory)
            }
            else {
                [void]$releaseDirectories.Add($directory)
            }
        }
    }

    # Fixed point: a release-only candidate is dropped when any project/props/targets file outside
    # it AND outside the remaining candidates names its path (e.g. a commit-gate project linking one
    # of its sources). Dropping one candidate can expose references from its own files, so repeat.
    $candidates = [System.Collections.Generic.List[string]]::new()
    foreach ($directory in $releaseDirectories) {
        if (-not $commitDirectories.Contains($directory)) { $candidates.Add($directory) }
    }
    $referenceCache = @{}
    foreach ($directory in $candidates) {
        $leaf = $directory.Substring($directory.LastIndexOf('/') + 1)
        $pattern = '[/' + [char]92 + ']' + [regex]::Escape($leaf) + '[/' + [char]92 + ']'
        $referenceCache[$directory] = @(& git -C $RepoRoot grep -l -I -E $pattern -- '*.csproj' '*.props' '*.targets')
    }
    do {
        $removed = $false
        foreach ($directory in @($candidates)) {
            $outside = @($referenceCache[$directory] | Where-Object {
                $referrer = $_
                -not $referrer.StartsWith("$directory/", [System.StringComparison]::Ordinal) -and
                -not ($candidates | Where-Object { $referrer.StartsWith("$_/", [System.StringComparison]::Ordinal) })
            })
            if ($outside.Count -gt 0) {
                [void]$candidates.Remove($directory)
                $removed = $true
            }
        }
    } while ($removed)
    return @($candidates)
}
# Mechanical proof that the inert list is true: no tracked non-documentation file that can affect a
# commit gate may refer to an inert docs directory. Returns offending (directory, file, reason)
# rows; an empty result means the proof holds.
function Remove-CiCommentLines {
    param([string]$Text, [string]$Path)

    $extension = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    $commentPattern = switch ($extension) {
        '.cs' { '^\s*(//|/\*|\*)' }
        { @('.mjs', '.js', '.ts') -contains $_ } { '^\s*(//|/\*|\*)' }
        { @('.ps1', '.psm1', '.sh', '.py', '.yml', '.yaml') -contains $_ } { '^\s*#' }
        default { $null }
    }
    if ($null -eq $commentPattern) {
        return $Text
    }
    return (($Text -split "`r?`n" | Where-Object { $_ -notmatch $commentPattern }) -join "`n")
}

# Exemptions are reviewed, named files whose reference to an inert directory is not a read that any
# gate performs (a generator that WRITES there and that no gate, preflight or test invokes). Each
# exemption must keep proving that: the exempt file's name may be mentioned only by the exempt
# files themselves (and docs), so wiring it into a gate invalidates the exemption.
function Test-CiInertExemptions {
    param(
        [string]$RepoRoot,
        [object[]]$Exemptions
    )

    $problems = [System.Collections.Generic.List[string]]::new()
    $exemptFiles = @($Exemptions | ForEach-Object { [string]$_.file })
    foreach ($exemption in $Exemptions) {
        $file = [string]$exemption.file
        if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $file) -PathType Leaf)) {
            $problems.Add("inert-reference exemption '$file' no longer exists; remove it from ciImpact.inertReferenceExemptions.")
            continue
        }
        $leaf = [System.IO.Path]::GetFileNameWithoutExtension($file)
        $stem = $leaf -replace '^Test-', ''
        $mentions = @(& git -C $RepoRoot grep -l -I -F -e $stem -- . ':!docs' ':!*.md' ':!eng/test-gates.json')
        foreach ($mention in $mentions) {
            if ($exemptFiles -notcontains $mention) {
                $problems.Add("'$mention' refers to exempt tool '$stem', so '$file' is no longer provably outside every gate.")
            }
        }
    }
    return @($problems)
}

function Test-CiInertDirectoryReferences {
    param(
        [string]$RepoRoot,
        [string[]]$InertDirectories,
        [object[]]$Exemptions = @()
    )

    $exemptFiles = @($Exemptions | ForEach-Object { [string]$_.file })
    $excluded = @(Get-CiReleaseOnlyTestDirectories -RepoRoot $RepoRoot | ForEach-Object { "$_/" })
    $findings = [System.Collections.Generic.List[object]]::new()

    # Phase 1: tracked text files outside docs/ and markdown that mention any inert directory name.
    $grepArguments = @('-C', $RepoRoot, 'grep', '-l', '-I', '-F')
    foreach ($directory in $InertDirectories) {
        $grepArguments += @('-e', $directory)
    }
    $grepArguments += @('--', '.', ':!docs', ':!*.md', ':!*.png', ':!*.svg', ':!eng/test-gates.json')
    $candidates = @(& git @grepArguments)
    if ($LASTEXITCODE -gt 1) {
        throw 'git grep failed while checking inert documentation directories.'
    }

    $quoteBoundary = '["''`]\s*(?:,|\+)?\s*["''`]'
    $docsToken = '["'']docs["'']'
    foreach ($relative in $candidates) {
        $skip = $false
        foreach ($prefix in $excluded) {
            if ($relative.StartsWith($prefix, [System.StringComparison]::Ordinal)) { $skip = $true; break }
        }
        if ($skip) { continue }
        if ($exemptFiles -contains $relative) { continue }
        $full = Join-Path $RepoRoot $relative
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }

        # A mention inside a comment is documentation, not a read.
        $text = Remove-CiCommentLines -Text ([System.IO.File]::ReadAllText($full)) -Path $relative
        # Fold the ways code spells a path into one canonical 'a/b' form: backslashes, and quote
        # boundaries between Path.Combine / Join-Path arguments (also across line breaks).
        $folded = $text.Replace('\', '/')
        $folded = [regex]::Replace($folded, $quoteBoundary, '/')
        foreach ($directory in $InertDirectories) {
            $escaped = [regex]::Escape($directory)
            if ($folded -match "docs/+$escaped(?![A-Za-z0-9_])") {
                $findings.Add([pscustomobject]@{ Directory = $directory; File = $relative; Reason = "refers to docs/$directory" })
                continue
            }
            # A file that names docs AND names the directory as a bare quoted path component
            # (Join-Path $docs 'archive') is treated the same, even if it is a false alarm.
            if ($text -match ('["''/\\]' + $escaped + '["''/\\]') -and $text -match $docsToken) {
                $findings.Add([pscustomobject]@{ Directory = $directory; File = $relative; Reason = "names docs and the bare component '$directory'" })
            }
        }
    }
    return @($findings)
}
