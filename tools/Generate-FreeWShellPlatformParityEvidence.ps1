param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$jsonPath = Join-Path $repo 'docs\parity\freew-shell-platform-parity-20260720.json'
$markdownPath = Join-Path $repo 'docs\parity\freew-shell-platform-parity-20260720.md'

$sourceFiles = @(
    'freew/FreeW.App.Host/MainWindow.cs',
    'freew/FreeW.App.Host/XpsExport.cs',
    'freew/FreeW.App.Host/PrintPreviewWindow.cs',
    'freew/FreeW.App.Avalonia/MainWindow.cs',
    'freew/FreeW.App.Avalonia/PrintPreviewDialog.cs',
    'freew/FreeW.App.Avalonia/Printing/CupsPrintDialog.cs',
    'freew/FreeW.App.Avalonia/Pdf/FreeWAvaloniaPdfExport.cs',
    'freew/FreeW.App.Avalonia/Pdf/FreeWAvaloniaXpsExport.cs',
    'freew/FreeW.App.Avalonia/Printing/CupsPrintCommandPlanner.cs',
    'freew/FreeW.App.Avalonia/Printing/CupsPrintService.cs',
    'freew/FreeW.App.Avalonia/Editing/DocumentView.cs',
    'freew/FreeW.App.Avalonia.Tests/Printing/CupsPrintServiceTests.cs',
    'freew/FreeW.App.Avalonia.Tests/Printing/PortableXpsWriterTests.cs',
    'freew/FreeW.App.Presentation/Printing/PrintSelectionPlanner.cs',
    'freew/FreeW.App.Presentation.Tests/Printing/PrintSelectionPlannerTests.cs',
    'shared/Free.Shared.AppServices/Printing/PrintContracts.cs',
    'shared/Free.Shared.AppServices/Printing/PrintProcessContracts.cs',
    'shared/Free.Shared.AppServices/Printing/SystemProcessRunner.cs',
    'shared/Free.Shared.Pdf/PdfContentDocument.cs',
    'shared/Free.Shared.Pdf/PdfDrawOp.cs',
    'shared/Free.Shared.Pdf/PortableXpsWriter.cs'
    ,'shared/Free.Shared.Pdf.Skia/SkiaPdfWriter.cs'
)

