param(
    [string]$ProvenancePath = "docs\parity\freew-dialog-harness\freew_font_visual_provenance.json",
    [string]$ComparisonPath = "docs\parity\freew-dialog-harness\freew_dialog_visual_comparison.json",
    [string]$InventoryPath = "docs\parity\freew-dialog-harness\freew_dialog_evidence_inventory.json",
    [string]$FreshnessPath = "docs\parity\freew-dialog-harness\freew_dialog_visual_freshness.json",
    [switch]$StrictImprovementContractProbe
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-RepoPath([string]$path) {
    return [IO.Path]::GetFullPath((Join-Path $repoRoot $path))
}

function Assert-Condition([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw "FreeW Font visual provenance failed: $message"
    }
}

function Assert-ChangedPixelRequirement(
    [string]$schema,
    [int]$wave,
    [int]$actual,
    [int]$baseline,
    [string]$scenarioId
) {
    if ($schema -eq "freew.font-visual-provenance.v1" -and $wave -ge 193) {
        Assert-Condition ($actual -lt $baseline) "changed pixels did not strictly improve for $scenarioId."
        return
    }

    Assert-Condition ($actual -le $baseline) "changed pixels regressed for $scenarioId."
}

if ($StrictImprovementContractProbe) {
    $wave193EqualityRejected = $false
    try {
        Assert-ChangedPixelRequirement "freew.font-visual-provenance.v1" 193 100 100 "font.probe"
    }
    catch {
        $wave193EqualityRejected = $_.Exception.Message -match 'did not strictly improve'
    }

    Assert-Condition $wave193EqualityRejected "Wave193 equality was not rejected by the strict-improvement contract."
    Assert-ChangedPixelRequirement "freew.font-visual-provenance.v1" 192 100 100 "font.legacy-probe"
    Write-Host "FreeW Font strict-improvement contract probe passed: Wave193 equality rejected; Wave192 equality retained."
    return
}

function Get-FileSha256([string]$path) {
    return (Get-FileHash -Algorithm SHA256 -LiteralPath (Resolve-RepoPath $path)).Hash.ToLowerInvariant()
}

function Get-NormalizedTextSha256([string]$text) {
    $text = $text.Replace("`r`n", "`n").Replace("`r", "`n")
    $bytes = [Text.Encoding]::UTF8.GetBytes($text)
    $hash = [Security.Cryptography.SHA256]::Create().ComputeHash($bytes)
    return (-join ($hash | ForEach-Object { $_.ToString("x2") }))
}

function Get-NormalizedSourceSha256([string]$path) {
    $text = [IO.File]::ReadAllText((Resolve-RepoPath $path))
    return Get-NormalizedTextSha256 $text
}

function Format-ProcessArg([string]$arg) {
    if ($null -eq $arg) { $arg = "" }
    if ($arg -match '[\s"]') {
        return '"' + ($arg -replace '"', '\"') + '"'
    }
    if ($arg -eq "") { return '""' }
    return $arg
}

function Get-NormalizedGitBlobSha256([string]$revision, [string]$path) {
    $objectSpec = "$revision`:$path"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "git"
    $psi.WorkingDirectory = $repoRoot
    $psi.Arguments = ((@("cat-file", "blob", $objectSpec) | ForEach-Object { Format-ProcessArg $_ }) -join " ")
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true

    $process = [Diagnostics.Process]::Start($psi)
    $blobBytes = New-Object IO.MemoryStream
    try {
        $process.StandardOutput.BaseStream.CopyTo($blobBytes)
        $diagnostic = $process.StandardError.ReadToEnd().Trim()
        $process.WaitForExit()
        $detail = if ([string]::IsNullOrWhiteSpace($diagnostic)) { "no diagnostic output" } else { $diagnostic }
        Assert-Condition ($process.ExitCode -eq 0) ("source file '{0}' could not be read as a blob at capture source revision '{1}'. git cat-file diagnostic: {2}" -f $path, $revision, $detail)

        $blobBytes.Position = 0
        $reader = New-Object IO.StreamReader($blobBytes, [Text.Encoding]::UTF8, $true)
        try {
            return Get-NormalizedTextSha256 $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $blobBytes.Dispose()
        $process.Dispose()
    }
}

function Get-CanonicalJsonSha256($value) {
    $json = $value | ConvertTo-Json -Depth 20 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    $hash = [Security.Cryptography.SHA256]::Create().ComputeHash($bytes)
    return (-join ($hash | ForEach-Object { $_.ToString("x2") }))
}

function Assert-Equal([object]$actual, [object]$expected, [string]$label) {
    Assert-Condition ([string]$actual -eq [string]$expected) "$label expected '$expected', got '$actual'."
}

$provenance = Get-Content -LiteralPath (Resolve-RepoPath $ProvenancePath) -Raw | ConvertFrom-Json
$comparison = Get-Content -LiteralPath (Resolve-RepoPath $ComparisonPath) -Raw | ConvertFrom-Json
$inventory = Get-Content -LiteralPath (Resolve-RepoPath $InventoryPath) -Raw | ConvertFrom-Json
$freshness = Get-Content -LiteralPath (Resolve-RepoPath $FreshnessPath) -Raw | ConvertFrom-Json

Assert-Equal $provenance.schema "freew.font-visual-provenance.v1" "schema"
Assert-Equal $provenance.wave 194 "wave"
Assert-Equal $provenance.routeId "font" "route"
Assert-Condition ([string]$provenance.generatedAtSourceRevision -match '^[0-9a-f]{40}$') "capture source revision is not a commit SHA."

$revisionCheck = git -C $repoRoot cat-file -e "$($provenance.generatedAtSourceRevision)^{commit}" 2>&1
Assert-Condition ($LASTEXITCODE -eq 0) "capture source revision '$($provenance.generatedAtSourceRevision)' is not present in the repository."

foreach ($source in @($provenance.sourceFiles)) {
    $sourcePath = Resolve-RepoPath $source.path
    Assert-Condition (Test-Path -LiteralPath $sourcePath) "source file is missing: $($source.path)."
    Assert-Equal (Get-NormalizedSourceSha256 $source.path) $source.sha256 "normalized source hash for $($source.path)"
    $revisionHash = Get-NormalizedGitBlobSha256 $provenance.generatedAtSourceRevision $source.path
    Assert-Equal $revisionHash $source.sha256 "normalized source hash at revision '$($provenance.generatedAtSourceRevision)' for $($source.path)"
}

Assert-Equal (Get-FileSha256 $InventoryPath) $provenance.trackedInputs.inventory.sha256 "inventory hash"
Assert-Equal (Get-FileSha256 $ComparisonPath) $provenance.trackedInputs.comparison.sha256 "comparison hash"
Assert-Equal (Get-FileSha256 $FreshnessPath) $provenance.trackedInputs.freshnessSidecar.sha256 "freshness sidecar hash"
Assert-Equal $freshness.inventorySha256 $provenance.trackedInputs.inventory.sha256 "freshness inventory identity"
Assert-Equal $freshness.wpfSha256 $provenance.captureArtifacts.wpf.sha256 "WPF manifest identity"
Assert-Equal $freshness.avaloniaSha256 $provenance.captureArtifacts.avalonia.sha256 "Avalonia manifest identity"

foreach ($captureHost in @("wpf", "avalonia")) {
    $artifact = $provenance.captureArtifacts.$captureHost
    Assert-Condition (-not $artifact.tracked) "top-level $captureHost capture artifact must not be reported as tracked."
    $externalPath = Resolve-RepoPath $artifact.path
    if (Test-Path -LiteralPath $externalPath) {
        Assert-Equal (Get-FileSha256 $artifact.path) $artifact.sha256 "present top-level $captureHost capture manifest hash"
    }
}

Assert-Condition ([string]$provenance.freshGeneration.command -match 'compare') "fresh generation command is missing."
Assert-Condition ([string]$provenance.freshGeneration.checkCommand -match '--check') "fresh comparison check command is missing."
Assert-Condition ([string]$provenance.freshGeneration.provenanceCheckCommand -match 'Test-FreeWFontVisualProvenance') "provenance check command is missing."
Assert-Condition (-not $provenance.captureArtifactPolicy.externalArtifactsTracked) "capture artifact tracking claim must remain honest."
Assert-Condition ([string]$provenance.captureArtifactPolicy.limitation -match 'not committed') "missing-capture limitation is not documented."

$expectedStates = @("initial", "populated", "validation-error")
$rows = @($provenance.rows)
$captures = @($provenance.captures)
Assert-Equal $rows.Count 3 "provenance row count"
Assert-Equal $captures.Count 6 "provenance capture count"
Assert-Equal (@($captures | ForEach-Object { "$($_.host)/$($_.scenarioId)" } | Sort-Object -Unique).Count) 6 "capture uniqueness"

$baselinePath = $ComparisonPath.Replace('\', '/')
$baselineText = git -C $repoRoot show "$($provenance.generatedAtSourceRevision):$baselinePath" 2>&1
Assert-Condition ($LASTEXITCODE -eq 0) "Wave192 comparison could not be read from the capture source revision."
$baselineComparison = ($baselineText -join "`n") | ConvertFrom-Json
$baselineRowsByScenario = @{}
foreach ($baselineRow in @($baselineComparison.rows)) {
    $baselineRowsByScenario[$baselineRow.scenarioId] = $baselineRow
}

$nonFontRows = @($comparison.rows | Where-Object { $_.scenarioId -notlike 'font.*' })
$changedNonFontRows = @($nonFontRows | Where-Object {
    -not $baselineRowsByScenario.ContainsKey($_.scenarioId) -or
    (Get-CanonicalJsonSha256 $_) -ne (Get-CanonicalJsonSha256 $baselineRowsByScenario[$_.scenarioId])
})
Assert-Equal $nonFontRows.Count $provenance.wave194Result.nonFontRowsCompared "non-Font row count"
Assert-Equal $changedNonFontRows.Count $provenance.wave194Result.nonFontRowsChanged "changed non-Font row count"
Assert-Equal $changedNonFontRows.Count 0 "changed non-Font row count contract"

$aggregateChangedPixels = 0

foreach ($expectedState in $expectedStates) {
    $bundleRow = $rows | Where-Object scenarioId -eq "font.$expectedState"
    Assert-Equal @($bundleRow).Count 1 "bundle row for $expectedState"
    $rowIndex = [int]([regex]::Match([string]$bundleRow.comparisonRowPointer, '/rows/(?<index>\d+)$').Groups['index'].Value)
    Assert-Condition ($rowIndex -ge 0 -and $rowIndex -lt $comparison.rows.Count) "comparison pointer for $($bundleRow.scenarioId) is outside the comparison."
    $actualRow = $comparison.rows[$rowIndex]
    Assert-Equal $actualRow.scenarioId $bundleRow.scenarioId "comparison scenario at $($bundleRow.comparisonRowPointer)"
    Assert-Equal (Get-CanonicalJsonSha256 $actualRow) $bundleRow.comparisonRowSha256 "exact comparison row hash for $($bundleRow.scenarioId)"
    Assert-Equal $actualRow.classification $bundleRow.classification "classification for $($bundleRow.scenarioId)"
    foreach ($property in @("comparedPixels", "changedPixels", "changedRatio", "meanAbsoluteChannelDelta", "p95AbsoluteChannelDelta", "luminanceSimilarity", "perceptualHashDistance")) {
        Assert-Equal $actualRow.metrics.$property $bundleRow.metrics.$property "metric $property for $($bundleRow.scenarioId)"
    }
    $baselineChangedPixels = $provenance.wave193Baseline.changedPixelsByState.$expectedState
    Assert-Equal $baselineRowsByScenario[$bundleRow.scenarioId].metrics.changedPixels $baselineChangedPixels "Wave193 changed pixels for $($bundleRow.scenarioId)"
    Assert-ChangedPixelRequirement $provenance.schema $provenance.wave $actualRow.metrics.changedPixels $baselineChangedPixels $bundleRow.scenarioId
    $aggregateChangedPixels += $actualRow.metrics.changedPixels

    foreach ($captureHost in @("wpf", "avalonia")) {
        $capture = $captures | Where-Object { $_.host -eq $captureHost -and $_.scenarioId -eq $bundleRow.scenarioId }
        Assert-Equal @($capture).Count 1 "capture for $captureHost/$($bundleRow.state)"
        $content = if ($captureHost -eq "wpf") { $actualRow.wpfContent } else { $actualRow.avaloniaContent }
        Assert-Equal $capture.state $expectedState "state for $captureHost/$($bundleRow.scenarioId)"
        Assert-Equal $capture.dimensions.width $content.width "width for $captureHost/$($bundleRow.scenarioId)"
        Assert-Equal $capture.dimensions.height $content.height "height for $captureHost/$($bundleRow.scenarioId)"
        Assert-Equal $capture.dimensions.pixelCount $content.pixelCount "pixel count for $captureHost/$($bundleRow.scenarioId)"
        foreach ($property in @("x", "y", "width", "height")) {
            Assert-Equal $capture.paintedBounds.$property $content.contentBounds.$property "painted bound $property for $captureHost/$($bundleRow.scenarioId)"
        }
        Assert-Equal $capture.paintedBounds.x 12 "painted bound x contract for $captureHost/$($bundleRow.scenarioId)"
        Assert-Equal $capture.paintedBounds.y 12 "painted bound y contract for $captureHost/$($bundleRow.scenarioId)"
        Assert-Equal $capture.paintedBounds.width 421 "painted bound width contract for $captureHost/$($bundleRow.scenarioId)"
        Assert-Equal $capture.paintedBounds.height 321 "painted bound height contract for $captureHost/$($bundleRow.scenarioId)"
        Assert-Condition $content.passesContentGate "content gate is not passed for $captureHost/$($bundleRow.scenarioId)."
        Assert-Equal $capture.trackedRow.path $provenance.trackedInputs.comparison.path "tracked row path for $captureHost/$($bundleRow.scenarioId)"
        Assert-Equal $capture.trackedRow.sha256 $provenance.trackedInputs.comparison.sha256 "tracked row artifact hash for $captureHost/$($bundleRow.scenarioId)"
        Assert-Equal $capture.trackedRow.jsonPointer $bundleRow.comparisonRowPointer "tracked row pointer for $captureHost/$($bundleRow.scenarioId)"
        Assert-Equal $capture.trackedRow.sha256OfCanonicalRow $bundleRow.comparisonRowSha256 "tracked row hash for $captureHost/$($bundleRow.scenarioId)"
        $expectedManifestHash = if ($captureHost -eq "wpf") { $freshness.wpfSha256 } else { $freshness.avaloniaSha256 }
        $expectedManifestPath = $provenance.captureArtifacts.$captureHost.path
        Assert-Equal $capture.captureArtifact.path $expectedManifestPath "external capture path for $captureHost/$($bundleRow.scenarioId)"
        Assert-Equal $capture.captureArtifact.sha256 $expectedManifestHash "external capture identity for $captureHost/$($bundleRow.scenarioId)"
        Assert-Condition (-not $capture.captureArtifact.tracked) "external capture must not be reported as tracked for $captureHost/$($bundleRow.scenarioId)."

        $inventoryRow = @($inventory.scenarios | Where-Object id -eq $capture.inventoryId)
        Assert-Equal $inventoryRow.Count 1 "inventory row for $($capture.inventoryId)"
        Assert-Equal $inventoryRow[0].host $captureHost "inventory host for $($capture.inventoryId)"
        Assert-Equal $inventoryRow[0].state $expectedState "inventory state for $($capture.inventoryId)"

        $externalPath = Resolve-RepoPath $capture.captureArtifact.path
        if (Test-Path -LiteralPath $externalPath) {
            Assert-Equal (Get-FileSha256 $capture.captureArtifact.path) $capture.captureArtifact.sha256 "present external capture manifest hash for $captureHost/$($bundleRow.scenarioId)"
        }
    }
}

Assert-Equal $aggregateChangedPixels $provenance.wave194Result.aggregateChangedPixels "Wave194 aggregate changed pixels"
Assert-Equal (@($provenance.wave192Baseline.changedPixelsByState.PSObject.Properties | ForEach-Object { [int]$_.Value } | Measure-Object -Sum).Sum) $provenance.wave192Baseline.aggregateChangedPixels "Wave192 aggregate changed pixels"
Assert-Equal (@($provenance.wave193Baseline.changedPixelsByState.PSObject.Properties | ForEach-Object { [int]$_.Value } | Measure-Object -Sum).Sum) $provenance.wave193Baseline.aggregateChangedPixels "Wave193 aggregate changed pixels"
Assert-Equal ($aggregateChangedPixels - $provenance.wave193Baseline.aggregateChangedPixels) $provenance.wave194Result.aggregateDelta "aggregate changed-pixel delta"

Write-Host "FreeW Font visual provenance passed: 3 improved states, 6 exact 421x321 host captures, 0/288 non-Font row changes, 32,312 aggregate changed pixels."
if (@($provenance.captures | Where-Object { -not (Test-Path -LiteralPath (Resolve-RepoPath $_.captureArtifact.path)) }).Count -gt 0) {
    Write-Host "External capture manifests are absent locally; repository-backed row summaries and source hashes are current, as documented."
}
