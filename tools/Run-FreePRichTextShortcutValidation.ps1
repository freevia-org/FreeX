<##
.SYNOPSIS
  Runs the dedicated physical FreeP rich-editor soft-break evidence lane.

.DESCRIPTION
  Starts one harness-owned FreeP Linux desktop, injects the independently-owned
  rich-text probe, and validates its exact five-row non-baseline contract.
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)][int]$Port = 6095,
    [ValidateRange(640, 7680)][int]$Width = 1280,
    [ValidateRange(480, 4320)][int]$Height = 820,
    [ValidateRange(72, 240)][int]$Dpi = 96,
    [ValidateSet("2g", "4g", "6g", "8g")][string]$MemoryLimit = "4g",
    [string]$OutputDir = "artifacts/freep-rich-text",
    [string]$PublishDir = "",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$GroupedChild,
    [switch]$GroupedCaret,
    [switch]$PointerSelection,
    [switch]$Replace,
    [switch]$KeepContainer
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "VisualEvidenceScriptSupport.ps1")
$resolvedOutputRoot = Resolve-VisualEvidenceOutputDirectory -OutputDirectory $OutputDir -RepoRoot $repoRoot
$fixturePath = Join-Path $repoRoot "tools/FreeP.RenderCompare/corpus/21-comments-notes.pptx"
$surface = "in-canvas-rich-text-soft-break"
$scope = "physical FreeP rich-editor soft-break evidence lane"
if (@($GroupedChild, $GroupedCaret, $PointerSelection).Where({ $_ }).Count -gt 1) { throw "GroupedChild, GroupedCaret, and PointerSelection are mutually exclusive." }
if ($GroupedChild) {
    $fixturePath = Join-Path $resolvedOutputRoot "fixtures/21-comments-notes-grouped-child.pptx"
    $surface = "in-canvas-grouped-child-rich-text"
    $scope = "physical FreeP grouped-child rich-editor edit-save-reopen lane"
    $generator = Join-Path $repoRoot "tools/FreeP.RenderCompare/Generate-GroupedTextFixture.ps1"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $generator `
        -Source (Join-Path $repoRoot "tools/FreeP.RenderCompare/corpus/21-comments-notes.pptx") `
        -Destination $fixturePath
    if ($LASTEXITCODE -ne 0) { throw "Grouped-child fixture generation failed with exit code $LASTEXITCODE." }
}
elseif ($GroupedCaret) {
    $fixturePath = Join-Path $resolvedOutputRoot "fixtures/21-comments-notes-grouped-child-caret.pptx"
    $surface = "in-canvas-grouped-child-caret"
    $scope = "physical FreeP grouped-child caret navigation selection edit-save-reopen lane"
    $generator = Join-Path $repoRoot "tools/FreeP.RenderCompare/Generate-GroupedTextFixture.ps1"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $generator `
        -Source (Join-Path $repoRoot "tools/FreeP.RenderCompare/corpus/21-comments-notes.pptx") `
        -Destination $fixturePath -CaretGeometry
    if ($LASTEXITCODE -ne 0) { throw "Grouped-child fixture generation failed with exit code $LASTEXITCODE." }
}
elseif ($PointerSelection) {
    $fixturePath = Join-Path $resolvedOutputRoot "fixtures/21-comments-notes-grouped-child-pointer-selection.pptx"
    $surface = "in-canvas-grouped-child-pointer-selection"
    $scope = "physical FreeP grouped-child pointer drag selection across unequal wrapped visual lines and a paragraph boundary"
    $generator = Join-Path $repoRoot "tools/FreeP.RenderCompare/Generate-GroupedTextFixture.ps1"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $generator `
        -Source (Join-Path $repoRoot "tools/FreeP.RenderCompare/corpus/21-comments-notes.pptx") `
        -Destination $fixturePath -PointerSelectionGeometry
    if ($LASTEXITCODE -ne 0) { throw "Pointer-selection fixture generation failed with exit code $LASTEXITCODE." }
}
$fixtureFileName = Split-Path -Leaf $fixturePath
$genericRunner = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$probeSource = Join-Path $PSScriptRoot "LinuxInteractiveDocker/run-freep-rich-text-shortcut-probe.sh"
$schemaPath = Join-Path $PSScriptRoot "LinuxInteractiveDocker/freep-rich-text-shortcut-validation.schema.json"
$manifestEvidenceHelper = Join-Path $PSScriptRoot "LinuxInteractiveDocker/ManifestEvidence.ps1"
$null = . $manifestEvidenceHelper

