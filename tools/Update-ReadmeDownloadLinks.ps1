[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$Version,

    # Date shown after the version in the Downloads sentence, as yyyy-MM-dd. Defaults to today (UTC).
    [ValidatePattern('^[0-9]{4}-[0-9]{2}-[0-9]{2}$')]
    [string]$ReleaseDate = '',

    [string]$ReadmePath = "README.md"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Rewrites every versioned download/tag link and the "current tester release" sentence in the README
# Downloads section to one published version. tools/Test-ReadmeDownloadFreshness.ps1 (release-readiness)
# fails when the README lags the newest published freex-v* release; run this after a release is
# published, review the diff, and land it like any other change. It is intentionally not wired into
# the release workflow, which has no business pushing commits to main.

if (-not (Test-Path -LiteralPath $ReadmePath -PathType Leaf)) {
    throw "README not found: $ReadmePath"
}

if ([string]::IsNullOrWhiteSpace($ReleaseDate)) {
    $ReleaseDate = [DateTime]::UtcNow.ToString('yyyy-MM-dd')
}

$readme = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $ReadmePath).Path)
$versionPattern = '[0-9]+\.[0-9]+\.[0-9]+'

$updated = $readme
# Tags and asset names: freex-v0.8.1, FreeX-v0.8.1-..., free-suite-v0.8.1, link text "FreeW v0.8.1".
$updated = [regex]::Replace($updated, "(?<=(?i:free(?:x|w|p|-suite))-v)$versionPattern", $Version)
$updated = [regex]::Replace($updated, "(?<=\[Free(?:X|W|P|\sSuite)\sv)$versionPattern", $Version)
$updated = [regex]::Replace($updated, "(?<=FreeX\sv)$versionPattern(?=\sdownloads)", $Version)
$updated = [regex]::Replace(
    $updated,
    "(current tester release is \*\*v)$versionPattern(\*\*\s\()[0-9]{4}-[0-9]{2}-[0-9]{2}(\))",
    "`${1}$Version`${2}$ReleaseDate`${3}")

if ($updated -ceq $readme) {
    Write-Host "$ReadmePath already advertises v$Version."
    return
}

[System.IO.File]::WriteAllText((Resolve-Path -LiteralPath $ReadmePath).Path, $updated, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "$ReadmePath now advertises v$Version ($ReleaseDate)."
