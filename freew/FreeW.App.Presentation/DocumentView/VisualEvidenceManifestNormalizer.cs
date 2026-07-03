using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FreeW.App.Presentation.DocumentView;

public sealed record FreeWVisualEvidenceExpectedScenario(
    string HostId,
    string ScenarioId,
    int MinimumExpectedOutputs);

public sealed record FreeWVisualEvidenceNormalizedSource(
    string ManifestPath,
    IReadOnlyList<string> HostIds,
    int EvidenceCount);

public sealed record FreeWVisualEvidenceNormalizedScenario(
    string HostId,
    string ScenarioId,
    int MinimumExpectedOutputs,
    int ActualOutputs,
    int TrustedOutputs,
    bool Expected,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualEvidenceNormalizedRow(
    string EvidenceId,
    string SourceManifestPath,
    string ScenarioId,
    string HostId,
    IReadOnlyList<string> ExpectedFeatureTags,
    string OutputName,
    string OutputPath,
    int PixelWidth,
    int PixelHeight,
    long ByteLength,
    string Sha256,
    FreeWVisualPixelStats PixelStats,
    int PageNumber,
    int PageCount,
    string LayoutKind,
    string ExpectedOutputName,
    FreeWVisualPageFeatureExpectation PageFeatures,
    FreeWVisualTableExpectation Tables,
    FreeWVisualDrawingObjectExpectation DrawingObjects,
    FreeWVisualChartSmartArtExpectation ChartSmartArt,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualEvidenceNormalizedSummary(
    string SchemaId,
    int SchemaVersion,
    IReadOnlyList<FreeWVisualEvidenceNormalizedSource> Sources,
    IReadOnlyList<FreeWVisualEvidenceExpectedScenario> ExpectedScenarios,
    IReadOnlyList<FreeWVisualEvidenceNormalizedScenario> Scenarios,
    IReadOnlyList<FreeWVisualEvidenceNormalizedRow> Evidence,
    IReadOnlyList<FreeWVisualBaselineComparison> BaselineComparisons,
    FreeWVisualEvidenceTrust Trust);

public static class FreeWVisualEvidenceManifestNormalizer
{
    public const string SummarySchemaId = "freew.visual-evidence-summary.v1";
    public const int SummarySchemaVersion = 8;
    public const string SummaryJsonFileName = "freew_visual_evidence_summary.json";
    public const string SummaryMarkdownFileName = "freew_visual_evidence_summary.md";
    public const string WpfHostId = "wpf-fidelity-render";
    public const string AvaloniaHostId = "avalonia-page-layout-shot";
    public static IReadOnlyList<string> BackstageRendererScenarioIds { get; } =
    [
        "backstage-print-preview-fidelity",
        "backstage-pdf-export-fidelity"
    ];
    public static IReadOnlyList<string> NoteRendererScenarioIds { get; } =
    [
        "f2-footnotes",
        "f2-endnotes"
    ];
    public static IReadOnlyList<string> SectionGeometryRendererScenarioIds { get; } =
    [
        "f2-section-landscape"
    ];
    public static IReadOnlyList<string> ReviewRendererScenarioIds { get; } =
    [
        "f2-tracked-changes",
        "f2-comments"
    ];
    public static IReadOnlyList<string> TableRendererScenarioIds { get; } =
    [
        "table-layout-complex"
    ];
    public static IReadOnlyList<string> DrawingObjectRendererScenarioIds { get; } =
    [
        "drawing-objects-complex",
        "wordart-watermark-stress"
    ];
    public static IReadOnlyList<string> WordArtWatermarkRendererScenarioIds { get; } =
    [
        "wordart-picture-watermark-layout"
    ];
    public static IReadOnlyList<string> ChartSmartArtRendererScenarioIds { get; } =
    [
        "chart-smartart-complex"
    ];

    public static IReadOnlyList<FreeWVisualEvidenceExpectedScenario> DefaultExpectedScenarios { get; } =
        BuildDefaultExpectedScenarios();

    public static FreeWVisualEvidenceManifest ReadManifest(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        var json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<FreeWVisualEvidenceManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Visual evidence manifest could not be read: {Path.GetFileName(manifestPath)}");
    }

    public static FreeWVisualEvidenceNormalizedSummary BuildNormalizedSummaryFromFiles(
        IReadOnlyList<string> manifestPaths,
        string runRoot,
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario>? expectedScenarios = null)
    {
        ArgumentNullException.ThrowIfNull(manifestPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(runRoot);

        if (manifestPaths.Count == 0)
            throw new ArgumentException("At least one visual evidence manifest is required.", nameof(manifestPaths));

        var normalizedRoot = Path.GetFullPath(runRoot);
        var expected = (expectedScenarios ?? DefaultExpectedScenarios)
            .OrderBy(e => e.HostId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var expectedByKey = expected.ToDictionary(
            e => ScenarioKey(e.HostId, e.ScenarioId),
            StringComparer.OrdinalIgnoreCase);

        var failures = new List<string>();
        var sources = new List<FreeWVisualEvidenceNormalizedSource>();
        var rows = new List<FreeWVisualEvidenceNormalizedRow>();
        var evidenceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var manifestPath in manifestPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var fullManifestPath = Path.GetFullPath(manifestPath);
            var sourceManifestPath = NormalizeRelativePath(normalizedRoot, fullManifestPath);
            if (!IsSubPathOf(normalizedRoot, fullManifestPath))
                failures.Add($"source manifest '{sourceManifestPath}' is outside the run root");

            var manifest = ReadManifest(fullManifestPath);
            ValidateManifestHeader(manifest, sourceManifestPath, failures);

            var hostIds = manifest.Evidence
                .Select(e => e.HostId)
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)
                .ToList();
            sources.Add(new FreeWVisualEvidenceNormalizedSource(
                sourceManifestPath,
                hostIds,
                manifest.Evidence.Count));

            var manifestDirectory = Path.GetDirectoryName(fullManifestPath) ?? normalizedRoot;
            foreach (var row in manifest.Evidence)
            {
                rows.Add(NormalizeRow(
                    row,
                    sourceManifestPath,
                    manifestDirectory,
                    normalizedRoot,
                    evidenceIds,
                    failures));
            }
        }

        var scenarios = BuildScenarioSummaries(rows, expected, expectedByKey, failures);
        ValidateBackstageRendererPairs(rows, failures);
        ValidateSectionGeometryRendererPairs(rows, failures);
        ValidateReviewRendererPairs(rows, failures);
        var summaryTrust = new FreeWVisualEvidenceTrust(failures.Count == 0, failures);
        return new FreeWVisualEvidenceNormalizedSummary(
            SummarySchemaId,
            SummarySchemaVersion,
            sources
                .OrderBy(s => s.ManifestPath, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            expected,
            scenarios,
            rows
                .OrderBy(r => r.HostId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.ScenarioId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.PageNumber)
                .ThenBy(r => r.OutputName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            [],
            summaryTrust);
    }

    public static string ToJson(FreeWVisualEvidenceNormalizedSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return JsonSerializer.Serialize(summary, JsonOptions);
    }

    public static string ToMarkdown(FreeWVisualEvidenceNormalizedSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var sb = new StringBuilder();
        sb.AppendLine("# FreeW Visual Evidence Summary");
        sb.AppendLine();
        sb.AppendLine($"Schema: `{summary.SchemaId}` v{summary.SchemaVersion.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Trust: {(summary.Trust.Passed ? "passed" : "failed")}");
        sb.AppendLine($"Evidence rows: {summary.Evidence.Count.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Baseline comparisons: {summary.BaselineComparisons.Count.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine();

        if (summary.Trust.Failures.Count > 0)
        {
            sb.AppendLine("## Validation Failures");
            sb.AppendLine();
            foreach (var failure in summary.Trust.Failures)
                sb.AppendLine($"- {failure}");
            sb.AppendLine();
        }

        sb.AppendLine("## Scenario Coverage");
        sb.AppendLine();
        sb.AppendLine("| Host | Scenario | Outputs | Trusted | Minimum | Trust |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: | --- |");
        foreach (var scenario in summary.Scenarios)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(scenario.HostId)} | {EscapeMarkdown(scenario.ScenarioId)} | " +
                $"{scenario.ActualOutputs.ToString(CultureInfo.InvariantCulture)} | " +
                $"{scenario.TrustedOutputs.ToString(CultureInfo.InvariantCulture)} | " +
                $"{scenario.MinimumExpectedOutputs.ToString(CultureInfo.InvariantCulture)} | " +
                $"{(scenario.Trust.Passed ? "passed" : "failed")} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Evidence");
        sb.AppendLine();
        sb.AppendLine("| Host | Scenario | Output | Features | Size | Bytes | SHA-256 | Trust |");
        sb.AppendLine("| --- | --- | --- | --- | ---: | ---: | --- | --- |");
        foreach (var row in summary.Evidence)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(row.HostId)} | {EscapeMarkdown(row.ScenarioId)} | " +
                $"{EscapeMarkdown(row.OutputPath)} | " +
                $"{EscapeMarkdown(FeatureSummary(row))} | " +
                $"{row.PixelWidth.ToString(CultureInfo.InvariantCulture)}x{row.PixelHeight.ToString(CultureInfo.InvariantCulture)} | " +
                $"{row.ByteLength.ToString(CultureInfo.InvariantCulture)} | `{row.Sha256}` | " +
                $"{(row.Trust.Passed ? "passed" : "failed")} |");
        }

        if (summary.BaselineComparisons.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Word Baseline Comparison");
            sb.AppendLine();
            sb.AppendLine($"Status counts: {EscapeMarkdown(FormatBaselineStatusCounts(summary.BaselineComparisons))}");
            sb.AppendLine();
            sb.AppendLine("| Host | Scenario | Output | Baseline ID | Baseline Path | Status | Size | Mean Channel Delta | Mean Gray Delta | Changed Pixels | Tolerance | Limits | Notes |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | --- | --- | --- |");
            foreach (var comparison in summary.BaselineComparisons)
            {
                var metrics = comparison.Metrics;
                var size = metrics is null
                    ? "-"
                    : $"{metrics.ActualWidth.ToString(CultureInfo.InvariantCulture)}x{metrics.ActualHeight.ToString(CultureInfo.InvariantCulture)} vs {metrics.BaselineWidth.ToString(CultureInfo.InvariantCulture)}x{metrics.BaselineHeight.ToString(CultureInfo.InvariantCulture)}{(metrics.BaselineResized ? " resized" : string.Empty)}";
                var meanChannel = metrics?.MeanAbsoluteChannelDelta.ToString("0.####", CultureInfo.InvariantCulture) ?? "-";
                var meanGray = metrics?.MeanAbsoluteGrayscaleDelta.ToString("0.####", CultureInfo.InvariantCulture) ?? "-";

                sb.AppendLine(
                    $"| {EscapeMarkdown(comparison.HostId)} | {EscapeMarkdown(comparison.ScenarioId)} | " +
                    $"{EscapeMarkdown(comparison.OutputName)} | " +
                    $"{EscapeMarkdown(comparison.BaselineId)} | " +
                    $"{EscapeMarkdown(FormatBaselinePath(comparison))} | " +
                    $"{EscapeMarkdown(comparison.Status)} | " +
                    $"{EscapeMarkdown(size)} | {meanChannel} | {meanGray} | {FormatChangedPixels(metrics)} | " +
                    $"{EscapeMarkdown(comparison.Tolerance.Name)} | " +
                    $"{EscapeMarkdown(FormatToleranceLimits(comparison.Tolerance))} | " +
                    $"{EscapeMarkdown(FormatComparisonNotes(comparison))} |");
            }
        }

        return sb.ToString();
    }

    public static FreeWVisualEvidenceNormalizedSummary WithBaselineComparisons(
        FreeWVisualEvidenceNormalizedSummary summary,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(baselineComparisons);

        var ordered = baselineComparisons
            .OrderBy(c => c.HostId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.PageNumber)
            .ThenBy(c => c.OutputName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var failures = summary.Trust.Failures.ToList();
        foreach (var comparison in ordered.Where(c => !c.Trust.Passed))
        {
            foreach (var failure in comparison.Trust.Failures)
            {
                failures.Add(
                    $"{comparison.HostId}/{comparison.ScenarioId}/{comparison.OutputName}: {failure}");
            }
        }

        return summary with
        {
            BaselineComparisons = ordered,
            Trust = new FreeWVisualEvidenceTrust(failures.Count == 0, failures)
        };
    }

    public static void WriteSummaryFiles(
        FreeWVisualEvidenceNormalizedSummary summary,
        string jsonPath,
        string markdownPath)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(markdownPath);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonPath)) ?? ".");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(markdownPath)) ?? ".");
        File.WriteAllText(jsonPath, ToJson(summary));
        File.WriteAllText(markdownPath, ToMarkdown(summary));
    }

    public static void EnsureSummaryTrusted(FreeWVisualEvidenceNormalizedSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        if (summary.Trust.Passed)
            return;

        throw new InvalidOperationException(
            "Visual evidence summary failed validation: " + string.Join("; ", summary.Trust.Failures));
    }

    private static IReadOnlyList<FreeWVisualEvidenceExpectedScenario> BuildDefaultExpectedScenarios()
    {
        var expected = new List<FreeWVisualEvidenceExpectedScenario>();
        foreach (var scenario in FreeWVisualEvidencePlanner.Scenarios)
        {
            if (NoteRendererScenarioIds.Contains(scenario.ScenarioId, StringComparer.OrdinalIgnoreCase))
            {
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    WpfHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    AvaloniaHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
            }
            else if (SectionGeometryRendererScenarioIds.Contains(scenario.ScenarioId, StringComparer.OrdinalIgnoreCase))
            {
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    WpfHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    AvaloniaHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
            }
            else if (ReviewRendererScenarioIds.Contains(scenario.ScenarioId, StringComparer.OrdinalIgnoreCase))
            {
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    WpfHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    AvaloniaHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
            }
            else if (scenario.ScenarioId.StartsWith("f2-", StringComparison.OrdinalIgnoreCase))
            {
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    WpfHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
            }
            else if (scenario.ExpectedFeatureTags.Contains("avalonia", StringComparer.OrdinalIgnoreCase))
            {
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    AvaloniaHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
            }
            else if (BackstageRendererScenarioIds.Contains(scenario.ScenarioId, StringComparer.OrdinalIgnoreCase))
            {
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    WpfHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    AvaloniaHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
            }
            else if (TableRendererScenarioIds.Contains(scenario.ScenarioId, StringComparer.OrdinalIgnoreCase))
            {
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    WpfHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    AvaloniaHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
            }
            else if (DrawingObjectRendererScenarioIds.Contains(scenario.ScenarioId, StringComparer.OrdinalIgnoreCase))
            {
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    WpfHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    AvaloniaHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
            }
            else if (WordArtWatermarkRendererScenarioIds.Contains(scenario.ScenarioId, StringComparer.OrdinalIgnoreCase))
            {
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    WpfHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    AvaloniaHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
            }
            else if (ChartSmartArtRendererScenarioIds.Contains(scenario.ScenarioId, StringComparer.OrdinalIgnoreCase))
            {
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    WpfHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    AvaloniaHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
            }
        }

        return expected;
    }

    private static void ValidateManifestHeader(
        FreeWVisualEvidenceManifest manifest,
        string sourceManifestPath,
        List<string> failures)
    {
        if (!string.Equals(manifest.SchemaId, FreeWVisualEvidencePlanner.SchemaId, StringComparison.Ordinal))
            failures.Add($"source manifest '{sourceManifestPath}' has unsupported schema '{manifest.SchemaId}'");
        if (manifest.SchemaVersion != FreeWVisualEvidencePlanner.SchemaVersion)
            failures.Add($"source manifest '{sourceManifestPath}' has unsupported schema version {manifest.SchemaVersion.ToString(CultureInfo.InvariantCulture)}");
        if (!string.Equals(manifest.Product, "FreeW", StringComparison.Ordinal))
            failures.Add($"source manifest '{sourceManifestPath}' has unsupported product '{manifest.Product}'");
        if (manifest.Evidence.Count == 0)
            failures.Add($"source manifest '{sourceManifestPath}' contains no evidence rows");
    }

    private static FreeWVisualEvidenceNormalizedRow NormalizeRow(
        FreeWVisualEvidenceRow row,
        string sourceManifestPath,
        string manifestDirectory,
        string runRoot,
        HashSet<string> evidenceIds,
        List<string> summaryFailures)
    {
        var rowFailures = new List<string>();
        if (!evidenceIds.Add(row.EvidenceId))
            rowFailures.Add($"duplicate evidence id '{row.EvidenceId}'");
        if (string.IsNullOrWhiteSpace(row.HostId))
            rowFailures.Add("host id is required");
        if (string.IsNullOrWhiteSpace(row.ScenarioId))
            rowFailures.Add("scenario id is required");
        if (row.PixelWidth <= 0 || row.PixelHeight <= 0)
            rowFailures.Add("pixel dimensions must be positive");
        if (row.PixelStats.Width != row.PixelWidth || row.PixelStats.Height != row.PixelHeight)
            rowFailures.Add("pixel stats dimensions do not match evidence dimensions");
        if (!string.Equals(row.OutputName, row.PageExpectation.ExpectedOutputName, StringComparison.OrdinalIgnoreCase))
            rowFailures.Add($"output name '{row.OutputName}' does not match expected '{row.PageExpectation.ExpectedOutputName}'");
        ValidateFeatureExpectations(row, rowFailures);

        var outputPath = ResolveOutputPath(row.OutputPath, manifestDirectory);
        var relativeOutputPath = NormalizeRelativePath(runRoot, outputPath);
        if (!IsSubPathOf(runRoot, outputPath))
            rowFailures.Add($"output path '{relativeOutputPath}' is outside the run root");
        if (!string.Equals(Path.GetFileName(outputPath), row.OutputName, StringComparison.OrdinalIgnoreCase))
            rowFailures.Add($"output file name '{Path.GetFileName(outputPath)}' does not match manifest output name '{row.OutputName}'");

        var fileLength = 0L;
        var sha256 = string.Empty;
        if (File.Exists(outputPath))
        {
            var file = new FileInfo(outputPath);
            fileLength = file.Length;
            sha256 = ComputeSha256(outputPath);
            if (fileLength != row.ByteLength)
                rowFailures.Add($"byte length {row.ByteLength.ToString(CultureInfo.InvariantCulture)} does not match file length {fileLength.ToString(CultureInfo.InvariantCulture)} for '{relativeOutputPath}'");
        }
        else
        {
            rowFailures.Add($"output file '{relativeOutputPath}' does not exist");
        }

        rowFailures.AddRange(row.Trust.Failures);
        if (!row.Trust.Passed)
            rowFailures.Add($"manifest trust failed for '{row.OutputName}'");

        if (rowFailures.Count > 0)
        {
            foreach (var failure in rowFailures)
                summaryFailures.Add($"{row.HostId}/{row.ScenarioId}/{row.OutputName}: {failure}");
        }

        var trust = new FreeWVisualEvidenceTrust(rowFailures.Count == 0, rowFailures);
        return new FreeWVisualEvidenceNormalizedRow(
            row.EvidenceId,
            sourceManifestPath,
            row.ScenarioId,
            row.HostId,
            row.ExpectedFeatureTags,
            row.OutputName,
            relativeOutputPath,
            row.PixelWidth,
            row.PixelHeight,
            fileLength > 0 ? fileLength : row.ByteLength,
            sha256,
            row.PixelStats,
            row.PageExpectation.PageNumber,
            row.PageExpectation.PageCount,
            row.PageExpectation.LayoutKind,
            row.PageExpectation.ExpectedOutputName,
            row.PageExpectation.Features,
            row.PageExpectation.Tables,
            row.PageExpectation.DrawingObjects,
            row.PageExpectation.ChartSmartArt,
            trust);
    }

    private static void ValidateFeatureExpectations(
        FreeWVisualEvidenceRow row,
        List<string> rowFailures)
    {
        var composition = row.PageExpectation.Composition;
        var features = row.PageExpectation.Features;
        if (composition.ExpectsColumns && features.Columns.Count <= 1)
            rowFailures.Add("scenario expects multi-column layout but the page expectation records one column");
        if (composition.ExpectsPageBorder && !features.PageBorder.Present)
            rowFailures.Add("scenario expects a page border but the page expectation records none");
        if (composition.ExpectsWatermark && !features.Watermark.Present)
            rowFailures.Add("scenario expects a watermark but the page expectation records none");
        if (composition.ExpectsTables && row.PageExpectation.Tables.TableCount <= 0)
            rowFailures.Add("scenario expects table layout but the page expectation records no tables");
        if (composition.ExpectsFloatingObjects && row.PageExpectation.DrawingObjects.FloatingObjectCount <= 0)
            rowFailures.Add("scenario expects floating objects but the page expectation records none");
        ValidateTableFeatureTags(row, rowFailures);
        ValidateWatermarkFeatureTags(row, rowFailures);
        ValidateDrawingObjectFeatureTags(row, rowFailures);
        ValidateChartSmartArtFeatureTags(row, rowFailures);
        if (features.Section.SectionOrdinal <= 0)
            rowFailures.Add("section ordinal must be positive");
        if (features.Section.SectionRelativePageNumber <= 0)
            rowFailures.Add("section-relative page number must be positive");
    }

    private static void ValidateTableFeatureTags(
        FreeWVisualEvidenceRow row,
        List<string> rowFailures)
    {
        var tags = row.ExpectedFeatureTags;
        var tables = row.PageExpectation.Tables;
        if (!tags.Contains("table-layout", StringComparer.OrdinalIgnoreCase))
            return;

        if (tables.TableCount <= 0)
            rowFailures.Add("table-layout evidence must include at least one table plan");
        if (tags.Contains("merged-cells", StringComparer.OrdinalIgnoreCase) && !tables.HasMergedCells)
            rowFailures.Add("table-layout evidence expects merged cells but the table plan records none");
        if (tags.Contains("vertical-merge", StringComparer.OrdinalIgnoreCase) && !tables.HasVerticalMerges)
            rowFailures.Add("table-layout evidence expects vertical merges but the table plan records none");
        if (tags.Contains("repeat-header-row", StringComparer.OrdinalIgnoreCase) && !tables.RepeatsHeaderRow)
            rowFailures.Add("table-layout evidence expects repeated header rows but the table plan records none");
        if (tags.Contains("banded-rows", StringComparer.OrdinalIgnoreCase) && !tables.HasBandedRows)
            rowFailures.Add("table-layout evidence expects banded rows but the table plan records none");
        if (tags.Contains("cell-shading", StringComparer.OrdinalIgnoreCase) && !tables.HasCellShading)
            rowFailures.Add("table-layout evidence expects cell shading but the table plan records none");
        if (tags.Contains("cell-borders", StringComparer.OrdinalIgnoreCase) && !tables.HasCustomCellBorders)
            rowFailures.Add("table-layout evidence expects custom cell borders but the table plan records none");
        if (tags.Contains("cell-margins", StringComparer.OrdinalIgnoreCase) && !tables.HasCellMargins)
            rowFailures.Add("table-layout evidence expects cell margins but the table plan records none");
        if (tags.Contains("cell-spacing", StringComparer.OrdinalIgnoreCase) && !tables.HasCellSpacing)
            rowFailures.Add("table-layout evidence expects cell spacing but the table plan records none");
        if (tags.Contains("vertical-text", StringComparer.OrdinalIgnoreCase) && !tables.HasVerticalText)
            rowFailures.Add("table-layout evidence expects vertical text but the table plan records none");
        if (tags.Contains("named-table-style", StringComparer.OrdinalIgnoreCase) && !tables.HasNamedStyle)
            rowFailures.Add("table-layout evidence expects a named table style but the table plan records none");
    }

    private static void ValidateWatermarkFeatureTags(
        FreeWVisualEvidenceRow row,
        List<string> rowFailures)
    {
        var tags = row.ExpectedFeatureTags;
        var watermark = row.PageExpectation.Features.Watermark;
        if (tags.Contains("picture-watermark", StringComparer.OrdinalIgnoreCase) && !watermark.IsPicture)
            rowFailures.Add("evidence expects a picture watermark but the page expectation records a text watermark");
    }

    private static void ValidateDrawingObjectFeatureTags(
        FreeWVisualEvidenceRow row,
        List<string> rowFailures)
    {
        var tags = row.ExpectedFeatureTags;
        var objects = row.PageExpectation.DrawingObjects;
        if (!tags.Contains("drawing-objects", StringComparer.OrdinalIgnoreCase))
            return;

        if (objects.FloatingObjectCount <= 0)
            rowFailures.Add("drawing-object evidence must include at least one floating object plan");
        if (tags.Contains("shapes", StringComparer.OrdinalIgnoreCase) && !objects.HasShapes)
            rowFailures.Add("drawing-object evidence expects shapes but the object plan records none");
        if (tags.Contains("charts", StringComparer.OrdinalIgnoreCase) && !objects.HasCharts)
            rowFailures.Add("drawing-object evidence expects charts but the object plan records none");
        if (tags.Contains("smartart", StringComparer.OrdinalIgnoreCase) && !objects.HasSmartArt)
            rowFailures.Add("drawing-object evidence expects SmartArt but the object plan records none");
        if (tags.Contains("wordart", StringComparer.OrdinalIgnoreCase) && !objects.HasWordArt)
            rowFailures.Add("drawing-object evidence expects WordArt but the object plan records none");
        if (tags.Contains("drawing-groups", StringComparer.OrdinalIgnoreCase) && !objects.HasGroups)
            rowFailures.Add("drawing-object evidence expects drawing groups but the object plan records none");
        if (tags.Contains("behind-text", StringComparer.OrdinalIgnoreCase) && objects.BehindTextCount <= 0)
            rowFailures.Add("drawing-object evidence expects behind-text objects but the object plan records none");
        if (tags.Contains("in-front", StringComparer.OrdinalIgnoreCase) && objects.InFrontCount <= 0)
            rowFailures.Add("drawing-object evidence expects in-front objects but the object plan records none");
        if (tags.Contains("square-wrap", StringComparer.OrdinalIgnoreCase) && !objects.HasSquareWrap)
            rowFailures.Add("drawing-object evidence expects square wrapping but the object plan records none");
        if (tags.Contains("top-bottom-wrap", StringComparer.OrdinalIgnoreCase) && !objects.HasTopAndBottomWrap)
            rowFailures.Add("drawing-object evidence expects top-and-bottom wrapping but the object plan records none");
        if (tags.Contains("z-order", StringComparer.OrdinalIgnoreCase) && !objects.HasZOrder)
            rowFailures.Add("drawing-object evidence expects z-order depth but the object plan records a single layer");
    }

    private static void ValidateChartSmartArtFeatureTags(
        FreeWVisualEvidenceRow row,
        List<string> rowFailures)
    {
        var tags = row.ExpectedFeatureTags;
        var chartSmartArt = row.PageExpectation.ChartSmartArt;
        if (!tags.Contains("chart-smartart", StringComparer.OrdinalIgnoreCase))
            return;

        if (tags.Contains("charts", StringComparer.OrdinalIgnoreCase) && chartSmartArt.ChartCount <= 0)
            rowFailures.Add("chart/SmartArt evidence expects charts but the page expectation records none");
        if (tags.Contains("smartart", StringComparer.OrdinalIgnoreCase) && chartSmartArt.SmartArtCount <= 0)
            rowFailures.Add("chart/SmartArt evidence expects SmartArt but the page expectation records none");
        if (tags.Contains("chart-palette", StringComparer.OrdinalIgnoreCase) && !chartSmartArt.HasChartPalette)
            rowFailures.Add("chart/SmartArt evidence expects chart palettes but the chart plan records none");
        if (tags.Contains("quick-layout", StringComparer.OrdinalIgnoreCase) && !chartSmartArt.HasChartQuickLayout)
            rowFailures.Add("chart/SmartArt evidence expects a chart quick layout but the chart plan records none");
        if (tags.Contains("scatter-markers", StringComparer.OrdinalIgnoreCase) && !chartSmartArt.HasMarkerOnlyScatter)
            rowFailures.Add("chart/SmartArt evidence expects marker-only scatter geometry but the chart plan records none");
        if (tags.Contains("chart-legend", StringComparer.OrdinalIgnoreCase) && !chartSmartArt.HasLegend)
            rowFailures.Add("chart/SmartArt evidence expects chart legends but the chart plan records none");
        if (tags.Contains("chart-gridlines", StringComparer.OrdinalIgnoreCase) && !chartSmartArt.HasGridlines)
            rowFailures.Add("chart/SmartArt evidence expects chart gridlines but the chart plan records none");
        if (tags.Contains("data-labels", StringComparer.OrdinalIgnoreCase) && !chartSmartArt.HasDataLabels)
            rowFailures.Add("chart/SmartArt evidence expects chart data labels but the chart plan records none");
        if (tags.Contains("axis-titles", StringComparer.OrdinalIgnoreCase) && !chartSmartArt.HasAxisTitles)
            rowFailures.Add("chart/SmartArt evidence expects chart axis titles but the chart plan records none");
        if (tags.Contains("plot-area-fill", StringComparer.OrdinalIgnoreCase) && !chartSmartArt.HasPlotAreaFill)
            rowFailures.Add("chart/SmartArt evidence expects chart plot-area fill but the chart plan records none");
        if (tags.Contains("smartart-layout", StringComparer.OrdinalIgnoreCase) && !chartSmartArt.HasSmartArtLayout)
            rowFailures.Add("chart/SmartArt evidence expects SmartArt layout metadata but the SmartArt plan records none");
        if (tags.Contains("smartart-colors", StringComparer.OrdinalIgnoreCase) && !chartSmartArt.HasSmartArtColorScheme)
            rowFailures.Add("chart/SmartArt evidence expects SmartArt color scheme metadata but the SmartArt plan records none");
        if (tags.Contains("smartart-style", StringComparer.OrdinalIgnoreCase) && !chartSmartArt.HasSmartArtStyle)
            rowFailures.Add("chart/SmartArt evidence expects SmartArt style metadata but the SmartArt plan records none");
        if (chartSmartArt.SmartArtCount > 0 && chartSmartArt.SmartArtNodeCount <= 0)
            rowFailures.Add("chart/SmartArt evidence includes SmartArt but records no nodes");
    }

    private static IReadOnlyList<FreeWVisualEvidenceNormalizedScenario> BuildScenarioSummaries(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows,
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario> expected,
        IReadOnlyDictionary<string, FreeWVisualEvidenceExpectedScenario> expectedByKey,
        List<string> failures)
    {
        var keys = rows
            .Select(r => ScenarioKey(r.HostId, r.ScenarioId))
            .Concat(expected.Select(e => ScenarioKey(e.HostId, e.ScenarioId)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var scenarios = new List<FreeWVisualEvidenceNormalizedScenario>();
        foreach (var key in keys)
        {
            var keyParts = key.Split('\u001f');
            var hostId = keyParts[0];
            var scenarioId = keyParts.Length > 1 ? keyParts[1] : string.Empty;
            var scenarioRows = rows
                .Where(r => string.Equals(r.HostId, hostId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var scenarioFailures = scenarioRows
                .SelectMany(r => r.Trust.Failures)
                .ToList();
            var expectedScenario = expectedByKey.TryGetValue(key, out var item) ? item : null;
            var minimumExpectedOutputs = expectedScenario?.MinimumExpectedOutputs ?? 0;
            var trustedOutputs = scenarioRows.Count(r => r.Trust.Passed);
            if (expectedScenario is not null && trustedOutputs < minimumExpectedOutputs)
            {
                scenarioFailures.Add(
                    $"expected at least {minimumExpectedOutputs.ToString(CultureInfo.InvariantCulture)} trusted output(s), found {trustedOutputs.ToString(CultureInfo.InvariantCulture)}");
            }

            if (scenarioFailures.Count > 0)
            {
                foreach (var failure in scenarioFailures)
                    failures.Add($"{hostId}/{scenarioId}: {failure}");
            }

            scenarios.Add(new FreeWVisualEvidenceNormalizedScenario(
                hostId,
                scenarioId,
                minimumExpectedOutputs,
                scenarioRows.Count,
                trustedOutputs,
                expectedScenario is not null,
                new FreeWVisualEvidenceTrust(scenarioFailures.Count == 0, scenarioFailures)));
        }

        return scenarios;
    }

    private static void ValidateBackstageRendererPairs(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows,
        List<string> failures)
    {
        foreach (var scenarioId in BackstageRendererScenarioIds)
        {
            var wpfRows = TrustedRowsForHostScenario(rows, WpfHostId, scenarioId);
            var avaloniaRows = TrustedRowsForHostScenario(rows, AvaloniaHostId, scenarioId);
            if (wpfRows.Count == 0 || avaloniaRows.Count == 0)
                continue;

            ValidateUniquePages(scenarioId, WpfHostId, wpfRows, failures);
            ValidateUniquePages(scenarioId, AvaloniaHostId, avaloniaRows, failures);

            var wpfPages = wpfRows.Select(r => r.PageNumber).Distinct().Order().ToList();
            var avaloniaPages = avaloniaRows.Select(r => r.PageNumber).Distinct().Order().ToList();
            var requiredPages = RequiredScenarioPages(scenarioId);
            var missingAvaloniaPages = requiredPages.Except(avaloniaPages).ToList();
            var missingWpfPages = requiredPages.Except(wpfPages).ToList();
            if (missingAvaloniaPages.Count > 0)
            {
                failures.Add(
                    $"backstage renderer pair '{scenarioId}' is missing Avalonia page(s): {FormatPages(missingAvaloniaPages)}");
            }

            if (missingWpfPages.Count > 0)
            {
                failures.Add(
                    $"backstage renderer pair '{scenarioId}' is missing WPF page(s): {FormatPages(missingWpfPages)}");
            }

            foreach (var pageNumber in wpfPages.Intersect(avaloniaPages))
            {
                var wpf = wpfRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                var avalonia = avaloniaRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                if (wpf is null || avalonia is null)
                    continue;

                ValidateRendererPairRow("backstage renderer pair", scenarioId, pageNumber, wpf, avalonia, failures);
            }
        }
    }

    private static void ValidateSectionGeometryRendererPairs(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows,
        List<string> failures)
    {
        foreach (var scenarioId in SectionGeometryRendererScenarioIds)
        {
            var wpfRows = TrustedRowsForHostScenario(rows, WpfHostId, scenarioId);
            var avaloniaRows = TrustedRowsForHostScenario(rows, AvaloniaHostId, scenarioId);
            if (wpfRows.Count == 0 || avaloniaRows.Count == 0)
                continue;

            ValidateUniquePages(scenarioId, WpfHostId, wpfRows, failures);
            ValidateUniquePages(scenarioId, AvaloniaHostId, avaloniaRows, failures);

            var wpfPages = wpfRows.Select(r => r.PageNumber).Distinct().Order().ToList();
            var avaloniaPages = avaloniaRows.Select(r => r.PageNumber).Distinct().Order().ToList();
            var requiredPages = RequiredScenarioPages(scenarioId);
            var missingAvaloniaPages = requiredPages.Except(avaloniaPages).ToList();
            var missingWpfPages = requiredPages.Except(wpfPages).ToList();
            if (missingAvaloniaPages.Count > 0)
            {
                failures.Add(
                    $"section-geometry renderer pair '{scenarioId}' is missing Avalonia page(s): {FormatPages(missingAvaloniaPages)}");
            }

            if (missingWpfPages.Count > 0)
            {
                failures.Add(
                    $"section-geometry renderer pair '{scenarioId}' is missing WPF page(s): {FormatPages(missingWpfPages)}");
            }

            foreach (var pageNumber in wpfPages.Intersect(avaloniaPages))
            {
                var wpf = wpfRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                var avalonia = avaloniaRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                if (wpf is null || avalonia is null)
                    continue;

                ValidateRendererPairRow("section-geometry renderer pair", scenarioId, pageNumber, wpf, avalonia, failures);
                ValidateSectionPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
            }
        }
    }

    private static void ValidateReviewRendererPairs(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows,
        List<string> failures)
    {
        foreach (var scenarioId in ReviewRendererScenarioIds)
        {
            var wpfRows = TrustedRowsForHostScenario(rows, WpfHostId, scenarioId);
            var avaloniaRows = TrustedRowsForHostScenario(rows, AvaloniaHostId, scenarioId);
            if (wpfRows.Count == 0 || avaloniaRows.Count == 0)
                continue;

            ValidateUniquePages(scenarioId, WpfHostId, wpfRows, failures);
            ValidateUniquePages(scenarioId, AvaloniaHostId, avaloniaRows, failures);

            var wpfPages = wpfRows.Select(r => r.PageNumber).Distinct().Order().ToList();
            var avaloniaPages = avaloniaRows.Select(r => r.PageNumber).Distinct().Order().ToList();
            var requiredPages = RequiredScenarioPages(scenarioId);
            var missingAvaloniaPages = requiredPages.Except(avaloniaPages).ToList();
            var missingWpfPages = requiredPages.Except(wpfPages).ToList();
            if (missingAvaloniaPages.Count > 0)
            {
                failures.Add(
                    $"review renderer pair '{scenarioId}' is missing Avalonia page(s): {FormatPages(missingAvaloniaPages)}");
            }

            if (missingWpfPages.Count > 0)
            {
                failures.Add(
                    $"review renderer pair '{scenarioId}' is missing WPF page(s): {FormatPages(missingWpfPages)}");
            }

            foreach (var pageNumber in wpfPages.Intersect(avaloniaPages))
            {
                var wpf = wpfRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                var avalonia = avaloniaRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                if (wpf is null || avalonia is null)
                    continue;

                ValidateRendererPairRow("review renderer pair", scenarioId, pageNumber, wpf, avalonia, failures);
            }
        }
    }

    private static IReadOnlyList<int> RequiredScenarioPages(string scenarioId)
    {
        var minimumOutputs = Math.Max(1, FreeWVisualEvidencePlanner.ResolveScenario(scenarioId).MinimumExpectedOutputs);
        return Enumerable.Range(1, minimumOutputs).ToList();
    }

    private static List<FreeWVisualEvidenceNormalizedRow> TrustedRowsForHostScenario(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows,
        string hostId,
        string scenarioId) =>
        rows
            .Where(r => string.Equals(r.HostId, hostId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(r.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.Trust.Passed)
            .ToList();

    private static void ValidateUniquePages(
        string scenarioId,
        string hostId,
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows,
        List<string> failures)
    {
        foreach (var group in rows.GroupBy(r => r.PageNumber).Where(g => g.Count() > 1))
        {
            failures.Add(
                $"{hostId}/{scenarioId}: duplicate page {group.Key.ToString(CultureInfo.InvariantCulture)} evidence rows: " +
                string.Join(", ", group.Select(r => r.OutputName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)));
        }
    }

    private static void ValidateRendererPairRow(
        string pairLabel,
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia,
        List<string> failures)
    {
        var pairName = $"{pairLabel} '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        if (!string.Equals(wpf.LayoutKind, avalonia.LayoutKind, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"{pairName} layout kinds differ: WPF '{wpf.LayoutKind}', Avalonia '{avalonia.LayoutKind}'");
        }

        if (!string.Equals(wpf.ExpectedOutputName, avalonia.ExpectedOutputName, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"{pairName} expected output names differ: WPF '{wpf.ExpectedOutputName}', Avalonia '{avalonia.ExpectedOutputName}'");
        }
    }

    private static void ValidateSectionPairRow(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia,
        List<string> failures)
    {
        var pairName = $"section-geometry renderer pair '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        if (wpf.PageFeatures.Section.SectionOrdinal != avalonia.PageFeatures.Section.SectionOrdinal)
        {
            failures.Add(
                $"{pairName} section ordinals differ: WPF '{wpf.PageFeatures.Section.SectionOrdinal.ToString(CultureInfo.InvariantCulture)}', Avalonia '{avalonia.PageFeatures.Section.SectionOrdinal.ToString(CultureInfo.InvariantCulture)}'");
        }

        if (wpf.PageFeatures.Section.SectionRelativePageNumber != avalonia.PageFeatures.Section.SectionRelativePageNumber)
        {
            failures.Add(
                $"{pairName} section-relative page numbers differ: WPF '{wpf.PageFeatures.Section.SectionRelativePageNumber.ToString(CultureInfo.InvariantCulture)}', Avalonia '{avalonia.PageFeatures.Section.SectionRelativePageNumber.ToString(CultureInfo.InvariantCulture)}'");
        }

        if (!string.Equals(wpf.PageFeatures.Section.OwnerId, avalonia.PageFeatures.Section.OwnerId, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add(
                $"{pairName} section owner ids differ: WPF '{wpf.PageFeatures.Section.OwnerId}', Avalonia '{avalonia.PageFeatures.Section.OwnerId}'");
        }
    }

    private static string FormatPages(IEnumerable<int> pageNumbers) =>
        string.Join(
            ", ",
            pageNumbers
                .Order()
                .Select(p => "p" + p.ToString(CultureInfo.InvariantCulture)));

    private static string ScenarioKey(string hostId, string scenarioId) =>
        string.Concat(hostId, "\u001f", scenarioId);

    private static string ResolveOutputPath(string outputPath, string manifestDirectory)
    {
        if (Path.IsPathRooted(outputPath))
            return Path.GetFullPath(outputPath);

        return Path.GetFullPath(Path.Combine(manifestDirectory, outputPath));
    }

    private static string NormalizeRelativePath(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative.Replace('\\', '/');
    }

    private static bool IsSubPathOf(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(fullRoot, fullPath, StringComparison.OrdinalIgnoreCase))
            return true;

        var rootWithSeparator = fullRoot + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string FeatureSummary(FreeWVisualEvidenceNormalizedRow row)
    {
        var features = row.PageFeatures;
        var parts = new List<string>
        {
            features.Section.OwnerId,
            features.Columns.Count > 1
                ? $"{features.Columns.Count.ToString(CultureInfo.InvariantCulture)} columns"
                : "1 column"
        };

        if (features.PageBorder.Present)
            parts.Add("page border");
        if (features.Watermark.Present)
            parts.Add("watermark");
        if (row.Tables.TableCount > 0)
        {
            parts.Add(
                $"{row.Tables.TableCount.ToString(CultureInfo.InvariantCulture)} table(s), " +
                $"{row.Tables.MaxGridColumnCount.ToString(CultureInfo.InvariantCulture)} grid column(s)");
        }
        if (row.DrawingObjects.FloatingObjectCount > 0)
        {
            parts.Add(
                $"{row.DrawingObjects.FloatingObjectCount.ToString(CultureInfo.InvariantCulture)} drawing object(s), " +
                $"{row.DrawingObjects.BehindTextCount.ToString(CultureInfo.InvariantCulture)} behind text");
        }
        if (row.ChartSmartArt.ChartCount > 0 || row.ChartSmartArt.SmartArtCount > 0)
        {
            parts.Add(
                $"{row.ChartSmartArt.ChartCount.ToString(CultureInfo.InvariantCulture)} chart(s), " +
                $"{row.ChartSmartArt.SmartArtCount.ToString(CultureInfo.InvariantCulture)} SmartArt");
        }

        return string.Join(", ", parts);
    }

    private static string FormatBaselineStatusCounts(
        IReadOnlyList<FreeWVisualBaselineComparison> comparisons) =>
        string.Join(
            ", ",
            comparisons
                .GroupBy(c => c.Status, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => $"{g.Key}={g.Count().ToString(CultureInfo.InvariantCulture)}"));

    private static string FormatBaselinePath(FreeWVisualBaselineComparison comparison)
    {
        if (!string.IsNullOrWhiteSpace(comparison.BaselinePath))
            return comparison.BaselinePath;

        if (comparison.CandidateBaselinePaths.Count > 0)
            return string.Join(", ", comparison.CandidateBaselinePaths);

        return "-";
    }

    private static string FormatChangedPixels(FreeWVisualBaselineComparisonMetrics? metrics)
    {
        if (metrics is null)
            return "-";

        return string.Concat(
            metrics.ChangedPixels.ToString(CultureInfo.InvariantCulture),
            "/",
            metrics.ComparedPixels.ToString(CultureInfo.InvariantCulture),
            " (",
            metrics.ChangedPixelRatio.ToString("P3", CultureInfo.InvariantCulture),
            ")");
    }

    private static string FormatToleranceLimits(FreeWVisualBaselineComparisonTolerance tolerance) =>
        string.Concat(
            "pixel delta > ",
            tolerance.ChangedPixelDeltaThreshold.ToString(CultureInfo.InvariantCulture),
            "; mean <= ",
            tolerance.MaxMeanAbsoluteChannelDelta.ToString("0.####", CultureInfo.InvariantCulture),
            "; gray <= ",
            tolerance.MaxMeanAbsoluteGrayscaleDelta.ToString("0.####", CultureInfo.InvariantCulture),
            "; changed <= ",
            tolerance.MaxChangedPixelRatio.ToString("P3", CultureInfo.InvariantCulture),
            "; dimensions ",
            tolerance.RequireDimensionMatch ? "must match" : "may resize");

    private static string FormatComparisonNotes(FreeWVisualBaselineComparison comparison)
    {
        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(comparison.SkipReason))
            notes.Add(comparison.SkipReason);
        notes.AddRange(comparison.Trust.Failures);

        return notes.Count == 0
            ? "-"
            : string.Join("; ", notes.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string EscapeMarkdown(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal);

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