$requiredIds = @(
    "visible-window-discovery",
    "rich-editor-physical-soft-break-input",
    "saved-soft-break-native-package",
    "undo-restores-original-text",
    "redo-restores-soft-break"
)

function Assert-ExactClipboardTranscript {
    param(
        [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
        [Parameter(Mandatory = $true)][string]$Name
    )
    $expectedPath = Join-Path $EvidenceDirectory "$Name-expected.txt"
    $actualPath = Join-Path $EvidenceDirectory "$Name-actual.txt"
    $proofPath = Join-Path $EvidenceDirectory "$Name-proof.txt"
    foreach ($path in @($expectedPath, $actualPath, $proofPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -le 0) {
            throw "Clipboard transcript artifact is missing or empty: $path"
        }
    }
    $expectedBytes = [IO.File]::ReadAllBytes($expectedPath)
    $actualBytes = [IO.File]::ReadAllBytes($actualPath)
    if ([Convert]::ToBase64String($expectedBytes) -ne [Convert]::ToBase64String($actualBytes)) {
        throw "Clipboard transcript '$Name' does not exactly match its expected bytes."
    }
    $proof = Get-Content -LiteralPath $proofPath -Raw
    if ($proof -notmatch '(?m)^tool=xclip$' -or
        $proof -notmatch '(?m)^selection=clipboard$' -or
        $proof -notmatch '(?m)^status=true$' -or
        $proof -notmatch '(?m)^exact-match=true$') {
        throw "Clipboard transcript '$Name' did not report a bounded exact xclip match."
    }
}

function Assert-GroupedCaretSemanticContract {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$EvidenceDirectory
    )
    if ($surface -ne "in-canvas-grouped-child-caret") { return }
    if ($null -eq $Manifest.semanticReadback -or
        $Manifest.semanticReadback.tool -ne "xclip" -or
        $Manifest.semanticReadback.selection -ne "clipboard" -or
        [string]::Join("|", @($Manifest.semanticReadback.transcripts)) -ne
        "grouped-caret-selection|grouped-caret-vertical-down|grouped-caret-vertical-roundtrip" -or
        $Manifest.semanticReadback.reopenProof -ne "grouped-caret-reopen-proof.txt") {
        throw "Grouped-caret manifest is missing its exact Wave67 semantic readback declaration."
    }
    foreach ($name in @(
        "grouped-caret-selection",
        "grouped-caret-vertical-down",
        "grouped-caret-vertical-roundtrip"
    )) {
        Assert-ExactClipboardTranscript -EvidenceDirectory $EvidenceDirectory -Name $name
    }
    $reopenProofPath = Join-Path $EvidenceDirectory "grouped-caret-reopen-proof.txt"
    $reopenScreenshotPath = Join-Path $EvidenceDirectory "grouped-caret-reopened.png"
    if (-not (Test-Path -LiteralPath $reopenProofPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $reopenScreenshotPath -PathType Leaf) -or
        (Get-Item -LiteralPath $reopenScreenshotPath).Length -le 0) {
        throw "Grouped-caret reopen proof or screenshot is missing."
    }
    $reopenProof = Get-Content -LiteralPath $reopenProofPath -Raw
    if ($reopenProof -notmatch '(?m)^dialog-open=true$' -or
        $reopenProof -notmatch '(?m)^dialog-closed=true$' -or
        $reopenProof -notmatch '(?m)^clipboard-readback=true$' -or
        $reopenProof -notmatch '(?m)^screenshot-captured=true$' -or
        $reopenProof -notmatch '(?m)^reopen-pass=true$') {
        throw "Grouped-caret reopen proof did not prove open, close, exact clipboard readback, and screenshot capture."
    }
    Assert-ExactClipboardTranscript -EvidenceDirectory $EvidenceDirectory -Name "grouped-caret-reopened"
}

