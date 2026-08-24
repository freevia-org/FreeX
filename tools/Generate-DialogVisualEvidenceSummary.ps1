param(
    [string]$MarkdownPath = "docs\parity\dialog-visual-evidence-summary.md",
    [string]$JsonPath = "docs\parity\dialog-visual-evidence-summary.json",
    [string]$InventoryPath = "docs\parity\dialog-parity-inventory.json",
    [string]$WpfManifestPath = "docs\parity\dialog-visual-assets\wpf-capture\manifest.json",
    [string]$AvaloniaManifestPath = "docs\parity\dialog-visual-assets\avalonia-capture\manifest.json",
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$visualReviewTriageThreshold = 0.4
$visualReviewTriageThresholdRationale = "This is a deterministic review-prioritization cutoff over the triage score (normalized sample, luma, non-background, and logical-size deltas); it is not a pass/fail or visual-parity acceptance threshold. Rows at or above it remain unresolved review candidates until a human compares the paired evidence."

Add-Type -AssemblyName System.Drawing.Common
Add-Type -AssemblyName System.Private.Windows.GdiPlus
Add-Type -AssemblyName System.Private.Windows.Core
Add-Type -AssemblyName System.Drawing.Primitives
$imageAnalyzerReferences = [AppContext]::GetData("TRUSTED_PLATFORM_ASSEMBLIES").Split([IO.Path]::PathSeparator)
Add-Type -ReferencedAssemblies $imageAnalyzerReferences -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public sealed class DialogPngMetrics
{
    public string Path { get; set; }
    public long FileBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double DpiX { get; set; }
    public double DpiY { get; set; }
    public double LogicalWidth { get; set; }
    public double LogicalHeight { get; set; }
    public long Pixels { get; set; }
    public int DistinctColors { get; set; }
    public double OpaqueRatio { get; set; }
    public double NonBackgroundRatio { get; set; }
    public bool IsNonBlank { get; set; }
    public double MeanAlpha { get; set; }
    public double MeanLuma { get; set; }
    public double MeanRed { get; set; }
    public double MeanGreen { get; set; }
    public double MeanBlue { get; set; }
    public int[] Signature { get; set; }
}

public static class DialogPngAnalyzer
{
    public static DialogPngMetrics Analyze(string path)
    {
        string fullPath = System.IO.Path.GetFullPath(path);
        using (var source = new Bitmap(fullPath))
        using (var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb))
        {
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);
            }

            var fileInfo = new FileInfo(fullPath);
            int width = bitmap.Width;
            int height = bitmap.Height;
            double dpiX = source.HorizontalResolution > 0 ? source.HorizontalResolution : 96.0;
            double dpiY = source.VerticalResolution > 0 ? source.VerticalResolution : 96.0;
            long pixels = (long)width * height;
            var rect = new Rectangle(0, 0, width, height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int stride = Math.Abs(data.Stride);
                byte[] bytes = new byte[stride * height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

                int bgOffset = 0;
                int bgB = bytes[bgOffset];
                int bgG = bytes[bgOffset + 1];
                int bgR = bytes[bgOffset + 2];
                int bgA = bytes[bgOffset + 3];

                var distinctColors = new System.Collections.Hashtable();
                long opaquePixels = 0;
                long nonBackgroundPixels = 0;
                long alphaTotal = 0;
                long redTotal = 0;
                long greenTotal = 0;
                long blueTotal = 0;

                for (int y = 0; y < height; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        int offset = rowOffset + (x * 4);
                        int b = bytes[offset];
                        int g = bytes[offset + 1];
                        int r = bytes[offset + 2];
                        int a = bytes[offset + 3];
                        int argb = unchecked((int)(((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b));

                        distinctColors[argb] = true;
                        alphaTotal += a;
                        redTotal += r;
                        greenTotal += g;
                        blueTotal += b;

                        if (a > 0)
                        {
                            opaquePixels++;
                        }

                        int backgroundDistance = Math.Abs(a - bgA) + Math.Abs(r - bgR) + Math.Abs(g - bgG) + Math.Abs(b - bgB);
                        if (backgroundDistance > 24)
                        {
                            nonBackgroundPixels++;
                        }
                    }
                }

                const int signatureSize = 32;
                int[] signature = new int[signatureSize * signatureSize];
                int signatureIndex = 0;
                for (int sy = 0; sy < signatureSize; sy++)
                {
                    int sourceY = signatureSize == 1 ? 0 : (int)Math.Round((double)sy * (height - 1) / (signatureSize - 1));
                    int rowOffset = sourceY * stride;
                    for (int sx = 0; sx < signatureSize; sx++)
                    {
                        int sourceX = signatureSize == 1 ? 0 : (int)Math.Round((double)sx * (width - 1) / (signatureSize - 1));
                        int offset = rowOffset + (sourceX * 4);
                        int b = bytes[offset];
                        int g = bytes[offset + 1];
                        int r = bytes[offset + 2];
                        int a = bytes[offset + 3];
                        signature[signatureIndex++] = unchecked((int)(((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
                    }
                }

                double meanAlpha = pixels == 0 ? 0 : (double)alphaTotal / pixels;
                double meanRed = pixels == 0 ? 0 : (double)redTotal / pixels;
                double meanGreen = pixels == 0 ? 0 : (double)greenTotal / pixels;
                double meanBlue = pixels == 0 ? 0 : (double)blueTotal / pixels;
                double meanLuma = (0.2126 * meanRed) + (0.7152 * meanGreen) + (0.0722 * meanBlue);
                double opaqueRatio = pixels == 0 ? 0 : (double)opaquePixels / pixels;
                double nonBackgroundRatio = pixels == 0 ? 0 : (double)nonBackgroundPixels / pixels;
                bool isNonBlank = pixels > 0 && opaquePixels > 0 && distinctColors.Count > 1 && nonBackgroundPixels > 0;

                return new DialogPngMetrics
                {
                    Path = fullPath,
                    FileBytes = fileInfo.Length,
                    Width = width,
                    Height = height,
                    DpiX = dpiX,
                    DpiY = dpiY,
                    LogicalWidth = width * 96.0 / dpiX,
                    LogicalHeight = height * 96.0 / dpiY,
                    Pixels = pixels,
                    DistinctColors = distinctColors.Count,
                    OpaqueRatio = opaqueRatio,
                    NonBackgroundRatio = nonBackgroundRatio,
                    IsNonBlank = isNonBlank,
                    MeanAlpha = meanAlpha,
                    MeanLuma = meanLuma,
                    MeanRed = meanRed,
                    MeanGreen = meanGreen,
                    MeanBlue = meanBlue,
                    Signature = signature
                };
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
    }

    public static double SignatureDelta(int[] left, int[] right)
    {
        int length = Math.Min(left.Length, right.Length);
        if (length == 0)
        {
            return 0;
        }

        long total = 0;
        for (int i = 0; i < length; i++)
        {
            int l = left[i];
            int r = right[i];
            total += Math.Abs(((l >> 24) & 0xff) - ((r >> 24) & 0xff));
            total += Math.Abs(((l >> 16) & 0xff) - ((r >> 16) & 0xff));
            total += Math.Abs(((l >> 8) & 0xff) - ((r >> 8) & 0xff));
            total += Math.Abs((l & 0xff) - (r & 0xff));
        }

        return (double)total / (length * 4.0 * 255.0);
    }
}
"@

$repoRoot = Split-Path -Parent $PSScriptRoot
$invariantCulture = [System.Globalization.CultureInfo]::InvariantCulture
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Format-ReportNumber {
    param(
        [Parameter(Mandatory = $true)][double]$Value,
        [string]$Format = "0.000"
    )

    $Value.ToString($Format, $invariantCulture)
}

function Format-DisplayNumber {
    param(
        [Parameter(Mandatory = $true)][double]$Value,
        [double]$IntegerTolerance = 0.25
    )

    $rounded = [math]::Round($Value)
    if ([math]::Abs($Value - $rounded) -le $IntegerTolerance) {
        return ([int]$rounded).ToString($invariantCulture)
    }

    Format-ReportNumber $Value "0.###"
}

function Format-LogicalSize {
    param([Parameter(Mandatory = $true)]$Metric)

    "$(Format-DisplayNumber $Metric.LogicalWidth)x$(Format-DisplayNumber $Metric.LogicalHeight)"
}

function Format-PhysicalSize {
    param([Parameter(Mandatory = $true)]$Metric)

    "$($Metric.Width)x$($Metric.Height) px @ $(Format-DisplayNumber $Metric.DpiX 0.05) DPI"
}

function Resolve-ManifestPngPath {
    param(
        [Parameter(Mandatory = $true)]$Surface,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )

    $png = [string]$Surface.png
    if ([string]::IsNullOrWhiteSpace($png)) {
        return $null
    }

    if ([System.IO.Path]::IsPathRooted($png)) {
        return $png
    }

    Join-Path (Split-Path -Parent (Resolve-ToolRepoPath -Path $ManifestPath -RepoRoot $repoRoot)) $png
}

function Test-ManifestPngExists {
    param(
        [Parameter(Mandatory = $true)]$Surface,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )

    $resolvedPngPath = Resolve-ManifestPngPath -Surface $Surface -ManifestPath $ManifestPath
    if ([string]::IsNullOrWhiteSpace($resolvedPngPath)) {
        return $false
    }

    Test-Path -LiteralPath $resolvedPngPath -PathType Leaf
}

function Get-CapturedSurfaces {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [string]$ManifestPath,
        [switch]$RequirePng
    )

    $capturedSurfaces = @($Manifest.surfaces) |
        Where-Object { $_.captured -eq $true }

    if ($RequirePng) {
        if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
            throw "ManifestPath is required when RequirePng is set."
        }

        $capturedSurfaces = @($capturedSurfaces |
            Where-Object { Test-ManifestPngExists -Surface $_ -ManifestPath $ManifestPath })
    }

    $capturedSurfaces | Sort-Object -Property id
}

function Get-RouteFamily {
    param(
        [Parameter(Mandatory = $true)][string]$SurfaceId,
        [Parameter(Mandatory = $true)][string[]]$RouteIds
    )

    if ($RouteIds -contains $SurfaceId) {
        return $SurfaceId
    }

    $match = $RouteIds |
        Where-Object { $SurfaceId.StartsWith($_ + ".", [System.StringComparison]::Ordinal) } |
        Sort-Object -Property Length -Descending |
        Select-Object -First 1

    if ($null -ne $match) {
        return [string]$match
    }

    return $SurfaceId
}

function Get-PngMetrics {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][ValidateSet("wpf", "avalonia")][string]$CaptureHost
    )

    $metrics = [DialogPngAnalyzer]::Analyze($Path)
    # Avalonia's headless encoder currently emits 120-DPI PNG metadata while
    # its dialog harness captures a 96-DPI logical surface. Preserve the raw
    # metadata for diagnostics, but compare its logical dimensions using the
    # harness contract rather than treating the encoder tag as UI scaling.
    if ($CaptureHost -eq "avalonia") {
        $metrics.LogicalWidth = $metrics.Width
        $metrics.LogicalHeight = $metrics.Height
    }
    $metrics
}

function Get-OptionalStringProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        return $null
    }

    $value = [string]$property.Value
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $null
    }

    return $value
}

