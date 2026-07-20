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
    [ordered]@{ name = 'Later Avalonia print dialog contract'; status = 'implemented-contract'; authority = 'WPF dialog exposes printer, copies, page range, and page orientation decisions.'; implementation = 'Shared PrintSelection, PrintPageRange, PrinterInfo, PrintDialogPlan, and PrintSelectionPlanner.'; tests = 'PrintSelectionPlannerTests cover requested/default printer, no printers, and validation.' },
    [ordered]@{ name = 'Cross-platform XPS'; status = 'truthful-fixed-layout-only'; authority = 'XPS FixedPage supports vector Path content and Glyphs only with packaged font resources.'; implementation = 'PortableXpsWriter writes a real OPC XPS package for supported vector operations and analyzes unsupported text/image dependencies; FreeWAvaloniaXpsExport never relabels PDF bytes.'; tests = 'PortableXpsWriterTests validate package parts and prove text fails without an embedded font resource.' }
)

$uiGaps = @(
    [ordered]@{ file = 'freew/FreeW.App.Avalonia/MainWindow.cs'; gap = 'Current AvaloniaDirectPrintCapability is Deferred and ExportXps is null in the Backstage callback set; integration must inject CupsPrintService and later dialog selection.' },
    [ordered]@{ file = 'freew/FreeW.App.Avalonia/PrintPreviewDialog.cs'; gap = 'The existing preview button chooses Print versus Create PDF from BackstageDirectPrintCapability; the integration pass must provide a real direct-print callback and cancellation/error messaging.' },
    [ordered]@{ file = 'freew/FreeW.App.Avalonia/Backstage/BackstageView.cs'; gap = 'The view already accepts DirectPrintCapability and ExportXps callbacks; integration must supply the new adapter contracts without changing this owned shell UI file.' },
    [ordered]@{ file = 'freew/FreeW.App.Presentation/Backstage/BackstagePaneSurfacePlanner.cs'; gap = 'Export XPS rows appear only when an ExportXps callback is supplied; the integration pass decides whether the truthful Avalonia capability is exposed after a font-resource provider exists.' },
    [ordered]@{ file = 'freew/FreeW.App.Host/MainWindow.cs'; gap = 'No gap to wire in this slice: WPF PrintDialog and native XPS behavior remain intact and outside the edit boundary.' }
)

$evidence = [ordered]@{
    schema = 'freex.freew.shell-platform-parity.v1'
    authority = 'FreeW WPF native PrintDialog/XpsDocumentWriter behavior, supplemented by the existing Avalonia shared PDF draw-op contract'
    generatedInputs = $sourceFiles
    sourceSha256 = $hashes
    ownershipBoundary = @(
        'No FreeW MainWindow, ribbon registry/definition, Backstage view/model, page-layout/media/mail/design dialog, or shared shell UI file was edited.',
        'UI wiring gaps are recorded for the integration pass.'
    )
    surfaces = $surfaces
    uiWiringGaps = $uiGaps
    xpsLimitation = 'The current Avalonia PdfContentDocument/PdfDrawOp model exposes text strings and positioned draw ops, but no embedded XPS font bytes, font URI, glyph indices, subsetting metadata, or equivalent glyph-outline renderer. A normal text document therefore cannot be truthfully emitted as selectable XPS until that exact dependency is supplied.'
    freshnessCheck = 'Run tools/Generate-FreeWShellPlatformParityEvidence.ps1 -Check; nonzero means generated JSON/Markdown no longer match current source hashes.'
}

$jsonText = $evidence | ConvertTo-Json -Depth 20
$surfaceLines = ($surfaces | ForEach-Object { "| $($_.name) | $($_.status) | $($_.implementation) | $($_.tests) |" }) -join "`n"
$gapLines = ($uiGaps | ForEach-Object { "| ``$($_.file)`` | $($_.gap) |" }) -join "`n"
$markdownText = "# FreeW Shell Platform Parity`n`n" +
    "Generated from WPF authority, Avalonia adapters, shared contracts, and focused-test source hashes. Run ``tools/Generate-FreeWShellPlatformParityEvidence.ps1 -Check`` to verify freshness.`n`n" +
    "- Schema: ``$($evidence.schema)```n" +
    "- Authority: $($evidence.authority)`n" +
    "- No owned UI files were edited.`n`n" +
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
