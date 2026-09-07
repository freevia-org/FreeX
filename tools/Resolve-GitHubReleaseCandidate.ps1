[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$CommitSha,

    [ValidateRange(1, 250)]
    [int]$MaxAncestors = 50,

    [string[]]$RequiredWorkflows = @('ci.yml', 'codeql.yml'),

    [string]$AncestorMetadataPath = '',

    # Offline testing: a directory holding one subdirectory per commit sha, each in the layout
    # Test-GitHubReleaseCandidate.ps1 expects (<workflow>.json). Lets the ancestor walk be exercised
    # without reaching GitHub.
    [string]$RunMetadataRoot = ''
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Releases must be built from a commit that CI and CodeQL both attested. Requiring that of the
# EXACT dispatch head does not work on this repository: main takes roughly a commit a minute from
# parallel sessions, codeql.yml runs for about ten minutes with cancel-in-progress, and so the next
# push usually cancels it. Measured over twelve consecutive runs, codeql completed on seven -- but
# almost never on the head that a release happened to dispatch from. Three consecutive release
# attempts were rejected for exactly this reason while the code itself was fine.
#
# So instead of demanding the head be attested, walk back from it and take the NEWEST ancestor that
# is. The attestation itself is unchanged -- Test-GitHubReleaseCandidate.ps1 still requires a
# completed, successful run of every required workflow for whichever commit is chosen.
#
# What this does NOT do is make the release fully attested: the workflow still builds the dispatch
# head, so any commit between the attested ancestor and the head ships without a green CI run of its
# own. That is a deliberate trade and it is reported loudly below rather than hidden, so the gap is
# visible in the release log and can be judged per release.

$normalizedSha = $CommitSha.ToLowerInvariant()

function Get-AncestorShas {
    if (-not [string]::IsNullOrWhiteSpace($AncestorMetadataPath)) {
        if (-not (Test-Path -LiteralPath $AncestorMetadataPath -PathType Leaf)) {
            throw "Offline ancestor metadata was not found: $AncestorMetadataPath"
        }
        $text = Get-Content -LiteralPath $AncestorMetadataPath -Raw
    }
    else {
        # The candidate job checks out at depth 1, so local git cannot enumerate history. Ask the
        # API instead; it returns the commit and its ancestors, newest first.
        $text = & gh api "repos/$Repository/commits?sha=$normalizedSha&per_page=$MaxAncestors"
        if ($LASTEXITCODE -ne 0) {
            throw "Could not list ancestors of $normalizedSha in $Repository."
        }
    }

    try {
        $parsed = $text | ConvertFrom-Json
    }
    catch {
        throw "Ancestor metadata is not valid JSON: $($_.Exception.Message)"
    }

    $shas = @(@($parsed) | ForEach-Object { ([string]$_.sha).ToLowerInvariant() } | Where-Object { $_ -match '^[0-9a-f]{40}$' })
    if ($shas.Count -eq 0) {
        throw "No ancestors were returned for $normalizedSha."
    }
    if ($shas[0] -ne $normalizedSha) {
        throw "Ancestor listing did not start at $normalizedSha; refusing to attest against an unrelated history."
    }

    return $shas
}

$ancestors = Get-AncestorShas
$candidateScript = Join-Path $PSScriptRoot "Test-GitHubReleaseCandidate.ps1"
$attempts = [System.Collections.Generic.List[string]]::new()

for ($index = 0; $index -lt $ancestors.Count; $index++) {
    $candidate = $ancestors[$index]
    $failure = $null
    try {
        $candidateArguments = @{
            Repository = $Repository
            CommitSha = $candidate
            RequiredWorkflows = $RequiredWorkflows
        }
        if (-not [string]::IsNullOrWhiteSpace($RunMetadataRoot)) {
            $candidateArguments['RunMetadataDirectory'] = Join-Path $RunMetadataRoot $candidate
        }
        & $candidateScript @candidateArguments | Out-Null
    }
    catch {
        $failure = $_.Exception.Message
    }

    if ($null -ne $failure) {
        $attempts.Add("  $($candidate.Substring(0, 9)): $failure")
        continue
    }

    $behind = $index
    if ($behind -eq 0) {
        Write-Host "Release candidate $($candidate.Substring(0, 9)) is attested by every required workflow."
    }
    else {
        $unattested = $ancestors[0..($behind - 1)]
        Write-Warning ("Release head $($normalizedSha.Substring(0, 9)) is NOT attested. Using the newest attested " +
            "ancestor $($candidate.Substring(0, 9)), which leaves $behind commit(s) in this release without a " +
            "green run of their own:")
        foreach ($sha in $unattested) {
            Write-Warning "    $($sha.Substring(0, 9))"
        }
    }

    if (-not [string]::IsNullOrEmpty($env:GITHUB_OUTPUT)) {
        "attested_sha=$candidate" | Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8
        "unattested_commits=$behind" | Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8
    }

    [pscustomobject]@{
        repository = $Repository
        releaseSha = $normalizedSha
        attestedSha = $candidate
        unattestedCommits = $behind
    } | ConvertTo-Json -Depth 3
    return
}

throw ("No commit among $($ancestors.Count) ancestor(s) of $normalizedSha has a successful run of every " +
    "required workflow ($($RequiredWorkflows -join ', ')). Attempts:`n" + ($attempts -join "`n"))