function Get-OptionalIntProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        return $null
    }

    return [int]$property.Value
}

function Get-EvidenceProvenance {
    param([Parameter(Mandatory = $true)]$Surface)

    $classification = Get-OptionalStringProperty -Object $Surface -Name "evidenceSource"
    $sourcePng = Get-OptionalStringProperty -Object $Surface -Name "sourcePng"
    $recaptureStatus = Get-OptionalStringProperty -Object $Surface -Name "recaptureStatus"
    $expectedWidth = Get-OptionalIntProperty -Object $Surface -Name "expectedWidth"
    $expectedHeight = Get-OptionalIntProperty -Object $Surface -Name "expectedHeight"
    $hasStructuredProvenance = -not [string]::IsNullOrWhiteSpace($classification) -or
        -not [string]::IsNullOrWhiteSpace($sourcePng) -or
        -not [string]::IsNullOrWhiteSpace($recaptureStatus) -or
        $null -ne $expectedWidth -or
        $null -ne $expectedHeight
    $note = [string]$Surface.note

    if ([string]::IsNullOrWhiteSpace($classification)) {
        $classification = if ($note -match "Promoted from") { "promoted-fallback" } else { "direct-parity-capture" }
    }

    if ([string]::IsNullOrWhiteSpace($recaptureStatus) -and $note -match "parity-capture emitted a transparent") {
        $recaptureStatus = "blocked-transparent-direct-parity-capture"
    }

    if ([string]::IsNullOrWhiteSpace($sourcePng) -and $note -match "\(([^)]*\.png)\)") {
        $sourcePng = $Matches[1]
    }

    [pscustomobject]@{
        classification = $classification
        sourcePng = $sourcePng
        recaptureStatus = $recaptureStatus
        expectedWidth = $expectedWidth
        expectedHeight = $expectedHeight
        manifestNote = $note
        hasStructuredProvenance = $hasStructuredProvenance
    }
}

function Test-StalePromotedExpectedSizeEvidence {
    param([Parameter(Mandatory = $true)]$Row)

    if (-not $Row.comparison.expectedSizeMismatch) {
        return $false
    }

    $wpfClassification = [string]$Row.wpf.provenance.classification
    $avaloniaClassification = [string]$Row.avalonia.provenance.classification
    $wpfMismatch = -not $Row.comparison.wpfExpectedSizeMatch
    $avaloniaMismatch = -not $Row.comparison.avaloniaExpectedSizeMatch
    return ($wpfMismatch -and $wpfClassification.StartsWith("promoted", [System.StringComparison]::OrdinalIgnoreCase)) -or
        ($avaloniaMismatch -and $avaloniaClassification.StartsWith("promoted", [System.StringComparison]::OrdinalIgnoreCase))
}

function Get-StalePromotedExpectedSizeNextAction {
    param(
        [string]$SurfaceId,
        [string]$RecaptureStatus
    )

    if (($SurfaceId -eq "dialog.OpenWorkbook" -or $SurfaceId -eq "dialog.SaveAsWorkbook") -and
        $RecaptureStatus -eq "blocked-blank-wpf-direct-surface-frame-capture") {
        return "Fix the WPF WorkbookFileDialogSurfacePlanner direct-surface frame render so the 640x420 capture produces nonblank pixels; then replace the promoted PNG."
    }

    return "Recapture WPF direct parity evidence at planner size after transparent offscreen capture is fixed."
}

function Get-ExpectedEvidenceSize {
    param(
        [Parameter(Mandatory = $true)][string]$SurfaceId,
        [Parameter(Mandatory = $true)][string]$Shell
    )

    if (($SurfaceId -eq "dialog.OpenWorkbook" -or $SurfaceId -eq "dialog.SaveAsWorkbook") -and
        ($Shell -eq "wpf" -or $Shell -eq "avalonia")) {
        return [pscustomobject]@{
            width = 640
            height = 420
            source = "WorkbookFileDialogSurfacePlanner.Width/Height"
        }
    }

    if ($Shell -eq "wpf" -or $Shell -eq "avalonia") {
        switch ($SurfaceId) {
            { $_ -eq "dialog.FindReplace" -or $_ -eq "dialog.FindReplace.Find" -or $_ -eq "dialog.FindReplace.Replace" } {
                return [pscustomobject]@{
                    width = 720
                    height = 430
                    source = "FindReplaceDialogPlanner.Width/Height"
                }
            }
            "dialog.GoToSpecial" {
                return [pscustomobject]@{
                    width = 430
                    height = 438
                    source = "GoToSpecialDialogPlanner.Width/Height"
                }
            }
            "dialog.InsertHyperlink" {
                return [pscustomobject]@{
                    width = 560
                    height = 300
                    source = "HyperlinkDialogPlanner.Width/Height"
                }
            }
            "dialog.SymbolPicker" {
                return [pscustomobject]@{
                    width = 840
                    height = 620
                    source = "SymbolPickerCatalogPlanner.DialogWidth/DialogHeight"
                }
            }
            "dialog.ConditionalFormatNewRule" {
                return [pscustomobject]@{
                    width = 634
                    height = 334
                    source = "ConditionalFormatDialogCatalog.RuleEditorCaptureWidth/Height"
                }
            }
            "dialog.Consolidate" {
                return [pscustomobject]@{
                    width = 380
                    height = 420
                    source = "ConsolidateDialogPlanner.CaptureWidth/Height"
                }
            }
            "dialog.ShapeGradient" {
                return [pscustomobject]@{
                    width = 500
                    height = 300
                    source = "ShapeGradientPlanner.DialogWidth/DialogHeight"
                }
            }
            "dialog.Sort" {
                return [pscustomobject]@{
                    width = 760
                    height = 500
                    source = "SortDialog.DialogDefaultWidth/DialogDefaultHeight"
                }
            }
            "dialog.WorkbookStatistics" {
                return [pscustomobject]@{
                    width = 500
                    height = 560
                    source = "WorkbookStatisticsDialogPlanner.Width/Height"
                }
            }
            "dialog.ExportOptions" {
                return [pscustomobject]@{
                    width = 430
                    height = 552
                    source = "ExportOptionsDialogSurfacePlanner.CaptureWidth/CaptureHeight"
                }
            }
            "dialog.ProtectWorkbook" {
                return [pscustomobject]@{
                    width = 380
                    height = 250
                    source = "ProtectionDialogPlanner.ProtectWorkbookCaptureWidth/CaptureHeight"
                }
            }
            "dialog.Sparkline" {
                return [pscustomobject]@{
                    width = 380
                    height = 280
                    source = "SparklinePlanner.InsertDialogCaptureWidth/CaptureHeight"
                }
            }
            "dialog.FormatChartArea" {
                return [pscustomobject]@{
                    width = 420
                    height = 590
                    source = "ChartAreaFormatPlanner.DialogWidth/DialogHeight"
                }
            }
            "dialog.ChangeChartType" {
                return [pscustomobject]@{
                    width = 640
                    height = 390
                    source = "ChartTypeChangePlanner.DialogWidth/DialogHeight"
                }
            }
            "dialog.WatchWindow" {
                return [pscustomobject]@{
                    width = 760
                    height = 320
                    source = "WatchWindowDialogPlanner.Width/Height"
                }
            }
            { $_ -eq "dialog.PivotTableOptions" -or $_ -eq "dialog.PivotTableOptions.LayoutAndFormat" } {
                return [pscustomobject]@{
                    width = 520
                    height = 676
                    source = "PivotOptionsPlanner.DialogWidth/LayoutAndFormatCaptureHeight"
                }
            }
            "dialog.ProtectSheet" {
                return [pscustomobject]@{
                    width = 430
                    height = 540
                    source = "ProtectionDialogPlanner.ProtectSheetWidth/Height"
                }
            }
        }
    }

    return $null
}

