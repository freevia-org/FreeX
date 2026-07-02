param(
    [string]$OutputRoot = "tools/ux-parity-runs",

    [string]$RunId,

    [string]$FreeXExe,

    [ValidateSet("smoke", "core", "dialogs", "status", "formula", "filtering", "grid", "all")]
    [string]$Suite = "smoke",

    [switch]$SkipBuild,

    [switch]$MinimizeForeignForeground,

    [ValidateRange(1, 5)]
    [int]$MaxAttempts = 2
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Get-GitValue {
    param(
        [string]$RepoRoot,
        [string[]]$Arguments
    )

    try {
        $value = & git -C $RepoRoot @Arguments 2>$null
        if ($LASTEXITCODE -eq 0) {
            return ($value -join "`n").Trim()
        }
    }
    catch {
    }

    return $null
}

function Resolve-FreeXExe {
    param(
        [string]$RepoRoot,
        [string]$RequestedPath,
        [switch]$SkipBuild
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $resolved = (Resolve-Path $RequestedPath).Path
        if (-not (Test-Path $resolved)) {
            throw "FreeX executable was not found at $RequestedPath"
        }
        return $resolved
    }

    $candidate = Join-Path $RepoRoot "src/FreeX.App.Host/bin/Release/net10.0-windows10.0.19041.0/FreeX.App.Host.exe"
    if (-not (Test-Path $candidate) -and -not $SkipBuild) {
        $buildOutput = & dotnet build (Join-Path $RepoRoot "src/FreeX.App.Host/FreeX.App.Host.csproj") --configuration Release
        $buildOutput | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) {
            throw "FreeX host build failed with exit code $LASTEXITCODE"
        }
    }

    if (-not (Test-Path $candidate)) {
        throw "FreeX host executable was not found. Build Release or pass -FreeXExe. Expected: $candidate"
    }

    return (Resolve-Path $candidate).Path
}

function Resolve-ForegroundCaptureProject {
    param(
        [string]$RepoRoot,
        [switch]$SkipBuild
    )

    $project = Join-Path $RepoRoot "tools/FreeX.ForegroundCapture/FreeX.ForegroundCapture.csproj"

    if (-not (Test-Path $project)) {
        throw "FreeX.ForegroundCapture project was not found. Expected: $project"
    }

    if (-not $SkipBuild) {
        $buildOutput = & dotnet build $project --configuration Release
        $buildOutput | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) {
            throw "FreeX.ForegroundCapture build failed with exit code $LASTEXITCODE"
        }
    }

    return (Resolve-Path $project).Path
}

function Initialize-ForegroundWindowInterop {
    if ("UxParityForegroundWindow" -as [type]) {
        return
    }

    Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class UxParityForegroundWindow
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
}
"@
}

function Clear-ForeignForegroundWindow {
    param([string]$Scenario)

    if (-not $MinimizeForeignForeground) {
        return
    }

    Initialize-ForegroundWindowInterop
    $handle = [UxParityForegroundWindow]::GetForegroundWindow()
    if ($handle -eq [IntPtr]::Zero) {
        return
    }

    $titleBuilder = New-Object System.Text.StringBuilder 512
    [void][UxParityForegroundWindow]::GetWindowText($handle, $titleBuilder, $titleBuilder.Capacity)
    $title = $titleBuilder.ToString()
    [uint32]$foregroundProcessId = 0
    [void][UxParityForegroundWindow]::GetWindowThreadProcessId($handle, [ref]$foregroundProcessId)
    $process = Get-Process -Id $foregroundProcessId -ErrorAction SilentlyContinue
    $processName = if ($null -eq $process) { "" } else { $process.ProcessName }

    $knownForegroundBlocker =
        $processName -eq "ApplicationFrameHost" -and
        $title.IndexOf("Media Player", [StringComparison]::OrdinalIgnoreCase) -ge 0

    if (-not $knownForegroundBlocker) {
        return
    }

    Write-Host "Minimizing known foreground blocker '$title' (PID $foregroundProcessId, $processName) before '$Scenario'."
    [void][UxParityForegroundWindow]::ShowWindow($handle, 6)
    Start-Sleep -Milliseconds 250
}

