param(
    [string]$ProvenancePath = "docs\parity\freew-dialog-harness\freew_font_visual_provenance.json",
    [string]$ComparisonPath = "docs\parity\freew-dialog-harness\freew_dialog_visual_comparison.json",
    [string]$InventoryPath = "docs\parity\freew-dialog-harness\freew_dialog_evidence_inventory.json",
    [string]$FreshnessPath = "docs\parity\freew-dialog-harness\freew_dialog_visual_freshness.json"
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

function Get-FileSha256([string]$path) {
    return (Get-FileHash -Algorithm SHA256 -LiteralPath (Resolve-RepoPath $path)).Hash.ToLowerInvariant()
}

function Get-NormalizedSourceSha256([string]$path) {
    $text = [IO.File]::ReadAllText((Resolve-RepoPath $path))
    $text = $text.Replace("`r`n", "`n").Replace("`r", "`n")
    $bytes = [Text.Encoding]::UTF8.GetBytes($text)
    $hash = [Security.Cryptography.SHA256]::Create().ComputeHash($bytes)
    return (-join ($hash | ForEach-Object { $_.ToString("x2") }))
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
Assert-Equal $provenance.wave 192 "wave"
Assert-Equal $provenance.routeId "font" "route"
Assert-Condition ([string]$provenance.generatedAtSourceRevision -match '^[0-9a-f]{40}$') "capture source revision is not a commit SHA."

$revisionCheck = git -C $repoRoot cat-file -e "$($provenance.generatedAtSourceRevision)^{commit}" 2>&1
Assert-Condition ($LASTEXITCODE -eq 0) "capture source revision '$($provenance.generatedAtSourceRevision)' is not present in the repository."

foreach ($source in @($provenance.sourceFiles)) {
    $sourcePath = Resolve-RepoPath $source.path
    Assert-Condition (Test-Path -LiteralPath $sourcePath) "source file is missing: $($source.path)."
    Assert-Equal (Get-NormalizedSourceSha256 $source.path) $source.sha256 "normalized source hash for $($source.path)"
}

Assert-Equal (Get-FileSha256 $InventoryPath) $provenance.trackedInputs.inventory.sha256 "inventory hash"
Assert-Equal (Get-FileSha256 $ComparisonPath) $provenance.trackedInputs.comparison.sha256 "comparison hash"
Assert-Equal (Get-FileSha256 $FreshnessPath) $provenance.trackedInputs.freshnessSidecar.sha256 "freshness sidecar hash"
Assert-Equal $freshness.inventorySha256 $provenance.trackedInputs.inventory.sha256 "freshness inventory identity"
Assert-Equal $freshness.wpfSha256 "d32578baacc83a2069e83583489b54cfcd606eae572f915f623f59bfe82d8374" "WPF manifest identity"
Assert-Equal $freshness.avaloniaSha256 "5443d0a8c60fb77d8d56a35e8c55e329db6bf772a006f94c3f3c4602bd18ed74" "Avalonia manifest identity"

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
        Assert-Condition $content.passesContentGate "content gate is not passed for $captureHost/$($bundleRow.scenarioId)."
        Assert-Equal $capture.trackedRow.path $provenance.trackedInputs.comparison.path "tracked row path for $captureHost/$($bundleRow.scenarioId)"
        Assert-Equal $capture.trackedRow.sha256 $provenance.trackedInputs.comparison.sha256 "tracked row artifact hash for $captureHost/$($bundleRow.scenarioId)"
        Assert-Equal $capture.trackedRow.jsonPointer $bundleRow.comparisonRowPointer "tracked row pointer for $captureHost/$($bundleRow.scenarioId)"
        Assert-Equal $capture.trackedRow.sha256OfCanonicalRow $bundleRow.comparisonRowSha256 "tracked row hash for $captureHost/$($bundleRow.scenarioId)"
        $expectedManifestHash = if ($captureHost -eq "wpf") { $freshness.wpfSha256 } else { $freshness.avaloniaSha256 }
        Assert-Equal $capture.captureArtifact.sha256 $expectedManifestHash "external capture identity for $captureHost/$($bundleRow.scenarioId)"
        Assert-Condition (-not $capture.captureArtifact.tracked) "external capture must not be reported as tracked for $captureHost/$($bundleRow.scenarioId)."

        $inventoryRow = @($inventory.scenarios | Where-Object id -eq $capture.inventoryId)
        Assert-Equal $inventoryRow.Count 1 "inventory row for $($capture.inventoryId)"
        Assert-Equal $inventoryRow[0].host $captureHost "inventory host for $($capture.inventoryId)"
        Assert-Equal $inventoryRow[0].state $expectedState "inventory state for $($capture.inventoryId)"

        $externalPath = Resolve-RepoPath $capture.captureArtifact.path
        if (Test-Path -LiteralPath $externalPath) {
            Assert-Equal (Get-FileSha256 $capture.captureArtifact.path) $capture.captureArtifact.sha256 "present external capture manifest hash for $host/$($bundleRow.scenarioId)"
        }
    }
}

Write-Host "FreeW Font visual provenance passed: 3 states, 6 host captures, 6 exact row bindings."
if (@($provenance.captures | Where-Object { -not (Test-Path -LiteralPath (Resolve-RepoPath $_.captureArtifact.path)) }).Count -gt 0) {
    Write-Host "External capture manifests are absent locally; repository-backed row summaries and source hashes are current, as documented."
}