function New-DimensionMismatchClassification {
    param(
        [Parameter(Mandatory = $true)][string]$Bucket,
        [Parameter(Mandatory = $true)][string]$Reason,
        [Parameter(Mandatory = $true)][string]$NextAction,
        $PolicyAcceptance = $null
    )

    [pscustomobject]@{
        bucket = $Bucket
        reason = $Reason
        nextAction = $NextAction
        policyAcceptance = $PolicyAcceptance
    }
}

function New-NativeDifferencePolicyAcceptance {
    param(
        [Parameter(Mandatory = $true)][string]$Family,
        [Parameter(Mandatory = $true)][string]$Rationale
    )

    [pscustomobject]@{
        status = "policy-accepted"
        policy = "expected platform/native control variance"
        family = $Family
        rationale = $Rationale
        clearCriteria = "Clear only if both shells adopt an explicit shared fixed capture size and the paired screenshots remain content-equivalent."
    }
}

function Get-NativeDifferencePolicyAcceptance {
    param([Parameter(Mandatory = $true)][string]$SurfaceId)

    if ($SurfaceId -eq "dialog.Options" -or
        $SurfaceId.StartsWith("dialog.Options.", [System.StringComparison]::Ordinal)) {
        return New-NativeDifferencePolicyAcceptance `
            -Family "Options host frame" `
            -Rationale "The paired screenshots show the same Options navigation/content contract; the remaining delta is the WPF options host frame versus the Avalonia host frame and default control spacing."
    }

    if ($SurfaceId -eq "dialog.FindReplace" -or
        $SurfaceId.StartsWith("dialog.FindReplace.", [System.StringComparison]::Ordinal)) {
        return New-NativeDifferencePolicyAcceptance `
            -Family "Find/Replace native control stack" `
            -Rationale "The paired screenshots show the same Find/Replace fields and actions; the remaining height delta is native textbox/button spacing and tab-host chrome."
    }

    switch ($SurfaceId) {
        "dialog.ChangeChartType" {
            return New-NativeDifferencePolicyAcceptance `
                -Family "Chart type picker controls" `
                -Rationale "The paired screenshots show the same chart-type list and preview state; the remaining delta is default list, preview, and dialog chrome metrics."
        }
    }

    return $null
}

function ConvertTo-JsonPolicyAcceptance {
    param($PolicyAcceptance)

    if ($null -eq $PolicyAcceptance) {
        return $null
    }

    [ordered]@{
        status = [string]$PolicyAcceptance.status
        policy = [string]$PolicyAcceptance.policy
        family = [string]$PolicyAcceptance.family
        rationale = [string]$PolicyAcceptance.rationale
        clearCriteria = [string]$PolicyAcceptance.clearCriteria
    }
}

