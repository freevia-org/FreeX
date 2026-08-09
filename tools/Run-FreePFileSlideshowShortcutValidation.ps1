<##
.SYNOPSIS
  Runs the dedicated physical FreeP file and slideshow shortcut evidence lane.

.DESCRIPTION
  Starts one harness-owned FreeP Linux desktop through the generic interactive
  runner, passes the two-slide comments/notes corpus through -DocumentPath, and
  validates the probe's ten-row physical X11 contract. This is intentionally a
  non-exhaustive lane and does not alter the family baseline contract.
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$Port = 6092,
    [ValidateRange(640, 7680)][int]$Width = 1280,
    [ValidateRange(480, 4320)][int]$Height = 820,
    [ValidateRange(72, 240)][int]$Dpi = 96,
    [ValidateSet("2g", "4g", "6g", "8g")][string]$MemoryLimit = "4g",
    [string]$OutputDir = "artifacts/linux-freep-file-slideshow-shortcuts",
    [string]$PublishDir = "",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$Replace,
    [switch]$KeepContainer
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "VisualEvidenceScriptSupport.ps1")
$resolvedOutputRoot = Resolve-VisualEvidenceOutputDirectory -OutputDirectory $OutputDir -RepoRoot $repoRoot
$fixturePath = Join-Path $repoRoot "tools/FreeP.RenderCompare/corpus/21-comments-notes.pptx"
$fixtureFileName = Split-Path -Leaf $fixturePath
$genericRunner = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$probeSource = Join-Path $PSScriptRoot "LinuxInteractiveDocker/run-freep-file-slideshow-shortcut-probe.sh"
$schemaPath = Join-Path $PSScriptRoot "LinuxInteractiveDocker/freep-file-slideshow-shortcut-validation.schema.json"
$manifestEvidenceHelper = Join-Path $PSScriptRoot "LinuxInteractiveDocker/ManifestEvidence.ps1"
$null = . $manifestEvidenceHelper

$requiredIds = @(
    "visible-window-discovery",
    "file-new-shortcut-lifecycle",
    "file-open-shortcut-lifecycle",
    "file-save-shortcut-current-path",
    "file-save-as-shortcut-lifecycle",
    "print-shortcut-backstage-lifecycle",
    "slideshow-from-beginning-lifecycle",
    "slideshow-from-current-lifecycle",
    "find-shortcut-lifecycle",
    "replace-shortcut-lifecycle"
)

