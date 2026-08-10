using System.Globalization;
using System.Text;
using System.Text.Json;
using Free.ToolsShared;
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
    FreeWVisualEquationExpectation Equations,
    FreeWVisualHeaderFooterExpectation HeaderFooters,
    FreeWVisualTableOfAuthoritiesExpectation TableOfAuthorities,
    FreeWVisualProofingDiagnosticExpectation ProofingDiagnostics,
    FreeWVisualReviewProtectionExpectation ReviewProtection,
    FreeWVisualReviewMarkupExpectation ReviewMarkup,
    FreeWVisualReviewCompareCombineExpectation ReviewCompareCombine,
    bool HasFootnotes,
    bool HasEndnotes,
    bool IsSyntheticPage,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualEvidenceNormalizedSummary(
    string SchemaId,
    int SchemaVersion,
    IReadOnlyList<FreeWVisualEvidenceNormalizedSource> Sources,
    IReadOnlyList<FreeWVisualEvidenceExpectedScenario> ExpectedScenarios,
    IReadOnlyList<FreeWVisualEvidenceNormalizedScenario> Scenarios,
    IReadOnlyList<FreeWVisualEvidenceNormalizedRow> Evidence,
    IReadOnlyList<FreeWVisualEvidenceBackstagePrintReadiness> BackstagePrintEvidenceReadiness,
    IReadOnlyList<FreeWVisualNotePlacementProofReadiness> NotePlacementProofReadiness,
    IReadOnlyList<FreeWVisualSectionGeometryProofReadiness> SectionGeometryProofReadiness,
    IReadOnlyList<FreeWVisualFloatingWrappingProofReadiness> FloatingWrappingProofReadiness,
    IReadOnlyList<FreeWVisualHeaderFooterImageProofReadiness> HeaderFooterImageProofReadiness,
    IReadOnlyList<FreeWVisualTablePaginationProofReadiness> TablePaginationProofReadiness,
    IReadOnlyList<FreeWVisualDrawingObjectProofReadiness> DrawingObjectProofReadiness,
    IReadOnlyList<FreeWVisualWordArtWatermarkProofReadiness> WordArtWatermarkProofReadiness,
    IReadOnlyList<FreeWVisualReviewMarkupProofReadiness> ReviewMarkupProofReadiness,
    IReadOnlyList<FreeWVisualReviewCompareCombineProofReadiness> ReviewCompareCombineProofReadiness,
    IReadOnlyList<FreeWVisualReviewProofingProofReadiness> ReviewProofingProofReadiness,
    IReadOnlyList<FreeWVisualReferencesHeavyProofReadiness> ReferencesHeavyProofReadiness,
    IReadOnlyList<FreeWVisualLegalReferenceProofReadiness> LegalReferenceProofReadiness,
    IReadOnlyList<FreeWVisualBaselineComparison> BaselineComparisons,
    IReadOnlyList<FreeWVisualBaselineTriageItem> WordBaselineTriage,
    FreeWVisualEvidenceAuthoritySummary EvidenceAuthority,
    IReadOnlyList<FreeWVisualRemainingEvidenceBlocker> RemainingEvidenceBlockers,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualEvidenceBackstagePrintReadiness(
    string ScenarioId,
    string HostId,
    int PageNumber,
    string Status,
    string OutputSummary,
    string Notes);

public sealed record FreeWVisualNotePlacementProofReadiness(
    string ScenarioId,
    int PageNumber,
    string Status,
    string WpfOutputSummary,
    string AvaloniaOutputSummary,
    string WordBaselineStatus,
    string BaselineReadiness,
    string SemanticEvidence,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualSectionGeometryProofReadiness(
    string ScenarioId,
    int PageNumber,
    string Status,
    string WpfOutputSummary,
    string AvaloniaOutputSummary,
    string WordBaselineStatus,
    string BaselineReadiness,
    string SemanticEvidence,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualFloatingWrappingProofReadiness(
    string ScenarioId,
    int PageNumber,
    string Status,
    string WpfScenarioId,
    string WpfOutputSummary,
    string AvaloniaScenarioId,
    string AvaloniaOutputSummary,
    string WordBaselineStatus,
    string BaselineReadiness,
    string SemanticEvidence,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualHeaderFooterImageProofReadiness(
    string ScenarioId,
    int PageNumber,
    string Status,
    string WpfOutputSummary,
    string AvaloniaOutputSummary,
    string WordBaselineStatus,
    string BaselineReadiness,
    string SemanticEvidence,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualTablePaginationProofReadiness(
    string ScenarioId,
    int PageNumber,
    string Status,
    string WpfOutputSummary,
    string AvaloniaOutputSummary,
    string WordBaselineStatus,
    string BaselineReadiness,
    string SemanticEvidence,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualDrawingObjectProofReadiness(
    string ScenarioId,
    int PageNumber,
    string Status,
    string WpfOutputSummary,
    string AvaloniaOutputSummary,
    string WordBaselineStatus,
    string BaselineReadiness,
    string SemanticEvidence,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualWordArtWatermarkProofReadiness(
    string ScenarioId,
    int PageNumber,
    string Status,
    string WpfOutputSummary,
    string AvaloniaOutputSummary,
    string WordBaselineStatus,
    string BaselineReadiness,
    string SemanticEvidence,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualReviewMarkupProofReadiness(
    string ScenarioId,
    int PageNumber,
    string Status,
    string WpfOutputSummary,
    string AvaloniaOutputSummary,
    string WordBaselineStatus,
    string BaselineReadiness,
    string SemanticEvidence,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualReviewCompareCombineProofReadiness(
    string ScenarioId,
    int PageNumber,
    string Status,
    string WpfOutputSummary,
    string AvaloniaOutputSummary,
    string WordBaselineStatus,
    string BaselineReadiness,
    string SemanticEvidence,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualReviewProofingProofReadiness(
    string ScenarioId,
    int PageNumber,
    string Status,
    string WpfOutputSummary,
    string AvaloniaOutputSummary,
    string WordBaselineStatus,
    string BaselineReadiness,
    string SemanticEvidence,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualReferencesHeavyProofReadiness(
    string ScenarioId,
    int PageNumber,
    string Status,
    string WpfOutputSummary,
    string AvaloniaOutputSummary,
    string WordBaselineStatus,
    string BaselineReadiness,
    string SemanticEvidence,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualLegalReferenceProofReadiness(
    string ScenarioId,
    int PageNumber,
    string Status,
    string WpfOutputSummary,
    string AvaloniaOutputSummary,
    string WordBaselineStatus,
    string BaselineReadiness,
    string SemanticEvidence,
    FreeWVisualEvidenceTrust Trust);

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

public sealed record FreeWVisualEvidenceAuthoritySummary(
    string AuthorityLevel,
    bool AuthoritativeWordPngParityClaimed,
    int TrustedEvidenceRows,
    int ComparableWordBaselineRows,
    int RealWordPngComparedRows,
    int WordBaselineUnavailableRows,
    int MissingWordBaselineRows,
    int FailedOrDecodeFailedRows,
    int SkippedOrUnmappedRows,
    int PreparatoryEvidenceRows,
    IReadOnlyList<string> Notes);

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
    public const int SummarySchemaVersion = 50;
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
    public static IReadOnlyList<string> NotePlacementVisualProofScenarioIds { get; } =
        NoteRendererScenarioIds;
    public static IReadOnlyList<string> SectionGeometryRendererScenarioIds { get; } =
    [
        "f2-section-landscape"
    ];
    public static IReadOnlyList<string> SectionGeometryVisualProofScenarioIds { get; } =
        SectionGeometryRendererScenarioIds;
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
        "review-protection-proofing-comments-only",
        "review-compare-visual-proof",
        "review-combine-visual-proof"
    ];
    public static IReadOnlyList<string> TableRendererScenarioIds { get; } =
    [
        "table-layout-complex",
        "table-pagination-repeat-header",
        "table-page-composition-stress"
    ];
    public static IReadOnlyList<string> TablePaginationVisualProofScenarioIds { get; } =
    [
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
        "references-heavy-fields",
        "legal-reference-section-page-numbers"
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
    public static IReadOnlyList<string> HeaderFooterImageVisualProofScenarioIds { get; } =
    [
        "f2-hf-images"
    ];
    public static IReadOnlyList<string> DrawingObjectVisualProofScenarioIds { get; } =
        DrawingObjectRendererScenarioIds
            .Concat(ChartSmartArtRendererScenarioIds)
            .Concat(WordArtWatermarkRendererScenarioIds)
            .ToArray();
    public static IReadOnlyList<string> WordArtWatermarkVisualProofScenarioIds { get; } =
    [
        "wordart-watermark-stress",
        "wordart-picture-watermark-layout"
    ];
    public const string FloatingWrappingWpfScenarioId = "f2-01-float-wrap";
    public const string FloatingWrappingAvaloniaScenarioId = "page-composition-floating-image";
    public const string FloatingWrappingProofScenarioId = "floating-wrapping-visual-proof";
    public static IReadOnlyList<string> FloatingWrappingVisualProofScenarioIds { get; } =
    [
        FloatingWrappingWpfScenarioId,
        FloatingWrappingAvaloniaScenarioId
    ];
    public static IReadOnlyList<string> ReviewCompareCombineVisualProofScenarioIds { get; } =
    [
        "review-compare-visual-proof",
        "review-combine-visual-proof"
    ];
    public static IReadOnlyList<string> ReviewMarkupVisualProofScenarioIds { get; } =
    [
        "f2-tracked-changes",
        "f2-comments"
    ];
    public static IReadOnlyList<string> ReviewProofingVisualProofScenarioIds { get; } =
    [
        "review-proofing-visual-depth",
        "review-protection-proofing-comments-only"
    ];
    public const string ReferencesHeavyProofScenarioId = "references-heavy-fields";
    public const string LegalReferenceSectionPageProofScenarioId = "legal-reference-section-page-numbers";

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

    private static readonly string[] LegalReferenceRequiredToaPageReferenceSignatures =
    [
        "category=Cases|entry=Matter of Sectioned Pages, 101 F. Supp. 3d 2026 (D. FreeW)|kind=section-formatted-page-numbers|pages=1,2|text=i, 1",
        "category=Statutes|entry=Restart Numbering Act, 7 FreeW Code 13|kind=explicit-page-numbers|pages=2|text=1"
    ];

    private static readonly (string Kind, int MinimumCount)[] EquationStructureRequiredElementKinds =
    [
        ("Fraction", 1),
        ("Radical", 1),
        ("NAry", 2),
        ("Matrix", 1),
        ("EquationArray", 1),
        ("Accent", 1),
        ("Bar", 2),
        ("Delimiter", 1),
        ("GroupChar", 2),
        ("FunctionApply", 2)
    ];

    private static readonly string[] EquationStructureRequiredGeometryTokens =
    [
        "geometry=script",
        "geometry=fraction",
        "geometry=radical",
        "geometry=nary",
        "geometry=matrix",
        "geometry=equationarray",
        "geometry=accent",
        "geometry=bar",
        "geometry=delimiter",
        "geometry=groupchar",
        "geometry=function-apply"
    ];

    private static readonly string[] EquationStructureRequiredSpacingTokens =
    [
        "spacing=script",
        "spacing=fraction",
        "spacing=radical",
        "spacing=nary",
        "spacing=matrix",
        "spacing=equationarray"
    ];

    private static readonly (string Role, int MinimumCount)[] EquationStructureRequiredSegmentRoles =
    [
        ("Superscript", 2),
        ("Subscript", 2),
        ("FractionNumerator", 1),
        ("FractionDenominator", 1),
        ("RadicalDegree", 1),
        ("RadicalRadicand", 1),
        ("NAryLowerLimit", 2),
        ("NAryUpperLimit", 2),
        ("NAryOperand", 2),
        ("MatrixCell", 6),
        ("AccentMark", 1),
        ("BarMark", 2),
        ("DelimiterContent", 1),
        ("GroupCharMark", 2),
        ("FunctionArgument", 2)
    ];

    public static IReadOnlyList<FreeWVisualEvidenceExpectedScenario> DefaultExpectedScenarios { get; } =
        BuildDefaultExpectedScenarios();

    public static FreeWVisualEvidenceManifest ReadManifest(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        return VisualEvidenceManifestIO.Read<FreeWVisualEvidenceManifest>(
            manifestPath,
            JsonOptions,
            invalidExceptionFactory: () => new InvalidOperationException(
                $"Visual evidence manifest could not be read: {Path.GetFileName(manifestPath)}"));
    }

    public static FreeWVisualEvidenceNormalizedSummary BuildNormalizedSummaryFromFiles(
        IReadOnlyList<string> manifestPaths,
        string runRoot,
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario>? expectedScenarios = null,
        IReadOnlyCollection<string>? includedScenarioIds = null,
        bool allowNoWordFallbackEvidence = false)
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
        var usingDefaultExpectedScenarios = expectedScenarios is null;
        var expected = (expectedScenarios ?? DefaultExpectedScenarios)
            .Where(e => included is null || included.Count == 0 || included.Contains(e.ScenarioId))
            .OrderBy(e => e.HostId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (allowNoWordFallbackEvidence && usingDefaultExpectedScenarios)
        {
            expected = expected
                .Where(e => !IsDefaultNoWordFallbackOptionalScenario(e))
                .ToList();
        }
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
            var sourceManifestPath = VisualEvidencePathPolicy.NormalizeRelativePath(normalizedRoot, fullManifestPath);
            if (!VisualEvidencePathPolicy.IsContained(
                    normalizedRoot,
                    fullManifestPath,
                    StringComparison.OrdinalIgnoreCase))
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
                    failures,
                    allowNoWordFallbackEvidence));
            }
        }

        var scenarios = BuildScenarioSummaries(rows, expected, expectedByKey, failures);
        ValidateBackstageRendererPairs(rows, failures);
        ValidateNoteRendererPairs(rows, failures);
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
        var notePlacementProofReadiness = BuildNotePlacementProofReadinessRows(expected, orderedRows, []);
        var sectionGeometryProofReadiness = BuildSectionGeometryProofReadinessRows(expected, orderedRows, []);
        var floatingWrappingProofReadiness = BuildFloatingWrappingProofReadinessRows(expected, orderedRows, []);
        var headerFooterImageProofReadiness = BuildHeaderFooterImageProofReadinessRows(expected, orderedRows, []);
        var tablePaginationProofReadiness = BuildTablePaginationProofReadinessRows(expected, orderedRows, []);
        var drawingObjectProofReadiness = BuildDrawingObjectProofReadinessRows(expected, orderedRows, []);
        var wordArtWatermarkProofReadiness = BuildWordArtWatermarkProofReadinessRows(expected, orderedRows, []);
        var reviewMarkupProofReadiness = BuildReviewMarkupProofReadinessRows(expected, orderedRows, []);
        var reviewCompareCombineProofReadiness = BuildReviewCompareCombineProofReadinessRows(expected, orderedRows, []);
        var reviewProofingProofReadiness = BuildReviewProofingProofReadinessRows(expected, orderedRows, []);
        var referencesHeavyProofReadiness = BuildReferencesHeavyProofReadinessRows(expected, orderedRows, []);
        var legalReferenceProofReadiness = BuildLegalReferenceProofReadinessRows(expected, orderedRows, []);
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
            notePlacementProofReadiness,
            sectionGeometryProofReadiness,
            floatingWrappingProofReadiness,
            headerFooterImageProofReadiness,
            tablePaginationProofReadiness,
            drawingObjectProofReadiness,
            wordArtWatermarkProofReadiness,
            reviewMarkupProofReadiness,
            reviewCompareCombineProofReadiness,
            reviewProofingProofReadiness,
            referencesHeavyProofReadiness,
            legalReferenceProofReadiness,
            [],
            [],
            BuildEvidenceAuthoritySummary(orderedRows, []),
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
        return VisualEvidenceManifestIO.Serialize(summary, JsonOptions);
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

        AppendEvidenceAuthoritySummary(sb, summary.EvidenceAuthority);

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
        AppendNotePlacementProofReadiness(sb, summary);
        AppendSectionGeometryProofReadiness(sb, summary);
        AppendFloatingWrappingProofReadiness(sb, summary);
        AppendHeaderFooterImageProofReadiness(sb, summary);
        AppendTablePaginationProofReadiness(sb, summary);
        AppendDrawingObjectProofReadiness(sb, summary);
        AppendWordArtWatermarkProofReadiness(sb, summary);
        AppendReviewMarkupProofReadiness(sb, summary);
        AppendReviewCompareCombineProofReadiness(sb, summary);
        AppendReviewProofingProofReadiness(sb, summary);
        AppendReferencesHeavyProofReadiness(sb, summary);
        AppendLegalReferenceProofReadiness(sb, summary);
        AppendEquationGeometryEvidence(sb, summary);

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

    private static void AppendFloatingWrappingProofReadiness(
        StringBuilder sb,
        FreeWVisualEvidenceNormalizedSummary summary)
    {
        if (summary.FloatingWrappingProofReadiness.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## Floating/Wrapping Visual Proof Readiness");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Page | Status | WPF Scenario | WPF Output | Avalonia Scenario | Avalonia Output | Word Baseline | Baseline Readiness | Semantic Evidence | Trust |");
        sb.AppendLine("| --- | ---: | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var row in summary.FloatingWrappingProofReadiness)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(row.ScenarioId)} | " +
                $"{row.PageNumber.ToString(CultureInfo.InvariantCulture)} | " +
                $"{EscapeMarkdown(row.Status)} | " +
                $"{EscapeMarkdown(row.WpfScenarioId)} | " +
                $"{EscapeMarkdown(row.WpfOutputSummary)} | " +
                $"{EscapeMarkdown(row.AvaloniaScenarioId)} | " +
                $"{EscapeMarkdown(row.AvaloniaOutputSummary)} | " +
                $"{EscapeMarkdown(row.WordBaselineStatus)} | " +
                $"{EscapeMarkdown(row.BaselineReadiness)} | " +
                $"{EscapeMarkdown(row.SemanticEvidence)} | " +
                $"{(row.Trust.Passed ? "passed" : "failed")} |");
        }
    }

    private static void AppendNotePlacementProofReadiness(
        StringBuilder sb,
        FreeWVisualEvidenceNormalizedSummary summary)
    {
        if (summary.NotePlacementProofReadiness.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## Note Placement Visual Proof Readiness");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Page | Status | WPF Output | Avalonia Output | Word Baseline | Baseline Readiness | Semantic Evidence | Trust |");
        sb.AppendLine("| --- | ---: | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var row in summary.NotePlacementProofReadiness)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(row.ScenarioId)} | " +
                $"{row.PageNumber.ToString(CultureInfo.InvariantCulture)} | " +
                $"{EscapeMarkdown(row.Status)} | " +
                $"{EscapeMarkdown(row.WpfOutputSummary)} | " +
                $"{EscapeMarkdown(row.AvaloniaOutputSummary)} | " +
                $"{EscapeMarkdown(row.WordBaselineStatus)} | " +
                $"{EscapeMarkdown(row.BaselineReadiness)} | " +
                $"{EscapeMarkdown(row.SemanticEvidence)} | " +
                $"{(row.Trust.Passed ? "passed" : "failed")} |");
        }
    }

    private static void AppendSectionGeometryProofReadiness(
        StringBuilder sb,
        FreeWVisualEvidenceNormalizedSummary summary)
    {
        if (summary.SectionGeometryProofReadiness.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## Section Geometry Visual Proof Readiness");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Page | Status | WPF Output | Avalonia Output | Word Baseline | Baseline Readiness | Semantic Evidence | Trust |");
        sb.AppendLine("| --- | ---: | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var row in summary.SectionGeometryProofReadiness)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(row.ScenarioId)} | " +
                $"{row.PageNumber.ToString(CultureInfo.InvariantCulture)} | " +
                $"{EscapeMarkdown(row.Status)} | " +
                $"{EscapeMarkdown(row.WpfOutputSummary)} | " +
                $"{EscapeMarkdown(row.AvaloniaOutputSummary)} | " +
                $"{EscapeMarkdown(row.WordBaselineStatus)} | " +
                $"{EscapeMarkdown(row.BaselineReadiness)} | " +
                $"{EscapeMarkdown(row.SemanticEvidence)} | " +
                $"{(row.Trust.Passed ? "passed" : "failed")} |");
        }
    }

    private static void AppendTablePaginationProofReadiness(
        StringBuilder sb,
        FreeWVisualEvidenceNormalizedSummary summary)
    {
        if (summary.TablePaginationProofReadiness.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## Table Pagination/Page Composition Proof Readiness");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Page | Status | WPF Output | Avalonia Output | Word Baseline | Baseline Readiness | Semantic Evidence | Trust |");
        sb.AppendLine("| --- | ---: | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var row in summary.TablePaginationProofReadiness)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(row.ScenarioId)} | " +
                $"{row.PageNumber.ToString(CultureInfo.InvariantCulture)} | " +
                $"{EscapeMarkdown(row.Status)} | " +
                $"{EscapeMarkdown(row.WpfOutputSummary)} | " +
                $"{EscapeMarkdown(row.AvaloniaOutputSummary)} | " +
                $"{EscapeMarkdown(row.WordBaselineStatus)} | " +
                $"{EscapeMarkdown(row.BaselineReadiness)} | " +
                $"{EscapeMarkdown(row.SemanticEvidence)} | " +
                $"{(row.Trust.Passed ? "passed" : "failed")} |");
        }
    }

    private static void AppendHeaderFooterImageProofReadiness(
        StringBuilder sb,
        FreeWVisualEvidenceNormalizedSummary summary)
    {
        if (summary.HeaderFooterImageProofReadiness.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## Header/Footer Image Visual Proof Readiness");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Page | Status | WPF Output | Avalonia Output | Word Baseline | Baseline Readiness | Semantic Evidence | Trust |");
        sb.AppendLine("| --- | ---: | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var row in summary.HeaderFooterImageProofReadiness)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(row.ScenarioId)} | " +
                $"{row.PageNumber.ToString(CultureInfo.InvariantCulture)} | " +
                $"{EscapeMarkdown(row.Status)} | " +
                $"{EscapeMarkdown(row.WpfOutputSummary)} | " +
                $"{EscapeMarkdown(row.AvaloniaOutputSummary)} | " +
                $"{EscapeMarkdown(row.WordBaselineStatus)} | " +
                $"{EscapeMarkdown(row.BaselineReadiness)} | " +
                $"{EscapeMarkdown(row.SemanticEvidence)} | " +
                $"{(row.Trust.Passed ? "passed" : "failed")} |");
        }
    }

    private static void AppendDrawingObjectProofReadiness(
        StringBuilder sb,
        FreeWVisualEvidenceNormalizedSummary summary)
    {
        if (summary.DrawingObjectProofReadiness.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## Drawing/Object Visual Proof Readiness");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Page | Status | WPF Output | Avalonia Output | Word Baseline | Baseline Readiness | Semantic Evidence | Trust |");
        sb.AppendLine("| --- | ---: | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var row in summary.DrawingObjectProofReadiness)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(row.ScenarioId)} | " +
                $"{row.PageNumber.ToString(CultureInfo.InvariantCulture)} | " +
                $"{EscapeMarkdown(row.Status)} | " +
                $"{EscapeMarkdown(row.WpfOutputSummary)} | " +
                $"{EscapeMarkdown(row.AvaloniaOutputSummary)} | " +
                $"{EscapeMarkdown(row.WordBaselineStatus)} | " +
                $"{EscapeMarkdown(row.BaselineReadiness)} | " +
                $"{EscapeMarkdown(row.SemanticEvidence)} | " +
                $"{(row.Trust.Passed ? "passed" : "failed")} |");
        }
    }

    private static void AppendWordArtWatermarkProofReadiness(
        StringBuilder sb,
        FreeWVisualEvidenceNormalizedSummary summary)
    {
        if (summary.WordArtWatermarkProofReadiness.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## WordArt/Watermark Visual Proof Readiness");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Page | Status | WPF Output | Avalonia Output | Word Baseline | Baseline Readiness | Semantic Evidence | Trust |");
        sb.AppendLine("| --- | ---: | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var row in summary.WordArtWatermarkProofReadiness)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(row.ScenarioId)} | " +
                $"{row.PageNumber.ToString(CultureInfo.InvariantCulture)} | " +
                $"{EscapeMarkdown(row.Status)} | " +
                $"{EscapeMarkdown(row.WpfOutputSummary)} | " +
                $"{EscapeMarkdown(row.AvaloniaOutputSummary)} | " +
                $"{EscapeMarkdown(row.WordBaselineStatus)} | " +
                $"{EscapeMarkdown(row.BaselineReadiness)} | " +
                $"{EscapeMarkdown(row.SemanticEvidence)} | " +
                $"{(row.Trust.Passed ? "passed" : "failed")} |");
        }
    }

    private static void AppendReviewCompareCombineProofReadiness(
        StringBuilder sb,
        FreeWVisualEvidenceNormalizedSummary summary)
    {
        if (summary.ReviewCompareCombineProofReadiness.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## Review Compare/Combine Visual Proof Readiness");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Page | Status | WPF Output | Avalonia Output | Word Baseline | Baseline Readiness | Semantic Evidence | Trust |");
        sb.AppendLine("| --- | ---: | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var row in summary.ReviewCompareCombineProofReadiness)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(row.ScenarioId)} | {row.PageNumber.ToString(CultureInfo.InvariantCulture)} | " +
                $"{EscapeMarkdown(row.Status)} | {EscapeMarkdown(row.WpfOutputSummary)} | " +
                $"{EscapeMarkdown(row.AvaloniaOutputSummary)} | {EscapeMarkdown(row.WordBaselineStatus)} | " +
                $"{EscapeMarkdown(row.BaselineReadiness)} | {EscapeMarkdown(row.SemanticEvidence)} | " +
                $"{(row.Trust.Passed ? "passed" : "failed")} |");
        }
    }

    private static void AppendReviewMarkupProofReadiness(
        StringBuilder sb,
        FreeWVisualEvidenceNormalizedSummary summary)
    {
        if (summary.ReviewMarkupProofReadiness.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## Review Markup Visual Proof Readiness");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Page | Status | WPF Output | Avalonia Output | Word Baseline | Baseline Readiness | Semantic Evidence | Trust |");
        sb.AppendLine("| --- | ---: | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var row in summary.ReviewMarkupProofReadiness)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(row.ScenarioId)} | {row.PageNumber.ToString(CultureInfo.InvariantCulture)} | " +
                $"{EscapeMarkdown(row.Status)} | {EscapeMarkdown(row.WpfOutputSummary)} | " +
                $"{EscapeMarkdown(row.AvaloniaOutputSummary)} | {EscapeMarkdown(row.WordBaselineStatus)} | " +
                $"{EscapeMarkdown(row.BaselineReadiness)} | {EscapeMarkdown(row.SemanticEvidence)} | " +
                $"{(row.Trust.Passed ? "passed" : "failed")} |");
        }
    }

    private static void AppendReviewProofingProofReadiness(
        StringBuilder sb,
        FreeWVisualEvidenceNormalizedSummary summary)
    {
        if (summary.ReviewProofingProofReadiness.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## Review Proofing Visual Proof Readiness");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Page | Status | WPF Output | Avalonia Output | Word Baseline | Baseline Readiness | Semantic Evidence | Trust |");
        sb.AppendLine("| --- | ---: | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var row in summary.ReviewProofingProofReadiness)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(row.ScenarioId)} | {row.PageNumber.ToString(CultureInfo.InvariantCulture)} | " +
                $"{EscapeMarkdown(row.Status)} | {EscapeMarkdown(row.WpfOutputSummary)} | " +
                $"{EscapeMarkdown(row.AvaloniaOutputSummary)} | {EscapeMarkdown(row.WordBaselineStatus)} | " +
                $"{EscapeMarkdown(row.BaselineReadiness)} | {EscapeMarkdown(row.SemanticEvidence)} | " +
                $"{(row.Trust.Passed ? "passed" : "failed")} |");
        }
    }

    private static void AppendLegalReferenceProofReadiness(
        StringBuilder sb,
        FreeWVisualEvidenceNormalizedSummary summary)
    {
        if (summary.LegalReferenceProofReadiness.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## Legal Reference Section Page-Number Proof Readiness");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Page | Status | WPF Output | Avalonia Output | Word Baseline | Baseline Readiness | Semantic Evidence | Trust |");
        sb.AppendLine("| --- | ---: | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var row in summary.LegalReferenceProofReadiness)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(row.ScenarioId)} | {row.PageNumber.ToString(CultureInfo.InvariantCulture)} | " +
                $"{EscapeMarkdown(row.Status)} | {EscapeMarkdown(row.WpfOutputSummary)} | " +
                $"{EscapeMarkdown(row.AvaloniaOutputSummary)} | {EscapeMarkdown(row.WordBaselineStatus)} | " +
                $"{EscapeMarkdown(row.BaselineReadiness)} | {EscapeMarkdown(row.SemanticEvidence)} | " +
                $"{(row.Trust.Passed ? "passed" : "failed")} |");
        }
    }

    private static void AppendReferencesHeavyProofReadiness(
        StringBuilder sb,
        FreeWVisualEvidenceNormalizedSummary summary)
    {
        if (summary.ReferencesHeavyProofReadiness.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## References-Heavy Field/TOA Proof Readiness");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Page | Status | WPF Output | Avalonia Output | Word Baseline | Baseline Readiness | Semantic Evidence | Trust |");
        sb.AppendLine("| --- | ---: | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var row in summary.ReferencesHeavyProofReadiness)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(row.ScenarioId)} | {row.PageNumber.ToString(CultureInfo.InvariantCulture)} | " +
                $"{EscapeMarkdown(row.Status)} | {EscapeMarkdown(row.WpfOutputSummary)} | " +
                $"{EscapeMarkdown(row.AvaloniaOutputSummary)} | {EscapeMarkdown(row.WordBaselineStatus)} | " +
                $"{EscapeMarkdown(row.BaselineReadiness)} | {EscapeMarkdown(row.SemanticEvidence)} | " +
                $"{(row.Trust.Passed ? "passed" : "failed")} |");
        }
    }

    private static void AppendEquationGeometryEvidence(
        StringBuilder sb,
        FreeWVisualEvidenceNormalizedSummary summary)
    {
        var rows = summary.Evidence
            .Where(row => row.Equations.EquationCount > 0)
            .ToList();
        if (rows.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("## Equation Geometry Evidence");
        sb.AppendLine();
        sb.AppendLine("| Host | Scenario | Page | Counts | Roles | Geometry Signatures | Spacing Signatures |");
        sb.AppendLine("| --- | --- | ---: | --- | --- | --- | --- |");
        foreach (var row in rows)
        {
            var equations = row.Equations;
            var counts = string.Join(
                ", ",
                $"{equations.EquationCount.ToString(CultureInfo.InvariantCulture)} equation(s)",
                $"{equations.ElementCount.ToString(CultureInfo.InvariantCulture)} element(s)",
                $"{equations.SegmentCount.ToString(CultureInfo.InvariantCulture)} segment(s)",
                $"{equations.NestedSlotCount.ToString(CultureInfo.InvariantCulture)} nested slot(s)",
                $"max depth {equations.MaxNestedSlotDepth.ToString(CultureInfo.InvariantCulture)}");
            var roles = string.Join(
                "; ",
                "segments: " + FormatSummaries(equations.SegmentRoleCounts),
                "baselines: " + FormatSummaries(equations.BaselineRoleCounts),
                "elements: " + FormatSummaries(equations.ElementKindCounts));
            var signatures = FormatSummaries(equations.ElementGeometrySignatures);
            var spacingSignatures = FormatSummaries(equations.SpacingGeometrySignatures);

            sb.AppendLine(
                $"| {EscapeMarkdown(row.HostId)} | {EscapeMarkdown(row.ScenarioId)} | " +
                $"{row.PageNumber.ToString(CultureInfo.InvariantCulture)} | " +
                $"{EscapeMarkdown(counts)} | {EscapeMarkdown(roles)} | " +
                $"{EscapeMarkdown(signatures)} | {EscapeMarkdown(spacingSignatures)} |");
        }
    }

    private static void AppendEvidenceAuthoritySummary(
        StringBuilder sb,
        FreeWVisualEvidenceAuthoritySummary authority)
    {
        sb.AppendLine("## Evidence Authority");
        sb.AppendLine();
        sb.AppendLine($"Authority level: `{EscapeMarkdown(authority.AuthorityLevel)}`");
        sb.AppendLine($"Authoritative Word PNG parity claimed: {(authority.AuthoritativeWordPngParityClaimed ? "yes" : "no")}");
        sb.AppendLine();
        sb.AppendLine("| Trusted Evidence | Comparable Word Rows | Real Word Compared | Word Unavailable | Missing Baseline | Failed/Decode Failed | Skipped/Unmapped | Preparatory Evidence |");
        sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        sb.AppendLine(
            $"| {authority.TrustedEvidenceRows.ToString(CultureInfo.InvariantCulture)} | " +
            $"{authority.ComparableWordBaselineRows.ToString(CultureInfo.InvariantCulture)} | " +
            $"{authority.RealWordPngComparedRows.ToString(CultureInfo.InvariantCulture)} | " +
            $"{authority.WordBaselineUnavailableRows.ToString(CultureInfo.InvariantCulture)} | " +
            $"{authority.MissingWordBaselineRows.ToString(CultureInfo.InvariantCulture)} | " +
            $"{authority.FailedOrDecodeFailedRows.ToString(CultureInfo.InvariantCulture)} | " +
            $"{authority.SkippedOrUnmappedRows.ToString(CultureInfo.InvariantCulture)} | " +
            $"{authority.PreparatoryEvidenceRows.ToString(CultureInfo.InvariantCulture)} |");
        if (authority.Notes.Count > 0)
        {
            sb.AppendLine();
            foreach (var note in authority.Notes)
                sb.AppendLine($"- {EscapeMarkdown(note)}");
        }

        sb.AppendLine();
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
                        if (trusted.All(IsBackstageWpfSoftwareRendererFallback))
                        {
                            rows.Add(new FreeWVisualEvidenceBackstagePrintReadiness(
                                scenarioId,
                                hostId,
                                pageNumber,
                                "fallback",
                                outputSummary,
                                "WPF software renderer fallback retained for no-Word evidence; real wpf-composite-renderer capture still required"));
                            continue;
                        }

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

    private static IReadOnlyList<FreeWVisualFloatingWrappingProofReadiness> BuildFloatingWrappingProofReadinessRows(
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario> expectedScenarios,
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> evidence,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var expected = expectedScenarios.Any(e =>
            string.Equals(e.ScenarioId, FloatingWrappingWpfScenarioId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.ScenarioId, FloatingWrappingAvaloniaScenarioId, StringComparison.OrdinalIgnoreCase));
        var hasEvidence = evidence.Any(row =>
            string.Equals(row.ScenarioId, FloatingWrappingWpfScenarioId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(row.ScenarioId, FloatingWrappingAvaloniaScenarioId, StringComparison.OrdinalIgnoreCase));
        if (!expected && !hasEvidence)
            return [];

        const int pageNumber = 1;
        var wpfRows = RowsForHostScenarioPage(evidence, WpfHostId, FloatingWrappingWpfScenarioId, pageNumber);
        var avaloniaRows = RowsForHostScenarioPage(evidence, AvaloniaHostId, FloatingWrappingAvaloniaScenarioId, pageNumber);
        var trustedWpf = wpfRows.FirstOrDefault(row => row.Trust.Passed);
        var trustedAvalonia = avaloniaRows.FirstOrDefault(row => row.Trust.Passed);
        var relatedBaseline = baselineComparisons
            .Where(comparison =>
                comparison.PageNumber == pageNumber &&
                (string.Equals(comparison.ScenarioId, FloatingWrappingWpfScenarioId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(comparison.ScenarioId, FloatingWrappingAvaloniaScenarioId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (trustedWpf is null || trustedAvalonia is null)
        {
            return
            [
                new FreeWVisualFloatingWrappingProofReadiness(
                    FloatingWrappingProofScenarioId,
                    pageNumber,
                    "missing-paired-renderer-evidence",
                    FloatingWrappingWpfScenarioId,
                    FormatOutputSummary(wpfRows),
                    FloatingWrappingAvaloniaScenarioId,
                    FormatOutputSummary(avaloniaRows),
                    FormatWordBaselineStatus(relatedBaseline),
                    "paired WPF floating-wrap fixture evidence and Avalonia floating-image evidence are required before Word baseline comparison readiness",
                    FormatFloatingWrappingSemanticEvidence(trustedWpf, trustedAvalonia),
                    new FreeWVisualEvidenceTrust(false, BuildMissingFloatingWrappingPairFailures(trustedWpf, trustedAvalonia)))
            ];
        }

        var failures = BuildFloatingWrappingSemanticFailures(trustedWpf, trustedAvalonia);
        var baselineTrust = EvaluateDrawingObjectProofReadiness(relatedBaseline);
        failures.AddRange(baselineTrust.Failures);
        var trust = new FreeWVisualEvidenceTrust(failures.Count == 0, failures);

        return
        [
            new FreeWVisualFloatingWrappingProofReadiness(
                FloatingWrappingProofScenarioId,
                pageNumber,
                trust.Passed ? "paired-renderer-proof-ready" : "floating-wrapping-proof-failed",
                FloatingWrappingWpfScenarioId,
                FormatOutputSummary(wpfRows),
                FloatingWrappingAvaloniaScenarioId,
                FormatOutputSummary(avaloniaRows),
                FormatWordBaselineStatus(relatedBaseline),
                FormatFloatingWrappingBaselineReadiness(relatedBaseline),
                FormatFloatingWrappingSemanticEvidence(trustedWpf, trustedAvalonia),
                trust)
        ];
    }

    private static IReadOnlyList<FreeWVisualNotePlacementProofReadiness> BuildNotePlacementProofReadinessRows(
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario> expectedScenarios,
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> evidence,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var rows = new List<FreeWVisualNotePlacementProofReadiness>();
        foreach (var scenarioId in NotePlacementVisualProofScenarioIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            var expected = expectedScenarios
                .Any(e => string.Equals(e.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            var hasEvidence = evidence
                .Any(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            if (!expected && !hasEvidence)
                continue;

            foreach (var pageNumber in RequiredNotePlacementPages(scenarioId))
            {
                var wpfRows = RowsForHostScenarioPage(evidence, WpfHostId, scenarioId, pageNumber);
                var avaloniaRows = RowsForHostScenarioPage(evidence, AvaloniaHostId, scenarioId, pageNumber);
                var trustedWpf = wpfRows.FirstOrDefault(row => row.Trust.Passed);
                var trustedAvalonia = avaloniaRows.FirstOrDefault(row => row.Trust.Passed);
                var relatedBaseline = baselineComparisons
                    .Where(comparison =>
                        string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase)
                        && comparison.PageNumber == pageNumber)
                    .ToList();

                if (trustedWpf is null || trustedAvalonia is null)
                {
                    rows.Add(new FreeWVisualNotePlacementProofReadiness(
                        scenarioId,
                        pageNumber,
                        "missing-paired-renderer-evidence",
                        FormatOutputSummary(wpfRows),
                        FormatOutputSummary(avaloniaRows),
                        FormatWordBaselineStatus(relatedBaseline),
                        "paired WPF/Avalonia note placement evidence is required before Word baseline comparison readiness",
                        FormatNotePlacementProofSemanticEvidence(trustedWpf, trustedAvalonia),
                        new FreeWVisualEvidenceTrust(false, BuildMissingNotePlacementPairFailures(scenarioId, pageNumber, trustedWpf, trustedAvalonia))));
                    continue;
                }

                var failures = BuildNotePlacementSemanticFailures(scenarioId, pageNumber, trustedWpf, trustedAvalonia);
                var baselineTrust = EvaluateDrawingObjectProofReadiness(relatedBaseline);
                failures.AddRange(baselineTrust.Failures);
                var trust = new FreeWVisualEvidenceTrust(failures.Count == 0, failures);
                rows.Add(new FreeWVisualNotePlacementProofReadiness(
                    scenarioId,
                    pageNumber,
                    trust.Passed ? "paired-renderer-proof-ready" : "note-placement-proof-failed",
                    FormatOutputSummary(wpfRows),
                    FormatOutputSummary(avaloniaRows),
                    FormatWordBaselineStatus(relatedBaseline),
                    FormatNotePlacementBaselineReadiness(relatedBaseline, scenarioId),
                    FormatNotePlacementProofSemanticEvidence(trustedWpf, trustedAvalonia),
                    trust));
            }
        }

        return rows;
    }

    private static IReadOnlyList<int> RequiredNotePlacementPages(string scenarioId) =>
        RequiredScenarioPages(scenarioId);

    private static IReadOnlyList<FreeWVisualSectionGeometryProofReadiness> BuildSectionGeometryProofReadinessRows(
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario> expectedScenarios,
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> evidence,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var rows = new List<FreeWVisualSectionGeometryProofReadiness>();
        foreach (var scenarioId in SectionGeometryVisualProofScenarioIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            var expected = expectedScenarios
                .Any(e => string.Equals(e.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            var hasEvidence = evidence
                .Any(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            if (!expected && !hasEvidence)
                continue;

            foreach (var pageNumber in RequiredScenarioPages(scenarioId))
            {
                var wpfRows = RowsForHostScenarioPage(evidence, WpfHostId, scenarioId, pageNumber);
                var avaloniaRows = RowsForHostScenarioPage(evidence, AvaloniaHostId, scenarioId, pageNumber);
                var trustedWpf = wpfRows.FirstOrDefault(row => row.Trust.Passed);
                var trustedAvalonia = avaloniaRows.FirstOrDefault(row => row.Trust.Passed);
                var relatedBaseline = baselineComparisons
                    .Where(comparison =>
                        string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase)
                        && comparison.PageNumber == pageNumber)
                    .ToList();

                if (trustedWpf is null || trustedAvalonia is null)
                {
                    rows.Add(new FreeWVisualSectionGeometryProofReadiness(
                        scenarioId,
                        pageNumber,
                        "missing-paired-renderer-evidence",
                        FormatOutputSummary(wpfRows),
                        FormatOutputSummary(avaloniaRows),
                        FormatWordBaselineStatus(relatedBaseline),
                        "paired WPF/Avalonia section geometry evidence is required before Word baseline comparison readiness",
                        FormatSectionGeometryProofSemanticEvidence(trustedWpf, trustedAvalonia),
                        new FreeWVisualEvidenceTrust(false, BuildMissingSectionGeometryPairFailures(scenarioId, pageNumber, trustedWpf, trustedAvalonia))));
                    continue;
                }

                var failures = BuildSectionGeometrySemanticFailures(scenarioId, pageNumber, trustedWpf, trustedAvalonia);
                var baselineTrust = EvaluateDrawingObjectProofReadiness(relatedBaseline);
                failures.AddRange(baselineTrust.Failures);
                var trust = new FreeWVisualEvidenceTrust(failures.Count == 0, failures);
                rows.Add(new FreeWVisualSectionGeometryProofReadiness(
                    scenarioId,
                    pageNumber,
                    trust.Passed ? "paired-renderer-proof-ready" : "section-geometry-proof-failed",
                    FormatOutputSummary(wpfRows),
                    FormatOutputSummary(avaloniaRows),
                    FormatWordBaselineStatus(relatedBaseline),
                    FormatSectionGeometryBaselineReadiness(relatedBaseline, scenarioId),
                    FormatSectionGeometryProofSemanticEvidence(trustedWpf, trustedAvalonia),
                    trust));
            }
        }

        return rows;
    }

    private static IReadOnlyList<FreeWVisualDrawingObjectProofReadiness> BuildDrawingObjectProofReadinessRows(
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario> expectedScenarios,
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> evidence,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var rows = new List<FreeWVisualDrawingObjectProofReadiness>();
        foreach (var scenarioId in DrawingObjectVisualProofScenarioIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            var expected = expectedScenarios
                .Any(e => string.Equals(e.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            var hasEvidence = evidence
                .Any(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            if (!expected && !hasEvidence)
                continue;

            foreach (var pageNumber in RequiredScenarioPages(scenarioId))
            {
                var wpfRows = RowsForHostScenarioPage(evidence, WpfHostId, scenarioId, pageNumber);
                var avaloniaRows = RowsForHostScenarioPage(evidence, AvaloniaHostId, scenarioId, pageNumber);
                var trustedWpf = wpfRows.FirstOrDefault(row => row.Trust.Passed);
                var trustedAvalonia = avaloniaRows.FirstOrDefault(row => row.Trust.Passed);
                var relatedBaseline = baselineComparisons
                    .Where(comparison =>
                        string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase)
                        && comparison.PageNumber == pageNumber)
                    .ToList();

                if (trustedWpf is null || trustedAvalonia is null)
                {
                    rows.Add(new FreeWVisualDrawingObjectProofReadiness(
                        scenarioId,
                        pageNumber,
                        "missing-paired-renderer-evidence",
                        FormatOutputSummary(wpfRows),
                        FormatOutputSummary(avaloniaRows),
                        FormatWordBaselineStatus(relatedBaseline),
                        "paired WPF/Avalonia visual evidence is required before Word baseline comparison readiness",
                        FormatDrawingObjectProofSemanticEvidence(trustedWpf, trustedAvalonia),
                        new FreeWVisualEvidenceTrust(false, BuildMissingPairFailures(scenarioId, pageNumber, trustedWpf, trustedAvalonia))));
                    continue;
                }

                var proofTrust = EvaluateDrawingObjectProofReadiness(relatedBaseline);
                rows.Add(new FreeWVisualDrawingObjectProofReadiness(
                    scenarioId,
                    pageNumber,
                    proofTrust.Passed ? "paired-renderer-proof-ready" : "baseline-policy-failed",
                    FormatOutputSummary(wpfRows),
                    FormatOutputSummary(avaloniaRows),
                    FormatWordBaselineStatus(relatedBaseline),
                    FormatDrawingObjectBaselineReadiness(relatedBaseline, scenarioId),
                    FormatDrawingObjectProofSemanticEvidence(trustedWpf, trustedAvalonia),
                    proofTrust));
            }
        }

        return rows;
    }

    private static IReadOnlyList<FreeWVisualWordArtWatermarkProofReadiness> BuildWordArtWatermarkProofReadinessRows(
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario> expectedScenarios,
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> evidence,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var rows = new List<FreeWVisualWordArtWatermarkProofReadiness>();
        foreach (var scenarioId in WordArtWatermarkVisualProofScenarioIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            var expected = expectedScenarios
                .Any(e => string.Equals(e.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            var hasEvidence = evidence
                .Any(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            if (!expected && !hasEvidence)
                continue;

            foreach (var pageNumber in RequiredScenarioPages(scenarioId))
            {
                var wpfRows = RowsForHostScenarioPage(evidence, WpfHostId, scenarioId, pageNumber);
                var avaloniaRows = RowsForHostScenarioPage(evidence, AvaloniaHostId, scenarioId, pageNumber);
                var trustedWpf = wpfRows.FirstOrDefault(row => row.Trust.Passed);
                var trustedAvalonia = avaloniaRows.FirstOrDefault(row => row.Trust.Passed);
                var relatedBaseline = baselineComparisons
                    .Where(comparison =>
                        string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase)
                        && comparison.PageNumber == pageNumber)
                    .ToList();

                if (trustedWpf is null || trustedAvalonia is null)
                {
                    rows.Add(new FreeWVisualWordArtWatermarkProofReadiness(
                        scenarioId,
                        pageNumber,
                        "missing-paired-renderer-evidence",
                        FormatOutputSummary(wpfRows),
                        FormatOutputSummary(avaloniaRows),
                        FormatWordBaselineStatus(relatedBaseline),
                        "paired WPF/Avalonia WordArt/watermark visual evidence is required before Word baseline comparison readiness",
                        FormatDrawingObjectProofSemanticEvidence(trustedWpf, trustedAvalonia),
                        new FreeWVisualEvidenceTrust(false, BuildMissingPairFailures(scenarioId, pageNumber, trustedWpf, trustedAvalonia))));
                    continue;
                }

                var proofTrust = EvaluateDrawingObjectProofReadiness(relatedBaseline);
                rows.Add(new FreeWVisualWordArtWatermarkProofReadiness(
                    scenarioId,
                    pageNumber,
                    proofTrust.Passed ? "paired-renderer-proof-ready" : "baseline-policy-failed",
                    FormatOutputSummary(wpfRows),
                    FormatOutputSummary(avaloniaRows),
                    FormatWordBaselineStatus(relatedBaseline),
                    FormatDrawingObjectBaselineReadiness(relatedBaseline, scenarioId),
                    FormatDrawingObjectProofSemanticEvidence(trustedWpf, trustedAvalonia),
                    proofTrust));
            }
        }

        return rows;
    }

    private static IReadOnlyList<FreeWVisualHeaderFooterImageProofReadiness> BuildHeaderFooterImageProofReadinessRows(
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario> expectedScenarios,
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> evidence,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var rows = new List<FreeWVisualHeaderFooterImageProofReadiness>();
        foreach (var scenarioId in HeaderFooterImageVisualProofScenarioIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            var expected = expectedScenarios
                .Any(e => string.Equals(e.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            var hasEvidence = evidence
                .Any(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            if (!expected && !hasEvidence)
                continue;

            foreach (var pageNumber in RequiredScenarioPages(scenarioId))
            {
                var wpfRows = RowsForHostScenarioPage(evidence, WpfHostId, scenarioId, pageNumber);
                var avaloniaRows = RowsForHostScenarioPage(evidence, AvaloniaHostId, scenarioId, pageNumber);
                var trustedWpf = wpfRows.FirstOrDefault(row => row.Trust.Passed);
                var trustedAvalonia = avaloniaRows.FirstOrDefault(row => row.Trust.Passed);
                var relatedBaseline = baselineComparisons
                    .Where(comparison =>
                        string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase)
                        && comparison.PageNumber == pageNumber)
                    .ToList();

                if (trustedWpf is null || trustedAvalonia is null)
                {
                    rows.Add(new FreeWVisualHeaderFooterImageProofReadiness(
                        scenarioId,
                        pageNumber,
                        "missing-paired-renderer-evidence",
                        FormatOutputSummary(wpfRows),
                        FormatOutputSummary(avaloniaRows),
                        FormatWordBaselineStatus(relatedBaseline),
                        "paired WPF/Avalonia header/footer image evidence is required before Word baseline comparison readiness",
                        FormatHeaderFooterImageProofSemanticEvidence(trustedWpf, trustedAvalonia),
                        new FreeWVisualEvidenceTrust(false, BuildMissingHeaderFooterImagePairFailures(scenarioId, pageNumber, trustedWpf, trustedAvalonia))));
                    continue;
                }

                var failures = BuildHeaderFooterImageSemanticFailures(scenarioId, pageNumber, trustedWpf, trustedAvalonia);
                var baselineTrust = EvaluateDrawingObjectProofReadiness(relatedBaseline);
                failures.AddRange(baselineTrust.Failures);
                var trust = new FreeWVisualEvidenceTrust(failures.Count == 0, failures);
                rows.Add(new FreeWVisualHeaderFooterImageProofReadiness(
                    scenarioId,
                    pageNumber,
                    trust.Passed ? "paired-renderer-proof-ready" : "header-footer-image-proof-failed",
                    FormatOutputSummary(wpfRows),
                    FormatOutputSummary(avaloniaRows),
                    FormatWordBaselineStatus(relatedBaseline),
                    FormatHeaderFooterImageBaselineReadiness(relatedBaseline, scenarioId),
                    FormatHeaderFooterImageProofSemanticEvidence(trustedWpf, trustedAvalonia),
                    trust));
            }
        }

        return rows;
    }

    private static IReadOnlyList<FreeWVisualTablePaginationProofReadiness> BuildTablePaginationProofReadinessRows(
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario> expectedScenarios,
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> evidence,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var rows = new List<FreeWVisualTablePaginationProofReadiness>();
        foreach (var scenarioId in TablePaginationVisualProofScenarioIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            var expected = expectedScenarios
                .Any(e => string.Equals(e.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            var hasEvidence = evidence
                .Any(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            if (!expected && !hasEvidence)
                continue;

            foreach (var pageNumber in RequiredScenarioPages(scenarioId))
            {
                var wpfRows = RowsForHostScenarioPage(evidence, WpfHostId, scenarioId, pageNumber);
                var avaloniaRows = RowsForHostScenarioPage(evidence, AvaloniaHostId, scenarioId, pageNumber);
                var trustedWpf = wpfRows.FirstOrDefault(row => row.Trust.Passed);
                var trustedAvalonia = avaloniaRows.FirstOrDefault(row => row.Trust.Passed);
                var relatedBaseline = baselineComparisons
                    .Where(comparison =>
                        string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase)
                        && comparison.PageNumber == pageNumber)
                    .ToList();

                if (trustedWpf is null || trustedAvalonia is null)
                {
                    rows.Add(new FreeWVisualTablePaginationProofReadiness(
                        scenarioId,
                        pageNumber,
                        "missing-paired-renderer-evidence",
                        FormatOutputSummary(wpfRows),
                        FormatOutputSummary(avaloniaRows),
                        FormatWordBaselineStatus(relatedBaseline),
                        "paired WPF/Avalonia table pagination evidence is required before Word baseline comparison readiness",
                        FormatTablePaginationProofSemanticEvidence(trustedWpf, trustedAvalonia),
                        new FreeWVisualEvidenceTrust(false, BuildMissingTablePaginationPairFailures(scenarioId, pageNumber, trustedWpf, trustedAvalonia))));
                    continue;
                }

                var failures = BuildTablePaginationSemanticFailures(scenarioId, pageNumber, trustedWpf, trustedAvalonia);
                var baselineTrust = EvaluateDrawingObjectProofReadiness(relatedBaseline);
                failures.AddRange(baselineTrust.Failures);
                var trust = new FreeWVisualEvidenceTrust(failures.Count == 0, failures);
                rows.Add(new FreeWVisualTablePaginationProofReadiness(
                    scenarioId,
                    pageNumber,
                    trust.Passed ? "paired-renderer-proof-ready" : "table-pagination-proof-failed",
                    FormatOutputSummary(wpfRows),
                    FormatOutputSummary(avaloniaRows),
                    FormatWordBaselineStatus(relatedBaseline),
                    FormatTablePaginationBaselineReadiness(relatedBaseline, scenarioId),
                    FormatTablePaginationProofSemanticEvidence(trustedWpf, trustedAvalonia),
                    trust));
            }
        }

        return rows;
    }

    private static IReadOnlyList<FreeWVisualReviewMarkupProofReadiness> BuildReviewMarkupProofReadinessRows(
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario> expectedScenarios,
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> evidence,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var rows = new List<FreeWVisualReviewMarkupProofReadiness>();
        foreach (var scenarioId in ReviewMarkupVisualProofScenarioIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            var expected = expectedScenarios
                .Any(e => string.Equals(e.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            var hasEvidence = evidence
                .Any(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            if (!expected && !hasEvidence)
                continue;

            foreach (var pageNumber in RequiredScenarioPages(scenarioId))
            {
                var wpfRows = RowsForHostScenarioPage(evidence, WpfHostId, scenarioId, pageNumber);
                var avaloniaRows = RowsForHostScenarioPage(evidence, AvaloniaHostId, scenarioId, pageNumber);
                var trustedWpf = wpfRows.FirstOrDefault(row => row.Trust.Passed);
                var trustedAvalonia = avaloniaRows.FirstOrDefault(row => row.Trust.Passed);
                var relatedBaseline = baselineComparisons
                    .Where(comparison =>
                        string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase)
                        && comparison.PageNumber == pageNumber)
                    .ToList();

                if (trustedWpf is null || trustedAvalonia is null)
                {
                    rows.Add(new FreeWVisualReviewMarkupProofReadiness(
                        scenarioId,
                        pageNumber,
                        "missing-paired-renderer-evidence",
                        FormatOutputSummary(wpfRows),
                        FormatOutputSummary(avaloniaRows),
                        FormatWordBaselineStatus(relatedBaseline),
                        "paired WPF/Avalonia review markup evidence is required before Word baseline comparison readiness",
                        FormatReviewMarkupProofSemanticEvidence(trustedWpf, trustedAvalonia),
                        new FreeWVisualEvidenceTrust(false, BuildMissingReviewMarkupPairFailures(scenarioId, pageNumber, trustedWpf, trustedAvalonia))));
                    continue;
                }

                var failures = BuildReviewMarkupSemanticFailures(scenarioId, pageNumber, trustedWpf, trustedAvalonia);
                var baselineTrust = EvaluateDrawingObjectProofReadiness(relatedBaseline);
                failures.AddRange(baselineTrust.Failures);
                var trust = new FreeWVisualEvidenceTrust(failures.Count == 0, failures);
                rows.Add(new FreeWVisualReviewMarkupProofReadiness(
                    scenarioId,
                    pageNumber,
                    trust.Passed ? "paired-renderer-proof-ready" : "review-markup-proof-failed",
                    FormatOutputSummary(wpfRows),
                    FormatOutputSummary(avaloniaRows),
                    FormatWordBaselineStatus(relatedBaseline),
                    FormatReviewMarkupBaselineReadiness(relatedBaseline, scenarioId),
                    FormatReviewMarkupProofSemanticEvidence(trustedWpf, trustedAvalonia),
                    trust));
            }
        }

        return rows;
    }

    private static IReadOnlyList<FreeWVisualReviewCompareCombineProofReadiness> BuildReviewCompareCombineProofReadinessRows(
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario> expectedScenarios,
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> evidence,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var rows = new List<FreeWVisualReviewCompareCombineProofReadiness>();
        foreach (var scenarioId in ReviewCompareCombineVisualProofScenarioIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            var expected = expectedScenarios
                .Any(e => string.Equals(e.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            var hasEvidence = evidence
                .Any(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            if (!expected && !hasEvidence)
                continue;

            foreach (var pageNumber in RequiredScenarioPages(scenarioId))
            {
                var wpfRows = RowsForHostScenarioPage(evidence, WpfHostId, scenarioId, pageNumber);
                var avaloniaRows = RowsForHostScenarioPage(evidence, AvaloniaHostId, scenarioId, pageNumber);
                var trustedWpf = wpfRows.FirstOrDefault(row => row.Trust.Passed);
                var trustedAvalonia = avaloniaRows.FirstOrDefault(row => row.Trust.Passed);
                var relatedBaseline = baselineComparisons
                    .Where(comparison =>
                        string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase)
                        && comparison.PageNumber == pageNumber)
                    .ToList();

                if (trustedWpf is null || trustedAvalonia is null)
                {
                    rows.Add(new FreeWVisualReviewCompareCombineProofReadiness(
                        scenarioId,
                        pageNumber,
                        "missing-paired-renderer-evidence",
                        FormatOutputSummary(wpfRows),
                        FormatOutputSummary(avaloniaRows),
                        FormatWordBaselineStatus(relatedBaseline),
                        "paired WPF/Avalonia compare-combine evidence is required before Word baseline comparison readiness",
                        FormatReviewCompareCombineProofSemanticEvidence(trustedWpf, trustedAvalonia),
                        new FreeWVisualEvidenceTrust(false, BuildMissingReviewCompareCombinePairFailures(scenarioId, pageNumber, trustedWpf, trustedAvalonia))));
                    continue;
                }

                var failures = BuildReviewCompareCombineSemanticFailures(scenarioId, pageNumber, trustedWpf, trustedAvalonia);
                var baselineTrust = EvaluateDrawingObjectProofReadiness(relatedBaseline);
                failures.AddRange(baselineTrust.Failures);
                var trust = new FreeWVisualEvidenceTrust(failures.Count == 0, failures);
                rows.Add(new FreeWVisualReviewCompareCombineProofReadiness(
                    scenarioId,
                    pageNumber,
                    trust.Passed ? "paired-renderer-proof-ready" : "review-compare-combine-proof-failed",
                    FormatOutputSummary(wpfRows),
                    FormatOutputSummary(avaloniaRows),
                    FormatWordBaselineStatus(relatedBaseline),
                    FormatReviewCompareCombineBaselineReadiness(relatedBaseline, scenarioId),
                    FormatReviewCompareCombineProofSemanticEvidence(trustedWpf, trustedAvalonia),
                    trust));
            }
        }

        return rows;
    }

    private static IReadOnlyList<FreeWVisualReviewProofingProofReadiness> BuildReviewProofingProofReadinessRows(
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario> expectedScenarios,
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> evidence,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var rows = new List<FreeWVisualReviewProofingProofReadiness>();
        foreach (var scenarioId in ReviewProofingVisualProofScenarioIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            var expected = expectedScenarios
                .Any(e => string.Equals(e.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            var hasEvidence = evidence
                .Any(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
            if (!expected && !hasEvidence)
                continue;

            foreach (var pageNumber in RequiredScenarioPages(scenarioId))
            {
                var wpfRows = RowsForHostScenarioPage(evidence, WpfHostId, scenarioId, pageNumber);
                var avaloniaRows = RowsForHostScenarioPage(evidence, AvaloniaHostId, scenarioId, pageNumber);
                var trustedWpf = wpfRows.FirstOrDefault(row => row.Trust.Passed);
                var trustedAvalonia = avaloniaRows.FirstOrDefault(row => row.Trust.Passed);
                var relatedBaseline = baselineComparisons
                    .Where(comparison =>
                        string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase)
                        && comparison.PageNumber == pageNumber)
                    .ToList();

                if (trustedWpf is null || trustedAvalonia is null)
                {
                    rows.Add(new FreeWVisualReviewProofingProofReadiness(
                        scenarioId,
                        pageNumber,
                        "missing-paired-renderer-evidence",
                        FormatOutputSummary(wpfRows),
                        FormatOutputSummary(avaloniaRows),
                        FormatWordBaselineStatus(relatedBaseline),
                        "paired WPF/Avalonia proofing visual adornment evidence is required before Word baseline comparison readiness",
                        FormatReviewProofingProofSemanticEvidence(trustedWpf, trustedAvalonia),
                        new FreeWVisualEvidenceTrust(false, BuildMissingReviewProofingPairFailures(scenarioId, pageNumber, trustedWpf, trustedAvalonia))));
                    continue;
                }

                var failures = BuildReviewProofingSemanticFailures(scenarioId, pageNumber, trustedWpf, trustedAvalonia);
                var baselineTrust = EvaluateDrawingObjectProofReadiness(relatedBaseline);
                failures.AddRange(baselineTrust.Failures);
                var trust = new FreeWVisualEvidenceTrust(failures.Count == 0, failures);
                rows.Add(new FreeWVisualReviewProofingProofReadiness(
                    scenarioId,
                    pageNumber,
                    trust.Passed ? "paired-renderer-proof-ready" : "review-proofing-proof-failed",
                    FormatOutputSummary(wpfRows),
                    FormatOutputSummary(avaloniaRows),
                    FormatWordBaselineStatus(relatedBaseline),
                    FormatReviewProofingBaselineReadiness(relatedBaseline, scenarioId),
                    FormatReviewProofingProofSemanticEvidence(trustedWpf, trustedAvalonia),
                    trust));
            }
        }

        return rows;
    }

    private static IReadOnlyList<FreeWVisualLegalReferenceProofReadiness> BuildLegalReferenceProofReadinessRows(
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario> expectedScenarios,
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> evidence,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        const string scenarioId = LegalReferenceSectionPageProofScenarioId;
        var expected = expectedScenarios
            .Any(e => string.Equals(e.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
        var hasEvidence = evidence
            .Any(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
        if (!expected && !hasEvidence)
            return [];

        var rows = new List<FreeWVisualLegalReferenceProofReadiness>();
        foreach (var pageNumber in RequiredScenarioPages(scenarioId))
        {
            var wpfRows = RowsForHostScenarioPage(evidence, WpfHostId, scenarioId, pageNumber);
            var avaloniaRows = RowsForHostScenarioPage(evidence, AvaloniaHostId, scenarioId, pageNumber);
            var trustedWpf = wpfRows.FirstOrDefault(row => row.Trust.Passed);
            var trustedAvalonia = avaloniaRows.FirstOrDefault(row => row.Trust.Passed);
            var relatedBaseline = baselineComparisons
                .Where(comparison =>
                    string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase)
                    && comparison.PageNumber == pageNumber)
                .ToList();

            if (trustedWpf is null || trustedAvalonia is null)
            {
                rows.Add(new FreeWVisualLegalReferenceProofReadiness(
                    scenarioId,
                    pageNumber,
                    "missing-paired-renderer-evidence",
                    FormatOutputSummary(wpfRows),
                    FormatOutputSummary(avaloniaRows),
                    FormatWordBaselineStatus(relatedBaseline),
                    "paired WPF/Avalonia legal-reference page-number evidence is required before Word baseline comparison readiness",
                    FormatLegalReferenceProofSemanticEvidence(trustedWpf, trustedAvalonia),
                    new FreeWVisualEvidenceTrust(false, BuildMissingLegalReferencePairFailures(pageNumber, trustedWpf, trustedAvalonia))));
                continue;
            }

            var failures = BuildLegalReferenceSemanticFailures(pageNumber, trustedWpf, trustedAvalonia);
            var baselineTrust = EvaluateDrawingObjectProofReadiness(relatedBaseline);
            failures.AddRange(baselineTrust.Failures);
            var trust = new FreeWVisualEvidenceTrust(failures.Count == 0, failures);
            rows.Add(new FreeWVisualLegalReferenceProofReadiness(
                scenarioId,
                pageNumber,
                trust.Passed ? "paired-renderer-proof-ready" : "legal-reference-proof-failed",
                FormatOutputSummary(wpfRows),
                FormatOutputSummary(avaloniaRows),
                FormatWordBaselineStatus(relatedBaseline),
                FormatLegalReferenceBaselineReadiness(relatedBaseline),
                FormatLegalReferenceProofSemanticEvidence(trustedWpf, trustedAvalonia),
                trust));
        }

        return rows;
    }

    private static IReadOnlyList<FreeWVisualReferencesHeavyProofReadiness> BuildReferencesHeavyProofReadinessRows(
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario> expectedScenarios,
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> evidence,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        const string scenarioId = ReferencesHeavyProofScenarioId;
        var expected = expectedScenarios
            .Any(e => string.Equals(e.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
        var hasEvidence = evidence
            .Any(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));
        if (!expected && !hasEvidence)
            return [];

        var rows = new List<FreeWVisualReferencesHeavyProofReadiness>();
        foreach (var pageNumber in RequiredScenarioPages(scenarioId))
        {
            var wpfRows = RowsForHostScenarioPage(evidence, WpfHostId, scenarioId, pageNumber);
            var avaloniaRows = RowsForHostScenarioPage(evidence, AvaloniaHostId, scenarioId, pageNumber);
            var trustedWpf = wpfRows.FirstOrDefault(row => row.Trust.Passed);
            var trustedAvalonia = avaloniaRows.FirstOrDefault(row => row.Trust.Passed);
            var relatedBaseline = baselineComparisons
                .Where(comparison =>
                    string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase)
                    && comparison.PageNumber == pageNumber)
                .ToList();

            if (trustedWpf is null || trustedAvalonia is null)
            {
                rows.Add(new FreeWVisualReferencesHeavyProofReadiness(
                    scenarioId,
                    pageNumber,
                    "missing-paired-renderer-evidence",
                    FormatOutputSummary(wpfRows),
                    FormatOutputSummary(avaloniaRows),
                    FormatWordBaselineStatus(relatedBaseline),
                    "paired WPF/Avalonia references-heavy field and TOA evidence is required before Word baseline comparison readiness",
                    FormatReferencesHeavyProofSemanticEvidence(trustedWpf, trustedAvalonia),
                    new FreeWVisualEvidenceTrust(false, BuildMissingReferencesHeavyPairFailures(pageNumber, trustedWpf, trustedAvalonia))));
                continue;
            }

            var failures = BuildReferencesHeavySemanticFailures(pageNumber, trustedWpf, trustedAvalonia);
            var baselineTrust = EvaluateDrawingObjectProofReadiness(relatedBaseline);
            failures.AddRange(baselineTrust.Failures);
            var trust = new FreeWVisualEvidenceTrust(failures.Count == 0, failures);
            rows.Add(new FreeWVisualReferencesHeavyProofReadiness(
                scenarioId,
                pageNumber,
                trust.Passed ? "paired-renderer-proof-ready" : "references-heavy-proof-failed",
                FormatOutputSummary(wpfRows),
                FormatOutputSummary(avaloniaRows),
                FormatWordBaselineStatus(relatedBaseline),
                FormatReferencesHeavyBaselineReadiness(relatedBaseline),
                FormatReferencesHeavyProofSemanticEvidence(trustedWpf, trustedAvalonia),
                trust));
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
            NotePlacementProofReadiness = BuildNotePlacementProofReadinessRows(
                summary.ExpectedScenarios,
                summary.Evidence,
                ordered),
            SectionGeometryProofReadiness = BuildSectionGeometryProofReadinessRows(
                summary.ExpectedScenarios,
                summary.Evidence,
                ordered),
            FloatingWrappingProofReadiness = BuildFloatingWrappingProofReadinessRows(
                summary.ExpectedScenarios,
                summary.Evidence,
                ordered),
            HeaderFooterImageProofReadiness = BuildHeaderFooterImageProofReadinessRows(
                summary.ExpectedScenarios,
                summary.Evidence,
                ordered),
            TablePaginationProofReadiness = BuildTablePaginationProofReadinessRows(
                summary.ExpectedScenarios,
                summary.Evidence,
                ordered),
            DrawingObjectProofReadiness = BuildDrawingObjectProofReadinessRows(
                summary.ExpectedScenarios,
                summary.Evidence,
                ordered),
            WordArtWatermarkProofReadiness = BuildWordArtWatermarkProofReadinessRows(
                summary.ExpectedScenarios,
                summary.Evidence,
                ordered),
            ReviewMarkupProofReadiness = BuildReviewMarkupProofReadinessRows(
                summary.ExpectedScenarios,
                summary.Evidence,
                ordered),
            ReviewCompareCombineProofReadiness = BuildReviewCompareCombineProofReadinessRows(
                summary.ExpectedScenarios,
                summary.Evidence,
                ordered),
            ReviewProofingProofReadiness = BuildReviewProofingProofReadinessRows(
                summary.ExpectedScenarios,
                summary.Evidence,
                ordered),
            ReferencesHeavyProofReadiness = BuildReferencesHeavyProofReadinessRows(
                summary.ExpectedScenarios,
                summary.Evidence,
                ordered),
            LegalReferenceProofReadiness = BuildLegalReferenceProofReadinessRows(
                summary.ExpectedScenarios,
                summary.Evidence,
                ordered),
            BaselineComparisons = ordered,
            WordBaselineTriage = BuildWordBaselineTriage(ordered),
            EvidenceAuthority = BuildEvidenceAuthoritySummary(summary.Evidence, ordered),
            RemainingEvidenceBlockers = BuildRemainingEvidenceBlockers(summary, ordered),
            Trust = new FreeWVisualEvidenceTrust(failures.Count == 0, failures)
        };
    }

    private static List<FreeWVisualEvidenceNormalizedRow> RowsForHostScenarioPage(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> evidence,
        string hostId,
        string scenarioId,
        int pageNumber) =>
        evidence
            .Where(row =>
                string.Equals(row.HostId, hostId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase)
                && row.PageNumber == pageNumber)
            .OrderBy(row => row.OutputName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<string> BuildMissingPairFailures(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow? trustedWpf,
        FreeWVisualEvidenceNormalizedRow? trustedAvalonia)
    {
        var failures = new List<string>();
        if (trustedWpf is null)
            failures.Add($"drawing/object proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted WPF visual evidence");
        if (trustedAvalonia is null)
            failures.Add($"drawing/object proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted Avalonia visual evidence");
        return failures;
    }

    private static IReadOnlyList<string> BuildMissingHeaderFooterImagePairFailures(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow? trustedWpf,
        FreeWVisualEvidenceNormalizedRow? trustedAvalonia)
    {
        var failures = new List<string>();
        if (trustedWpf is null)
            failures.Add($"header/footer image proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted WPF visual evidence");
        if (trustedAvalonia is null)
            failures.Add($"header/footer image proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted Avalonia visual evidence");
        return failures;
    }

    private static IReadOnlyList<string> BuildMissingFloatingWrappingPairFailures(
        FreeWVisualEvidenceNormalizedRow? trustedWpf,
        FreeWVisualEvidenceNormalizedRow? trustedAvalonia)
    {
        var failures = new List<string>();
        if (trustedWpf is null)
            failures.Add($"floating/wrapping proof page 1 is missing trusted WPF visual evidence for '{FloatingWrappingWpfScenarioId}'");
        if (trustedAvalonia is null)
            failures.Add($"floating/wrapping proof page 1 is missing trusted Avalonia visual evidence for '{FloatingWrappingAvaloniaScenarioId}'");
        return failures;
    }

    private static IReadOnlyList<string> BuildMissingNotePlacementPairFailures(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow? trustedWpf,
        FreeWVisualEvidenceNormalizedRow? trustedAvalonia)
    {
        var failures = new List<string>();
        if (trustedWpf is null)
            failures.Add($"note placement proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted WPF visual evidence");
        if (trustedAvalonia is null)
            failures.Add($"note placement proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted Avalonia visual evidence");
        return failures;
    }

    private static IReadOnlyList<string> BuildMissingSectionGeometryPairFailures(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow? trustedWpf,
        FreeWVisualEvidenceNormalizedRow? trustedAvalonia)
    {
        var failures = new List<string>();
        if (trustedWpf is null)
            failures.Add($"section geometry proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted WPF visual evidence");
        if (trustedAvalonia is null)
            failures.Add($"section geometry proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted Avalonia visual evidence");
        return failures;
    }

    private static IReadOnlyList<string> BuildMissingTablePaginationPairFailures(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow? trustedWpf,
        FreeWVisualEvidenceNormalizedRow? trustedAvalonia)
    {
        var failures = new List<string>();
        if (trustedWpf is null)
            failures.Add($"table pagination proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted WPF visual evidence");
        if (trustedAvalonia is null)
            failures.Add($"table pagination proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted Avalonia visual evidence");
        return failures;
    }

    private static IReadOnlyList<string> BuildMissingReviewCompareCombinePairFailures(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow? trustedWpf,
        FreeWVisualEvidenceNormalizedRow? trustedAvalonia)
    {
        var failures = new List<string>();
        if (trustedWpf is null)
            failures.Add($"review compare/combine proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted WPF visual evidence");
        if (trustedAvalonia is null)
            failures.Add($"review compare/combine proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted Avalonia visual evidence");
        return failures;
    }

    private static IReadOnlyList<string> BuildMissingReviewProofingPairFailures(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow? trustedWpf,
        FreeWVisualEvidenceNormalizedRow? trustedAvalonia)
    {
        var failures = new List<string>();
        if (trustedWpf is null)
            failures.Add($"review proofing visual proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted WPF visual evidence");
        if (trustedAvalonia is null)
            failures.Add($"review proofing visual proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted Avalonia visual evidence");
        return failures;
    }

    private static IReadOnlyList<string> BuildMissingReviewMarkupPairFailures(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow? trustedWpf,
        FreeWVisualEvidenceNormalizedRow? trustedAvalonia)
    {
        var failures = new List<string>();
        if (trustedWpf is null)
            failures.Add($"review markup visual proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted WPF visual evidence");
        if (trustedAvalonia is null)
            failures.Add($"review markup visual proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted Avalonia visual evidence");
        return failures;
    }

    private static IReadOnlyList<string> BuildMissingLegalReferencePairFailures(
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow? trustedWpf,
        FreeWVisualEvidenceNormalizedRow? trustedAvalonia)
    {
        var failures = new List<string>();
        if (trustedWpf is null)
            failures.Add($"legal-reference section page-number proof page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted WPF visual evidence");
        if (trustedAvalonia is null)
            failures.Add($"legal-reference section page-number proof page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted Avalonia visual evidence");
        return failures;
    }

    private static IReadOnlyList<string> BuildMissingReferencesHeavyPairFailures(
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow? trustedWpf,
        FreeWVisualEvidenceNormalizedRow? trustedAvalonia)
    {
        var failures = new List<string>();
        if (trustedWpf is null)
            failures.Add($"references-heavy field/TOA proof page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted WPF visual evidence");
        if (trustedAvalonia is null)
            failures.Add($"references-heavy field/TOA proof page {pageNumber.ToString(CultureInfo.InvariantCulture)} is missing trusted Avalonia visual evidence");
        return failures;
    }

    private static List<string> BuildReferencesHeavySemanticFailures(
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia)
    {
        var failures = new List<string>();
        var pairName = $"references-heavy field/TOA proof page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        ValidateReferencesHeavySemanticRow(pairName + " WPF", wpf, failures);
        ValidateReferencesHeavySemanticRow(pairName + " Avalonia", avalonia, failures);
        ValidateToaFieldPairRow(ReferencesHeavyProofScenarioId, pageNumber, wpf, avalonia, failures);
        return failures;
    }

    private static void ValidateReferencesHeavySemanticRow(
        string rowName,
        FreeWVisualEvidenceNormalizedRow row,
        List<string> failures)
    {
        var fields = row.Fields;
        var toa = row.TableOfAuthorities;
        foreach (var keyword in ReferencesHeavyRequiredComplexFieldKeywords)
        {
            if (!fields.ComplexFieldKeywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
                failures.Add($"{rowName} expected cached {keyword} complex field metadata");
        }

        if (!fields.ComplexFieldResultSignatures.Contains("BIBLIOGRAPHY=References", StringComparer.OrdinalIgnoreCase))
            failures.Add($"{rowName} expected cached bibliography result signature");
        if (!fields.ComplexFieldResultSignatures.Contains("TOA=Cases\\t1, 2", StringComparer.OrdinalIgnoreCase))
            failures.Add($"{rowName} expected cached TOA page-reference sentinel");
        if (!toa.HasGeneratedTable || toa.EntryWithPageReferenceCount < 2)
            failures.Add($"{rowName} expected generated Table of Authorities page references");
        if (!toa.HasExplicitPageNumbers)
            failures.Add($"{rowName} expected explicit TOA page numbers");
        foreach (var category in ReferencesHeavyRequiredToaCategories)
        {
            if (!toa.Categories.Contains(category, StringComparer.OrdinalIgnoreCase))
                failures.Add($"{rowName} expected generated TOA category '{category}'");
        }

        foreach (var signature in ReferencesHeavyRequiredToaPageReferenceSignatures)
        {
            if (!toa.PageReferenceSignatures.Contains(signature, StringComparer.Ordinal))
                failures.Add($"{rowName} missing generated page-reference signature '{signature}'");
        }
    }

    private static List<string> BuildLegalReferenceSemanticFailures(
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia)
    {
        var failures = new List<string>();
        var pairName = $"legal-reference section page-number proof page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        ValidateLegalReferenceSemanticRow(pairName + " WPF", wpf, failures);
        ValidateLegalReferenceSemanticRow(pairName + " Avalonia", avalonia, failures);
        ValidateToaFieldPairRow(LegalReferenceSectionPageProofScenarioId, pageNumber, wpf, avalonia, failures);
        return failures;
    }

    private static void ValidateLegalReferenceSemanticRow(
        string rowName,
        FreeWVisualEvidenceNormalizedRow row,
        List<string> failures)
    {
        var fields = row.Fields;
        var toa = row.TableOfAuthorities;
        if (!fields.ComplexFieldKeywords.Contains("TOA", StringComparer.OrdinalIgnoreCase))
            failures.Add($"{rowName} expected cached TOA complex field metadata");
        if (!fields.ComplexFieldResultSignatures.Contains("TOA=Cases\\ti, 1", StringComparer.OrdinalIgnoreCase))
            failures.Add($"{rowName} expected cached TOA displayed page-reference sentinel");
        if (!toa.HasGeneratedTable || toa.EntryWithPageReferenceCount < 2)
            failures.Add($"{rowName} expected generated Table of Authorities page references");
        if (!toa.HasExplicitPageNumbers)
            failures.Add($"{rowName} expected explicit physical page numbers");
        if (!toa.PageReferences.Any(reference =>
            string.Equals(reference.PageReferenceKind, "section-formatted-page-numbers", StringComparison.OrdinalIgnoreCase)
            && reference.PageNumbers.Contains(1)
            && reference.PageNumbers.Contains(2)
            && reference.DisplayedPageReferences.Contains("i", StringComparer.OrdinalIgnoreCase)
            && reference.DisplayedPageReferences.Contains("1", StringComparer.OrdinalIgnoreCase)))
        {
            failures.Add($"{rowName} expected section-formatted displayed page references 'i' and '1' for physical pages 1 and 2");
        }

        foreach (var signature in LegalReferenceRequiredToaPageReferenceSignatures)
        {
            if (!toa.PageReferenceSignatures.Contains(signature, StringComparer.Ordinal))
                failures.Add($"{rowName} missing generated page-reference signature '{signature}'");
        }
    }

    private static List<string> BuildReviewProofingSemanticFailures(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia)
    {
        var failures = new List<string>();
        var pairName = $"review proofing visual proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        ValidateReviewProofingSemanticRow(pairName + " WPF", wpf.ProofingDiagnostics, failures);
        ValidateReviewProofingSemanticRow(pairName + " Avalonia", avalonia.ProofingDiagnostics, failures);
        ValidateReviewProofingPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
        ValidateReviewProtectionPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
        return failures;
    }

    private static void ValidateReviewProofingSemanticRow(
        string rowName,
        FreeWVisualProofingDiagnosticExpectation proofing,
        List<string> failures)
    {
        if (proofing.DiagnosticCount <= 0)
            failures.Add($"{rowName} expected proofing diagnostic evidence");
        if (proofing.SpellingCount <= 0)
            failures.Add($"{rowName} expected spelling diagnostic evidence");
        if (proofing.GrammarCount <= 0)
            failures.Add($"{rowName} expected grammar diagnostic evidence");
        if (proofing.AdornmentCount <= 0)
            failures.Add($"{rowName} expected proofing visual adornment evidence");
        if (!proofing.HasSpellingUnderline)
            failures.Add($"{rowName} expected spelling underline adornment evidence");
        if (!proofing.HasGrammarUnderline)
            failures.Add($"{rowName} expected grammar underline adornment evidence");
        if (proofing.AdornmentCount != proofing.DiagnosticCount)
            failures.Add($"{rowName} proofing adornment count must match diagnostic count");
        if (proofing.SpellingAdornmentCount != proofing.SpellingCount)
            failures.Add($"{rowName} spelling adornment count must match spelling diagnostic count");
        if (proofing.GrammarAdornmentCount != proofing.GrammarCount)
            failures.Add($"{rowName} grammar adornment count must match grammar diagnostic count");
        if (!proofing.AdornmentStableSignatures.Any(signature => signature.Contains("adornment=spelling-squiggle", StringComparison.Ordinal)))
            failures.Add($"{rowName} expected stable spelling-squiggle visual signature");
        if (!proofing.AdornmentStableSignatures.Any(signature => signature.Contains("adornment=grammar-squiggle", StringComparison.Ordinal)))
            failures.Add($"{rowName} expected stable grammar-squiggle visual signature");
    }

    private static List<string> BuildReviewMarkupSemanticFailures(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia)
    {
        var failures = new List<string>();
        var pairName = $"review markup visual proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        ValidateReviewMarkupSemanticRow(pairName + " WPF", scenarioId, wpf.ReviewMarkup, failures);
        ValidateReviewMarkupSemanticRow(pairName + " Avalonia", scenarioId, avalonia.ReviewMarkup, failures);
        ValidateReviewMarkupPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
        return failures;
    }

    private static void ValidateReviewMarkupSemanticRow(
        string rowName,
        string scenarioId,
        FreeWVisualReviewMarkupExpectation markup,
        List<string> failures)
    {
        if (string.Equals(scenarioId, "f2-tracked-changes", StringComparison.OrdinalIgnoreCase))
        {
            if (markup.RevisionCount <= 0)
                failures.Add($"{rowName} expected tracked revision evidence");
            if (markup.InsertionCount <= 0)
                failures.Add($"{rowName} expected tracked insertion evidence");
            if (markup.DeletionCount <= 0)
                failures.Add($"{rowName} expected tracked deletion evidence");
            if (markup.AuthorCount < 3
                || !markup.Authors.Contains("Alice", StringComparer.Ordinal)
                || !markup.Authors.Contains("Bob", StringComparer.Ordinal)
                || !markup.Authors.Contains("Carol", StringComparer.Ordinal))
            {
                failures.Add($"{rowName} expected tracked-change authors Alice, Bob, and Carol, found '{FormatSummaries(markup.Authors)}'");
            }
            if (markup.RevisionStableSignatures.Count < markup.RevisionCount)
                failures.Add($"{rowName} revision signatures must cover every tracked revision");
        }
        else if (string.Equals(scenarioId, "f2-comments", StringComparison.OrdinalIgnoreCase))
        {
            if (markup.CommentCount <= 0)
                failures.Add($"{rowName} expected comment thread evidence");
            if (markup.CommentAnchorCount <= 0)
                failures.Add($"{rowName} expected comment range anchor evidence");
            if (markup.CommentReferenceCount <= 0)
                failures.Add($"{rowName} expected comment reference marker evidence");
            if (markup.CommentStableSignatures.Count < markup.CommentCount)
                failures.Add($"{rowName} comment signatures must cover every top-level comment");
        }
    }

    private static List<string> BuildReviewCompareCombineSemanticFailures(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia)
    {
        var failures = new List<string>();
        var pairName = $"review compare/combine proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        ValidateReviewCompareCombineSemanticRow(pairName + " WPF", scenarioId, wpf.ReviewCompareCombine, failures);
        ValidateReviewCompareCombineSemanticRow(pairName + " Avalonia", scenarioId, avalonia.ReviewCompareCombine, failures);
        ValidateReviewCompareCombinePairRow(scenarioId, pageNumber, wpf, avalonia, failures);
        return failures;
    }

    private static void ValidateReviewCompareCombineSemanticRow(
        string rowName,
        string scenarioId,
        FreeWVisualReviewCompareCombineExpectation expectation,
        List<string> failures)
    {
        if (string.Equals(scenarioId, "review-compare-visual-proof", StringComparison.OrdinalIgnoreCase))
        {
            if (!expectation.HasCompareSemantics || !string.Equals(expectation.Operation, "compare", StringComparison.Ordinal))
                failures.Add($"{rowName} is missing compare semantic evidence");
            if (expectation.AuthorCount != 1 || !expectation.Authors.Contains("Riley", StringComparer.Ordinal))
                failures.Add($"{rowName} expected single compare author Riley, found '{FormatSummaries(expectation.Authors)}'");
        }
        else if (string.Equals(scenarioId, "review-combine-visual-proof", StringComparison.OrdinalIgnoreCase))
        {
            if (!expectation.HasCombineSemantics || !string.Equals(expectation.Operation, "combine", StringComparison.Ordinal))
                failures.Add($"{rowName} is missing combine semantic evidence");
            if (expectation.AuthorCount < 2
                || !expectation.Authors.Contains("Alice", StringComparer.Ordinal)
                || !expectation.Authors.Contains("Bob", StringComparer.Ordinal))
            {
                failures.Add($"{rowName} expected combined authors Alice and Bob, found '{FormatSummaries(expectation.Authors)}'");
            }
        }

        if (expectation.RevisionCount <= 0)
            failures.Add($"{rowName} expected tracked revision entries");
        if (expectation.InsertionCount <= 0)
            failures.Add($"{rowName} expected insertion revision entries");
        if (expectation.DeletionCount <= 0)
            failures.Add($"{rowName} expected deletion revision entries");
        if (expectation.StableSignatures.Count != expectation.RevisionCount)
            failures.Add($"{rowName} revision signatures must cover every review entry");
        if (!expectation.HasRetainedModelSafety)
            failures.Add($"{rowName} expected retained model safety evidence");
        if (!expectation.HasPreservedSettings)
            failures.Add($"{rowName} expected preserved settings evidence");
        if (!expectation.HasPreservedCustomProperties)
            failures.Add($"{rowName} expected preserved custom-property evidence");
        if (expectation.PreservedPartCount <= 0)
            failures.Add($"{rowName} expected preserved package part evidence");
        if (expectation.RetainedModelSafetySignatures.Count < 3)
            failures.Add($"{rowName} retained model safety signatures must cover settings, custom properties, and package parts");
    }

    private static List<string> BuildFloatingWrappingSemanticFailures(
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia)
    {
        var failures = new List<string>();
        var wpfObjects = wpf.DrawingObjects;
        var avaloniaObjects = avalonia.DrawingObjects;

        if (wpfObjects.FloatingObjectCount <= 0 || !wpfObjects.HasImages)
            failures.Add("floating/wrapping proof is missing WPF floating image evidence");
        if (!wpfObjects.HasSquareWrap)
            failures.Add("floating/wrapping proof is missing WPF square-wrap evidence");
        if (!wpfObjects.Objects.Any(o => o.Wrapping == ImageWrapping.Tight))
            failures.Add("floating/wrapping proof is missing WPF tight-wrap evidence");

        if (avaloniaObjects.FloatingObjectCount <= 0 || !avaloniaObjects.HasImages)
            failures.Add("floating/wrapping proof is missing Avalonia floating image evidence");
        if (!avaloniaObjects.HasTopAndBottomWrap)
            failures.Add("floating/wrapping proof is missing Avalonia top-and-bottom floating placement evidence");
        if (avaloniaObjects.BehindTextCount <= 0 || avaloniaObjects.InFrontCount <= 0)
            failures.Add("floating/wrapping proof is missing Avalonia behind-text and in-front z-order evidence");

        return failures;
    }

    private static List<string> BuildNotePlacementSemanticFailures(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia)
    {
        var failures = new List<string>();
        var pairName = $"note placement proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        ValidateNotePlacementSemanticRow(pairName + " WPF", scenarioId, wpf, failures);
        ValidateNotePlacementSemanticRow(pairName + " Avalonia", scenarioId, avalonia, failures);
        ValidateNotePlacementPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
        return failures;
    }

    private static void ValidateNotePlacementSemanticRow(
        string rowName,
        string scenarioId,
        FreeWVisualEvidenceNormalizedRow row,
        List<string> failures)
    {
        if (string.Equals(scenarioId, "f2-footnotes", StringComparison.OrdinalIgnoreCase))
        {
            if (row.HasEndnotes)
                failures.Add($"{rowName} should not report endnote evidence for the footnote fixture");
            if (row.IsSyntheticPage)
                failures.Add($"{rowName} should be a normal body page, not a synthetic endnote page");
            if (!row.ExpectedFeatureTags.Contains("footnotes", StringComparer.OrdinalIgnoreCase))
                failures.Add($"{rowName} expected the footnotes feature tag");
            return;
        }

        if (string.Equals(scenarioId, "f2-endnotes", StringComparison.OrdinalIgnoreCase))
        {
            if (row.HasFootnotes)
                failures.Add($"{rowName} should not report footnote evidence for the endnote fixture");
            if (!row.ExpectedFeatureTags.Contains("endnotes", StringComparer.OrdinalIgnoreCase))
                failures.Add($"{rowName} expected the endnotes feature tag");
            if (row.IsSyntheticPage)
                failures.Add($"{rowName} should attach endnotes to the final body page, not a synthetic page");
            if (row.PageNumber == row.PageCount && !row.HasEndnotes)
                failures.Add($"{rowName} expected endnote placement evidence on the final body page");
            if (row.PageNumber != row.PageCount && row.HasEndnotes)
                failures.Add($"{rowName} should only report endnote placement on the final body page");
        }
    }

    private static List<string> BuildSectionGeometrySemanticFailures(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia)
    {
        var failures = new List<string>();
        var pairName = $"section geometry proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        ValidateSectionGeometrySemanticRow(pairName + " WPF", scenarioId, pageNumber, wpf, failures);
        ValidateSectionGeometrySemanticRow(pairName + " Avalonia", scenarioId, pageNumber, avalonia, failures);
        ValidateSectionPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
        return failures;
    }

    private static void ValidateSectionGeometrySemanticRow(
        string rowName,
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow row,
        List<string> failures)
    {
        if (!row.ExpectedFeatureTags.Contains("section-geometry", StringComparer.OrdinalIgnoreCase))
            failures.Add($"{rowName} expected the section-geometry feature tag");
        if (!row.ExpectedFeatureTags.Contains("portrait-landscape", StringComparer.OrdinalIgnoreCase))
            failures.Add($"{rowName} expected the portrait-landscape feature tag");
        if (row.PageFeatures.Section.SectionOrdinal <= 0)
            failures.Add($"{rowName} expected positive section ordinal evidence");
        if (row.PageFeatures.Section.SectionRelativePageNumber <= 0)
            failures.Add($"{rowName} expected positive section-relative page evidence");
        if (string.IsNullOrWhiteSpace(row.PageFeatures.Section.OwnerId))
            failures.Add($"{rowName} expected a section owner id");
        if (pageNumber == 1 && row.PageFeatures.Section.SectionOrdinal != 1)
            failures.Add($"{rowName} expected page 1 to belong to section 1");
        if (pageNumber == 2 && row.PageFeatures.Section.SectionOrdinal != 2)
            failures.Add($"{rowName} expected page 2 to belong to section 2");
        if (!string.Equals(scenarioId, "f2-section-landscape", StringComparison.OrdinalIgnoreCase))
            failures.Add($"{rowName} expected f2-section-landscape as the focused section geometry proof scenario");
    }

    private static List<string> BuildTablePaginationSemanticFailures(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia)
    {
        var failures = new List<string>();
        var pairName = $"table pagination proof '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        ValidateTablePaginationSemanticRow(pairName + " WPF", scenarioId, wpf, failures);
        ValidateTablePaginationSemanticRow(pairName + " Avalonia", scenarioId, avalonia, failures);
        ValidateTablePairRow(scenarioId, pageNumber, wpf, avalonia, failures);
        return failures;
    }

    private static void ValidateTablePaginationSemanticRow(
        string rowName,
        string scenarioId,
        FreeWVisualEvidenceNormalizedRow row,
        List<string> failures)
    {
        var tables = row.Tables;
        if (tables.TableCount <= 0)
            failures.Add($"{rowName} expected table layout evidence");
        if (!tables.HasPaginationPlan)
            failures.Add($"{rowName} expected a table pagination plan");
        if (!tables.HasMultiPageTables || tables.EstimatedPageCount < 2)
            failures.Add($"{rowName} expected multi-page table pagination evidence");
        if (!tables.HasRepeatedHeaderPages)
            failures.Add($"{rowName} expected repeated header row evidence on later pages");
        if (!tables.HasKeepTogetherRows)
            failures.Add($"{rowName} expected keep-together row evidence");
        if (!tables.PaginationPlans.Any(plan =>
            plan.RepeatsHeaderRows
            && plan.Pages.Count >= 2
            && plan.Pages.Skip(1).Any(page => page.IncludesRepeatedHeader)))
        {
            failures.Add($"{rowName} expected pagination signatures with repeated headers after page 1");
        }

        if (!string.Equals(scenarioId, "table-page-composition-stress", StringComparison.OrdinalIgnoreCase))
            return;

        if (!row.PageFeatures.PageBorder.Present)
            failures.Add($"{rowName} expected page border evidence");
        if (!row.PageFeatures.Watermark.Present)
            failures.Add($"{rowName} expected watermark evidence");
        if (!row.Fields.HasPageFields || !row.Fields.HasNumPagesFields || !row.Fields.HasHeaderFooterFields)
            failures.Add($"{rowName} expected PAGE, NUMPAGES, and header/footer field evidence");
        if (row.HeaderFooters.SlotCount <= 0)
            failures.Add($"{rowName} expected header/footer surface evidence");
        if (!tables.HasCustomCellBorders || !tables.HasCellMargins || !tables.HasCellSpacing || !tables.HasNamedStyle)
            failures.Add($"{rowName} expected table page-composition cell border, margin, spacing, and named-style evidence");
    }

    private static FreeWVisualEvidenceTrust EvaluateDrawingObjectProofReadiness(
        IReadOnlyList<FreeWVisualBaselineComparison> relatedBaseline)
    {
        var failed = relatedBaseline
            .Where(comparison => !comparison.Trust.Passed)
            .ToList();
        if (failed.Count == 0)
            return new FreeWVisualEvidenceTrust(true, []);

        var failures = failed
            .SelectMany(comparison => comparison.Trust.Failures.Count == 0
                ? [$"{comparison.HostId}/{comparison.OutputName}: baseline comparison status '{comparison.Status}' failed trust"]
                : comparison.Trust.Failures.Select(failure => $"{comparison.HostId}/{comparison.OutputName}: {failure}"))
            .ToList();
        return new FreeWVisualEvidenceTrust(false, failures);
    }

    private static List<string> BuildHeaderFooterImageSemanticFailures(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia)
    {
        var failures = new List<string>();
        ValidateRequiredHeaderFooterImageEvidence(scenarioId, pageNumber, wpf, failures);
        ValidateRequiredHeaderFooterImageEvidence(scenarioId, pageNumber, avalonia, failures);
        ValidateHeaderFooterPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
        return failures;
    }

    private static string FormatDrawingObjectBaselineReadiness(
        IReadOnlyList<FreeWVisualBaselineComparison> relatedBaseline,
        string scenarioId)
    {
        if (relatedBaseline.Count == 0)
            return "paired renderer evidence is present; run Word PNG baseline comparison for " + scenarioId;

        if (relatedBaseline.All(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "Word COM or baseline generation unavailable; paired WPF/Avalonia evidence is retained without authoritative Word parity";
        }

        if (relatedBaseline.Any(comparison => !comparison.Trust.Passed))
            return "one or more Word baseline comparison rows failed trust; inspect baseline triage";

        if (relatedBaseline.Any(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.PassedStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "real Word PNG baseline compared within configured tolerance";
        }

        return "Word baseline policy rows are present and trusted";
    }

    private static string FormatHeaderFooterImageBaselineReadiness(
        IReadOnlyList<FreeWVisualBaselineComparison> relatedBaseline,
        string scenarioId)
    {
        if (relatedBaseline.Count == 0)
            return "paired renderer evidence is present; run Word PNG baseline comparison for " + scenarioId;

        if (relatedBaseline.All(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "Word COM or baseline generation unavailable; paired WPF/Avalonia header/footer image evidence is retained without authoritative Word parity";
        }

        if (relatedBaseline.Any(comparison => !comparison.Trust.Passed))
            return "one or more Word baseline comparison rows failed trust; inspect baseline triage";

        if (relatedBaseline.Any(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.PassedStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "real Word PNG baseline compared within configured tolerance";
        }

        return "Word baseline policy rows are present and trusted";
    }

    private static string FormatFloatingWrappingBaselineReadiness(
        IReadOnlyList<FreeWVisualBaselineComparison> relatedBaseline)
    {
        if (relatedBaseline.Count == 0)
            return "paired renderer evidence is present; run Word PNG baseline comparison for floating/wrapping proof";

        if (relatedBaseline.All(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "Word COM or baseline generation unavailable; paired WPF/Avalonia floating evidence is retained without authoritative Word wrap parity";
        }

        if (relatedBaseline.Any(comparison => !comparison.Trust.Passed))
            return "one or more Word baseline comparison rows failed trust; inspect baseline triage";

        if (relatedBaseline.Any(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.PassedStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "real Word PNG baseline compared within configured tolerance";
        }

        return "Word baseline policy rows are present and trusted";
    }

    private static string FormatNotePlacementBaselineReadiness(
        IReadOnlyList<FreeWVisualBaselineComparison> relatedBaseline,
        string scenarioId)
    {
        if (relatedBaseline.Count == 0)
            return "paired renderer evidence is present; run Word PNG baseline comparison for " + scenarioId;

        if (relatedBaseline.All(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "Word COM or baseline generation unavailable; paired WPF/Avalonia note placement evidence is retained without authoritative Word parity";
        }

        if (relatedBaseline.Any(comparison => !comparison.Trust.Passed))
            return "one or more Word baseline comparison rows failed trust; inspect baseline triage";

        if (relatedBaseline.Any(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.PassedStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "real Word PNG baseline compared within configured tolerance";
        }

        return "Word baseline policy rows are present and trusted";
    }

    private static string FormatSectionGeometryBaselineReadiness(
        IReadOnlyList<FreeWVisualBaselineComparison> relatedBaseline,
        string scenarioId)
    {
        if (relatedBaseline.Count == 0)
            return "paired renderer evidence is present; run Word PNG baseline comparison for " + scenarioId;

        if (relatedBaseline.All(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "Word COM or baseline generation unavailable; paired WPF/Avalonia section geometry evidence is retained without authoritative Word parity";
        }

        if (relatedBaseline.Any(comparison => !comparison.Trust.Passed))
            return "one or more Word baseline comparison rows failed trust; inspect baseline triage";

        if (relatedBaseline.Any(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.PassedStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "real Word PNG baseline compared within configured tolerance";
        }

        return "Word baseline policy rows are present and trusted";
    }

    private static string FormatTablePaginationBaselineReadiness(
        IReadOnlyList<FreeWVisualBaselineComparison> relatedBaseline,
        string scenarioId)
    {
        if (relatedBaseline.Count == 0)
            return "paired renderer evidence is present; run Word PNG baseline comparison for " + scenarioId;

        if (relatedBaseline.All(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "Word COM or baseline generation unavailable; paired WPF/Avalonia table pagination evidence is retained without authoritative Word table parity";
        }

        if (relatedBaseline.Any(comparison => !comparison.Trust.Passed))
            return "one or more Word baseline comparison rows failed trust; inspect baseline triage";

        if (relatedBaseline.Any(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.PassedStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "real Word PNG baseline compared within configured tolerance";
        }

        return "Word baseline policy rows are present and trusted";
    }

    private static string FormatReviewCompareCombineBaselineReadiness(
        IReadOnlyList<FreeWVisualBaselineComparison> relatedBaseline,
        string scenarioId)
    {
        if (relatedBaseline.Count == 0)
            return "paired renderer evidence is present; run Word PNG baseline comparison for " + scenarioId;

        if (relatedBaseline.All(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "Word COM or baseline generation unavailable; paired WPF/Avalonia compare/combine evidence is retained without authoritative Word parity";
        }

        if (relatedBaseline.Any(comparison => !comparison.Trust.Passed))
            return "one or more Word baseline comparison rows failed trust; inspect baseline triage";

        if (relatedBaseline.Any(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.PassedStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "real Word PNG baseline compared within configured tolerance";
        }

        return "Word baseline policy rows are present and trusted";
    }

    private static string FormatReviewMarkupBaselineReadiness(
        IReadOnlyList<FreeWVisualBaselineComparison> relatedBaseline,
        string scenarioId)
    {
        if (relatedBaseline.Count == 0)
            return "paired renderer evidence is present; run Word PNG baseline comparison for " + scenarioId;

        if (relatedBaseline.All(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "Word COM or baseline generation unavailable; paired WPF/Avalonia review markup evidence is retained without authoritative Word parity";
        }

        if (relatedBaseline.Any(comparison => !comparison.Trust.Passed))
            return "one or more Word baseline comparison rows failed trust; inspect baseline triage";

        if (relatedBaseline.Any(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.PassedStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "real Word PNG baseline compared within configured tolerance";
        }

        return "Word baseline policy rows are present and trusted";
    }

    private static string FormatReviewProofingBaselineReadiness(
        IReadOnlyList<FreeWVisualBaselineComparison> relatedBaseline,
        string scenarioId)
    {
        if (relatedBaseline.Count == 0)
            return "paired renderer evidence is present; run Word PNG baseline comparison for " + scenarioId;

        if (relatedBaseline.All(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "Word COM or baseline generation unavailable; paired WPF/Avalonia proofing adornment evidence is retained without authoritative Word parity";
        }

        if (relatedBaseline.Any(comparison => !comparison.Trust.Passed))
            return "one or more Word baseline comparison rows failed trust; inspect baseline triage";

        if (relatedBaseline.Any(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.PassedStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "real Word PNG baseline compared within configured tolerance";
        }

        return "Word baseline policy rows are present and trusted";
    }

    private static string FormatLegalReferenceBaselineReadiness(
        IReadOnlyList<FreeWVisualBaselineComparison> relatedBaseline)
    {
        if (relatedBaseline.Count == 0)
            return "paired renderer evidence is present; run Word PNG baseline comparison for legal-reference section page numbers";

        if (relatedBaseline.All(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "Word COM or baseline generation unavailable; paired WPF/Avalonia legal-reference page-number evidence is retained without authoritative Word parity";
        }

        if (relatedBaseline.Any(comparison => !comparison.Trust.Passed))
            return "one or more Word baseline comparison rows failed trust; inspect baseline triage";

        if (relatedBaseline.Any(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.PassedStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "real Word PNG baseline compared within configured tolerance";
        }

        return "Word baseline policy rows are present and trusted";
    }

    private static string FormatReferencesHeavyBaselineReadiness(
        IReadOnlyList<FreeWVisualBaselineComparison> relatedBaseline)
    {
        if (relatedBaseline.Count == 0)
            return "paired renderer evidence is present; run Word PNG baseline comparison for references-heavy field and TOA page-number evidence";

        if (relatedBaseline.All(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "Word COM or baseline generation unavailable; paired WPF/Avalonia references-heavy field and TOA evidence is retained without authoritative Word parity";
        }

        if (relatedBaseline.Any(comparison => !comparison.Trust.Passed))
            return "one or more Word baseline comparison rows failed trust; inspect baseline triage";

        if (relatedBaseline.Any(comparison => string.Equals(
            comparison.Status,
            FreeWVisualBaselineComparisonPlanner.PassedStatus,
            StringComparison.OrdinalIgnoreCase)))
        {
            return "real Word PNG baseline compared within configured tolerance";
        }

        return "Word baseline policy rows are present and trusted";
    }

    private static string FormatWordBaselineStatus(IReadOnlyList<FreeWVisualBaselineComparison> relatedBaseline)
    {
        if (relatedBaseline.Count == 0)
            return "not-run";

        return string.Join(
            ", ",
            relatedBaseline
                .GroupBy(comparison => comparison.Status, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => WordBaselineTriageStatusPriority(group.Key))
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => $"{group.Key}={group.Count().ToString(CultureInfo.InvariantCulture)}"));
    }

    private static string FormatOutputSummary(IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows)
    {
        if (rows.Count == 0)
            return "-";

        return string.Join(
            ", ",
            rows.Select(row => row.Trust.Passed ? row.OutputPath : row.OutputPath + " (failed)"));
    }

    private static string FormatReviewCompareCombineProofSemanticEvidence(
        FreeWVisualEvidenceNormalizedRow? wpf,
        FreeWVisualEvidenceNormalizedRow? avalonia)
    {
        var parts = new List<string>();
        if (wpf is not null)
            parts.Add("WPF " + FormatReviewCompareCombineSemanticEvidence(wpf.ReviewCompareCombine));
        if (avalonia is not null)
            parts.Add("Avalonia " + FormatReviewCompareCombineSemanticEvidence(avalonia.ReviewCompareCombine));
        return parts.Count == 0 ? "-" : string.Join("; ", parts);
    }

    private static string FormatReviewMarkupProofSemanticEvidence(
        FreeWVisualEvidenceNormalizedRow? wpf,
        FreeWVisualEvidenceNormalizedRow? avalonia)
    {
        var parts = new List<string>();
        if (wpf is not null)
            parts.Add("WPF " + FormatReviewMarkupSemanticEvidence(wpf.ReviewMarkup));
        if (avalonia is not null)
            parts.Add("Avalonia " + FormatReviewMarkupSemanticEvidence(avalonia.ReviewMarkup));
        return parts.Count == 0 ? "-" : string.Join("; ", parts);
    }

    private static string FormatReviewMarkupSemanticEvidence(
        FreeWVisualReviewMarkupExpectation expectation) =>
        string.Join(
            ", ",
            "revisions=" + expectation.RevisionCount.ToString(CultureInfo.InvariantCulture),
            "insertions=" + expectation.InsertionCount.ToString(CultureInfo.InvariantCulture),
            "deletions=" + expectation.DeletionCount.ToString(CultureInfo.InvariantCulture),
            "comments=" + expectation.CommentCount.ToString(CultureInfo.InvariantCulture),
            "replies=" + expectation.ReplyCount.ToString(CultureInfo.InvariantCulture),
            "resolved=" + expectation.ResolvedCommentCount.ToString(CultureInfo.InvariantCulture),
            "anchors=" + expectation.CommentAnchorCount.ToString(CultureInfo.InvariantCulture),
            "references=" + expectation.CommentReferenceCount.ToString(CultureInfo.InvariantCulture),
            "authors=" + FormatSummaries(expectation.Authors));

    private static string FormatReviewCompareCombineSemanticEvidence(
        FreeWVisualReviewCompareCombineExpectation expectation) =>
        string.Concat(
            expectation.Operation,
            " ",
            FormatReviewCompareCombineCounts(expectation),
            ", authors=",
            expectation.Authors.Count == 0 ? "-" : string.Join("/", expectation.Authors),
            ", retained=",
            expectation.HasRetainedModelSafety
                ? string.Join("/", expectation.RetainedModelSafetySignatures)
                : "-");

    private static string FormatReviewCompareCombineCounts(
        FreeWVisualReviewCompareCombineExpectation expectation) =>
        string.Concat(
            "revisions=",
            expectation.RevisionCount.ToString(CultureInfo.InvariantCulture),
            " insertions=",
            expectation.InsertionCount.ToString(CultureInfo.InvariantCulture),
            " deletions=",
            expectation.DeletionCount.ToString(CultureInfo.InvariantCulture),
            " formatting=",
            expectation.FormattingCount.ToString(CultureInfo.InvariantCulture),
            " preservedParts=",
            expectation.PreservedPartCount.ToString(CultureInfo.InvariantCulture),
            " preservedContentTypeDefaults=",
            expectation.PreservedContentTypeDefaultCount.ToString(CultureInfo.InvariantCulture));

    private static string FormatReviewProofingProofSemanticEvidence(
        FreeWVisualEvidenceNormalizedRow? wpf,
        FreeWVisualEvidenceNormalizedRow? avalonia)
    {
        var parts = new List<string>();
        if (wpf is not null)
            parts.Add("WPF " + FormatReviewProofingSemanticEvidence(wpf.ProofingDiagnostics));
        if (avalonia is not null)
            parts.Add("Avalonia " + FormatReviewProofingSemanticEvidence(avalonia.ProofingDiagnostics));
        return parts.Count == 0 ? "-" : string.Join("; ", parts);
    }

    private static string FormatReviewProofingSemanticEvidence(
        FreeWVisualProofingDiagnosticExpectation proofing)
    {
        var adornmentSummaries = proofing.Adornments
            .Select(adornment => $"{adornment.AdornmentKind} {adornment.UnderlineStyle} {adornment.ColorHex}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(summary => summary, StringComparer.Ordinal)
            .ToList();
        return string.Concat(
            "diagnostics=",
            proofing.DiagnosticCount.ToString(CultureInfo.InvariantCulture),
            " spelling=",
            proofing.SpellingCount.ToString(CultureInfo.InvariantCulture),
            " grammar=",
            proofing.GrammarCount.ToString(CultureInfo.InvariantCulture),
            " adornments=",
            proofing.AdornmentCount.ToString(CultureInfo.InvariantCulture),
            " squiggles=",
            adornmentSummaries.Count == 0 ? "-" : string.Join("/", adornmentSummaries));
    }

    private static string FormatFloatingWrappingSemanticEvidence(
        FreeWVisualEvidenceNormalizedRow? wpf,
        FreeWVisualEvidenceNormalizedRow? avalonia)
    {
        var parts = new List<string>();
        if (wpf is not null)
            parts.Add("WPF " + FormatFloatingWrappingRowSemanticEvidence(wpf));
        if (avalonia is not null)
            parts.Add("Avalonia " + FormatFloatingWrappingRowSemanticEvidence(avalonia));
        return parts.Count == 0 ? "-" : string.Join("; ", parts);
    }

    private static string FormatFloatingWrappingRowSemanticEvidence(FreeWVisualEvidenceNormalizedRow row)
    {
        var objects = row.DrawingObjects;
        var wrapKinds = objects.Objects
            .Select(o => o.Wrapping.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(w => w, StringComparer.Ordinal)
            .ToList();
        var parts = new List<string>
        {
            $"{objects.FloatingObjectCount.ToString(CultureInfo.InvariantCulture)} floating object(s)",
            $"{objects.BehindTextCount.ToString(CultureInfo.InvariantCulture)} behind text",
            $"{objects.InFrontCount.ToString(CultureInfo.InvariantCulture)} in front",
            "wraps=" + (wrapKinds.Count == 0 ? "none" : string.Join("/", wrapKinds))
        };

        if (objects.HasImages)
            parts.Add("image evidence");
        if (objects.HasZOrder)
            parts.Add("z-order evidence");

        return string.Join(", ", parts);
    }

    private static string FormatNotePlacementProofSemanticEvidence(
        FreeWVisualEvidenceNormalizedRow? wpf,
        FreeWVisualEvidenceNormalizedRow? avalonia)
    {
        var parts = new List<string>();
        if (wpf is not null)
            parts.Add("WPF " + FormatNotePlacementRowSemanticEvidence(wpf));
        if (avalonia is not null)
            parts.Add("Avalonia " + FormatNotePlacementRowSemanticEvidence(avalonia));
        return parts.Count == 0 ? "-" : string.Join("; ", parts);
    }

    private static string FormatNotePlacementRowSemanticEvidence(FreeWVisualEvidenceNormalizedRow row) =>
        string.Join(
            ", ",
            row.HasFootnotes ? "footnotes" : "no footnotes",
            row.HasEndnotes ? "endnotes" : "no endnotes",
            row.IsSyntheticPage ? "synthetic page" : "body page",
            "tags=" + FormatSummaries(row.ExpectedFeatureTags));

    private static string FormatSectionGeometryProofSemanticEvidence(
        FreeWVisualEvidenceNormalizedRow? wpf,
        FreeWVisualEvidenceNormalizedRow? avalonia)
    {
        var parts = new List<string>();
        if (wpf is not null)
            parts.Add("WPF " + FormatSectionGeometryRowSemanticEvidence(wpf));
        if (avalonia is not null)
            parts.Add("Avalonia " + FormatSectionGeometryRowSemanticEvidence(avalonia));
        return parts.Count == 0 ? "-" : string.Join("; ", parts);
    }

    private static string FormatSectionGeometryRowSemanticEvidence(FreeWVisualEvidenceNormalizedRow row)
    {
        var section = row.PageFeatures.Section;
        var orientation = section.SectionOrdinal == 2 ? "landscape" : "portrait";
        return string.Concat(
            "section=",
            section.SectionOrdinal.ToString(CultureInfo.InvariantCulture),
            " owner=",
            string.IsNullOrWhiteSpace(section.OwnerId) ? "-" : section.OwnerId,
            " sectionPage=",
            section.SectionRelativePageNumber.ToString(CultureInfo.InvariantCulture),
            " expectedOrientation=",
            orientation,
            " outputPixels=",
            row.PixelWidth.ToString(CultureInfo.InvariantCulture),
            "x",
            row.PixelHeight.ToString(CultureInfo.InvariantCulture));
    }

    private static string FormatTablePaginationProofSemanticEvidence(
        FreeWVisualEvidenceNormalizedRow? wpf,
        FreeWVisualEvidenceNormalizedRow? avalonia)
    {
        var parts = new List<string>();
        if (wpf is not null)
            parts.Add("WPF " + FormatTablePaginationRowSemanticEvidence(wpf));
        if (avalonia is not null)
            parts.Add("Avalonia " + FormatTablePaginationRowSemanticEvidence(avalonia));
        return parts.Count == 0 ? "-" : string.Join("; ", parts);
    }

    private static string FormatTablePaginationRowSemanticEvidence(FreeWVisualEvidenceNormalizedRow row)
    {
        var tables = row.Tables;
        var repeatedHeaderPages = tables.PaginationPlans
            .SelectMany(plan => plan.Pages)
            .Count(page => page.IncludesRepeatedHeader);
        var pageFeatures = new List<string>();
        if (row.PageFeatures.PageBorder.Present)
            pageFeatures.Add("page-border");
        if (row.PageFeatures.Watermark.Present)
            pageFeatures.Add("watermark");
        if (row.HeaderFooters.SlotCount > 0)
            pageFeatures.Add("header-footer");
        if (row.Fields.HasPageFields)
            pageFeatures.Add("PAGE");
        if (row.Fields.HasNumPagesFields)
            pageFeatures.Add("NUMPAGES");

        var cellFeatures = new[]
        {
            tables.HasCustomCellBorders ? "borders" : "no-borders",
            tables.HasCellMargins ? "margins" : "no-margins",
            tables.HasCellSpacing ? "spacing" : "no-spacing",
            tables.HasNamedStyle ? "named-style" : "no-named-style"
        };

        return string.Concat(
            tables.TableCount.ToString(CultureInfo.InvariantCulture),
            " table(s); estimatedPages=",
            tables.EstimatedPageCount.ToString(CultureInfo.InvariantCulture),
            "; rowCells=",
            tables.TotalRows.ToString(CultureInfo.InvariantCulture),
            "/",
            tables.TotalCells.ToString(CultureInfo.InvariantCulture),
            "; repeatedHeaderPages=",
            repeatedHeaderPages.ToString(CultureInfo.InvariantCulture),
            "; keepRows=",
            BoolFlag(tables.HasKeepTogetherRows),
            "; tableSig=",
            BuildTablePaginationTableFingerprint(tables),
            "; paginationSig=",
            BuildTablePaginationPlanFingerprint(tables),
            "; cellFeatures=",
            FormatSummaries(cellFeatures),
            "; pageFeatures=",
            pageFeatures.Count == 0 ? "-" : FormatSummaries(pageFeatures));
    }

    private static string FormatDrawingObjectProofSemanticEvidence(
        FreeWVisualEvidenceNormalizedRow? wpf,
        FreeWVisualEvidenceNormalizedRow? avalonia)
    {
        var parts = new List<string>();
        if (wpf is not null)
            parts.Add("WPF " + FormatDrawingObjectRowSemanticEvidence(wpf));
        if (avalonia is not null)
            parts.Add("Avalonia " + FormatDrawingObjectRowSemanticEvidence(avalonia));
        return parts.Count == 0 ? "-" : string.Join("; ", parts);
    }

    private static string FormatHeaderFooterImageProofSemanticEvidence(
        FreeWVisualEvidenceNormalizedRow? wpf,
        FreeWVisualEvidenceNormalizedRow? avalonia)
    {
        var parts = new List<string>();
        if (wpf is not null)
            parts.Add("WPF " + FormatHeaderFooterImageRowSemanticEvidence(wpf));
        if (avalonia is not null)
            parts.Add("Avalonia " + FormatHeaderFooterImageRowSemanticEvidence(avalonia));
        return parts.Count == 0 ? "-" : string.Join("; ", parts);
    }

    private static string FormatHeaderFooterImageRowSemanticEvidence(FreeWVisualEvidenceNormalizedRow row)
    {
        var plan = row.HeaderFooters ?? HeaderFooterVisualPlanner.EmptyExpectation;
        var slotSummaries = plan.Slots
            .Where(slot => slot.ImageCount > 0)
            .Select(slot => string.Concat(
                slot.SlotName,
                "/section",
                slot.SectionOrdinal.ToString(CultureInfo.InvariantCulture),
                "/page",
                slot.PageNumber.ToString(CultureInfo.InvariantCulture),
                "/images=",
                slot.ImageCount.ToString(CultureInfo.InvariantCulture),
                "/align=",
                slot.Alignment))
            .OrderBy(summary => summary, StringComparer.Ordinal)
            .ToList();

        return string.Concat(
            plan.ImageCount.ToString(CultureInfo.InvariantCulture),
            " header/footer image(s), ",
            plan.SlotCount.ToString(CultureInfo.InvariantCulture),
            " slot(s), slots=",
            FormatSummaries(plan.SlotNames ?? []),
            ", image slots=",
            slotSummaries.Count == 0 ? "-" : FormatSummaries(slotSummaries));
    }

    private static string FormatLegalReferenceProofSemanticEvidence(
        FreeWVisualEvidenceNormalizedRow? wpf,
        FreeWVisualEvidenceNormalizedRow? avalonia)
    {
        var parts = new List<string>();
        if (wpf is not null)
            parts.Add("WPF " + FormatLegalReferenceRowSemanticEvidence(wpf));
        if (avalonia is not null)
            parts.Add("Avalonia " + FormatLegalReferenceRowSemanticEvidence(avalonia));
        return parts.Count == 0 ? "-" : string.Join("; ", parts);
    }

    private static string FormatReferencesHeavyProofSemanticEvidence(
        FreeWVisualEvidenceNormalizedRow? wpf,
        FreeWVisualEvidenceNormalizedRow? avalonia)
    {
        var parts = new List<string>();
        if (wpf is not null)
            parts.Add("WPF " + FormatReferencesHeavyRowSemanticEvidence(wpf));
        if (avalonia is not null)
            parts.Add("Avalonia " + FormatReferencesHeavyRowSemanticEvidence(avalonia));
        return parts.Count == 0 ? "-" : string.Join("; ", parts);
    }

    private static string FormatReferencesHeavyRowSemanticEvidence(FreeWVisualEvidenceNormalizedRow row)
    {
        var fields = row.Fields;
        var toa = row.TableOfAuthorities;
        return string.Concat(
            "keywords=",
            FormatSummaries(fields.ComplexFieldKeywords),
            ", field results=",
            FormatSummaries(fields.ComplexFieldResultSignatures),
            ", TOA entries=",
            toa.EntryCount.ToString(CultureInfo.InvariantCulture),
            ", page refs=",
            toa.EntryWithPageReferenceCount.ToString(CultureInfo.InvariantCulture),
            ", categories=",
            FormatSummaries(toa.Categories),
            ", signatures=",
            FormatSummaries(toa.PageReferenceSignatures));
    }

    private static string FormatLegalReferenceRowSemanticEvidence(FreeWVisualEvidenceNormalizedRow row)
    {
        var toa = row.TableOfAuthorities;
        var sectionFormatted = toa.PageReferences
            .Where(reference => string.Equals(
                reference.PageReferenceKind,
                "section-formatted-page-numbers",
                StringComparison.OrdinalIgnoreCase))
            .Select(reference => reference.StableSignature)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();
        return string.Concat(
            "TOA entries=",
            toa.EntryCount.ToString(CultureInfo.InvariantCulture),
            ", page refs=",
            toa.EntryWithPageReferenceCount.ToString(CultureInfo.InvariantCulture),
            ", section refs=",
            sectionFormatted.Count == 0 ? "-" : string.Join("/", sectionFormatted));
    }

    private static string FormatDrawingObjectRowSemanticEvidence(FreeWVisualEvidenceNormalizedRow row)
    {
        var objects = row.DrawingObjects;
        var chartSmartArt = row.ChartSmartArt;
        var objectSignatures = BuildObjectFormatSemanticSignatures(objects.Objects);
        var parts = new List<string>
        {
            $"{objects.FloatingObjectCount.ToString(CultureInfo.InvariantCulture)} object(s)",
            $"{objects.GroupChildren.ChildCount.ToString(CultureInfo.InvariantCulture)} grouped child object(s)",
            $"{objects.Effects.EffectObjectCount.ToString(CultureInfo.InvariantCulture)} effect object(s)",
            $"{objects.Effects.RenderedGroupChildEffectObjectCount.ToString(CultureInfo.InvariantCulture)} rendered grouped child effect object(s)",
            $"{objects.AltTextObjectCount.ToString(CultureInfo.InvariantCulture)} alt-text object(s)",
            "kinds=" + FormatDrawingObjectKinds(objects)
        };

        if (objectSignatures.Count > 0)
            parts.Add("object format signatures=" + FormatSummaries(objectSignatures));

        if (objects.Effects.EffectSummaries.Count > 0)
            parts.Add("effects=" + FormatSummaries(objects.Effects.EffectSummaries));

        if (objects.GroupChildren.ChildKindSummaries.Count > 0)
        {
            parts.Add("grouped child kinds=" + FormatSummaries(objects.GroupChildren.ChildKindSummaries));
        }

        if (objects.GroupChildren.ChildVisualSignatures.Count > 0)
        {
            parts.Add("grouped child visual signatures=" + objects.GroupChildren.ChildVisualSignatures.Count.ToString(CultureInfo.InvariantCulture));
        }

        if (chartSmartArt.ChartCount > 0 || chartSmartArt.SmartArtCount > 0)
        {
            parts.Add(
                $"{chartSmartArt.ChartCount.ToString(CultureInfo.InvariantCulture)} chart(s)");
            parts.Add(
                $"{chartSmartArt.SmartArtCount.ToString(CultureInfo.InvariantCulture)} SmartArt");
            if (chartSmartArt.ChartVisualSignatures.Count > 0)
                parts.Add("chart signatures=" + chartSmartArt.ChartVisualSignatures.Count.ToString(CultureInfo.InvariantCulture));
            if (chartSmartArt.ChartDataSignatures.Count > 0)
                parts.Add("chart data signatures=" + chartSmartArt.ChartDataSignatures.Count.ToString(CultureInfo.InvariantCulture));
            if (chartSmartArt.SmartArtVisualSignatures.Count > 0)
                parts.Add("SmartArt signatures=" + chartSmartArt.SmartArtVisualSignatures.Count.ToString(CultureInfo.InvariantCulture));
            var smartArtLayoutIds = chartSmartArt.SmartArts
                .Select(plan => plan.LayoutId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (smartArtLayoutIds.Count > 0)
                parts.Add("SmartArt layouts=" + string.Join("/", smartArtLayoutIds));
            var smartArtGeometryKinds = chartSmartArt.SmartArts
                .Select(plan => plan.LayoutGeometry?.Kind.ToString())
                .Where(kind => !string.IsNullOrWhiteSpace(kind))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (smartArtGeometryKinds.Count > 0)
                parts.Add("SmartArt geometry=" + string.Join("/", smartArtGeometryKinds));
            var smartArtPolygonCount = chartSmartArt.SmartArts
                .SelectMany(plan => plan.LayoutGeometry?.Nodes ?? Array.Empty<SmartArtLayoutNodeGeometry>())
                .Count(node => node.HasPolygon);
            if (smartArtPolygonCount > 0)
                parts.Add("SmartArt polygon nodes=" + smartArtPolygonCount.ToString(CultureInfo.InvariantCulture));
        }

        if (row.PageFeatures.Watermark.Present)
        {
            parts.Add(row.PageFeatures.Watermark.IsPicture
                ? "picture watermark"
                : "text watermark");
        }

        if (row.PageFeatures.PageBorder.Present)
            parts.Add("page border");

        return string.Join(", ", parts);
    }

    private static List<string> BuildObjectFormatSemanticSignatures(IEnumerable<DocumentFloatingObjectSnapshot> objects) =>
        objects
            .Select(o => string.Join(
                ":",
                o.TypeTag,
                o.Wrapping.ToString(),
                "z" + o.ZOrderIndex.ToString(CultureInfo.InvariantCulture),
                FormatDouble(o.Rect.WidthDip) + "x" + FormatDouble(o.Rect.HeightDip),
                o.BehindText ? "behind" : "front"))
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();

    private static string FormatDrawingObjectKinds(FreeWVisualDrawingObjectExpectation objects)
    {
        var kinds = new List<string>();
        if (objects.HasImages) kinds.Add("image");
        if (objects.HasShapes) kinds.Add("shape");
        if (objects.HasCharts) kinds.Add("chart");
        if (objects.HasSmartArt) kinds.Add("smartart");
        if (objects.HasWordArt) kinds.Add("wordart");
        if (objects.HasGroups) kinds.Add("group");
        return kinds.Count == 0 ? "none" : string.Join("/", kinds);
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

    private static FreeWVisualEvidenceAuthoritySummary BuildEvidenceAuthoritySummary(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> evidence,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var trustedRows = evidence
            .Where(row => row.Trust.Passed)
            .ToList();
        var comparableRows = trustedRows
            .Where(row => FreeWVisualBaselineComparisonPlanner.ResolveWordBaselinePolicy(row).IsComparable)
            .ToList();
        var compared = baselineComparisons
            .Count(comparison => IsBaselineStatus(comparison, FreeWVisualBaselineComparisonPlanner.PassedStatus));
        var unavailable = baselineComparisons
            .Count(comparison => IsBaselineStatus(comparison, FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus));
        var missing = baselineComparisons
            .Count(comparison => IsBaselineStatus(comparison, FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus));
        var failedOrDecode = baselineComparisons
            .Count(comparison =>
                IsBaselineStatus(comparison, FreeWVisualBaselineComparisonPlanner.FailedStatus)
                || IsBaselineStatus(comparison, FreeWVisualBaselineComparisonPlanner.DecodeFailedStatus));
        var skipped = baselineComparisons
            .Count(comparison => IsBaselineStatus(comparison, FreeWVisualBaselineComparisonPlanner.SkippedStatus));
        var comparedEvidenceIds = baselineComparisons
            .Where(comparison =>
                IsBaselineStatus(comparison, FreeWVisualBaselineComparisonPlanner.PassedStatus)
                || IsBaselineStatus(comparison, FreeWVisualBaselineComparisonPlanner.FailedStatus)
                || IsBaselineStatus(comparison, FreeWVisualBaselineComparisonPlanner.DecodeFailedStatus))
            .Select(comparison => comparison.EvidenceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var preparatoryEvidenceRows = trustedRows.Count(row => !comparedEvidenceIds.Contains(row.EvidenceId));
        var hasIncompleteWordBaseline = unavailable > 0 || missing > 0 || failedOrDecode > 0;
        var authorityLevel = DetermineEvidenceAuthorityLevel(
            baselineComparisons.Count,
            compared,
            unavailable,
            missing,
            failedOrDecode);
        var authoritativeWordPngParityClaimed =
            compared > 0
            && comparableRows.Count > 0
            && !hasIncompleteWordBaseline
            && baselineComparisons
                .Where(comparison => !IsBaselineStatus(comparison, FreeWVisualBaselineComparisonPlanner.SkippedStatus))
                .All(comparison => IsBaselineStatus(comparison, FreeWVisualBaselineComparisonPlanner.PassedStatus));

        return new FreeWVisualEvidenceAuthoritySummary(
            authorityLevel,
            authoritativeWordPngParityClaimed,
            trustedRows.Count,
            comparableRows.Count,
            compared,
            unavailable,
            missing,
            failedOrDecode,
            skipped,
            preparatoryEvidenceRows,
            BuildEvidenceAuthorityNotes(
                authorityLevel,
                authoritativeWordPngParityClaimed,
                comparableRows.Count,
                baselineComparisons.Count,
                compared,
                unavailable,
                missing,
                failedOrDecode));
    }

    private static string DetermineEvidenceAuthorityLevel(
        int baselineComparisonCount,
        int compared,
        int unavailable,
        int missing,
        int failedOrDecode)
    {
        if (baselineComparisonCount == 0)
            return "local-visual-evidence-only";
        if (unavailable > 0 && compared == 0)
            return "word-baseline-unavailable";
        if (missing > 0)
            return "word-baseline-missing";
        if (failedOrDecode > 0)
            return "word-baseline-needs-review";
        if (compared > 0 && unavailable == 0)
            return "real-word-png-comparison";

        return "mixed-word-baseline-evidence";
    }

    private static IReadOnlyList<string> BuildEvidenceAuthorityNotes(
        string authorityLevel,
        bool authoritativeWordPngParityClaimed,
        int comparableRows,
        int baselineComparisonCount,
        int compared,
        int unavailable,
        int missing,
        int failedOrDecode)
    {
        var notes = new List<string>();
        if (baselineComparisonCount == 0)
        {
            notes.Add(
                comparableRows == 0
                    ? "No comparable Word-baseline rows were present in the selected evidence."
                    : "Trusted WPF/Avalonia evidence is preparatory only until real Word PNG baselines are supplied.");
        }
        if (unavailable > 0)
        {
            notes.Add(
                "Word COM or baseline generation was unavailable; no authoritative Word PNG parity is claimed for unavailable rows.");
        }
        if (missing > 0)
            notes.Add("Mapped Word PNG baseline paths are recorded but missing on disk.");
        if (failedOrDecode > 0)
            notes.Add("One or more real Word PNG comparisons failed or could not be decoded; inspect triage before claiming parity.");
        if (compared > 0 && authoritativeWordPngParityClaimed)
            notes.Add("All non-skipped real Word PNG comparisons passed the configured tolerance.");
        if (compared > 0 && !authoritativeWordPngParityClaimed && authorityLevel == "real-word-png-comparison")
            notes.Add("Real Word PNG comparisons ran, but the selected evidence does not cover every comparable row.");

        return notes;
    }

    private static bool IsBaselineStatus(FreeWVisualBaselineComparison comparison, string status) =>
        string.Equals(comparison.Status, status, StringComparison.OrdinalIgnoreCase);

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
        blockers.AddRange(BuildNotePlacementWordBaselineBlockers(summary, baselineComparisons));
        blockers.AddRange(BuildSectionGeometryWordBaselineBlockers(summary, baselineComparisons));
        blockers.AddRange(BuildGroupedDrawingObjectWordBaselineBlockers(summary, baselineComparisons));
        blockers.AddRange(BuildObjectFormatWordBaselineBlockers(summary, baselineComparisons));
        blockers.AddRange(BuildSmartArtPolygonWordBaselineBlockers(summary, baselineComparisons));
        blockers.AddRange(BuildReviewMarkupWordBaselineBlockers(summary, baselineComparisons));
        blockers.AddRange(BuildReviewCompareCombineWordBaselineBlockers(summary, baselineComparisons));
        blockers.AddRange(BuildReviewProofingWordBaselineBlockers(summary, baselineComparisons));
        blockers.AddRange(BuildEquationStructureWordBaselineBlockers(summary, baselineComparisons));
        blockers.AddRange(BuildLegalReferenceWordBaselineBlockers(summary, baselineComparisons));
        blockers.AddRange(BuildTablePaginationWordBaselineBlockers(summary, baselineComparisons));
        blockers.AddRange(BuildHeaderFooterImageWordBaselineBlockers(summary, baselineComparisons));
        blockers.AddRange(BuildWordArtWatermarkWordBaselineBlockers(summary, baselineComparisons));

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

    private static IReadOnlyList<FreeWVisualRemainingEvidenceBlocker> BuildGroupedDrawingObjectWordBaselineBlockers(
        FreeWVisualEvidenceNormalizedSummary summary,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        const string scenarioId = "drawing-objects-complex";
        var rows = summary.Evidence
            .Where(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
            .Where(row => row.Trust.Passed)
            .ToList();
        if (rows.Count == 0)
            return [];

        var semanticEvidence = BuildGroupedDrawingObjectSemanticEvidence(rows);
        if (semanticEvidence.Count == 0)
        {
            return
            [
                BuildGroupedDrawingObjectVisualBlocker(
                    "semantic-grouped-drawing-objects-missing",
                    "trusted grouped child kind, visual-signature, and rendered-effect metadata",
                    "trusted drawing-objects-complex evidence did not record grouped child visual metadata; regenerate current-schema evidence or fix shared drawing-object planning before treating this as a Word-baseline-only gap",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: false)
            ];
        }

        var related = baselineComparisons
            .Where(comparison => string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (related.Count == 0)
        {
            return
            [
                BuildGroupedDrawingObjectVisualBlocker(
                    "needs-word-baseline-run",
                    "real MS Word PNG comparisons for grouped drawing/object fidelity",
                    "grouped child visual signatures are present in trusted FreeW evidence; run a Word-baseline comparison for drawing-objects-complex to prove Word visual parity",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: true)
            ];
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
                ? "MS Word baseline PNG generation was unavailable for drawing-objects-complex"
                : string.Join("; ", reasons);
            return
            [
                BuildGroupedDrawingObjectVisualBlocker(
                    FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
                    "real MS Word PNG comparisons for grouped drawing/object fidelity",
                    reason,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true)
            ];
        }

        if (related.Any(comparison =>
            string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return
            [
                BuildGroupedDrawingObjectVisualBlocker(
                    FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus,
                    "real MS Word PNG comparisons for grouped drawing/object fidelity",
                    "grouped child visual signatures are present in trusted FreeW evidence, but grouped drawing Word baseline PNGs are missing",
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true)
            ];
        }

        if (related.All(comparison =>
            string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.PassedStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return [];
        }

        return
        [
            BuildGroupedDrawingObjectVisualBlocker(
                "needs-render-review",
                "render-review resolution for failed grouped drawing Word PNG comparisons",
                "grouped drawing Word baseline comparison did not fully pass; inspect grouped child rendering differences",
                statuses,
                candidates,
                semanticEvidence,
                requiresWordBaseline: false)
        ];
    }

    private static IReadOnlyList<string> BuildGroupedDrawingObjectSemanticEvidence(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows)
    {
        return rows
            .Select(row =>
            {
                var groupChildren = NormalizeGroupChildren(row.DrawingObjects.GroupChildren);
                if (groupChildren.ChildCount <= 0 || groupChildren.ChildVisualSignatures.Count == 0)
                    return null;

                var effects = row.DrawingObjects.Effects;
                return string.Concat(
                    row.HostId,
                    "/p",
                    row.PageNumber.ToString(CultureInfo.InvariantCulture),
                    ": children=",
                    groupChildren.ChildCount.ToString(CultureInfo.InvariantCulture),
                    ", kinds=",
                    FormatSummaries(groupChildren.ChildKindSummaries),
                    ", visualSignatures=",
                    groupChildren.ChildVisualSignatures.Count.ToString(CultureInfo.InvariantCulture),
                    ", renderedEffects=",
                    effects.RenderedGroupChildEffectObjectCount.ToString(CultureInfo.InvariantCulture),
                    ":",
                    FormatSummaries(effects.RenderedGroupChildEffectSummaries));
            })
            .Where(summary => !string.IsNullOrWhiteSpace(summary))
            .Cast<string>()
            .OrderBy(summary => summary, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static FreeWVisualRemainingEvidenceBlocker BuildGroupedDrawingObjectVisualBlocker(
        string status,
        string requiredEvidence,
        string reason,
        IReadOnlyList<string> relatedBaselineStatuses,
        IReadOnlyList<string> candidateBaselinePaths,
        IReadOnlyList<string> semanticEvidence,
        bool requiresWordBaseline)
    {
        return new FreeWVisualRemainingEvidenceBlocker(
            "drawing-objects-complex-word-baseline-fidelity",
            "drawing-objects-complex",
            "Grouped drawing/object visual fidelity",
            status,
            requiredEvidence,
            reason,
            relatedBaselineStatuses,
            candidateBaselinePaths,
            semanticEvidence,
            requiresWordBaseline,
            new FreeWVisualEvidenceTrust(true, []));
    }

    private static IReadOnlyList<FreeWVisualRemainingEvidenceBlocker> BuildObjectFormatWordBaselineBlockers(
        FreeWVisualEvidenceNormalizedSummary summary,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        const string scenarioId = "object-format-position-size-style";
        var rows = summary.Evidence
            .Where(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
            .Where(row => row.Trust.Passed)
            .ToList();
        if (rows.Count == 0)
            return [];

        var semanticEvidence = BuildObjectFormatSemanticEvidence(rows);
        if (semanticEvidence.Count == 0)
        {
            return
            [
                BuildObjectFormatVisualBlocker(
                    "semantic-object-format-missing",
                    "trusted object-format position, size, style, alt-text, and effect metadata",
                    "trusted object-format-position-size-style evidence did not record object-format semantic metadata; regenerate current-schema evidence or fix shared drawing-object planning before treating this as a Word-baseline-only gap",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: false)
            ];
        }

        var related = baselineComparisons
            .Where(comparison => string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (related.Count == 0)
        {
            return
            [
                BuildObjectFormatVisualBlocker(
                    "needs-word-baseline-run",
                    "real MS Word PNG comparisons for object-format position, size, and style fidelity",
                    "object-format semantic signatures are present in trusted FreeW evidence; run a Word-baseline comparison for object-format-position-size-style to prove Word visual parity",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: true)
            ];
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
                ? "MS Word baseline PNG generation was unavailable for object-format-position-size-style"
                : string.Join("; ", reasons);
            return
            [
                BuildObjectFormatVisualBlocker(
                    FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
                    "real MS Word PNG comparisons for object-format position, size, and style fidelity",
                    reason,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true)
            ];
        }

        if (related.Any(comparison =>
            string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return
            [
                BuildObjectFormatVisualBlocker(
                    FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus,
                    "real MS Word PNG comparisons for object-format position, size, and style fidelity",
                    "object-format semantic signatures are present in trusted FreeW evidence, but mapped Word baseline PNGs are missing",
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true)
            ];
        }

        if (related.All(comparison =>
            string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.PassedStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return [];
        }

        return
        [
            BuildObjectFormatVisualBlocker(
                "needs-render-review",
                "render-review resolution for failed object-format Word PNG comparisons",
                "object-format Word baseline comparison did not fully pass; inspect position, size, style, alt-text, or effect rendering differences",
                statuses,
                candidates,
                semanticEvidence,
                requiresWordBaseline: false)
        ];
    }

    private static IReadOnlyList<string> BuildObjectFormatSemanticEvidence(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows)
    {
        return rows
            .Select(row =>
            {
                var objects = row.DrawingObjects;
                var effects = objects.Effects;
                if (objects.FloatingObjectCount < 3 ||
                    objects.AltTextObjectCount < 3 ||
                    effects.EffectObjectCount < 3 ||
                    !objects.HasZOrder)
                {
                    return null;
                }

                return string.Concat(
                    row.HostId,
                    "/p",
                    row.PageNumber.ToString(CultureInfo.InvariantCulture),
                    ": objects=",
                    objects.FloatingObjectCount.ToString(CultureInfo.InvariantCulture),
                    ", altText=",
                    objects.AltTextObjectCount.ToString(CultureInfo.InvariantCulture),
                    ", effects=",
                    effects.EffectObjectCount.ToString(CultureInfo.InvariantCulture),
                    ", signatures=",
                    FormatSummaries(BuildObjectFormatSemanticSignatures(objects.Objects)),
                    ", effectSummaries=",
                    FormatSummaries(effects.EffectSummaries));
            })
            .Where(summary => !string.IsNullOrWhiteSpace(summary))
            .Cast<string>()
            .OrderBy(summary => summary, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static FreeWVisualRemainingEvidenceBlocker BuildObjectFormatVisualBlocker(
        string status,
        string requiredEvidence,
        string reason,
        IReadOnlyList<string> relatedBaselineStatuses,
        IReadOnlyList<string> candidateBaselinePaths,
        IReadOnlyList<string> semanticEvidence,
        bool requiresWordBaseline)
    {
        return new FreeWVisualRemainingEvidenceBlocker(
            "object-format-position-size-style-word-baseline-fidelity",
            "object-format-position-size-style",
            "Drawing/object visual fidelity",
            status,
            requiredEvidence,
            reason,
            relatedBaselineStatuses,
            candidateBaselinePaths,
            semanticEvidence,
            requiresWordBaseline,
            new FreeWVisualEvidenceTrust(true, []));
    }

    private static IReadOnlyList<FreeWVisualRemainingEvidenceBlocker> BuildHeaderFooterImageWordBaselineBlockers(
        FreeWVisualEvidenceNormalizedSummary summary,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        const string scenarioId = "f2-hf-images";
        var rows = summary.Evidence
            .Where(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
            .Where(row => row.Trust.Passed)
            .ToList();
        if (rows.Count == 0)
            return [];

        var semanticEvidence = BuildHeaderFooterImageSemanticEvidence(rows);
        if (semanticEvidence.Count == 0)
        {
            return
            [
                BuildHeaderFooterImageVisualBlocker(
                    "semantic-header-footer-images-missing",
                    "trusted WPF and Avalonia header/footer image metadata",
                    "trusted f2-hf-images evidence did not record header/footer image metadata; regenerate current-schema evidence or fix shared header/footer planning before treating this as a Word-baseline-only gap",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: false)
            ];
        }

        var related = baselineComparisons
            .Where(comparison => string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (related.Count == 0)
        {
            return
            [
                BuildHeaderFooterImageVisualBlocker(
                    "needs-word-baseline-run",
                    "real MS Word PNG comparisons for header/footer image placement",
                    "trusted FreeW header/footer image evidence is present; run a Word-baseline comparison for f2-hf-images to prove Word visual parity",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: true)
            ];
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
                ? "MS Word baseline PNG generation was unavailable for f2-hf-images"
                : string.Join("; ", reasons);
            return
            [
                BuildHeaderFooterImageVisualBlocker(
                    FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
                    "real MS Word PNG comparisons for header/footer image placement",
                    reason,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true)
            ];
        }

        if (related.Any(comparison =>
            string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return
            [
                BuildHeaderFooterImageVisualBlocker(
                    FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus,
                    "real MS Word PNG comparisons for header/footer image placement",
                    "trusted header/footer image evidence is present, but mapped Word baseline PNGs are missing for f2-hf-images",
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true)
            ];
        }

        if (related.All(comparison =>
            string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.PassedStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return [];
        }

        return
        [
            BuildHeaderFooterImageVisualBlocker(
                "needs-render-review",
                "render-review resolution for failed header/footer image Word PNG comparisons",
                "f2-hf-images Word baseline comparison did not fully pass; inspect header/footer image placement differences",
                statuses,
                candidates,
                semanticEvidence,
                requiresWordBaseline: false)
        ];
    }

    private static FreeWVisualRemainingEvidenceBlocker BuildHeaderFooterImageVisualBlocker(
        string status,
        string requiredEvidence,
        string reason,
        IReadOnlyList<string> relatedBaselineStatuses,
        IReadOnlyList<string> candidateBaselinePaths,
        IReadOnlyList<string> semanticEvidence,
        bool requiresWordBaseline) =>
        new(
            "f2-hf-images-word-baseline-fidelity",
            "f2-hf-images",
            "Header/footer image visual fidelity",
            status,
            requiredEvidence,
            reason,
            relatedBaselineStatuses,
            candidateBaselinePaths,
            semanticEvidence,
            requiresWordBaseline,
            new FreeWVisualEvidenceTrust(true, []));

    private static IReadOnlyList<string> BuildHeaderFooterImageSemanticEvidence(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows) =>
        rows
            .Where(row => row.HeaderFooters.HasImages && row.HeaderFooters.ImageCount > 0)
            .Select(row => string.Concat(
                row.HostId,
                "/p",
                row.PageNumber.ToString(CultureInfo.InvariantCulture),
                ": images=",
                row.HeaderFooters.ImageCount.ToString(CultureInfo.InvariantCulture),
                "; slots=",
                FormatSummaries(row.HeaderFooters.SlotNames ?? []),
                "; signatures=",
                FormatSummaries(row.HeaderFooters.ImageSignatures ?? [])))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<FreeWVisualRemainingEvidenceBlocker> BuildNotePlacementWordBaselineBlockers(
        FreeWVisualEvidenceNormalizedSummary summary,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var blockers = new List<FreeWVisualRemainingEvidenceBlocker>();
        foreach (var scenarioId in NotePlacementVisualProofScenarioIds)
        {
            var rows = summary.Evidence
                .Where(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .Where(row => row.Trust.Passed)
                .ToList();
            if (rows.Count == 0)
                continue;

            var semanticEvidence = BuildNotePlacementSemanticEvidence(rows);
            if (semanticEvidence.Count == 0)
            {
                blockers.Add(BuildNotePlacementVisualBlocker(
                    scenarioId,
                    "semantic-note-placement-missing",
                    "trusted WPF and Avalonia footnote/endnote placement metadata",
                    "trusted note evidence did not record footnote/endnote placement metadata; regenerate current-schema evidence or fix shared note planning before treating this as a Word-baseline-only gap",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: false));
                continue;
            }

            var related = baselineComparisons
                .Where(comparison => string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (related.Count == 0)
            {
                blockers.Add(BuildNotePlacementVisualBlocker(
                    scenarioId,
                    "needs-word-baseline-run",
                    "real MS Word PNG comparisons for note placement",
                    "trusted FreeW note placement evidence is present; run a Word-baseline comparison for " + scenarioId + " to prove Word visual parity",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
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
                    ? "MS Word baseline PNG generation was unavailable for " + scenarioId
                    : string.Join("; ", reasons);
                blockers.Add(BuildNotePlacementVisualBlocker(
                    scenarioId,
                    FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
                    "real MS Word PNG comparisons for note placement",
                    reason,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
            }

            if (related.Any(comparison =>
                string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus, StringComparison.OrdinalIgnoreCase)))
            {
                blockers.Add(BuildNotePlacementVisualBlocker(
                    scenarioId,
                    FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus,
                    "real MS Word PNG comparisons for note placement",
                    "trusted note placement evidence is present, but mapped Word baseline PNGs are missing for " + scenarioId,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
            }

            if (related.All(comparison =>
                string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.PassedStatus, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            blockers.Add(BuildNotePlacementVisualBlocker(
                scenarioId,
                "needs-render-review",
                "render-review resolution for failed note-placement Word PNG comparisons",
                scenarioId + " Word baseline comparison did not fully pass; inspect note placement differences",
                statuses,
                candidates,
                semanticEvidence,
                requiresWordBaseline: false));
        }

        return blockers;
    }

    private static FreeWVisualRemainingEvidenceBlocker BuildNotePlacementVisualBlocker(
        string scenarioId,
        string status,
        string requiredEvidence,
        string reason,
        IReadOnlyList<string> relatedBaselineStatuses,
        IReadOnlyList<string> candidateBaselinePaths,
        IReadOnlyList<string> semanticEvidence,
        bool requiresWordBaseline) =>
        new(
            scenarioId + "-word-baseline-fidelity",
            scenarioId,
            "Note placement visual fidelity",
            status,
            requiredEvidence,
            reason,
            relatedBaselineStatuses,
            candidateBaselinePaths,
            semanticEvidence,
            requiresWordBaseline,
            new FreeWVisualEvidenceTrust(true, []));

    private static IReadOnlyList<string> BuildNotePlacementSemanticEvidence(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows) =>
        rows
            .Where(row => row.HasFootnotes || row.HasEndnotes)
            .Select(row => string.Concat(
                row.HostId,
                "/p",
                row.PageNumber.ToString(CultureInfo.InvariantCulture),
                ": footnotes=",
                BoolFlag(row.HasFootnotes),
                "; endnotes=",
                BoolFlag(row.HasEndnotes),
                "; synthetic=",
                BoolFlag(row.IsSyntheticPage),
                "; tags=",
                FormatSummaries(row.ExpectedFeatureTags)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<FreeWVisualRemainingEvidenceBlocker> BuildSectionGeometryWordBaselineBlockers(
        FreeWVisualEvidenceNormalizedSummary summary,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var blockers = new List<FreeWVisualRemainingEvidenceBlocker>();
        foreach (var scenarioId in SectionGeometryVisualProofScenarioIds)
        {
            var rows = summary.Evidence
                .Where(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .Where(row => row.Trust.Passed)
                .ToList();
            if (rows.Count == 0)
                continue;

            var semanticEvidence = BuildSectionGeometrySemanticEvidence(rows);
            if (semanticEvidence.Count == 0)
            {
                blockers.Add(BuildSectionGeometryVisualBlocker(
                    scenarioId,
                    "semantic-section-geometry-missing",
                    "trusted WPF and Avalonia section geometry metadata",
                    "trusted section geometry evidence did not record portrait/landscape section-owner metadata; regenerate current-schema evidence before treating this as a Word-baseline-only gap",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: false));
                continue;
            }

            var related = baselineComparisons
                .Where(comparison => string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (related.Count == 0)
            {
                blockers.Add(BuildSectionGeometryVisualBlocker(
                    scenarioId,
                    "needs-word-baseline-run",
                    "real MS Word PNG comparisons for section portrait/landscape page geometry",
                    "trusted FreeW section geometry evidence is present; run a Word-baseline comparison for " + scenarioId + " to prove Word visual parity",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
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
                blockers.Add(BuildSectionGeometryVisualBlocker(
                    scenarioId,
                    FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
                    "real MS Word PNG comparisons for section portrait/landscape page geometry",
                    reasons.Count == 0
                        ? "MS Word baseline PNG generation was unavailable for " + scenarioId
                        : string.Join("; ", reasons),
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
            }

            if (related.Any(comparison =>
                string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus, StringComparison.OrdinalIgnoreCase)))
            {
                blockers.Add(BuildSectionGeometryVisualBlocker(
                    scenarioId,
                    FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus,
                    "real MS Word PNG comparisons for section portrait/landscape page geometry",
                    "trusted section geometry evidence is present, but Word baseline PNGs are missing for portrait/landscape comparison",
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
            }

            if (related.All(comparison =>
                string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.PassedStatus, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            blockers.Add(BuildSectionGeometryVisualBlocker(
                scenarioId,
                "needs-render-review",
                "render-review resolution for failed section geometry Word PNG comparisons",
                scenarioId + " Word baseline comparison did not fully pass; inspect portrait/landscape page geometry rendering differences",
                statuses,
                candidates,
                semanticEvidence,
                requiresWordBaseline: false));
        }

        return blockers;
    }

    private static FreeWVisualRemainingEvidenceBlocker BuildSectionGeometryVisualBlocker(
        string scenarioId,
        string status,
        string requiredEvidence,
        string reason,
        IReadOnlyList<string> relatedBaselineStatuses,
        IReadOnlyList<string> candidateBaselinePaths,
        IReadOnlyList<string> semanticEvidence,
        bool requiresWordBaseline) =>
        new(
            scenarioId + "-word-baseline-fidelity",
            scenarioId,
            "Section geometry fidelity",
            status,
            requiredEvidence,
            reason,
            relatedBaselineStatuses,
            candidateBaselinePaths,
            semanticEvidence,
            requiresWordBaseline,
            new FreeWVisualEvidenceTrust(true, []));

    private static IReadOnlyList<string> BuildSectionGeometrySemanticEvidence(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows) =>
        rows
            .Select(row => string.Concat(
                row.HostId,
                "/p",
                row.PageNumber.ToString(CultureInfo.InvariantCulture),
                ": section=",
                row.PageFeatures.Section.SectionOrdinal.ToString(CultureInfo.InvariantCulture),
                "; owner=",
                row.PageFeatures.Section.OwnerId,
                "; sectionPage=",
                row.PageFeatures.Section.SectionRelativePageNumber.ToString(CultureInfo.InvariantCulture),
                "; pixels=",
                row.PixelWidth.ToString(CultureInfo.InvariantCulture),
                "x",
                row.PixelHeight.ToString(CultureInfo.InvariantCulture)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<FreeWVisualRemainingEvidenceBlocker> BuildEquationStructureWordBaselineBlockers(
        FreeWVisualEvidenceNormalizedSummary summary,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        const string scenarioId = "equation-structures";
        var rows = summary.Evidence
            .Where(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
            .Where(row => row.Trust.Passed)
            .ToList();
        if (rows.Count == 0)
            return [];

        var semanticEvidence = BuildEquationStructureSemanticEvidence(rows);
        if (semanticEvidence.Count == 0 || rows.Any(row => !HasRequiredEquationStructureEvidence(row.Equations)))
        {
            return
            [
                BuildEquationStructureVisualBlocker(
                    "semantic-equation-structure-depth-missing",
                    "trusted WPF and Avalonia equation geometry metadata for every modeled OfficeMath family",
                    "trusted equation evidence did not record the required fraction, radical, n-ary, script, matrix, equation-array, decorator, delimiter, group-character, function-apply, segment-role, geometry, and spacing signatures; regenerate current-schema evidence or fix shared equation planning before treating this as a Word-baseline-only gap",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: false)
            ];
        }

        var related = baselineComparisons
            .Where(comparison => string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (related.Count == 0)
        {
            return
            [
                BuildEquationStructureVisualBlocker(
                    "needs-word-baseline-run",
                    "real MS Word PNG comparisons for OfficeMath equation structures",
                    "trusted FreeW equation structure evidence is present; run a Word-baseline comparison for equation-structures to prove Word visual parity",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: true)
            ];
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
                ? "MS Word baseline PNG generation was unavailable for equation-structures"
                : string.Join("; ", reasons);
            return
            [
                BuildEquationStructureVisualBlocker(
                    FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
                    "real MS Word PNG comparisons for OfficeMath equation structures",
                    reason,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true)
            ];
        }

        if (related.Any(comparison =>
            string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return
            [
                BuildEquationStructureVisualBlocker(
                    FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus,
                    "real MS Word PNG comparisons for OfficeMath equation structures",
                    "trusted equation structure evidence is present, but mapped Word baseline PNGs are missing for equation-structures",
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true)
            ];
        }

        if (related.All(comparison =>
            string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.PassedStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return [];
        }

        return
        [
            BuildEquationStructureVisualBlocker(
                "needs-render-review",
                "render-review resolution for failed equation-structure Word PNG comparisons",
                "equation-structures Word baseline comparison did not fully pass; inspect OfficeMath rendering differences",
                statuses,
                candidates,
                semanticEvidence,
                requiresWordBaseline: false)
        ];
    }

    private static IReadOnlyList<FreeWVisualRemainingEvidenceBlocker> BuildSmartArtPolygonWordBaselineBlockers(
        FreeWVisualEvidenceNormalizedSummary summary,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        const string scenarioId = "chart-smartart-complex";
        var rows = summary.Evidence
            .Where(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
            .Where(row => row.Trust.Passed)
            .ToList();
        if (rows.Count == 0)
            return [];

        var semanticEvidence = BuildSmartArtPolygonSemanticEvidence(rows);
        if (semanticEvidence.Count == 0)
        {
            return
            [
                BuildSmartArtPolygonVisualBlocker(
                    "semantic-smartart-polygon-geometry-missing",
                    "trusted WPF and Avalonia Basic Pyramid SmartArt polygon metadata",
                    "trusted chart/SmartArt evidence did not record Basic Pyramid polygon geometry; regenerate current-schema evidence or fix shared SmartArt planning before treating this as a Word-baseline-only gap",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: false)
            ];
        }

        var related = baselineComparisons
            .Where(comparison => string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (related.Count == 0)
        {
            return
            [
                BuildSmartArtPolygonVisualBlocker(
                    "needs-word-baseline-run",
                    "real MS Word PNG comparisons for Basic Pyramid SmartArt polygon layout",
                    "trusted FreeW SmartArt polygon evidence is present; run a Word-baseline comparison for chart-smartart-complex to prove Word visual parity",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: true)
            ];
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
                ? "MS Word baseline PNG generation was unavailable for chart-smartart-complex"
                : string.Join("; ", reasons);
            return
            [
                BuildSmartArtPolygonVisualBlocker(
                    FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
                    "real MS Word PNG comparisons for Basic Pyramid SmartArt polygon layout",
                    reason,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true)
            ];
        }

        if (related.Any(comparison =>
            string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return
            [
                BuildSmartArtPolygonVisualBlocker(
                    FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus,
                    "real MS Word PNG comparisons for Basic Pyramid SmartArt polygon layout",
                    "trusted SmartArt polygon evidence is present, but mapped Word baseline PNGs are missing for chart-smartart-complex",
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true)
            ];
        }

        if (related.All(comparison =>
            string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.PassedStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return [];
        }

        return
        [
            BuildSmartArtPolygonVisualBlocker(
                "needs-render-review",
                "render-review resolution for failed SmartArt polygon Word PNG comparisons",
                "chart-smartart-complex Word baseline comparison did not fully pass; inspect SmartArt polygon rendering differences",
                statuses,
                candidates,
                semanticEvidence,
                requiresWordBaseline: false)
        ];
    }

    private static FreeWVisualRemainingEvidenceBlocker BuildSmartArtPolygonVisualBlocker(
        string status,
        string requiredEvidence,
        string reason,
        IReadOnlyList<string> relatedBaselineStatuses,
        IReadOnlyList<string> candidateBaselinePaths,
        IReadOnlyList<string> semanticEvidence,
        bool requiresWordBaseline) =>
        new(
            "chart-smartart-complex-word-baseline-fidelity",
            "chart-smartart-complex",
            "SmartArt polygon visual fidelity",
            status,
            requiredEvidence,
            reason,
            relatedBaselineStatuses,
            candidateBaselinePaths,
            semanticEvidence,
            requiresWordBaseline,
            new FreeWVisualEvidenceTrust(true, []));

    private static IReadOnlyList<string> BuildSmartArtPolygonSemanticEvidence(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows) =>
        rows
            .Where(row => HasSmartArtPolygonGeometry(row.ChartSmartArt))
            .Select(row => string.Concat(
                row.HostId,
                "/p",
                row.PageNumber.ToString(CultureInfo.InvariantCulture),
                ": SmartArt=",
                row.ChartSmartArt.SmartArtCount.ToString(CultureInfo.InvariantCulture),
                "; layouts=",
                FormatSummaries(row.ChartSmartArt.SmartArts.Select(plan => plan.LayoutId).ToList()),
                "; polygonNodes=",
                row.ChartSmartArt.SmartArts
                    .SelectMany(plan => plan.LayoutGeometry?.Nodes ?? Array.Empty<SmartArtLayoutNodeGeometry>())
                    .Count(node => node.HasPolygon)
                    .ToString(CultureInfo.InvariantCulture),
                "; signatures=",
                FormatSummaries(row.ChartSmartArt.SmartArtVisualSignatures)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static FreeWVisualRemainingEvidenceBlocker BuildEquationStructureVisualBlocker(
        string status,
        string requiredEvidence,
        string reason,
        IReadOnlyList<string> relatedBaselineStatuses,
        IReadOnlyList<string> candidateBaselinePaths,
        IReadOnlyList<string> semanticEvidence,
        bool requiresWordBaseline) =>
        new(
            "equation-structures-word-baseline-fidelity",
            "equation-structures",
            "Equation structure visual fidelity",
            status,
            requiredEvidence,
            reason,
            relatedBaselineStatuses,
            candidateBaselinePaths,
            semanticEvidence,
            requiresWordBaseline,
            new FreeWVisualEvidenceTrust(true, []));

    private static IReadOnlyList<string> BuildEquationStructureSemanticEvidence(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows) =>
        rows
            .Where(row => row.Equations.EquationCount > 0)
            .Select(row => string.Concat(
                row.HostId,
                "/p",
                row.PageNumber.ToString(CultureInfo.InvariantCulture),
                ": equations=",
                row.Equations.EquationCount.ToString(CultureInfo.InvariantCulture),
                "; elements=",
                row.Equations.ElementCount.ToString(CultureInfo.InvariantCulture),
                "; nestedSlots=",
                row.Equations.NestedSlotCount.ToString(CultureInfo.InvariantCulture),
                "; maxDepth=",
                row.Equations.MaxNestedSlotDepth.ToString(CultureInfo.InvariantCulture),
                "; kinds=",
                string.Join("/", row.Equations.ElementKindCounts.OrderBy(value => value, StringComparer.Ordinal)),
                "; structureFamilies=",
                FormatRequiredEquationElementKinds(row.Equations),
                "; roleFamilies=",
                FormatRequiredEquationSegmentRoles(row.Equations),
                "; geometryFamilies=",
                FormatRequiredEquationSignatureTokens(row.Equations.ElementGeometrySignatures, EquationStructureRequiredGeometryTokens),
                "; spacingFamilies=",
                FormatRequiredEquationSignatureTokens(row.Equations.SpacingGeometrySignatures, EquationStructureRequiredSpacingTokens)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();

    private static bool HasRequiredEquationStructureEvidence(FreeWVisualEquationExpectation equations) =>
        equations.EquationCount >= 8
        && equations.ElementCount >= 8
        && equations.SegmentCount >= 8
        && EquationStructureRequiredElementKinds.All(required =>
            ReadEquationElementKindCount(equations.ElementKindCounts, required.Kind) >= required.MinimumCount)
        && EquationStructureRequiredSegmentRoles.All(required =>
            ReadEquationElementKindCount(equations.SegmentRoleCounts, required.Role) >= required.MinimumCount)
        && EquationStructureRequiredGeometryTokens.All(token =>
            equations.ElementGeometrySignatures.Any(signature => signature.Contains(token, StringComparison.Ordinal)))
        && EquationStructureRequiredSpacingTokens.All(token =>
            equations.SpacingGeometrySignatures.Any(signature => signature.Contains(token, StringComparison.Ordinal)));

    private static string FormatRequiredEquationElementKinds(FreeWVisualEquationExpectation equations) =>
        string.Join(
            "/",
            EquationStructureRequiredElementKinds.Select(required =>
                required.Kind + "=" + ReadEquationElementKindCount(equations.ElementKindCounts, required.Kind)
                    .ToString(CultureInfo.InvariantCulture)));

    private static string FormatRequiredEquationSegmentRoles(FreeWVisualEquationExpectation equations) =>
        string.Join(
            "/",
            EquationStructureRequiredSegmentRoles.Select(required =>
                required.Role + "=" + ReadEquationElementKindCount(equations.SegmentRoleCounts, required.Role)
                    .ToString(CultureInfo.InvariantCulture)));

    private static int ReadEquationElementKindCount(IReadOnlyList<string> elementKindCounts, string kind)
    {
        var prefix = kind + "=";
        var match = elementKindCounts.FirstOrDefault(signature =>
            signature.StartsWith(prefix, StringComparison.Ordinal));
        if (match is null)
            return 0;

        return int.TryParse(match[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            ? count
            : 0;
    }

    private static string FormatRequiredEquationSignatureTokens(
        IReadOnlyList<string> signatures,
        IReadOnlyList<string> requiredTokens) =>
        string.Join(
            "/",
            requiredTokens.Select(token =>
                token + ":" + signatures.Count(signature => signature.Contains(token, StringComparison.Ordinal))
                    .ToString(CultureInfo.InvariantCulture)));

    private static IReadOnlyList<FreeWVisualRemainingEvidenceBlocker> BuildReviewMarkupWordBaselineBlockers(
        FreeWVisualEvidenceNormalizedSummary summary,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var blockers = new List<FreeWVisualRemainingEvidenceBlocker>();
        foreach (var scenarioId in ReviewMarkupVisualProofScenarioIds)
        {
            var rows = summary.Evidence
                .Where(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .Where(row => row.Trust.Passed)
                .ToList();
            if (rows.Count == 0)
                continue;

            var semanticEvidence = BuildReviewMarkupSemanticEvidence(rows);
            if (semanticEvidence.Count == 0)
            {
                blockers.Add(BuildReviewMarkupVisualBlocker(
                    scenarioId,
                    "semantic-review-markup-missing",
                    "trusted WPF and Avalonia review markup metadata",
                    "trusted review evidence did not record stable tracked-change or comment metadata; regenerate current-schema evidence or fix shared review markup planning before treating this as a Word-baseline-only gap",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: false));
                continue;
            }

            var related = baselineComparisons
                .Where(comparison => string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (related.Count == 0)
            {
                blockers.Add(BuildReviewMarkupVisualBlocker(
                    scenarioId,
                    "needs-word-baseline-run",
                    "real MS Word PNG comparisons for review markup visuals",
                    "trusted FreeW review markup evidence is present; run a Word-baseline comparison for " + scenarioId + " to prove Word visual parity",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
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
                    ? "MS Word baseline PNG generation was unavailable for " + scenarioId
                    : string.Join("; ", reasons);
                blockers.Add(BuildReviewMarkupVisualBlocker(
                    scenarioId,
                    FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
                    "real MS Word PNG comparisons for review markup visuals",
                    reason,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
            }

            if (related.Any(comparison =>
                string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus, StringComparison.OrdinalIgnoreCase)))
            {
                blockers.Add(BuildReviewMarkupVisualBlocker(
                    scenarioId,
                    FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus,
                    "real MS Word PNG comparisons for review markup visuals",
                    "trusted review markup evidence is present, but mapped Word baseline PNGs are missing for " + scenarioId,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
            }

            if (related.All(comparison =>
                string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.PassedStatus, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            blockers.Add(BuildReviewMarkupVisualBlocker(
                scenarioId,
                "word-baseline-comparison-not-passed",
                "passing real MS Word PNG comparisons for review markup visuals",
                "trusted review markup evidence is present, but at least one mapped Word baseline comparison did not pass for " + scenarioId,
                statuses,
                candidates,
                semanticEvidence,
                requiresWordBaseline: false));
        }

        return blockers;
    }

    private static FreeWVisualRemainingEvidenceBlocker BuildReviewMarkupVisualBlocker(
        string scenarioId,
        string status,
        string requiredEvidence,
        string reason,
        IReadOnlyList<string> relatedBaselineStatuses,
        IReadOnlyList<string> candidateBaselinePaths,
        IReadOnlyList<string> semanticEvidence,
        bool requiresWordBaseline) =>
        new(
            scenarioId + "-word-baseline-fidelity",
            scenarioId,
            "Review markup visual fidelity",
            status,
            requiredEvidence,
            reason,
            relatedBaselineStatuses,
            candidateBaselinePaths,
            semanticEvidence,
            requiresWordBaseline,
            new FreeWVisualEvidenceTrust(true, []));

    private static IReadOnlyList<string> BuildReviewMarkupSemanticEvidence(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows) =>
        rows
            .Where(row => row.ReviewMarkup.RevisionCount > 0 || row.ReviewMarkup.CommentCount > 0)
            .Select(row => string.Concat(
                row.HostId,
                "/p",
                row.PageNumber.ToString(CultureInfo.InvariantCulture),
                ": revisions=",
                row.ReviewMarkup.RevisionCount.ToString(CultureInfo.InvariantCulture),
                "; comments=",
                row.ReviewMarkup.CommentCount.ToString(CultureInfo.InvariantCulture),
                "; replies=",
                row.ReviewMarkup.ReplyCount.ToString(CultureInfo.InvariantCulture),
                "; authors=",
                FormatSummaries(row.ReviewMarkup.Authors),
                "; revisionSignatures=",
                FormatSummaries(row.ReviewMarkup.RevisionStableSignatures),
                "; commentSignatures=",
                FormatSummaries(row.ReviewMarkup.CommentStableSignatures)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<FreeWVisualRemainingEvidenceBlocker> BuildReviewCompareCombineWordBaselineBlockers(
        FreeWVisualEvidenceNormalizedSummary summary,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var blockers = new List<FreeWVisualRemainingEvidenceBlocker>();
        foreach (var scenarioId in ReviewCompareCombineVisualProofScenarioIds)
        {
            var rows = summary.Evidence
                .Where(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .Where(row => row.Trust.Passed)
                .ToList();
            if (rows.Count == 0)
                continue;

            var semanticEvidence = BuildReviewCompareCombineSemanticEvidence(rows);
            if (semanticEvidence.Count == 0)
            {
                blockers.Add(BuildReviewCompareCombineVisualBlocker(
                    scenarioId,
                    "semantic-review-compare-combine-missing",
                    "trusted WPF and Avalonia review compare/combine revision metadata",
                    "trusted compare/combine evidence did not record stable revision, author, and retained-model metadata; regenerate current-schema evidence or fix shared review compare/combine planning before treating this as a Word-baseline-only gap",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: false));
                continue;
            }

            var related = baselineComparisons
                .Where(comparison => string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (related.Count == 0)
            {
                blockers.Add(BuildReviewCompareCombineVisualBlocker(
                    scenarioId,
                    "needs-word-baseline-run",
                    "real MS Word PNG comparisons for review compare/combine visuals",
                    "trusted FreeW compare/combine revision evidence is present; run a Word-baseline comparison for " + scenarioId + " to prove Word visual parity",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
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
                    ? "MS Word baseline PNG generation was unavailable for " + scenarioId
                    : string.Join("; ", reasons);
                blockers.Add(BuildReviewCompareCombineVisualBlocker(
                    scenarioId,
                    FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
                    "real MS Word PNG comparisons for review compare/combine visuals",
                    reason,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
            }

            if (related.Any(comparison =>
                string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus, StringComparison.OrdinalIgnoreCase)))
            {
                blockers.Add(BuildReviewCompareCombineVisualBlocker(
                    scenarioId,
                    FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus,
                    "real MS Word PNG comparisons for review compare/combine visuals",
                    "trusted compare/combine revision evidence is present, but mapped Word baseline PNGs are missing for " + scenarioId,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
            }

            if (related.All(comparison =>
                string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.PassedStatus, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            blockers.Add(BuildReviewCompareCombineVisualBlocker(
                scenarioId,
                "word-baseline-comparison-not-passed",
                "passing real MS Word PNG comparisons for review compare/combine visuals",
                "trusted compare/combine evidence is present, but at least one mapped Word baseline comparison did not pass for " + scenarioId,
                statuses,
                candidates,
                semanticEvidence,
                requiresWordBaseline: false));
        }

        return blockers;
    }

    private static FreeWVisualRemainingEvidenceBlocker BuildReviewCompareCombineVisualBlocker(
        string scenarioId,
        string status,
        string requiredEvidence,
        string reason,
        IReadOnlyList<string> relatedBaselineStatuses,
        IReadOnlyList<string> candidateBaselinePaths,
        IReadOnlyList<string> semanticEvidence,
        bool requiresWordBaseline) =>
        new(
            scenarioId + "-word-baseline-fidelity",
            scenarioId,
            "Review compare/combine visual fidelity",
            status,
            requiredEvidence,
            reason,
            relatedBaselineStatuses,
            candidateBaselinePaths,
            semanticEvidence,
            requiresWordBaseline,
            new FreeWVisualEvidenceTrust(true, []));

    private static IReadOnlyList<string> BuildReviewCompareCombineSemanticEvidence(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows) =>
        rows
            .Where(row => row.ReviewCompareCombine.RevisionCount > 0)
            .Select(row => string.Concat(
                row.HostId,
                "/p",
                row.PageNumber.ToString(CultureInfo.InvariantCulture),
                ": operation=",
                row.ReviewCompareCombine.Operation,
                "; revisions=",
                row.ReviewCompareCombine.RevisionCount.ToString(CultureInfo.InvariantCulture),
                "; authors=",
                FormatSummaries(row.ReviewCompareCombine.Authors),
                "; retained=",
                BoolFlag(row.ReviewCompareCombine.HasRetainedModelSafety),
                "; signatures=",
                FormatSummaries(row.ReviewCompareCombine.StableSignatures)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<FreeWVisualRemainingEvidenceBlocker> BuildReviewProofingWordBaselineBlockers(
        FreeWVisualEvidenceNormalizedSummary summary,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var blockers = new List<FreeWVisualRemainingEvidenceBlocker>();
        foreach (var scenarioId in ReviewProofingVisualProofScenarioIds)
        {
            var rows = summary.Evidence
                .Where(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .Where(row => row.Trust.Passed)
                .ToList();
            if (rows.Count == 0)
                continue;

            var semanticEvidence = BuildReviewProofingSemanticEvidence(rows);
            if (semanticEvidence.Count == 0)
            {
                blockers.Add(BuildReviewProofingVisualBlocker(
                    scenarioId,
                    "semantic-proofing-adornments-missing",
                    "trusted WPF and Avalonia proofing visual adornment metadata",
                    "trusted proofing evidence did not record stable squiggle adornment metadata; regenerate current-schema evidence or fix shared proofing adornment planning before treating this as a Word-baseline-only gap",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: false));
                continue;
            }

            var related = baselineComparisons
                .Where(comparison => string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (related.Count == 0)
            {
                blockers.Add(BuildReviewProofingVisualBlocker(
                    scenarioId,
                    "needs-word-baseline-run",
                    "real MS Word PNG comparisons for review proofing visual adornments",
                    "trusted FreeW proofing adornment evidence is present; run a Word-baseline comparison for " + scenarioId + " to prove Word visual parity",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
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
                    ? "MS Word baseline PNG generation was unavailable for " + scenarioId
                    : string.Join("; ", reasons);
                blockers.Add(BuildReviewProofingVisualBlocker(
                    scenarioId,
                    FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
                    "real MS Word PNG comparisons for review proofing visual adornments",
                    reason,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
            }

            if (related.Any(comparison =>
                string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus, StringComparison.OrdinalIgnoreCase)))
            {
                blockers.Add(BuildReviewProofingVisualBlocker(
                    scenarioId,
                    FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus,
                    "real MS Word PNG comparisons for review proofing visual adornments",
                    "trusted proofing adornment evidence is present, but mapped Word baseline PNGs are missing for " + scenarioId,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
            }

            if (related.All(comparison =>
                string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.PassedStatus, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            blockers.Add(BuildReviewProofingVisualBlocker(
                scenarioId,
                "needs-render-review",
                "render-review resolution for failed review proofing Word PNG comparisons",
                scenarioId + " Word baseline comparison did not fully pass; inspect proofing adornment rendering differences",
                statuses,
                candidates,
                semanticEvidence,
                requiresWordBaseline: false));
        }

        return blockers;
    }

    private static FreeWVisualRemainingEvidenceBlocker BuildReviewProofingVisualBlocker(
        string scenarioId,
        string status,
        string requiredEvidence,
        string reason,
        IReadOnlyList<string> relatedBaselineStatuses,
        IReadOnlyList<string> candidateBaselinePaths,
        IReadOnlyList<string> semanticEvidence,
        bool requiresWordBaseline) =>
        new(
            scenarioId + "-word-baseline-fidelity",
            scenarioId,
            "Review proofing visual adornment fidelity",
            status,
            requiredEvidence,
            reason,
            relatedBaselineStatuses,
            candidateBaselinePaths,
            semanticEvidence,
            requiresWordBaseline,
            new FreeWVisualEvidenceTrust(true, []));

    private static IReadOnlyList<string> BuildReviewProofingSemanticEvidence(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows) =>
        rows
            .Where(row => row.ProofingDiagnostics.AdornmentCount > 0)
            .Select(row => string.Concat(
                row.HostId,
                "/p",
                row.PageNumber.ToString(CultureInfo.InvariantCulture),
                ": diagnostics=",
                row.ProofingDiagnostics.DiagnosticCount.ToString(CultureInfo.InvariantCulture),
                "; adornments=",
                row.ProofingDiagnostics.AdornmentCount.ToString(CultureInfo.InvariantCulture),
                "; signatures=",
                FormatSummaries(row.ProofingDiagnostics.AdornmentStableSignatures)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

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

            if (missing.All(IsBackstageWpfSoftwareFallbackReadinessRow))
            {
                blockers.Add(new FreeWVisualRemainingEvidenceBlocker(
                    $"backstage-runner-evidence-hygiene-{scenarioGroup.Key}",
                    scenarioGroup.Key,
                    "Backstage print/export visual evidence runner",
                    "runner-evidence-hygiene",
                    $"real WPF composite capture rows for {scenarioGroup.Key} on a capture-capable runner before treating the WPF side as renderer parity evidence",
                    $"{scenarioLabel} has paired renderer contracts and trusted Avalonia rows, but the no-Word runner retained WPF software fallback evidence with wpfRenderTargetBitmap=unavailable: {missingSummary}. This is runner evidence hygiene, not a FreeW Word parity gap or an MS Word PNG parity claim.",
                    [],
                    [],
                    semanticEvidence,
                    false,
                    new FreeWVisualEvidenceTrust(true, [])));
                continue;
            }

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

    private static bool IsBackstageWpfSoftwareFallbackReadinessRow(FreeWVisualEvidenceBackstagePrintReadiness row) =>
        string.Equals(row.HostId, WpfHostId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(row.Status, "fallback", StringComparison.OrdinalIgnoreCase);

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

    private static IReadOnlyList<FreeWVisualRemainingEvidenceBlocker> BuildTablePaginationWordBaselineBlockers(
        FreeWVisualEvidenceNormalizedSummary summary,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var blockers = new List<FreeWVisualRemainingEvidenceBlocker>();
        foreach (var scenarioId in TablePaginationVisualProofScenarioIds)
        {
            var rows = summary.Evidence
                .Where(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .Where(row => row.Trust.Passed)
                .ToList();
            if (rows.Count == 0)
                continue;

            var semanticEvidence = BuildTablePaginationSemanticEvidence(rows);
            if (semanticEvidence.Count == 0)
            {
                blockers.Add(BuildTablePaginationBlocker(
                    scenarioId,
                    "semantic-table-pagination-missing",
                    "trusted WPF and Avalonia table pagination metadata",
                    "trusted table evidence did not record repeated-header pagination metadata; regenerate current-schema evidence or fix shared table pagination planning before treating this as a Word-baseline-only gap",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: false));
                continue;
            }

            var related = baselineComparisons
                .Where(comparison => string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (related.Count == 0)
            {
                blockers.Add(BuildTablePaginationBlocker(
                    scenarioId,
                    "needs-word-baseline-run",
                    "real MS Word PNG comparisons for table pagination and page composition",
                    "trusted FreeW table pagination evidence is present; run a Word-baseline comparison for " + scenarioId + " to prove Word visual parity",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
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
                    ? "MS Word baseline PNG generation was unavailable for " + scenarioId
                    : string.Join("; ", reasons);
                blockers.Add(BuildTablePaginationBlocker(
                    scenarioId,
                    FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
                    "real MS Word PNG comparisons for table pagination and page composition",
                    reason,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
            }

            if (related.Any(comparison =>
                string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus, StringComparison.OrdinalIgnoreCase)))
            {
                blockers.Add(BuildTablePaginationBlocker(
                    scenarioId,
                    FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus,
                    "real MS Word PNG comparisons for table pagination and page composition",
                    "trusted table pagination evidence is present, but mapped Word baseline PNGs are missing for " + scenarioId,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
            }

            if (related.All(comparison =>
                string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.PassedStatus, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            blockers.Add(BuildTablePaginationBlocker(
                scenarioId,
                "needs-render-review",
                "render-review resolution for failed table pagination Word PNG comparisons",
                scenarioId + " Word baseline comparison did not fully pass; inspect table pagination and page-composition rendering differences",
                statuses,
                candidates,
                semanticEvidence,
                requiresWordBaseline: false));
        }

        return blockers;
    }

    private static FreeWVisualRemainingEvidenceBlocker BuildTablePaginationBlocker(
        string scenarioId,
        string status,
        string requiredEvidence,
        string reason,
        IReadOnlyList<string> relatedBaselineStatuses,
        IReadOnlyList<string> candidateBaselinePaths,
        IReadOnlyList<string> semanticEvidence,
        bool requiresWordBaseline) =>
        new(
            scenarioId + "-word-baseline-fidelity",
            scenarioId,
            "Table pagination/page composition fidelity",
            status,
            requiredEvidence,
            reason,
            relatedBaselineStatuses,
            candidateBaselinePaths,
            semanticEvidence,
            requiresWordBaseline,
            new FreeWVisualEvidenceTrust(true, []));

    private static IReadOnlyList<string> BuildTablePaginationSemanticEvidence(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows) =>
        rows
            .Where(row => row.Tables.HasRepeatedHeaderPages)
            .Select(row => string.Concat(
                row.HostId,
                "/p",
                row.PageNumber.ToString(CultureInfo.InvariantCulture),
                "/",
                row.Tables.TotalRows.ToString(CultureInfo.InvariantCulture),
                "r",
                row.Tables.TotalCells.ToString(CultureInfo.InvariantCulture),
                "c",
                ": estimatedPages=",
                row.Tables.EstimatedPageCount.ToString(CultureInfo.InvariantCulture),
                "; repeatedHeaderPages=",
                row.Tables.PaginationPlans
                    .SelectMany(plan => plan.Pages)
                    .Count(page => page.IncludesRepeatedHeader)
                    .ToString(CultureInfo.InvariantCulture),
                "; keepRows=",
                BoolFlag(row.Tables.HasKeepTogetherRows),
                "; tableSig=",
                BuildTablePaginationTableFingerprint(row.Tables),
                "; paginationSig=",
                BuildTablePaginationPlanFingerprint(row.Tables),
                "; pageFeatures=",
                FormatTablePaginationBlockerPageFeatures(row)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<FreeWVisualRemainingEvidenceBlocker> BuildWordArtWatermarkWordBaselineBlockers(
        FreeWVisualEvidenceNormalizedSummary summary,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        var blockers = new List<FreeWVisualRemainingEvidenceBlocker>();
        foreach (var scenarioId in WordArtWatermarkVisualProofScenarioIds)
        {
            var rows = summary.Evidence
                .Where(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .Where(row => row.Trust.Passed)
                .ToList();
            if (rows.Count == 0)
                continue;

            var semanticEvidence = BuildWordArtWatermarkSemanticEvidence(rows);
            if (semanticEvidence.Count == 0)
            {
                blockers.Add(BuildWordArtWatermarkBlocker(
                    scenarioId,
                    "semantic-wordart-watermark-evidence-missing",
                    "trusted WordArt/watermark rows with WordArt, watermark, and page-surface metadata",
                    "trusted WordArt/watermark evidence did not record semantic WordArt/watermark metadata; regenerate current-schema evidence before treating this as a Word-baseline-only gap",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: false));
                continue;
            }

            var related = baselineComparisons
                .Where(comparison => string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (related.Count == 0)
            {
                blockers.Add(BuildWordArtWatermarkBlocker(
                    scenarioId,
                    "needs-word-baseline-run",
                    "real MS Word PNG comparisons for WordArt/watermark visual fidelity",
                    "paired WPF/Avalonia WordArt/watermark evidence is present; run a Word-baseline comparison for " + scenarioId + " to prove Word visual parity",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
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
                    ? "MS Word baseline PNG generation was unavailable for " + scenarioId
                    : string.Join("; ", reasons);
                blockers.Add(BuildWordArtWatermarkBlocker(
                    scenarioId,
                    FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
                    "real MS Word PNG comparisons for WordArt/watermark visual fidelity",
                    reason,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
            }

            if (related.Any(comparison =>
                string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus, StringComparison.OrdinalIgnoreCase)))
            {
                blockers.Add(BuildWordArtWatermarkBlocker(
                    scenarioId,
                    FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus,
                    "real MS Word PNG comparisons for WordArt/watermark visual fidelity",
                    "trusted WordArt/watermark evidence is present, but mapped Word baseline PNGs are missing for " + scenarioId,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true));
                continue;
            }

            if (related.All(comparison =>
                string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.PassedStatus, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            blockers.Add(BuildWordArtWatermarkBlocker(
                scenarioId,
                "needs-render-review",
                "render-review resolution for failed WordArt/watermark Word PNG comparisons",
                scenarioId + " Word baseline comparison did not fully pass; inspect WordArt/watermark rendering differences",
                statuses,
                candidates,
                semanticEvidence,
                requiresWordBaseline: false));
        }

        return blockers;
    }

    private static FreeWVisualRemainingEvidenceBlocker BuildWordArtWatermarkBlocker(
        string scenarioId,
        string status,
        string requiredEvidence,
        string reason,
        IReadOnlyList<string> relatedBaselineStatuses,
        IReadOnlyList<string> candidateBaselinePaths,
        IReadOnlyList<string> semanticEvidence,
        bool requiresWordBaseline) =>
        new(
            scenarioId + "-word-baseline-fidelity",
            scenarioId,
            "WordArt/watermark visual fidelity",
            status,
            requiredEvidence,
            reason,
            relatedBaselineStatuses,
            candidateBaselinePaths,
            semanticEvidence,
            requiresWordBaseline,
            new FreeWVisualEvidenceTrust(true, []));

    private static IReadOnlyList<string> BuildWordArtWatermarkSemanticEvidence(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows) =>
        rows
            .Where(row => row.DrawingObjects.HasWordArt && row.PageFeatures.Watermark.Present)
            .Select(row => string.Concat(
                row.HostId,
                "/p",
                row.PageNumber.ToString(CultureInfo.InvariantCulture),
                ": ",
                row.DrawingObjects.Objects.Count.ToString(CultureInfo.InvariantCulture),
                " object(s), wordart=",
                BoolFlag(row.DrawingObjects.HasWordArt),
                ", watermark=",
                row.PageFeatures.Watermark.IsPicture ? "picture" : "text",
                ", pageBorder=",
                BoolFlag(row.PageFeatures.PageBorder.Present),
                ", effects=",
                row.DrawingObjects.Effects.EffectObjectCount.ToString(CultureInfo.InvariantCulture)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string FormatTablePaginationBlockerPageFeatures(FreeWVisualEvidenceNormalizedRow row)
    {
        var parts = new List<string>();
        if (row.PageFeatures.PageBorder.Present)
            parts.Add("page-border");
        if (row.PageFeatures.Watermark.Present)
            parts.Add("watermark");
        if (row.HeaderFooters.SlotCount > 0)
            parts.Add("header-footer");
        if (row.Fields.HasPageFields)
            parts.Add("PAGE");
        if (row.Fields.HasNumPagesFields)
            parts.Add("NUMPAGES");
        return parts.Count == 0 ? "-" : FormatSummaries(parts);
    }

    private static IReadOnlyList<FreeWVisualRemainingEvidenceBlocker> BuildLegalReferenceWordBaselineBlockers(
        FreeWVisualEvidenceNormalizedSummary summary,
        IReadOnlyList<FreeWVisualBaselineComparison> baselineComparisons)
    {
        const string scenarioId = LegalReferenceSectionPageProofScenarioId;
        var rows = summary.Evidence
            .Where(row => string.Equals(row.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
            .Where(row => row.Trust.Passed)
            .ToList();
        if (rows.Count == 0)
            return [];

        var semanticEvidence = BuildLegalReferenceSemanticEvidence(rows);
        if (semanticEvidence.Count == 0)
        {
            return
            [
                BuildLegalReferenceBlocker(
                    "semantic-legal-reference-page-numbers-missing",
                    "trusted WPF and Avalonia section-formatted Table of Authorities page-number metadata",
                    "trusted legal-reference evidence did not record section-formatted TOA page references; regenerate current-schema evidence or fix shared TOA generation before treating this as a Word-baseline-only gap",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: false)
            ];
        }

        var related = baselineComparisons
            .Where(comparison => string.Equals(comparison.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (related.Count == 0)
        {
            return
            [
                BuildLegalReferenceBlocker(
                    "needs-word-baseline-run",
                    "real MS Word PNG comparisons for section-formatted Table of Authorities page numbers",
                    "trusted FreeW legal-reference page-number evidence is present; run a Word-baseline comparison for legal-reference-section-page-numbers to prove Word visual parity",
                    [],
                    [],
                    semanticEvidence,
                    requiresWordBaseline: true)
            ];
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
                ? "MS Word baseline PNG generation was unavailable for legal-reference-section-page-numbers"
                : string.Join("; ", reasons);
            return
            [
                BuildLegalReferenceBlocker(
                    FreeWVisualBaselineComparisonPlanner.WordBaselineUnavailableStatus,
                    "real MS Word PNG comparisons for section-formatted Table of Authorities page numbers",
                    reason,
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true)
            ];
        }

        if (related.Any(comparison =>
            string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return
            [
                BuildLegalReferenceBlocker(
                    FreeWVisualBaselineComparisonPlanner.MissingBaselineStatus,
                    "real MS Word PNG comparisons for section-formatted Table of Authorities page numbers",
                    "semantic section-formatted TOA page references are present in trusted FreeW evidence, but legal-reference Word baseline PNGs are missing for page-number comparison",
                    statuses,
                    candidates,
                    semanticEvidence,
                    requiresWordBaseline: true)
            ];
        }

        if (related.All(comparison =>
            string.Equals(comparison.Status, FreeWVisualBaselineComparisonPlanner.PassedStatus, StringComparison.OrdinalIgnoreCase)))
        {
            return [];
        }

        return
        [
            BuildLegalReferenceBlocker(
                "needs-render-review",
                "render-review resolution for failed legal-reference Word PNG comparisons",
                "legal-reference Word baseline comparison did not fully pass; inspect section-formatted TOA page-number rendering differences",
                statuses,
                candidates,
                semanticEvidence,
                requiresWordBaseline: false)
        ];
    }

    private static FreeWVisualRemainingEvidenceBlocker BuildLegalReferenceBlocker(
        string status,
        string requiredEvidence,
        string reason,
        IReadOnlyList<string> relatedBaselineStatuses,
        IReadOnlyList<string> candidateBaselinePaths,
        IReadOnlyList<string> semanticEvidence,
        bool requiresWordBaseline) =>
        new(
            "legal-reference-section-page-number-fidelity",
            LegalReferenceSectionPageProofScenarioId,
            "Section-formatted TOA page-number fidelity",
            status,
            requiredEvidence,
            reason,
            relatedBaselineStatuses,
            candidateBaselinePaths,
            semanticEvidence,
            requiresWordBaseline,
            new FreeWVisualEvidenceTrust(true, []));

    private static IReadOnlyList<string> BuildLegalReferenceSemanticEvidence(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows) =>
        rows
            .Where(row => HasLegalReferenceToaPageReferenceSignatures(row.TableOfAuthorities))
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

    private static bool HasLegalReferenceToaPageReferenceSignatures(
        FreeWVisualTableOfAuthoritiesExpectation tableOfAuthorities) =>
        LegalReferenceRequiredToaPageReferenceSignatures.All(signature =>
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
        List<string> summaryFailures,
        bool allowNoWordFallbackEvidence)
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
        ValidateBackstageCaptureSource(normalizedRow, rowFailures, allowNoWordFallbackEvidence);

        var outputPath = VisualEvidencePathPolicy.ResolveDeclaredPath(manifestDirectory, row.OutputPath);
        var relativeOutputPath = VisualEvidencePathPolicy.NormalizeRelativePath(runRoot, outputPath);
        if (!VisualEvidencePathPolicy.IsContained(
                runRoot,
                outputPath,
                StringComparison.OrdinalIgnoreCase))
            rowFailures.Add($"output path '{relativeOutputPath}' is outside the run root");
        if (!string.Equals(Path.GetFileName(outputPath), row.OutputName, StringComparison.OrdinalIgnoreCase))
            rowFailures.Add($"output file name '{Path.GetFileName(outputPath)}' does not match manifest output name '{row.OutputName}'");

        var fileLength = 0L;
        var sha256 = string.Empty;
        if (File.Exists(outputPath))
        {
            var file = new FileInfo(outputPath);
            fileLength = file.Length;
            sha256 = VisualEvidenceHash.Sha256File(outputPath);
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
            VisualEvidenceNormalization.OrderMetadata(
                row.HostMetadata,
                StringComparer.OrdinalIgnoreCase),
            pageExpectation.PageNumber,
            pageExpectation.PageCount,
            pageExpectation.LayoutKind,
            pageExpectation.ExpectedOutputName,
            pageExpectation.Features,
            pageExpectation.Tables,
            pageExpectation.DrawingObjects,
            pageExpectation.ChartSmartArt,
            pageExpectation.Fields,
            pageExpectation.Equations,
            pageExpectation.HeaderFooters ?? HeaderFooterVisualPlanner.EmptyExpectation,
            pageExpectation.TableOfAuthorities,
            pageExpectation.ProofingDiagnostics,
            pageExpectation.ReviewProtection,
            pageExpectation.ReviewMarkup,
            pageExpectation.ReviewCompareCombine,
            pageExpectation.HasFootnotes,
            pageExpectation.HasEndnotes,
            pageExpectation.IsSyntheticPage,
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
        ValidateLegalReferenceTableOfAuthoritiesEvidence(row, rowFailures);
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
        ValidateReviewMarkupFeatureTags(row, rowFailures);
        ValidateReviewCompareCombineFeatureTags(row, rowFailures);
        if (row.ExpectedFeatureTags.Contains("equations", StringComparer.OrdinalIgnoreCase)
            && row.PageExpectation.Equations.EquationCount <= 0)
        {
            rowFailures.Add("scenario expects equation structures but no equation geometry evidence was recorded");
        }
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

    private static void ValidateLegalReferenceTableOfAuthoritiesEvidence(
        FreeWVisualEvidenceRow row,
        List<string> rowFailures)
    {
        if (!string.Equals(row.ScenarioId, "legal-reference-section-page-numbers", StringComparison.OrdinalIgnoreCase))
            return;

        var fields = row.PageExpectation.Fields;
        var toa = row.PageExpectation.TableOfAuthorities;

        if (!fields.ComplexFieldKeywords.Contains("TOA", StringComparer.OrdinalIgnoreCase))
        {
            rowFailures.Add(
                "legal-reference section page-number evidence must include cached TOA field metadata");
        }

        if (!fields.ComplexFieldResultSignatures.Contains("TOA=Cases\\ti, 1", StringComparer.OrdinalIgnoreCase))
        {
            rowFailures.Add(
                "legal-reference field evidence must include cached TOA displayed page-reference sentinel 'TOA=Cases\\ti, 1'");
        }

        if (!toa.HasGeneratedTable || toa.EntryCount < 2)
        {
            rowFailures.Add(
                "legal-reference TOA evidence must include shared generated Table of Authorities entries");
        }

        if (toa.EntryWithPageReferenceCount < 2)
        {
            rowFailures.Add(
                "legal-reference TOA evidence must include generated page references for both authority entries");
        }

        foreach (var signature in LegalReferenceRequiredToaPageReferenceSignatures)
        {
            if (!toa.PageReferenceSignatures.Contains(signature, StringComparer.Ordinal))
            {
                rowFailures.Add(
                    $"legal-reference TOA evidence is missing generated page-reference signature '{signature}'");
            }
        }

        var sectionFormatted = toa.PageReferences
            .Where(reference => string.Equals(
                reference.PageReferenceKind,
                "section-formatted-page-numbers",
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sectionFormatted.Count == 0)
        {
            rowFailures.Add(
                "legal-reference TOA evidence must include a section-formatted page-reference row");
        }

        foreach (var reference in sectionFormatted)
        {
            if (!reference.PageNumbers.Contains(1) || !reference.PageNumbers.Contains(2))
            {
                rowFailures.Add(
                    "legal-reference section-formatted TOA evidence must preserve physical pages 1 and 2");
            }

            if (!reference.DisplayedPageReferences.Contains("i", StringComparer.OrdinalIgnoreCase)
                || !reference.DisplayedPageReferences.Contains("1", StringComparer.OrdinalIgnoreCase))
            {
                rowFailures.Add(
                    "legal-reference section-formatted TOA evidence must preserve displayed page references 'i' and '1'");
            }
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
        if (tags.Contains("chart-data", StringComparer.OrdinalIgnoreCase)
            && (chartSmartArt.ChartDataSignatures is null || chartSmartArt.ChartDataSignatures.Count == 0))
        {
            rowFailures.Add("chart/SmartArt evidence expects chart data signatures but the chart plan records none");
        }
        if (tags.Contains("smartart-layout", StringComparer.OrdinalIgnoreCase) && !chartSmartArt.HasSmartArtLayout)
            rowFailures.Add("chart/SmartArt evidence expects SmartArt layout metadata but the SmartArt plan records none");
        if (tags.Contains("smartart-colors", StringComparer.OrdinalIgnoreCase) && !chartSmartArt.HasSmartArtColorScheme)
            rowFailures.Add("chart/SmartArt evidence expects SmartArt color scheme metadata but the SmartArt plan records none");
        if (tags.Contains("smartart-style", StringComparer.OrdinalIgnoreCase) && !chartSmartArt.HasSmartArtStyle)
            rowFailures.Add("chart/SmartArt evidence expects SmartArt style metadata but the SmartArt plan records none");
        if (tags.Contains("smartart-node-fills", StringComparer.OrdinalIgnoreCase) && chartSmartArt.DistinctSmartArtFillCount <= 1)
            rowFailures.Add("chart/SmartArt evidence expects distinct SmartArt node fills but the SmartArt plan records one or fewer");
        if (tags.Contains("smartart-polygon-geometry", StringComparer.OrdinalIgnoreCase)
            && !HasSmartArtPolygonGeometry(chartSmartArt))
        {
            rowFailures.Add("chart/SmartArt evidence expects SmartArt polygon layout geometry but the SmartArt plan records none");
        }
        if (tags.Contains("smartart-visual-signature", StringComparer.OrdinalIgnoreCase)
            && (chartSmartArt.SmartArtVisualSignatures is null || chartSmartArt.SmartArtVisualSignatures.Count == 0))
        {
            rowFailures.Add("chart/SmartArt evidence expects SmartArt visual signatures but the SmartArt plan records none");
        }
        if (chartSmartArt.SmartArtCount > 0 && chartSmartArt.SmartArtNodeCount <= 0)
            rowFailures.Add("chart/SmartArt evidence includes SmartArt but records no nodes");
        ValidateChartSmartArtVisualSignatures(chartSmartArt, rowFailures);
    }

    private static bool HasSmartArtPolygonGeometry(FreeWVisualChartSmartArtExpectation chartSmartArt) =>
        chartSmartArt.SmartArts.Any(plan =>
            plan.LayoutGeometry is { Kind: SmartArtLayoutGeometryKind.Pyramid } geometry
            && geometry.Nodes.Any(node => node.HasPolygon));

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

        var expectedChartDataSignatures = ChartSmartArtVisualPlanner.BuildChartDataSignatures(chartSmartArt.Charts ?? []);
        var actualChartDataSignatures = OrderedSummaries(chartSmartArt.ChartDataSignatures ?? []);
        if (!expectedChartDataSignatures.SequenceEqual(actualChartDataSignatures, StringComparer.Ordinal))
        {
            rowFailures.Add(
                $"chart data signatures do not match chart plans: expected '{FormatSummaries(expectedChartDataSignatures)}', actual '{FormatSummaries(actualChartDataSignatures)}'");
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
        if (tags.Contains("resolved-header-footer-field-text", StringComparer.OrdinalIgnoreCase))
        {
            var signatures = OrderedSummaries(fields.HeaderFooterResolvedFieldSignatures ?? []);
            if (signatures.Count == 0)
            {
                rowFailures.Add("field evidence expects resolved header/footer field text but records no resolved field signatures");
            }
            if (tags.Contains("page-number-fields", StringComparer.OrdinalIgnoreCase)
                && !signatures.Any(signature => signature.Contains("field=PAGE", StringComparison.Ordinal)))
            {
                rowFailures.Add("field evidence expects resolved PAGE header/footer field text but records none");
            }
            if (tags.Contains("numpages-fields", StringComparer.OrdinalIgnoreCase)
                && !signatures.Any(signature => signature.Contains("field=NUMPAGES", StringComparison.Ordinal)))
            {
                rowFailures.Add("field evidence expects resolved NUMPAGES header/footer field text but records none");
            }
        }
        if (tags.Contains("chapter-prefixed-page-number-fields", StringComparer.OrdinalIgnoreCase)
            && !(fields.HeaderFooterResolvedFieldSignatures ?? []).Any(signature =>
                signature.Contains("field=PAGE", StringComparison.Ordinal)
                && TryGetResolvedFieldSignatureText(signature, out var text)
                && text.Contains('-', StringComparison.Ordinal)))
        {
            rowFailures.Add("field evidence expects chapter-prefixed PAGE display text but records none");
        }
    }

    private static bool TryGetResolvedFieldSignatureText(
        string? signature,
        out string text)
    {
        text = string.Empty;
        if (string.IsNullOrWhiteSpace(signature))
            return false;

        const string TextPrefix = "text=";
        var parts = signature.Split('|', StringSplitOptions.None);
        foreach (var part in parts)
        {
            if (!part.StartsWith(TextPrefix, StringComparison.Ordinal))
                continue;

            text = part[TextPrefix.Length..];
            return text.Length > 0;
        }

        return false;
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
        if (!tags.Contains("proofing-adornments", StringComparer.OrdinalIgnoreCase)
            && !tags.Contains("proofing-underline-intent", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        if (proofing.AdornmentCount <= 0)
            rowFailures.Add("scenario expects proofing visual adornment evidence but the page expectation records none");
        if (proofing.AdornmentCount != proofing.DiagnosticCount)
            rowFailures.Add("proofing visual adornment count must match the proofing diagnostic count");
        if (proofing.SpellingAdornmentCount != proofing.SpellingCount)
            rowFailures.Add("spelling visual adornment count must match the spelling diagnostic count");
        if (proofing.GrammarAdornmentCount != proofing.GrammarCount)
            rowFailures.Add("grammar visual adornment count must match the grammar diagnostic count");
        if (!proofing.HasSpellingUnderline)
            rowFailures.Add("scenario expects spelling underline visual evidence but the page expectation records none");
        if (!proofing.HasGrammarUnderline)
            rowFailures.Add("scenario expects grammar underline visual evidence but the page expectation records none");
        if (proofing.AdornmentStableSignatures.Count != proofing.AdornmentCount)
            rowFailures.Add("proofing visual adornment signatures must cover every adornment");
        if (proofing.Adornments.Any(adornment =>
                !string.Equals(adornment.UnderlineStyle, "wavy", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(adornment.ColorHex)
                || adornment.Length <= 0
                || adornment.ParagraphEndOffset <= adornment.ParagraphStartOffset))
        {
            rowFailures.Add("proofing visual adornments must include stable wavy underline color and range evidence");
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
            if (!protection.RestrictEditing.IsChecked)
                rowFailures.Add("scenario expects Restrict Editing checked but the page expectation records it unchecked");
        }

        if (tags.Contains("marked-as-final", StringComparer.OrdinalIgnoreCase)
            || tags.Contains("final-advisory-read-only", StringComparer.OrdinalIgnoreCase))
        {
            if (!protection.IsMarkedAsFinal)
                rowFailures.Add("scenario expects Mark as Final but the page expectation records it disabled");
            if (!protection.MarkAsFinal.IsChecked)
                rowFailures.Add("scenario expects Mark as Final checked but the page expectation records it unchecked");
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
        if (tags.Contains("comment-workflow-blocked", StringComparer.OrdinalIgnoreCase))
        {
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.CommentInsert), "None", isAllowed: false, blockReason: nameof(RestrictEditingBlockReason.MarkedAsFinal));
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.CommentReply), "None", isAllowed: false, blockReason: nameof(RestrictEditingBlockReason.MarkedAsFinal));
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.CommentResolve), "None", isAllowed: false, blockReason: nameof(RestrictEditingBlockReason.MarkedAsFinal));
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.CommentDelete), "None", isAllowed: false, blockReason: nameof(RestrictEditingBlockReason.MarkedAsFinal));
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.HistoryUndo), nameof(DocumentCommandMutationKind.Comment), isAllowed: false, blockReason: nameof(RestrictEditingBlockReason.MarkedAsFinal));
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.HistoryRedo), nameof(DocumentCommandMutationKind.Comment), isAllowed: false, blockReason: nameof(RestrictEditingBlockReason.MarkedAsFinal));
        }

        if (tags.Contains("final-advisory-read-only", StringComparer.OrdinalIgnoreCase))
        {
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.BodyTextEdit), "None", isAllowed: false, blockReason: nameof(RestrictEditingBlockReason.MarkedAsFinal));
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.BodyFormatting), "None", isAllowed: false, blockReason: nameof(RestrictEditingBlockReason.MarkedAsFinal));
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.ProofingReplacement), "None", isAllowed: false, blockReason: nameof(RestrictEditingBlockReason.MarkedAsFinal));
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.HistoryUndo), nameof(DocumentCommandMutationKind.BodyText), isAllowed: false, blockReason: nameof(RestrictEditingBlockReason.MarkedAsFinal));
            RequireProtectionDecision(rowFailures, protection, nameof(RestrictEditingOperationKind.HistoryRedo), nameof(DocumentCommandMutationKind.BodyFormatting), isAllowed: false, blockReason: nameof(RestrictEditingBlockReason.MarkedAsFinal));
        }
    }

    private static void ValidateReviewCompareCombineFeatureTags(
        FreeWVisualEvidenceRow row,
        List<string> rowFailures)
    {
        var tags = row.ExpectedFeatureTags;
        if (!tags.Contains("compare-semantics", StringComparer.OrdinalIgnoreCase)
            && !tags.Contains("combine-semantics", StringComparer.OrdinalIgnoreCase)
            && !tags.Contains("multi-author-revisions", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var expectation = row.PageExpectation.ReviewCompareCombine;
        if (tags.Contains("compare-semantics", StringComparer.OrdinalIgnoreCase))
        {
            if (!expectation.HasCompareSemantics)
                rowFailures.Add("scenario expects compare semantic evidence but the page expectation records none");
            if (!string.Equals(expectation.Operation, "compare", StringComparison.Ordinal))
                rowFailures.Add("scenario expects compare operation evidence but the page expectation records a different operation");
        }

        if (tags.Contains("combine-semantics", StringComparer.OrdinalIgnoreCase))
        {
            if (!expectation.HasCombineSemantics)
                rowFailures.Add("scenario expects combine semantic evidence but the page expectation records none");
            if (!string.Equals(expectation.Operation, "combine", StringComparison.Ordinal))
                rowFailures.Add("scenario expects combine operation evidence but the page expectation records a different operation");
        }

        if (expectation.RevisionCount <= 0)
            rowFailures.Add("scenario expects compare/combine revision entries but the page expectation records none");
        if (expectation.InsertionCount <= 0)
            rowFailures.Add("scenario expects compare/combine insertion entries but the page expectation records none");
        if (expectation.DeletionCount <= 0)
            rowFailures.Add("scenario expects compare/combine deletion entries but the page expectation records none");
        if (tags.Contains("multi-author-revisions", StringComparer.OrdinalIgnoreCase)
            && expectation.AuthorCount < 2)
        {
            rowFailures.Add("scenario expects multi-author combine revisions but the page expectation records fewer than two authors");
        }
        if (tags.Contains("compare-authorship", StringComparer.OrdinalIgnoreCase)
            && expectation.AuthorCount <= 0)
        {
            rowFailures.Add("scenario expects compare/combine authorship evidence but the page expectation records none");
        }
        if (expectation.StableSignatures.Count != expectation.RevisionCount)
            rowFailures.Add("compare/combine revision signatures must cover every review entry");
    }

    private static void ValidateReviewMarkupFeatureTags(
        FreeWVisualEvidenceRow row,
        List<string> rowFailures)
    {
        var tags = row.ExpectedFeatureTags;
        var markup = row.PageExpectation.ReviewMarkup;
        if (tags.Contains("tracked-changes", StringComparer.OrdinalIgnoreCase)
            || tags.Contains("revision-marks", StringComparer.OrdinalIgnoreCase))
        {
            if (markup.RevisionCount <= 0)
                rowFailures.Add("scenario expects tracked-change evidence but the page expectation records none");
            if (markup.InsertionCount <= 0)
                rowFailures.Add("scenario expects tracked insertion evidence but the page expectation records none");
            if (markup.DeletionCount <= 0)
                rowFailures.Add("scenario expects tracked deletion evidence but the page expectation records none");
            if (markup.RevisionStableSignatures.Count < markup.RevisionCount)
                rowFailures.Add("tracked-change revision signatures must cover every review entry");
        }

        if (tags.Contains("comments", StringComparer.OrdinalIgnoreCase)
            || tags.Contains("comment-anchors", StringComparer.OrdinalIgnoreCase)
            || tags.Contains("comment-replies", StringComparer.OrdinalIgnoreCase)
            || tags.Contains("resolved-comments", StringComparer.OrdinalIgnoreCase)
            || tags.Contains("table-comment-anchors", StringComparer.OrdinalIgnoreCase))
        {
            if (markup.CommentCount <= 0)
                rowFailures.Add("scenario expects comment evidence but the page expectation records none");
            if (markup.CommentAnchorCount <= 0)
                rowFailures.Add("scenario expects comment anchor evidence but the page expectation records none");
            if (markup.CommentReferenceCount <= 0)
                rowFailures.Add("scenario expects comment reference evidence but the page expectation records none");
            if (markup.CommentStableSignatures.Count < markup.CommentCount)
                rowFailures.Add("comment signatures must cover every top-level comment");
        }
    }

    private static void RequireProtectionDecision(
        List<string> rowFailures,
        FreeWVisualReviewProtectionExpectation protection,
        string operation,
        string mutationKind,
        bool isAllowed,
        string? blockReason = null)
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

        if (blockReason is not null
            && !string.Equals(decision.BlockReason, blockReason, StringComparison.Ordinal))
        {
            rowFailures.Add(
                $"scenario expects protection decision {operation}/{mutationKind} blockReason={blockReason} but the page expectation records blockReason={decision.BlockReason}");
        }
    }

    private static void ValidateBackstageCaptureSource(
        FreeWVisualEvidenceRow row,
        List<string> rowFailures,
        bool allowNoWordFallbackEvidence)
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

        var isAllowedNoWordFallback = allowNoWordFallbackEvidence
            && IsBackstageWpfSoftwareRendererFallback(row);
        if (isAllowedNoWordFallback)
            ValidateBackstageWpfSoftwareRendererFallback(row, rowFailures);

        if (!string.Equals(captureSource, expectedCaptureSource, StringComparison.OrdinalIgnoreCase)
            && !isAllowedNoWordFallback)
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

    private static bool IsDefaultNoWordFallbackOptionalScenario(FreeWVisualEvidenceExpectedScenario scenario) =>
        string.Equals(scenario.HostId, WpfHostId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(scenario.ScenarioId, FloatingWrappingWpfScenarioId, StringComparison.OrdinalIgnoreCase);

    private static bool IsBackstageWpfSoftwareRendererFallback(FreeWVisualEvidenceRow row) =>
        BackstageRendererScenarioIds.Contains(row.ScenarioId, StringComparer.OrdinalIgnoreCase)
        && string.Equals(row.HostId, WpfHostId, StringComparison.OrdinalIgnoreCase)
        && row.HostMetadata.TryGetValue("captureSource", out var captureSource)
        && string.Equals(captureSource, "software-renderer", StringComparison.OrdinalIgnoreCase);

    private static bool IsBackstageWpfSoftwareRendererFallback(FreeWVisualEvidenceNormalizedRow row) =>
        BackstageRendererScenarioIds.Contains(row.ScenarioId, StringComparer.OrdinalIgnoreCase)
        && string.Equals(row.HostId, WpfHostId, StringComparison.OrdinalIgnoreCase)
        && row.HostMetadata.TryGetValue("captureSource", out var captureSource)
        && string.Equals(captureSource, "software-renderer", StringComparison.OrdinalIgnoreCase);

    private static void ValidateBackstageWpfSoftwareRendererFallback(
        FreeWVisualEvidenceRow row,
        List<string> rowFailures)
    {
        if (!row.HostMetadata.TryGetValue("wpfRenderTargetBitmap", out var renderTargetStatus)
            || !string.Equals(renderTargetStatus, "unavailable", StringComparison.OrdinalIgnoreCase))
        {
            rowFailures.Add(
                "backstage WPF software fallback evidence must declare wpfRenderTargetBitmap 'unavailable'");
        }

        if (!row.HostMetadata.TryGetValue("wpfRenderTargetBitmapReason", out var reason)
            || string.IsNullOrWhiteSpace(reason))
        {
            rowFailures.Add(
                "backstage WPF software fallback evidence must declare wpfRenderTargetBitmapReason");
        }
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
                ValidateReviewMarkupPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
                ValidateReviewProtectionPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
                ValidateReviewCompareCombinePairRow(scenarioId, pageNumber, wpf, avalonia, failures);
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
                if (IsTableOfAuthoritiesFieldScenario(scenarioId))
                    ValidateToaFieldPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
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
                ValidateEquationPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
            }
        }
    }

    private static void ValidateNoteRendererPairs(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows,
        List<string> failures)
    {
        foreach (var scenarioId in NoteRendererScenarioIds)
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
                    $"note renderer pair '{scenarioId}' is missing Avalonia page(s): {FormatPages(missingAvaloniaPages)}");
            }

            if (missingWpfPages.Count > 0)
            {
                failures.Add(
                    $"note renderer pair '{scenarioId}' is missing WPF page(s): {FormatPages(missingWpfPages)}");
            }

            foreach (var pageNumber in wpfPages.Intersect(avaloniaPages))
            {
                var wpf = wpfRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                var avalonia = avaloniaRows.SingleOrDefault(r => r.PageNumber == pageNumber);
                if (wpf is null || avalonia is null)
                    continue;

                ValidateRendererPairRow("note renderer pair", scenarioId, pageNumber, wpf, avalonia, failures);
                ValidateNotePlacementPairRow(scenarioId, pageNumber, wpf, avalonia, failures);
            }
        }
    }

    private static void ValidateEquationPairRow(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia,
        List<string> failures)
    {
        var pairName = $"equation renderer pair '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        var wpfEquations = wpf.Equations ?? FreeWVisualEquationExpectation.Empty;
        var avaloniaEquations = avalonia.Equations ?? FreeWVisualEquationExpectation.Empty;
        if (wpfEquations.EquationCount <= 0)
            failures.Add($"{pairName} is missing WPF equation geometry evidence");
        if (avaloniaEquations.EquationCount <= 0)
            failures.Add($"{pairName} is missing Avalonia equation geometry evidence");

        if (wpfEquations.EquationCount != avaloniaEquations.EquationCount)
        {
            failures.Add(
                $"{pairName} equation counts differ: WPF {wpfEquations.EquationCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaEquations.EquationCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfEquations.ElementCount != avaloniaEquations.ElementCount)
        {
            failures.Add(
                $"{pairName} equation element counts differ: WPF {wpfEquations.ElementCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaEquations.ElementCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfEquations.SegmentCount != avaloniaEquations.SegmentCount)
        {
            failures.Add(
                $"{pairName} equation segment counts differ: WPF {wpfEquations.SegmentCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaEquations.SegmentCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfEquations.NestedSlotCount != avaloniaEquations.NestedSlotCount)
        {
            failures.Add(
                $"{pairName} nested slot counts differ: WPF {wpfEquations.NestedSlotCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaEquations.NestedSlotCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfEquations.MaxNestedSlotDepth != avaloniaEquations.MaxNestedSlotDepth)
        {
            failures.Add(
                $"{pairName} max nested slot depths differ: WPF {wpfEquations.MaxNestedSlotDepth.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaEquations.MaxNestedSlotDepth.ToString(CultureInfo.InvariantCulture)}");
        }

        ValidateEquationSignatureList(pairName, "element kind counts", wpfEquations.ElementKindCounts, avaloniaEquations.ElementKindCounts, failures);
        ValidateEquationSignatureList(pairName, "segment role counts", wpfEquations.SegmentRoleCounts, avaloniaEquations.SegmentRoleCounts, failures);
        ValidateEquationSignatureList(pairName, "baseline role counts", wpfEquations.BaselineRoleCounts, avaloniaEquations.BaselineRoleCounts, failures);
        ValidateEquationSignatureList(pairName, "segment geometry signatures", wpfEquations.SegmentGeometrySignatures, avaloniaEquations.SegmentGeometrySignatures, failures);
        ValidateEquationSignatureList(pairName, "element geometry signatures", wpfEquations.ElementGeometrySignatures, avaloniaEquations.ElementGeometrySignatures, failures);
        ValidateEquationSignatureList(pairName, "spacing geometry signatures", wpfEquations.SpacingGeometrySignatures, avaloniaEquations.SpacingGeometrySignatures, failures);
        ValidateEquationSignatureList(pairName, "slot geometry signatures", wpfEquations.SlotGeometrySignatures, avaloniaEquations.SlotGeometrySignatures, failures);
    }

    private static void ValidateEquationSignatureList(
        string pairName,
        string label,
        IReadOnlyList<string> wpfSignatures,
        IReadOnlyList<string> avaloniaSignatures,
        List<string> failures)
    {
        var wpf = OrderedSummaries(wpfSignatures ?? []);
        var avalonia = OrderedSummaries(avaloniaSignatures ?? []);
        if (!wpf.SequenceEqual(avalonia, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} {label} differ: WPF '{FormatSummaries(wpf)}', Avalonia '{FormatSummaries(avalonia)}'");
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

        var wpfDrawingSignatures = BuildOriginNormalizedFloatingObjectSignatures(wpfObjects.Objects);
        var avaloniaDrawingSignatures = BuildOriginNormalizedFloatingObjectSignatures(avaloniaObjects.Objects);
        if (!wpfDrawingSignatures.SequenceEqual(avaloniaDrawingSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} origin-normalized floating object signatures differ: WPF '{FormatSummaries(wpfDrawingSignatures)}', Avalonia '{FormatSummaries(avaloniaDrawingSignatures)}'");
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

        var wpfGroupChildVisualSignatures = BuildProofComparableGroupChildVisualSignatures(wpfGroupChildren.ChildVisualSignatures ?? []);
        var avaloniaGroupChildVisualSignatures = BuildProofComparableGroupChildVisualSignatures(avaloniaGroupChildren.ChildVisualSignatures ?? []);
        if (!wpfGroupChildVisualSignatures.SequenceEqual(avaloniaGroupChildVisualSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} proof-comparable grouped child visual signatures differ: WPF '{FormatSummaries(wpfGroupChildVisualSignatures)}', Avalonia '{FormatSummaries(avaloniaGroupChildVisualSignatures)}'");
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

        var wpfResolvedFieldSignatures = OrderedSummaries(wpfFields.HeaderFooterResolvedFieldSignatures ?? []);
        var avaloniaResolvedFieldSignatures = OrderedSummaries(avaloniaFields.HeaderFooterResolvedFieldSignatures ?? []);
        if (!wpfResolvedFieldSignatures.SequenceEqual(avaloniaResolvedFieldSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} resolved header/footer field signatures differ: WPF '{FormatSummaries(wpfResolvedFieldSignatures)}', Avalonia '{FormatSummaries(avaloniaResolvedFieldSignatures)}'");
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

    private static void ValidateNotePlacementPairRow(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia,
        List<string> failures)
    {
        var pairName = $"note renderer pair '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        if (wpf.IsSyntheticPage != avalonia.IsSyntheticPage)
        {
            failures.Add(
                $"{pairName} synthetic page flags differ: WPF {BoolFlag(wpf.IsSyntheticPage)}, Avalonia {BoolFlag(avalonia.IsSyntheticPage)}");
        }
    }

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

        if (wpfProofing.AdornmentCount != avaloniaProofing.AdornmentCount)
        {
            failures.Add(
                $"{pairName} proofing visual adornment counts differ: WPF {wpfProofing.AdornmentCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaProofing.AdornmentCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfProofing.SpellingAdornmentCount != avaloniaProofing.SpellingAdornmentCount)
        {
            failures.Add(
                $"{pairName} spelling visual adornment counts differ: WPF {wpfProofing.SpellingAdornmentCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaProofing.SpellingAdornmentCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (wpfProofing.GrammarAdornmentCount != avaloniaProofing.GrammarAdornmentCount)
        {
            failures.Add(
                $"{pairName} grammar visual adornment counts differ: WPF {wpfProofing.GrammarAdornmentCount.ToString(CultureInfo.InvariantCulture)}, Avalonia {avaloniaProofing.GrammarAdornmentCount.ToString(CultureInfo.InvariantCulture)}");
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

        var wpfAdornmentSignatures = OrderedSummaries(wpfProofing.AdornmentStableSignatures);
        var avaloniaAdornmentSignatures = OrderedSummaries(avaloniaProofing.AdornmentStableSignatures);
        if (!wpfAdornmentSignatures.SequenceEqual(avaloniaAdornmentSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} proofing visual adornment signatures differ: WPF '{FormatSummaries(wpfAdornmentSignatures)}', Avalonia '{FormatSummaries(avaloniaAdornmentSignatures)}'");
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

    private static void ValidateReviewMarkupPairRow(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia,
        List<string> failures)
    {
        if (!ReviewMarkupVisualProofScenarioIds.Contains(scenarioId, StringComparer.OrdinalIgnoreCase)
            && !ReviewProofingVisualProofScenarioIds.Contains(scenarioId, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var pairName = $"review renderer pair '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        var wpfMarkup = wpf.ReviewMarkup;
        var avaloniaMarkup = avalonia.ReviewMarkup;
        if (wpfMarkup.RevisionCount != avaloniaMarkup.RevisionCount
            || wpfMarkup.InsertionCount != avaloniaMarkup.InsertionCount
            || wpfMarkup.DeletionCount != avaloniaMarkup.DeletionCount
            || wpfMarkup.FormattingRevisionCount != avaloniaMarkup.FormattingRevisionCount)
        {
            failures.Add(
                $"{pairName} review revision counts differ: WPF {FormatReviewMarkupCounts(wpfMarkup)}, Avalonia {FormatReviewMarkupCounts(avaloniaMarkup)}");
        }

        if (wpfMarkup.CommentCount != avaloniaMarkup.CommentCount
            || wpfMarkup.ReplyCount != avaloniaMarkup.ReplyCount
            || wpfMarkup.ResolvedCommentCount != avaloniaMarkup.ResolvedCommentCount
            || wpfMarkup.CommentAnchorCount != avaloniaMarkup.CommentAnchorCount
            || wpfMarkup.CommentReferenceCount != avaloniaMarkup.CommentReferenceCount)
        {
            failures.Add(
                $"{pairName} comment counts differ: WPF {FormatReviewMarkupCounts(wpfMarkup)}, Avalonia {FormatReviewMarkupCounts(avaloniaMarkup)}");
        }

        if (!wpfMarkup.Authors.SequenceEqual(avaloniaMarkup.Authors, StringComparer.Ordinal))
            failures.Add($"{pairName} review authors differ: WPF {FormatSummaries(wpfMarkup.Authors)}, Avalonia {FormatSummaries(avaloniaMarkup.Authors)}");
        if (!wpfMarkup.RevisionStableSignatures.SequenceEqual(avaloniaMarkup.RevisionStableSignatures, StringComparer.Ordinal))
            failures.Add($"{pairName} tracked-change signatures differ");
        if (!wpfMarkup.CommentStableSignatures.SequenceEqual(avaloniaMarkup.CommentStableSignatures, StringComparer.Ordinal))
            failures.Add($"{pairName} comment signatures differ");
    }

    private static void ValidateReviewCompareCombinePairRow(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia,
        List<string> failures)
    {
        if (!ReviewCompareCombineVisualProofScenarioIds.Contains(scenarioId, StringComparer.OrdinalIgnoreCase))
            return;

        var pairName = $"review renderer pair '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
        var wpfExpectation = wpf.ReviewCompareCombine;
        var avaloniaExpectation = avalonia.ReviewCompareCombine;

        if (!string.Equals(wpfExpectation.Operation, avaloniaExpectation.Operation, StringComparison.Ordinal))
        {
            failures.Add(
                $"{pairName} compare/combine operations differ: WPF '{wpfExpectation.Operation}', Avalonia '{avaloniaExpectation.Operation}'");
        }

        if (wpfExpectation.RevisionCount != avaloniaExpectation.RevisionCount
            || wpfExpectation.InsertionCount != avaloniaExpectation.InsertionCount
            || wpfExpectation.DeletionCount != avaloniaExpectation.DeletionCount
            || wpfExpectation.FormattingCount != avaloniaExpectation.FormattingCount
            || wpfExpectation.PreservedPartCount != avaloniaExpectation.PreservedPartCount
            || wpfExpectation.PreservedContentTypeDefaultCount != avaloniaExpectation.PreservedContentTypeDefaultCount)
        {
            failures.Add(
                $"{pairName} compare/combine revision counts differ: WPF {FormatReviewCompareCombineCounts(wpfExpectation)}, Avalonia {FormatReviewCompareCombineCounts(avaloniaExpectation)}");
        }

        if (wpfExpectation.HasPreservedSettings != avaloniaExpectation.HasPreservedSettings
            || wpfExpectation.HasPreservedCustomProperties != avaloniaExpectation.HasPreservedCustomProperties
            || wpfExpectation.HasRetainedModelSafety != avaloniaExpectation.HasRetainedModelSafety)
        {
            failures.Add(
                $"{pairName} compare/combine retained model flags differ: WPF settings={wpfExpectation.HasPreservedSettings} customProperties={wpfExpectation.HasPreservedCustomProperties} retained={wpfExpectation.HasRetainedModelSafety}, Avalonia settings={avaloniaExpectation.HasPreservedSettings} customProperties={avaloniaExpectation.HasPreservedCustomProperties} retained={avaloniaExpectation.HasRetainedModelSafety}");
        }

        var wpfAuthors = OrderedSummaries(wpfExpectation.Authors);
        var avaloniaAuthors = OrderedSummaries(avaloniaExpectation.Authors);
        if (!wpfAuthors.SequenceEqual(avaloniaAuthors, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} compare/combine authors differ: WPF '{FormatSummaries(wpfAuthors)}', Avalonia '{FormatSummaries(avaloniaAuthors)}'");
        }

        var wpfSignatures = OrderedSummaries(wpfExpectation.StableSignatures);
        var avaloniaSignatures = OrderedSummaries(avaloniaExpectation.StableSignatures);
        if (!wpfSignatures.SequenceEqual(avaloniaSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} compare/combine signatures differ: WPF '{FormatSummaries(wpfSignatures)}', Avalonia '{FormatSummaries(avaloniaSignatures)}'");
        }

        var wpfRetainedSignatures = OrderedSummaries(wpfExpectation.RetainedModelSafetySignatures);
        var avaloniaRetainedSignatures = OrderedSummaries(avaloniaExpectation.RetainedModelSafetySignatures);
        if (!wpfRetainedSignatures.SequenceEqual(avaloniaRetainedSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} compare/combine retained model safety signatures differ: WPF '{FormatSummaries(wpfRetainedSignatures)}', Avalonia '{FormatSummaries(avaloniaRetainedSignatures)}'");
        }
    }

    private static string FormatReviewMarkupCounts(FreeWVisualReviewMarkupExpectation expectation) =>
        string.Join(
            "/",
            expectation.RevisionCount.ToString(CultureInfo.InvariantCulture),
            expectation.InsertionCount.ToString(CultureInfo.InvariantCulture),
            expectation.DeletionCount.ToString(CultureInfo.InvariantCulture),
            expectation.FormattingRevisionCount.ToString(CultureInfo.InvariantCulture),
            expectation.CommentCount.ToString(CultureInfo.InvariantCulture),
            expectation.ReplyCount.ToString(CultureInfo.InvariantCulture),
            expectation.ResolvedCommentCount.ToString(CultureInfo.InvariantCulture),
            expectation.CommentAnchorCount.ToString(CultureInfo.InvariantCulture),
            expectation.CommentReferenceCount.ToString(CultureInfo.InvariantCulture));

    private static bool IsReviewProofingEvidenceScenario(string scenarioId) =>
        string.Equals(scenarioId, "review-proofing-visual-depth", StringComparison.OrdinalIgnoreCase)
        || string.Equals(scenarioId, "review-protection-proofing-comments-only", StringComparison.OrdinalIgnoreCase);

    private static bool IsTableOfAuthoritiesFieldScenario(string scenarioId) =>
        string.Equals(scenarioId, "references-heavy-fields", StringComparison.OrdinalIgnoreCase)
        || string.Equals(scenarioId, "legal-reference-section-page-numbers", StringComparison.OrdinalIgnoreCase);

    private static void ValidateToaFieldPairRow(
        string scenarioId,
        int pageNumber,
        FreeWVisualEvidenceNormalizedRow wpf,
        FreeWVisualEvidenceNormalizedRow avalonia,
        List<string> failures)
    {
        var pairName = $"field renderer pair '{scenarioId}' page {pageNumber.ToString(CultureInfo.InvariantCulture)}";
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

        var wpfDrawingSignatures = BuildOriginNormalizedFloatingObjectSignatures(wpf.DrawingObjects.Objects);
        var avaloniaDrawingSignatures = BuildOriginNormalizedFloatingObjectSignatures(avalonia.DrawingObjects.Objects);
        if (!wpfDrawingSignatures.SequenceEqual(avaloniaDrawingSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} origin-normalized floating object signatures differ: WPF '{FormatSummaries(wpfDrawingSignatures)}', Avalonia '{FormatSummaries(avaloniaDrawingSignatures)}'");
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

        var wpfChartDataSignatures = OrderedSummaries(wpfPlan.ChartDataSignatures ?? []);
        var avaloniaChartDataSignatures = OrderedSummaries(avaloniaPlan.ChartDataSignatures ?? []);
        if (!wpfChartDataSignatures.SequenceEqual(avaloniaChartDataSignatures, StringComparer.Ordinal))
        {
            failures.Add(
                $"{pairName} chart data signatures differ: WPF '{FormatSummaries(wpfChartDataSignatures)}', Avalonia '{FormatSummaries(avaloniaChartDataSignatures)}'");
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

    private static string BuildTablePaginationTableFingerprint(FreeWVisualTableExpectation tables) =>
        BuildStableEvidenceFingerprint(BuildTablePlanSignatures(tables.Tables));

    private static string BuildTablePaginationPlanFingerprint(FreeWVisualTableExpectation tables) =>
        BuildStableEvidenceFingerprint(BuildTablePaginationSignatures(tables.PaginationPlans));

    private static string BuildStableEvidenceFingerprint(IEnumerable<string> signatures)
    {
        var value = string.Join("\n", signatures.OrderBy(signature => signature, StringComparer.Ordinal));
        if (string.IsNullOrEmpty(value))
            return "-";

        return VisualEvidenceHash.Sha256Text(value)[..12];
    }

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

    private static List<string> BuildOriginNormalizedFloatingObjectSignatures(IEnumerable<DocumentFloatingObjectSnapshot> objects)
    {
        var snapshots = objects.ToList();
        // WPF and Avalonia evidence can report the same page content from different renderer origins.
        // Preserve relative X geometry so per-object horizontal drift still fails.
        var originXDip = snapshots.Count == 0 ? 0 : snapshots.Min(o => o.Rect.XDip);
        return snapshots
            .Select(o => string.Join(
                "|",
                o.TypeTag,
                o.BlockIndex.ToString(CultureInfo.InvariantCulture),
                o.RunIndex.ToString(CultureInfo.InvariantCulture),
                "xRel=" + FormatDouble(o.Rect.XDip - originXDip),
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
    }

    private static List<string> BuildProofComparableGroupChildVisualSignatures(IEnumerable<string> signatures) =>
        signatures
            .Where(signature => !string.IsNullOrWhiteSpace(signature))
            .Select(NormalizeProofComparableGroupChildVisualSignature)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();

    private static string NormalizeProofComparableGroupChildVisualSignature(string signature)
    {
        var firstSeparator = signature.IndexOf(':');
        if (firstSeparator < 0)
            return signature;

        var secondSeparator = signature.IndexOf(':', firstSeparator + 1);
        if (secondSeparator < 0)
            return signature;

        var prefix = signature[..secondSeparator];
        var details = signature[(secondSeparator + 1)..];
        if (prefix.EndsWith(":Image", StringComparison.Ordinal))
            return NormalizeImageGroupChildSignature(prefix, details);
        if (prefix.EndsWith(":Shape", StringComparison.Ordinal) ||
            prefix.EndsWith(":WordArt", StringComparison.Ordinal))
            return signature;
        if (prefix.EndsWith(":Chart", StringComparison.Ordinal))
            return prefix + ":" + JoinSignatureParts(details, "kind", "geometry", "gridlines", "markers");
        if (prefix.EndsWith(":SmartArt", StringComparison.Ordinal))
            // Grouped SmartArt child internals differ by renderer capture path; standalone SmartArt proof rows keep full signatures.
            return prefix;

        return signature;
    }

    private static string NormalizeImageGroupChildSignature(string prefix, string details)
    {
        var parts = details
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !part.StartsWith("bytes=", StringComparison.Ordinal))
            .ToList();
        return prefix + ":" + string.Join(";", parts);
    }

    private static string JoinSignatureParts(string details, params string[] keys)
    {
        var parts = details
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => keys.Any(key => part.StartsWith(key + "=", StringComparison.Ordinal)))
            .ToList();
        return parts.Count == 0 ? details : string.Join("|", parts);
    }

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
        if (row.Equations.EquationCount > 0)
        {
            parts.Add(
                $"{row.Equations.EquationCount.ToString(CultureInfo.InvariantCulture)} equation(s), " +
                $"{row.Equations.ElementCount.ToString(CultureInfo.InvariantCulture)} equation element(s), " +
                $"{row.Equations.NestedSlotCount.ToString(CultureInfo.InvariantCulture)} nested slot(s), " +
                $"max depth {row.Equations.MaxNestedSlotDepth.ToString(CultureInfo.InvariantCulture)}");
        }
        if (row.HeaderFooters.ImageCount > 0)
        {
            parts.Add(
                $"{row.HeaderFooters.ImageCount.ToString(CultureInfo.InvariantCulture)} header/footer image(s), " +
                $"{row.HeaderFooters.SlotCount.ToString(CultureInfo.InvariantCulture)} slot(s)");
        }
        if (row.HasFootnotes)
            parts.Add("footnote placement");
        if (row.HasEndnotes)
            parts.Add(row.IsSyntheticPage ? "synthetic endnote page" : "endnote placement");
        if (row.ProofingDiagnostics.DiagnosticCount > 0)
        {
            parts.Add(
                $"{row.ProofingDiagnostics.DiagnosticCount.ToString(CultureInfo.InvariantCulture)} proofing diagnostic(s), " +
                $"{row.ProofingDiagnostics.SpellingCount.ToString(CultureInfo.InvariantCulture)} spelling, " +
                $"{row.ProofingDiagnostics.GrammarCount.ToString(CultureInfo.InvariantCulture)} grammar");
            if (row.ProofingDiagnostics.AdornmentCount > 0)
            {
                parts.Add(
                    $"{row.ProofingDiagnostics.AdornmentCount.ToString(CultureInfo.InvariantCulture)} proofing visual adornment(s): " +
                    string.Join("/", row.ProofingDiagnostics.Adornments
                        .Select(adornment => $"{adornment.AdornmentKind} {adornment.UnderlineStyle} {adornment.ColorHex}")
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(summary => summary, StringComparer.Ordinal)));
            }
        }
        if (row.ReviewProtection.IsProtected)
        {
            parts.Add(
                $"protection {row.ReviewProtection.ProtectionMode}, " +
                $"{row.ReviewProtection.Operations.Count.ToString(CultureInfo.InvariantCulture)} command decision(s)");
        }
        if (row.ReviewProtection.IsMarkedAsFinal || row.ReviewProtection.MarkAsFinal.IsChecked)
        {
            parts.Add("Mark as Final checked");
        }
        if (row.ReviewMarkup.RevisionCount > 0 || row.ReviewMarkup.CommentCount > 0)
        {
            parts.Add(
                $"{row.ReviewMarkup.RevisionCount.ToString(CultureInfo.InvariantCulture)} tracked revision(s), " +
                $"{row.ReviewMarkup.CommentCount.ToString(CultureInfo.InvariantCulture)} comment thread(s), " +
                $"{row.ReviewMarkup.ReplyCount.ToString(CultureInfo.InvariantCulture)} repl(y/ies)");
        }
        if (row.ReviewCompareCombine.RevisionCount > 0)
        {
            parts.Add(
                $"{row.ReviewCompareCombine.Operation} " +
                $"{row.ReviewCompareCombine.RevisionCount.ToString(CultureInfo.InvariantCulture)} revision(s), " +
                $"{row.ReviewCompareCombine.AuthorCount.ToString(CultureInfo.InvariantCulture)} author(s)");
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

    private static JsonSerializerOptions JsonOptions { get; } =
        VisualEvidenceManifestIO.CreateJsonOptions(
            propertyNameCaseInsensitive: true,
            stringEnums: false);
}