function Get-DimensionMismatchClassification {
    param(
        [Parameter(Mandatory = $true)][string]$SurfaceId,
        [Parameter(Mandatory = $true)][bool]$LogicalDimensionMatch,
        [Parameter(Mandatory = $true)][bool]$ExpectedSizeMismatch,
        [Parameter(Mandatory = $true)][double]$LogicalWidthDelta,
        [Parameter(Mandatory = $true)][double]$LogicalHeightDelta
    )

    if ($LogicalDimensionMatch) {
        return $null
    }

    if ($ExpectedSizeMismatch) {
        return New-DimensionMismatchClassification `
            -Bucket "evidence limitation" `
            -Reason "The checked-in PNG disagrees with an explicit expected capture size, so the dimension delta is suspect evidence." `
            -NextAction "Recapture or replace the stale evidence before treating this as a product layout mismatch."
    }

    switch ($SurfaceId) {
        "dialog.SelectionPane" {
            return New-DimensionMismatchClassification `
                -Bucket "content/visual mismatch" `
                -Reason "The committed PNGs use different sample objects and selected rows." `
                -NextAction "Align the sample worksheet objects and selection state, then re-rank the surface."
        }
        "dialog.AccessibilityChecker" {
            return New-DimensionMismatchClassification `
                -Bucket "content/visual mismatch" `
                -Reason "The committed PNGs show different issue models: a compact WPF list versus an Avalonia grouped inspection tree." `
                -NextAction "Compare the Accessibility Checker data model and decide whether the grouped Avalonia presentation is intentional."
        }
        "dialog.SymbolPicker" {
            return New-DimensionMismatchClassification `
                -Bucket "content/visual mismatch" `
                -Reason "The committed PNGs show different symbol-picker presentations, including Avalonia search/detail content absent from WPF." `
                -NextAction "Decide the target symbol-picker contract, then align either WPF evidence state or Avalonia layout."
        }
        "dialog.Options.Formulas" {
            return New-DimensionMismatchClassification `
                -Bucket "content/visual mismatch" `
                -Reason "The WPF capture exposes a much taller Formulas options page than the fixed-height Avalonia Options frame." `
                -NextAction "Review the Formulas page content height and scrolling contract before changing dialog dimensions."
        }
        "dialog.GoalSeekStatus" {
            return New-DimensionMismatchClassification `
                -Bucket "evidence limitation" `
                -Reason "The semantic status content matches, but the WPF PNG includes extra bottom capture area." `
                -NextAction "Recapture or tighten the WPF status crop before treating the height delta as a product bug."
        }
        "dialog.PivotTableOptions.Display" {
            return New-DimensionMismatchClassification `
                -Bucket "evidence limitation" `
                -Reason "Only a near-one-DIP height delta remains, consistent with border or capture rounding." `
                -NextAction "Leave below the product-action threshold unless a future recapture widens the delta."
        }
    }

    if ($SurfaceId.StartsWith("dialog.FormatCells", [System.StringComparison]::Ordinal)) {
        return New-DimensionMismatchClassification `
            -Bucket "content/visual mismatch" `
            -Reason "The Format Cells captures differ in tab/control presentation and content density in addition to size." `
            -NextAction "Review the Format Cells tab model, tab order, and target frame size together."
    }

    if ($SurfaceId -eq "dialog.AutoFilter" -or $SurfaceId -eq "dialog.SelectDataSource") {
        return New-DimensionMismatchClassification `
            -Bucket "content/visual mismatch" `
            -Reason "The committed PNGs show different dialog content/state, so the size delta is not isolated layout evidence." `
            -NextAction "Align the harness data/state first, then reclassify any residual logical-size delta."
    }

    if ($SurfaceId.StartsWith("dialog.Options.", [System.StringComparison]::Ordinal) -or
        $SurfaceId -eq "dialog.Options" -or
        $SurfaceId.StartsWith("dialog.FindReplace", [System.StringComparison]::Ordinal) -or
        $SurfaceId -eq "dialog.ChangeChartType" -or
        $SurfaceId -eq "dialog.ExportOptions" -or
        $SurfaceId -eq "dialog.ProtectWorkbook" -or
        $SurfaceId -eq "dialog.Sparkline") {
        $policyAcceptance = Get-NativeDifferencePolicyAcceptance -SurfaceId $SurfaceId
        $reason = if ($null -eq $policyAcceptance) { "The PNGs show the same paired surface with small WPF/Avalonia control, chrome, or default-spacing differences." } else { [string]$policyAcceptance.rationale }
        return New-DimensionMismatchClassification `
            -Bucket "expected platform/native difference" `
            -Reason $reason `
            -NextAction "Policy accepted as native/control variance; keep tracked separately from content, evidence, or real logical-size mismatches." `
            -PolicyAcceptance $policyAcceptance
    }

    $largestDelta = [math]::Max([math]::Abs($LogicalWidthDelta), [math]::Abs($LogicalHeightDelta))
    if ($largestDelta -le 1.25) {
        return New-DimensionMismatchClassification `
            -Bucket "evidence limitation" `
            -Reason "The remaining logical-size delta is within capture rounding noise." `
            -NextAction "Treat as low-priority evidence noise unless a future capture increases the delta."
    }

    New-DimensionMismatchClassification `
        -Bucket "real logical-size mismatch" `
        -Reason "The paired surface has a DPI-normalized logical size delta above tolerance with no known evidence-only exemption." `
        -NextAction "Review the WPF/Avalonia planner or layout target, then align the size or document an intentional size contract."
}

function ConvertTo-JsonMetric {
    param(
        [Parameter(Mandatory = $true)]$Metric,
        $Provenance = $null
    )

    $model = [ordered]@{
        relativePath = ConvertTo-ToolRepoRelativePath -Path $Metric.Path -RepoRoot $repoRoot
        fileBytes = $Metric.FileBytes
        width = $Metric.Width
        height = $Metric.Height
        dpiX = [math]::Round($Metric.DpiX, 6)
        dpiY = [math]::Round($Metric.DpiY, 6)
        logicalWidth = [math]::Round($Metric.LogicalWidth, 3)
        logicalHeight = [math]::Round($Metric.LogicalHeight, 3)
        pixels = $Metric.Pixels
        distinctColors = $Metric.DistinctColors
        opaqueRatio = [math]::Round($Metric.OpaqueRatio, 6)
        nonBackgroundRatio = [math]::Round($Metric.NonBackgroundRatio, 6)
        isNonBlank = $Metric.IsNonBlank
        meanLuma = [math]::Round($Metric.MeanLuma, 6)
    }

    if ($null -ne $Provenance -and $Provenance.hasStructuredProvenance) {
        $model.manifestNote = [string]$Provenance.manifestNote
        $model.evidenceSource = [string]$Provenance.classification
        $model.sourcePng = $Provenance.sourcePng
        $model.recaptureStatus = $Provenance.recaptureStatus
        $model.expectedWidth = $Provenance.expectedWidth
        $model.expectedHeight = $Provenance.expectedHeight
    }

    $model
}

function Compare-PngMetrics {
    param(
        [Parameter(Mandatory = $true)]$WpfMetric,
        [Parameter(Mandatory = $true)]$AvaloniaMetric,
        [Parameter(Mandatory = $true)][string]$SurfaceId
    )

    $sampleMeanDelta = [DialogPngAnalyzer]::SignatureDelta($WpfMetric.Signature, $AvaloniaMetric.Signature)
    $rawPixelWidthDelta = [math]::Abs([int]$WpfMetric.Width - [int]$AvaloniaMetric.Width)
    $rawPixelHeightDelta = [math]::Abs([int]$WpfMetric.Height - [int]$AvaloniaMetric.Height)
    $logicalWidthDelta = [math]::Abs([double]$WpfMetric.LogicalWidth - [double]$AvaloniaMetric.LogicalWidth)
    $logicalHeightDelta = [math]::Abs([double]$WpfMetric.LogicalHeight - [double]$AvaloniaMetric.LogicalHeight)
    $logicalDimensionTolerance = 0.5
    $logicalDimensionMatch = ($logicalWidthDelta -le $logicalDimensionTolerance -and $logicalHeightDelta -le $logicalDimensionTolerance)
    $rawPixelDimensionMatch = ($rawPixelWidthDelta -eq 0 -and $rawPixelHeightDelta -eq 0)
    $dimensionDeltaRatio = if ($WpfMetric.LogicalWidth -le 0 -or $WpfMetric.LogicalHeight -le 0) {
        1.0
    }
    else {
        ([math]::Abs([double]$AvaloniaMetric.LogicalWidth - [double]$WpfMetric.LogicalWidth) / [double]$WpfMetric.LogicalWidth) +
            ([math]::Abs([double]$AvaloniaMetric.LogicalHeight - [double]$WpfMetric.LogicalHeight) / [double]$WpfMetric.LogicalHeight)
    }

    $lumaDelta = [math]::Abs([double]$WpfMetric.MeanLuma - [double]$AvaloniaMetric.MeanLuma) / 255.0
    $nonBackgroundDelta = [math]::Abs([double]$WpfMetric.NonBackgroundRatio - [double]$AvaloniaMetric.NonBackgroundRatio)
    $triageScore = $sampleMeanDelta + $lumaDelta + $nonBackgroundDelta + [math]::Min($dimensionDeltaRatio, 2.0)
    $wpfExpectedSize = Get-ExpectedEvidenceSize -SurfaceId $SurfaceId -Shell "wpf"
    $avaloniaExpectedSize = Get-ExpectedEvidenceSize -SurfaceId $SurfaceId -Shell "avalonia"
    $wpfExpectedMatch = $true
    $avaloniaExpectedMatch = $true
    $expectedSizeSource = $null
    if ($null -ne $wpfExpectedSize) {
        $wpfExpectedMatch = ([math]::Abs([double]$WpfMetric.LogicalWidth - [double]$wpfExpectedSize.width) -le $logicalDimensionTolerance -and [math]::Abs([double]$WpfMetric.LogicalHeight - [double]$wpfExpectedSize.height) -le $logicalDimensionTolerance)
        $expectedSizeSource = [string]$wpfExpectedSize.source
    }
    if ($null -ne $avaloniaExpectedSize) {
        $avaloniaExpectedMatch = ([math]::Abs([double]$AvaloniaMetric.LogicalWidth - [double]$avaloniaExpectedSize.width) -le $logicalDimensionTolerance -and [math]::Abs([double]$AvaloniaMetric.LogicalHeight - [double]$avaloniaExpectedSize.height) -le $logicalDimensionTolerance)
        if ($null -eq $expectedSizeSource) {
            $expectedSizeSource = [string]$avaloniaExpectedSize.source
        }
    }
    $hasExpectedSize = $null -ne $wpfExpectedSize -or $null -ne $avaloniaExpectedSize
    $expectedWidth = if ($null -ne $wpfExpectedSize) { [int]$wpfExpectedSize.width } elseif ($null -ne $avaloniaExpectedSize) { [int]$avaloniaExpectedSize.width } else { $null }
    $expectedHeight = if ($null -ne $wpfExpectedSize) { [int]$wpfExpectedSize.height } elseif ($null -ne $avaloniaExpectedSize) { [int]$avaloniaExpectedSize.height } else { $null }
    $expectedSizeMismatch = $hasExpectedSize -and (-not $wpfExpectedMatch -or -not $avaloniaExpectedMatch)
    $dimensionMismatchClassification = Get-DimensionMismatchClassification `
        -SurfaceId $SurfaceId `
        -LogicalDimensionMatch $logicalDimensionMatch `
        -ExpectedSizeMismatch $expectedSizeMismatch `
        -LogicalWidthDelta $logicalWidthDelta `
        -LogicalHeightDelta $logicalHeightDelta

    [pscustomobject]@{
        widthDelta = $logicalWidthDelta
        heightDelta = $logicalHeightDelta
        dimensionMatch = $logicalDimensionMatch
        logicalDimensionMatch = $logicalDimensionMatch
        logicalWidthDelta = $logicalWidthDelta
        logicalHeightDelta = $logicalHeightDelta
        logicalDimensionTolerance = $logicalDimensionTolerance
        rawPixelDimensionMatch = $rawPixelDimensionMatch
        rawPixelWidthDelta = [int]$rawPixelWidthDelta
        rawPixelHeightDelta = [int]$rawPixelHeightDelta
        captureScaleNormalizedDimensionMatch = (-not $rawPixelDimensionMatch -and $logicalDimensionMatch)
        dimensionDeltaRatio = $dimensionDeltaRatio
        sampleMeanDelta = $sampleMeanDelta
        lumaDelta = $lumaDelta
        nonBackgroundDelta = $nonBackgroundDelta
        triageScore = $triageScore
        hasExpectedSize = $hasExpectedSize
        expectedWidth = $expectedWidth
        expectedHeight = $expectedHeight
        expectedSizeSource = $expectedSizeSource
        wpfExpectedSizeMatch = $wpfExpectedMatch
        avaloniaExpectedSizeMatch = $avaloniaExpectedMatch
        expectedSizeMismatch = $expectedSizeMismatch
        dimensionMismatchBucket = if ($null -eq $dimensionMismatchClassification) { $null } else { [string]$dimensionMismatchClassification.bucket }
        dimensionMismatchReason = if ($null -eq $dimensionMismatchClassification) { $null } else { [string]$dimensionMismatchClassification.reason }
        dimensionMismatchNextAction = if ($null -eq $dimensionMismatchClassification) { $null } else { [string]$dimensionMismatchClassification.nextAction }
        policyAcceptance = if ($null -eq $dimensionMismatchClassification) { $null } else { ConvertTo-JsonPolicyAcceptance $dimensionMismatchClassification.policyAcceptance }
    }
}

function New-PngEvidence {
    param(
        [Parameter(Mandatory = $true)]$Surface,
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][ValidateSet("wpf", "avalonia")][string]$CaptureHost
    )

    $pngPath = Resolve-ManifestPngPath -Surface $Surface -ManifestPath $ManifestPath
    if ([string]::IsNullOrWhiteSpace($pngPath)) {
        throw "Captured surface '$($Surface.id)' does not declare a PNG path."
    }

    $metrics = Get-PngMetrics -Path $pngPath -CaptureHost $CaptureHost

    [pscustomobject]@{
        id = [string]$Surface.id
        png = [string]$Surface.png
        note = [string]$Surface.note
        provenance = Get-EvidenceProvenance -Surface $Surface
        metrics = $metrics
    }
}

$inventory = Read-ToolJson -Path $InventoryPath -RepoRoot $repoRoot -MissingMessage "Required dialog evidence input was not found"
$wpfManifest = Read-ToolJson -Path $WpfManifestPath -RepoRoot $repoRoot -MissingMessage "Required dialog evidence input was not found"
$avaloniaManifest = Read-ToolJson -Path $AvaloniaManifestPath -RepoRoot $repoRoot -MissingMessage "Required dialog evidence input was not found"

$inventoryRows = @($inventory.rows)
$routeIds = @($inventoryRows | ForEach-Object { [string]$_.routeId })

$wpfSurfaces = @(Get-CapturedSurfaces -Manifest $wpfManifest -ManifestPath $WpfManifestPath -RequirePng)
$avaloniaSurfaces = @(Get-CapturedSurfaces -Manifest $avaloniaManifest -ManifestPath $AvaloniaManifestPath -RequirePng)

$wpfById = [ordered]@{}
foreach ($surface in $wpfSurfaces) {
    $wpfById[[string]$surface.id] = New-PngEvidence -Surface $surface -ManifestPath $WpfManifestPath -CaptureHost "wpf"
}

$avaloniaById = [ordered]@{}
foreach ($surface in $avaloniaSurfaces) {
    $avaloniaById[[string]$surface.id] = New-PngEvidence -Surface $surface -ManifestPath $AvaloniaManifestPath -CaptureHost "avalonia"
}