function Get-ScenarioPairs {
    param([string]$Suite)

    $pairs = @(
        [ordered]@{
            id = "format-cells-dialog"
            area = "Dialogs"
            excelScenario = "excel-format-cells-dialog"
            freexScenario = "freex-format-cells-dialog"
        },
        [ordered]@{
            id = "format-cells-context-dialog"
            area = "Dialogs"
            excelScenario = "excel-format-cells-context-dialog"
            freexScenario = "freex-format-cells-context-dialog"
        },
        [ordered]@{
            id = "open-dialog"
            area = "Native file dialogs"
            excelScenario = "excel-open-dialog"
            freexScenario = "freex-open-dialog"
        },
        [ordered]@{
            id = "save-as-dialog"
            area = "Native file dialogs"
            excelScenario = "excel-save-as-dialog"
            freexScenario = "freex-save-as-dialog"
        },
        [ordered]@{
            id = "sheet-tab-context-menu"
            area = "Sheet tabs"
            excelScenario = "excel-sheet-tab-context-menu"
            freexScenario = "freex-sheet-tab-context-menu"
        },
        [ordered]@{
            id = "sheet-tab-overflow-activate-dialog"
            area = "Sheet tabs"
            excelScenario = "excel-sheet-tab-overflow-activate-dialog"
            freexScenario = "freex-sheet-tab-overflow-activate-dialog"
        },
        [ordered]@{
            id = "status-footer-reference"
            area = "Status bar"
            excelScenario = "excel-status-footer-reference"
            freexScenario = "freex-status-live-stats-accessibility"
        },
        [ordered]@{
            id = "formula-bar-name-box-reference"
            area = "Formula bar and name box"
            excelScenario = "excel-formula-bar-name-box-reference"
            freexScenario = "freex-formula-bar-name-box-reference"
        },
        [ordered]@{
            id = "autofilter-opened-state"
            area = "Sorting and filtering"
            excelScenario = "excel-autofilter"
            freexScenario = "freex-autofilter"
        },
        [ordered]@{
            id = "grid-row-column-resize"
            area = "Grid pointer mechanics"
            comparisonMode = "freex-only"
            freexScenario = "freex-grid-row-column-resize"
        },
        [ordered]@{
            id = "grid-wheel-scroll"
            area = "Grid pointer mechanics"
            comparisonMode = "freex-only"
            freexScenario = "freex-grid-wheel-scroll"
        }
    )

    switch ($Suite) {
        "smoke" { return $pairs | Where-Object { $_["id"] -in @("format-cells-dialog", "sheet-tab-context-menu") } }
        "core" { return $pairs | Where-Object { $_["id"] -in @("format-cells-dialog", "format-cells-context-dialog", "sheet-tab-context-menu", "sheet-tab-overflow-activate-dialog") } }
        "dialogs" { return $pairs | Where-Object { $_["area"] -in @("Dialogs", "Native file dialogs") } }
        "status" { return $pairs | Where-Object { $_["area"] -eq "Status bar" } }
        "formula" { return $pairs | Where-Object { $_["area"] -eq "Formula bar and name box" } }
        "filtering" { return $pairs | Where-Object { $_["area"] -eq "Sorting and filtering" } }
        "grid" { return $pairs | Where-Object { $_["area"] -eq "Grid pointer mechanics" } }
        default { return $pairs }
    }
}

function New-NotRequiredScenarioResult {
    param(
        [string]$Scenario,
        [string]$Reason
    )

    return [ordered]@{
        exitCode = 0
        output = $Reason
        manifest = [ordered]@{
            scenario = $Scenario
            captureStatus = "not-required"
            captureMode = "not-required"
            screenshotPath = $null
            continuationScreenshotPath = $null
            resultValidation = $Reason
            blockReason = $null
            manifestPath = $null
        }
        attempts = 0
        attemptHistory = @()
    }
}

function Read-ScenarioManifest {
    param(
        [string]$OutputDirectory,
        [string]$Scenario
    )

    $path = Join-Path $OutputDirectory (Join-Path $Scenario "$($Scenario)_manifest.json")
    if (-not (Test-Path $path)) {
        return [ordered]@{
            scenario = $Scenario
            captureStatus = "missing-manifest"
            manifestPath = $path
            blockReason = "Scenario did not write its manifest."
        }
    }

    $manifest = Get-Content -Raw $path | ConvertFrom-Json
    return [ordered]@{
        scenario = $Scenario
        captureStatus = $manifest.CaptureStatus
        captureMode = $manifest.CaptureMode
        screenshotPath = $manifest.ScreenshotPath
        continuationScreenshotPath = $manifest.ContinuationScreenshotPath
        resultValidation = $manifest.ResultValidation
        blockReason = $manifest.BlockReason
        manifestPath = $path
    }
}

