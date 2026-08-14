using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using FreeP.App.Compositor;
using FreeP.VisualEvidence;

namespace FreeP.RenderCompare;

internal sealed record DialogPaneVisualEvidenceSummary(
    int SchemaVersion,
    string GeneratedAtUtc,
    int ScenarioCount,
    int RouteCount,
    int PairedCaptureCount,
    int PassCount,
    int MismatchCount,
    int LimitationCount,
    DialogPaneVisualEvidenceHostManifest Wpf,
    DialogPaneVisualEvidenceHostManifest Avalonia,
    IReadOnlyList<DialogPaneVisualEvidenceComparison> Comparisons,
    string NativePickerManifestPath,
    IReadOnlyList<string> EnvironmentNotes,
    IReadOnlyList<string> Limitations);

internal static class DialogPaneVisualEvidence
{
    private const double MaximumChangedPixelRatio = 0.20;
    // Cross-framework glyph and control rasterization makes foreground-union deltas diagnostic;
    // whole-target changed-pixel ratio and mean channel delta are the visual acceptance gates.
    private const double MaximumForegroundChangedPixelRatio = 1.0;
    private const double MaximumMeanChannelDelta = 18.0;

    internal static int Run(string outputDirectory, string wpfExecutable, string avaloniaExecutable, TimeSpan timeout)
    {
        var collection = PairedVisualEvidenceCollector.Collect(
            outputDirectory,
            wpfExecutable,
            avaloniaExecutable,
            timeout,
            DialogPaneCollectorProfile);
        var summary = BuildSummary(
            collection.Wpf,
            collection.Avalonia,
            collection.Limitations,
            collection.OutputDirectory);
        WriteReports(collection.OutputDirectory, summary);

        Console.WriteLine($"Paired captures: {summary.PairedCaptureCount}/{summary.ScenarioCount}");
        Console.WriteLine($"Pass: {summary.PassCount}; mismatch: {summary.MismatchCount}; limitation: {summary.LimitationCount}");
        Console.WriteLine($"Summary: {Path.Combine(collection.OutputDirectory, "summary.json")}");
        return 0;
    }