$pairedIds = @($wpfById.Keys | Where-Object { $avaloniaById.Contains($_) } | Sort-Object)
$wpfOnlyIds = @($wpfById.Keys | Where-Object { -not $avaloniaById.Contains($_) } | Sort-Object)
$avaloniaOnlyIds = @($avaloniaById.Keys | Where-Object { -not $wpfById.Contains($_) } | Sort-Object)

$pairedRows = @(
    foreach ($id in $pairedIds) {
        $wpfEvidence = $wpfById[$id]
        $avaloniaEvidence = $avaloniaById[$id]
        $comparison = Compare-PngMetrics -WpfMetric $wpfEvidence.metrics -AvaloniaMetric $avaloniaEvidence.metrics -SurfaceId $id

        [pscustomobject]@{
            id = $id
            routeFamily = Get-RouteFamily -SurfaceId $id -RouteIds $routeIds
            wpf = $wpfEvidence
            avalonia = $avaloniaEvidence
            comparison = $comparison
        }
    }
)

$avaloniaOnlyRows = @(
    foreach ($id in $avaloniaOnlyIds) {
        $avaloniaEvidence = $avaloniaById[$id]
        [pscustomobject]@{
            id = $id
            routeFamily = Get-RouteFamily -SurfaceId $id -RouteIds $routeIds
            avalonia = $avaloniaEvidence
        }
    }
)

$additionalGroups = @(
    $avaloniaOnlyRows |
        Group-Object -Property routeFamily |
        Sort-Object -Property Name
)

$allPngRows = @($pairedRows | ForEach-Object { $_.wpf; $_.avalonia }) + @($avaloniaOnlyRows | ForEach-Object { $_.avalonia })
$blankEvidenceRows = @($allPngRows | Where-Object { -not $_.metrics.IsNonBlank } | Sort-Object -Property id)
$dimensionMismatchRows = @($pairedRows | Where-Object { -not $_.comparison.dimensionMatch } | Sort-Object -Property id)
$rawPixelDimensionMismatchRows = @($pairedRows | Where-Object { -not $_.comparison.rawPixelDimensionMatch } | Sort-Object -Property id)
$captureScaleNormalizedDimensionRows = @($pairedRows | Where-Object { $_.comparison.captureScaleNormalizedDimensionMatch } | Sort-Object -Property id)
$expectedSizeMismatchRows = @($pairedRows | Where-Object { $_.comparison.expectedSizeMismatch } | Sort-Object -Property id)
$stalePromotedExpectedSizeRows = @($expectedSizeMismatchRows | Where-Object { Test-StalePromotedExpectedSizeEvidence -Row $_ } | Sort-Object -Property id)
$policyAcceptedNativeDifferenceRows = @($dimensionMismatchRows | Where-Object {
        $null -ne $_.comparison.policyAcceptance -and
        $_.comparison.policyAcceptance.status -eq "policy-accepted"
    } | Sort-Object -Property id)
$topOutlierRows = @($pairedRows | Sort-Object @{ Expression = { $_.comparison.triageScore }; Descending = $true }, @{ Expression = { $_.id }; Ascending = $true } | Select-Object -First 10)
$visualReviewCandidateRows = @($pairedRows | Where-Object {
        $_.comparison.triageScore -ge $visualReviewTriageThreshold
    } | Sort-Object @{ Expression = { $_.comparison.triageScore }; Descending = $true }, @{ Expression = { $_.id }; Ascending = $true })
$dimensionMismatchBucketGroups = @(
    $dimensionMismatchRows |
        Group-Object -Property { $_.comparison.dimensionMismatchBucket } |
        Sort-Object -Property Name
)
$policyAcceptedNativeDifferenceGroups = @(
    $policyAcceptedNativeDifferenceRows |
        Group-Object -Property { $_.comparison.policyAcceptance.family } |
        Sort-Object -Property Name
)

$wpfManifestRelativePath = ConvertTo-ToolRepoRelativePath -Path (Resolve-ToolRepoPath -Path $WpfManifestPath -RepoRoot $repoRoot) -RepoRoot $repoRoot
$avaloniaManifestRelativePath = ConvertTo-ToolRepoRelativePath -Path (Resolve-ToolRepoPath -Path $AvaloniaManifestPath -RepoRoot $repoRoot) -RepoRoot $repoRoot
$inventoryRelativePath = ConvertTo-ToolRepoRelativePath -Path (Resolve-ToolRepoPath -Path $InventoryPath -RepoRoot $repoRoot) -RepoRoot $repoRoot
$resolvedMarkdownPath = Resolve-ToolRepoPath -Path $MarkdownPath -RepoRoot $repoRoot
$resolvedJsonPath = Resolve-ToolRepoPath -Path $JsonPath -RepoRoot $repoRoot
$jsonRelativePath = ConvertTo-ToolRepoRelativePath -Path $resolvedJsonPath -RepoRoot $repoRoot

