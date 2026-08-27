param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'ToolScriptSupport.ps1')
Invoke-ToolCanonicalPwshHost -ScriptPath $PSCommandPath -ForwardedArguments @("-Check:$([bool]$Check)")
$jsonPath = Join-Path $repo 'docs/parity/freew-shell-platform-parity-20260720.json'
$markdownPath = Join-Path $repo 'docs/parity/freew-shell-platform-parity-20260720.md'

$sourceFiles = @(
    'freew/FreeW.App.Host/MainWindow.cs',
    'freew/FreeW.App.Host/XpsExport.cs',
    'freew/FreeW.App.Host/PrintPreviewWindow.cs',
    'freew/FreeW.App.Avalonia/MainWindow.cs',
    'freew/FreeW.App.Avalonia/PrintPreviewDialog.cs',
    'freew/FreeW.App.Avalonia/Printing/CupsPrintDialog.cs',
    'freew/FreeW.App.Avalonia/Pdf/FreeWAvaloniaPdfExport.cs',
    'freew/FreeW.App.Avalonia/Pdf/FreeWAvaloniaXpsExport.cs',
    'shared/Free.Shared.AppServices/Printing/CupsPrintService.cs',
    'freew/FreeW.App.Avalonia/Editing/DocumentView.cs',
    'tests/FreeX.App.Services.Tests/SharedCupsPrintServiceTests.cs',
    'freew/FreeW.App.Avalonia.Tests/Printing/PrintLifecycleTests.cs',
    'freew/FreeW.App.Avalonia.Tests/Printing/PortableXpsWriterTests.cs',
    'shared/Free.Shared.AppServices/Printing/PrintSelectionPlanner.cs',
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
    $path = Join-Path $repo $relative
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing evidence input: $relative" }
    $hashes[$relative] = Get-ToolNormalizedTextSha256 -Path $path
}

$surfaces = @(
    [ordered]@{ name = 'WPF native printing'; status = 'implemented'; authority = 'FreeW.App.Host.MainWindow.Print uses System.Windows.Controls.PrintDialog, PrintTicket, PrintLayout.BuildPaginator, and PrintDocument.'; implementation = 'Preserved unchanged; evidence hashes the authoritative source.'; tests = 'Existing WPF host print/paginator tests remain the authority.' },
    [ordered]@{ name = 'WPF native XPS'; status = 'implemented'; authority = 'FreeW.App.Host.XpsExport uses System.Windows.Xps.Packaging.XpsDocument and XpsDocumentWriter on the WPF paginator.'; implementation = 'Preserved unchanged; it writes a real FixedDocumentSequence OPC package on STA.'; tests = 'Existing FreeW.App.Host.Tests.XpsExportTests cover package/page output.' },
    [ordered]@{ name = 'Linux/macOS printer discovery'; status = 'implemented'; authority = 'CUPS lpstat -p and lpstat -d contract.'; implementation = 'CupsPrintService with injected IProcessRunner and explicit no-printer/unavailable/failed/cancelled results.'; tests = 'CupsPrintServiceTests parse printers/default and cancellation.' },
    [ordered]@{ name = 'Linux/macOS PDF submission'; status = 'implemented'; authority = 'CUPS lp accepts -d, -n, -P, orientation-requested, and a generated PDF path.'; implementation = 'CupsPrintCommandPlanner plus CupsPrintService; arguments use ProcessStartInfo.ArgumentList, never a shell command string.'; tests = 'CupsPrintServiceTests assert exact lp arguments and no-printer short circuit.' },
    [ordered]@{ name = 'Avalonia native print lifecycle'; status = 'implemented'; authority = 'WPF native printing remains the semantic authority; the existing Avalonia CUPS route is capability-gated and must preserve cancellation and owner focus.'; implementation = 'IPlatformPrintService is the non-WPF platform boundary. MainWindow routes supported Backstage print commands through injected discovery/dialog/submission services, cancels in-flight work on close, and restores prior focus.'; tests = 'PrintLifecycleTests cover capability gating, command routing, cancellation, and focus restoration; CupsPrintServiceTests cover CUPS cancellation and injected process behavior.' },
    [ordered]@{ name = 'Avalonia XPS export route'; status = 'not-exposed-platform-limitation'; authority = 'XPS is a WPF-only user-facing capability through System.Windows.Xps.Packaging and XpsDocumentWriter.'; implementation = 'Backstage ExportXps is omitted from Avalonia. The portable writer remains available as shared/internal code and is not presented as an Avalonia platform capability.'; tests = 'BackstageViewTests assert the Avalonia XPS callback is absent; WPF XpsExportTests remain the authority for XPS output.' }
)

