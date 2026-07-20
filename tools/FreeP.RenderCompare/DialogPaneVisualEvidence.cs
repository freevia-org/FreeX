using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FreeP.App.Compositor;

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
    IReadOnlyList<string> Limitations);

internal static class DialogPaneVisualEvidence
{
    private const string HostOutputArgument = "--dialog-pane-visual-evidence-output";
    private const string HostScenarioArgument = "--dialog-pane-visual-evidence-scenario";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    internal static int Run(string outputDirectory, string wpfExecutable, string avaloniaExecutable, TimeSpan timeout)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        wpfExecutable = Path.GetFullPath(wpfExecutable);
        avaloniaExecutable = Path.GetFullPath(avaloniaExecutable);
        if (!File.Exists(wpfExecutable))
            throw new FileNotFoundException("WPF capture host was not found.", wpfExecutable);
        if (!File.Exists(avaloniaExecutable))
            throw new FileNotFoundException("Avalonia capture host was not found.", avaloniaExecutable);

        Directory.CreateDirectory(outputDirectory);
        var runRoot = Path.Combine(Path.GetTempPath(), "freep-dialog-pane-evidence-" + Guid.NewGuid().ToString("N"));
        var limitations = new List<string>();
        try
        {
            var wpf = CaptureHost("wpf", wpfExecutable, outputDirectory, runRoot, timeout, limitations);
            var avalonia = CaptureHost("avalonia", avaloniaExecutable, outputDirectory, runRoot, timeout, limitations);
            var summary = BuildSummary(wpf, avalonia, limitations);
            WriteReports(outputDirectory, summary);

            Console.WriteLine($"Paired captures: {summary.PairedCaptureCount}/{summary.ScenarioCount}");
            Console.WriteLine($"Pass: {summary.PassCount}; mismatch: {summary.MismatchCount}; limitation: {summary.LimitationCount}");
            Console.WriteLine($"Summary: {Path.Combine(outputDirectory, "summary.json")}");
            return 0;
        }
        finally
        {
            try
            {
                if (Directory.Exists(runRoot))
                    Directory.Delete(runRoot, recursive: true);
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"Temporary capture cleanup was incomplete: {ex.Message}");
            }
        }
    }

    internal static DialogPaneVisualEvidenceSummary BuildSummary(
        DialogPaneVisualEvidenceHostManifest wpf,
        DialogPaneVisualEvidenceHostManifest avalonia,
        IReadOnlyList<string>? runnerLimitations = null)
    {
        var wpfByScenario = wpf.Captures.ToDictionary(capture => capture.ScenarioId, StringComparer.Ordinal);
        var avaloniaByScenario = avalonia.Captures.ToDictionary(capture => capture.ScenarioId, StringComparer.Ordinal);
        var comparisons = DialogPaneVisualEvidenceCatalog.All
            .Select(scenario => DialogPaneVisualEvidenceComparer.Compare(
                scenario,
                wpfByScenario.GetValueOrDefault(scenario.Id),
                avaloniaByScenario.GetValueOrDefault(scenario.Id)))
            .ToArray();
        var limitations = wpf.Limitations
            .Concat(avalonia.Limitations)
            .Concat(runnerLimitations ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new DialogPaneVisualEvidenceSummary(
            1,
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
            limitations);
    }

    internal static void WriteReports(string outputDirectory, DialogPaneVisualEvidenceSummary summary)
    {
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(
            Path.Combine(outputDirectory, "summary.json"),
            JsonSerializer.Serialize(summary, JsonOptions));
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
        builder.AppendLine();
        builder.AppendLine("| Scenario | Classification | WPF dimensions | Avalonia dimensions | Nonblank | Focus | Buttons | Enabled state | Paired images |");
        builder.AppendLine("|---|---|---:|---:|---|---|---|---|---|");
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
                .Append(" | ").Append(ImageLinks(comparison))
                .AppendLine(" |");
            foreach (var detail in comparison.Details)
                builder.AppendLine($"|  | Detail |  |  |  |  |  |  | {detail.Replace("|", "\\|")} |");
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
                  <div class="checks">WPF {Dimensions(wpf)}; Avalonia {Dimensions(avalonia)}; nonblank {Check(comparison.WpfNonblank && comparison.AvaloniaNonblank)}; focus {Check(comparison.FocusMatches)}; buttons {Check(comparison.ButtonOrderMatches)}; enabled state {Check(comparison.EnabledStateMatches)}</div>
                  {details}
                  <div class="images">{HtmlImage("WPF", comparison.WpfImagePath)}{HtmlImage("Avalonia", comparison.AvaloniaImagePath)}</div>
                </section>
                """);
        }

        return $$$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>FreeP paired dialog/pane visual evidence</title>
            <style>
            body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#202124;background:#f7f8fa} main{max-width:1500px;margin:auto} .summary{display:flex;gap:18px;flex-wrap:wrap;margin:16px 0 24px} .pair{background:white;border:1px solid #cfd4dc;border-left:5px solid #2e7d32;margin:14px 0;padding:16px} .pair.mismatch{border-left-color:#c62828} .pair.limitation{border-left-color:#8a5a00} h2{font-size:18px;margin:0 0 8px} h2 span{font-size:13px;text-transform:uppercase;margin-left:10px} .checks{font-size:13px;color:#4b5563} .images{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px;margin-top:12px} figure{margin:0} img{max-width:100%;border:1px solid #b8bec7;background:white} figcaption{font-weight:600;margin-bottom:6px} @media(max-width:800px){.images{grid-template-columns:1fr}}
            </style></head><body><main>
            <h1>FreeP Dialog/Pane Paired Visual Evidence</h1>
            <p>Real app-owned WPF and Avalonia render-target captures at a 96 DPI target. Semantic route coverage is not visual evidence.</p>
            <div class="summary"><b>Scenarios {{{summary.ScenarioCount}}}</b><b>Paired {{{summary.PairedCaptureCount}}}</b><b>Pass {{{summary.PassCount}}}</b><b>Mismatch {{{summary.MismatchCount}}}</b><b>Limitation {{{summary.LimitationCount}}}</b></div>
            {{{rows}}}
            </main></body></html>
            """;
    }

    private static DialogPaneVisualEvidenceHostManifest CaptureHost(
        string host,
        string executable,
        string outputDirectory,
        string runRoot,
        TimeSpan timeout,
        List<string> runnerLimitations)
    {
        var captures = new List<DialogPaneVisualEvidenceCapture>();
        var hostLimitations = new List<string>();
        var finalHostDirectory = Path.Combine(outputDirectory, host);
        Directory.CreateDirectory(finalHostDirectory);
        foreach (var scenario in DialogPaneVisualEvidenceCatalog.All)
        {
            var finalImage = Path.Combine(finalHostDirectory, scenario.Id + ".png");
            if (File.Exists(finalImage))
                File.Delete(finalImage);
            var scenarioRoot = Path.Combine(runRoot, host, scenario.Id);
            Directory.CreateDirectory(scenarioRoot);
            Console.WriteLine($"[{host}] {scenario.Id}");
            var result = RunScenario(executable, scenarioRoot, scenario.Id, timeout);
            var manifestPath = Path.Combine(scenarioRoot, host, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                runnerLimitations.Add($"{host} {scenario.Id}: {result} No host manifest was produced.");
                continue;
            }

            var manifest = JsonSerializer.Deserialize<DialogPaneVisualEvidenceHostManifest>(
                File.ReadAllText(manifestPath), JsonOptions);
            var capture = manifest?.Captures.SingleOrDefault(item => StringComparer.Ordinal.Equals(item.ScenarioId, scenario.Id));
            if (capture is null)
            {
                runnerLimitations.Add($"{host} {scenario.Id}: {result} The host manifest contained no scenario capture.");
                continue;
            }

            var sourceImage = Path.Combine(scenarioRoot, capture.ImagePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(sourceImage))
            {
                runnerLimitations.Add($"{host} {scenario.Id}: {result} The declared PNG was missing.");
                continue;
            }

            File.Copy(sourceImage, finalImage, overwrite: true);
            captures.Add(capture with { ImagePath = $"{host}/{scenario.Id}.png" });
            if (manifest is not null)
                hostLimitations.AddRange(manifest.Limitations);
        }

        var hostManifest = new DialogPaneVisualEvidenceHostManifest(
            1,
            host,
            "visible-app-owned-render-target; scenario-isolated-processes",
            DialogPaneVisualEvidenceCatalog.TargetDpi,
            DialogPaneVisualEvidenceCatalog.LogicalShellWidth,
            DialogPaneVisualEvidenceCatalog.LogicalShellHeight,
            DateTimeOffset.UtcNow.ToString("O"),
            captures,
            hostLimitations.Distinct(StringComparer.Ordinal).ToArray());
        File.WriteAllText(
            Path.Combine(finalHostDirectory, "manifest.json"),
            JsonSerializer.Serialize(hostManifest, JsonOptions));
        return hostManifest;
    }

    private static string RunScenario(string executable, string outputRoot, string scenarioId, TimeSpan timeout)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            Arguments = $"{Quote(HostOutputArgument)} {Quote(outputRoot)} {Quote(HostScenarioArgument)} {Quote(scenarioId)}",
        });
        if (process is null)
            return "The process did not start.";
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            return $"PID {process.Id} timed out after {timeout.TotalSeconds:0} seconds and its exact process tree was stopped.";
        }
        return $"PID {process.Id} exited with code {process.ExitCode}.";
    }

    private static string Quote(string value) => '"' + value.Replace("\"", "\\\"") + '"';

    private static string Dimensions(DialogPaneVisualEvidenceCapture? capture) => capture is null
        ? "n/a"
        : $"{capture.LogicalWidth:0.##}x{capture.LogicalHeight:0.##} logical / {capture.PixelWidth}x{capture.PixelHeight} px @ {capture.DpiX:0.##} DPI";

    private static string Check(bool value) => value ? "pass" : "mismatch";

    private static string ImageLinks(DialogPaneVisualEvidenceComparison comparison)
    {
        var links = new List<string>();
        if (!string.IsNullOrWhiteSpace(comparison.WpfImagePath))
            links.Add($"[WPF]({comparison.WpfImagePath})");
        if (!string.IsNullOrWhiteSpace(comparison.AvaloniaImagePath))
            links.Add($"[Avalonia]({comparison.AvaloniaImagePath})");
        return links.Count == 0 ? "n/a" : string.Join(" / ", links);
    }

    private static string HtmlImage(string host, string imagePath) => string.IsNullOrWhiteSpace(imagePath)
        ? $"<figure><figcaption>{host}</figcaption><p>Capture unavailable.</p></figure>"
        : $"<figure><figcaption>{host}</figcaption><img loading=\"lazy\" src=\"{WebUtility.HtmlEncode(imagePath)}\" alt=\"{host} capture\"></figure>";
}
