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

# The repository history was rewritten twice on 2026-11-12/13 (screenshots and evidence, then personal data
# and generated reports). Each marker commit exists only in a superseded history; a branch that still
# contains one was based on that history and would re-publish purged content when merged or pushed, even if
# its current tree is clean.
$preRewriteCommits = @(
    "ed45d43fe7486bebeaa78dbfc286b43d6cd5d267",
    "e17caa8ebd567874eb5872de1fdf6ba2ff508873"
)
foreach ($preRewriteCommit in $preRewriteCommits) {
    $knownCommit = @(& git -C $RepoRoot rev-parse -q --verify "$preRewriteCommit^{commit}")
    if ($knownCommit.Count -gt 0) {
        & git -C $RepoRoot merge-base --is-ancestor $preRewriteCommit HEAD
        if ($LASTEXITCODE -eq 0) {
            throw ("HEAD still carries history from before the 2026-11 rewrites, which contains purged personal data, " +
                "screenshots and evidence. Do not merge or push it. Re-create the branch from the current origin/main " +
                "and cherry-pick only your own commits onto it (see AGENTS.md).")
        }
    }
}

Write-Host "No local-only evidence paths are tracked."