$jsonModel = [ordered]@{
    schemaVersion = 1
    generatedBy = "tools/Generate-DialogVisualEvidenceSummary.ps1"
    sources = [ordered]@{
        inventory = $inventoryRelativePath
        wpfManifest = $wpfManifestRelativePath
        avaloniaManifest = $avaloniaManifestRelativePath
    }
    summary = [ordered]@{
        dialogRoutes = [int]$inventory.summary.totalRoutes
        wpfCaptureEvidence = [int]$inventory.summary.wpfCaptures
        avaloniaCaptureEvidence = [int]$inventory.summary.avaloniaCaptures
        avaloniaHarnessRoutes = [int]$inventory.summary.avaloniaHarnessRoutes
        sharedOrPresentationBackedRoutes = [int]$inventory.summary.sharedOrPresentationBacked
        wpfCapturedManifestSurfaces = [int]$wpfSurfaces.Count
        avaloniaCapturedManifestSurfaces = [int]$avaloniaSurfaces.Count
        pairedCapturedSurfaceIds = [int]$pairedIds.Count
        wpfManifestIdsWithoutAvaloniaPair = [int]$wpfOnlyIds.Count
        additionalAvaloniaCapturedSurfaceIds = [int]$avaloniaOnlyIds.Count
        nonBlankPngFailures = [int]$blankEvidenceRows.Count
        pairedDimensionMismatches = [int]$dimensionMismatchRows.Count
        pairedRawPixelDimensionMismatches = [int]$rawPixelDimensionMismatchRows.Count
        pairedCaptureScaleNormalizedDimensionMatches = [int]$captureScaleNormalizedDimensionRows.Count
        pairedExpectedSizeMismatches = [int]$expectedSizeMismatchRows.Count
        stalePromotedExpectedSizeEvidence = [int]$stalePromotedExpectedSizeRows.Count
        policyAcceptedNativeDifferences = [int]$policyAcceptedNativeDifferenceRows.Count
        visualReviewTriageThreshold = $visualReviewTriageThreshold
        visualReviewTriageThresholdRationale = $visualReviewTriageThresholdRationale
        visualReviewCandidateCount = [int]$visualReviewCandidateRows.Count
        highestTriageScore = if ($pairedRows.Count -eq 0) { 0 } else { [math]::Round(([double]($pairedRows | ForEach-Object { [double]$_.comparison.triageScore } | Measure-Object -Maximum).Maximum), 6) }
        dimensionMismatchBuckets = [ordered]@{}
    }
    pairedSurfaces = @(
        foreach ($row in $pairedRows) {
            [ordered]@{
                id = $row.id
                routeFamily = $row.routeFamily
                wpf = ConvertTo-JsonMetric -Metric $row.wpf.metrics -Provenance $row.wpf.provenance
                avalonia = ConvertTo-JsonMetric -Metric $row.avalonia.metrics -Provenance $row.avalonia.provenance
                comparison = [ordered]@{
                    dimensionMatch = $row.comparison.dimensionMatch
                    widthDelta = [math]::Round($row.comparison.widthDelta, 3)
                    heightDelta = [math]::Round($row.comparison.heightDelta, 3)
                    logicalDimensionMatch = $row.comparison.logicalDimensionMatch
                    logicalWidthDelta = [math]::Round($row.comparison.logicalWidthDelta, 3)
                    logicalHeightDelta = [math]::Round($row.comparison.logicalHeightDelta, 3)
                    logicalDimensionTolerance = [math]::Round($row.comparison.logicalDimensionTolerance, 3)
                    rawPixelDimensionMatch = $row.comparison.rawPixelDimensionMatch
                    rawPixelWidthDelta = $row.comparison.rawPixelWidthDelta
                    rawPixelHeightDelta = $row.comparison.rawPixelHeightDelta
                    captureScaleNormalizedDimensionMatch = $row.comparison.captureScaleNormalizedDimensionMatch
                    dimensionDeltaRatio = [math]::Round($row.comparison.dimensionDeltaRatio, 6)
                    sampleMeanDelta = [math]::Round($row.comparison.sampleMeanDelta, 6)
                    lumaDelta = [math]::Round($row.comparison.lumaDelta, 6)
                    nonBackgroundDelta = [math]::Round($row.comparison.nonBackgroundDelta, 6)
                    triageScore = [math]::Round($row.comparison.triageScore, 6)
                    expectedSizeMismatch = $row.comparison.expectedSizeMismatch
                    expectedWidth = $row.comparison.expectedWidth
                    expectedHeight = $row.comparison.expectedHeight
                    expectedSizeSource = $row.comparison.expectedSizeSource
                    wpfExpectedSizeMatch = $row.comparison.wpfExpectedSizeMatch
                    avaloniaExpectedSizeMatch = $row.comparison.avaloniaExpectedSizeMatch
                    dimensionMismatchBucket = $row.comparison.dimensionMismatchBucket
                    dimensionMismatchReason = $row.comparison.dimensionMismatchReason
                    dimensionMismatchNextAction = $row.comparison.dimensionMismatchNextAction
                    policyAcceptance = $row.comparison.policyAcceptance
                }
            }
        }
    )
    topPairedOutliers = @(
        foreach ($row in $topOutlierRows) {
            [ordered]@{
                id = $row.id
                triageScore = [math]::Round($row.comparison.triageScore, 6)
                sampleMeanDelta = [math]::Round($row.comparison.sampleMeanDelta, 6)
                dimension = "$([math]::Round($row.wpf.metrics.LogicalWidth, 3))x$([math]::Round($row.wpf.metrics.LogicalHeight, 3)) vs $([math]::Round($row.avalonia.metrics.LogicalWidth, 3))x$([math]::Round($row.avalonia.metrics.LogicalHeight, 3)) logical; $($row.wpf.metrics.Width)x$($row.wpf.metrics.Height) vs $($row.avalonia.metrics.Width)x$($row.avalonia.metrics.Height) px"
                dimensionMismatchBucket = $row.comparison.dimensionMismatchBucket
                nonBackgroundDelta = [math]::Round($row.comparison.nonBackgroundDelta, 6)
            }
        }
    )
    visualReviewCandidates = @(
        foreach ($row in $visualReviewCandidateRows) {
            [ordered]@{
                id = $row.id
                triageScore = [math]::Round($row.comparison.triageScore, 6)
                reviewStatus = "unresolved visual review candidate"
                logicalDimensionMatch = $row.comparison.logicalDimensionMatch
                dimensionMismatchBucket = $row.comparison.dimensionMismatchBucket
                expectedSizeMismatch = $row.comparison.expectedSizeMismatch
                reviewReason = if ($row.comparison.expectedSizeMismatch) { "High image delta with suspect expected-size evidence; recapture before drawing a product conclusion." } elseif ($null -ne $row.comparison.policyAcceptance) { "High image delta retained for visual review even though a dimension difference has policy-accepted native/control variance." } else { "High image delta requires paired WPF/Avalonia visual review; equal dimensions do not resolve it." }
            }
        }
    )
    dimensionMismatchClassification = @(
        foreach ($group in $dimensionMismatchBucketGroups) {
            $groupRows = @($group.Group | Sort-Object @{ Expression = { $_.comparison.triageScore }; Descending = $true }, @{ Expression = { $_.id }; Ascending = $true })
            [ordered]@{
                bucket = [string]$group.Name
                count = [int]$group.Count
                topSurfaceIds = @($groupRows | Select-Object -First 5 | ForEach-Object { $_.id })
                nextAction = [string]$groupRows[0].comparison.dimensionMismatchNextAction
                policyAccepted = @($groupRows | Where-Object {
                        $null -ne $_.comparison.policyAcceptance -and
                        $_.comparison.policyAcceptance.status -eq "policy-accepted"
                    }).Count -eq $groupRows.Count
            }
        }
    )
    policyAcceptedNativeDifferenceFamilies = @(
        foreach ($group in $policyAcceptedNativeDifferenceGroups) {
            $groupRows = @($group.Group | Sort-Object -Property id)
            [ordered]@{
                family = [string]$group.Name
                count = [int]$group.Count
                surfaceIds = @($groupRows | ForEach-Object { $_.id })
                rationale = [string]$groupRows[0].comparison.policyAcceptance.rationale
                clearCriteria = [string]$groupRows[0].comparison.policyAcceptance.clearCriteria
            }
        }
    )
    dimensionMismatchDetails = @(
        foreach ($row in $dimensionMismatchRows) {
            [ordered]@{
                id = $row.id
                bucket = $row.comparison.dimensionMismatchBucket
                reason = $row.comparison.dimensionMismatchReason
                nextAction = $row.comparison.dimensionMismatchNextAction
                policyAcceptance = $row.comparison.policyAcceptance
                wpfLogicalSize = "$(Format-LogicalSize $row.wpf.metrics)"
                avaloniaLogicalSize = "$(Format-LogicalSize $row.avalonia.metrics)"
                logicalWidthDelta = [math]::Round($row.comparison.logicalWidthDelta, 3)
                logicalHeightDelta = [math]::Round($row.comparison.logicalHeightDelta, 3)
                triageScore = [math]::Round($row.comparison.triageScore, 6)
            }
        }
    )
    additionalAvaloniaSurfacesNeedingWpfPair = @(
        foreach ($row in $avaloniaOnlyRows) {
            [ordered]@{
                id = $row.id
                routeFamily = $row.routeFamily
                avalonia = ConvertTo-JsonMetric -Metric $row.avalonia.metrics -Provenance $row.avalonia.provenance
            }
        }
    )
    additionalAvaloniaRouteFamilies = @(
        foreach ($group in $additionalGroups) {
            [ordered]@{
                routeFamily = $group.Name
                count = [int]$group.Count
                ids = @($group.Group | Sort-Object -Property id | ForEach-Object { $_.id })
            }
        }
    )
}

foreach ($group in $dimensionMismatchBucketGroups) {
    $jsonModel.summary.dimensionMismatchBuckets[[string]$group.Name] = [int]$group.Count
}

$json = ($jsonModel | ConvertTo-Json -Depth 12) + [Environment]::NewLine

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Dialog visual evidence summary")
[void]$md.AppendLine()
[void]$md.AppendLine("Generated by tools/Generate-DialogVisualEvidenceSummary.ps1 from committed dialog capture manifests, PNG evidence, and the generated dialog parity inventory.")
[void]$md.AppendLine()
[void]$md.AppendLine("This deterministic triage report compares checked-in WPF and Avalonia capture PNGs. It is not a full visual-parity claim; it flags stale or suspect evidence, ranks paired screenshot outliers by simple image metrics, and names Avalonia-manifest-only screenshot surface ids that still need an exact WPF manifest PNG id pair. Dimension comparisons are DPI-normalized to 96-DPI logical units so high-DPI WPF captures are compared like-for-like with Avalonia logical captures; raw PNG pixel dimensions remain reported as capture metadata.")
[void]$md.AppendLine()
[void]$md.AppendLine("Sources:")
[void]$md.AppendLine()
[void]$md.AppendLine("- $inventoryRelativePath")
[void]$md.AppendLine("- $wpfManifestRelativePath")
[void]$md.AppendLine("- $avaloniaManifestRelativePath")
[void]$md.AppendLine("- $jsonRelativePath")
[void]$md.AppendLine()
[void]$md.AppendLine("## Current inventory rollup")
[void]$md.AppendLine()
[void]$md.AppendLine("| Metric | Count |")
[void]$md.AppendLine("| --- | ---: |")
[void]$md.AppendLine("| Dialog routes | $($inventory.summary.totalRoutes) |")
[void]$md.AppendLine("| WPF capture evidence | $($inventory.summary.wpfCaptures) |")
[void]$md.AppendLine("| Avalonia capture evidence | $($inventory.summary.avaloniaCaptures) |")
[void]$md.AppendLine("| Avalonia harness routes | $($inventory.summary.avaloniaHarnessRoutes) |")
[void]$md.AppendLine("| Shared/presentation-backed routes | $($inventory.summary.sharedOrPresentationBacked) |")
[void]$md.AppendLine()
[void]$md.AppendLine("## Manifest and PNG triage")
[void]$md.AppendLine()
[void]$md.AppendLine("| Metric | Count |")
[void]$md.AppendLine("| --- | ---: |")
[void]$md.AppendLine("| WPF captured manifest surfaces with committed PNGs | $($wpfSurfaces.Count) |")
[void]$md.AppendLine("| Avalonia captured manifest surfaces with committed PNGs | $($avaloniaSurfaces.Count) |")
[void]$md.AppendLine("| Paired captured surface ids | $($pairedIds.Count) |")
[void]$md.AppendLine("| WPF manifest ids without Avalonia pair | $($wpfOnlyIds.Count) |")
[void]$md.AppendLine("| Avalonia-manifest-only screenshot surface ids needing WPF manifest pair | $($avaloniaOnlyIds.Count) |")
[void]$md.AppendLine("| Nonblank PNG check failures | $($blankEvidenceRows.Count) |")
[void]$md.AppendLine("| Paired dimension mismatches (scale-aware logical units) | $($dimensionMismatchRows.Count) |")
[void]$md.AppendLine("| Raw PNG pixel dimension mismatches | $($rawPixelDimensionMismatchRows.Count) |")
[void]$md.AppendLine("| Raw PNG mismatches normalized by capture DPI | $($captureScaleNormalizedDimensionRows.Count) |")
[void]$md.AppendLine("| Paired expected-size evidence mismatches | $($expectedSizeMismatchRows.Count) |")
[void]$md.AppendLine("| Stale promoted expected-size evidence | $($stalePromotedExpectedSizeRows.Count) |")
[void]$md.AppendLine("| Policy-accepted native/control differences | $($policyAcceptedNativeDifferenceRows.Count) |")
[void]$md.AppendLine("| High-delta visual review candidates | $($visualReviewCandidateRows.Count) |")
[void]$md.AppendLine("| Visual review triage threshold | $visualReviewTriageThreshold |")
[void]$md.AppendLine()

