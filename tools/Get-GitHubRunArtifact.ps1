[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9]+$')]
    [string]$RunId,

    [Parameter(Mandatory = $true)]
    [string]$Pattern,

    [Parameter(Mandatory = $true)]
    [string]$Destination,

    [ValidateRange(1, 10)]
    [int]$MaxAttempts = 4,

    [ValidateRange(0, 300)]
    [int]$InitialDelaySeconds = 20,

    # Offline testing: the number of leading attempts that should fail, and whether the run should
    # ever succeed. Lets the backoff and the give-up path be exercised without GitHub.
    # Mirrors download-artifact merge-multiple: true. gh always creates one directory per
    # artifact, so a call site that expects the files flattened must say so, or the fallback
    # would hand packaging a different layout than the action it stands in for.
    [switch]$Flatten,

    [ValidateRange(-1, 10)]
    [int]$SimulateFailedAttempts = -1
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Why this exists.
#
# Release 0.8.187 finished every test gate, packaged all nine app bundles, and then lost three of
# its four Free Suite jobs inside two minutes to
#   "Unable to download artifact(s): Artifact download failed after 5 retries."
# Nothing was wrong with the build. actions/download-artifact was pulling three 100-144 MB artifacts
# concurrently while GitHub's artifact service was unhealthy, exhausted its own retries, and failed
# the job -- discarding roughly forty minutes of completed packaging behind it.
#
# download-artifact's internal retries all happen inside one step, over a few seconds, against the
# same unhealthy service. This is the outer loop it does not have: fewer, much later attempts
# through a different client, which is what actually survives a service blip lasting minutes.
#
# Used as a fallback after the action rather than replacing it: the action is faster and handles the
# common case, and a fallback that only runs on failure costs nothing when things are healthy.

function Merge-ArtifactDirectories {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [bool]$Enabled
    )

    if (-not $Enabled) { return }

    foreach ($file in @(Get-ChildItem -LiteralPath $Root -Recurse -File)) {
        $target = Join-Path $Root $file.Name
        if ($file.FullName -ne $target) {
            Move-Item -LiteralPath $file.FullName -Destination $target -Force
        }
    }

    Get-ChildItem -LiteralPath $Root -Directory | Remove-Item -Recurse -Force
}

function Get-AttemptDelaySeconds {
    param([Parameter(Mandatory = $true)][int]$Attempt)

    # Exponential: the outage that motivated this lasted minutes, so the useful attempts are the
    # late ones. 20s, 40s, 80s.
    return $InitialDelaySeconds * [math]::Pow(2, $Attempt - 1)
}

if (-not (Test-Path -LiteralPath $Destination)) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
}

$lastError = ""
for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    if ($attempt -gt 1) {
        $delay = Get-AttemptDelaySeconds -Attempt ($attempt - 1)
        Write-Host "Waiting $delay second(s) before attempt $attempt of $MaxAttempts."
        Start-Sleep -Seconds $delay
    }

    # A failed attempt can leave a half-extracted artifact behind, and a partial tree is worse than
    # an empty one: the packaging step would read it as real content.
    Get-ChildItem -LiteralPath $Destination -Force -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    if ($SimulateFailedAttempts -ge 0) {
        if ($attempt -le $SimulateFailedAttempts) {
            $lastError = "simulated artifact service failure"
            Write-Host "Attempt $attempt failed: $lastError"
            continue
        }

        Merge-ArtifactDirectories -Root $Destination -Enabled:$Flatten
        Write-Host "Downloaded artifacts matching '$Pattern' on attempt $attempt."
        return
    }

    $output = & gh run download $RunId --repo $Repository --pattern $Pattern --dir $Destination 2>&1
    if ($LASTEXITCODE -eq 0) {
        $downloaded = @(Get-ChildItem -LiteralPath $Destination -Force -ErrorAction SilentlyContinue)
        if ($downloaded.Count -eq 0) {
            # gh reports success when nothing matched. For a release that is a missing upstream
            # package, not a healthy download, and it must not be mistaken for one.
            throw ("No artifact matched '$Pattern' in run $RunId of $Repository. The packaging job " +
                "that produces it did not upload anything, so this is a missing package rather than " +
                "a transient download failure; retrying cannot help.")
        }

        Merge-ArtifactDirectories -Root $Destination -Enabled:$Flatten

        Write-Host "Downloaded $($downloaded.Count) artifact(s) matching '$Pattern' on attempt $attempt."
        return
    }

    $lastError = ($output -join [Environment]::NewLine)
    Write-Host "Attempt $attempt failed: $lastError"
}

throw ("Could not download artifacts matching '$Pattern' from run $RunId after $MaxAttempts attempts " +
    "spread over several minutes. GitHub's artifact service is most likely unhealthy; the packaged " +
    "outputs themselves are intact and re-running the failed jobs will reuse them. Last error: $lastError")
