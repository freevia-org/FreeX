param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
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

function Sha([string]$path) {
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$avaloniaManifestPath = Join-Path $root 'avalonia\freew_avalonia_shell_capture_manifest.json'
$avalonia = Read-Json $avaloniaManifestPath
if ([string]$avalonia.schema -ne 'freex.freew.shell-visual-capture.v1') { throw 'Unexpected Avalonia shell capture manifest schema.' }

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
    'freew/FreeW.App.Avalonia/MainWindow.cs'
)
$sourceSha256 = [ordered]@{}
foreach ($relativePath in $sourceFiles) {
    $sourceSha256[$relativePath] = Sha (Join-Path $repo ($relativePath -replace '/', '\\'))
}

$standardTabs = @('home', 'insert', 'design', 'layout', 'references', 'mailings', 'review', 'view', 'help', 'developer')
$rows = @()
$wpfContextOnly = @()
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
            wpfSha256 = Sha $wpfFile
            avaloniaSha256 = Sha $avaFile
            note = 'Whole-window capture is present for both hosts. No pixel pass/fail is inferred because the WPF and Avalonia shell chrome intentionally use different native/window-frame and toolbar structures.'
        }
        $pairedWpfIndices += [int]$wpfCapture[0].TabIndex
    }
    foreach ($capture in $wpf) {
        if ($pairedWpfIndices -notcontains [int]$capture.TabIndex) {
            $file = Join-Path $root "wpf\$width\$($capture.Path)"
            if (-not (Test-Path -LiteralPath $file) -or (Get-Item -LiteralPath $file).Length -le 0) { throw "Invalid WPF contextual PNG: $file" }
            $wpfContextOnly += [ordered]@{
                width = $width
                height = 720
                tab = [string]$capture.TabName
                classification = 'wpf-contextual-capture-without-avalonia-context-fixture'
                wpfPath = Relative $file
                wpfSha256 = Sha $file
                note = 'WPF forced this contextual tab visible. The Avalonia shell capture uses the normal no-selection document state, so no matching contextual-state image is claimed.'
            }
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
    wpfContextualOnly = $wpfContextOnly
    counts = [ordered]@{
        pairedStaticChrome = $rows.Count
        wpfContextualOnly = $wpfContextOnly.Count
        avaloniaContextualMissing = $wpfContextOnly.Count
        wordOfficeChromeReferences = 0
    }
    rerun = @(
        'dotnet run --project freew/tools/FreeW.ShellVisualHarness.Avalonia/FreeW.ShellVisualHarness.Avalonia.csproj -c Release -- --output docs/parity/freew-shell-visual-2026-08-16/avalonia',
        'Run FreeW.RibbonShot once per width to docs/parity/freew-shell-visual-2026-08-16/wpf/<width>: all <width> 720.',
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
| WPF contextual ribbon tabs | $($wpfContextOnly.Count) | 0 matched contextual-state captures | Explicit coverage gap; not represented as a parity pass or mismatch |
| Backstage / app dialogs | Existing dialog harness | Existing dialog harness | Outside this shell-only matrix |
| Microsoft Word chrome | 0 | n/a | No reference artifacts captured |

Widths are 1500, 1100, 900, and 750 DIPs; every capture is 720 DIPs high. WPF uses the real `FreeW.App.Host.MainWindow` via `FreeW.RibbonShot`; Avalonia uses the real `FreeW.App.Avalonia.MainWindow` through an actual Skia headless compositor frame.

## Classification

The $($rows.Count) static rows are intentionally `paired-capture-review-required`, not pixel passes. The two hosts have deliberate structural chrome differences (native frame, title/QAT arrangement, and compact toolbar layout), so a raw whole-window pixel threshold would report implementation-independent differences as product failures. Each artifact is hash-listed in `freew_shell_visual_evidence.json` and must exist and be non-empty for generation/check to pass.

The $($wpfContextOnly.Count) WPF contextual-tab images are retained as real WPF shell evidence. Avalonia's normal seeded document has no table/picture/chart/SmartArt selection, so this capture does not invent an Avalonia counterpart. Adding matching state-fixture activation is the next remaining FreeW shell-capture task.

## Reproduce and Check

````powershell
dotnet run --project freew/tools/FreeW.ShellVisualHarness.Avalonia/FreeW.ShellVisualHarness.Avalonia.csproj -c Release -- --output docs/parity/freew-shell-visual-2026-08-16/avalonia
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