$hashes = [ordered]@{}
foreach ($relative in $sourceFiles) {
    $path = Join-Path $repo ($relative -replace '/', '\')
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing evidence input: $relative" }
    $hashes[$relative] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$surfaces = @(
    [ordered]@{ name = 'WPF native printing'; status = 'implemented'; authority = 'FreeW.App.Host.MainWindow.Print uses System.Windows.Controls.PrintDialog, PrintTicket, PrintLayout.BuildPaginator, and PrintDocument.'; implementation = 'Preserved unchanged; evidence hashes the authoritative source.'; tests = 'Existing WPF host print/paginator tests remain the authority.' },
    [ordered]@{ name = 'WPF native XPS'; status = 'implemented'; authority = 'FreeW.App.Host.XpsExport uses System.Windows.Xps.Packaging.XpsDocument and XpsDocumentWriter on the WPF paginator.'; implementation = 'Preserved unchanged; it writes a real FixedDocumentSequence OPC package on STA.'; tests = 'Existing FreeW.App.Host.Tests.XpsExportTests cover package/page output.' },
    [ordered]@{ name = 'Linux/macOS printer discovery'; status = 'implemented'; authority = 'CUPS lpstat -p and lpstat -d contract.'; implementation = 'CupsPrintService with injected IProcessRunner and explicit no-printer/unavailable/failed/cancelled results.'; tests = 'CupsPrintServiceTests parse printers/default and cancellation.' },
    [ordered]@{ name = 'Linux/macOS PDF submission'; status = 'implemented'; authority = 'CUPS lp accepts -d, -n, -P, orientation-requested, and a generated PDF path.'; implementation = 'CupsPrintCommandPlanner plus CupsPrintService; arguments use ProcessStartInfo.ArgumentList, never a shell command string.'; tests = 'CupsPrintServiceTests assert exact lp arguments and no-printer short circuit.' },
    [ordered]@{ name = 'Later Avalonia print dialog contract'; status = 'implemented'; authority = 'WPF dialog exposes printer, copies, page range, and page orientation decisions.'; implementation = 'CupsPrintDialog applies PrintSelectionPlanner output and submits the generated PDF through CupsPrintService.'; tests = 'PrintSelectionPlannerTests and CupsPrintServiceTests cover selection, no printers, validation, and submission.' },
    [ordered]@{ name = 'Cross-platform XPS'; status = 'implemented-raster-fallback'; authority = 'XPS FixedPage supports vector Path content and Glyphs only with packaged font resources.'; implementation = 'PortableXpsWriter writes vector XPS when possible; otherwise Skia renders each laid-out page to PNG and PortableXpsWriter embeds those images in a real OPC XPS package. PDF bytes are never relabeled.'; tests = 'PortableXpsWriterTests validate package parts and unsupported vector text; Avalonia export exercises the raster fallback.' }
)

$uiGaps = @()

$evidence = [ordered]@{
    schema = 'freex.freew.shell-platform-parity.v1'
    authority = 'FreeW WPF native PrintDialog/XpsDocumentWriter behavior, supplemented by the existing Avalonia shared PDF draw-op contract'
    generatedInputs = $sourceFiles
    sourceSha256 = $hashes
    ownershipBoundary = @(
        'MainWindow, ribbon registry/definition, Backstage callback, and shared print/XPS adapter routes are included in the integration.'
    )
    surfaces = $surfaces
    uiWiringGaps = $uiGaps
    xpsLimitation = 'Normal documents use a standards-compliant raster-page fallback when XPS glyph/font resources are unavailable. The fallback is fixed-layout XPS with embedded PNG page images; the vector writer still preserves embedded-font/glyph output when an XpsFontResource is supplied.'
    freshnessCheck = 'Run tools/Generate-FreeWShellPlatformParityEvidence.ps1 -Check; nonzero means generated JSON/Markdown no longer match current source hashes.'
}

$jsonText = $evidence | ConvertTo-Json -Depth 20
$surfaceLines = ($surfaces | ForEach-Object { "| $($_.name) | $($_.status) | $($_.implementation) | $($_.tests) |" }) -join "`n"
$gapLines = if ($uiGaps.Count -eq 0) { '| None | No owned shell/UI wiring gaps remain. |' } else { ($uiGaps | ForEach-Object { "| ``$($_.file)`` | $($_.gap) |" }) -join "`n" }
$markdownText = "# FreeW Shell Platform Parity`n`n" +
    "Generated from WPF authority, Avalonia adapters, shared contracts, and focused-test source hashes. Run ``tools/Generate-FreeWShellPlatformParityEvidence.ps1 -Check`` to verify freshness.`n`n" +
    "- Schema: ``$($evidence.schema)```n" +
    "- Authority: $($evidence.authority)`n" +
    "- Owned shell/UI wiring is complete; only external printer availability remains host-dependent.`n`n" +
    "## Capability Matrix`n`n| Surface | Status | Implementation | Focused evidence |`n|---|---|---|---|`n$surfaceLines`n`n" +
    "## Exact UI Wiring Gaps`n`n| File | Gap for integration pass |`n|---|---|`n$gapLines`n`n" +
    "## XPS Boundary`n`n$($evidence.xpsLimitation)`n`n" +
    "The shared writer emits a real OPC package with ``FixedDocSeq.fdseq``, ``FixedDocument.fdoc``, and ``.fpage`` parts for representable vector content. It does not write PDF bytes with an XPS extension.`n`n" +
    "## Freshness`n`nThe JSON records SHA-256 hashes for every authority, implementation, shared contract, and focused-test input. ``-Check`` regenerates both artifacts in memory and fails if either committed artifact differs.`n"

if ($Check) {
    if (-not (Test-Path -LiteralPath $jsonPath) -or -not (Test-Path -LiteralPath $markdownPath)) { throw 'Generated evidence files are missing.' }
    if ([IO.File]::ReadAllText($jsonPath) -ne $jsonText -or [IO.File]::ReadAllText($markdownPath) -ne $markdownText) { throw 'Generated evidence is stale. Run the generator without -Check.' }
    Write-Output "Fresh: $jsonPath"
    Write-Output "Fresh: $markdownPath"
    exit 0
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($jsonPath, $jsonText, $utf8)
[IO.File]::WriteAllText($markdownPath, $markdownText, $utf8)
Write-Output "Wrote $jsonPath"
Write-Output "Wrote $markdownPath"