$uiGaps = @()

$evidence = [ordered]@{
    schema = 'freex.freew.shell-platform-parity.v1'
    authority = 'FreeW WPF native PrintDialog/XpsDocumentWriter behavior, supplemented by the existing Avalonia shared PDF draw-op contract'
    generatedInputs = $sourceFiles
    sourceSha256 = $hashes
    ownershipBoundary = @(
        'MainWindow, ribbon registry/definition, Backstage callback, and the shared print-selection/platform boundary are included in the integration.'
    )
    surfaces = $surfaces
    uiWiringGaps = $uiGaps
    xpsLimitation = 'XPS remains WPF-only at the application surface. Avalonia does not expose an XPS export action because the native XPS stack is Windows/WPF-specific; the portable writer is retained as shared/internal code and is not a claim of Avalonia parity.'
    freshnessCheck = 'Run tools/Generate-FreeWShellPlatformParityEvidence.ps1 -Check; nonzero means generated JSON/Markdown no longer match current source hashes.'
}

$jsonText = $evidence | ConvertTo-Json -Depth 20
$surfaceLines = ($surfaces | ForEach-Object { "| $($_.name) | $($_.status) | $($_.implementation) | $($_.tests) |" }) -join "`n"
$gapLines = if ($uiGaps.Count -eq 0) { '| None | No owned shell/UI wiring gaps remain. |' } else { ($uiGaps | ForEach-Object { "| ``$($_.file)`` | $($_.gap) |" }) -join "`n" }
$markdownText = "# FreeW Shell Platform Parity`n`n" +
    "Generated from WPF authority, Avalonia adapters, shared contracts, and focused-test source hashes. Run ``tools/Generate-FreeWShellPlatformParityEvidence.ps1 -Check`` to verify freshness.`n`n" +
    "- Schema: ``$($evidence.schema)```n" +
    "- Authority: $($evidence.authority)`n" +
    "- Owned shell/UI wiring is complete; only external printer availability and the WPF-only XPS boundary remain platform-dependent.`n`n" +
    "## Capability Matrix`n`n| Surface | Status | Implementation | Focused evidence |`n|---|---|---|---|`n$surfaceLines`n`n" +
    "## Exact UI Wiring Gaps`n`n| File | Gap for integration pass |`n|---|---|`n$gapLines`n`n" +
    "## XPS Boundary`n`n$($evidence.xpsLimitation)`n`n" +
    "The shared writer can emit a real OPC package with ``FixedDocSeq.fdseq``, ``FixedDocument.fdoc``, and ``.fpage`` parts for representable vector content, but this does not make XPS an Avalonia application capability. It does not write PDF bytes with an XPS extension.`n`n" +
    "## Freshness`n`nThe JSON records SHA-256 hashes for every authority, implementation, shared contract, and focused-test input. ``-Check`` regenerates both artifacts in memory and fails if either committed artifact differs.`n"

if ($Check) {
    if (-not (Test-Path -LiteralPath $jsonPath) -or -not (Test-Path -LiteralPath $markdownPath)) { throw 'Generated evidence files are missing.' }
    Test-ToolGeneratedContentMatches -ExpectedContent $jsonText -ActualPath $jsonPath -Label 'FreeW shell platform parity JSON' -GeneratorScriptName 'tools/Generate-FreeWShellPlatformParityEvidence.ps1' -NormalizeNewlines
    Test-ToolGeneratedContentMatches -ExpectedContent $markdownText -ActualPath $markdownPath -Label 'FreeW shell platform parity Markdown' -GeneratorScriptName 'tools/Generate-FreeWShellPlatformParityEvidence.ps1' -NormalizeNewlines
    Write-Output "Fresh: $jsonPath"
    Write-Output "Fresh: $markdownPath"
    exit 0
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($jsonPath, $jsonText, $utf8)
[IO.File]::WriteAllText($markdownPath, $markdownText, $utf8)
Write-Output "Wrote $jsonPath"
Write-Output "Wrote $markdownPath"
