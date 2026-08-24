param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'VisualEvidenceScriptSupport.ps1')
$root = Join-Path $repo 'docs\parity\freew-shell-visual-2026-08-16'
$jsonPath = Join-Path $root 'freew_shell_visual_evidence.json'
$markdownPath = Join-Path $root 'README.md'
$widths = @(1500, 1100, 900, 750)

function Read-Json([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Required capture manifest is missing: $path" }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

function Relative([string]$path) {
    $fullPath = [IO.Path]::GetFullPath($path)
    if (-not $fullPath.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Evidence path escapes the repository: $fullPath"
    }
    return $fullPath.Substring($repo.Length).TrimStart('\').Replace('\', '/')
}

$avaloniaManifestPath = Join-Path $root 'avalonia\freew_avalonia_shell_capture_manifest.json'
$avalonia = Read-Json $avaloniaManifestPath
if ([string]$avalonia.schema -ne 'freex.freew.shell-visual-capture.v1') { throw 'Unexpected Avalonia shell capture manifest schema.' }

$wordChromeRoot = Join-Path $repo 'docs\parity\freew-word-chrome-2026-08-16'
$wordChromeManifestPath = Join-Path $wordChromeRoot 'manifest.json'
$wordChrome = Read-Json $wordChromeManifestPath
if ($wordChrome.schemaVersion -ne 1 -or [string]$wordChrome.captureStatus -ne 'complete' -or
    [int]$wordChrome.expectedCaptureCount -ne 36 -or [int]$wordChrome.actualCaptureCount -ne 36 -or
    @($wordChrome.captures).Count -ne 36) {
    throw 'FreeW native Word chrome manifest is incomplete or has an unexpected capture contract.'
}
foreach ($capture in $wordChrome.captures) {
    $file = Join-Path $wordChromeRoot ([string]$capture.fileName)
    if ([string]$capture.captureStatus -ne 'complete' -or -not (Test-Path -LiteralPath $file) -or
        (Get-Item -LiteralPath $file).Length -le 0 -or (Get-VisualEvidenceFileSha256 -Path $file) -ne [string]$capture.sha256) {
        throw "Invalid native Word chrome capture: $file"
    }
}

$wpfByWidth = @{}
foreach ($width in $widths) {
    $manifestPath = Join-Path $root "wpf\$width\freew_ribbonshot_manifest.json"
    $manifest = Read-Json $manifestPath
    if ([int]$manifest.RenderWidth -ne $width -or [int]$manifest.RenderHeight -ne 720) {
        throw "WPF shell manifest has an unexpected geometry: $manifestPath"
    }
    $wpfByWidth[$width] = $manifest
}

$sourceFiles = @(
    'freew/tools/FreeW.RibbonShot/Program.cs',
    'freew/tools/FreeW.ShellVisualHarness.Avalonia/Program.cs',
    'freew/FreeW.App.Host/MainWindow.cs',
    'freew/FreeW.App.Avalonia/MainWindow.cs',
    # The hosts inject these gallery surfaces after the declarative ribbon is rendered. Include them
    # explicitly so evidence freshness cannot report a current shell after a visible gallery changed.
    'freew/FreeW.App.Host/Ribbon/StylesGallery.cs',
    'freew/FreeW.App.Host/Ribbon/ThemeGallery.cs',
    'freew/FreeW.App.Host/Ribbon/TableStylesGallery.cs',
    'freew/FreeW.App.Host/Ribbon/ChartDesignGallery.cs',
    'freew/FreeW.App.Host/Ribbon/SmartArtGallery.cs',
    'freew/FreeW.App.Avalonia/DocumentStylesGallery.cs',
    'freew/FreeW.App.Avalonia/DocumentThemeGallery.cs',
    'freew/FreeW.App.Avalonia/TableStylesGallery.cs',
    'freew/FreeW.App.Avalonia/ChartStylesGallery.cs',
    'freew/FreeW.App.Avalonia/SmartArtStylesGallery.cs'
)
$sourceSha256 = [ordered]@{}
foreach ($relativePath in $sourceFiles) {
    $sourceSha256[$relativePath] = Get-VisualEvidenceFileSha256 -Path (Join-Path $repo ($relativePath -replace '/', '\\'))
}

$standardTabs = @('home', 'insert', 'design', 'layout', 'references', 'mailings', 'review', 'view', 'help', 'developer')
$contextualTabs = [ordered]@{
    '11' = 'drawing-format'
    '12' = 'picture-format'
    '13' = 'chart-design'
    '14' = 'chart-format'
    '15' = 'smartart-design'
    '16' = 'table-design'
    '17' = 'table-layout'
    '18' = 'header-footer-design'
}
$rows = @()
$contextRows = @()
foreach ($width in $widths) {
    $wpf = @($wpfByWidth[$width].Captures)
    $ava = @($avalonia.captures | Where-Object { [int]$_.width -eq $width })
    $pairedWpfIndices = @()
    foreach ($tabId in $standardTabs) {
        $wpfCapture = @($wpf | Where-Object { ([string]$_.TabName).ToLowerInvariant() -eq $tabId } | Select-Object -First 1)
        $avaCapture = @($ava | Where-Object { ([string]$_.tabId).ToLowerInvariant() -eq $tabId } | Select-Object -First 1)
        if ($wpfCapture.Count -ne 1 -or $avaCapture.Count -ne 1) { throw "Missing paired shell capture for width=$width tab=$tabId." }
        $wpfFile = Join-Path $root "wpf\$width\$($wpfCapture[0].Path)"
        $avaFile = Join-Path $root "avalonia\$($avaCapture[0].fileName)"
        foreach ($file in @($wpfFile, $avaFile)) {
            if (-not (Test-Path -LiteralPath $file) -or (Get-Item -LiteralPath $file).Length -le 0) { throw "Invalid shell PNG: $file" }
        }
        $rows += [ordered]@{
            width = $width
            height = 720
            tab = $tabId
            classification = 'paired-capture-review-required'
            wpfPath = Relative $wpfFile
            avaloniaPath = Relative $avaFile
            wpfSha256 = Get-VisualEvidenceFileSha256 -Path $wpfFile
            avaloniaSha256 = Get-VisualEvidenceFileSha256 -Path $avaFile
            note = 'Whole-window capture is present for both hosts. No pixel pass/fail is inferred because the WPF and Avalonia shell chrome intentionally use different native/window-frame and toolbar structures.'
        }
        $pairedWpfIndices += [int]$wpfCapture[0].TabIndex
    }
    foreach ($tabIndex in $contextualTabs.Keys) {
        $wpfCapture = @($wpf | Where-Object { [int]$_.TabIndex -eq [int]$tabIndex } | Select-Object -First 1)
        $avaloniaTabId = [string]$contextualTabs[[string]$tabIndex]
        $avaCapture = @($ava | Where-Object { ([string]$_.tabId).ToLowerInvariant() -eq $avaloniaTabId } | Select-Object -First 1)
        if ($wpfCapture.Count -ne 1 -or $avaCapture.Count -ne 1) {
            throw "Missing paired contextual shell capture for width=$width WPF-tab-index=$tabIndex Avalonia-tab=$avaloniaTabId."
        }
        if ([string]$avaCapture[0].fixture -eq 'static') {
            throw "Avalonia contextual shell capture was not produced by a context fixture: width=$width tab=$avaloniaTabId."
        }
        $wpfFile = Join-Path $root "wpf\$width\$($wpfCapture[0].Path)"
        $avaFile = Join-Path $root "avalonia\$($avaCapture[0].fileName)"
        foreach ($file in @($wpfFile, $avaFile)) {
            if (-not (Test-Path -LiteralPath $file) -or (Get-Item -LiteralPath $file).Length -le 0) { throw "Invalid contextual shell PNG: $file" }
        }
        $contextRows += [ordered]@{
            width = $width
            height = 720
            wpfTabIndex = [int]$tabIndex
            wpfTab = [string]$wpfCapture[0].TabName
            avaloniaTabId = $avaloniaTabId
            avaloniaFixture = [string]$avaCapture[0].fixture
            classification = 'paired-contextual-capture-review-required'
            wpfPath = Relative $wpfFile
            avaloniaPath = Relative $avaFile
            wpfSha256 = Get-VisualEvidenceFileSha256 -Path $wpfFile
            avaloniaSha256 = Get-VisualEvidenceFileSha256 -Path $avaFile
            note = 'Both hosts render the contextual ribbon state. WPF uses its established visible-tab driver; Avalonia activates the named state through a real editor fixture before the shell frame is captured. No pixel pass/fail is inferred because host chrome remains structurally different.'
        }
    }
}

$evidence = [ordered]@{
    schema = 'freex.freew.shell-visual-evidence.v1'
    scope = [ordered]@{
        app = 'FreeW'
        comparison = 'WPF application shell versus Avalonia application shell only'
        excluded = @('Microsoft Word ribbon/chrome comparison', 'document-canvas Word baseline comparison', 'native Backstage window and OS-owned dialogs')
    }
    renderers = [ordered]@{
        wpf = 'FreeW.RibbonShot / real MainWindow / WPF RenderTargetBitmap'
        avalonia = [string]$avalonia.renderer
    }
    sourceSha256 = $sourceSha256
    pairedStaticChrome = $rows
    pairedContextualChrome = $contextRows
    nativeWordChrome = [ordered]@{
        captureStatus = [string]$wordChrome.captureStatus
        expectedCaptureCount = [int]$wordChrome.expectedCaptureCount
        actualCaptureCount = [int]$wordChrome.actualCaptureCount
        normalizedDpi = [int]$wordChrome.normalizedDpi
        manifestPath = Relative $wordChromeManifestPath
        comparisonBoundary = [string]$wordChrome.comparisonBoundary
    }
    counts = [ordered]@{
        pairedStaticChrome = $rows.Count
        pairedContextualChrome = $contextRows.Count
        avaloniaContextualMissing = 0
        wordOfficeChromeReferences = [int]$wordChrome.actualCaptureCount
    }
    rerun = @(
        'dotnet run --project freew/tools/FreeW.ShellVisualHarness.Avalonia/FreeW.ShellVisualHarness.Avalonia.csproj -c Release -- --output docs/parity/freew-shell-visual-2026-08-16/avalonia --height 720 --include-contextual',
        'Run FreeW.RibbonShot once per width to docs/parity/freew-shell-visual-2026-08-16/wpf/<width>: all <width> 720.',
        'powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Capture-FreeWWordChrome.ps1',
        'powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Generate-FreeWShellVisualEvidence.ps1'
    )
}

$jsonText = $evidence | ConvertTo-Json -Depth 12
$markdownText = @"
# FreeW Whole-Window and Chrome Evidence

This bundle makes the FreeW desktop-shell evidence uniform across the two application hosts. It is **not** a Microsoft Word visual-parity report: Word document-page baselines remain separately tracked under `docs/parity/freew-word-baseline-2026-08-16/`, and no native Word ribbon/chrome capture is claimed here.

## Coverage

| Evidence family | WPF | Avalonia | Result |
|---|---:|---:|---|
| Static whole-window ribbon/chrome (10 tabs x 4 widths) | 40 | 40 | 40 paired captures, visual review required |
| Contextual whole-window ribbon/chrome (8 tabs x 4 widths) | $($contextRows.Count) | $($contextRows.Count) | $($contextRows.Count) paired captures, visual review required |
| Backstage / app dialogs | Existing dialog harness | Existing dialog harness | Outside this shell-only matrix |
| Microsoft Word chrome | 36 native Word references | n/a | Complete standard-profile reference lane; semantic review required |

Widths are 1500, 1100, 900, and 750 DIPs; every capture is 720 DIPs high. WPF uses the real `FreeW.App.Host.MainWindow` via `FreeW.RibbonShot`; Avalonia uses the real `FreeW.App.Avalonia.MainWindow` through an actual Skia headless compositor frame.

## Classification

The $($rows.Count) static rows are intentionally `paired-capture-review-required`, not pixel passes. The two hosts have deliberate structural chrome differences (native frame, title/QAT arrangement, and compact toolbar layout), so a raw whole-window pixel threshold would report implementation-independent differences as product failures. Each artifact is hash-listed in `freew_shell_visual_evidence.json` and must exist and be non-empty for generation/check to pass.

The $($contextRows.Count) contextual rows are state-driven on both hosts. The WPF harness keeps its established forced-visible contextual-tab contract. Avalonia uses actual editor fixtures: a selected shape, selected floating picture, selected floating chart, selected floating SmartArt, a table-cell caret, and a header/footer caret. Each fixture is isolated in a new real `MainWindow` so multiple contexts cannot leak into a synthetic tab strip.

The native Word lane contains $($wordChrome.actualCaptureCount)/$($wordChrome.expectedCaptureCount) complete standard-profile top-band references at $($wordChrome.normalizedDpi) DPI. They are authoritative Word artifacts for semantic chrome review, but are not converted into host pixel pass/fail results because Word and FreeW intentionally have different frame and ribbon implementations. The configurable FreeW Developer tab and Word contextual tabs are outside this default-profile reference lane.

## Reproduce and Check

````powershell
dotnet run --project freew/tools/FreeW.ShellVisualHarness.Avalonia/FreeW.ShellVisualHarness.Avalonia.csproj -c Release -- --output docs/parity/freew-shell-visual-2026-08-16/avalonia --height 720 --include-contextual
# For each width 1500, 1100, 900, 750:
dotnet run --project freew/tools/FreeW.RibbonShot/FreeW.RibbonShot.csproj -c Release -- docs/parity/freew-shell-visual-2026-08-16/wpf/<width> all <width> 720
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Generate-FreeWShellVisualEvidence.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/Test-FreeWShellVisualEvidence.ps1
````

The source hashes, row inventory, PNG hashes, and sizes are generated into `freew_shell_visual_evidence.json`. `-Check` is byte-for-byte against both generated files.
"@

if ($Check) {
    if (-not (Test-Path -LiteralPath $jsonPath) -or -not (Test-Path -LiteralPath $markdownPath)) { throw 'Generated FreeW shell evidence files are missing.' }
    if ([IO.File]::ReadAllText($jsonPath) -ne $jsonText -or [IO.File]::ReadAllText($markdownPath) -ne $markdownText) { throw 'Generated FreeW shell evidence is stale. Run the generator without -Check.' }
    Write-Output "Fresh: $jsonPath"
    Write-Output "Fresh: $markdownPath"
    exit 0
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($jsonPath, $jsonText, $utf8)
[IO.File]::WriteAllText($markdownPath, $markdownText, $utf8)
Write-Output "Wrote $jsonPath"
Write-Output "Wrote $markdownPath"