function Invoke-ForegroundScenario {
    param(
        [string]$ForegroundCaptureProject,
        [string]$Scenario,
        [string]$OutputDirectory,
        [string]$FreeXExe,
        [switch]$NoBuild,
        [int]$MaxAttempts = 2
    )

    $attempts = New-Object System.Collections.Generic.List[object]
    $last = $null

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        if ($attempt -gt 1) {
            Start-Sleep -Seconds 2
        }

        Clear-ForeignForegroundWindow $Scenario

        $arguments = @(
            "run",
            "--project",
            $ForegroundCaptureProject,
            "--configuration",
            "Release"
        )

        if ($NoBuild) {
            $arguments += "--no-build"
        }

        $arguments += @(
            "--",
            "--scenario",
            $Scenario,
            "--output",
            $OutputDirectory
        )

        if ($Scenario.StartsWith("freex-", [StringComparison]::OrdinalIgnoreCase)) {
            $arguments += @("--freex-exe", $FreeXExe)
        }

        $rawOutput = & dotnet @arguments 2>&1
        $exitCode = $LASTEXITCODE
        $manifest = Read-ScenarioManifest $OutputDirectory $Scenario
        $last = [ordered]@{
            exitCode = $exitCode
            output = ($rawOutput -join "`n")
            manifest = $manifest
        }

        $attempts.Add([ordered]@{
            attempt = $attempt
            exitCode = $exitCode
            captureStatus = $manifest["captureStatus"]
            blockReason = $manifest["blockReason"]
            manifestPath = $manifest["manifestPath"]
        })

        if ($manifest["captureStatus"] -eq "complete") {
            break
        }

        if (-not ([string]$manifest["blockReason"]).StartsWith("foreground-guard-failed:", [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
    }

    $result = [ordered]@{}
    foreach ($key in $last.Keys) {
        $result[$key] = $last[$key]
    }

    $result["attempts"] = $attempts.Count
    $result["attemptHistory"] = $attempts.ToArray()
    return $result
}

function ConvertTo-HtmlText {
    param([object]$Value)

    if ($null -eq $Value) {
        return ""
    }

    return [string]$Value `
        -replace '&', '&amp;' `
        -replace '<', '&lt;' `
        -replace '>', '&gt;' `
        -replace '"', '&quot;' `
        -replace "'", '&#39;'
}

function ConvertTo-FileUri {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path $Path)) {
        return $null
    }

    return (New-Object System.Uri((Resolve-Path $Path).Path)).AbsoluteUri
}

function New-ImageCellHtml {
    param([string]$Path)

    $uri = ConvertTo-FileUri $Path
    if ($null -eq $uri) {
        return "<span class=""muted"">No screenshot</span>"
    }

    $label = ConvertTo-HtmlText $Path
    return "<a href=""$uri""><img src=""$uri"" alt=""$label""></a><div class=""path"">$label</div>"
}

function Write-ScenarioReport {
    param(
        [object]$Summary,
        [string]$Path
    )

    $rows = New-Object System.Collections.Generic.List[string]
    foreach ($record in $Summary["records"]) {
        $excel = $record["excel"]["manifest"]
        $freex = $record["freex"]["manifest"]
        $rows.Add(@"
<tr>
  <td>
    <strong>$(ConvertTo-HtmlText $record["id"])</strong>
    <div class="muted">$(ConvertTo-HtmlText $record["area"])</div>
    <div>Status: $(ConvertTo-HtmlText $record["status"])</div>
    <div>Review: $(ConvertTo-HtmlText $record["comparisonStatus"])</div>
  </td>
  <td>
    <div class="status">$(ConvertTo-HtmlText $excel["captureStatus"])</div>
    <div class="block">$(ConvertTo-HtmlText $excel["blockReason"])</div>
    $(New-ImageCellHtml $excel["screenshotPath"])
  </td>
  <td>
    <div class="status">$(ConvertTo-HtmlText $freex["captureStatus"])</div>
    <div class="block">$(ConvertTo-HtmlText $freex["blockReason"])</div>
    $(New-ImageCellHtml $freex["screenshotPath"])
  </td>
</tr>
"@)
    }

    $html = @"
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <title>FreeX / Excel UX Scenario Batch</title>
  <style>
    body { font-family: Segoe UI, Arial, sans-serif; margin: 24px; color: #1f2933; }
    h1 { font-size: 22px; margin-bottom: 4px; }
    .summary { margin: 0 0 18px 0; color: #52606d; }
    table { border-collapse: collapse; width: 100%; table-layout: fixed; }
    th, td { border: 1px solid #cbd2d9; padding: 10px; vertical-align: top; }
    th { background: #f5f7fa; text-align: left; }
    img { max-width: 100%; border: 1px solid #d9e2ec; background: white; }
    .muted, .path { color: #66788a; font-size: 12px; overflow-wrap: anywhere; }
    .status { font-weight: 600; margin-bottom: 4px; }
    .block { color: #9b1c1c; font-size: 12px; margin-bottom: 8px; overflow-wrap: anywhere; }
  </style>
</head>
<body>
  <h1>FreeX / Excel UX Scenario Batch</h1>
  <p class="summary">
    Suite: $(ConvertTo-HtmlText $Summary["suite"]) |
    Status: $(ConvertTo-HtmlText $Summary["status"]) |
    Complete: $(ConvertTo-HtmlText $Summary["pairedCaptureComplete"]) |
    FreeX-only complete: $(ConvertTo-HtmlText $Summary["freexCaptureComplete"]) |
    Partial: $(ConvertTo-HtmlText $Summary["partialCapture"]) |
    Blocked: $(ConvertTo-HtmlText $Summary["blocked"])
  </p>
  <table>
    <thead>
      <tr>
        <th>Pair</th>
        <th>Excel</th>
        <th>FreeX</th>
      </tr>
    </thead>
    <tbody>
      $($rows -join "`n")
    </tbody>
  </table>
</body>
</html>
"@

    $html | Set-Content -Path $Path -Encoding UTF8
}

function Write-ScenarioContactSheet {
    param(
        [object]$Summary,
        [string]$Path
    )

    Add-Type -AssemblyName System.Drawing

    $records = @($Summary["records"])
    $cellWidth = 640
    $cellHeight = 430
    $headerHeight = 72
    $rowLabelHeight = 30
    $width = $cellWidth * 2
    $height = $headerHeight + (($cellHeight + $rowLabelHeight) * [Math]::Max(1, $records.Count))

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::White)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    $titleFont = New-Object System.Drawing.Font "Segoe UI", 18, ([System.Drawing.FontStyle]::Bold)
    $headingFont = New-Object System.Drawing.Font "Segoe UI", 12, ([System.Drawing.FontStyle]::Bold)
    $brush = [System.Drawing.Brushes]::Black

    try {
        $title = "FreeX / Excel UX $($Summary["suite"]) Pair - $($Summary["runId"])"
        $graphics.DrawString($title, $titleFont, $brush, 16, 12)
        $graphics.DrawString("Excel", $headingFont, $brush, 16, 48)
        $graphics.DrawString("FreeX", $headingFont, $brush, $cellWidth + 16, 48)

        for ($i = 0; $i -lt $records.Count; $i++) {
            $record = $records[$i]
            $y = $headerHeight + ($i * ($cellHeight + $rowLabelHeight))
            $graphics.FillRectangle([System.Drawing.Brushes]::Gainsboro, 0, $y, $width, $rowLabelHeight)
            $graphics.DrawString([string]$record["id"], $headingFont, $brush, 16, $y + 5)

            foreach ($side in @("excel", "freex")) {
                $manifest = $record[$side]["manifest"]
                $imagePath = [string]$manifest["screenshotPath"]
                $columnX = if ($side -eq "excel") { 14 } else { $cellWidth + 14 }
                if ([string]::IsNullOrWhiteSpace($imagePath) -or -not (Test-Path $imagePath)) {
                    continue
                }

                $image = [System.Drawing.Image]::FromFile((Resolve-Path $imagePath).Path)
                try {
                    $maxWidth = $cellWidth - 28
                    $maxHeight = $cellHeight - 28
                    $scale = [Math]::Min($maxWidth / $image.Width, $maxHeight / $image.Height)
                    $drawWidth = [int]($image.Width * $scale)
                    $drawHeight = [int]($image.Height * $scale)
                    $drawX = $columnX + [int](($maxWidth - $drawWidth) / 2)
                    $drawY = $y + $rowLabelHeight + 14 + [int](($maxHeight - $drawHeight) / 2)
                    $graphics.DrawImage($image, $drawX, $drawY, $drawWidth, $drawHeight)
                    $graphics.DrawRectangle([System.Drawing.Pens]::LightGray, $drawX, $drawY, $drawWidth, $drawHeight)
                }
                finally {
                    $image.Dispose()
                }
            }
        }

        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $titleFont.Dispose()
        $headingFont.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$repoRoot = Get-RepoRoot
if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = Get-Date -Format "yyyyMMdd-HHmmss"
}

$runRoot = Join-Path (Resolve-Path $repoRoot).Path $OutputRoot
$runDirectory = Join-Path $runRoot $RunId
$foregroundOutput = Join-Path $runDirectory "foreground-captures"
$batchManifestPath = Join-Path $runDirectory "ux-scenario-batch.json"
$batchReportPath = Join-Path $runDirectory "ux-scenario-report.html"
$batchContactSheetPath = Join-Path $runDirectory "ux-scenario-contact-sheet.png"
New-Item -ItemType Directory -Force -Path $foregroundOutput | Out-Null

$freeXPath = Resolve-FreeXExe $repoRoot $FreeXExe -SkipBuild:$SkipBuild
$foregroundProject = Resolve-ForegroundCaptureProject $repoRoot -SkipBuild:$SkipBuild
$pairs = @(Get-ScenarioPairs $Suite)
$records = New-Object System.Collections.Generic.List[object]

foreach ($pair in $pairs) {
    Write-Host "Running UX parity pair '$($pair["id"])'..."
    $comparisonMode = if ($pair.Contains("comparisonMode")) { [string]$pair["comparisonMode"] } else { "paired" }
    $excel = if ($comparisonMode -eq "freex-only") {
        New-NotRequiredScenarioResult "excel-not-required" "Excel capture is not required for this FreeX-only foreground evidence scenario; run the matching Excel scenario separately when COM is available."
    }
    else {
        Invoke-ForegroundScenario $foregroundProject $pair["excelScenario"] $foregroundOutput $freeXPath -NoBuild:$SkipBuild -MaxAttempts $MaxAttempts
    }
    $freex = Invoke-ForegroundScenario $foregroundProject $pair["freexScenario"] $foregroundOutput $freeXPath -NoBuild:$SkipBuild -MaxAttempts $MaxAttempts

    $excelStatus = $excel["manifest"]["captureStatus"]
    $freexStatus = $freex["manifest"]["captureStatus"]
    $pairStatus = if ($comparisonMode -eq "freex-only" -and $freexStatus -eq "complete") {
        "freex-capture-complete"
    }
    elseif ($comparisonMode -eq "freex-only") {
        "blocked"
    }
    elseif ($excelStatus -eq "complete" -and $freexStatus -eq "complete") {
        "paired-capture-complete"
    }
    elseif ($excelStatus -eq "complete" -or $freexStatus -eq "complete") {
        "partial-capture"
    }
    else {
        "blocked"
    }

    $records.Add([ordered]@{
        id = $pair["id"]
        area = $pair["area"]
        comparisonMode = $comparisonMode
        status = $pairStatus
        comparisonStatus = if ($pairStatus -eq "paired-capture-complete") { "needs-human-visual-review" } elseif ($pairStatus -eq "freex-capture-complete") { "needs-freeX-workflow-review" } else { "needs-rerun-or-harness-fix" }
        excel = $excel
        freex = $freex
    })
}

$recordArray = @($records.ToArray())
$pairedCaptureComplete = @($recordArray | Where-Object { $_["status"] -eq "paired-capture-complete" }).Count
$freexCaptureComplete = @($recordArray | Where-Object { $_["status"] -eq "freex-capture-complete" }).Count
$partialCapture = @($recordArray | Where-Object { $_["status"] -eq "partial-capture" }).Count
$blocked = @($recordArray | Where-Object { $_["status"] -eq "blocked" }).Count

$summary = [ordered]@{
    schemaVersion = 1
    suite = $Suite
    runId = $RunId
    status = if ($partialCapture -eq 0 -and $blocked -eq 0) { "ready-for-visual-review" } else { "needs-attention" }
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    machine = $env:COMPUTERNAME
    user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    repo = [ordered]@{
        root = $repoRoot
        branch = Get-GitValue $repoRoot @("rev-parse", "--abbrev-ref", "HEAD")
        commit = Get-GitValue $repoRoot @("rev-parse", "HEAD")
        status = Get-GitValue $repoRoot @("status", "--short", "--branch")
    }
    freexExe = $freeXPath
    foregroundCaptureProject = $foregroundProject
    outputDirectory = $foregroundOutput
    reportPath = $batchReportPath
    contactSheetPath = $batchContactSheetPath
    scenarioCount = $recordArray.Count
    pairedCaptureComplete = $pairedCaptureComplete
    freexCaptureComplete = $freexCaptureComplete
    partialCapture = $partialCapture
    blocked = $blocked
    records = $recordArray
}

Write-ScenarioReport $summary $batchReportPath
Write-ScenarioContactSheet $summary $batchContactSheetPath
$summary | ConvertTo-Json -Depth 20 | Set-Content -Path $batchManifestPath -Encoding UTF8

Write-Host "UX parity scenario batch manifest: $batchManifestPath"
Write-Host "UX parity scenario batch report: $batchReportPath"
Write-Host "UX parity scenario contact sheet: $batchContactSheetPath"
if ($summary.status -ne "ready-for-visual-review") {
    exit 1
}
