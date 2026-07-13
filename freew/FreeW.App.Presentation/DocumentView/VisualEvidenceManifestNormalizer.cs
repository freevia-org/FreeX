using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FreeW.Core.Model;

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
    IReadOnlyDictionary<string, string> HostMetadata,
    int PageNumber,
    int PageCount,
    string LayoutKind,
    string ExpectedOutputName,
    FreeWVisualPageFeatureExpectation PageFeatures,
    FreeWVisualTableExpectation Tables,
    FreeWVisualDrawingObjectExpectation DrawingObjects,
    FreeWVisualChartSmartArtExpectation ChartSmartArt,
    FreeWVisualFieldExpectation Fields,
    FreeWVisualHeaderFooterExpectation HeaderFooters,
    FreeWVisualTableOfAuthoritiesExpectation TableOfAuthorities,
    FreeWVisualProofingDiagnosticExpectation ProofingDiagnostics,
    FreeWVisualReviewProtectionExpectation ReviewProtection,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualEvidenceNormalizedSummary(
    string SchemaId,
    int SchemaVersion,
    IReadOnlyList<FreeWVisualEvidenceNormalizedSource> Sources,
    IReadOnlyList<FreeWVisualEvidenceExpectedScenario> ExpectedScenarios,
    IReadOnlyList<FreeWVisualEvidenceNormalizedScenario> Scenarios,
    IReadOnlyList<FreeWVisualEvidenceNormalizedRow> Evidence,
    IReadOnlyList<FreeWVisualEvidenceBackstagePrintReadiness> BackstagePrintEvidenceReadiness,
    IReadOnlyList<FreeWVisualBaselineComparison> BaselineComparisons,
    IReadOnlyList<FreeWVisualBaselineTriageItem> WordBaselineTriage,
    IReadOnlyList<FreeWVisualRemainingEvidenceBlocker> RemainingEvidenceBlockers,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualEvidenceBackstagePrintReadiness(
    string ScenarioId,
    string HostId,
    int PageNumber,
    string Status,
    string OutputSummary,
    string Notes);

public sealed record FreeWVisualBaselineTriageItem(
    string HostId,
    string ScenarioId,
    int PageNumber,
    string OutputName,
    string Status,
    string TriageStatus,
    string BaselineId,
    string BaselinePathSummary,
    long? ChangedPixels,
    long? ComparedPixels,
    double? ChangedPixelRatio,
    double? MeanAbsoluteChannelDelta,
    double? MeanAbsoluteGrayscaleDelta,
    string ToleranceSummary,
    string Note);

public sealed record FreeWVisualRemainingEvidenceBlocker(
    string BlockerId,
    string ScenarioId,
    string Area,
    string Status,
    string RequiredEvidence,
    string Reason,
    IReadOnlyList<string> RelatedBaselineStatuses,
    IReadOnlyList<string> CandidateBaselinePaths,
    IReadOnlyList<string> SemanticEvidence,
    bool RequiresWordBaseline,
    FreeWVisualEvidenceTrust Trust);

public static class FreeWVisualEvidenceManifestNormalizer
{
    public const string SummarySchemaId = "freew.visual-evidence-summary.v1";
    public const int SummarySchemaVersion = 25;
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
    public static IReadOnlyList<string> SectionPageSurfaceRendererScenarioIds { get; } =
    [
        "f2-section-landscape",
        "f2-hf-images"
    ];
    public static IReadOnlyList<string> ReviewRendererScenarioIds { get; } =
    [
        "f2-tracked-changes",
        "f2-comments",
        "review-proofing-visual-depth",
        "review-protection-proofing-comments-only"
    ];
    public static IReadOnlyList<string> TableRendererScenarioIds { get; } =
    [
        "table-layout-complex",
        "table-pagination-repeat-header",
        "table-page-composition-stress"
    ];
    public static IReadOnlyList<string> DrawingObjectRendererScenarioIds { get; } =
    [
        "drawing-objects-complex",
        "object-format-position-size-style",
        "wordart-watermark-stress"
    ];
    public static IReadOnlyList<string> GroupedChildEffectRendererScenarioIds { get; } =
    [
        "drawing-objects-complex"
    ];
    public static IReadOnlyList<string> WordArtWatermarkRendererScenarioIds { get; } =
    [
        "wordart-picture-watermark-layout"
    ];
    public static IReadOnlyList<string> ChartSmartArtRendererScenarioIds { get; } =
    [
        "chart-smartart-complex"
    ];
    public static IReadOnlyList<string> FieldRendererScenarioIds { get; } =
    [
        "field-page-number-variants",
        "references-heavy-fields"
    ];
    public static IReadOnlyList<string> EquationRendererScenarioIds { get; } =
    [
        "equation-structures"
    ];
    public static IReadOnlyList<string> HeaderFooterRendererScenarioIds { get; } =
    [
        "f2-hf-basic",
        "f2-hf-firstpage",
        "f2-hf-oddeven",
        "f2-hf-images",
        "field-page-number-variants",
        "backstage-print-preview-fidelity",
        "backstage-pdf-export-fidelity"
    ];

    private static readonly string[] ReferencesHeavyRequiredComplexFieldKeywords =
    [
        "BIBLIOGRAPHY",
        "CITATION",
        "TOA"
    ];

    private static readonly string[] ReferencesHeavyRequiredToaCategories =
    [
        "Cases",
        "Statutes"
    ];

    private static readonly string[] ReferencesHeavyRequiredToaPageReferenceSignatures =
    [
        "category=Cases|entry=Example v. FreeW, 123 F.4th 456 (2026)|kind=explicit-page-numbers|pages=1,2|text=1, 2",
        "category=Statutes|entry=Free Software Evidence Act, 42 U.S.C. 2026|kind=explicit-page-numbers|pages=1|text=1"
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
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario>? expectedScenarios = null,
        IReadOnlyCollection<string>? includedScenarioIds = null)
    {
        ArgumentNullException.ThrowIfNull(manifestPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(runRoot);

        if (manifestPaths.Count == 0)
            throw new ArgumentException("At least one visual evidence manifest is required.", nameof(manifestPaths));

        var normalizedRoot = Path.GetFullPath(runRoot);
        var included = includedScenarioIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expected = (expectedScenarios ?? DefaultExpectedScenarios)
            .Where(e => included is null || included.Count == 0 || included.Contains(e.ScenarioId))
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

            var manifestRows = manifest.Evidence
                .Where(e => included is null || included.Count == 0 || included.Contains(e.ScenarioId))
                .ToList();

            var hostIds = manifestRows
                .Select(e => e.HostId)
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)
                .ToList();
            sources.Add(new FreeWVisualEvidenceNormalizedSource(
                sourceManifestPath,
                hostIds,
                manifestRows.Count));

            var manifestDirectory = Path.GetDirectoryName(fullManifestPath) ?? normalizedRoot;
            foreach (var row in manifestRows)
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
        ValidateFieldRendererPairs(rows, failures);
        ValidateEquationRendererPairs(rows, failures);
        ValidateHeaderFooterRendererPairs(rows, failures);
        ValidateTableRendererPairs(rows, failures);
        ValidateDrawingObjectRendererPairs(rows, failures);
        ValidateWordArtWatermarkRendererPairs(rows, failures);
        ValidateChartSmartArtRendererPairs(rows, failures);
        var orderedRows = rows
            .OrderBy(r => r.HostId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.PageNumber)
            .ThenBy(r => r.OutputName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var summaryTrust = new FreeWVisualEvidenceTrust(failures.Count == 0, failures);
        var backstageReadiness = BuildBackstagePrintEvidenceReadinessRows(expected, orderedRows);
        var summary = new FreeWVisualEvidenceNormalizedSummary(
            SummarySchemaId,
            SummarySchemaVersion,
            sources
                .OrderBy(s => s.ManifestPath, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            expected,
            scenarios,
            orderedRows,
            backstageReadiness,
            [],
            [],
            [],
            summaryTrust);
        return summary with
        {
            RemainingEvidenceBlockers = BuildRemainingEvidenceBlockers(summary, [])
        };
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

        AppendBackstagePrintEvidenceReadiness(sb, summary);

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
            sb.AppendLine("## Word Baseline Triage");
            sb.AppendLine();
            AppendWordBaselineTriage(
                sb,
                summary.WordBaselineTriage.Count > 0
                    ? summary.WordBaselineTriage
                    : BuildWordBaselineTriage(summary.BaselineComparisons));

            sb.AppendLine();
            sb.AppendLine("## Word Baseline Comparison");
            sb.AppendLine();
            sb.AppendLine($"Status counts: {EscapeMarkdown(FormatBaselineStatusCounts(summary.BaselineComparisons))}");
            sb.AppendLine($"Evidence class counts: {EscapeMarkdown(FormatBaselineEvidenceClassCounts(summary.BaselineComparisons))}");
            sb.AppendLine($"Evidence class legend: {EscapeMarkdown(FormatBaselineEvidenceClassLegend(summary.BaselineComparisons))}");
            sb.AppendLine();
            sb.AppendLine("| Host | Scenario | Output | Baseline ID | Baseline Path | Status | Evidence Class | Size | Mean Channel Delta | Mean Gray Delta | Changed Pixels | Tolerance | Limits | Notes |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | --- | --- | --- |");
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
                    $"{EscapeMarkdown(comparison.BaselineEvidenceClass)} | " +
                    $"{EscapeMarkdown(size)} | {meanChannel} | {meanGray} | {FormatChangedPixels(metrics)} | " +
                    $"{EscapeMarkdown(comparison.Tolerance.Name)} | " +
                    $"{EscapeMarkdown(FormatToleranceLimits(comparison.Tolerance))} | " +
                    $"{EscapeMarkdown(FormatComparisonNotes(comparison))} |");
            }
        }

        AppendRemainingEvidenceBlockers(sb, summary);

        return sb.ToString();
    }

    private static void AppendBackstagePrintEvidenceReadiness(
        StringBuilder sb,
        FreeWVisualEvidenceNormalizedSummary summary)
    {
        if (summary.BackstagePrintEvidenceReadiness.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## Backstage Print Evidence Readiness");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Host | Page | Status | Output | Notes |");
        sb.AppendLine("| --- | --- | ---: | --- | --- | --- |");
        foreach (var row in summary.BackstagePrintEvidenceReadiness)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(row.ScenarioId)} | {EscapeMarkdown(row.HostId)} | " +
                $"{row.PageNumber.ToString(CultureInfo.InvariantCulture)} | " +
                $"{EscapeMarkdown(row.Status)} | " +
                $"{EscapeMarkdown(row.OutputSummary)} | " +
                $"{EscapeMarkdown(row.Notes)} |");
        }
    }

    private static void AppendRemainingEvidenceBlockers(
        StringBuilder sb,
        FreeWVisualEvidenceNormalizedSummary summary)
    {
        if (summary.RemainingEvidenceBlockers.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## Remaining Evidence Blockers");
        sb.AppendLine();
        sb.AppendLine("| Blocker | Scenario | Area | Status | Required Evidence | Reason | Semantic Evidence | Word Baseline Required | Related Baseline Statuses | Candidate Baselines | Trust |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var blocker in summary.RemainingEvidenceBlockers)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(blocker.BlockerId)} | " +
                $"{EscapeMarkdown(blocker.ScenarioId)} | " +
                $"{EscapeMarkdown(blocker.Area)} | " +
                $"{EscapeMarkdown(blocker.Status)} | " +
                $"{EscapeMarkdown(blocker.RequiredEvidence)} | " +
                $"{EscapeMarkdown(blocker.Reason)} | " +
                $"{EscapeMarkdown(FormatBlockerList(blocker.SemanticEvidence))} | " +
                $"{(blocker.RequiresWordBaseline ? "yes" : "no")} | " +
                $"{EscapeMarkdown(FormatBlockerList(blocker.RelatedBaselineStatuses))} | " +
                $"{EscapeMarkdown(FormatBlockerList(blocker.CandidateBaselinePaths))} | " +
                $"{(blocker.Trust.Passed ? "passed" : "failed")} |");
        }
    }

    private static string FormatBlockerList(IReadOnlyList<string> values) =>
        values.Count == 0
            ? "-"
            : string.Join(", ", values);

    private static IReadOnlyList<FreeWVisualEvidenceBackstagePrintReadiness> BuildBackstagePrintEvidenceReadinessRows(
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario> expectedScenarios,
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> evidence)
    {
        var rows = new List<FreeWVisualEvidenceBackstagePrintReadiness>();
        var hosts = new[] { WpfHostId, AvaloniaHostId };
        foreach (var scenarioId in BackstageRendererScenarioIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            var expectedHosts = expectedScenarios
                .Where(expected => string.Equals(expected.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .Select(expected => expected.HostId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(hostId => Array.IndexOf(hosts, hostId) < 0 ? int.MaxValue : Array.IndexOf(hosts, hostId))
                .ThenBy(hostId => hostId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var evidenceHosts = evidence
                .Where(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .Select(row => row.HostId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(hostId => Array.IndexOf(hosts, hostId) < 0 ? int.MaxValue : Array.IndexOf(hosts, hostId))
                .ThenBy(hostId => hostId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (expectedHosts.Count == 0)
            {
                if (evidenceHosts.Count == 0)
                    continue;

                expectedHosts.AddRange(evidenceHosts);
            }

            var pages = RequiredScenarioPages(scenarioId);
            foreach (var hostId in expectedHosts)
            {
                foreach (var pageNumber in pages)
                {
                    var pageRows = evidence
                        .Where(row =>
                            string.Equals(row.HostId, hostId, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase) &&
                            row.PageNumber == pageNumber)
                        .OrderBy(row => row.OutputName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (pageRows.Count == 0)
                    {
                        rows.Add(new FreeWVisualEvidenceBackstagePrintReadiness(
                            scenarioId,
                            hostId,
                            pageNumber,
                            "missing",
                            "-",
                            "no normalized row"));
                        continue;
                    }

                    var trusted = pageRows.Where(row => row.Trust.Passed).ToList();
                    var outputSummary = string.Join(", ", pageRows.Select(row => row.OutputPath));
                    if (trusted.Count > 0)
                    {
                        rows.Add(new FreeWVisualEvidenceBackstagePrintReadiness(
                            scenarioId,
                            hostId,
                            pageNumber,
                            "trusted",
                            outputSummary,
                            trusted.Count == pageRows.Count ? "ready" : "trusted row present; failing duplicate also present"));
                        continue;
                    }

                    var notes = string.Join(
                        "; ",
                        pageRows
                            .SelectMany(row => row.Trust.Failures)
                            .Distinct(StringComparer.OrdinalIgnoreCase));
                    rows.Add(new FreeWVisualEvidenceBackstagePrintReadiness(
                        scenarioId,
                        hostId,
                        pageNumber,
                        "failed",
                        outputSummary,
                        string.IsNullOrWhiteSpace(notes) ? "row is not trusted" : notes));
                }
            }
        }

        return rows;
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
            WordBaselineTriage = BuildWordBaselineTriage(ordered),
            RemainingEvidenceBlockers = BuildRemainingEvidenceBlockers(summary, ordered),
            Trust = new FreeWVisualEvidenceTrust(failures.Count == 0, failures)
        };
    }

    public static IReadOnlyList<FreeWVisualBaselineTriageItem> BuildWordBaselineTriage(
        IReadOnlyList<FreeWVisualBaselineComparison> comparisons)
    {
        ArgumentNullException.ThrowIfNull(comparisons);

        return comparisons
            .Select(BuildWordBaselineTriageItem)
            .OrderBy(item => WordBaselineTriageStatusPriority(item.Status))
            .ThenByDescending(item => item.ChangedPixelRatio ?? -1)
            .ThenBy(item => item.HostId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.PageNumber)
            .ThenBy(item => item.OutputName, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
            else if (FieldRendererScenarioIds.Contains(scenario.ScenarioId, StringComparer.OrdinalIgnoreCase))
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
            else if (EquationRendererScenarioIds.Contains(scenario.ScenarioId, StringComparer.OrdinalIgnoreCase))
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
            else if (HeaderFooterRendererScenarioIds.Contains(scenario.ScenarioId, StringComparer.OrdinalIgnoreCase)
                && scenario.ExpectedFeatureTags.Contains("header-footer-images", StringComparer.OrdinalIgnoreCase))
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

    private static IReadOnlyList<FreeWVisualRemainingEvidenceBlocker> BuildRemainingEvidenceBlockers(
        FreeWVisualEvidenceNormalizedSummary summary,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var blockers = new List<FreeWVisualRemainingEvidenceBlocker>();
        blockers.AddRange(BuildBackstageRealCaptureBlockers(summary));

        var rows = summary.Evidence
            .Where(row => string.Equals(row.ScenarioId, "references-heavy-fields", StringComparison.OrdinalIgnoreCase))
            .Where(row => row.Trust.Passed)
            .ToList();
        if (rows.Count == 0)
            return blockers;

        var semanticEvidence = BuildReferencesHeavyToaSemanticEvidence(rows);
        if (semanticEvidence.Count == 0)
        {
            blockers.Add(
                BuildReferencesHeavyToaBlocker(
                    "semantic-toa-page-references-missing",
                    "trusted references-heavy rows with generated Table of Authorities page-reference metadata",
                    "trusted references-heavy evidence did not record generated TOA page references; regenerate current-schema evidence or fix shared TOA generation before treating this as a Word-baseline-only gap",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: false));
            return blockers;
        }

        var related = baselineComparisons
            .Where(comparison => string.Equals(comparison.ScenarioId, "references-heavy-fields", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (related.Count == 0)
        {
            blockers.Add(
                BuildReferencesHeavyToaBlocker(
                    "needs-word-baseline-run",
                    "real MS Word PNG comparisons for references-heavy Table of Authorities page numbers",
                    "semantic generated TOA page references are present in trusted FreeW evidence; run a Word-baseline comparison for references-heavy-fields to prove Word visual parity",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: true));
            return blockers;
        }

        var statuses = related
            .Select(comparison => comparison.Status)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(status => status, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var candidates = related
            .SelectMany(comparison => comparison.CandidateBaselinePaths)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (related.Any(comparison =>
            string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus, StringComparison.OrdinalIgnoreCase)))
        {
            var reasons = related
                .Where(comparison => string.Equals(
                    comparison.Status,
                    FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
                    StringComparison.OrdinalIgnoreCase))
                .Select(FormatComparisonNotes)
                .Where(reason => !string.IsNullOrWhiteSpace(reason) && reason != "-")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(reason => reason, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var reason = reasons.Count == 0
                ? "MS Word baseline PNG generation was unavailable for references-heavy-fields"
                : string.Join("; ", reasons);
            blockers.Add(
                BuildReferencesHeavyToaBlocker(
                    FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
                    "real MS Word PNG comparisons for references-heavy Table of Authorities page numbers",
                    reason,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true));
            return blockers;
        }

        if (related.Any(comparison =>
            string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus, StringComparison.OrdinalIgnoreCase)))
        {
            blockers.Add(
                BuildReferencesHeavyToaBlocker(
                    FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus,
                    "real MS Word PNG comparisons for references-heavy Table of Authorities page numbers",
                    "semantic generated TOA page references are present in trusted FreeW evidence, but references-heavy Word baseline PNGs are missing for TOA page-number comparison",
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true));
            return blockers;
        }

        if (related.All(comparison =>
            string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.PassedStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return blockers;
        }

        blockers.Add(
            BuildReferencesHeavyToaBlocker(
                "needs-render-review",
                "render-review resolution for failed references-heavy Word PNG comparisons",
                "references-heavy Word baseline comparison did not fully pass; inspect TOA page-number rendering differences",
                statuses,
                candidates,
                semanticEvidence,
                requiresWordBaseline: false));
        return blockers;
    }

    private static IReadOnlyList<FreeWVisualRemainingEvidenceBlocker> BuildBackstageRealCaptureBlockers(
        FreeWVisualEvidenceNormalizedSummary summary)
    {
        if (summary.BackstagePrintEvidenceReadiness.Count == 0)
            return [];

        var blockers = new List<FreeWVisualRemainingEvidenceBlocker>();
        foreach (var scenarioGroup in summary.BackstagePrintEvidenceReadiness
            .GroupBy(row => row.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var missing = scenarioGroup
                .Where(row => !string.Equals(row.Status, "trusted", StringComparison.OrdinalIgnoreCase))
                .OrderBy(row => row.HostId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.PageNumber)
                .ToList();
            if (missing.Count == 0)
                continue;

            var semanticEvidence = scenarioGroup
                .OrderBy(row => row.HostId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.PageNumber)
                .Select(row => $"{row.HostId}/p{row.PageNumber.ToString(CultureInfo.InvariantCulture)}={row.Status}")
                .ToList();
            var missingSummary = string.Join(
                "; ",
                missing.Select(row =>
                    $"{row.HostId}/p{row.PageNumber.ToString(CultureInfo.InvariantCulture)} {row.Status}: {row.Notes}"));
            var scenarioLabel = scenarioGroup.Key switch
            {
                "backstage-print-preview-fidelity" => "Backstage print preview",
                "backstage-pdf-export-fidelity" => "Backstage PDF export",
                _ => scenarioGroup.Key
            };

            blockers.Add(new FreeWVisualRemainingEvidenceBlocker(
                $"backstage-real-captures-{scenarioGroup.Key}",
                scenarioGroup.Key,
                "Backstage print/export visual evidence",
                "missing-real-captures",
                $"trusted WPF and Avalonia real capture rows for {scenarioGroup.Key}",
                $"{scenarioLabel} has paired renderer contracts, but the visual-evidence summary is missing trusted real capture rows: {missingSummary}",
                [],
                [],
                semanticEvidence,
                false,
                new FreeWVisualEvidenceTrust(
                    false,
                    missing.Select(row =>
                        $"{row.HostId}/{row.ScenarioId}/p{row.PageNumber.ToString(CultureInfo.InvariantCulture)} is {row.Status}: {row.Notes}")
                        .ToList())));
        }

        return blockers;
    }

    private static FreeWVisualRemainingEvidenceBlocker BuildReferencesHeavyToaBlocker(
        string status,
        string requiredEvidence,
        string reason,
        IReadOnlyList<string> relatedBaselineStatuses,
        IReadOnlyList<string> candidateBaselinePaths,
        IReadOnlyList<string> semanticEvidence,
        bool requiresWordBaseline) =>
        new(
            "references-heavy-toa-page-number-fidelity",
            "references-heavy-fields",
            "TOA page-number fidelity",
            status,
            requiredEvidence,
            reason,
            relatedBaselineStatuses,
            candidateBaselinePaths,
            semanticEvidence,
            requiresWordBaseline,
            new FreeWVisualEvidenceTrust(true, []));

    private static IReadOnlyList<string> BuildReferencesHeavyToaSemanticEvidence(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows) =>
        rows
            .Where(row => HasReferencesHeavyToaPageReferenceSignatures(row.TableOfAuthorities))
            .SelectMany(row => row.TableOfAuthorities.PageReferenceSignatures.Select(signature =>
                string.Concat(
                    row.HostId,
                    "/p",
                    row.PageNumber.ToString(CultureInfo.InvariantCulture),
                    ": entries=",
                    row.TableOfAuthorities.EntryCount.ToString(CultureInfo.InvariantCulture),
                    "; categories=",
                    string.Join(",", row.TableOfAuthorities.Categories),
                    "; ",
                    signature)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool HasReferencesHeavyToaPageReferenceSignatures(
        FreeWVisualTableOfAuthoritiesExpectation tableOfAuthorities) =>
        ReferencesHeavyRequiredToaPageReferenceSignatures.All(signature =>
            tableOfAuthorities.PageReferenceSignatures.Contains(signature, StringComparer.Ordinal));

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
        var pageExpectation = row.PageExpectation with
        {
            Tables = FreeWVisualEvidencePlanner.NormalizeTableFillEvidence(row.PageExpectation.Tables)
        };
        var normalizedRow = row with { PageExpectation = pageExpectation };
        if (!string.Equals(row.OutputName, pageExpectation.ExpectedOutputName, StringComparison.OrdinalIgnoreCase))
            rowFailures.Add($"output name '{row.OutputName}' does not match expected '{pageExpectation.ExpectedOutputName}'");
        ValidateFeatureExpectations(normalizedRow, rowFailures);
        ValidateBackstageCaptureSource(normalizedRow, rowFailures);

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
            row.HostMetadata
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase),
            pageExpectation.PageNumber,
            pageExpectation.PageCount,
            pageExpectation.LayoutKind,
            pageExpectation.ExpectedOutputName,
            pageExpectation.Features,
            pageExpectation.Tables,
            pageExpectation.DrawingObjects,
            pageExpectation.ChartSmartArt,
            pageExpectation.Fields,
            pageExpectation.HeaderFooters ?? HeaderFooterVisualPlanner.EmptyExpectation,
            pageExpectation.TableOfAuthorities,
            pageExpectation.ProofingDiagnostics,
            pageExpectation.ReviewProtection,
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
        if (row.ExpectedFeatureTags.Contains("generated-toa-page-references", StringComparer.OrdinalIgnoreCase)
            && !row.PageExpectation.TableOfAuthorities.HasPageReferences)
        {
            rowFailures.Add(
                "scenario expects generated Table of Authorities page references but the page expectation records none");
        }
        ValidateReferencesHeavyTableOfAuthoritiesEvidence(row, rowFailures);
        if (composition.ExpectsTables && row.PageExpectation.Tables.TableCount <= 0)
            rowFailures.Add("scenario expects table layout but the page expectation records no tables");
        if (composition.ExpectsFloatingObjects && row.PageExpectation.DrawingObjects.FloatingObjectCount <= 0)
            rowFailures.Add("scenario expects floating objects but the page expectation records none");
        ValidateTableFeatureTags(row, rowFailures);
        ValidateWatermarkFeatureTags(row, rowFailures);
        ValidateDrawingObjectFeatureTags(row, rowFailures);
        ValidateChartSmartArtFeatureTags(row, rowFailures);
        ValidateFieldFeatureTags(row, rowFailures);
        ValidateHeaderFooterFeatureTags(row, rowFailures);
        ValidateProofingFeatureTags(row, rowFailures);
        ValidateReviewProtectionFeatureTags(row, rowFailures);
        if (features.Section.SectionOrdinal <= 0)
            rowFailures.Add("section ordinal must be positive");
        if (features.Section.SectionRelativePageNumber <= 0)
            rowFailures.Add("section-relative page number must be positive");
    }

    private static void ValidateHeaderFooterFeatureTags(
        FreeWVisualEvidenceRow row,
        List<string> rowFailures)
    {
        var tags = row.ExpectedFeatureTags;
        var headerFooters = row.PageExpectation.HeaderFooters ?? HeaderFooterVisualPlanner.EmptyExpectation;
        if (tags.Contains("header-footer-images", StringComparer.OrdinalIgnoreCase) && !headerFooters.HasImages)
        {
            rowFailures.Add(
                "scenario expects header/footer image evidence but the page expectation records no header/footer images");
        }
    }

    private static void ValidateReferencesHeavyTableOfAuthoritiesEvidence(
        FreeWVisualEvidenceRow row,
        List<string> rowFailures)
    {
        if (!string.Equals(row.ScenarioId, "references-heavy-fields", StringComparison.OrdinalIgnoreCase))
            return;

        var fields = row.PageExpectation.Fields;
        var toa = row.PageExpectation.TableOfAuthorities;

        foreach (var keyword in ReferencesHeavyRequiredComplexFieldKeywords)
        {
            if (!fields.ComplexFieldKeywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
            {
                rowFailures.Add(
                    $"references-heavy field evidence must include complex {keyword} field metadata");
            }
        }

        if (!fields.ComplexFieldResultSignatures.Contains("TOA=Cases\\t1, 2", StringComparer.OrdinalIgnoreCase))
        {
            rowFailures.Add(
                "references-heavy field evidence must include cached TOA page-reference sentinel 'TOA=Cases\\t1, 2'");
        }

        if (!toa.HasGeneratedTable || toa.EntryCount < 2)
        {
            rowFailures.Add(
                "references-heavy TOA evidence must include the shared generated Table of Authorities entries");
        }

        if (toa.EntryWithPageReferenceCount < 2)
        {
            rowFailures.Add(
                "references-heavy TOA evidence must include generated page references for both authority entries");
        }

        if (toa.CategoryCount < 2)
        {
            rowFailures.Add(
                "references-heavy TOA evidence must include distinct Cases and Statutes category labels");
        }

        foreach (var category in ReferencesHeavyRequiredToaCategories)
        {
            if (!toa.Categories.Contains(category, StringComparer.OrdinalIgnoreCase))
            {
                rowFailures.Add(
                    $"references-heavy TOA evidence is missing category label '{category}'");
            }
        }

        if (!toa.HasExplicitPageNumbers)
        {
            rowFailures.Add(
                "references-heavy TOA evidence must include explicit page-number references, not generic field metadata only");
        }

        foreach (var signature in ReferencesHeavyRequiredToaPageReferenceSignatures)
        {
            if (!toa.PageReferenceSignatures.Contains(signature, StringComparer.Ordinal))
            {
                rowFailures.Add(
                    $"references-heavy TOA evidence is missing generated page-reference signature '{signature}'");
            }
        }

        var weakReferences = toa.PageReferences
            .Where(reference => !reference.HasPageReferenceSentinel)
            .Select(reference => reference.StableSignature)
            .Where(signature => !string.IsNullOrWhiteSpace(signature))
            .ToList();
        if (weakReferences.Count > 0)
        {
            rowFailures.Add(
                "references-heavy TOA evidence contains weak generated page-reference signature(s): " +
                string.Join("; ", weakReferences));
        }
    }

    private static void ValidateTableFeatureTags(
        FreeWVisualEvidenceRow row,
        List<string> rowFailures)
    {
        var tags = row.ExpectedFeatureTags;
        var tables = row.PageExpectation.Tables;
        var fillSignatures = tables.TableCellFillSignatures ?? [];
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
        if (tags.Contains("table-pagination", StringComparer.OrdinalIgnoreCase) && !tables.HasMultiPageTables)
            rowFailures.Add("table-layout evidence expects multi-page table pagination but the plan records one page");
        if (tags.Contains("table-pagination", StringComparer.OrdinalIgnoreCase) && !tables.HasRepeatedHeaderPages)
            rowFailures.Add("table-layout evidence expects repeated header pages but the pagination plan records none");
        if (tags.Contains("keep-rows", StringComparer.OrdinalIgnoreCase) && !tables.HasKeepTogetherRows)
            rowFailures.Add("table-layout evidence expects keep-together rows but the pagination plan records none");
        if (tags.Contains("banded-rows", StringComparer.OrdinalIgnoreCase) && !tables.HasBandedRows)
            rowFailures.Add("table-layout evidence expects banded rows but the table plan records none");
        if (tags.Contains("cell-shading", StringComparer.OrdinalIgnoreCase) && !tables.HasCellShading)
            rowFailures.Add("table-layout evidence expects cell shading but the table plan records none");
        if (tags.Contains("table-fill-signatures", StringComparer.OrdinalIgnoreCase)
            && fillSignatures.Count == 0)
            rowFailures.Add("table-layout evidence expects table cell fill signatures but the table plan records none");
        if (tags.Contains("style-derived-header-fill", StringComparer.OrdinalIgnoreCase)
            && !fillSignatures.Any(signature =>
                signature.Contains("source=style-derived-header", StringComparison.Ordinal)))
            rowFailures.Add("table-layout evidence expects style-derived header fill signatures but the table plan records none");
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
        var groupChildren = NormalizeGroupChildren(objects.GroupChildren);
        if (tags.Contains("grouped-mixed-children", StringComparer.OrdinalIgnoreCase) && !groupChildren.HasMixedTypedChildren)
            rowFailures.Add("drawing-object evidence expects grouped image/chart/SmartArt children but the group child plan records none");
        if (tags.Contains("grouped-child-images", StringComparer.OrdinalIgnoreCase) && groupChildren.ImageChildCount <= 0)
            rowFailures.Add("drawing-object evidence expects grouped image children but the group child plan records none");
        if (tags.Contains("grouped-child-charts", StringComparer.OrdinalIgnoreCase) && groupChildren.ChartChildCount <= 0)
            rowFailures.Add("drawing-object evidence expects grouped chart children but the group child plan records none");
        if (tags.Contains("grouped-child-smartart", StringComparer.OrdinalIgnoreCase) && groupChildren.SmartArtChildCount <= 0)
            rowFailures.Add("drawing-object evidence expects grouped SmartArt children but the group child plan records none");
        if (tags.Contains("grouped-child-visual-signature", StringComparer.OrdinalIgnoreCase)
            && (groupChildren.ChildVisualSignatures is null || groupChildren.ChildVisualSignatures.Count == 0))
            rowFailures.Add("drawing-object evidence expects grouped child visual signatures but the group child plan records none");
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
        if (tags.Contains("alt-text", StringComparer.OrdinalIgnoreCase) && objects.AltTextObjectCount <= 0)
            rowFailures.Add("drawing-object evidence expects alt text but the object plan records none");
        if (tags.Contains("drawing-effects", StringComparer.OrdinalIgnoreCase) && !objects.Effects.HasAny)
            rowFailures.Add("drawing-object evidence expects effect metadata but the object plan records no effects");
        if (tags.Contains("shape-effects", StringComparer.OrdinalIgnoreCase) && objects.Effects.ShapeEffectObjectCount <= 0)
            rowFailures.Add("drawing-object evidence expects shape effects but the object plan records none");
        if (tags.Contains("image-effects", StringComparer.OrdinalIgnoreCase) && objects.Effects.ImageEffectObjectCount <= 0)
            rowFailures.Add("drawing-object evidence expects image effects but the object plan records none");
        if (tags.Contains("wordart-effects", StringComparer.OrdinalIgnoreCase) && objects.Effects.WordArtEffectObjectCount <= 0)
            rowFailures.Add("drawing-object evidence expects WordArt effects but the object plan records none");
        if (tags.Contains("grouped-child-effects", StringComparer.OrdinalIgnoreCase) && objects.Effects.RenderedGroupChildEffectObjectCount <= 0)
            rowFailures.Add("drawing-object evidence expects rendered grouped child effects but the object plan records none");
        if (tags.Contains("grouped-child-shape-effects", StringComparer.OrdinalIgnoreCase) && objects.Effects.RenderedGroupChildShapeEffectObjectCount <= 0)
            rowFailures.Add("drawing-object evidence expects rendered grouped child shape effects but the object plan records none");
        if (tags.Contains("grouped-child-wordart-effects", StringComparer.OrdinalIgnoreCase) && objects.Effects.RenderedGroupChildWordArtEffectObjectCount <= 0)
            rowFailures.Add("drawing-object evidence expects rendered grouped child WordArt effects but the object plan records none");
        if (tags.Contains("shadow", StringComparer.OrdinalIgnoreCase) && !objects.Effects.HasShadow)
            rowFailures.Add("drawing-object evidence expects shadow effects but the object plan records none");
        if (tags.Contains("glow", StringComparer.OrdinalIgnoreCase) && !objects.Effects.HasGlow)
            rowFailures.Add("drawing-object evidence expects glow effects but the object plan records none");
        if (tags.Contains("reflection", StringComparer.OrdinalIgnoreCase) && !objects.Effects.HasReflection)
            rowFailures.Add("drawing-object evidence expects reflection effects but the object plan records none");
        if (tags.Contains("soft-edge", StringComparer.OrdinalIgnoreCase) && !objects.Effects.HasSoftEdge)
            rowFailures.Add("drawing-object evidence expects soft-edge effects but the object plan records none");
        if (tags.Contains("bevel", StringComparer.OrdinalIgnoreCase) && !objects.Effects.HasBevel)
            rowFailures.Add("drawing-object evidence expects bevel effects but the object plan records none");
        if (tags.Contains("artistic-effect", StringComparer.OrdinalIgnoreCase) && !objects.Effects.HasArtisticEffect)
            rowFailures.Add("drawing-object evidence expects artistic image effects but the object plan records none");
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
        if (tags.Contains("chart-visual-signature", StringComparer.OrdinalIgnoreCase)
            && (chartSmartArt.ChartVisualSignatures is null || chartSmartArt.ChartVisualSignatures.Count == 0))
        {
            rowFailures.Add("chart/SmartArt evidence expects chart visual signatures but the chart plan records none");
        }
        if (tags.Contains("smartart-layout", StringComparer.OrdinalIgnoreCase) && !chartSmartArt.HasSmartArtLayout)
            rowFailures.Add("chart/SmartArt evidence expects SmartArt layout metadata but the SmartArt plan records none");
        if (tags.Contains("smartart-colors", StringComparer.OrdinalIgnoreCase) && !chartSmartArt.HasSmartArtColorScheme)
            rowFailures.Add("chart/SmartArt evidence expects SmartArt color scheme metadata but the SmartArt plan records none");
        if (tags.Contains("smartart-style", StringComparer.OrdinalIgnoreCase) && !chartSmartArt.HasSmartArtStyle)
            rowFailures.Add("chart/SmartArt evidence expects SmartArt style metadata but the SmartArt plan records none");
        if (tags.Contains("smartart-node-fills", StringComparer.OrdinalIgnoreCase) && chartSmartArt.DistinctSmartArtFillCount <= 1)
            rowFailures.Add("chart/SmartArt evidence expects distinct SmartArt node fills but the SmartArt plan records one or fewer");
        if (tags.Contains("smartart-visual-signature", StringComparer.OrdinalIgnoreCase)
            && (chartSmartArt.SmartArtVisualSignatures is null || chartSmartArt.SmartArtVisualSignatures.Count == 0))
        {
            rowFailures.Add("chart/SmartArt evidence expects SmartArt visual signatures but the SmartArt plan records none");
        }
        if (chartSmartArt.SmartArtCount > 0 && chartSmartArt.SmartArtNodeCount <= 0)
            rowFailures.Add("chart/SmartArt evidence includes SmartArt but records no nodes");
        ValidateChartSmartArtVisualSignatures(chartSmartArt, rowFailures);
    }

    private static void ValidateChartSmartArtVisualSignatures(
        FreeWVisualChartSmartArtExpectation chartSmartArt,
        List<string> rowFailures)
    {
        var expectedChartSignatures = ChartSmartArtVisualPlanner.BuildChartVisualSignatures(chartSmartArt.Charts ?? []);
        var actualChartSignatures = OrderedSummaries(chartSmartArt.ChartVisualSignatures ?? []);
        if (!expectedChartSignatures.SequenceEqual(actualChartSignatures, StringComparer.Ordinal))
        {
            rowFailures.Add(
                $"chart visual signatures do not match chart plans: expected '{FormatSummaries(expectedChartSignatures)}', actual '{FormatSummaries(actualChartSignatures)}'");
        }

        var expectedSmartArtSignatures = ChartSmartArtVisualPlanner.BuildSmartArtVisualSignatures(chartSmartArt.SmartArts ?? []);
        var actualSmartArtSignatures = OrderedSummaries(chartSmartArt.SmartArtVisualSignatures ?? []);
        if (!expectedSmartArtSignatures.SequenceEqual(actualSmartArtSignatures, StringComparer.Ordinal))
        {
            rowFailures.Add(
                $"SmartArt visual signatures do not match SmartArt plans: expected '{FormatSummaries(expectedSmartArtSignatures)}', actual '{FormatSummaries(actualSmartArtSignatures)}'");
        }
    }

    private static void ValidateFieldFeatureTags(
        FreeWVisualEvidenceRow row,
        List<string> rowFailures)
    {
        var tags = row.ExpectedFeatureTags;
        var fields = row.PageExpectation.Fields;
        if (!tags.Contains("fields", StringComparer.OrdinalIgnoreCase))
            return;

        if (fields.SimpleFieldCount + fields.ComplexFieldCount <= 0)
            rowFailures.Add("field evidence must include at least one field run");
        if (tags.Contains("page-number-fields", StringComparer.OrdinalIgnoreCase) && !fields.HasPageFields)
            rowFailures.Add("field evidence expects PAGE fields but the field expectation records none");
        if (tags.Contains("numpages-fields", StringComparer.OrdinalIgnoreCase) && !fields.HasNumPagesFields)
            rowFailures.Add("field evidence expects NUMPAGES fields but the field expectation records none");
        if (tags.Contains("document-property-fields", StringComparer.OrdinalIgnoreCase) && !fields.HasDocumentPropertyFields)
            rowFailures.Add("field evidence expects document-property fields but the field expectation records none");
        if (tags.Contains("complex-fields", StringComparer.OrdinalIgnoreCase) && !fields.HasComplexFields)
            rowFailures.Add("field evidence expects complex fields but the field expectation records none");
        if (tags.Contains("complex-fields", StringComparer.OrdinalIgnoreCase) && !fields.HasComplexResultFields)
            rowFailures.Add("field evidence expects cached complex field results but the field expectation records none");
        if (tags.Contains("header-footer-fields", StringComparer.OrdinalIgnoreCase) && !fields.HasHeaderFooterFields)
            rowFailures.Add("field evidence expects header/footer fields but the field expectation records none");
    }

    private static void ValidateProofingFeatureTags(
        FreeWVisualEvidenceRow row,
        List<string> rowFailures)
    {
        var tags = row.ExpectedFeatureTags;
        if (!tags.Contains("proofing-diagnostics", StringComparer.OrdinalIgnoreCase))
            return;

        var proofing = row.PageExpectation.ProofingDiagnostics;
        if (proofing.DiagnosticCount <= 0)
            rowFailures.Add("scenario expects proofing diagnostics but the page expectation records none");
        if (!proofing.HasSpelling)
            rowFailures.Add("scenario expects spelling diagnostic evidence but the page expectation records none");
        if (!proofing.HasGrammar)
            rowFailures.Add("scenario expects grammar diagnostic evidence but the page expectation records none");
        if (tags.Contains("proofing-language", StringComparer.OrdinalIgnoreCase)
            && proofing.LanguageTags.Count == 0)
        {
            rowFailures.Add("scenario expects proofing language evidence but the proofing diagnostics record no language tags");
        }
    }

    private static void ValidateReviewProtectionFeatureTags(
        FreeWVisualEvidenceRow row,
        List<string> rowFailures)
    {
        var tags = row.ExpectedFeatureTags;
        if (!tags.Contains("review-protection-state", StringComparer.OrdinalIgnoreCase)
            && !tags.Contains("protection-command-matrix", StringComparer.OrdinalIgnoreCase)
            && !tags.Contains("comments-only-protection", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var protection = row.PageExpectation.ReviewProtection;
        if (tags.Contains("comments-only-protection", StringComparer.OrdinalIgnoreCase))
        {
            if (!string.Equals(protection.ProtectionMode, ProtectionMode.CommentsOnly.ToString(), StringComparison.Ordinal))
                rowFailures.Add("scenario expects CommentsOnly protection but the page expectation records a different protection mode");
            if (!protection.IsProtected)
                rowFailures.Add("scenario expects active editing restrictions but the page expectation records an unprotected document");
            if (protection.IsMarkedAsFinal || protection.MarkAsFinal.IsChecked)
                rowFailures.Add("scenario expects Mark as Final to be unchecked for the CommentsOnly protection slice");
            if (!protection.RestrictEditing.IsChecked)
                rowFailures.Add("scenario expects Restrict Editing checked but the page expectation records it unchecked");
        }

        if (tags.Contains("body-edit-blocked", StringComparer.OrdinalIgnoreCase))
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.BodyTextEdit), "None", isAllowed: false);
        if (tags.Contains("body-formatting-blocked", StringComparer.OrdinalIgnoreCase))
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.BodyFormatting), "None", isAllowed: false);
        if (tags.Contains("proofing-replacement-blocked", StringComparer.OrdinalIgnoreCase))
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.ProofingReplacement), "None", isAllowed: false);
        if (tags.Contains("history-blocked", StringComparer.OrdinalIgnoreCase))
        {
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.HistoryUndo), nameof(DocumentCommandMutationKind.BodyText), isAllowed: false);
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.HistoryRedo), nameof(DocumentCommandMutationKind.BodyFormatting), isAllowed: false);
        }
        if (tags.Contains("comment-workflow-allowed", StringComparer.OrdinalIgnoreCase))
        {
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.CommentInsert), "None", isAllowed: true);
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.CommentReply), "None", isAllowed: true);
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.CommentResolve), "None", isAllowed: true);
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.CommentDelete), "None", isAllowed: true);
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.HistoryUndo), nameof(DocumentCommandMutationKind.Comment), isAllowed: true);
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.HistoryRedo), nameof(DocumentCommandMutationKind.Comment), isAllowed: true);
        }
    }

    private static void RequireProtectionDecision(
        List<string> rowFailures,
        FreeWVisualReviewProtectionExpectation protection,
        string operation,
        string mutationKind,
        bool isAllowed)
    {
        var decision = protection.Operations.SingleOrDefault(item =>
            string.Equals(item.Operation, operation, StringComparison.Ordinal)
            && string.Equals(item.MutationKind, mutationKind, StringComparison.Ordinal));
        if (decision is null)
        {
            rowFailures.Add($"scenario expects protection decision {operation}/{mutationKind} but the page expectation records none");
            return;
        }

        if (decision.IsAllowed != isAllowed)
        {
            rowFailures.Add(
                $"scenario expects protection decision {operation}/{mutationKind} allowed={BoolFlag(isAllowed)} but the page expectation records allowed={BoolFlag(decision.IsAllowed)}");
        }
    }

    private static void ValidateBackstageCaptureSource(
        FreeWVisualEvidenceRow row,
        List<string> rowFailures)
    {
        if (!BackstageRendererScenarioIds.Contains(row.ScenarioId, StringComparer.OrdinalIgnoreCase))
            return;

        foreach (var (key, value) in row.HostMetadata)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (value.Contains("placeholder", StringComparison.OrdinalIgnoreCase))
            {
                rowFailures.Add(
                    $"backstage renderer evidence cannot use placeholder capture metadata '{key}={value}'");
            }
        }

        if (row.HostMetadata.TryGetValue("captureSource", out var captureSource)
            && captureSource.Contains("fallback", StringComparison.OrdinalIgnoreCase))
        {
            rowFailures.Add(
                $"backstage renderer evidence cannot use fallback capture source '{captureSource}'");
        }

        if (!row.HostMetadata.TryGetValue("captureSource", out captureSource)
            || string.IsNullOrWhiteSpace(captureSource))
        {
            rowFailures.Add("backstage renderer evidence must declare real captureSource metadata");
            return;
        }

        var expectedCaptureSource = row.HostId switch
        {
            WpfHostId => "wpf-composite-renderer",
            AvaloniaHostId => "avalonia-render-target",
            _ => null
        };
        if (expectedCaptureSource is null)
        {
            rowFailures.Add($"backstage renderer evidence has unsupported host id '{row.HostId}'");
            return;
        }

        if (!string.Equals(captureSource, expectedCaptureSource, StringComparison.OrdinalIgnoreCase))
        {
            rowFailures.Add(
                $"backstage renderer evidence for host '{row.HostId}' must use real capture source '{expectedCaptureSource}', found '{captureSource}'");
        }

        var expectedWorkflow = ExpectedBackstageWorkflow(row.ScenarioId);
        if (expectedWorkflow is null)
            return;

        if (!row.HostMetadata.TryGetValue("backstageWorkflow", out var workflow)
            || string.IsNullOrWhiteSpace(workflow))
        {
            rowFailures.Add(
                $"backstage renderer evidence for scenario '{row.ScenarioId}' must declare backstageWorkflow '{expectedWorkflow}'");
            return;
        }

        if (!string.Equals(workflow, expectedWorkflow, StringComparison.OrdinalIgnoreCase))
        {
            rowFailures.Add(
                $"backstage renderer evidence for scenario '{row.ScenarioId}' must use backstageWorkflow '{expectedWorkflow}', found '{workflow}'");
        }

        var expectedArtifactKind = ExpectedBackstageArtifactKind(row.ScenarioId);
        if (expectedArtifactKind is not null)
        {
            ValidateBackstageArtifactMetadata(
                row,
                rowFailures,
                "backstageArtifactKind",
                expectedArtifactKind);
        }

        var expectedPipeline = ExpectedBackstagePipeline(row.ScenarioId);
        if (expectedPipeline is not null)
        {
            ValidateBackstageArtifactMetadata(
                row,
                rowFailures,
                "backstagePipeline",
                expectedPipeline);
        }

        var expectedCaptureRoute = ExpectedBackstageCaptureRoute(row.ScenarioId);
        if (expectedCaptureRoute is not null)
        {
            ValidateBackstageArtifactMetadata(
                row,
                rowFailures,
                "backstageCaptureRoute",
                expectedCaptureRoute);
        }
    }

    private static string? ExpectedBackstageWorkflow(string scenarioId) =>
        scenarioId switch
        {
            "backstage-print-preview-fidelity" => "print-preview",
            "backstage-pdf-export-fidelity" => "pdf-export",
            _ => null
        };

    private static string? ExpectedBackstageArtifactKind(string scenarioId) =>
        scenarioId switch
        {
            "backstage-print-preview-fidelity" => "print-preview-fixed-layout",
            "backstage-pdf-export-fidelity" => "pdf-export-rasterized",
            _ => null
        };

    private static string? ExpectedBackstagePipeline(string scenarioId) =>
        scenarioId switch
        {
            "backstage-print-preview-fidelity" => "print-preview-fixed-layout-artifact",
            "backstage-pdf-export-fidelity" => "pdf-export-rasterized-artifact",
            _ => null
        };

    private static string? ExpectedBackstageCaptureRoute(string scenarioId) =>
        scenarioId switch
        {
            "backstage-print-preview-fidelity" => "backstage-print-preview-fixed-layout-capture",
            "backstage-pdf-export-fidelity" => "backstage-pdf-export-raster-capture",
            _ => null
        };

    private static void ValidateBackstageArtifactMetadata(
        FreeWVisualEvidenceRow row,
        List<string> rowFailures,
        string key,
        string expectedValue)
    {
        if (!row.HostMetadata.TryGetValue(key, out var value)
            || string.IsNullOrWhiteSpace(value))
        {
            rowFailures.Add(
                $"backstage renderer evidence for scenario '{row.ScenarioId}' must declare {key} '{expectedValue}'");
            return;
        }

        var normalizedValue = value.Trim();
        if (IsGenericOrFallbackBackstageArtifactMetadata(normalizedValue))
        {
            rowFailures.Add(
                $"backstage renderer evidence for scenario '{row.ScenarioId}' cannot use generic or fallback {key} '{value}'");
            return;
        }

        if (!string.Equals(normalizedValue, expectedValue, StringComparison.OrdinalIgnoreCase))
        {
            rowFailures.Add(
                $"backstage renderer evidence for scenario '{row.ScenarioId}' must use {key} '{expectedValue}', found '{value}'");
        }
    }

    private static bool IsGenericOrFallbackBackstageArtifactMetadata(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (value.Contains("placeholder", StringComparison.OrdinalIgnoreCase)
            || value.Contains("fallback", StringComparison.OrdinalIgnoreCase)
            || value.Contains("generic", StringComparison.OrdinalIgnoreCase)
            || value.Contains("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var genericValues = new[]
        {
            "capture",
            "page-capture",
            "page-screenshot",
            "pdf-export",
            "print-preview",
            "rasterized",
            "screenshot",
            "screen-capture",
            "ui-screenshot",
            "workflow-only"
        };
        return genericValues.Contains(value, StringComparer.OrdinalIgnoreCase);
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
            if (wpfRows.Count == 0 && avaloniaRows.Count == 0)
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
                ValidateReviewProofingPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
                ValidateReviewProtectionPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
            }
        }
    }

    private static void ValidateFieldRendererPairs(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows,
        List<string> failures)
    {
        foreach (var scenarioId in FieldRendererScenarioIds)
        {
            var wpfRows = TrustedRowsForHostScenario(rows, WpfHostId, scenarioId);
            var avaloniaRows = TrustedRowsForHostScenario(rows, AvaloniaHostId, scenarioId);
            if (wpfRows.Count == 0 || avaloniaRows.Count == 0)
            {
                if (!string.Equals(scenarioId, "references-heavy-fields", StringComparison.OrdinalIgnoreCase)
                    || (wpfRows.Count == 0 && avaloniaRows.Count == 0))
                {
                    continue;
                }
            }

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
                    $"field renderer pair '{scenarioId}' is missing Avalonia page(s): {FormatPages(missingAvaloniaPages)}");
            }

            if (missingWpfPages.Count > 0)
            {
                failures.Add(
                    $"field renderer pair '{scenarioId}' is missing WPF page(s): {FormatPages(missingWpfPages)}");
            }

            foreach (var pageNumber in wpfPages.Intersect(avaloniaPages))
            {
                var wpf = wpfRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                var avalonia = avaloniaRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                if (wpf is null || avalonia is null)
                    continue;

                ValidateRendererPairRow("field renderer pair", scenarioId, pageNumber, wpf, avalonia, failures);
                ValidateFieldPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
                if (string.Equals(scenarioId, "references-heavy-fields", StringComparison.OrdinalIgnoreCase))
                    ValidateReferencesHeavyToaPairRow(pageNumber, wpf, avalonia, failures);
            }
        }
    }

    private static void ValidateEquationRendererPairs(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows,
        List<string> failures)
    {
        foreach (var scenarioId in EquationRendererScenarioIds)
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
                    $"equation renderer pair '{scenarioId}' is missing Avalonia page(s): {FormatPages(missingAvaloniaPages)}");
            }

            if (missingWpfPages.Count > 0)
            {
                failures.Add(
                    $"equation renderer pair '{scenarioId}' is missing WPF page(s): {FormatPages(missingWpfPages)}");
            }

            foreach (var pageNumber in wpfPages.Intersect(avaloniaPages))
            {
                var wpf = wpfRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                var avalonia = avaloniaRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                if (wpf is null || avalonia is null)
                    continue;

                ValidateRendererPairRow("equation renderer pair", scenarioId, pageNumber, wpf, avalonia, failures);
            }
        }
    }

    private static void ValidateHeaderFooterRendererPairs(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows,
        List<string> failures)
    {
        foreach (var scenarioId in HeaderFooterRendererScenarioIds)
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
                    $"header/footer renderer pair '{scenarioId}' is missing Avalonia page(s): {FormatPages(missingAvaloniaPages)}");
            }

            if (missingWpfPages.Count > 0)
            {
                failures.Add(
                    $"header/footer renderer pair '{scenarioId}' is missing WPF page(s): {FormatPages(missingWpfPages)}");
            }

            foreach (var pageNumber in wpfPages.Intersect(avaloniaPages))
            {
                var wpf = wpfRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                var avalonia = avaloniaRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                if (wpf is null || avalonia is null)
                    continue;

                ValidateRendererPairRow("header/footer renderer pair", scenarioId, pageNumber, wpf, avalonia, failures);
                ValidateRequiredHeaderFooterImageEvidence(scenarioId, pageNumber, wpf, failures);
                ValidateRequiredHeaderFooterImageEvidence(scenarioId, pageNumber, avalonia, failures);
                ValidateHeaderFooterPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
            }
        }
    }

    private static void ValidateRequiredHeaderFooterImageEvidence(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow row,
        List<string> failures)
    {
        if (!row.ExpectedFeatureTags.Contains("header-footer-images", StringComparer.OrdinalIgnoreCase))
            return;

        var plan = row.HeaderFooters ?? HeaderFooterVisualPlanner.EmptyExpectation;
        if (plan.HasImages && plan.ImageCount > 0)
            return;

        failures.Add(
            $"header/footer renderer pair '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)} host '{row.HostId}' expected header/footer image evidence");
    }

    private static void ValidateTableRendererPairs(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows,
        List<string> failures)
    {
        foreach (var scenarioId in TableRendererScenarioIds)
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
                    $"table renderer pair '{scenarioId}' is missing Avalonia page(s): {FormatPages(missingAvaloniaPages)}");
            }

            if (missingWpfPages.Count > 0)
            {
                failures.Add(
                    $"table renderer pair '{scenarioId}' is missing WPF page(s): {FormatPages(missingWpfPages)}");
            }

            foreach (var pageNumber in wpfPages.Intersect(avaloniaPages))
            {
                var wpf = wpfRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                var avalonia = avaloniaRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                if (wpf is null || avalonia is null)
                    continue;

                ValidateRendererPairRow("table renderer pair", scenarioId, pageNumber, wpf, avalonia, failures);
                ValidateTablePairRow(scenarioId, pageNumber, wpf, avalonia, failures);
            }
        }
    }

    private static void ValidateDrawingObjectRendererPairs(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows,
        List<string> failures)
    {
        foreach (var scenarioId in DrawingObjectRendererScenarioIds)
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
                    $"drawing-object renderer pair '{scenarioId}' is missing Avalonia page(s): {FormatPages(missingAvaloniaPages)}");
            }

            if (missingWpfPages.Count > 0)
            {
                failures.Add(
                    $"drawing-object renderer pair '{scenarioId}' is missing WPF page(s): {FormatPages(missingWpfPages)}");
            }

            foreach (var pageNumber in wpfPages.Intersect(avaloniaPages))
            {
                var wpf = wpfRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                var avalonia = avaloniaRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                if (wpf is null || avalonia is null)
                    continue;

                ValidateRendererPairRow("drawing-object renderer pair", scenarioId, pageNumber, wpf, avalonia, failures);
                ValidateDrawingObjectPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
                if (GroupedChildEffectRendererScenarioIds.Contains(scenarioId, StringComparer.OrdinalIgnoreCase))
                    ValidateGroupedChildEffectPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
            }
        }
    }

    private static void ValidateChartSmartArtRendererPairs(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows,
        List<string> failures)
    {
        foreach (var scenarioId in ChartSmartArtRendererScenarioIds)
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
                    $"chart/SmartArt renderer pair '{scenarioId}' is missing Avalonia page(s): {FormatPages(missingAvaloniaPages)}");
            }

            if (missingWpfPages.Count > 0)
            {
                failures.Add(
                    $"chart/SmartArt renderer pair '{scenarioId}' is missing WPF page(s): {FormatPages(missingWpfPages)}");
            }

            foreach (var pageNumber in wpfPages.Intersect(avaloniaPages))
            {
                var wpf = wpfRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                var avalonia = avaloniaRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                if (wpf is null || avalonia is null)
                    continue;

                ValidateRendererPairRow("chart/SmartArt renderer pair", scenarioId, pageNumber, wpf, avalonia, failures);
                ValidateChartSmartArtPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
            }
        }
    }

    private static void ValidateWordArtWatermarkRendererPairs(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows,
        List<string> failures)
    {
        foreach (var scenarioId in WordArtWatermarkRendererScenarioIds)
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
                    $"WordArt watermark renderer pair '{scenarioId}' is missing Avalonia page(s): {FormatPages(missingAvaloniaPages)}");
            }

            if (missingWpfPages.Count > 0)
            {
                failures.Add(
                    $"WordArt watermark renderer pair '{scenarioId}' is missing WPF page(s): {FormatPages(missingWpfPages)}");
            }

            foreach (var pageNumber in wpfPages.Intersect(avaloniaPages))
            {
                var wpf = wpfRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                var avalonia = avaloniaRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                if (wpf is null || avalonia is null)
                    continue;

                ValidateRendererPairRow("WordArt watermark renderer pair", scenarioId, pageNumber, wpf, avalonia, failures);
                ValidateWordArtWatermarkPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
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

    private static void ValidateGroupedChildEffectPairRow(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia,
        List<string> failures)
    {
        var pairName = $"drawing-object renderer pair '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        var wpfEffects = wpf.DrawingObjects.Effects;
        var avaloniaEffects = avalonia.DrawingObjects.Effects;
        if (wpfEffects.RenderedGroupChildEffectObjectCount <= 0)
        {
            failures.Add($"{pairName} is missing WPF rendered grouped child effect evidence");
        }

        if (avaloniaEffects.RenderedGroupChildEffectObjectCount <= 0)
        {
            failures.Add($"{pairName} is missing Avalonia rendered grouped child effect evidence");
        }

        var wpfSummaries = OrderedSummaries(wpfEffects.RenderedGroupChildEffectSummaries);
        var avaloniaSummaries = OrderedSummaries(avaloniaEffects.RenderedGroupChildEffectSummaries);
        if (!wpfSummaries.SequenceEqual(avaloniaSummaries, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} rendered grouped child effect summaries differ: WPF '{FormatSummaries(wpfSummaries)}', Avalonia '{FormatSummaries(avaloniaSummaries)}'");
        }
    }

    private static void ValidateDrawingObjectPairRow(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia,
        List<string> failures)
    {
        var pairName = $"drawing-object renderer pair '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        var wpfObjects = wpf.DrawingObjects;
        var avaloniaObjects = avalonia.DrawingObjects;
        if (wpfObjects.FloatingObjectCount != avaloniaObjects.FloatingObjectCount)
        {
            failures.Add(
                $"{pairName} floating object counts differ: WPF {wpfObjects.FloatingObjectCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaObjects.FloatingObjectCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfObjects.BehindTextCount != avaloniaObjects.BehindTextCount)
        {
            failures.Add(
                $"{pairName} behind-text counts differ: WPF {wpfObjects.BehindTextCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaObjects.BehindTextCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfObjects.InFrontCount != avaloniaObjects.InFrontCount)
        {
            failures.Add(
                $"{pairName} in-front counts differ: WPF {wpfObjects.InFrontCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaObjects.InFrontCount.ToString(CultureInfo.InvariantCulture)}");
        }

        var wpfDrawingSignatures = BuildFloatingObjectSignatures(wpfObjects.Objects);
        var avaloniaDrawingSignatures = BuildFloatingObjectSignatures(avaloniaObjects.Objects);
        if (!wpfDrawingSignatures.SequenceEqual(avaloniaDrawingSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} floating object signatures differ: WPF '{FormatSummaries(wpfDrawingSignatures)}', Avalonia '{FormatSummaries(avaloniaDrawingSignatures)}'");
        }

        var wpfAltTextSummaries = OrderedSummaries(wpfObjects.AltTextSummaries ?? []);
        var avaloniaAltTextSummaries = OrderedSummaries(avaloniaObjects.AltTextSummaries ?? []);
        if (!wpfAltTextSummaries.SequenceEqual(avaloniaAltTextSummaries, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} alt text summaries differ: WPF '{FormatSummaries(wpfAltTextSummaries)}', Avalonia '{FormatSummaries(avaloniaAltTextSummaries)}'");
        }

        var wpfGroupChildren = NormalizeGroupChildren(wpfObjects.GroupChildren);
        var avaloniaGroupChildren = NormalizeGroupChildren(avaloniaObjects.GroupChildren);
        if (wpfGroupChildren.ChildCount != avaloniaGroupChildren.ChildCount)
        {
            failures.Add(
                $"{pairName} grouped child counts differ: WPF {wpfGroupChildren.ChildCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaGroupChildren.ChildCount.ToString(CultureInfo.InvariantCulture)}");
        }

        var wpfGroupChildKinds = OrderedSummaries(wpfGroupChildren.ChildKindSummaries ?? []);
        var avaloniaGroupChildKinds = OrderedSummaries(avaloniaGroupChildren.ChildKindSummaries ?? []);
        if (!wpfGroupChildKinds.SequenceEqual(avaloniaGroupChildKinds, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} grouped child kind summaries differ: WPF '{FormatSummaries(wpfGroupChildKinds)}', Avalonia '{FormatSummaries(avaloniaGroupChildKinds)}'");
        }

        var wpfGroupChildVisualSignatures = OrderedSummaries(wpfGroupChildren.ChildVisualSignatures ?? []);
        var avaloniaGroupChildVisualSignatures = OrderedSummaries(avaloniaGroupChildren.ChildVisualSignatures ?? []);
        if (!wpfGroupChildVisualSignatures.SequenceEqual(avaloniaGroupChildVisualSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} grouped child visual signatures differ: WPF '{FormatSummaries(wpfGroupChildVisualSignatures)}', Avalonia '{FormatSummaries(avaloniaGroupChildVisualSignatures)}'");
        }

        var wpfEffectSummaries = OrderedSummaries(wpfObjects.Effects.EffectSummaries);
        var avaloniaEffectSummaries = OrderedSummaries(avaloniaObjects.Effects.EffectSummaries);
        if (!wpfEffectSummaries.SequenceEqual(avaloniaEffectSummaries, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} drawing effect summaries differ: WPF '{FormatSummaries(wpfEffectSummaries)}', Avalonia '{FormatSummaries(avaloniaEffectSummaries)}'");
        }
    }

    private static void ValidateFieldPairRow(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia,
        List<string> failures)
    {
        var pairName = $"field renderer pair '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        var wpfFields = wpf.Fields;
        var avaloniaFields = avalonia.Fields;
        if (wpfFields.SimpleFieldCount != avaloniaFields.SimpleFieldCount)
        {
            failures.Add(
                $"{pairName} simple field counts differ: WPF {wpfFields.SimpleFieldCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaFields.SimpleFieldCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfFields.ComplexFieldCount != avaloniaFields.ComplexFieldCount)
        {
            failures.Add(
                $"{pairName} complex field counts differ: WPF {wpfFields.ComplexFieldCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaFields.ComplexFieldCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfFields.PageFieldCount != avaloniaFields.PageFieldCount)
        {
            failures.Add(
                $"{pairName} PAGE field counts differ: WPF {wpfFields.PageFieldCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaFields.PageFieldCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfFields.NumPagesFieldCount != avaloniaFields.NumPagesFieldCount)
        {
            failures.Add(
                $"{pairName} NUMPAGES field counts differ: WPF {wpfFields.NumPagesFieldCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaFields.NumPagesFieldCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfFields.DocumentPropertyFieldCount != avaloniaFields.DocumentPropertyFieldCount)
        {
            failures.Add(
                $"{pairName} document-property field counts differ: WPF {wpfFields.DocumentPropertyFieldCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaFields.DocumentPropertyFieldCount.ToString(CultureInfo.InvariantCulture)}");
        }

        var wpfKinds = OrderedSummaries(wpfFields.FieldKinds);
        var avaloniaKinds = OrderedSummaries(avaloniaFields.FieldKinds);
        if (!wpfKinds.SequenceEqual(avaloniaKinds, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} field kinds differ: WPF '{FormatSummaries(wpfKinds)}', Avalonia '{FormatSummaries(avaloniaKinds)}'");
        }

        var wpfComplexKeywords = OrderedSummaries(wpfFields.ComplexFieldKeywords);
        var avaloniaComplexKeywords = OrderedSummaries(avaloniaFields.ComplexFieldKeywords);
        if (!wpfComplexKeywords.SequenceEqual(avaloniaComplexKeywords, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} complex field keywords differ: WPF '{FormatSummaries(wpfComplexKeywords)}', Avalonia '{FormatSummaries(avaloniaComplexKeywords)}'");
        }

        var wpfComplexResultSignatures = OrderedSummaries(wpfFields.ComplexFieldResultSignatures);
        var avaloniaComplexResultSignatures = OrderedSummaries(avaloniaFields.ComplexFieldResultSignatures);
        if (!wpfComplexResultSignatures.SequenceEqual(avaloniaComplexResultSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} complex field result signatures differ: WPF '{FormatSummaries(wpfComplexResultSignatures)}', Avalonia '{FormatSummaries(avaloniaComplexResultSignatures)}'");
        }

        var wpfSlots = OrderedSummaries(wpfFields.HeaderFooterSlotNames);
        var avaloniaSlots = OrderedSummaries(avaloniaFields.HeaderFooterSlotNames);
        if (!wpfSlots.SequenceEqual(avaloniaSlots, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} header/footer field slots differ: WPF '{FormatSummaries(wpfSlots)}', Avalonia '{FormatSummaries(avaloniaSlots)}'");
        }
    }

    private static void ValidateHeaderFooterPairRow(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia,
        List<string> failures)
    {
        var pairName = $"header/footer renderer pair '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        var wpfPlan = wpf.HeaderFooters ?? HeaderFooterVisualPlanner.EmptyExpectation;
        var avaloniaPlan = avalonia.HeaderFooters ?? HeaderFooterVisualPlanner.EmptyExpectation;

        if (wpfPlan.SlotCount != avaloniaPlan.SlotCount)
        {
            failures.Add(
                $"{pairName} slot counts differ: WPF {wpfPlan.SlotCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaPlan.SlotCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfPlan.ImageCount != avaloniaPlan.ImageCount)
        {
            failures.Add(
                $"{pairName} header/footer image counts differ: WPF {wpfPlan.ImageCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaPlan.ImageCount.ToString(CultureInfo.InvariantCulture)}");
        }

        var wpfSlots = OrderedSummaries(wpfPlan.SlotNames ?? []);
        var avaloniaSlots = OrderedSummaries(avaloniaPlan.SlotNames ?? []);
        if (!wpfSlots.SequenceEqual(avaloniaSlots, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} header/footer slot names differ: WPF '{FormatSummaries(wpfSlots)}', Avalonia '{FormatSummaries(avaloniaSlots)}'");
        }

        var wpfImageSignatures = OrderedSummaries(wpfPlan.ImageSignatures ?? []);
        var avaloniaImageSignatures = OrderedSummaries(avaloniaPlan.ImageSignatures ?? []);
        if (!wpfImageSignatures.SequenceEqual(avaloniaImageSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} header/footer image signatures differ: WPF '{FormatSummaries(wpfImageSignatures)}', Avalonia '{FormatSummaries(avaloniaImageSignatures)}'");
        }

        var wpfSlotSignatures = BuildHeaderFooterSlotSignatures(wpfPlan.Slots ?? []);
        var avaloniaSlotSignatures = BuildHeaderFooterSlotSignatures(avaloniaPlan.Slots ?? []);
        if (!wpfSlotSignatures.SequenceEqual(avaloniaSlotSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} header/footer slot image summaries differ: WPF '{FormatSummaries(wpfSlotSignatures)}', Avalonia '{FormatSummaries(avaloniaSlotSignatures)}'");
        }
    }

    private static IReadOnlyList<string> BuildHeaderFooterSlotSignatures(
        IReadOnlyList<FreeWVisualHeaderFooterSlotPlan> slots) =>
        slots
            .Select(slot => string.Join(
                "|",
                $"slot={slot.SlotName}",
                $"section={slot.SectionOrdinal.ToString(CultureInfo.InvariantCulture)}",
                $"sectionPage={slot.SectionRelativePageNumber.ToString(CultureInfo.InvariantCulture)}",
                $"page={slot.PageNumber.ToString(CultureInfo.InvariantCulture)}",
                $"footer={BoolFlag(slot.IsFooter)}",
                $"align={slot.Alignment}",
                $"images={slot.ImageCount.ToString(CultureInfo.InvariantCulture)}",
                $"signatures={string.Join(",", OrderedSummaries(slot.ImageSignatures ?? []))}"))
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();

    private static void ValidateReviewProofingPairRow(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia,
        List<string> failures)
    {
        if (!IsReviewProofingEvidenceScenario(scenarioId))
            return;

        var pairName = $"review renderer pair '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        var wpfProofing = wpf.ProofingDiagnostics;
        var avaloniaProofing = avalonia.ProofingDiagnostics;

        if (wpfProofing.DiagnosticCount != avaloniaProofing.DiagnosticCount)
        {
            failures.Add(
                $"{pairName} proofing diagnostic counts differ: WPF {wpfProofing.DiagnosticCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaProofing.DiagnosticCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfProofing.SpellingCount != avaloniaProofing.SpellingCount)
        {
            failures.Add(
                $"{pairName} spelling diagnostic counts differ: WPF {wpfProofing.SpellingCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaProofing.SpellingCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfProofing.GrammarCount != avaloniaProofing.GrammarCount)
        {
            failures.Add(
                $"{pairName} grammar diagnostic counts differ: WPF {wpfProofing.GrammarCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaProofing.GrammarCount.ToString(CultureInfo.InvariantCulture)}");
        }

        var wpfKinds = OrderedSummaries(wpfProofing.Kinds);
        var avaloniaKinds = OrderedSummaries(avaloniaProofing.Kinds);
        if (!wpfKinds.SequenceEqual(avaloniaKinds, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} proofing diagnostic kinds differ: WPF '{FormatSummaries(wpfKinds)}', Avalonia '{FormatSummaries(avaloniaKinds)}'");
        }

        var wpfLanguages = OrderedSummaries(wpfProofing.LanguageTags);
        var avaloniaLanguages = OrderedSummaries(avaloniaProofing.LanguageTags);
        if (!wpfLanguages.SequenceEqual(avaloniaLanguages, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} proofing diagnostic language tags differ: WPF '{FormatSummaries(wpfLanguages)}', Avalonia '{FormatSummaries(avaloniaLanguages)}'");
        }

        var wpfSignatures = OrderedSummaries(wpfProofing.StableSignatures);
        var avaloniaSignatures = OrderedSummaries(avaloniaProofing.StableSignatures);
        if (!wpfSignatures.SequenceEqual(avaloniaSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} proofing diagnostic signatures differ: WPF '{FormatSummaries(wpfSignatures)}', Avalonia '{FormatSummaries(avaloniaSignatures)}'");
        }
    }

    private static void ValidateReviewProtectionPairRow(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia,
        List<string> failures)
    {
        if (!string.Equals(scenarioId, "review-protection-proofing-comments-only", StringComparison.OrdinalIgnoreCase))
            return;

        var pairName = $"review renderer pair '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        var wpfProtection = wpf.ReviewProtection;
        var avaloniaProtection = avalonia.ReviewProtection;

        if (!string.Equals(wpfProtection.ProtectionMode, avaloniaProtection.ProtectionMode, StringComparison.Ordinal))
        {
            failures.Add(
                $"{pairName} protection modes differ: WPF '{wpfProtection.ProtectionMode}', Avalonia '{avaloniaProtection.ProtectionMode}'");
        }

        if (wpfProtection.IsProtected != avaloniaProtection.IsProtected)
        {
            failures.Add(
                $"{pairName} protected states differ: WPF {BoolFlag(wpfProtection.IsProtected)}, Avalonia {BoolFlag(avaloniaProtection.IsProtected)}");
        }

        if (wpfProtection.IsMarkedAsFinal != avaloniaProtection.IsMarkedAsFinal)
        {
            failures.Add(
                $"{pairName} Mark as Final states differ: WPF {BoolFlag(wpfProtection.IsMarkedAsFinal)}, Avalonia {BoolFlag(avaloniaProtection.IsMarkedAsFinal)}");
        }

        if (wpfProtection.MarkAsFinal.IsChecked != avaloniaProtection.MarkAsFinal.IsChecked)
        {
            failures.Add(
                $"{pairName} Mark as Final checked states differ: WPF {BoolFlag(wpfProtection.MarkAsFinal.IsChecked)}, Avalonia {BoolFlag(avaloniaProtection.MarkAsFinal.IsChecked)}");
        }

        if (wpfProtection.RestrictEditing.IsChecked != avaloniaProtection.RestrictEditing.IsChecked)
        {
            failures.Add(
                $"{pairName} Restrict Editing checked states differ: WPF {BoolFlag(wpfProtection.RestrictEditing.IsChecked)}, Avalonia {BoolFlag(avaloniaProtection.RestrictEditing.IsChecked)}");
        }

        var wpfSignatures = OrderedSummaries(wpfProtection.StableSignatures);
        var avaloniaSignatures = OrderedSummaries(avaloniaProtection.StableSignatures);
        if (!wpfSignatures.SequenceEqual(avaloniaSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} protection command signatures differ: WPF '{FormatSummaries(wpfSignatures)}', Avalonia '{FormatSummaries(avaloniaSignatures)}'");
        }
    }

    private static bool IsReviewProofingEvidenceScenario(string scenarioId) =>
        string.Equals(scenarioId, "review-proofing-visual-depth", StringComparison.OrdinalIgnoreCase)
        || string.Equals(scenarioId, "review-protection-proofing-comments-only", StringComparison.OrdinalIgnoreCase);

    private static void ValidateReferencesHeavyToaPairRow(
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia,
        List<string> failures)
    {
        var pairName = $"field renderer pair 'references-heavy-fields' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        var wpfToa = wpf.TableOfAuthorities;
        var avaloniaToa = avalonia.TableOfAuthorities;

        if (wpfToa.EntryCount != avaloniaToa.EntryCount)
        {
            failures.Add(
                $"{pairName} generated TOA entry counts differ: WPF {wpfToa.EntryCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaToa.EntryCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfToa.EntryWithPageReferenceCount != avaloniaToa.EntryWithPageReferenceCount)
        {
            failures.Add(
                $"{pairName} generated TOA page-reference counts differ: WPF {wpfToa.EntryWithPageReferenceCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaToa.EntryWithPageReferenceCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfToa.CategoryCount != avaloniaToa.CategoryCount)
        {
            failures.Add(
                $"{pairName} generated TOA category counts differ: WPF {wpfToa.CategoryCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaToa.CategoryCount.ToString(CultureInfo.InvariantCulture)}");
        }

        var wpfCategories = OrderedSummaries(wpfToa.Categories);
        var avaloniaCategories = OrderedSummaries(avaloniaToa.Categories);
        if (!wpfCategories.SequenceEqual(avaloniaCategories, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} generated TOA category labels differ: WPF '{FormatSummaries(wpfCategories)}', Avalonia '{FormatSummaries(avaloniaCategories)}'");
        }

        var wpfSignatures = OrderedSummaries(wpfToa.PageReferenceSignatures);
        var avaloniaSignatures = OrderedSummaries(avaloniaToa.PageReferenceSignatures);
        if (!wpfSignatures.SequenceEqual(avaloniaSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} generated TOA page-reference signatures differ: WPF '{FormatSummaries(wpfSignatures)}', Avalonia '{FormatSummaries(avaloniaSignatures)}'");
        }
    }

    private static void ValidateTablePairRow(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia,
        List<string> failures)
    {
        var pairName = $"table renderer pair '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        var wpfTables = FreeWVisualEvidencePlanner.NormalizeTableFillEvidence(wpf.Tables);
        var avaloniaTables = FreeWVisualEvidencePlanner.NormalizeTableFillEvidence(avalonia.Tables);
        if (wpfTables.TableCount != avaloniaTables.TableCount)
        {
            failures.Add(
                $"{pairName} table counts differ: WPF {wpfTables.TableCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaTables.TableCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfTables.TotalRows != avaloniaTables.TotalRows)
        {
            failures.Add(
                $"{pairName} total row counts differ: WPF {wpfTables.TotalRows.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaTables.TotalRows.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfTables.TotalCells != avaloniaTables.TotalCells)
        {
            failures.Add(
                $"{pairName} total cell counts differ: WPF {wpfTables.TotalCells.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaTables.TotalCells.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfTables.EstimatedPageCount != avaloniaTables.EstimatedPageCount)
        {
            failures.Add(
                $"{pairName} estimated table page counts differ: WPF {wpfTables.EstimatedPageCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaTables.EstimatedPageCount.ToString(CultureInfo.InvariantCulture)}");
        }

        var wpfComparisonTables = wpfTables.Tables;
        var avaloniaComparisonTables = avaloniaTables.Tables;
        var wpfFillSignatures = OrderedSummaries(wpfTables.TableCellFillSignatures);
        var avaloniaFillSignatures = OrderedSummaries(avaloniaTables.TableCellFillSignatures);
        if (!wpfFillSignatures.SequenceEqual(avaloniaFillSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} table cell fill signatures differ: WPF '{FormatSummaries(wpfFillSignatures)}', Avalonia '{FormatSummaries(avaloniaFillSignatures)}'");
        }

        var wpfTableSignatures = BuildTablePlanSignatures(wpfComparisonTables);
        var avaloniaTableSignatures = BuildTablePlanSignatures(avaloniaComparisonTables);
        if (!wpfTableSignatures.SequenceEqual(avaloniaTableSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} table plan signatures differ: {DescribeTablePlanDifferences(wpfComparisonTables, avaloniaComparisonTables)}; WPF '{FormatSummaries(wpfTableSignatures)}', Avalonia '{FormatSummaries(avaloniaTableSignatures)}'");
        }

        var wpfPaginationSignatures = BuildTablePaginationSignatures(wpfTables.PaginationPlans);
        var avaloniaPaginationSignatures = BuildTablePaginationSignatures(avaloniaTables.PaginationPlans);
        if (!wpfPaginationSignatures.SequenceEqual(avaloniaPaginationSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} table pagination signatures differ: WPF '{FormatSummaries(wpfPaginationSignatures)}', Avalonia '{FormatSummaries(avaloniaPaginationSignatures)}'");
        }
    }

    private static void ValidateWordArtWatermarkPairRow(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia,
        List<string> failures)
    {
        var pairName = $"WordArt watermark renderer pair '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        var wpfFeatureSignature = BuildWordArtWatermarkFeatureSignature(wpf.PageFeatures);
        var avaloniaFeatureSignature = BuildWordArtWatermarkFeatureSignature(avalonia.PageFeatures);
        if (!string.Equals(wpfFeatureSignature, avaloniaFeatureSignature, StringComparison.Ordinal))
        {
            failures.Add(
                $"{pairName} page feature signatures differ: WPF '{wpfFeatureSignature}', Avalonia '{avaloniaFeatureSignature}'");
        }

        var wpfDrawingSignatures = BuildFloatingObjectSignatures(wpf.DrawingObjects.Objects);
        var avaloniaDrawingSignatures = BuildFloatingObjectSignatures(avalonia.DrawingObjects.Objects);
        if (!wpfDrawingSignatures.SequenceEqual(avaloniaDrawingSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} floating object signatures differ: WPF '{FormatSummaries(wpfDrawingSignatures)}', Avalonia '{FormatSummaries(avaloniaDrawingSignatures)}'");
        }

        var wpfEffectSummaries = OrderedSummaries(wpf.DrawingObjects.Effects.EffectSummaries);
        var avaloniaEffectSummaries = OrderedSummaries(avalonia.DrawingObjects.Effects.EffectSummaries);
        if (!wpfEffectSummaries.SequenceEqual(avaloniaEffectSummaries, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} drawing effect summaries differ: WPF '{FormatSummaries(wpfEffectSummaries)}', Avalonia '{FormatSummaries(avaloniaEffectSummaries)}'");
        }
    }

    private static void ValidateChartSmartArtPairRow(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia,
        List<string> failures)
    {
        var pairName = $"chart/SmartArt renderer pair '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        var wpfPlan = wpf.ChartSmartArt;
        var avaloniaPlan = avalonia.ChartSmartArt;

        if (wpfPlan.ChartCount != avaloniaPlan.ChartCount)
        {
            failures.Add(
                $"{pairName} chart counts differ: WPF {wpfPlan.ChartCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaPlan.ChartCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfPlan.SmartArtCount != avaloniaPlan.SmartArtCount)
        {
            failures.Add(
                $"{pairName} SmartArt counts differ: WPF {wpfPlan.SmartArtCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaPlan.SmartArtCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfPlan.SmartArtNodeCount != avaloniaPlan.SmartArtNodeCount)
        {
            failures.Add(
                $"{pairName} SmartArt node counts differ: WPF {wpfPlan.SmartArtNodeCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaPlan.SmartArtNodeCount.ToString(CultureInfo.InvariantCulture)}");
        }

        var wpfChartSignatures = OrderedSummaries(wpfPlan.ChartVisualSignatures ?? []);
        var avaloniaChartSignatures = OrderedSummaries(avaloniaPlan.ChartVisualSignatures ?? []);
        if (!wpfChartSignatures.SequenceEqual(avaloniaChartSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} chart visual signatures differ: WPF '{FormatSummaries(wpfChartSignatures)}', Avalonia '{FormatSummaries(avaloniaChartSignatures)}'");
        }

        var wpfSmartArtSignatures = OrderedSummaries(wpfPlan.SmartArtVisualSignatures ?? []);
        var avaloniaSmartArtSignatures = OrderedSummaries(avaloniaPlan.SmartArtVisualSignatures ?? []);
        if (!wpfSmartArtSignatures.SequenceEqual(avaloniaSmartArtSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} SmartArt visual signatures differ: WPF '{FormatSummaries(wpfSmartArtSignatures)}', Avalonia '{FormatSummaries(avaloniaSmartArtSignatures)}'");
        }
    }

    private static string DescribeTablePlanDifferences(
        IReadOnlyList<DocumentTableLayoutPlan> wpfTables,
        IReadOnlyList<DocumentTableLayoutPlan> avaloniaTables)
    {
        const int maxDifferences = 8;
        var differences = new List<string>();
        var wpfByIndex = wpfTables.ToDictionary(t => t.TableIndex);
        var avaloniaByIndex = avaloniaTables.ToDictionary(t => t.TableIndex);
        var tableIndexes = wpfByIndex.Keys
            .Concat(avaloniaByIndex.Keys)
            .Distinct()
            .Order()
            .ToList();

        foreach (var tableIndex in tableIndexes)
        {
            if (!wpfByIndex.TryGetValue(tableIndex, out var wpfTable))
            {
                differences.Add($"table {tableIndex.ToString(CultureInfo.InvariantCulture)} missing from WPF");
                continue;
            }

            if (!avaloniaByIndex.TryGetValue(tableIndex, out var avaloniaTable))
            {
                differences.Add($"table {tableIndex.ToString(CultureInfo.InvariantCulture)} missing from Avalonia");
                continue;
            }

            AddTablePropertyDifference(differences, tableIndex, "row count", wpfTable.RowCount, avaloniaTable.RowCount);
            AddTablePropertyDifference(differences, tableIndex, "grid column count", wpfTable.GridColumnCount, avaloniaTable.GridColumnCount);
            AddTablePropertyDifference(differences, tableIndex, "has header row", wpfTable.HasHeaderRow, avaloniaTable.HasHeaderRow);
            AddTablePropertyDifference(differences, tableIndex, "repeats header row", wpfTable.RepeatsHeaderRow, avaloniaTable.RepeatsHeaderRow);
            AddTablePropertyDifference(differences, tableIndex, "has banded rows", wpfTable.HasBandedRows, avaloniaTable.HasBandedRows);
            AddTablePropertyDifference(differences, tableIndex, "has banded columns", wpfTable.HasBandedColumns, avaloniaTable.HasBandedColumns);
            AddTablePropertyDifference(differences, tableIndex, "has first column", wpfTable.HasFirstColumn, avaloniaTable.HasFirstColumn);
            AddTablePropertyDifference(differences, tableIndex, "has last column", wpfTable.HasLastColumn, avaloniaTable.HasLastColumn);
            AddTablePropertyDifference(differences, tableIndex, "has last row", wpfTable.HasLastRow, avaloniaTable.HasLastRow);
            AddTablePropertyDifference(differences, tableIndex, "has merged cells", wpfTable.HasMergedCells, avaloniaTable.HasMergedCells);
            AddTablePropertyDifference(differences, tableIndex, "has vertical merges", wpfTable.HasVerticalMerges, avaloniaTable.HasVerticalMerges);
            AddTablePropertyDifference(differences, tableIndex, "has cell shading", wpfTable.HasCellShading, avaloniaTable.HasCellShading);
            AddTablePropertyDifference(differences, tableIndex, "has custom cell borders", wpfTable.HasCustomCellBorders, avaloniaTable.HasCustomCellBorders);
            AddTablePropertyDifference(differences, tableIndex, "has cell margins", wpfTable.HasCellMargins, avaloniaTable.HasCellMargins);
            AddTablePropertyDifference(differences, tableIndex, "has cell spacing", wpfTable.HasCellSpacing, avaloniaTable.HasCellSpacing);
            AddTablePropertyDifference(differences, tableIndex, "has vertical text", wpfTable.HasVerticalText, avaloniaTable.HasVerticalText);
            AddTablePropertyDifference(differences, tableIndex, "has vertical alignment", wpfTable.HasVerticalAlignment, avaloniaTable.HasVerticalAlignment);
            AddTablePropertyDifference(differences, tableIndex, "has preferred widths", wpfTable.HasPreferredWidths, avaloniaTable.HasPreferredWidths);
            AddTablePropertyDifference(differences, tableIndex, "has named style", wpfTable.HasNamedStyle, avaloniaTable.HasNamedStyle);
            AddTablePropertyDifference(differences, tableIndex, "alignment", wpfTable.Alignment, avaloniaTable.Alignment);
            AddTablePropertyDifference(differences, tableIndex, "auto fit", wpfTable.AutoFit, avaloniaTable.AutoFit);
            AddTablePropertyDifference(differences, tableIndex, "style id", wpfTable.TableStyleId, avaloniaTable.TableStyleId);
            AddTablePropertyDifference(
                differences,
                tableIndex,
                "column widths",
                string.Join(",", wpfTable.ColumnWidthsDip.Select(FormatDouble)),
                string.Join(",", avaloniaTable.ColumnWidthsDip.Select(FormatDouble)));

            DescribeTableCellDifferences(differences, tableIndex, wpfTable.Cells, avaloniaTable.Cells);
        }

        return FormatDifferenceList(differences, maxDifferences);
    }

    private static void DescribeTableCellDifferences(
        List<string> differences,
        int tableIndex,
        IReadOnlyList<DocumentTableCellLayoutPlan> wpfCells,
        IReadOnlyList<DocumentTableCellLayoutPlan> avaloniaCells)
    {
        var wpfByKey = wpfCells.ToDictionary(TableCellKey, StringComparer.Ordinal);
        var avaloniaByKey = avaloniaCells.ToDictionary(TableCellKey, StringComparer.Ordinal);
        var cellKeys = wpfByKey.Keys
            .Concat(avaloniaByKey.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        foreach (var key in cellKeys)
        {
            if (!wpfByKey.TryGetValue(key, out var wpfCell))
            {
                differences.Add($"table {tableIndex.ToString(CultureInfo.InvariantCulture)} cell {key} missing from WPF");
                continue;
            }

            if (!avaloniaByKey.TryGetValue(key, out var avaloniaCell))
            {
                differences.Add($"table {tableIndex.ToString(CultureInfo.InvariantCulture)} cell {key} missing from Avalonia");
                continue;
            }

            AddTableCellDifference(differences, tableIndex, key, "grid span", wpfCell.GridSpan, avaloniaCell.GridSpan);
            AddTableCellDifference(differences, tableIndex, key, "row span", wpfCell.RowSpan, avaloniaCell.RowSpan);
            AddTableCellDifference(differences, tableIndex, key, "vertical merge continuation", wpfCell.IsVerticalMergeContinuation, avaloniaCell.IsVerticalMergeContinuation);
            AddTableCellDifference(differences, tableIndex, key, "shading color", wpfCell.ShadingColorHex, avaloniaCell.ShadingColorHex);
            AddTableCellDifference(differences, tableIndex, key, "custom borders", wpfCell.HasCustomBorders, avaloniaCell.HasCustomBorders);
            AddTableCellDifference(differences, tableIndex, key, "text direction", wpfCell.TextDirection, avaloniaCell.TextDirection);
            AddTableCellDifference(differences, tableIndex, key, "vertical alignment", wpfCell.VerticalAlignment, avaloniaCell.VerticalAlignment);
            AddTableCellDifference(differences, tableIndex, key, "preferred width", wpfCell.PreferredWidthDip, avaloniaCell.PreferredWidthDip);
            AddTableCellDifference(differences, tableIndex, key, "height", wpfCell.HeightDip, avaloniaCell.HeightDip);
        }
    }

    private static string TableCellKey(DocumentTableCellLayoutPlan cell) =>
        string.Concat(
            "r",
            cell.RowIndex.ToString(CultureInfo.InvariantCulture),
            "c",
            cell.CellIndex.ToString(CultureInfo.InvariantCulture),
            "g",
            cell.GridColumnIndex.ToString(CultureInfo.InvariantCulture));

    private static void AddTablePropertyDifference<T>(
        List<string> differences,
        int tableIndex,
        string name,
        T wpfValue,
        T avaloniaValue)
    {
        if (EqualityComparer<T>.Default.Equals(wpfValue, avaloniaValue))
            return;

        differences.Add(
            $"table {tableIndex.ToString(CultureInfo.InvariantCulture)} {name} differs: WPF '{FormatDifferenceValue(wpfValue)}', Avalonia '{FormatDifferenceValue(avaloniaValue)}'");
    }

    private static void AddTableCellDifference<T>(
        List<string> differences,
        int tableIndex,
        string cellKey,
        string name,
        T wpfValue,
        T avaloniaValue)
    {
        if (EqualityComparer<T>.Default.Equals(wpfValue, avaloniaValue))
            return;

        differences.Add(
            $"table {tableIndex.ToString(CultureInfo.InvariantCulture)} cell {cellKey} {name} differs: WPF '{FormatDifferenceValue(wpfValue)}', Avalonia '{FormatDifferenceValue(avaloniaValue)}'");
    }

    private static string FormatDifferenceValue<T>(T value) =>
        value switch
        {
            null => "-",
            bool boolValue => boolValue ? "true" : "false",
            double doubleValue => FormatDouble(doubleValue),
            _ => value.ToString() ?? "-"
        };

    private static string FormatDifferenceList(IReadOnlyList<string> differences, int maxDifferences)
    {
        if (differences.Count == 0)
            return "no field-level differences isolated";

        var visible = differences
            .Take(Math.Max(1, maxDifferences))
            .ToList();
        var summary = string.Join("; ", visible);
        var hidden = differences.Count - visible.Count;
        return hidden > 0
            ? summary + $"; +{hidden.ToString(CultureInfo.InvariantCulture)} more"
            : summary;
    }

    private static List<string> BuildTablePlanSignatures(IEnumerable<DocumentTableLayoutPlan> tables) =>
        tables
            .Select(table => string.Join(
                "|",
                table.TableIndex.ToString(CultureInfo.InvariantCulture),
                table.RowCount.ToString(CultureInfo.InvariantCulture),
                table.GridColumnCount.ToString(CultureInfo.InvariantCulture),
                BoolFlag(table.HasHeaderRow),
                BoolFlag(table.RepeatsHeaderRow),
                BoolFlag(table.HasBandedRows),
                BoolFlag(table.HasBandedColumns),
                BoolFlag(table.HasFirstColumn),
                BoolFlag(table.HasLastColumn),
                BoolFlag(table.HasLastRow),
                BoolFlag(table.HasMergedCells),
                BoolFlag(table.HasVerticalMerges),
                BoolFlag(table.HasCellShading),
                BoolFlag(table.HasCustomCellBorders),
                BoolFlag(table.HasCellMargins),
                BoolFlag(table.HasCellSpacing),
                BoolFlag(table.HasVerticalText),
                BoolFlag(table.HasVerticalAlignment),
                BoolFlag(table.HasPreferredWidths),
                BoolFlag(table.HasNamedStyle),
                table.Alignment,
                table.AutoFit,
                table.TableStyleId ?? string.Empty,
                string.Join(",", table.ColumnWidthsDip.Select(FormatDouble)),
                string.Join(
                    ";",
                    table.Cells
                        .Select(cell => BuildTableCellSignature(table, cell))
                        .OrderBy(signature => signature, StringComparer.Ordinal))))
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();

    private static string BuildTableCellSignature(DocumentTableLayoutPlan table, DocumentTableCellLayoutPlan cell)
    {
        var fillPlan = DocumentViewLayoutPlanner.BuildTableCellEffectiveFillPlan(table, cell);

        return string.Join(
            ":",
            cell.RowIndex.ToString(CultureInfo.InvariantCulture),
            cell.CellIndex.ToString(CultureInfo.InvariantCulture),
            cell.GridColumnIndex.ToString(CultureInfo.InvariantCulture),
            cell.GridSpan.ToString(CultureInfo.InvariantCulture),
            cell.RowSpan.ToString(CultureInfo.InvariantCulture),
            BoolFlag(cell.IsVerticalMergeContinuation),
            cell.ShadingColorHex ?? string.Empty,
            fillPlan.ExplicitFillHex ?? string.Empty,
            fillPlan.StyleDerivedFillSource ?? string.Empty,
            fillPlan.StyleDerivedFillHex ?? string.Empty,
            fillPlan.EffectiveFillSource ?? string.Empty,
            fillPlan.EffectiveFillHex ?? string.Empty,
            BoolFlag(fillPlan.StyleDerivedBold),
            BoolFlag(fillPlan.EffectiveBold),
            BoolFlag(cell.HasCustomBorders),
            cell.TextDirection,
            cell.VerticalAlignment,
            cell.PreferredWidthDip.HasValue ? FormatDouble(cell.PreferredWidthDip.Value) : string.Empty,
            cell.HeightDip.HasValue ? FormatDouble(cell.HeightDip.Value) : string.Empty);
    }

    private static List<string> BuildTablePaginationSignatures(IEnumerable<DocumentTablePaginationPlan> plans) =>
        plans
            .Select(plan => string.Join(
                "|",
                plan.TableIndex.ToString(CultureInfo.InvariantCulture),
                plan.EstimatedPageCount.ToString(CultureInfo.InvariantCulture),
                FormatDouble(plan.AvailableBodyHeightDip),
                FormatDouble(plan.HeaderHeightDip),
                BoolFlag(plan.RepeatsHeaderRows),
                BoolFlag(plan.HasKeepTogetherRows),
                BoolFlag(plan.SplitsRowsAllowed),
                string.Join(",", plan.HeaderRowIndexes),
                string.Join(";", plan.Pages.Select(BuildTablePaginationPageSignature))))
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();

    private static string BuildTablePaginationPageSignature(DocumentTablePaginationPagePlan page) =>
        string.Join(
            ":",
            page.PageNumber.ToString(CultureInfo.InvariantCulture),
            string.Join(",", page.SourceRowIndexes),
            string.Join(",", page.RepeatedHeaderRowIndexes),
            string.Join(",", page.KeepTogetherRowIndexes),
            FormatDouble(page.UsedHeightDip),
            FormatDouble(page.AvailableHeightDip),
            string.Join(",", page.RenderRows.Select(BuildTablePaginationRenderRowSignature)));

    private static string BuildTablePaginationRenderRowSignature(DocumentTablePaginationRenderRowPlan row) =>
        string.Join(
            "/",
            row.SourceRowIndex.ToString(CultureInfo.InvariantCulture),
            row.PageNumber.ToString(CultureInfo.InvariantCulture),
            row.VisualRowIndexOnPage.ToString(CultureInfo.InvariantCulture),
            BoolFlag(row.IsRepeatedHeader),
            BoolFlag(row.StartsPlannedPage),
            FormatDouble(row.PageOffsetYDip),
            FormatDouble(row.EstimatedHeightDip));

    private static string BuildWordArtWatermarkFeatureSignature(FreeWVisualPageFeatureExpectation features)
    {
        var columns = features.Columns;
        var border = features.PageBorder;
        var watermark = features.Watermark;
        return string.Join(
            "|",
            columns.Count.ToString(CultureInfo.InvariantCulture),
            FormatDouble(columns.WidthDip),
            FormatDouble(columns.GapDip),
            BoolFlag(columns.LineBetween),
            string.Join(",", columns.WidthsDip.Select(FormatDouble)),
            BoolFlag(border.Present),
            border.ColorHex ?? string.Empty,
            FormatDouble(border.WidthDip),
            BoolFlag(watermark.Present),
            watermark.Text ?? string.Empty,
            watermark.Layout ?? string.Empty,
            watermark.FontColorHex ?? string.Empty,
            FormatDouble(watermark.Opacity),
            BoolFlag(watermark.IsPicture));
    }

    private static FreeWVisualDrawingObjectGroupChildExpectation NormalizeGroupChildren(
        FreeWVisualDrawingObjectGroupChildExpectation? groupChildren) =>
        groupChildren ?? new FreeWVisualDrawingObjectGroupChildExpectation(
            ChildCount: 0,
            ImageChildCount: 0,
            ShapeChildCount: 0,
            ChartChildCount: 0,
            SmartArtChildCount: 0,
            WordArtChildCount: 0,
            ChildKindSummaries: [],
            ChildVisualSignatures: []);

    private static List<string> BuildFloatingObjectSignatures(IEnumerable<DocumentFloatingObjectSnapshot> objects) =>
        objects
            .Select(o => string.Join(
                "|",
                o.TypeTag,
                o.BlockIndex.ToString(CultureInfo.InvariantCulture),
                o.RunIndex.ToString(CultureInfo.InvariantCulture),
                FormatDouble(o.Rect.XDip),
                FormatDouble(o.Rect.YDip),
                FormatDouble(o.Rect.WidthDip),
                FormatDouble(o.Rect.HeightDip),
                BoolFlag(o.BehindText),
                o.ZOrderIndex.ToString(CultureInfo.InvariantCulture),
                o.Wrapping.ToString(),
                FormatDouble(o.RotationAngle),
                BoolFlag(o.FlipH),
                BoolFlag(o.FlipV)))
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();

    private static string BoolFlag(bool value) => value ? "1" : "0";

    private static string FormatDouble(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private static List<string> OrderedSummaries(IEnumerable<string> summaries) =>
        summaries
            .Where(summary => !string.IsNullOrWhiteSpace(summary))
            .OrderBy(summary => summary, StringComparer.Ordinal)
            .ToList();

    private static string FormatSummaries(IReadOnlyList<string> summaries) =>
        summaries.Count == 0 ? "none" : string.Join("/", summaries);


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
            var groupChildren = NormalizeGroupChildren(row.DrawingObjects.GroupChildren);
            if (groupChildren.ChildCount > 0)
            {
                parts.Add(
                    $"{groupChildren.ChildCount.ToString(CultureInfo.InvariantCulture)} grouped child object(s): " +
                    string.Join("/", groupChildren.ChildKindSummaries));
            }
            if (row.DrawingObjects.Effects.EffectObjectCount > 0)
            {
                parts.Add(
                    $"{row.DrawingObjects.Effects.EffectObjectCount.ToString(CultureInfo.InvariantCulture)} drawing effect object(s): " +
                    string.Join("/", row.DrawingObjects.Effects.EffectSummaries));
            }
            if (row.DrawingObjects.Effects.HasRenderedGroupChildEffects)
            {
                parts.Add(
                    $"{row.DrawingObjects.Effects.RenderedGroupChildEffectObjectCount.ToString(CultureInfo.InvariantCulture)} rendered grouped child effect object(s): " +
                    string.Join("/", row.DrawingObjects.Effects.RenderedGroupChildEffectSummaries));
            }
            if (row.DrawingObjects.Effects.HasPlannedGroupChildEffects)
            {
                parts.Add(
                    $"{row.DrawingObjects.Effects.PlannedGroupChildEffectObjectCount.ToString(CultureInfo.InvariantCulture)} planned grouped child effect object(s): " +
                    string.Join("/", row.DrawingObjects.Effects.PlannedGroupChildEffectSummaries));
            }
        }
        if (row.ChartSmartArt.ChartCount > 0 || row.ChartSmartArt.SmartArtCount > 0)
        {
            parts.Add(
                $"{row.ChartSmartArt.ChartCount.ToString(CultureInfo.InvariantCulture)} chart(s), " +
                $"{row.ChartSmartArt.SmartArtCount.ToString(CultureInfo.InvariantCulture)} SmartArt");
        }
        if (row.HeaderFooters.ImageCount > 0)
        {
            parts.Add(
                $"{row.HeaderFooters.ImageCount.ToString(CultureInfo.InvariantCulture)} header/footer image(s), " +
                $"{row.HeaderFooters.SlotCount.ToString(CultureInfo.InvariantCulture)} slot(s)");
        }
        if (row.ProofingDiagnostics.DiagnosticCount > 0)
        {
            parts.Add(
                $"{row.ProofingDiagnostics.DiagnosticCount.ToString(CultureInfo.InvariantCulture)} proofing diagnostic(s), " +
                $"{row.ProofingDiagnostics.SpellingCount.ToString(CultureInfo.InvariantCulture)} spelling, " +
                $"{row.ProofingDiagnostics.GrammarCount.ToString(CultureInfo.InvariantCulture)} grammar");
        }
        if (row.ReviewProtection.IsProtected)
        {
            parts.Add(
                $"protection {row.ReviewProtection.ProtectionMode}, " +
                $"{row.ReviewProtection.Operations.Count.ToString(CultureInfo.InvariantCulture)} command decision(s)");
        }

        return string.Join(", ", parts);
    }

    private static void AppendWordBaselineTriage(
        StringBuilder sb,
        IReadOnlyList<FreeWVisualBaselineTriageItem> triage)
    {
        var unavailableRows = triage
            .Where(item => string.Equals(
                item.Status,
                FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (unavailableRows.Count > 0)
        {
            sb.AppendLine(
                $"Word baseline unavailable: {unavailableRows.Count.ToString(CultureInfo.InvariantCulture)} row(s). Trust remains passed for unavailable rows.");
            var unavailableReasons = unavailableRows
                .Select(item => item.Note)
                .Where(note => !string.IsNullOrWhiteSpace(note) && note != "-")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(note => note, StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            if (unavailableReasons.Count > 0)
                sb.AppendLine($"Unavailable reason(s): {EscapeMarkdown(string.Join("; ", unavailableReasons))}");

            sb.AppendLine();
        }

        var skippedRows = triage
            .Count(item => string.Equals(
                item.Status,
                FreeWVisualBaselineComparisonPlanner.SkippedStatus,
                StringComparison.OrdinalIgnoreCase));
        var tableRows = triage
            .Where(item => !IsHiddenFromTriageTable(item))
            .ToList();

        if (skippedRows > 0)
        {
            sb.AppendLine(
                $"Skipped rows hidden from triage table: {skippedRows.ToString(CultureInfo.InvariantCulture)}. Raw skipped rows remain in the Word Baseline Comparison table below.");
            sb.AppendLine();
        }

        sb.AppendLine($"Triage counts: {EscapeMarkdown(FormatTriageStatusCounts(triage))}");
        sb.AppendLine();

        if (tableRows.Count == 0)
        {
            sb.AppendLine("No actionable Word-baseline rows to show.");
            return;
        }

        sb.AppendLine("| Host | Scenario | Output | Triage | Status | Changed Pixels | Mean Delta (channel/gray) | Tolerance | Baseline | Note |");
        sb.AppendLine("| --- | --- | --- | --- | --- | ---: | ---: | --- | --- | --- |");
        foreach (var item in tableRows)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(item.HostId)} | {EscapeMarkdown(item.ScenarioId)} | " +
                $"{EscapeMarkdown(FormatTriageOutput(item))} | " +
                $"{EscapeMarkdown(item.TriageStatus)} | " +
                $"{EscapeMarkdown(item.Status)} | " +
                $"{FormatTriageChangedPixels(item)} | " +
                $"{FormatNullableMetricPair(item.MeanAbsoluteChannelDelta, item.MeanAbsoluteGrayscaleDelta)} | " +
                $"{EscapeMarkdown(item.ToleranceSummary)} | " +
                $"{EscapeMarkdown(item.BaselinePathSummary)} | " +
                $"{EscapeMarkdown(item.Note)} |");
        }
    }

    private static FreeWVisualBaselineTriageItem BuildWordBaselineTriageItem(
        FreeWVisualBaselineComparison comparison)
    {
        var metrics = comparison.Metrics;
        return new FreeWVisualBaselineTriageItem(
            comparison.HostId,
            comparison.ScenarioId,
            Math.Max(1, comparison.PageNumber),
            comparison.OutputName,
            comparison.Status,
            ClassifyWordBaselineTriage(comparison),
            comparison.BaselineId,
            FormatTriageBaselinePath(comparison),
            metrics?.ChangedPixels,
            metrics?.ComparedPixels,
            metrics?.ChangedPixelRatio,
            metrics?.MeanAbsoluteChannelDelta,
            metrics?.MeanAbsoluteGrayscaleDelta,
            FormatTriageTolerance(comparison.Tolerance),
            FormatComparisonNotes(comparison));
    }

    private static bool IsHiddenFromTriageTable(FreeWVisualBaselineTriageItem item) =>
        string.Equals(item.Status, FreeWVisualBaselineComparisonPlanner.SkippedStatus, StringComparison.OrdinalIgnoreCase)
        || string.Equals(item.Status, FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus, StringComparison.OrdinalIgnoreCase);

    private static int WordBaselineTriageStatusPriority(string status)
    {
        if (string.Equals(status, FreeWVisualBaselineComparisonPlanner.FailedStatus, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, FreeWVisualBaselineComparisonPlanner.DecodeFailedStatus, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(status, FreeWVisualBaselineComparisonPlanner.PassedStatus, StringComparison.OrdinalIgnoreCase))
            return 1;

        if (string.Equals(status, FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus, StringComparison.OrdinalIgnoreCase))
            return 2;

        if (string.Equals(status, FreeWVisualBaselineComparisonPlanner.SkippedStatus, StringComparison.OrdinalIgnoreCase))
            return 3;

        return 4;
    }

    private static string FormatBaselineStatusCounts(
        IReadOnlyList<FreeWVisualBaselineComparison> comparisons) =>
        string.Join(
            ", ",
            comparisons
                .GroupBy(c => c.Status, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => $"{g.Key}={g.Count().ToString(CultureInfo.InvariantCulture)}"));

    private static string FormatBaselineEvidenceClassCounts(
        IReadOnlyList<FreeWVisualBaselineComparison> comparisons) =>
        string.Join(
            ", ",
            comparisons
                .GroupBy(c => c.BaselineEvidenceClass, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => WordBaselineEvidenceClassPriority(g.Key))
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => $"{g.Key}={g.Count().ToString(CultureInfo.InvariantCulture)}"));

    private static string FormatBaselineEvidenceClassLegend(
        IReadOnlyList<FreeWVisualBaselineComparison> comparisons)
    {
        var classes = comparisons
            .Select(c => c.BaselineEvidenceClass)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(WordBaselineEvidenceClassPriority)
            .ThenBy(evidenceClass => evidenceClass, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return classes.Count == 0
            ? "-"
            : string.Join(
                "; ",
                classes.Select(evidenceClass =>
                    evidenceClass + "=" + FreeWVisualBaselineComparisonPlanner.DescribeBaselineEvidenceClass(evidenceClass)));
    }

    private static string FormatTriageStatusCounts(
        IReadOnlyList<FreeWVisualBaselineTriageItem> triage) =>
        string.Join(
            ", ",
            triage
                .GroupBy(item => item.TriageStatus, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => WordBaselineTriageActionPriority(g.Key))
                .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => $"{g.Key}={g.Count().ToString(CultureInfo.InvariantCulture)}"));

    private static string FormatBaselinePath(FreeWVisualBaselineComparison comparison)
    {
        if (!string.IsNullOrWhiteSpace(comparison.BaselinePath))
            return comparison.BaselinePath;

        if (comparison.CandidateBaselinePaths.Count > 0)
            return string.Join(", ", comparison.CandidateBaselinePaths);

        return "-";
    }

    private static string FormatTriageBaselinePath(FreeWVisualBaselineComparison comparison)
    {
        if (!string.IsNullOrWhiteSpace(comparison.BaselinePath))
            return comparison.BaselinePath;

        if (comparison.CandidateBaselinePaths.Count == 0)
            return "-";

        if (comparison.CandidateBaselinePaths.Count <= 2)
            return "candidates: " + string.Join(", ", comparison.CandidateBaselinePaths);

        return "candidates: "
            + string.Join(", ", comparison.CandidateBaselinePaths.Take(2))
            + ", +"
            + (comparison.CandidateBaselinePaths.Count - 2).ToString(CultureInfo.InvariantCulture)
            + " more";
    }

    private static string FormatTriageOutput(FreeWVisualBaselineTriageItem item) =>
        string.Concat(
            "p",
            item.PageNumber.ToString(CultureInfo.InvariantCulture),
            "/",
            item.OutputName);

    private static string FormatNullableMetricPair(double? channel, double? grayscale)
    {
        if (!channel.HasValue || !grayscale.HasValue)
            return "-";

        return string.Concat(
            channel.Value.ToString("0.####", CultureInfo.InvariantCulture),
            "/",
            grayscale.Value.ToString("0.####", CultureInfo.InvariantCulture));
    }

    private static string FormatTriageChangedPixels(FreeWVisualBaselineTriageItem item)
    {
        if (!item.ChangedPixels.HasValue || !item.ComparedPixels.HasValue || !item.ChangedPixelRatio.HasValue)
            return "-";

        return string.Concat(
            item.ChangedPixels.Value.ToString(CultureInfo.InvariantCulture),
            "/",
            item.ComparedPixels.Value.ToString(CultureInfo.InvariantCulture),
            " (",
            item.ChangedPixelRatio.Value.ToString("P3", CultureInfo.InvariantCulture),
            ")");
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

    private static string FormatTriageTolerance(FreeWVisualBaselineComparisonTolerance tolerance) =>
        string.Concat(
            tolerance.Name,
            ": changed <= ",
            tolerance.MaxChangedPixelRatio.ToString("P3", CultureInfo.InvariantCulture),
            ", mean <= ",
            tolerance.MaxMeanAbsoluteChannelDelta.ToString("0.####", CultureInfo.InvariantCulture),
            "/",
            tolerance.MaxMeanAbsoluteGrayscaleDelta.ToString("0.####", CultureInfo.InvariantCulture),
            ", pixel delta > ",
            tolerance.ChangedPixelDeltaThreshold.ToString(CultureInfo.InvariantCulture),
            ", dimensions ",
            tolerance.RequireDimensionMatch ? "must match" : "may resize");

    private static string ClassifyWordBaselineTriage(FreeWVisualBaselineComparison comparison)
    {
        if (string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.FailedStatus, StringComparison.OrdinalIgnoreCase))
            return "needs-render-review";
        if (string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.DecodeFailedStatus, StringComparison.OrdinalIgnoreCase))
            return "needs-decode-fix";
        if (string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus, StringComparison.OrdinalIgnoreCase))
            return "needs-baseline";
        if (string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.PassedStatus, StringComparison.OrdinalIgnoreCase))
            return "within-tolerance";
        if (string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus, StringComparison.OrdinalIgnoreCase))
            return "word-unavailable";
        if (string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.SkippedStatus, StringComparison.OrdinalIgnoreCase))
            return "not-in-scope";

        return "unknown";
    }

    private static int WordBaselineTriageActionPriority(string triageStatus)
    {
        if (string.Equals(triageStatus, "needs-render-review", StringComparison.OrdinalIgnoreCase)
            || string.Equals(triageStatus, "needs-decode-fix", StringComparison.OrdinalIgnoreCase)
            || string.Equals(triageStatus, "needs-baseline", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(triageStatus, "within-tolerance", StringComparison.OrdinalIgnoreCase))
            return 1;

        if (string.Equals(triageStatus, "word-unavailable", StringComparison.OrdinalIgnoreCase))
            return 2;

        if (string.Equals(triageStatus, "not-in-scope", StringComparison.OrdinalIgnoreCase))
            return 3;

        return 4;
    }

    private static int WordBaselineEvidenceClassPriority(string evidenceClass)
    {
        if (string.Equals(evidenceClass, FreeWVisualBaselineComparisonPlanner.RealWordPngComparisonFailedClass, StringComparison.OrdinalIgnoreCase)
            || string.Equals(evidenceClass, FreeWVisualBaselineComparisonPlanner.PngDecodeFailedClass, StringComparison.OrdinalIgnoreCase)
            || string.Equals(evidenceClass, FreeWVisualBaselineComparisonPlanner.WordPngBaselineMissingClass, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(evidenceClass, FreeWVisualBaselineComparisonPlanner.RealWordPngComparedClass, StringComparison.OrdinalIgnoreCase))
            return 1;

        if (string.Equals(evidenceClass, FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableClass, StringComparison.OrdinalIgnoreCase))
            return 2;

        if (string.Equals(evidenceClass, FreeWVisualBaselineComparisonPlanner.ScenarioSkippedOrUnmappedClass, StringComparison.OrdinalIgnoreCase))
            return 3;

        return 4;
    }

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