[void]$md.AppendLine("## Visual Review Queue")
[void]$md.AppendLine()
[void]$md.AppendLine("This queue is a deterministic prioritization aid, not a pass/fail result. The threshold is ${visualReviewTriageThreshold}: $visualReviewTriageThresholdRationale")
[void]$md.AppendLine()
[void]$md.AppendLine("Equal logical dimensions, nonblank PNGs, and paired manifest ids establish evidence coverage and size comparability only; they do not establish visual parity. The $($visualReviewCandidateRows.Count) rows below remain unresolved high-delta candidates.")
[void]$md.AppendLine()
[void]$md.AppendLine("| Surface id | Score | Logical dimensions match | Dimension bucket | Expected-size mismatch | Review status | Review reason |")
[void]$md.AppendLine("| --- | ---: | --- | --- | --- | --- | --- |")
foreach ($row in $visualReviewCandidateRows) {
    $bucket = if ([string]::IsNullOrWhiteSpace([string]$row.comparison.dimensionMismatchBucket)) { "none" } else { [string]$row.comparison.dimensionMismatchBucket }
    $reviewReason = if ($row.comparison.expectedSizeMismatch) { "High image delta with suspect expected-size evidence; recapture before drawing a product conclusion." } elseif ($null -ne $row.comparison.policyAcceptance) { "High image delta retained for visual review despite policy-accepted native/control variance." } else { "High image delta requires paired WPF/Avalonia visual review; equal dimensions do not resolve it." }
    [void]$md.AppendLine("| $(ConvertTo-ToolMarkdownCell $row.id) | $(Format-ReportNumber $row.comparison.triageScore) | $($row.comparison.logicalDimensionMatch) | $(ConvertTo-ToolMarkdownCell $bucket) | $($row.comparison.expectedSizeMismatch) | unresolved visual review candidate | $(ConvertTo-ToolMarkdownCell $reviewReason) |")
}
[void]$md.AppendLine()

[void]$md.AppendLine("## Scale-Aware Dimension Mismatch Classification")
[void]$md.AppendLine()
[void]$md.AppendLine("The $($dimensionMismatchRows.Count) scale-aware logical dimension mismatches are bucketed from committed PNG evidence and deterministic surface-id rules. Buckets describe the next review posture: align real layout sizes, fix content/state drift before comparing, accept policy-approved platform/native control differences, or refresh limited evidence. Policy-accepted native/control rows are explicit accepted variance, not incomplete parity work.")
[void]$md.AppendLine()
[void]$md.AppendLine("| Bucket | Count | Policy accepted | Top surface ids | Top next action |")
[void]$md.AppendLine("| --- | ---: | --- | --- | --- |")
foreach ($group in $dimensionMismatchBucketGroups) {
    $groupRows = @($group.Group | Sort-Object @{ Expression = { $_.comparison.triageScore }; Descending = $true }, @{ Expression = { $_.id }; Ascending = $true })
    $topIds = @($groupRows | Select-Object -First 5 | ForEach-Object { $_.id })
    $nextAction = if ($groupRows.Count -eq 0) { "" } else { [string]$groupRows[0].comparison.dimensionMismatchNextAction }
    $policyAccepted = @($groupRows | Where-Object {
            $null -ne $_.comparison.policyAcceptance -and
            $_.comparison.policyAcceptance.status -eq "policy-accepted"
        }).Count -eq $groupRows.Count
    [void]$md.AppendLine("| $(ConvertTo-ToolMarkdownCell ([string]$group.Name)) | $($group.Count) | $policyAccepted | $(ConvertTo-ToolMarkdownCell ($topIds -join '<br>')) | $(ConvertTo-ToolMarkdownCell $nextAction) |")
}
[void]$md.AppendLine()

if ($policyAcceptedNativeDifferenceGroups.Count -gt 0) {
    [void]$md.AppendLine("## Policy-Accepted Native/Control Differences")
    [void]$md.AppendLine()
    [void]$md.AppendLine("These rows were reviewed against the committed WPF/Avalonia PNG pairs and are retained as intentional platform/native control variance. They do not count as content/visual mismatches, evidence limitations, or real logical-size mismatches.")
    [void]$md.AppendLine()
    [void]$md.AppendLine("| Family | Count | Surface ids | Rationale | Clear criteria |")
    [void]$md.AppendLine("| --- | ---: | --- | --- | --- |")
    foreach ($group in $policyAcceptedNativeDifferenceGroups) {
        $groupRows = @($group.Group | Sort-Object -Property id)
        $acceptance = $groupRows[0].comparison.policyAcceptance
        $ids = @($groupRows | ForEach-Object { $_.id })
        [void]$md.AppendLine("| $(ConvertTo-ToolMarkdownCell ([string]$group.Name)) | $($group.Count) | $(ConvertTo-ToolMarkdownCell ($ids -join '<br>')) | $(ConvertTo-ToolMarkdownCell ([string]$acceptance.rationale)) | $(ConvertTo-ToolMarkdownCell ([string]$acceptance.clearCriteria)) |")
    }
    [void]$md.AppendLine()
}

if ($blankEvidenceRows.Count -gt 0) {
    [void]$md.AppendLine("Nonblank check failures: $((@($blankEvidenceRows | ForEach-Object { $_.id }) | Sort-Object) -join ', ').")
    [void]$md.AppendLine()
}

if ($wpfOnlyIds.Count -gt 0) {
    [void]$md.AppendLine("WPF manifest ids without an Avalonia pair: $($wpfOnlyIds -join ', ').")
    [void]$md.AppendLine()
}

[void]$md.AppendLine("## Top paired visual outliers")
[void]$md.AppendLine()
[void]$md.AppendLine("Outliers are ranked by a deterministic triage score: normalized 32x32 ARGB sample delta, mean-luma delta, non-background coverage delta, and DPI-normalized logical dimension delta. Higher scores deserve earlier human review. Rows with expected-size evidence mismatches are stale or suspect capture evidence, not an Avalonia product layout verdict.")
[void]$md.AppendLine()
[void]$md.AppendLine("| Surface id | WPF logical size | Avalonia logical size | Raw PNG sizes | Bucket | Evidence flag | Score | Sample delta | Luma delta | Non-bg delta |")
[void]$md.AppendLine("| --- | ---: | ---: | --- | --- | --- | ---: | ---: | ---: | ---: |")
foreach ($row in $topOutlierRows) {
    $evidenceFlag = if ($row.comparison.expectedSizeMismatch) { "Expected $($row.comparison.expectedWidth)x$($row.comparison.expectedHeight) via $($row.comparison.expectedSizeSource)" } else { "" }
    $rawSizes = "$(Format-PhysicalSize $row.wpf.metrics) vs $(Format-PhysicalSize $row.avalonia.metrics)"
    $bucket = if ([string]::IsNullOrWhiteSpace([string]$row.comparison.dimensionMismatchBucket)) { "" } else { [string]$row.comparison.dimensionMismatchBucket }
    [void]$md.AppendLine("| $(ConvertTo-ToolMarkdownCell $row.id) | $(Format-LogicalSize $row.wpf.metrics) | $(Format-LogicalSize $row.avalonia.metrics) | $(ConvertTo-ToolMarkdownCell $rawSizes) | $(ConvertTo-ToolMarkdownCell $bucket) | $(ConvertTo-ToolMarkdownCell $evidenceFlag) | $(Format-ReportNumber $row.comparison.triageScore) | $(Format-ReportNumber $row.comparison.sampleMeanDelta) | $(Format-ReportNumber $row.comparison.lumaDelta) | $(Format-ReportNumber $row.comparison.nonBackgroundDelta) |")
}

[void]$md.AppendLine()
[void]$md.AppendLine("## Scale-Aware Dimension Mismatch Details")
[void]$md.AppendLine()
[void]$md.AppendLine("| Surface id | Bucket | Policy family | WPF logical size | Avalonia logical size | Logical delta | Reason | Next action |")
[void]$md.AppendLine("| --- | --- | --- | ---: | ---: | ---: | --- | --- |")
foreach ($row in $dimensionMismatchRows) {
    $logicalDelta = "$(Format-DisplayNumber $row.comparison.logicalWidthDelta)x$(Format-DisplayNumber $row.comparison.logicalHeightDelta)"
    $policyFamily = if ($null -eq $row.comparison.policyAcceptance) { "" } else { [string]$row.comparison.policyAcceptance.family }
    [void]$md.AppendLine("| $(ConvertTo-ToolMarkdownCell $row.id) | $(ConvertTo-ToolMarkdownCell $row.comparison.dimensionMismatchBucket) | $(ConvertTo-ToolMarkdownCell $policyFamily) | $(Format-LogicalSize $row.wpf.metrics) | $(Format-LogicalSize $row.avalonia.metrics) | $logicalDelta | $(ConvertTo-ToolMarkdownCell $row.comparison.dimensionMismatchReason) | $(ConvertTo-ToolMarkdownCell $row.comparison.dimensionMismatchNextAction) |")
}

