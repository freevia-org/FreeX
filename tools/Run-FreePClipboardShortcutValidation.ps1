<##
.SYNOPSIS
  Runs the dedicated physical FreeP clipboard shortcut evidence lane.

.DESCRIPTION
  Starts one harness-owned FreeP Linux desktop, injects the independently-owned
  clipboard probe, and validates its exact eight-row non-baseline contract.
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$Port = 6093,
    [ValidateRange(640, 7680)][int]$Width = 1280,
    [ValidateRange(480, 4320)][int]$Height = 820,
    [ValidateRange(72, 240)][int]$Dpi = 96,
    [ValidateSet("2g", "4g", "6g", "8g")][string]$MemoryLimit = "4g",
    [string]$OutputDir = "artifacts/linux-freep-clipboard-shortcuts",
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
$probeSource = Join-Path $PSScriptRoot "LinuxInteractiveDocker/run-freep-clipboard-shortcut-probe.sh"
$schemaPath = Join-Path $PSScriptRoot "LinuxInteractiveDocker/freep-clipboard-shortcut-validation.schema.json"
$manifestEvidenceHelper = Join-Path $PSScriptRoot "LinuxInteractiveDocker/ManifestEvidence.ps1"
$null = . $manifestEvidenceHelper

$requiredIds = @(
    "visible-window-discovery",
    "clipboard-copy-x11-preserves-source",
    "clipboard-paste-native-editable-shape",
    "select-all-multi-shape-mutation",
    "cut-all-x11-undoable",
    "undo-restores-editable-shapes",
    "redo-reapplies-cut",
    "paste-after-cut-restores-editable-shapes"
)