function Assert-PointerSelectionSemanticContract {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$EvidenceDirectory
    )
    if ($surface -ne "in-canvas-grouped-child-pointer-selection") { return }
    if ($null -eq $Manifest.semanticReadback -or
        $Manifest.semanticReadback.tool -ne "xclip" -or
        $Manifest.semanticReadback.selection -ne "clipboard" -or
        [string]::Join("|", @($Manifest.semanticReadback.transcripts)) -ne
        "pointer-selection-forward|pointer-selection-reverse|pointer-paragraph-selection" -or
        $Manifest.semanticReadback.geometryProof -ne "pointer-selection-calibration.txt") {
        throw "Pointer-selection manifest is missing its exact semantic readback declaration."
    }
    foreach ($name in @(
        "pointer-selection-forward",
        "pointer-selection-reverse",
        "pointer-paragraph-selection"
    )) {
        Assert-ExactClipboardTranscript -EvidenceDirectory $EvidenceDirectory -Name $name
    }
    $proofPath = Join-Path $EvidenceDirectory "pointer-selection-calibration.txt"
    foreach ($path in @($proofPath, (Join-Path $EvidenceDirectory "pointer-selection-forward.png"), (Join-Path $EvidenceDirectory "pointer-selection-reverse.png"))) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -le 0) {
            throw "Pointer-selection geometry or screenshot evidence is missing: $path"
        }
    }
    $proof = Get-Content -LiteralPath $proofPath -Raw
    if ($proof -notmatch '(?m)^drag-contract=first visual line to captured pointer beyond editor bottom across paragraph boundary$') {
        throw "Pointer-selection calibration proof does not describe the bounded drag contract."
    }
    $visualStatePath = Join-Path $EvidenceDirectory "pointer-selection-visual-state.json"
    if (-not (Test-Path -LiteralPath $visualStatePath -PathType Leaf) -or
        (Get-Item -LiteralPath $visualStatePath).Length -le 0) {
        throw "Pointer-selection paired visual state is missing."
    }
    $visualState = Get-Content -LiteralPath $visualStatePath -Raw | ConvertFrom-Json
    $expectedText = "Wide words make this first paragraph wrap at unequal visual line widths`ntail paragraph crosses the boundary"
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $expectedTextHash = ([BitConverter]::ToString(
            $sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($expectedText)))
        ).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
    $fixtureBeforePath = Join-Path $EvidenceDirectory "fixture-mounted-before.sha256.txt"
    $fixtureBefore = (Get-Content -LiteralPath $fixtureBeforePath -Raw).Trim()
    if ($visualState.contractId -ne "freep.rich-text.selection-visual.v1" -or
        $visualState.fixtureSha256 -ne $fixtureBefore -or
        $visualState.selectedText -ne $expectedText -or
        $visualState.selectedTextSha256 -ne $expectedTextHash -or
        $visualState.capture -ne "pointer-selection-forward.png" -or
        $visualState.direction -ne "forward") {
        throw "Pointer-selection paired visual state is stale or does not describe the exact selected-text capture."
    }
}

