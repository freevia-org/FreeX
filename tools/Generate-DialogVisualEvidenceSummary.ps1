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

Add-Type -ReferencedAssemblies "System.Drawing.dll" -TypeDefinition @"
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

                var distinctColors = new HashSet<int>();
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

                        distinctColors.Add(argb);
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

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function ConvertTo-RepoRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd('\')
    if ($fullPath.StartsWith($fullRoot + "\", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($fullRoot.Length + 1).Replace('/', '\')
    }

    return $fullPath.Replace('/', '\')
}

function Escape-MarkdownCell {
    param([string]$Value)

    if ($null -eq $Value -or $Value.Length -eq 0) {
        return ""
    }

    $Value.Replace('|', '\|')
}

function Format-ReportNumber {
    param(
        [Parameter(Mandatory = $true)][double]$Value,
        [string]$Format = "0.000"
    )

    $Value.ToString($Format, $invariantCulture)
}

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolvedPath = Resolve-RepoPath $Path
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "Required dialog evidence input was not found: $resolvedPath"
    }

    Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json
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

    Join-Path (Split-Path -Parent (Resolve-RepoPath $ManifestPath)) $png
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
    param([Parameter(Mandatory = $true)][string]$Path)

    [DialogPngAnalyzer]::Analyze($Path)
}

function ConvertTo-JsonMetric {
    param([Parameter(Mandatory = $true)]$Metric)

    [ordered]@{
        relativePath = ConvertTo-RepoRelativePath $Metric.Path
        fileBytes = $Metric.FileBytes
        width = $Metric.Width
        height = $Metric.Height
        pixels = $Metric.Pixels
        distinctColors = $Metric.DistinctColors
        opaqueRatio = [math]::Round($Metric.OpaqueRatio, 6)
        nonBackgroundRatio = [math]::Round($Metric.NonBackgroundRatio, 6)
        isNonBlank = $Metric.IsNonBlank
        meanLuma = [math]::Round($Metric.MeanLuma, 6)
    }
}

function Compare-PngMetrics {
    param(
        [Parameter(Mandatory = $true)]$WpfMetric,
        [Parameter(Mandatory = $true)]$AvaloniaMetric
    )

    $sampleMeanDelta = [DialogPngAnalyzer]::SignatureDelta($WpfMetric.Signature, $AvaloniaMetric.Signature)
    $widthDelta = [math]::Abs([int]$WpfMetric.Width - [int]$AvaloniaMetric.Width)
    $heightDelta = [math]::Abs([int]$WpfMetric.Height - [int]$AvaloniaMetric.Height)
    $dimensionDeltaRatio = if ($WpfMetric.Width -le 0 -or $WpfMetric.Height -le 0) {
        1.0
    }
    else {
        ([math]::Abs([double]$AvaloniaMetric.Width - [double]$WpfMetric.Width) / [double]$WpfMetric.Width) +
            ([math]::Abs([double]$AvaloniaMetric.Height - [double]$WpfMetric.Height) / [double]$WpfMetric.Height)
    }

    $lumaDelta = [math]::Abs([double]$WpfMetric.MeanLuma - [double]$AvaloniaMetric.MeanLuma) / 255.0
    $nonBackgroundDelta = [math]::Abs([double]$WpfMetric.NonBackgroundRatio - [double]$AvaloniaMetric.NonBackgroundRatio)
    $triageScore = $sampleMeanDelta + $lumaDelta + $nonBackgroundDelta + [math]::Min($dimensionDeltaRatio, 2.0)

    [pscustomobject]@{
        widthDelta = [int]$widthDelta
        heightDelta = [int]$heightDelta
        dimensionMatch = ($widthDelta -eq 0 -and $heightDelta -eq 0)
        dimensionDeltaRatio = $dimensionDeltaRatio
        sampleMeanDelta = $sampleMeanDelta
        lumaDelta = $lumaDelta
        nonBackgroundDelta = $nonBackgroundDelta
        triageScore = $triageScore
    }
}

function New-PngEvidence {
    param(
        [Parameter(Mandatory = $true)]$Surface,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )

    $pngPath = Resolve-ManifestPngPath -Surface $Surface -ManifestPath $ManifestPath
    if ([string]::IsNullOrWhiteSpace($pngPath)) {
        throw "Captured surface '$($Surface.id)' does not declare a PNG path."
    }

    $metrics = Get-PngMetrics $pngPath

    [pscustomobject]@{
        id = [string]$Surface.id
        png = [string]$Surface.png
        note = [string]$Surface.note
        metrics = $metrics
    }
}

$inventory = Read-JsonFile $InventoryPath
$wpfManifest = Read-JsonFile $WpfManifestPath
$avaloniaManifest = Read-JsonFile $AvaloniaManifestPath

$inventoryRows = @($inventory.rows)
$routeIds = @($inventoryRows | ForEach-Object { [string]$_.routeId })

