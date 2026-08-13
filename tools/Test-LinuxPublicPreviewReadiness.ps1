param(
    [string]$ArtifactRoot = "artifacts",
    [string[]]$Runtimes = @("linux-x64", "linux-arm64"),
    [string]$ExpectedRunId,
    [string]$ExpectedRunAttempt,
    [string]$ManifestPath = "artifacts/linux-preview-readiness-manifest.json"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$validationErrors = New-Object System.Collections.Generic.List[string]

. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Add-ValidationError {
    param([Parameter(Mandatory = $true)][string]$Message)

    Add-ToolValidationError -Errors $validationErrors -Message $Message -GitHubTitle "Linux public-preview readiness"
}

function Assert-True {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        Add-ValidationError $Message
    }
}

function Get-EvidenceMap {
    param([Parameter(Mandatory = $true)][string]$EvidencePath)

    $map = @{}
    foreach ($line in Get-Content -LiteralPath $EvidencePath) {
        $separator = $line.IndexOf("=")
        if ($separator -lt 1) { continue }
        $key = $line.Substring(0, $separator)
        $value = $line.Substring($separator + 1)
        # Last writer wins so terminal status markers override probe defaults.
        $map[$key] = $value
    }

    return $map
}

$root = Resolve-InputPath -Path $ArtifactRoot -RepoRoot $repoRoot
Assert-True (Test-Path -LiteralPath $root) "Artifact root '$root' was not found."

$runtimeManifests = New-Object System.Collections.Generic.List[object]

foreach ($runtime in $Runtimes) {
    $evidenceName = "freex-$runtime-linux-evidence.txt"
    $evidenceFile = Get-ChildItem -LiteralPath $root -Recurse -File -Filter $evidenceName -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if ($null -eq $evidenceFile) {
        Add-ValidationError "Evidence file '$evidenceName' was not found under '$root' for runtime '$runtime'."
        continue
    }

    $evidence = Get-EvidenceMap -EvidencePath $evidenceFile.FullName
    $artifactDir = $evidenceFile.Directory.FullName

    Assert-True ($evidence["artifact_channel"] -eq "internal-preview") "[$runtime] artifact_channel must be internal-preview."
    Assert-True ($evidence["runtime"] -eq $runtime) "[$runtime] evidence runtime marker mismatch."
    Assert-True ($evidence["packaging_smoke_status"] -eq "passed") "[$runtime] packaging_smoke_status must be passed."
    Assert-True ($evidence["launch_smoke_status"] -eq "passed") "[$runtime] launch_smoke_status must be passed."
    Assert-True ($evidence["format_cells_style_roundtrip"] -eq "true") "[$runtime] format_cells_style_roundtrip must be true."
    Assert-True ($evidence["desktop_entry_valid"] -eq "true") "[$runtime] desktop_entry_valid must be true."
    Assert-True ($evidence["mime_definition_present"] -eq "true") "[$runtime] mime_definition_present must be true."
    Assert-True ($evidence["icon_present"] -eq "true") "[$runtime] icon_present must be true."
    Assert-True ($evidence["app_diagnostics_events_jsonl"] -eq "true") "[$runtime] app_diagnostics_events_jsonl must be true."

    $roundtripCount = 0
    [void][int]::TryParse($evidence["format_cells_style_roundtrip_count"], [ref]$roundtripCount)
    Assert-True ($roundtripCount -ge 2) "[$runtime] format_cells_style_roundtrip_count must be >= 2."

    if ($ExpectedRunId) {
        Assert-True ($evidence["github_run_id"] -eq $ExpectedRunId) "[$runtime] github_run_id mismatch (expected $ExpectedRunId)."
    }
    if ($ExpectedRunAttempt) {
        Assert-True ($evidence["github_run_attempt"] -eq $ExpectedRunAttempt) "[$runtime] github_run_attempt mismatch (expected $ExpectedRunAttempt)."
    }

    $tarballName = $evidence["tarball_name"]
    $tarballSha256 = $null
    if ([string]::IsNullOrWhiteSpace($tarballName)) {
        Add-ValidationError "[$runtime] tarball_name marker is missing."
    }
    else {
        $tarballPath = Join-Path $artifactDir $tarballName
        $checksumPath = "$tarballPath.sha256"
        Assert-True (Test-Path -LiteralPath $tarballPath) "[$runtime] tarball '$tarballName' was not found next to the evidence file."
        Assert-True (Test-Path -LiteralPath $checksumPath) "[$runtime] checksum '$tarballName.sha256' was not found."

        if ((Test-Path -LiteralPath $tarballPath) -and (Test-Path -LiteralPath $checksumPath)) {
            $expectedHash = ((Get-Content -LiteralPath $checksumPath -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
            $actualHash = (Get-FileHash -LiteralPath $tarballPath -Algorithm SHA256).Hash.ToLowerInvariant()
            Assert-True ($expectedHash -eq $actualHash) "[$runtime] tarball checksum does not match recomputed SHA-256."
            Assert-True ($evidence["tarball_sha256"] -eq $actualHash) "[$runtime] evidence tarball_sha256 does not match the tarball."
            $tarballSha256 = $actualHash
        }
    }

    $runtimeManifests.Add([ordered]@{
        runtime = $runtime
        artifact_channel = $evidence["artifact_channel"]
        packaging_smoke_status = $evidence["packaging_smoke_status"]
        launch_smoke_status = $evidence["launch_smoke_status"]
        format_cells_style_roundtrip_count = $roundtripCount
        appimage_status = $evidence["appimage_status"]
        tarball_name = $tarballName
        tarball_sha256 = $tarballSha256
        evidence_file = (Resolve-Path -LiteralPath $evidenceFile.FullName -Relative)
    })
}

$manifest = [ordered]@{
    schema = "io.github.tony-xmelon.freex.linux-preview-readiness.v1"
    repository = $env:GITHUB_REPOSITORY
    workflow = $env:GITHUB_WORKFLOW
    run_id = $env:GITHUB_RUN_ID
    run_attempt = $env:GITHUB_RUN_ATTEMPT
    commit = $env:GITHUB_SHA
    runtimes = $runtimeManifests
    status = if ($validationErrors.Count -eq 0) { "ready" } else { "blocked" }
}

$manifestFull = Resolve-InputPath -Path $ManifestPath -RepoRoot $repoRoot
$manifestDir = Split-Path -Parent $manifestFull
if (-not (Test-Path -LiteralPath $manifestDir)) {
    New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestFull -Encoding ascii

if ($validationErrors.Count -gt 0) {
    Write-Host ""
    Write-Host "Linux public-preview readiness FAILED with $($validationErrors.Count) issue(s)."
    exit 1
}

Write-Host "Linux public-preview readiness PASSED for runtimes: $($Runtimes -join ', ')."
exit 0
