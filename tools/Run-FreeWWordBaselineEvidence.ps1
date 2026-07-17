param(
    [string]$RunRoot = (Join-Path $PSScriptRoot "..\freew-fidelity-corpus\runs\visual-evidence"),
    [string]$Configuration = "Release",
    [int]$MaxPagesPerDocument = 4,
    [string]$WordApplicationProgId = "Word.Application",
    [switch]$AllowMissingWord,
    [switch]$NoWord,
    [switch]$UseVisibleWordPublish,
    [switch]$SkipEvidenceRender
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Test-ComProgIdAvailable([string]$ProgId) {
    if ([string]::IsNullOrWhiteSpace($ProgId)) {
        return $false
    }

    $type = [type]::GetTypeFromProgID($ProgId, $false)
    return $null -ne $type
}

function Get-PngDimensions([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 24 -or $bytes[0] -ne 0x89 -or $bytes[1] -ne 0x50 -or $bytes[2] -ne 0x4E -or $bytes[3] -ne 0x47) {
        throw "Expected a PNG evidence image: $Path"
    }

    [pscustomobject]@{
        Width = ([uint32]$bytes[16] -shl 24) -bor ([uint32]$bytes[17] -shl 16) -bor ([uint32]$bytes[18] -shl 8) -bor [uint32]$bytes[19]
        Height = ([uint32]$bytes[20] -shl 24) -bor ([uint32]$bytes[21] -shl 16) -bor ([uint32]$bytes[22] -shl 8) -bor [uint32]$bytes[23]
    }
}

function Find-EvidencePage([string]$Stem) {
    $candidates = @(
        (Join-Path $wpfDir ($Stem + "_p1.png")),
        (Join-Path $wpfDir (($Stem -replace "-fidelity$", "") + "_p1.png"))
    )
    return $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}

function Get-JsonArray($Value) {
    if ($null -eq $Value) {
        return @()
    }

    return @($Value)
}

function Write-NoWordReadinessManifest([string]$Reason, [string]$UnavailableMarkerPath) {
    if (-not (Test-Path -LiteralPath $summaryJson -PathType Leaf)) {
        throw "expected normalized visual evidence summary was not found: $summaryJson"
    }

    $summary = Get-Content -LiteralPath $summaryJson -Raw | ConvertFrom-Json
    $comparisons = Get-JsonArray $summary.baselineComparisons
    $unavailableComparisons = @($comparisons | Where-Object { $_.status -eq "word-baseline-unavailable" })
    $remainingBlockers = Get-JsonArray $summary.remainingEvidenceBlockers
    $wordUnavailableBlockers = @($remainingBlockers | Where-Object {
        $_.status -eq "word-baseline-unavailable" -or
        (Get-JsonArray $_.relatedBaselineStatuses) -contains "word-baseline-unavailable"
    })
    $candidateBaselinePaths = @(
        $unavailableComparisons |
            ForEach-Object { Get-JsonArray $_.candidateBaselinePaths } |
            Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
            Sort-Object -Unique
    )
    $scenarioIds = @(
        $unavailableComparisons |
            ForEach-Object { $_.scenarioId } |
            Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
            Sort-Object -Unique
    )
    $blockerIds = @(
        $wordUnavailableBlockers |
            ForEach-Object { $_.blockerId } |
            Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
            Sort-Object -Unique
    )

    $trustFailures = New-Object System.Collections.Generic.List[string]
    if ([bool]$summary.evidenceAuthority.authoritativeWordPngParityClaimed) {
        $trustFailures.Add("no-Word run must not claim authoritative Word PNG parity")
    }
    if ([int]$summary.evidenceAuthority.realWordPngComparedRows -ne 0) {
        $trustFailures.Add("no-Word run must not report real Word PNG comparison rows")
    }
    if ($unavailableComparisons.Count -eq 0) {
        $trustFailures.Add("no-Word run must include word-baseline-unavailable comparison rows")
    }
    if ($candidateBaselinePaths.Count -eq 0) {
        $trustFailures.Add("no-Word run must record candidate Word baseline PNG paths")
    }

    $readinessPath = Join-Path $runRootFull "_word_baseline_readiness_manifest.json"
    [ordered]@{
        schema = "freew.word-baseline-readiness.v1"
        schemaVersion = 1
        status = "word-baseline-unavailable"
        evidenceMode = "no-word-fallback"
        baselineEvidenceClass = "word-baseline-unavailable"
        authoritativeWordPngParity = $false
        reason = $Reason
        allowMissingWord = [bool]$AllowMissingWord
        forcedNoWord = [bool]$NoWord
        unavailableMarkerPath = $UnavailableMarkerPath
        summary = [ordered]@{
            path = $summaryJson
            schemaVersion = [int]$summary.schemaVersion
            trustPassed = [bool]$summary.trust.passed
            authorityLevel = [string]$summary.evidenceAuthority.authorityLevel
            authoritativeWordPngParityClaimed = [bool]$summary.evidenceAuthority.authoritativeWordPngParityClaimed
        }
        counts = [ordered]@{
            evidenceRows = (Get-JsonArray $summary.evidence).Count
            baselineComparisons = $comparisons.Count
            wordBaselineUnavailableRows = $unavailableComparisons.Count
            realWordPngComparedRows = [int]$summary.evidenceAuthority.realWordPngComparedRows
            missingWordBaselineRows = [int]$summary.evidenceAuthority.missingWordBaselineRows
            remainingWordBaselineBlockers = $wordUnavailableBlockers.Count
        }
        comparableScenarioIds = $scenarioIds
        candidateBaselinePaths = $candidateBaselinePaths
        remainingWordBaselineBlockerIds = $blockerIds
        wordCapableHostNextStep = "Run this same evidence path on a host where Word.Application is registered to replace word-baseline-unavailable rows with real Word PNG comparisons."
        trust = [ordered]@{
            passed = $trustFailures.Count -eq 0
            failures = @($trustFailures)
        }
        createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
    } | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $readinessPath

    Write-Host "Word baseline readiness manifest: $readinessPath"

    if ($trustFailures.Count -gt 0) {
        throw "no-Word readiness manifest failed validation: $($trustFailures -join '; ')"
    }
}

function Write-WordBaselineUnavailableSummary([string]$Reason) {
    $skipPath = Join-Path $runRootFull "_word_baseline_unavailable.json"
    [ordered]@{
        status = "word-baseline-unavailable"
        evidenceMode = "no-word-fallback"
        baselineEvidenceClass = "word-baseline-unavailable"
        authoritativeWordPngParity = $false
        reason = $Reason
        allowMissingWord = [bool]$AllowMissingWord
        forcedNoWord = [bool]$NoWord
        trust = [ordered]@{
            passed = $true
            failures = @()
        }
        summaryRowStatus = "word-baseline-unavailable"
        createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
    } | ConvertTo-Json | Set-Content -Encoding UTF8 $skipPath

    if (-not $AllowMissingWord -and -not $NoWord) {
        Write-Error "Word baseline is unavailable: $Reason. Re-run with -AllowMissingWord to verify the no-Word summary path."
        exit 3
    }

    Write-Host "Word baseline unavailable: $skipPath"
    Write-Host "Word baseline mode: no-word-fallback"

    Invoke-DotNetRun $summaryProject @(
        "--run-root", $runRootFull,
        "--manifest", $wpfManifest,
        "--manifest", $avaloniaManifest,
        "--word-baseline-scope", "generated-corpus",
        "--baseline-tolerance", "word-png-default",
        "--allow-no-word-fallback-evidence",
        "--word-baseline-unavailable-reason", $Reason,
        "--output-json", $summaryJson,
        "--output-md", $summaryMd) -Configuration $Configuration

    Write-Host "summary json: $summaryJson"
    Write-Host "summary markdown: $summaryMd"
    Write-NoWordReadinessManifest $Reason $skipPath
    exit 0
}

$repoRoot = Resolve-ToolProviderPath (Join-Path $PSScriptRoot "..")
$runRootFull = Resolve-ToolProviderPath $RunRoot
$fixtureDir = Join-Path $runRootFull "fixtures"
$wpfDir = Join-Path $runRootFull "wpf"
$avaloniaDir = Join-Path $runRootFull "avalonia"
$wordPdfDir = Join-Path $runRootFull "word-pdf"
$wordBaselineDir = Join-Path $runRootFull "word-baseline"
$summaryJson = Join-Path $runRootFull "freew_visual_evidence_summary.json"
$summaryMd = Join-Path $runRootFull "freew_visual_evidence_summary.md"
$fidelityRenderProject = Join-Path $repoRoot "freew\tools\FreeW.FidelityRender\FreeW.FidelityRender.csproj"
$pageLayoutShotProject = Join-Path $repoRoot "freew\tools\FreeW.PageLayoutShot\FreeW.PageLayoutShot.csproj"
$pdfRasterizeProject = Join-Path $repoRoot "freew\tools\FreeW.PdfRasterize\FreeW.PdfRasterize.csproj"
$summaryProject = Join-Path $repoRoot "freew\tools\FreeW.VisualEvidenceSummary\FreeW.VisualEvidenceSummary.csproj"
$wordExportScript = if ($UseVisibleWordPublish) {
    Join-Path $repoRoot "tools\FreeW.RenderCompare\Export-WordPdfsVisible.ps1"
} else {
    Join-Path $repoRoot "tools\FreeW.RenderCompare\Export-WordPdfs.ps1"
}

New-Item -ItemType Directory -Force -Path $runRootFull, $fixtureDir, $wpfDir, $avaloniaDir, $wordPdfDir, $wordBaselineDir | Out-Null

if (-not $SkipEvidenceRender) {
    Invoke-DotNetBuild $fidelityRenderProject -Configuration $Configuration
    Invoke-DotNetRunNoBuild $fidelityRenderProject @("--generate-f2-corpus", $fixtureDir) -Configuration $Configuration
    Invoke-DotNetRunNoBuild $fidelityRenderProject @($fixtureDir, $wpfDir, $MaxPagesPerDocument.ToString([Globalization.CultureInfo]::InvariantCulture), "--composite", "--software-fallback") -Configuration $Configuration
    Invoke-DotNetRun $pageLayoutShotProject @($avaloniaDir) -Configuration $Configuration
}

$wpfManifest = Join-Path $wpfDir "freew_visual_evidence_manifest.json"
$avaloniaManifest = Join-Path $avaloniaDir "freew_visual_evidence_manifest.json"
if (-not (Test-Path $wpfManifest) -or -not (Test-Path $avaloniaManifest)) {
    throw "expected visual evidence manifests were not found under '$wpfDir' and '$avaloniaDir'"
}

if ($NoWord) {
    Write-WordBaselineUnavailableSummary "Word baseline skipped by -NoWord; no COM probe or Word process launch attempted"
}

$wordAvailable = Test-ComProgIdAvailable $WordApplicationProgId
if (-not $wordAvailable) {
    Write-WordBaselineUnavailableSummary "COM ProgID '$WordApplicationProgId' is not registered"
}

try {
    $wordExportArgs = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $wordExportScript,
        "-CorpusDir", $fixtureDir,
        "-OutDir", $wordPdfDir)
    if ($UseVisibleWordPublish) {
        $wordExportArgs += @("-WordApplicationProgId", $WordApplicationProgId)
    }

    & powershell.exe @wordExportArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Word PDF export failed with exit code $LASTEXITCODE"
    }

    foreach ($pdf in Get-ChildItem -Path $wordPdfDir -Filter *.pdf | Sort-Object Name) {
        $rasterArgs = @($pdf.FullName, $wordBaselineDir)
        $evidencePage = Find-EvidencePage ([IO.Path]::GetFileNameWithoutExtension($pdf.Name))
        if ($evidencePage) {
            $dimensions = Get-PngDimensions $evidencePage
            # FreeW.PdfRasterize applies a 120-to-96 DPI conversion internally, so
            # pass the pre-conversion surface that yields the evidence dimensions.
            $rasterArgs += @(
                ([int]($dimensions.Width * 1.25)),
                ([int]($dimensions.Height * 1.25)))
        }

        Invoke-DotNetRun $pdfRasterizeProject $rasterArgs -Configuration $Configuration
    }

    foreach ($page in 1..[Math]::Max(1, $MaxPagesPerDocument)) {
        $printPreviewSource = Join-Path $wordBaselineDir ("backstage-print-preview-fidelity_p{0}.png" -f $page)
        $printPreviewTarget = Join-Path $wordBaselineDir ("backstage-print-preview_p{0}.png" -f $page)
        if ((Test-Path $printPreviewSource) -and -not (Test-Path $printPreviewTarget)) {
            Copy-Item -LiteralPath $printPreviewSource -Destination $printPreviewTarget
        }

        $pdfExportSource = Join-Path $wordBaselineDir ("backstage-pdf-export-fidelity_p{0}.png" -f $page)
        $pdfExportTarget = Join-Path $wordBaselineDir ("backstage-pdf-export_p{0}.png" -f $page)
        if ((Test-Path $pdfExportSource) -and -not (Test-Path $pdfExportTarget)) {
            Copy-Item -LiteralPath $pdfExportSource -Destination $pdfExportTarget
        }
    }
}
catch {
    Write-WordBaselineUnavailableSummary "MS Word baseline PNG generation failed: $($_.Exception.Message)"
}

Invoke-DotNetRun $summaryProject @(
    "--run-root", $runRootFull,
    "--manifest", $wpfManifest,
    "--manifest", $avaloniaManifest,
    "--word-baseline-dir", $wordBaselineDir,
    "--word-baseline-scope", "generated-corpus",
    "--baseline-tolerance", "word-png-default",
    "--output-json", $summaryJson,
    "--output-md", $summaryMd) -Configuration $Configuration

Write-Host "fixtures: $((Get-ChildItem -Path $fixtureDir -Filter *.docx).Count)"
Write-Host "word baseline PNGs: $((Get-ChildItem -Path $wordBaselineDir -Filter *.png).Count)"
Write-Host "Word baseline mode: real-word-png-comparison"
Write-Host "summary json: $summaryJson"
Write-Host "summary markdown: $summaryMd"
