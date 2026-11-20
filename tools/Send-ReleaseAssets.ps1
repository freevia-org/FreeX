[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,

    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [Parameter(Mandatory = $true)]
    [string[]]$AssetPaths,

    # Offline testing: a file holding the release JSON (as GET releases/tags/<tag> returns it) replaces
    # the query, and -PlanOnly returns the plan without uploading, deleting or retrying.
    [string]$ExistingReleaseJsonPath = '',
    [switch]$PlanOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'Invoke-WithRetry.ps1')

# Why this exists.
#
# The publish jobs used `gh release upload $tag @assets --clobber` for an already-existing release.
# That is how a re-run resumes a partial publish, but --clobber replaces EVERY asset whether or not it
# changed, so a second publication of the same version silently overwrote assets that users may
# already have downloaded and verified against their .sha256 files.
#
# This resumes without that: it uploads only what is missing, leaves identical assets alone, and
# REFUSES (before uploading anything) when a published asset has the same name but different content,
# because a version's assets are immutable. The one exception is an asset GitHub itself reports as
# not fully uploaded (state other than 'uploaded', or zero bytes): that is the debris of an
# interrupted upload, not a published artifact, so it is deleted and uploaded again.
#
# The plan is rebuilt from the live release on every attempt, so a retry after a partial upload
# uploads only the remainder instead of replaying a stale list.

function Get-LocalAsset([string]$Path) {
    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    return [pscustomobject]@{
        Path = $item.FullName
        Name = $item.Name
        Size = [long]$item.Length
        Sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Get-ReleaseAssets {
    if (-not [string]::IsNullOrWhiteSpace($ExistingReleaseJsonPath)) {
        $text = Get-Content -LiteralPath $ExistingReleaseJsonPath -Raw
    }
    else {
        $text = Invoke-WithRetry -Description "Reading release '$Tag'" -Action {
            gh api --method GET "repos/$Repository/releases/tags/$Tag"
        }
        $text = $text -join "`n"
    }

    $release = $text | ConvertFrom-Json
    return @(@($release.assets) | Where-Object { $null -ne $_ })
}

function New-Plan([object[]]$LocalAssets) {
    $remote = @(Get-ReleaseAssets)
    $plan = foreach ($local in $LocalAssets) {
        $existing = @($remote | Where-Object { [string]$_.name -ceq $local.Name })
        if ($existing.Count -eq 0) {
            [pscustomobject]@{ Action = 'upload'; Name = $local.Name; Path = $local.Path; AssetId = ''; Detail = 'not on the release yet' }
            continue
        }

        $asset = $existing[0]
        $digest = if ($asset.PSObject.Properties.Name -contains 'digest') { [string]$asset.digest } else { '' }
        if ([string]$asset.state -ne 'uploaded' -or [long]$asset.size -le 0) {
            [pscustomobject]@{ Action = 'replace'; Name = $local.Name; Path = $local.Path; AssetId = [string]$asset.id; Detail = "interrupted upload (state '$($asset.state)', $($asset.size) bytes)" }
        }
        elseif ($digest -match '^sha256:(?<hash>[0-9a-fA-F]{64})$') {
            if ($Matches['hash'].ToLowerInvariant() -eq $local.Sha256) {
                [pscustomobject]@{ Action = 'skip'; Name = $local.Name; Path = $local.Path; AssetId = ''; Detail = 'identical sha256 already published' }
            }
            else {
                [pscustomobject]@{ Action = 'conflict'; Name = $local.Name; Path = $local.Path; AssetId = ''; Detail = "published sha256 $($Matches['hash'].ToLowerInvariant()) differs from local $($local.Sha256)" }
            }
        }
        elseif ([long]$asset.size -eq $local.Size) {
            [pscustomobject]@{ Action = 'skip'; Name = $local.Name; Path = $local.Path; AssetId = ''; Detail = 'same size already published (the release lists no digest)' }
        }
        else {
            [pscustomobject]@{ Action = 'conflict'; Name = $local.Name; Path = $local.Path; AssetId = ''; Detail = "published size $($asset.size) differs from local $($local.Size)" }
        }
    }

    return @($plan)
}

$local = @($AssetPaths | ForEach-Object { Get-LocalAsset $_ })
$duplicates = @($local | Group-Object Name | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
if ($duplicates.Count -gt 0) {
    throw "Two local files would publish under the same asset name: $($duplicates -join ', ')."
}

if ($PlanOnly) {
    return (New-Plan $local)
}

for ($attempt = 1; $attempt -le 3; $attempt++) {
    $plan = New-Plan $local
    $conflicts = @($plan | Where-Object { $_.Action -eq 'conflict' })
    if ($conflicts.Count -gt 0) {
        # Never retried and never overwritten: the version's published assets are immutable.
        throw ("Release '$Tag' already has published asset(s) with different content; refusing to overwrite them. " +
            "Use a new version. " + (($conflicts | ForEach-Object { "$($_.Name): $($_.Detail)" }) -join '; '))
    }

    foreach ($stale in @($plan | Where-Object { $_.Action -eq 'replace' })) {
        Write-Host "Removing interrupted upload '$($stale.Name)': $($stale.Detail)."
        gh api --method DELETE "repos/$Repository/releases/assets/$($stale.AssetId)" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not remove the interrupted asset '$($stale.Name)'." }
    }

    foreach ($skipped in @($plan | Where-Object { $_.Action -eq 'skip' })) {
        Write-Host "Keeping '$($skipped.Name)': $($skipped.Detail)."
    }

    $toUpload = @($plan | Where-Object { $_.Action -in @('upload', 'replace') })
    if ($toUpload.Count -eq 0) {
        Write-Host "Release '$Tag' already holds every asset."
        return
    }

    Write-Host "Uploading $($toUpload.Count) asset(s) to '$Tag' (attempt $attempt of 3)."
    $uploadPaths = @($toUpload | ForEach-Object { $_.Path })
    gh release upload $Tag @uploadPaths --repo $Repository
    if ($LASTEXITCODE -eq 0) { return }

    Write-Warning "Upload attempt $attempt failed with exit code $LASTEXITCODE."
    if ($attempt -lt 3) { Start-Sleep -Seconds (10 * $attempt) }
}

throw "Could not upload the assets of release '$Tag' after 3 attempts; re-running the job resumes from what is already published."