function Assert-ManifestContract {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$EvidenceDirectory
    )

    if (-not (Test-Path -LiteralPath $schemaPath -PathType Leaf)) {
        throw "Manifest schema is missing: $schemaPath"
    }
    $schema = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json
    if ($schema.'$schema' -notmatch "json-schema.org") {
        throw "Manifest contract reference is not a JSON Schema document."
    }

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ($manifest.contractValidation.status -ne "pending") {
        throw "Probe must leave contractValidation pending until the runner passes strict validation."
    }
    $expectedScope = "physical FreeP file/slideshow shortcut evidence lane"
    if ($manifest.schemaVersion -ne 1 -or
        $manifest.suite -ne "freep-linux-file-slideshow-shortcut-physical" -or
        $manifest.platform -ne "linux" -or
        $manifest.shell -ne "avalonia" -or
        $manifest.app -ne "FreeP" -or
        $manifest.baseline -ne $false -or
        $manifest.appSurface -ne "document-editor-file-slideshow-shortcuts" -or
        $manifest.coverage.exhaustive -ne $false -or
        $manifest.coverage.scope -ne $expectedScope -or
        $manifest.window.pattern -ne $fixtureFileName -or
        $manifest.window.visible -ne $true -or
        ([string]$manifest.window.title).IndexOf($fixtureFileName, [StringComparison]::Ordinal) -lt 0 -or
        ([string]$manifest.window.title).IndexOf("FreeP", [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "FreeP file/slideshow shortcut manifest header does not satisfy its dedicated contract."
    }

    $results = @($manifest.results)
    $ids = @($results | ForEach-Object { [string]$_.id })
    if ($results.Count -ne $requiredIds.Count -or
        $ids.Count -ne ($ids | Select-Object -Unique).Count) {
        throw "FreeP file/slideshow shortcut manifest must contain exactly ten unique result rows."
    }
    foreach ($requiredId in $requiredIds) {
        if ($ids -notcontains $requiredId) {
            throw "Manifest is missing required result '$requiredId'."
        }
    }
    $unexpectedIds = @($ids | Where-Object { $requiredIds -notcontains $_ })
    if ($unexpectedIds.Count -gt 0) {
        throw "Manifest contains unexpected result ID(s): $([string]::Join(', ', $unexpectedIds))."
    }

    $passed = @($results | Where-Object { $_.status -eq "passed" }).Count
    $failed = @($results | Where-Object { $_.status -eq "failed" }).Count
    if ($manifest.summary.total -ne 10 -or
        $manifest.summary.passed -ne $passed -or
        $manifest.summary.failed -ne $failed -or
        ($passed + $failed) -ne 10) {
        throw "Manifest summary does not match its ten result rows."
    }

    $fileMap = Get-ManifestEvidenceFileMap -EvidenceDirectory $EvidenceDirectory
    foreach ($result in $results) {
        if ($result.category -ne "physical-x11-file-slideshow-shortcut" -or
            $result.evidenceLevel -ne "physical-x11-input" -or
            @($result.evidence).Count -lt 1 -or
            [string]::IsNullOrWhiteSpace([string]$result.note)) {
            throw "Result '$($result.id)' is missing physical evidence metadata."
        }
        foreach ($evidence in @($result.evidence)) {
            $name = [string]$evidence
            if ([string]::IsNullOrWhiteSpace($name) -or
                [IO.Path]::IsPathRooted($name) -or
                [IO.Path]::GetFileName($name) -ne $name -or
                $name.Contains("/") -or $name.Contains("\") -or
                -not $fileMap.ContainsKey($name) -or $fileMap[$name].Length -le 0) {
                throw "Result '$($result.id)' references missing, empty, or non-basename evidence '$name'."
            }
        }
    }
    foreach ($screenshot in @($manifest.screenshots)) {
        $name = [string]$screenshot.name
        if ($screenshot.kind -ne "screenshot" -or
            [string]::IsNullOrWhiteSpace($name) -or
            [IO.Path]::IsPathRooted($name) -or
            [IO.Path]::GetFileName($name) -ne $name -or
            $name.Contains("/") -or $name.Contains("\") -or
            -not $fileMap.ContainsKey($name) -or $fileMap[$name].Length -le 0) {
            throw "Manifest references missing, empty, or non-basename screenshot '$name'."
        }
    }

    $manifest | Add-Member -NotePropertyName contractValidation -NotePropertyValue ([pscustomobject]@{
            status = "passed"
            validator = "tools/Run-FreePFileSlideshowShortcutValidation.ps1"
            contractReference = "tools/LinuxInteractiveDocker/freep-file-slideshow-shortcut-validation.schema.json"
        }) -Force
    $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $ManifestPath -Encoding utf8
    return $manifest
}

if (-not (Test-Path -LiteralPath $fixturePath -PathType Leaf)) {
    throw "Fixture was not found: $fixturePath"
}
if (-not (Test-Path -LiteralPath $probeSource -PathType Leaf)) {
    throw "Probe is missing: $probeSource"
}

$started = $false
$sessionDirectory = $null
$manifestPath = $null
$probeExitCode = 1
$probeOutput = @()
$evidenceDirectory = $null
try {
    $startArguments = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
        "-Action", "Start", "-App", "FreeP", "-Port", "$Port",
        "-Width", "$Width", "-Height", "$Height", "-Dpi", "$Dpi",
        "-MemoryLimit", $MemoryLimit, "-OutputDir", $resolvedOutputRoot,
        "-DocumentPath", $fixturePath
    )
    if (-not [string]::IsNullOrWhiteSpace($PublishDir)) { $startArguments += @("-PublishDir", $PublishDir) }
    if ($SkipPublish) { $startArguments += "-SkipPublish" }
    if ($SkipImageBuild) { $startArguments += "-SkipImageBuild" }
    if ($Replace) { $startArguments += "-Replace" }
    Invoke-VisualEvidenceProcess -FilePath "powershell.exe" -Arguments $startArguments -WorkingDirectory $repoRoot
    $started = $true

    $sessionMetadataPath = Join-Path $resolvedOutputRoot "freep/current-session.json"
    if (-not (Test-Path -LiteralPath $sessionMetadataPath -PathType Leaf)) {
        throw "Generic runner did not write current session metadata: $sessionMetadataPath"
    }
    $session = Get-Content -LiteralPath $sessionMetadataPath -Raw | ConvertFrom-Json
    $sessionDirectory = [IO.Path]::GetFullPath([string]$session.sessionDirectory)
    if (-not (Test-Path -LiteralPath $sessionDirectory -PathType Container)) {
        throw "Session directory does not exist: $sessionDirectory"
    }
    $readyPath = Join-Path $sessionDirectory "ready.json"
    $ready = if (Test-Path -LiteralPath $readyPath -PathType Leaf) {
        Get-Content -LiteralPath $readyPath -Raw | ConvertFrom-Json
    } else {
        [pscustomobject]@{ windowId = ""; windowTitle = "FreeP $fixtureFileName" }
    }

    $probeInWork = Join-Path $sessionDirectory "freep-file-slideshow-shortcut-probe.sh"
    Copy-Item -LiteralPath $probeSource -Destination $probeInWork -Force
    $manifestPath = Join-Path $sessionDirectory "fps/results.json"
    $evidenceDirectory = Split-Path -Parent $manifestPath
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    $probeLog = Join-Path $evidenceDirectory "probe.log"
    New-Item -ItemType File -Path $probeLog -Force | Out-Null

    $sourceBefore = Join-Path $evidenceDirectory "fixture-source-before.sha256.txt"
    $sourceAfter = Join-Path $evidenceDirectory "fixture-source-after.sha256.txt"
    (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash.ToLowerInvariant() |
        Set-Content -LiteralPath $sourceBefore -Encoding ascii

    $dockerArguments = @(
        "exec",
        "--env", "FREEP_DOCUMENT_PATH=/documents/$fixtureFileName",
        "--env", "FREEP_EXPECTED_DOCUMENT_NAME=$fixtureFileName",
        "--env", "FREEP_EXPECTED_WINDOW_PATTERN=FreeP",
        "--env", "FREEP_SCREEN_WIDTH=$Width",
        "--env", "FREEP_SCREEN_HEIGHT=$Height",
        "--env", "FREEP_SCREEN_DPI=$Dpi",
        [string]$session.containerName, "bash", "/work/freep-file-slideshow-shortcut-probe.sh",
        "/work/fps"
    )
    Push-Location $repoRoot
    try {
        $probeOutput = @(& docker @dockerArguments 2>&1)
        $probeExitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    if ($probeOutput.Count -gt 0) {
        $probeOutput | Set-Content -LiteralPath $probeLog -Encoding utf8
    } else {
        "docker exec produced no stdout/stderr; inspect the manifest and runtime evidence." |
            Set-Content -LiteralPath $probeLog -Encoding utf8
    }

    (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash.ToLowerInvariant() |
        Set-Content -LiteralPath $sourceAfter -Encoding ascii
    $mountedDocument = Join-Path $resolvedOutputRoot "freep/documents/$fixtureFileName"
    $mountedAfter = Join-Path $evidenceDirectory "fixture-host-mounted-after.sha256.txt"
    if (Test-Path -LiteralPath $mountedDocument -PathType Leaf) {
        (Get-FileHash -LiteralPath $mountedDocument -Algorithm SHA256).Hash.ToLowerInvariant() |
            Set-Content -LiteralPath $mountedAfter -Encoding ascii
    } else {
        "MISSING mounted document: $mountedDocument" | Set-Content -LiteralPath $mountedAfter -Encoding utf8
    }

    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        $failureEvidenceName = "probe-runner-failure.txt"
        @(
            "The dedicated FreeP probe exited without writing its manifest.",
            "docker-exit-code=$probeExitCode",
            "probe-log=$probeLog",
            "probe-output=$([string]::Join([Environment]::NewLine, @($probeOutput)))"
        ) | Set-Content -LiteralPath (Join-Path $evidenceDirectory $failureEvidenceName) -Encoding utf8

        $failureIds = @($requiredIds)
        $failureResults = @($failureIds | ForEach-Object {
            [ordered]@{
                id = $_
                category = "physical-x11-file-slideshow-shortcut"
                status = "failed"
                evidenceLevel = "physical-x11-input"
                evidence = @($failureEvidenceName)
                note = "Probe runner exited before producing row-specific evidence."
            }
        })
        $failureScreenshotName = $null
        $initialScreenshotPath = Join-Path $sessionDirectory "screenshots/initial.png"
        if (Test-Path -LiteralPath $initialScreenshotPath -PathType Leaf) {
            $failureScreenshotName = "probe-runner-failure.png"
            Copy-Item -LiteralPath $initialScreenshotPath -Destination (Join-Path $evidenceDirectory $failureScreenshotName) -Force
        }
        $failureScreenshots = if ($null -eq $failureScreenshotName) {
            @()
        } else {
            @([ordered]@{ name = $failureScreenshotName; kind = "screenshot" })
        }
        $failureManifest = [ordered]@{
            schemaVersion = 1
            suite = "freep-linux-file-slideshow-shortcut-physical"
            platform = "linux"
            shell = "avalonia"
            app = "FreeP"
            baseline = $false
            appSurface = "document-editor-file-slideshow-shortcuts"
            window = [ordered]@{
                id = if ([string]::IsNullOrWhiteSpace([string]$ready.windowId)) { "unknown-owner" } else { [string]$ready.windowId }
                title = if ([string]::IsNullOrWhiteSpace([string]$ready.windowTitle)) { "FreeP $fixtureFileName" } else { [string]$ready.windowTitle }
                pattern = $fixtureFileName
                visible = $true
            }
            parameters = [ordered]@{
                width = $Width
                height = $Height
                dpi = $Dpi
                fixture = $fixtureFileName
            }
            coverage = [ordered]@{
                scope = "physical FreeP file/slideshow shortcut evidence lane"
                exhaustive = $false
                familyContract = "tools/Run-FamilyLinuxInteractionValidation.ps1 keeps its exact FreeP 22-row contract."
            }
            contractValidation = [ordered]@{
                status = "pending"
                validator = "tools/Run-FreePFileSlideshowShortcutValidation.ps1"
                contractReference = "tools/LinuxInteractiveDocker/freep-file-slideshow-shortcut-validation.schema.json"
            }
            screenshots = $failureScreenshots
            summary = [ordered]@{ passed = 0; failed = 10; total = 10 }
            results = $failureResults
        }
        $failureManifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8
        Write-Warning "Probe did not write a manifest; deterministic failure manifest created at $manifestPath"
    } else {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $saveResult = @($manifest.results | Where-Object { $_.id -eq "file-save-shortcut-current-path" })[0]
        if ($null -eq $saveResult) {
            throw "Probe manifest is missing file-save-shortcut-current-path."
        }
        Add-VisualEvidenceResultReferences -Result $saveResult -Names @(
            "fixture-source-before.sha256.txt",
            "fixture-source-after.sha256.txt",
            "fixture-mounted-before.sha256.txt",
            "fixture-mounted-after.sha256.txt",
            "fixture-host-mounted-after.sha256.txt"
        )
        $hashPaths = [ordered]@{
            "source-before" = $sourceBefore
            "source-after" = $sourceAfter
            "mounted-before" = (Join-Path $evidenceDirectory "fixture-mounted-before.sha256.txt")
            "mounted-after" = (Join-Path $evidenceDirectory "fixture-mounted-after.sha256.txt")
            "host-mounted-after" = $mountedAfter
        }
        $hashes = @{}
        $hashFailures = [System.Collections.Generic.List[string]]::new()
        foreach ($entry in $hashPaths.GetEnumerator()) {
            if (-not (Test-Path -LiteralPath $entry.Value -PathType Leaf)) {
                $hashFailures.Add("$($entry.Key) hash artifact is missing")
                continue
            }
            $value = (Get-Content -LiteralPath $entry.Value -Raw).Trim()
            if ($value -notmatch '^[0-9a-f]{64}$') {
                $hashFailures.Add("$($entry.Key) hash is not an exact lowercase 64-hex value")
                continue
            }
            $hashes[$entry.Key] = $value
        }
        if ($hashes.ContainsKey("source-before") -and $hashes.ContainsKey("source-after") -and
            $hashes["source-before"] -ne $hashes["source-after"]) {
            $hashFailures.Add("original source before and after hashes differ")
        }
        if ($hashes.ContainsKey("mounted-before") -and $hashes.ContainsKey("source-before") -and
            $hashes["mounted-before"] -ne $hashes["source-before"]) {
            $hashFailures.Add("probe mounted-before does not equal source-before")
        }
        if ($hashes.ContainsKey("mounted-after") -and $hashes.ContainsKey("host-mounted-after") -and
            $hashes["mounted-after"] -ne $hashes["host-mounted-after"]) {
            $hashFailures.Add("probe mounted-after does not equal host-mounted-after")
        }
        if ($hashes.ContainsKey("mounted-before") -and $hashes.ContainsKey("mounted-after") -and
            $hashes["mounted-before"] -eq $hashes["mounted-after"]) {
            $hashFailures.Add("mounted before and after hashes are identical")
        }
        if ($hashFailures.Count -gt 0) {
            $saveResult.status = "failed"
            $saveResult.note = "File-save SHA256 evidence failed: $([string]::Join('; ', $hashFailures))."
        }
        $manifest.summary.passed = @($manifest.results | Where-Object { $_.status -eq "passed" }).Count
        $manifest.summary.failed = @($manifest.results | Where-Object { $_.status -eq "failed" }).Count
        $manifest.summary.total = @($manifest.results).Count
        $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    }

    Wait-ForManifestEvidence -ManifestPath $manifestPath -EvidenceDirectory $evidenceDirectory
    $manifest = Assert-ManifestContract -ManifestPath $manifestPath -EvidenceDirectory $evidenceDirectory
    Write-Host "Manifest contract validation: $($manifest.contractValidation.status)"
    Write-Host "Results: $($manifest.summary.passed) passed, $($manifest.summary.failed) failed, $($manifest.summary.total) total"
    Write-Host "Manifest: $manifestPath"
    Write-Host "Fixture: $fixturePath"
    if ($probeExitCode -ne 0 -or $manifest.summary.failed -gt 0) {
        throw "FreeP file/slideshow shortcut validation failed with probe exit code $probeExitCode and $($manifest.summary.failed) failed result(s). Evidence retained at $manifestPath."
    }
} finally {
    if ($started -and -not $KeepContainer) {
        try {
            Invoke-VisualEvidenceProcess -FilePath "powershell.exe" -Arguments @(
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
                "-Action", "Stop", "-App", "FreeP", "-Port", "$Port", "-OutputDir", $resolvedOutputRoot
            ) -WorkingDirectory $repoRoot
        } catch {
            Write-Warning "Could not stop harness-owned FreeP container on port ${Port}: $($_.Exception.Message)"
        }
    } elseif ($started) {
        Write-Host "Container retained by request on port $Port."
    }
}