function Assert-ManifestContract {
    param([Parameter(Mandatory = $true)][string]$ManifestPath, [Parameter(Mandatory = $true)][string]$EvidenceDirectory)
    if (-not (Test-Path -LiteralPath $schemaPath -PathType Leaf)) { throw "Manifest schema is missing: $schemaPath" }
    $schema = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json
    if ($schema.'$schema' -notmatch "json-schema.org") { throw "Manifest contract reference is not a JSON Schema document." }
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ($manifest.contractValidation.status -ne "pending") { throw "Probe must leave contractValidation pending until the runner passes strict validation." }
    if ($manifest.schemaVersion -ne 1 -or $manifest.suite -ne "freep-linux-rich-text-shortcut-physical" -or
        $manifest.platform -ne "linux" -or $manifest.shell -ne "avalonia" -or $manifest.app -ne "FreeP" -or
        $manifest.baseline -ne $false -or $manifest.appSurface -ne $surface -or
        $manifest.coverage.exhaustive -ne $false -or $manifest.coverage.scope -ne $scope -or
        $manifest.window.pattern -ne $fixtureFileName -or $manifest.window.visible -ne $true -or
        ([string]$manifest.window.title).IndexOf($fixtureFileName, [StringComparison]::Ordinal) -lt 0 -or
        ([string]$manifest.window.title).IndexOf("FreeP", [StringComparison]::OrdinalIgnoreCase) -lt 0) { throw "FreeP rich-text shortcut manifest header does not satisfy its dedicated contract." }

    $results = @($manifest.results)
    $ids = @($results | ForEach-Object { [string]$_.id })
    if ($results.Count -ne 5 -or $ids.Count -ne ($ids | Select-Object -Unique).Count -or
        [string]::Join("|", $ids) -ne [string]::Join("|", $requiredIds)) { throw "Manifest must contain exactly the five required unique result rows in contract order." }
    $passed = @($results | Where-Object { $_.status -eq "passed" }).Count
    $failed = @($results | Where-Object { $_.status -eq "failed" }).Count
    if ($manifest.summary.total -ne 5 -or $manifest.summary.passed -ne $passed -or $manifest.summary.failed -ne $failed -or ($passed + $failed) -ne 5) { throw "Manifest summary does not match its five result rows." }

    $fileMap = Get-ManifestEvidenceFileMap -EvidenceDirectory $EvidenceDirectory
    foreach ($result in $results) {
        if ($result.category -ne "physical-x11-rich-text-shortcut" -or $result.evidenceLevel -ne "physical-x11-input" -or
            @($result.evidence).Count -lt 1 -or [string]::IsNullOrWhiteSpace([string]$result.note) -or $result.status -notin @("passed", "failed")) { throw "Result '$($result.id)' is missing strict physical evidence metadata or has an invalid status." }
        foreach ($evidence in @($result.evidence)) {
            $name = [string]$evidence
            if ([string]::IsNullOrWhiteSpace($name) -or [IO.Path]::IsPathRooted($name) -or [IO.Path]::GetFileName($name) -ne $name -or $name.Contains("/") -or $name.Contains("\") -or -not $fileMap.ContainsKey($name) -or $fileMap[$name].Length -le 0) { throw "Result '$($result.id)' references missing, empty, or non-basename evidence '$name'." }
        }
    }
    foreach ($screenshot in @($manifest.screenshots)) {
        $name = [string]$screenshot.name
        if ($screenshot.kind -ne "screenshot" -or [string]::IsNullOrWhiteSpace($name) -or [IO.Path]::IsPathRooted($name) -or [IO.Path]::GetFileName($name) -ne $name -or $name.Contains("/") -or $name.Contains("\") -or -not $fileMap.ContainsKey($name) -or $fileMap[$name].Length -le 0) { throw "Manifest references missing, empty, or non-basename screenshot '$name'." }
    }
    $manifest | Add-Member -NotePropertyName contractValidation -NotePropertyValue ([pscustomobject]@{ status = "passed"; validator = "tools/Run-FreePRichTextShortcutValidation.ps1"; contractReference = "tools/LinuxInteractiveDocker/freep-rich-text-shortcut-validation.schema.json" }) -Force
    $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $ManifestPath -Encoding utf8
    return $manifest
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
    $probeInWork = Join-Path $sessionDirectory "freep-rich-text-shortcut-probe.sh"
    Copy-Item -LiteralPath $probeSource -Destination $probeInWork -Force
    $manifestPath = Join-Path $sessionDirectory "freep-rich-text-shortcut-validation/results.json"; $evidenceDirectory = Split-Path -Parent $manifestPath
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    $probeLog = Join-Path $evidenceDirectory "probe.log"; New-Item -ItemType File -Path $probeLog -Force | Out-Null
    $sourceBefore = Join-Path $evidenceDirectory "fixture-source-before.sha256.txt"; $sourceAfter = Join-Path $evidenceDirectory "fixture-source-after.sha256.txt"
    (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath $sourceBefore -Encoding ascii
    $dockerArguments = @("exec", "--env", "FREEP_DOCUMENT_PATH=/documents/$fixtureFileName", "--env", "FREEP_EXPECTED_DOCUMENT_NAME=$fixtureFileName", "--env", "FREEP_EXPECTED_WINDOW_PATTERN=FreeP", "--env", "FREEP_APP_SURFACE=$surface", "--env", "FREEP_COVERAGE_SCOPE=$scope", "--env", "FREEP_SCREEN_WIDTH=$Width", "--env", "FREEP_SCREEN_HEIGHT=$Height", "--env", "FREEP_SCREEN_DPI=$Dpi", [string]$session.containerName, "bash", "/work/freep-rich-text-shortcut-probe.sh", "/work/freep-rich-text-shortcut-validation")
    Push-Location $repoRoot; try { $probeOutput = @(& docker @dockerArguments 2>&1); $probeExitCode = $LASTEXITCODE } finally { Pop-Location }
    if ($probeOutput.Count -gt 0) { $probeOutput | Set-Content -LiteralPath $probeLog -Encoding utf8 } else { "docker exec produced no stdout/stderr; inspect the manifest and runtime evidence." | Set-Content -LiteralPath $probeLog -Encoding utf8 }
    (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath $sourceAfter -Encoding ascii
    $mountedDocument = Join-Path $resolvedOutputRoot "freep/documents/$fixtureFileName"; $mountedAfter = Join-Path $evidenceDirectory "fixture-host-mounted-after.sha256.txt"
    $hostMountedAfterAvailable = $false
    try {
        if (Test-Path -LiteralPath $mountedDocument -PathType Leaf) {
            $mountedHash = (Get-FileHash -LiteralPath $mountedDocument -Algorithm SHA256).Hash.ToLowerInvariant()
            Set-Content -LiteralPath $mountedAfter -Value $mountedHash -Encoding ascii -ErrorAction Stop
            $hostMountedAfterAvailable = $true
        }
    } catch {
        Write-Warning "Optional host-mounted-after hash unavailable: $($_.Exception.Message)"
    }
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        $failureEvidenceName = "probe-runner-failure.txt"; @("The dedicated FreeP rich-text probe exited without writing its manifest.", "docker-exit-code=$probeExitCode", "probe-log=$probeLog", "probe-output=$([string]::Join([Environment]::NewLine, @($probeOutput)))") | Set-Content -LiteralPath (Join-Path $evidenceDirectory $failureEvidenceName) -Encoding utf8
        $failureResults = @($requiredIds | ForEach-Object { [ordered]@{ id = $_; category = "physical-x11-rich-text-shortcut"; status = "failed"; evidenceLevel = "physical-x11-input"; evidence = @($failureEvidenceName); note = "Probe runner exited before producing row-specific evidence." } })
        $failureScreenshotName = $null; $initialScreenshotPath = Join-Path $sessionDirectory "screenshots/initial.png"
        if (Test-Path -LiteralPath $initialScreenshotPath -PathType Leaf) { $failureScreenshotName = "probe-runner-failure.png"; Copy-Item -LiteralPath $initialScreenshotPath -Destination (Join-Path $evidenceDirectory $failureScreenshotName) -Force }
        $failureScreenshots = if ($null -eq $failureScreenshotName) { @() } else { @([ordered]@{ name = $failureScreenshotName; kind = "screenshot" }) }
        [ordered]@{ schemaVersion = 1; suite = "freep-linux-rich-text-shortcut-physical"; platform = "linux"; shell = "avalonia"; app = "FreeP"; baseline = $false; appSurface = $surface; window = [ordered]@{ id = if ([string]::IsNullOrWhiteSpace([string]$ready.windowId)) { "unknown-owner" } else { [string]$ready.windowId }; title = if ([string]::IsNullOrWhiteSpace([string]$ready.windowTitle)) { "FreeP $fixtureFileName" } else { [string]$ready.windowTitle }; pattern = $fixtureFileName; visible = $true }; parameters = [ordered]@{ width = $Width; height = $Height; dpi = $Dpi; fixture = $fixtureFileName }; coverage = [ordered]@{ scope = $scope; exhaustive = $false; familyContract = "tools/Run-FamilyLinuxInteractionValidation.ps1 keeps its exact FreeP family contract." }; contractValidation = [ordered]@{ status = "pending"; validator = "tools/Run-FreePRichTextShortcutValidation.ps1"; contractReference = "tools/LinuxInteractiveDocker/freep-rich-text-shortcut-validation.schema.json" }; screenshots = $failureScreenshots; summary = [ordered]@{ passed = 0; failed = 5; total = 5 }; results = $failureResults } | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8
        Write-Warning "Probe did not write a manifest; deterministic failure manifest created at $manifestPath"
    } else {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $savedResult = @($manifest.results | Where-Object { $_.id -eq "saved-soft-break-native-package" })[0]
        if ($null -eq $savedResult) { throw "Probe manifest is missing saved-soft-break-native-package." }
        $evidenceNames = @("fixture-source-before.sha256.txt", "fixture-source-after.sha256.txt", "fixture-mounted-before.sha256.txt", "fixture-mounted-after.sha256.txt")
        $hashPaths = [ordered]@{ "source-before" = $sourceBefore; "source-after" = $sourceAfter; "mounted-before" = (Join-Path $evidenceDirectory "fixture-mounted-before.sha256.txt"); "mounted-after" = (Join-Path $evidenceDirectory "fixture-mounted-after.sha256.txt") }
        if ($hostMountedAfterAvailable) { $evidenceNames += "fixture-host-mounted-after.sha256.txt"; $hashPaths["host-mounted-after"] = $mountedAfter }
        Add-VisualEvidenceResultReferences -Result $savedResult -Names $evidenceNames
        $hashes = @{}; $hashFailures = [System.Collections.Generic.List[string]]::new()
        foreach ($entry in $hashPaths.GetEnumerator()) { if (-not (Test-Path -LiteralPath $entry.Value -PathType Leaf)) { $hashFailures.Add("$($entry.Key) hash artifact is missing"); continue }; if ((Get-Item -LiteralPath $entry.Value).Length -le 0) { $hashFailures.Add("$($entry.Key) hash artifact is empty"); continue }; $value = (Get-Content -LiteralPath $entry.Value -Raw).Trim(); if ($value -notmatch '^[0-9a-f]{64}$') { $hashFailures.Add("$($entry.Key) hash is not an exact lowercase 64-hex value"); continue }; $hashes[$entry.Key] = $value }
        foreach ($pair in @(@("source-before", "source-after"), @("source-before", "mounted-before"), @("mounted-after", "host-mounted-after"))) { if ($hashes.ContainsKey($pair[0]) -and $hashes.ContainsKey($pair[1]) -and $hashes[$pair[0]] -ne $hashes[$pair[1]]) { $hashFailures.Add("$($pair[0]) does not equal $($pair[1])") } }
        if ($surface -eq "in-canvas-grouped-child-pointer-selection") {
            if ($hashes.ContainsKey("mounted-before") -and $hashes.ContainsKey("mounted-after") -and $hashes["mounted-before"] -ne $hashes["mounted-after"]) { $hashFailures.Add("pointer-selection fixture changed even though the bounded contract is readback-only") }
        }
        elseif ($hashes.ContainsKey("mounted-before") -and $hashes.ContainsKey("mounted-after") -and $hashes["mounted-before"] -eq $hashes["mounted-after"]) { $hashFailures.Add("mounted-after does not differ from mounted-before after the final soft-break redo checkpoint save") }
        if ($hashFailures.Count -gt 0) { $savedResult.status = "failed"; $savedResult.note = "Rich-text source and saved working-copy SHA256 invariants failed: $([string]::Join('; ', $hashFailures))." }
        $manifest.summary.passed = @($manifest.results | Where-Object { $_.status -eq "passed" }).Count; $manifest.summary.failed = @($manifest.results | Where-Object { $_.status -eq "failed" }).Count; $manifest.summary.total = @($manifest.results).Count
        $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    }
    Wait-ForManifestEvidence -ManifestPath $manifestPath -EvidenceDirectory $evidenceDirectory
    Assert-GroupedCaretSemanticContract -Manifest $manifest -EvidenceDirectory $evidenceDirectory
    Assert-PointerSelectionSemanticContract -Manifest $manifest -EvidenceDirectory $evidenceDirectory
    $manifest = Assert-ManifestContract -ManifestPath $manifestPath -EvidenceDirectory $evidenceDirectory
    Write-Host "Manifest contract validation: $($manifest.contractValidation.status)"; Write-Host "Results: $($manifest.summary.passed) passed, $($manifest.summary.failed) failed, $($manifest.summary.total) total"; Write-Host "Manifest: $manifestPath"; Write-Host "Fixture: $fixturePath"
    if ($probeExitCode -ne 0 -or $manifest.summary.failed -gt 0) { throw "FreeP rich-text shortcut validation failed with probe exit code $probeExitCode and $($manifest.summary.failed) failed result(s). Evidence retained at $manifestPath." }
} finally {
    if ($started -and -not $KeepContainer) { try { Invoke-VisualEvidenceProcess -FilePath "powershell.exe" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner, "-Action", "Stop", "-App", "FreeP", "-Port", "$Port", "-OutputDir", $resolvedOutputRoot) -WorkingDirectory $repoRoot } catch { Write-Warning "Could not stop harness-owned FreeP container on port ${Port}: $($_.Exception.Message)" } } elseif ($started) { Write-Host "Container retained by request on port $Port." }
}
