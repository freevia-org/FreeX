param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"

# Screenshots, capture manifests and raw parity evidence are local-only: they are produced by the
# capture tools, are not material to the code, and were purged from the repository history. The
# .gitignore entries keep them out of a normal `git add`, but `git add -f` or merging a branch that
# still carries them would bring them back, so this check fails when any such path is tracked.
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}

$localOnlyPathspecs = @(
    "screenshots",
    "screenshots_excel",
    "tools/screenshots",
    "tools/screenshots_excel",
    "tools/screenshots_avalonia_ribbon",
    "tools/foreground-captures",
    "artifacts",
    ".metrics-data",
    "docs/ribbonshot-breaks",
    "docs/parity/*/*",
    "freew/docs/parity/evidence"
)

$tracked = @(& git -C $RepoRoot ls-files -- @localOnlyPathspecs)
if ($LASTEXITCODE -ne 0) {
    throw "git ls-files failed with exit code $LASTEXITCODE while checking local-only evidence paths."
}

if ($tracked.Count -gt 0) {
    $sample = ($tracked | Select-Object -First 10) -join "`n - "
    throw ("$($tracked.Count) local-only evidence file(s) are tracked. Screenshots, capture manifests and raw " +
        "parity evidence must stay out of Git (see .gitignore); remove them with 'git rm --cached'.`n - $sample")
}

Write-Host "No local-only evidence paths are tracked."
