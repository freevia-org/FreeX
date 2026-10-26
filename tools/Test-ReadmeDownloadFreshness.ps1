[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,

    [string]$ReadmePath = "README.md",

    # Offline testing: a JSON file shaped like the gh releases listing, so both the fresh and the
    # stale branch can be exercised without reaching GitHub.
    [string]$ReleaseMetadataPath = ''
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Why this exists.
#
# README.md advertises a specific tester version and links directly to that version's assets. Those
# links are the first thing a new tester follows, and nothing in the repository updates them: a
# release publishes new tags and the README keeps pointing at the old ones. It had drifted seventeen
# releases behind (v0.8.170 while 0.8.186 was current) without any check noticing, because every
# link still resolved -- to a stale build.
#
# This is deliberately not a merge gate. It needs the network and it compares against state outside
# the repository, so a push should never fail on it. It runs on the release-readiness schedule
# instead, which is the same reasoning as the release-only gates that workflow already covers:
# report drift within hours, against a small commit range, rather than at the next release.

if (-not (Test-Path -LiteralPath $ReadmePath -PathType Leaf)) {
    throw "README not found: $ReadmePath"
}

$readme = Get-Content -LiteralPath $ReadmePath -Raw

# The advertised version is whatever the download links actually point at, not the prose. Prose can
# be updated while a link is missed, and the link is the thing a tester follows.
$linkedVersions = @([regex]::Matches($readme, 'releases/(?:download|tag)/(?:freex|freew|freep|free-suite)-v(?<version>[0-9]+\.[0-9]+\.[0-9]+)') |
    ForEach-Object { $_.Groups['version'].Value } |
    Sort-Object -Unique)

if ($linkedVersions.Count -eq 0) {
    throw "$ReadmePath no longer links to any versioned release asset, so testers have no pinned download. Restore the per-version links."
}

if ($linkedVersions.Count -gt 1) {
    $versionList = $linkedVersions -join ", "
    throw ("$ReadmePath links to more than one release version ($versionList), so some links are left " +
        "over from an earlier release. Point every download and tag link at one version.")
}

$advertised = $linkedVersions[0]

if ([string]::IsNullOrWhiteSpace($ReleaseMetadataPath)) {
    $releasesText = & gh api --method GET "repos/$Repository/releases" -f "per_page=100"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list releases for $Repository."
    }
}
else {
    $releasesText = Get-Content -LiteralPath $ReleaseMetadataPath -Raw
}

$published = @($releasesText | ConvertFrom-Json | Where-Object { -not $_.draft })
$freexVersions = @($published |
    ForEach-Object { [regex]::Match($_.tag_name, '^freex-v(?<version>[0-9]+\.[0-9]+\.[0-9]+)$') } |
    Where-Object { $_.Success } |
    ForEach-Object { [version]$_.Groups['version'].Value } |
    Sort-Object -Descending)

if ($freexVersions.Count -eq 0) {
    throw "No published freex-v* release found in $Repository, so README freshness cannot be judged."
}

$newest = $freexVersions[0].ToString()

if ($newest -ne $advertised) {
    throw ("README.md still sends testers to v$advertised while v$newest is published. Update the " +
        "Downloads section -- the stale links still resolve, which is exactly why this goes unnoticed.")
}

Write-Host "README.md advertises v$advertised, which is the newest published FreeX release."