    internal static int RegenerateReports(string outputDirectory)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        var wpf = ReadHostManifest(outputDirectory, FreePVisualEvidenceCaptureOrchestration.WpfHost);
        var avalonia = ReadHostManifest(outputDirectory, FreePVisualEvidenceCaptureOrchestration.AvaloniaHost);
        var summary = BuildSummary(wpf, avalonia, evidenceRoot: outputDirectory);
        WriteReports(outputDirectory, summary);
        Console.WriteLine($"Regenerated paired evidence reports: {summary.PassCount} pass, {summary.MismatchCount} mismatch, {summary.LimitationCount} limitation.");
        return 0;
    }

    private static DialogPaneVisualEvidenceHostManifest ReadHostManifest(string outputDirectory, string host)
        => PairedVisualEvidenceCollector.ReadHostManifest<DialogPaneVisualEvidenceHostManifest>(
            outputDirectory,
            host,
            FreePVisualEvidenceRoutes.DialogPane,
            $"{host} evidence manifest was not found.",
            $"{host} evidence manifest could not be read.");

    internal static DialogPaneVisualEvidenceSummary BuildSummary(
        DialogPaneVisualEvidenceHostManifest wpf,
        DialogPaneVisualEvidenceHostManifest avalonia,
        IReadOnlyList<string>? runnerLimitations = null,
        string? evidenceRoot = null)
    {
        var wpfByScenario = wpf.Captures.ToDictionary(capture => capture.ScenarioId, StringComparer.Ordinal);
        var avaloniaByScenario = avalonia.Captures.ToDictionary(capture => capture.ScenarioId, StringComparer.Ordinal);
        var comparisons = DialogPaneVisualEvidenceCatalog.All
            .Select(scenario => AddPixelComparison(
                DialogPaneVisualEvidenceComparer.Compare(
                    scenario,
                    wpfByScenario.GetValueOrDefault(scenario.Id),
                    avaloniaByScenario.GetValueOrDefault(scenario.Id)),
                wpfByScenario.GetValueOrDefault(scenario.Id),
                avaloniaByScenario.GetValueOrDefault(scenario.Id),
                evidenceRoot))
            .ToArray();
        var limitations = wpf.Limitations
            .Concat(avalonia.Limitations)
            .Concat(runnerLimitations ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new DialogPaneVisualEvidenceSummary(
            3,
            DateTimeOffset.UtcNow.ToString("O"),
            DialogPaneVisualEvidenceCatalog.All.Count,
            DialogPaneVisualEvidenceCatalog.All.Select(scenario => scenario.RouteId).Distinct(StringComparer.Ordinal).Count(),
            comparisons.Count(comparison =>
                !string.IsNullOrWhiteSpace(comparison.WpfImagePath) &&
                !string.IsNullOrWhiteSpace(comparison.AvaloniaImagePath)),
            comparisons.Count(comparison => comparison.Classification == DialogPaneVisualEvidenceClassification.Pass),
            comparisons.Count(comparison => comparison.Classification == DialogPaneVisualEvidenceClassification.Mismatch),
            comparisons.Count(comparison => comparison.Classification == DialogPaneVisualEvidenceClassification.Limitation),
            wpf,
            avalonia,
            comparisons,
            "../freep-native-picker-human-evidence.json",
            BuildEnvironmentNotes(wpf, avalonia),
            limitations);
    }

    private static IReadOnlyList<string> BuildEnvironmentNotes(
        DialogPaneVisualEvidenceHostManifest wpf,
        DialogPaneVisualEvidenceHostManifest avalonia)
    {
        var sourceDpi = wpf.Captures.FirstOrDefault();
        return
        [
            sourceDpi is null
                ? "WPF source desktop DPI was unavailable."
                : $"WPF source desktop DPI was {sourceDpi.SourceDpiX:0.##}x{sourceDpi.SourceDpiY:0.##}; app-owned rasters were normalized to logical 96 DPI before comparison.",
            "Captures include app-owned client content only; native non-client title bars are outside the paired pixel gate.",
            $"WPF mode: {wpf.CaptureMode}; Avalonia mode: {avalonia.CaptureMode}.",
        ];
    }

    private static DialogPaneVisualEvidenceComparison AddPixelComparison(
        DialogPaneVisualEvidenceComparison semantic,
        DialogPaneVisualEvidenceCapture? wpf,
        DialogPaneVisualEvidenceCapture? avalonia,
        string? evidenceRoot)
    {
        if (evidenceRoot is null || wpf is null || avalonia is null ||
            string.IsNullOrWhiteSpace(wpf.ImagePath) || string.IsNullOrWhiteSpace(avalonia.ImagePath))
            return semantic;

        var wpfComparisonImage = string.IsNullOrWhiteSpace(wpf.PixelComparisonImagePath)
            ? wpf.ImagePath
            : wpf.PixelComparisonImagePath;
        var avaloniaComparisonImage = string.IsNullOrWhiteSpace(avalonia.PixelComparisonImagePath)
            ? avalonia.ImagePath
            : avalonia.PixelComparisonImagePath;
        var wpfComparisonWidth = wpf.PixelComparisonLogicalWidth > 0 ? wpf.PixelComparisonLogicalWidth : wpf.LogicalWidth;
        var wpfComparisonHeight = wpf.PixelComparisonLogicalHeight > 0 ? wpf.PixelComparisonLogicalHeight : wpf.LogicalHeight;
        var avaloniaComparisonWidth = avalonia.PixelComparisonLogicalWidth > 0 ? avalonia.PixelComparisonLogicalWidth : avalonia.LogicalWidth;
        var avaloniaComparisonHeight = avalonia.PixelComparisonLogicalHeight > 0 ? avalonia.PixelComparisonLogicalHeight : avalonia.LogicalHeight;
        var metrics = ComputePixelMetrics(
            evidenceRoot,
            wpfComparisonImage,
            avaloniaComparisonImage,
            wpfComparisonWidth,
            wpfComparisonHeight,
            avaloniaComparisonWidth,
            avaloniaComparisonHeight,
            $"diff/{semantic.ScenarioId}.png");
        if (metrics is null)
        {
            return semantic with
            {
                Classification = semantic.Classification == DialogPaneVisualEvidenceClassification.Mismatch
                    ? semantic.Classification
                    : DialogPaneVisualEvidenceClassification.Limitation,
                Details = semantic.Details.Concat(["Pixel comparison was unavailable because one or both PNGs were missing."]).ToArray(),
            };
        }

        var shellContextMetrics = StringComparer.Ordinal.Equals(wpfComparisonImage, wpf.ImagePath) &&
            StringComparer.Ordinal.Equals(avaloniaComparisonImage, avalonia.ImagePath)
            ? null
            : ComputePixelMetrics(
                evidenceRoot,
                wpf.ImagePath,
                avalonia.ImagePath,
                wpf.LogicalWidth,
                wpf.LogicalHeight,
                avalonia.LogicalWidth,
                avalonia.LogicalHeight,
                $"diff/context/{semantic.ScenarioId}.png");
        if (metrics.ThresholdPassed)
            return semantic with { PixelMetrics = metrics, ShellContextPixelMetrics = shellContextMetrics };

        var visualDetail = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Normalized target pixel threshold failed: changed {metrics.ChangedPixelRatio:P2} (max {MaximumChangedPixelRatio:P0}), foreground changed {metrics.ForegroundChangedPixelRatio:P2} (max {MaximumForegroundChangedPixelRatio:P0}), mean channel delta {metrics.MeanChannelDelta:F2} (max {MaximumMeanChannelDelta:F2}), source pixels WPF {metrics.WpfPixelWidth}x{metrics.WpfPixelHeight}, Avalonia {metrics.AvaloniaPixelWidth}x{metrics.AvaloniaPixelHeight}.");
        return semantic with
        {
            Classification = DialogPaneVisualEvidenceClassification.Mismatch,
            Details = semantic.Details.Concat([visualDetail]).ToArray(),
            PixelMetrics = metrics,
            ShellContextPixelMetrics = shellContextMetrics,
        };
    }

    private static DialogPaneVisualEvidencePixelMetrics? ComputePixelMetrics(
        string evidenceRoot,
        string wpfImagePath,
        string avaloniaImagePath,
        double wpfLogicalWidth,
        double wpfLogicalHeight,
        double avaloniaLogicalWidth,
        double avaloniaLogicalHeight,
        string heatmapRelativePath)
    {
        var wpfPath = Path.Combine(evidenceRoot, wpfImagePath.Replace('/', Path.DirectorySeparatorChar));
        var avaloniaPath = Path.Combine(evidenceRoot, avaloniaImagePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(wpfPath) || !File.Exists(avaloniaPath))
            return null;

        var normalizedWidth = Math.Max(1, (int)Math.Ceiling(Math.Max(wpfLogicalWidth, avaloniaLogicalWidth)));
        var normalizedHeight = Math.Max(1, (int)Math.Ceiling(Math.Max(wpfLogicalHeight, avaloniaLogicalHeight)));
        var heatmapPath = Path.Combine(evidenceRoot, heatmapRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var diff = ImageDiff.CompareNormalized(
            wpfPath,
            avaloniaPath,
            normalizedWidth,
            normalizedHeight,
            heatmapPath);
        var pixelDimensionsMatch = diff.WidthA == diff.WidthB && diff.HeightA == diff.HeightB;
        var thresholdPassed = pixelDimensionsMatch &&
            diff.ChangedPixelRatio <= MaximumChangedPixelRatio &&
            diff.ForegroundChangedPixelRatio <= MaximumForegroundChangedPixelRatio &&
            diff.MeanChannelDelta <= MaximumMeanChannelDelta;
        return new DialogPaneVisualEvidencePixelMetrics(
            diff.WidthA,
            diff.HeightA,
            diff.WidthB,
            diff.HeightB,
            diff.NormalizedWidth,
            diff.NormalizedHeight,
            diff.ComparedPixelCount,
            diff.ForegroundUnionPixelCount,
            diff.ChangedPixelCount,
            diff.ForegroundChangedPixelCount,
            diff.ChangedPixelRatio,
            diff.ForegroundChangedPixelRatio,
            diff.MeanChannelDelta,
            diff.MaxChannelDelta,
            diff.ChangedChannelThreshold,
            MaximumChangedPixelRatio,
            MaximumForegroundChangedPixelRatio,
            MaximumMeanChannelDelta,
            pixelDimensionsMatch,
            thresholdPassed,
            diff.BackgroundHandling,
            heatmapRelativePath,
            VisualEvidenceToolSupport.Sha256(wpfPath),
            VisualEvidenceToolSupport.Sha256(avaloniaPath),
            VisualEvidenceToolSupport.Sha256(heatmapPath));
    }

    internal static void WriteReports(string outputDirectory, DialogPaneVisualEvidenceSummary summary)
    {
        Directory.CreateDirectory(outputDirectory);
        FreePVisualEvidenceCaptureOrchestration.WriteManifest(
            Path.Combine(outputDirectory, "summary.json"),
            summary,
            FreePVisualEvidenceCaptureOrchestration.ToolManifestJsonOptions);
        File.WriteAllText(Path.Combine(outputDirectory, "report.md"), BuildMarkdown(summary));
        File.WriteAllText(Path.Combine(outputDirectory, "report.html"), BuildHtml(summary));
    }

    internal static string BuildMarkdown(DialogPaneVisualEvidenceSummary summary)
    {
        var captures = summary.Wpf.Captures.Concat(summary.Avalonia.Captures)
            .ToDictionary(capture => (capture.Host, capture.ScenarioId));
        var builder = new StringBuilder();
        builder.AppendLine("# FreeP Dialog/Pane Paired Visual Evidence");
        builder.AppendLine();
        builder.AppendLine($"Generated `{summary.GeneratedAtUtc}` from real app-owned WPF and Avalonia render targets. Semantic route coverage is not treated as visual parity.");
        builder.AppendLine();
        builder.AppendLine($"- Scenarios: {summary.ScenarioCount}");
        builder.AppendLine($"- Paired captures: {summary.PairedCaptureCount}");
        builder.AppendLine($"- Pass: {summary.PassCount}");
        builder.AppendLine($"- Mismatch: {summary.MismatchCount}");
        builder.AppendLine($"- Limitation: {summary.LimitationCount}");
        builder.AppendLine("- Native Open/Save As: human evidence only; no cross-picker pixel equality assertion.");
        foreach (var note in summary.EnvironmentNotes)
            builder.AppendLine($"- Environment: {note}");
        builder.AppendLine();
        builder.AppendLine("| Scenario | Classification | WPF dimensions | Avalonia dimensions | Nonblank | Focus | Buttons | Enabled state | Target pixel metrics | Shell-context metrics | Paired images |");
        builder.AppendLine("|---|---|---:|---:|---|---|---|---|---|---|---|");
        foreach (var comparison in summary.Comparisons)
        {
            captures.TryGetValue(("wpf", comparison.ScenarioId), out var wpf);
            captures.TryGetValue(("avalonia", comparison.ScenarioId), out var avalonia);
            builder.Append("| ").Append(comparison.ScenarioId)
                .Append(" | ").Append(comparison.Classification.ToString().ToLowerInvariant())
                .Append(" | ").Append(Dimensions(wpf))
                .Append(" | ").Append(Dimensions(avalonia))
                .Append(" | ").Append(Check(comparison.WpfNonblank && comparison.AvaloniaNonblank))
                .Append(" | ").Append(Check(comparison.FocusMatches))
                .Append(" | ").Append(Check(comparison.ButtonOrderMatches))
                .Append(" | ").Append(Check(comparison.EnabledStateMatches))
                .Append(" | ").Append(PixelMetrics(comparison.PixelMetrics))
                .Append(" | ").Append(PixelMetrics(comparison.ShellContextPixelMetrics))
                .Append(" | ").Append(ImageLinks(comparison, wpf, avalonia))
                .AppendLine(" |");
            foreach (var detail in comparison.Details)
                builder.AppendLine($"|  | Detail |  |  |  |  |  |  |  |  | {detail.Replace("|", "\\|")} |");
        }
        if (summary.Limitations.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Limitations");
            builder.AppendLine();
            foreach (var limitation in summary.Limitations)
                builder.AppendLine($"- {limitation}");
        }
        return builder.ToString();
    }

    internal static string BuildHtml(DialogPaneVisualEvidenceSummary summary)
    {
        var captures = summary.Wpf.Captures.Concat(summary.Avalonia.Captures)
            .ToDictionary(capture => (capture.Host, capture.ScenarioId));
        var rows = new StringBuilder();
        foreach (var comparison in summary.Comparisons)
        {
            captures.TryGetValue(("wpf", comparison.ScenarioId), out var wpf);
            captures.TryGetValue(("avalonia", comparison.ScenarioId), out var avalonia);
            var details = comparison.Details.Count == 0
                ? string.Empty
                : $"<ul>{string.Concat(comparison.Details.Select(detail => $"<li>{WebUtility.HtmlEncode(detail)}</li>"))}</ul>";
            rows.Append($"""
                <section class="pair {comparison.Classification.ToString().ToLowerInvariant()}">
                  <h2>{WebUtility.HtmlEncode(comparison.ScenarioId)} <span>{comparison.Classification}</span></h2>
                  <div class="checks">WPF {Dimensions(wpf)}; Avalonia {Dimensions(avalonia)}; nonblank {Check(comparison.WpfNonblank && comparison.AvaloniaNonblank)}; focus {Check(comparison.FocusMatches)}; buttons {Check(comparison.ButtonOrderMatches)}; enabled state {Check(comparison.EnabledStateMatches)}; target pixels {WebUtility.HtmlEncode(PixelMetrics(comparison.PixelMetrics))}; shell context {WebUtility.HtmlEncode(PixelMetrics(comparison.ShellContextPixelMetrics))}</div>
                  {details}
                  <div class="images">{HtmlImage("WPF context", comparison.WpfImagePath)}{HtmlImage("Avalonia context", comparison.AvaloniaImagePath)}{TargetImages(wpf, avalonia)}{HtmlImage("Target diff", comparison.PixelMetrics?.HeatmapPath ?? string.Empty)}{HtmlImage("Shell-context diff", comparison.ShellContextPixelMetrics?.HeatmapPath ?? string.Empty)}</div>
                </section>
                """);
        }

        var html = $$$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>FreeP paired dialog/pane visual evidence</title>
            <style>
            body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#202124;background:#f7f8fa} main{max-width:1500px;margin:auto} .summary{display:flex;gap:18px;flex-wrap:wrap;margin:16px 0 24px} .pair{background:white;border:1px solid #cfd4dc;border-left:5px solid #2e7d32;margin:14px 0;padding:16px} .pair.mismatch{border-left-color:#c62828} .pair.limitation{border-left-color:#8a5a00} h2{font-size:18px;margin:0 0 8px} h2 span{font-size:13px;text-transform:uppercase;margin-left:10px} .checks{font-size:13px;color:#4b5563} .images{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px;margin-top:12px} figure{margin:0} img{max-width:100%;border:1px solid #b8bec7;background:white} figcaption{font-weight:600;margin-bottom:6px} @media(max-width:800px){.images{grid-template-columns:1fr}}
            </style></head><body><main>
            <h1>FreeP Dialog/Pane Paired Visual Evidence</h1>
            <p>Real app-owned WPF and Avalonia render-target captures at a 96 DPI target. Semantic route coverage is not visual evidence.</p>
            <p>{{{WebUtility.HtmlEncode(string.Join(" ", summary.EnvironmentNotes))}}}</p>
            <div class="summary"><b>Scenarios {{{summary.ScenarioCount}}}</b><b>Paired {{{summary.PairedCaptureCount}}}</b><b>Pass {{{summary.PassCount}}}</b><b>Mismatch {{{summary.MismatchCount}}}</b><b>Limitation {{{summary.LimitationCount}}}</b></div>
            {{{rows}}}
            </main></body></html>
            """;
        return string.Join(
            Environment.NewLine,
            html.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Select(line => line.TrimEnd()));
    }

    private static readonly PairedVisualEvidenceProfile<
        DialogPaneVisualEvidenceScenario,
        DialogPaneVisualEvidenceHostManifest,
        DialogPaneVisualEvidenceCapture> DialogPaneCollectorProfile = new(
            FreePVisualEvidenceRoutes.DialogPane,
            DialogPaneVisualEvidenceCatalog.All,
            scenario => scenario.Id,
            manifest => manifest.Captures,
            capture => capture.ScenarioId,
            manifest => manifest.Limitations,
            "The host manifest contained no scenario capture.",
            "exact process tree",
            PrepareDialogPaneArtifacts,
            CollectDialogPaneArtifacts,
            CreateDialogPaneHostManifest);

    private static void PrepareDialogPaneArtifacts(VisualEvidenceScenarioOutputPlan output)
    {
        if (File.Exists(output.ImagePath))
            File.Delete(output.ImagePath);
    }

    private static PairedVisualEvidenceArtifactResult<DialogPaneVisualEvidenceCapture> CollectDialogPaneArtifacts(
        PairedVisualEvidenceArtifactContext<DialogPaneVisualEvidenceCapture> context)
    {
        var limitations = new List<string>();
        if (!PairedVisualEvidenceCollector.TryCopyArtifacts(
            context.ScenarioRoot,
            new PairedVisualEvidenceArtifact(
                context.Capture.ImagePath,
                context.FinalOutput.ImagePath!)))
        {
            limitations.Add(
                $"{context.Host} {context.ScenarioId}: {context.ProcessResult} The declared PNG was missing.");
            return new(null, limitations);
        }

        var finalComparisonImagePath = context.FinalOutput.ImageRelativePath!;
        if (!string.IsNullOrWhiteSpace(context.Capture.PixelComparisonImagePath) &&
            !StringComparer.Ordinal.Equals(context.Capture.PixelComparisonImagePath, context.Capture.ImagePath))
        {
            if (!PairedVisualEvidenceCollector.TryCopyArtifacts(
                context.ScenarioRoot,
                new PairedVisualEvidenceArtifact(
                    context.Capture.PixelComparisonImagePath,
                    context.FinalOutput.ComparisonImagePath!)))
            {
                limitations.Add(
                    $"{context.Host} {context.ScenarioId}: the declared target-subtree PNG was missing.");
            }
            else
            {
                finalComparisonImagePath = context.FinalOutput.ComparisonImageRelativePath!;
            }
        }

        return new(
            context.Capture with
            {
                ImagePath = context.FinalOutput.ImageRelativePath!,
                PixelComparisonImagePath = finalComparisonImagePath,
            },
            limitations);
    }

    private static DialogPaneVisualEvidenceHostManifest CreateDialogPaneHostManifest(
        string host,
        IReadOnlyList<DialogPaneVisualEvidenceCapture> captures,
        IReadOnlyList<string> limitations) =>
        new(
            1,
            host,
            "visible-app-owned-render-target; scenario-isolated-processes",
            DialogPaneVisualEvidenceCatalog.TargetDpi,
            DialogPaneVisualEvidenceCatalog.LogicalShellWidth,
            DialogPaneVisualEvidenceCatalog.LogicalShellHeight,
            FreePVisualEvidenceCaptureOrchestration.UtcTimestamp(),
            captures,
            limitations);

    private static string Dimensions(DialogPaneVisualEvidenceCapture? capture)
    {
        if (capture is null)
            return "n/a";

        var sourceDpi = Math.Abs(capture.SourceDpiX - capture.DpiX) <= 0.5 &&
            Math.Abs(capture.SourceDpiY - capture.DpiY) <= 0.5
            ? string.Empty
            : $"; source {capture.SourceDpiX:0.##}x{capture.SourceDpiY:0.##} DPI";
        return $"{capture.LogicalWidth:0.##}x{capture.LogicalHeight:0.##} logical / {capture.PixelWidth}x{capture.PixelHeight} px @ {capture.DpiX:0.##} DPI{sourceDpi}";
    }

    private static string Check(bool value) => value ? "pass" : "mismatch";

    private static string PixelMetrics(DialogPaneVisualEvidencePixelMetrics? metrics) => metrics is null
        ? "unavailable"
        : string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{metrics.WpfPixelWidth}x{metrics.WpfPixelHeight}/{metrics.AvaloniaPixelWidth}x{metrics.AvaloniaPixelHeight}; changed {metrics.ChangedPixelRatio:P2}; foreground {metrics.ForegroundChangedPixelRatio:P2}; mean/max {metrics.MeanChannelDelta:F2}/{metrics.MaxChannelDelta}; threshold {Check(metrics.ThresholdPassed)}");

    private static string ImageLinks(
        DialogPaneVisualEvidenceComparison comparison,
        DialogPaneVisualEvidenceCapture? wpf,
        DialogPaneVisualEvidenceCapture? avalonia)
    {
        var links = new List<string>();
        if (!string.IsNullOrWhiteSpace(comparison.WpfImagePath))
            links.Add($"[WPF]({comparison.WpfImagePath})");
        if (!string.IsNullOrWhiteSpace(comparison.AvaloniaImagePath))
            links.Add($"[Avalonia]({comparison.AvaloniaImagePath})");
        if (!string.IsNullOrWhiteSpace(comparison.PixelMetrics?.HeatmapPath))
            links.Add($"[target diff]({comparison.PixelMetrics.HeatmapPath})");
        if (wpf is not null && !string.IsNullOrWhiteSpace(wpf.PixelComparisonImagePath) &&
            !StringComparer.Ordinal.Equals(wpf.PixelComparisonImagePath, wpf.ImagePath))
            links.Add($"[WPF target]({wpf.PixelComparisonImagePath})");
        if (avalonia is not null && !string.IsNullOrWhiteSpace(avalonia.PixelComparisonImagePath) &&
            !StringComparer.Ordinal.Equals(avalonia.PixelComparisonImagePath, avalonia.ImagePath))
            links.Add($"[Avalonia target]({avalonia.PixelComparisonImagePath})");
        if (!string.IsNullOrWhiteSpace(comparison.ShellContextPixelMetrics?.HeatmapPath))
            links.Add($"[shell diff]({comparison.ShellContextPixelMetrics.HeatmapPath})");
        return links.Count == 0 ? "n/a" : string.Join(" / ", links);
    }

    private static string TargetImages(
        DialogPaneVisualEvidenceCapture? wpf,
        DialogPaneVisualEvidenceCapture? avalonia)
    {
        if (wpf is null || avalonia is null ||
            StringComparer.Ordinal.Equals(wpf.PixelComparisonImagePath, wpf.ImagePath) &&
            StringComparer.Ordinal.Equals(avalonia.PixelComparisonImagePath, avalonia.ImagePath))
            return string.Empty;
        return HtmlImage("WPF target", wpf.PixelComparisonImagePath) +
            HtmlImage("Avalonia target", avalonia.PixelComparisonImagePath);
    }

    private static string HtmlImage(string host, string imagePath) => string.IsNullOrWhiteSpace(imagePath)
        ? $"<figure><figcaption>{host}</figcaption><p>Capture unavailable.</p></figure>"
        : $"<figure><figcaption>{host}</figcaption><img loading=\"lazy\" src=\"{WebUtility.HtmlEncode(imagePath)}\" alt=\"{host} capture\"></figure>";
}