$wpfSurfaces = @(Get-CapturedSurfaces -Manifest $wpfManifest -ManifestPath $WpfManifestPath -RequirePng)
$avaloniaSurfaces = @(Get-CapturedSurfaces -Manifest $avaloniaManifest -ManifestPath $AvaloniaManifestPath -RequirePng)

$wpfById = [ordered]@{}
foreach ($surface in $wpfSurfaces) {
    $wpfById[[string]$surface.id] = New-PngEvidence -Surface $surface -ManifestPath $WpfManifestPath
}

$avaloniaById = [ordered]@{}
foreach ($surface in $avaloniaSurfaces) {
    $avaloniaById[[string]$surface.id] = New-PngEvidence -Surface $surface -ManifestPath $AvaloniaManifestPath
}

$pairedIds = @($wpfById.Keys | Where-Object { $avaloniaById.Contains($_) } | Sort-Object)
$wpfOnlyIds = @($wpfById.Keys | Where-Object { -not $avaloniaById.Contains($_) } | Sort-Object)
$avaloniaOnlyIds = @($avaloniaById.Keys | Where-Object { -not $wpfById.Contains($_) } | Sort-Object)

$pairedRows = @(
    foreach ($id in $pairedIds) {
        $wpfEvidence = $wpfById[$id]
        $avaloniaEvidence = $avaloniaById[$id]
        $comparison = Compare-PngMetrics -WpfMetric $wpfEvidence.metrics -AvaloniaMetric $avaloniaEvidence.metrics

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
$topOutlierRows = @($pairedRows | Sort-Object @{ Expression = { $_.comparison.triageScore }; Descending = $true }, @{ Expression = { $_.id }; Ascending = $true } | Select-Object -First 10)

$wpfManifestRelativePath = ConvertTo-RepoRelativePath (Resolve-RepoPath $WpfManifestPath)
$avaloniaManifestRelativePath = ConvertTo-RepoRelativePath (Resolve-RepoPath $AvaloniaManifestPath)
$inventoryRelativePath = ConvertTo-RepoRelativePath (Resolve-RepoPath $InventoryPath)
$resolvedMarkdownPath = Resolve-RepoPath $MarkdownPath
$resolvedJsonPath = Resolve-RepoPath $JsonPath
$jsonRelativePath = ConvertTo-RepoRelativePath $resolvedJsonPath

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
    }
    pairedSurfaces = @(
        foreach ($row in $pairedRows) {
            [ordered]@{
                id = $row.id
                routeFamily = $row.routeFamily
                wpf = ConvertTo-JsonMetric $row.wpf.metrics
                avalonia = ConvertTo-JsonMetric $row.avalonia.metrics
                comparison = [ordered]@{
                    dimensionMatch = $row.comparison.dimensionMatch
                    widthDelta = $row.comparison.widthDelta
                    heightDelta = $row.comparison.heightDelta
                    dimensionDeltaRatio = [math]::Round($row.comparison.dimensionDeltaRatio, 6)
                    sampleMeanDelta = [math]::Round($row.comparison.sampleMeanDelta, 6)
                    lumaDelta = [math]::Round($row.comparison.lumaDelta, 6)
                    nonBackgroundDelta = [math]::Round($row.comparison.nonBackgroundDelta, 6)
                    triageScore = [math]::Round($row.comparison.triageScore, 6)
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
                dimension = "$($row.wpf.metrics.Width)x$($row.wpf.metrics.Height) vs $($row.avalonia.metrics.Width)x$($row.avalonia.metrics.Height)"
                nonBackgroundDelta = [math]::Round($row.comparison.nonBackgroundDelta, 6)
            }
        }
    )
    additionalAvaloniaSurfacesNeedingWpfPair = @(
        foreach ($row in $avaloniaOnlyRows) {
            [ordered]@{
                id = $row.id
                routeFamily = $row.routeFamily
                avalonia = ConvertTo-JsonMetric $row.avalonia.metrics
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

$json = ($jsonModel | ConvertTo-Json -Depth 12) + [Environment]::NewLine

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Dialog visual evidence summary")
[void]$md.AppendLine()
[void]$md.AppendLine("Generated by tools/Generate-DialogVisualEvidenceSummary.ps1 from committed dialog capture manifests, PNG evidence, and the generated dialog parity inventory.")
[void]$md.AppendLine()
[void]$md.AppendLine("This deterministic triage report compares checked-in WPF and Avalonia capture PNGs. It is not a full visual-parity claim; it flags stale or suspect evidence, ranks paired screenshot outliers by simple image metrics, and names Avalonia-only surfaces that still need WPF pairing.")
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
[void]$md.AppendLine("| Additional Avalonia captured surface ids needing WPF pair | $($avaloniaOnlyIds.Count) |")
[void]$md.AppendLine("| Nonblank PNG check failures | $($blankEvidenceRows.Count) |")
[void]$md.AppendLine("| Paired dimension mismatches | $($dimensionMismatchRows.Count) |")
[void]$md.AppendLine()

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
[void]$md.AppendLine("Outliers are ranked by a deterministic triage score: normalized 32x32 ARGB sample delta, mean-luma delta, non-background coverage delta, and normalized dimension delta. Higher scores deserve earlier human review.")
[void]$md.AppendLine()
[void]$md.AppendLine("| Surface id | WPF size | Avalonia size | Score | Sample delta | Luma delta | Non-bg delta |")
[void]$md.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |")
foreach ($row in $topOutlierRows) {
    [void]$md.AppendLine("| $(Escape-MarkdownCell $row.id) | $($row.wpf.metrics.Width)x$($row.wpf.metrics.Height) | $($row.avalonia.metrics.Width)x$($row.avalonia.metrics.Height) | $(Format-ReportNumber $row.comparison.triageScore) | $(Format-ReportNumber $row.comparison.sampleMeanDelta) | $(Format-ReportNumber $row.comparison.lumaDelta) | $(Format-ReportNumber $row.comparison.nonBackgroundDelta) |")
}

[void]$md.AppendLine()
[void]$md.AppendLine("## Paired manifest surfaces")
[void]$md.AppendLine()
[void]$md.AppendLine("| Surface id | WPF PNG | WPF size | WPF nonblank | Avalonia PNG | Avalonia size | Avalonia nonblank | Score |")
[void]$md.AppendLine("| --- | --- | ---: | --- | --- | ---: | --- | ---: |")
foreach ($row in $pairedRows) {
    [void]$md.AppendLine("| $(Escape-MarkdownCell $row.id) | $(Escape-MarkdownCell $row.wpf.png) | $($row.wpf.metrics.Width)x$($row.wpf.metrics.Height) | $($row.wpf.metrics.IsNonBlank) | $(Escape-MarkdownCell $row.avalonia.png) | $($row.avalonia.metrics.Width)x$($row.avalonia.metrics.Height) | $($row.avalonia.metrics.IsNonBlank) | $(Format-ReportNumber $row.comparison.triageScore) |")
}

[void]$md.AppendLine()
[void]$md.AppendLine("## Additional Avalonia manifest surfaces needing WPF pair")
[void]$md.AppendLine()
[void]$md.AppendLine("Avalonia has $($avaloniaOnlyIds.Count) additional captured manifest surface ids across $($additionalGroups.Count) dialog route families. These are committed Avalonia PNGs with no matching WPF manifest surface id.")
[void]$md.AppendLine()
[void]$md.AppendLine("| Route family | Count | Additional surface ids |")
[void]$md.AppendLine("| --- | ---: | --- |")
foreach ($group in $additionalGroups) {
    $ids = @($group.Group | Sort-Object -Property id | ForEach-Object { $_.id })
    [void]$md.AppendLine("| $(Escape-MarkdownCell $group.Name) | $($group.Count) | $(Escape-MarkdownCell ($ids -join '<br>')) |")
}

[void]$md.AppendLine()
[void]$md.AppendLine("## Additional Avalonia PNG checks")
[void]$md.AppendLine()
[void]$md.AppendLine("| Surface id | PNG | Size | Nonblank | Distinct colors | Non-bg ratio |")
[void]$md.AppendLine("| --- | --- | ---: | --- | ---: | ---: |")
foreach ($row in $avaloniaOnlyRows) {
    [void]$md.AppendLine("| $(Escape-MarkdownCell $row.id) | $(Escape-MarkdownCell $row.avalonia.png) | $($row.avalonia.metrics.Width)x$($row.avalonia.metrics.Height) | $($row.avalonia.metrics.IsNonBlank) | $($row.avalonia.metrics.DistinctColors) | $(Format-ReportNumber $row.avalonia.metrics.NonBackgroundRatio) |")
}

$markdown = $md.ToString()

if ($Check) {
    $existingMarkdown = if (Test-Path -LiteralPath $resolvedMarkdownPath -PathType Leaf) { Get-Content -LiteralPath $resolvedMarkdownPath -Raw } else { "" }
    $existingJson = if (Test-Path -LiteralPath $resolvedJsonPath -PathType Leaf) { Get-Content -LiteralPath $resolvedJsonPath -Raw } else { "" }

    if ($existingMarkdown -ne $markdown -or $existingJson -ne $json) {
        throw "Dialog visual evidence summary is out of date. Run tools\Generate-DialogVisualEvidenceSummary.ps1 to refresh it."
    }

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
Write-Host "Additional Avalonia captured surface ids needing WPF pair: $($avaloniaOnlyIds.Count)"
Write-Host "Nonblank PNG check failures: $($blankEvidenceRows.Count)"
Write-Host "Paired dimension mismatches: $($dimensionMismatchRows.Count)"
Write-Host "Wrote $(ConvertTo-RepoRelativePath $resolvedMarkdownPath)"
Write-Host "Wrote $(ConvertTo-RepoRelativePath $resolvedJsonPath)"