[void]$md.AppendLine()
[void]$md.AppendLine("## Expected-Size Evidence Mismatches")
[void]$md.AppendLine()
[void]$md.AppendLine("These rows have a DPI-normalized checked-in PNG size that disagrees with the dialog planner's expected capture size. Treat their paired dimension delta as stale or suspect evidence until that shell can be recaptured nonblank at the expected size.")
[void]$md.AppendLine()
[void]$md.AppendLine("| Surface id | Expected logical size | Source | WPF logical size | WPF raw PNG | WPF matches | Avalonia logical size | Avalonia raw PNG | Avalonia matches |")
[void]$md.AppendLine("| --- | ---: | --- | ---: | ---: | --- | ---: | ---: | --- |")
foreach ($row in $expectedSizeMismatchRows) {
    [void]$md.AppendLine("| $(ConvertTo-ToolMarkdownCell $row.id) | $($row.comparison.expectedWidth)x$($row.comparison.expectedHeight) | $(ConvertTo-ToolMarkdownCell $row.comparison.expectedSizeSource) | $(Format-LogicalSize $row.wpf.metrics) | $(ConvertTo-ToolMarkdownCell (Format-PhysicalSize $row.wpf.metrics)) | $($row.comparison.wpfExpectedSizeMatch) | $(Format-LogicalSize $row.avalonia.metrics) | $(ConvertTo-ToolMarkdownCell (Format-PhysicalSize $row.avalonia.metrics)) | $($row.comparison.avaloniaExpectedSizeMatch) |")
}

if ($stalePromotedExpectedSizeRows.Count -gt 0) {
    [void]$md.AppendLine()
    [void]$md.AppendLine("## Stale Promoted Expected-Size Evidence")
    [void]$md.AppendLine()
    [void]$md.AppendLine("These expected-size mismatches are known promoted fallback screenshots, not direct same-harness parity captures. Recapture or replace only with a nonblank WPF parity-capture PNG at the planner size; do not compare their dimension delta as product layout evidence.")
    [void]$md.AppendLine()
    [void]$md.AppendLine("| Surface id | Stale shell | Current PNG size | Expected size | Promoted source PNG | Recapture status | Next action |")
    [void]$md.AppendLine("| --- | --- | ---: | ---: | --- | --- | --- |")
    foreach ($row in $stalePromotedExpectedSizeRows) {
        $staleShell = if (-not $row.comparison.wpfExpectedSizeMatch) { "WPF" } elseif (-not $row.comparison.avaloniaExpectedSizeMatch) { "Avalonia" } else { "Both" }
        $staleEvidence = if ($staleShell -eq "Avalonia") { $row.avalonia } else { $row.wpf }
        $currentSize = if ($staleShell -eq "Avalonia") { "$(Format-LogicalSize $row.avalonia.metrics) logical ($(Format-PhysicalSize $row.avalonia.metrics))" } else { "$(Format-LogicalSize $row.wpf.metrics) logical ($(Format-PhysicalSize $row.wpf.metrics))" }
        $sourcePng = if ([string]::IsNullOrWhiteSpace([string]$staleEvidence.provenance.sourcePng)) { "" } else { [string]$staleEvidence.provenance.sourcePng }
        $recaptureStatus = if ([string]::IsNullOrWhiteSpace([string]$staleEvidence.provenance.recaptureStatus)) { "" } else { [string]$staleEvidence.provenance.recaptureStatus }
        $nextAction = Get-StalePromotedExpectedSizeNextAction -SurfaceId $row.id -RecaptureStatus $recaptureStatus
        [void]$md.AppendLine("| $(ConvertTo-ToolMarkdownCell $row.id) | $staleShell | $currentSize | $($row.comparison.expectedWidth)x$($row.comparison.expectedHeight) | $(ConvertTo-ToolMarkdownCell $sourcePng) | $(ConvertTo-ToolMarkdownCell $recaptureStatus) | $(ConvertTo-ToolMarkdownCell $nextAction) |")
    }
}

[void]$md.AppendLine()
[void]$md.AppendLine("## Paired manifest surfaces")
[void]$md.AppendLine()
[void]$md.AppendLine("| Surface id | WPF PNG | WPF logical size | WPF raw PNG | WPF nonblank | Avalonia PNG | Avalonia logical size | Avalonia raw PNG | Avalonia nonblank | Dimension match | Score |")
[void]$md.AppendLine("| --- | --- | ---: | ---: | --- | --- | ---: | ---: | --- | --- | ---: |")
foreach ($row in $pairedRows) {
    [void]$md.AppendLine("| $(ConvertTo-ToolMarkdownCell $row.id) | $(ConvertTo-ToolMarkdownCell $row.wpf.png) | $(Format-LogicalSize $row.wpf.metrics) | $(ConvertTo-ToolMarkdownCell (Format-PhysicalSize $row.wpf.metrics)) | $($row.wpf.metrics.IsNonBlank) | $(ConvertTo-ToolMarkdownCell $row.avalonia.png) | $(Format-LogicalSize $row.avalonia.metrics) | $(ConvertTo-ToolMarkdownCell (Format-PhysicalSize $row.avalonia.metrics)) | $($row.avalonia.metrics.IsNonBlank) | $($row.comparison.dimensionMatch) | $(Format-ReportNumber $row.comparison.triageScore) |")
}

[void]$md.AppendLine()
[void]$md.AppendLine("## Avalonia-Manifest-Only Screenshot Surfaces")
[void]$md.AppendLine()
[void]$md.AppendLine("Avalonia has $($avaloniaOnlyIds.Count) committed PNG manifest surface ids across $($additionalGroups.Count) dialog route families with no exact WPF manifest PNG id pair. This describes screenshot evidence coverage, not whether a WPF dialog implementation exists.")
[void]$md.AppendLine()
[void]$md.AppendLine("| Route family | Count | Additional surface ids |")
[void]$md.AppendLine("| --- | ---: | --- |")
foreach ($group in $additionalGroups) {
    $ids = @($group.Group | Sort-Object -Property id | ForEach-Object { $_.id })
    [void]$md.AppendLine("| $(ConvertTo-ToolMarkdownCell $group.Name) | $($group.Count) | $(ConvertTo-ToolMarkdownCell ($ids -join '<br>')) |")
}

[void]$md.AppendLine()
[void]$md.AppendLine("## Avalonia-Manifest-Only PNG Checks")
[void]$md.AppendLine()
[void]$md.AppendLine("| Surface id | PNG | Size | Nonblank | Distinct colors | Non-bg ratio |")
[void]$md.AppendLine("| --- | --- | ---: | --- | ---: | ---: |")
foreach ($row in $avaloniaOnlyRows) {
    [void]$md.AppendLine("| $(ConvertTo-ToolMarkdownCell $row.id) | $(ConvertTo-ToolMarkdownCell $row.avalonia.png) | $($row.avalonia.metrics.Width)x$($row.avalonia.metrics.Height) | $($row.avalonia.metrics.IsNonBlank) | $($row.avalonia.metrics.DistinctColors) | $(Format-ReportNumber $row.avalonia.metrics.NonBackgroundRatio) |")
}

$markdown = $md.ToString()

if ($Check) {
    Test-ToolGeneratedContentMatches -ExpectedContent $markdown -ActualPath $resolvedMarkdownPath -Label "Dialog visual evidence summary Markdown" -GeneratorScriptName "tools\Generate-DialogVisualEvidenceSummary.ps1" -NormalizeNewlines
    Test-ToolGeneratedContentMatches -ExpectedContent $json -ActualPath $resolvedJsonPath -Label "Dialog visual evidence summary JSON" -GeneratorScriptName "tools\Generate-DialogVisualEvidenceSummary.ps1" -NormalizeNewlines

    Write-Host "Dialog visual evidence summary is up to date."
    return
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedMarkdownPath) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedJsonPath) | Out-Null
Set-Content -LiteralPath $resolvedMarkdownPath -Value $markdown -Encoding utf8 -NoNewline
Set-Content -LiteralPath $resolvedJsonPath -Value $json -Encoding utf8 -NoNewline

Write-Host "WPF captured manifest surfaces with committed PNGs: $($wpfSurfaces.Count)"
Write-Host "Avalonia captured manifest surfaces with committed PNGs: $($avaloniaSurfaces.Count)"
Write-Host "Paired captured surface ids: $($pairedIds.Count)"
Write-Host "WPF-only manifest surface ids: $($wpfOnlyIds.Count)"
Write-Host "Avalonia-manifest-only screenshot surface ids needing WPF manifest pair: $($avaloniaOnlyIds.Count)"
Write-Host "Nonblank PNG check failures: $($blankEvidenceRows.Count)"
Write-Host "Paired dimension mismatches: $($dimensionMismatchRows.Count)"
Write-Host "Raw PNG pixel dimension mismatches: $($rawPixelDimensionMismatchRows.Count)"
Write-Host "Raw PNG mismatches normalized by capture DPI: $($captureScaleNormalizedDimensionRows.Count)"
Write-Host "Paired expected-size evidence mismatches: $($expectedSizeMismatchRows.Count)"
Write-Host "Stale promoted expected-size evidence: $($stalePromotedExpectedSizeRows.Count)"
Write-Host "Policy-accepted native/control differences: $($policyAcceptedNativeDifferenceRows.Count)"
foreach ($group in $dimensionMismatchBucketGroups) {
    Write-Host "Dimension mismatch bucket '$($group.Name)': $($group.Count)"
}
Write-Host "Wrote $(ConvertTo-ToolRepoRelativePath -Path $resolvedMarkdownPath -RepoRoot $repoRoot)"
Write-Host "Wrote $(ConvertTo-ToolRepoRelativePath -Path $resolvedJsonPath -RepoRoot $repoRoot)"
