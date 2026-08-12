using System.Globalization;

namespace FreeW.App.Presentation.DocumentView;

public sealed record FreeWWordBaselineFixture(
    string ScenarioId,
    string DocumentName,
    IReadOnlyList<string> ExpectedBaselinePaths);

public sealed record FreeWWordBaselineGenerationPlan(
    string CorpusDirectory,
    string BaselineDirectory,
    string WordApplicationProgId,
    int MaxPagesPerDocument,
    IReadOnlyList<FreeWWordBaselineFixture> Fixtures)
{
    public int ExpectedFixtureCount => Fixtures.Count;
    public int ExpectedBaselinePngCount => Fixtures.Sum(f => f.ExpectedBaselinePaths.Count);
}

public static class FreeWWordBaselineEvidencePlanner
{
    public const string DefaultWordApplicationProgId = "Word.Application";
    public const int DefaultMaxPagesPerDocument = 3;
    public const string BaselineScopeAll = "all";
    public const string BaselineScopeGeneratedCorpus = "generated-corpus";

    private static readonly string[] GeneratedCorpusScenarioIds =
    [
        "f2-hf-basic",
        "f2-hf-firstpage",
        "f2-hf-oddeven",
        "f2-hf-images",
        "field-page-number-variants",
        "references-heavy-fields",
        "legal-reference-section-page-numbers",
        "equation-structures",
        "f2-footnotes",
        "f2-endnotes",
        "f2-01-float-wrap",
        "f2-columns",
        "f2-border-watermark",
        "table-layout-complex",
        "table-pagination-repeat-header",
        "table-page-composition-stress",
        "drawing-objects-complex",
        "object-format-position-size-style",
        "wordart-watermark-stress",
        "chart-smartart-complex",
        "wordart-picture-watermark-layout",
        "f2-section-landscape",
        "f2-tracked-changes",
        "f2-comments",
        "review-proofing-visual-depth",
        "review-protection-proofing-comments-only",
        "review-compare-visual-proof",
        "review-combine-visual-proof",
        "backstage-print-preview-fidelity",
        "backstage-pdf-export-fidelity"
    ];

    public static IReadOnlyList<string> GeneratedCorpusScenarios { get; } = GeneratedCorpusScenarioIds;

    public static FreeWWordBaselineGenerationPlan BuildGenerationPlan(
        string corpusDirectory,
        string baselineDirectory,
        int maxPagesPerDocument = DefaultMaxPagesPerDocument,
        string? wordApplicationProgId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(corpusDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineDirectory);

        var maxPages = Math.Max(1, maxPagesPerDocument);
        var fixtures = GeneratedCorpusScenarioIds
            .Select(scenarioId => BuildFixture(scenarioId, maxPages))
            .ToList();

        return new FreeWWordBaselineGenerationPlan(
            Path.GetFullPath(corpusDirectory),
            Path.GetFullPath(baselineDirectory),
            string.IsNullOrWhiteSpace(wordApplicationProgId)
                ? DefaultWordApplicationProgId
                : wordApplicationProgId.Trim(),
            maxPages,
            fixtures);
    }

    public static bool ShouldCompareToWordBaseline(
        FreeWVisualEvidenceNormalizedRow row,
        string? baselineScope)
    {
        ArgumentNullException.ThrowIfNull(row);

        var scope = NormalizeBaselineScope(baselineScope);
        if (string.Equals(scope, BaselineScopeAll, StringComparison.OrdinalIgnoreCase))
            return true;

        var policy = FreeWVisualBaselineComparisonPlanner.ResolveWordBaselinePolicy(row);
        var scenarioId = policy.BaselineScenarioId ?? row.ScenarioId;
        return GeneratedCorpusScenarioIds.Contains(
            FreeWVisualEvidencePlanner.NormalizeScenarioId(scenarioId),
            StringComparer.OrdinalIgnoreCase);
    }

    public static string NormalizeBaselineScope(string? baselineScope)
    {
        if (string.IsNullOrWhiteSpace(baselineScope))
            return BaselineScopeAll;

        var value = baselineScope.Trim();
        if (string.Equals(value, BaselineScopeAll, StringComparison.OrdinalIgnoreCase))
            return BaselineScopeAll;
        if (string.Equals(value, BaselineScopeGeneratedCorpus, StringComparison.OrdinalIgnoreCase))
            return BaselineScopeGeneratedCorpus;

        throw new ArgumentException(
            $"Unknown Word baseline scope '{baselineScope}'. Known scopes: {BaselineScopeAll}, {BaselineScopeGeneratedCorpus}");
    }

    private static FreeWWordBaselineFixture BuildFixture(string scenarioId, int maxPages)
    {
        var paths = Enumerable.Range(1, maxPages)
            .Select(page => FreeWVisualBaselineComparisonPlanner.NormalizeBaselinePath(
                Path.Combine(
                    scenarioId,
                    FreeWVisualEvidencePlanner.ExpectedOutputName(scenarioId, page))))
            .ToList();

        return new FreeWWordBaselineFixture(
            scenarioId,
            scenarioId + ".docx",
            paths);
    }
}