function Assert-ManifestContract {
    param([Parameter(Mandatory = $true)][string]$ManifestPath, [Parameter(Mandatory = $true)][string]$EvidenceDirectory)
    $manifest = Read-ManifestContract -ManifestPath $ManifestPath -SchemaPath $schemaPath
    if ($manifest.contractValidation.status -ne "pending") { throw "Probe must leave contractValidation pending until the runner passes strict validation." }
    $scope = "physical FreeP clipboard shortcut evidence lane"
    if ($manifest.schemaVersion -ne 1 -or $manifest.suite -ne "freep-linux-clipboard-shortcut-physical" -or
        $manifest.platform -ne "linux" -or $manifest.shell -ne "avalonia" -or $manifest.app -ne "FreeP" -or
        $manifest.baseline -ne $false -or $manifest.appSurface -ne "document-editor-clipboard-shortcuts" -or
        $manifest.coverage.exhaustive -ne $false -or $manifest.coverage.scope -ne $scope -or
        $manifest.window.pattern -ne $fixtureFileName -or $manifest.window.visible -ne $true -or
        ([string]$manifest.window.title).IndexOf($fixtureFileName, [StringComparison]::Ordinal) -lt 0 -or
        ([string]$manifest.window.title).IndexOf("FreeP", [StringComparison]::OrdinalIgnoreCase) -lt 0) { throw "FreeP clipboard shortcut manifest header does not satisfy its dedicated contract." }

    $results = @($manifest.results)
    $ids = @($results | ForEach-Object { [string]$_.id })
    if ($results.Count -ne 8 -or $ids.Count -ne ($ids | Select-Object -Unique).Count -or
        [string]::Join("|", $ids) -ne [string]::Join("|", $requiredIds)) { throw "Manifest must contain exactly the eight required unique result rows in contract order." }
    Assert-ManifestResultSummary -Manifest $manifest -Results $results -ExpectedTotal 8 `
        -RequireCompleteStatuses -FailureMessage "Manifest summary does not match its eight result rows."

    $fileMap = Get-ManifestEvidenceFileMap -EvidenceDirectory $EvidenceDirectory
    foreach ($result in $results) {
        if ($result.category -ne "physical-x11-clipboard-shortcut" -or $result.evidenceLevel -ne "physical-x11-input" -or
            @($result.evidence).Count -lt 1 -or [string]::IsNullOrWhiteSpace([string]$result.note) -or $result.status -notin @("passed", "failed")) { throw "Result '$($result.id)' is missing strict physical evidence metadata or has an invalid status." }
        foreach ($evidence in @($result.evidence)) {
            $name = [string]$evidence
            Assert-ManifestEvidenceReference -FileMap $fileMap -Name $name -Owner "Result '$($result.id)'"
        }
    }
    foreach ($screenshot in @($manifest.screenshots)) {
        $name = [string]$screenshot.name
        if ($screenshot.kind -ne "screenshot") { throw "Manifest screenshot '$name' has an invalid kind." }
        Assert-ManifestEvidenceReference -FileMap $fileMap -Name $name -Owner "Manifest" -ReferenceKind "screenshot"
    }
    return Complete-ManifestContract -Manifest $manifest -ManifestPath $ManifestPath `
        -Validator "tools/Run-FreePClipboardShortcutValidation.ps1" `
        -ContractReference "tools/LinuxInteractiveDocker/freep-clipboard-shortcut-validation.schema.json" -JsonDepth 16
}

if (-not (Test-Path -LiteralPath $fixturePath -PathType Leaf)) { throw "Fixture was not found: $fixturePath" }
if (-not (Test-Path -LiteralPath $probeSource -PathType Leaf)) { throw "Probe is missing: $probeSource" }

$started = $false; $sessionDirectory = $null; $manifestPath = $null; $probeExitCode = 1; $probeOutput = @(); $evidenceDirectory = $null
try {
    $startArguments = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner, "-Action", "Start", "-App", "FreeP", "-Port", "$Port", "-Width", "$Width", "-Height", "$Height", "-Dpi", "$Dpi", "-MemoryLimit", $MemoryLimit, "-OutputDir", $resolvedOutputRoot, "-DocumentPath", $fixturePath)
    if (-not [string]::IsNullOrWhiteSpace($PublishDir)) { $startArguments += @("-PublishDir", $PublishDir) }
    if ($SkipPublish) { $startArguments += "-SkipPublish" }; if ($SkipImageBuild) { $startArguments += "-SkipImageBuild" }; if ($Replace) { $startArguments += "-Replace" }
    Invoke-VisualEvidenceProcess -FilePath "powershell.exe" -Arguments $startArguments -WorkingDirectory $repoRoot; $started = $true
    $sessionMetadataPath = Join-Path $resolvedOutputRoot "freep/current-session.json"
    if (-not (Test-Path -LiteralPath $sessionMetadataPath -PathType Leaf)) { throw "Generic runner did not write current session metadata: $sessionMetadataPath" }
    $session = Get-Content -LiteralPath $sessionMetadataPath -Raw | ConvertFrom-Json
    $sessionDirectory = [IO.Path]::GetFullPath([string]$session.sessionDirectory)
    if (-not (Test-Path -LiteralPath $sessionDirectory -PathType Container)) { throw "Session directory does not exist: $sessionDirectory" }
    $readyPath = Join-Path $sessionDirectory "ready.json"
    $ready = if (Test-Path -LiteralPath $readyPath -PathType Leaf) { Get-Content -LiteralPath $readyPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{ windowId = ""; windowTitle = "FreeP $fixtureFileName" } }
    $probeInWork = Join-Path $sessionDirectory "freep-clipboard-shortcut-probe.sh"
    Copy-Item -LiteralPath $probeSource -Destination $probeInWork -Force
    $manifestPath = Join-Path $sessionDirectory "freep-clipboard-shortcut-validation/results.json"; $evidenceDirectory = Split-Path -Parent $manifestPath
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    $probeLog = Join-Path $evidenceDirectory "probe.log"; New-Item -ItemType File -Path $probeLog -Force | Out-Null
    $sourceBefore = Join-Path $evidenceDirectory "fixture-source-before.sha256.txt"; $sourceAfter = Join-Path $evidenceDirectory "fixture-source-after.sha256.txt"
    (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath $sourceBefore -Encoding ascii
    $dockerArguments = @("exec", "--env", "FREEP_DOCUMENT_PATH=/documents/$fixtureFileName", "--env", "FREEP_EXPECTED_DOCUMENT_NAME=$fixtureFileName", "--env", "FREEP_EXPECTED_WINDOW_PATTERN=FreeP", "--env", "FREEP_SCREEN_WIDTH=$Width", "--env", "FREEP_SCREEN_HEIGHT=$Height", "--env", "FREEP_SCREEN_DPI=$Dpi", [string]$session.containerName, "bash", "/work/freep-clipboard-shortcut-probe.sh", "/work/freep-clipboard-shortcut-validation")
    Push-Location $repoRoot; try { $probeOutput = @(& docker @dockerArguments 2>&1); $probeExitCode = $LASTEXITCODE } finally { Pop-Location }
    if ($probeOutput.Count -gt 0) { $probeOutput | Set-Content -LiteralPath $probeLog -Encoding utf8 } else { "docker exec produced no stdout/stderr; inspect the manifest and runtime evidence." | Set-Content -LiteralPath $probeLog -Encoding utf8 }
    (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath $sourceAfter -Encoding ascii
    $mountedDocument = Join-Path $resolvedOutputRoot "freep/documents/$fixtureFileName"; $mountedAfter = Join-Path $evidenceDirectory "fixture-host-mounted-after.sha256.txt"
    if (Test-Path -LiteralPath $mountedDocument -PathType Leaf) { (Get-FileHash -LiteralPath $mountedDocument -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath $mountedAfter -Encoding ascii } else { "MISSING mounted document: $mountedDocument" | Set-Content -LiteralPath $mountedAfter -Encoding utf8 }
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        $failureEvidenceName = "probe-runner-failure.txt"; @("The dedicated FreeP clipboard probe exited without writing its manifest.", "docker-exit-code=$probeExitCode", "probe-log=$probeLog", "probe-output=$([string]::Join([Environment]::NewLine, @($probeOutput)))") | Set-Content -LiteralPath (Join-Path $evidenceDirectory $failureEvidenceName) -Encoding utf8
        $failureResults = @($requiredIds | ForEach-Object { [ordered]@{ id = $_; category = "physical-x11-clipboard-shortcut"; status = "failed"; evidenceLevel = "physical-x11-input"; evidence = @($failureEvidenceName); note = "Probe runner exited before producing row-specific evidence." } })
        $failureScreenshotName = $null; $initialScreenshotPath = Join-Path $sessionDirectory "screenshots/initial.png"
        if (Test-Path -LiteralPath $initialScreenshotPath -PathType Leaf) { $failureScreenshotName = "probe-runner-failure.png"; Copy-Item -LiteralPath $initialScreenshotPath -Destination (Join-Path $evidenceDirectory $failureScreenshotName) -Force }
        $failureScreenshots = if ($null -eq $failureScreenshotName) { @() } else { @([ordered]@{ name = $failureScreenshotName; kind = "screenshot" }) }
        [ordered]@{ schemaVersion = 1; suite = "freep-linux-clipboard-shortcut-physical"; platform = "linux"; shell = "avalonia"; app = "FreeP"; baseline = $false; appSurface = "document-editor-clipboard-shortcuts"; window = [ordered]@{ id = if ([string]::IsNullOrWhiteSpace([string]$ready.windowId)) { "unknown-owner" } else { [string]$ready.windowId }; title = if ([string]::IsNullOrWhiteSpace([string]$ready.windowTitle)) { "FreeP $fixtureFileName" } else { [string]$ready.windowTitle }; pattern = $fixtureFileName; visible = $true }; parameters = [ordered]@{ width = $Width; height = $Height; dpi = $Dpi; fixture = $fixtureFileName }; coverage = [ordered]@{ scope = "physical FreeP clipboard shortcut evidence lane"; exhaustive = $false; familyContract = "tools/Run-FamilyLinuxInteractionValidation.ps1 keeps its exact FreeP family contract." }; contractValidation = [ordered]@{ status = "pending"; validator = "tools/Run-FreePClipboardShortcutValidation.ps1"; contractReference = "tools/LinuxInteractiveDocker/freep-clipboard-shortcut-validation.schema.json" }; screenshots = $failureScreenshots; summary = [ordered]@{ passed = 0; failed = 8; total = 8 }; results = $failureResults } | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8
        Write-Warning "Probe did not write a manifest; deterministic failure manifest created at $manifestPath"
    } else {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $copyResult = @($manifest.results | Where-Object { $_.id -eq "clipboard-copy-x11-preserves-source" })[0]
        if ($null -eq $copyResult) { throw "Probe manifest is missing clipboard-copy-x11-preserves-source." }
        Add-VisualEvidenceResultReferences -Result $copyResult -Names @("fixture-source-before.sha256.txt", "fixture-source-after.sha256.txt", "fixture-mounted-before.sha256.txt", "fixture-mounted-after.sha256.txt", "fixture-host-mounted-after.sha256.txt")
        $hashPaths = [ordered]@{ "source-before" = $sourceBefore; "source-after" = $sourceAfter; "mounted-before" = (Join-Path $evidenceDirectory "fixture-mounted-before.sha256.txt"); "mounted-after" = (Join-Path $evidenceDirectory "fixture-mounted-after.sha256.txt"); "host-mounted-after" = $mountedAfter }
        $hashes = @{}; $hashFailures = [System.Collections.Generic.List[string]]::new()
        foreach ($entry in $hashPaths.GetEnumerator()) { if (-not (Test-Path -LiteralPath $entry.Value -PathType Leaf)) { $hashFailures.Add("$($entry.Key) hash artifact is missing"); continue }; if ((Get-Item -LiteralPath $entry.Value).Length -le 0) { $hashFailures.Add("$($entry.Key) hash artifact is empty"); continue }; $value = (Get-Content -LiteralPath $entry.Value -Raw).Trim(); if ($value -notmatch '^[0-9a-f]{64}$') { $hashFailures.Add("$($entry.Key) hash is not an exact lowercase 64-hex value"); continue }; $hashes[$entry.Key] = $value }
        foreach ($pair in @(@("source-before", "source-after"), @("source-before", "mounted-before"), @("mounted-after", "host-mounted-after"))) { if ($hashes.ContainsKey($pair[0]) -and $hashes.ContainsKey($pair[1]) -and $hashes[$pair[0]] -ne $hashes[$pair[1]]) { $hashFailures.Add("$($pair[0]) does not equal $($pair[1])") } }
        if ($hashes.ContainsKey("mounted-before") -and $hashes.ContainsKey("mounted-after") -and $hashes["mounted-before"] -eq $hashes["mounted-after"]) { $hashFailures.Add("mounted-after does not differ from mounted-before after the final paste restoration checkpoint save") }
        if ($hashFailures.Count -gt 0) { $copyResult.status = "failed"; $copyResult.note = "Clipboard source and saved working-copy SHA256 invariants failed: $([string]::Join('; ', $hashFailures))." }
        $manifest.summary.passed = @($manifest.results | Where-Object { $_.status -eq "passed" }).Count; $manifest.summary.failed = @($manifest.results | Where-Object { $_.status -eq "failed" }).Count; $manifest.summary.total = @($manifest.results).Count
        $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    }
    Wait-ForManifestEvidence -ManifestPath $manifestPath -EvidenceDirectory $evidenceDirectory
    $manifest = Assert-ManifestContract -ManifestPath $manifestPath -EvidenceDirectory $evidenceDirectory
    Write-Host "Manifest contract validation: $($manifest.contractValidation.status)"; Write-Host "Results: $($manifest.summary.passed) passed, $($manifest.summary.failed) failed, $($manifest.summary.total) total"; Write-Host "Manifest: $manifestPath"; Write-Host "Fixture: $fixturePath"
    if ($probeExitCode -ne 0 -or $manifest.summary.failed -gt 0) { throw "FreeP clipboard shortcut validation failed with probe exit code $probeExitCode and $($manifest.summary.failed) failed result(s). Evidence retained at $manifestPath." }
} finally {
    if ($started -and -not $KeepContainer) { try { Invoke-VisualEvidenceProcess -FilePath "powershell.exe" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner, "-Action", "Stop", "-App", "FreeP", "-Port", "$Port", "-OutputDir", $resolvedOutputRoot) -WorkingDirectory $repoRoot } catch { Write-Warning "Could not stop harness-owned FreeP container on port ${Port}: $($_.Exception.Message)" } } elseif ($started) { Write-Host "Container retained by request on port $Port." }
}
