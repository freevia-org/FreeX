using System.Globalization;

namespace FreeW.App.Presentation.DocumentView;

public sealed record FreeWVisualBaselineComparisonTolerance(
    string Name,
    int ChangedPixelDeltaThreshold,
    double MaxMeanAbsoluteChannelDelta,
    double MaxMeanAbsoluteGrayscaleDelta,
    double MaxChangedPixelRatio,
    bool RequireDimensionMatch)
{
    public static FreeWVisualBaselineComparisonTolerance WordPngDefault { get; } = new(
        Name: "word-png-default",
        ChangedPixelDeltaThreshold: 8,
        MaxMeanAbsoluteChannelDelta: 3.0,
        MaxMeanAbsoluteGrayscaleDelta: 3.0,
        MaxChangedPixelRatio: 0.02,
        RequireDimensionMatch: true);

    public static FreeWVisualBaselineComparisonTolerance WordPngResizedLenient { get; } = new(
        Name: "word-png-resized-lenient",
        ChangedPixelDeltaThreshold: 16,
        MaxMeanAbsoluteChannelDelta: 8.0,
        MaxMeanAbsoluteGrayscaleDelta: 8.0,
        MaxChangedPixelRatio: 0.10,
        RequireDimensionMatch: false);

    public static IReadOnlyList<FreeWVisualBaselineComparisonTolerance> BuiltIn { get; } =
    [
        WordPngDefault,
        WordPngResizedLenient
    ];
}

public sealed record FreeWVisualBaselineComparisonMetrics(
    int ActualWidth,
    int ActualHeight,
    int BaselineWidth,
    int BaselineHeight,
    bool DimensionsMatch,
    bool BaselineResized,
    int ComparedWidth,
    int ComparedHeight,
    long ComparedPixels,
    long ChangedPixels,
    int ChangedPixelDeltaThreshold,
    double MeanAbsoluteChannelDelta,
    double MeanAbsoluteGrayscaleDelta,
    double ChangedPixelRatio);

public sealed record FreeWVisualBaselineComparison(
    string EvidenceId,
    string HostId,
    string ScenarioId,
    int PageNumber,
    string OutputName,
    string BaselineScenarioId,
    string MatchKey,
    string BaselinePath,
    IReadOnlyList<string> CandidateBaselinePaths,
    string Status,
    string SkipReason,
    FreeWVisualBaselineComparisonTolerance Tolerance,
    FreeWVisualBaselineComparisonMetrics? Metrics,
    FreeWVisualEvidenceTrust Trust)
{
    public string BaselineId => MatchKey;

    public string BaselineEvidenceClass =>
        FreeWVisualBaselineComparisonPlanner.ClassifyBaselineEvidence(this);

    public string BaselineEvidenceDescription =>
        FreeWVisualBaselineComparisonPlanner.DescribeBaselineEvidence(this);
}

public sealed record FreeWVisualWordBaselinePolicy(
    bool IsComparable,
    string? BaselineScenarioId,
    string SkipReason);

public static class FreeWVisualBaselineComparisonPlanner
{
    public const string MissingBaselineStatus = "missing-baseline";
    public const string DecodeFailedStatus = "decode-failed";
    public const string PassedStatus = "passed";
    public const string FailedStatus = "failed";
    public const string SkippedStatus = "skipped";
    public const string WordBaselineUnavailableStatus = "word-baseline-unavailable";
    public const string RealWordPngComparedClass = "real-word-png-compared";
    public const string RealWordPngComparisonFailedClass = "real-word-png-comparison-failed";
    public const string WordBaselineUnavailableClass = "word-baseline-unavailable";
    public const string WordPngBaselineMissingClass = "word-png-baseline-missing";
    public const string ScenarioSkippedOrUnmappedClass = "scenario-skipped-or-unmapped";
    public const string PngDecodeFailedClass = "png-decode-failed";
    public const string UnknownBaselineEvidenceClass = "unknown";

    private static readonly IReadOnlyDictionary<string, string> BaselineScenarioAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["page-composition-columns"] = "f2-columns",
            ["page-composition-border-watermark"] = "f2-border-watermark",
            ["page-composition-floating-image"] = "f2-01-float-wrap"
        };

    private static readonly IReadOnlySet<string> DirectWordBaselineScenarioIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "table-layout-complex",
            "table-pagination-repeat-header",
            "table-page-composition-stress",
            "drawing-objects-complex",
            "object-format-position-size-style",
            "chart-smartart-complex",
            "wordart-watermark-stress",
            "wordart-picture-watermark-layout",
            "field-page-number-variants",
            "references-heavy-fields",
            "legal-reference-section-page-numbers",
            "equation-structures",
            "f2-tracked-changes",
            "f2-comments",
            "f2-outofbody-comments",
            "review-proofing-visual-depth",
            "review-protection-proofing-comments-only",
            "review-compare-visual-proof",
            "review-combine-visual-proof",
            "backstage-print-preview-fidelity",
            "backstage-pdf-export-fidelity"
        };

    public static FreeWVisualBaselineComparisonTolerance ResolveTolerance(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return FreeWVisualBaselineComparisonTolerance.WordPngDefault;

        foreach (var tolerance in FreeWVisualBaselineComparisonTolerance.BuiltIn)
        {
            if (string.Equals(tolerance.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                return tolerance;
        }

        var known = string.Join(
            ", ",
            FreeWVisualBaselineComparisonTolerance.BuiltIn.Select(t => t.Name));
        throw new ArgumentException($"Unknown visual baseline tolerance '{name}'. Known tolerances: {known}");
    }

    public static string ClassifyBaselineEvidence(FreeWVisualBaselineComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        if (string.Equals(comparison.Status, PassedStatus, StringComparison.OrdinalIgnoreCase))
            return RealWordPngComparedClass;
        if (string.Equals(comparison.Status, FailedStatus, StringComparison.OrdinalIgnoreCase))
            return RealWordPngComparisonFailedClass;
        if (string.Equals(comparison.Status, WordBaselineUnavailableStatus, StringComparison.OrdinalIgnoreCase))
            return WordBaselineUnavailableClass;
        if (string.Equals(comparison.Status, MissingBaselineStatus, StringComparison.OrdinalIgnoreCase))
            return WordPngBaselineMissingClass;
        if (string.Equals(comparison.Status, SkippedStatus, StringComparison.OrdinalIgnoreCase))
            return ScenarioSkippedOrUnmappedClass;
        if (string.Equals(comparison.Status, DecodeFailedStatus, StringComparison.OrdinalIgnoreCase))
            return PngDecodeFailedClass;

        return UnknownBaselineEvidenceClass;
    }

    public static string DescribeBaselineEvidence(FreeWVisualBaselineComparison comparison) =>
        DescribeBaselineEvidenceClass(ClassifyBaselineEvidence(comparison));

    public static string DescribeBaselineEvidenceClass(string evidenceClass)
    {
        if (string.Equals(evidenceClass, RealWordPngComparedClass, StringComparison.OrdinalIgnoreCase))
            return "real Word PNG baseline available and compared within tolerance";
        if (string.Equals(evidenceClass, RealWordPngComparisonFailedClass, StringComparison.OrdinalIgnoreCase))
            return "real Word PNG baseline available and compared outside tolerance; metrics and tolerance failures are recorded";
        if (string.Equals(evidenceClass, WordBaselineUnavailableClass, StringComparison.OrdinalIgnoreCase))
            return "Word COM or baseline generation unavailable; no authoritative Word PNG parity claimed";
        if (string.Equals(evidenceClass, WordPngBaselineMissingClass, StringComparison.OrdinalIgnoreCase))
            return "mapped Word baseline PNG unavailable on disk; candidate paths are recorded";
        if (string.Equals(evidenceClass, ScenarioSkippedOrUnmappedClass, StringComparison.OrdinalIgnoreCase))
            return "scenario intentionally skipped, outside baseline scope, or unmapped";
        if (string.Equals(evidenceClass, PngDecodeFailedClass, StringComparison.OrdinalIgnoreCase))
            return "candidate Word PNG or evidence PNG could not be decoded before comparison";

        return "unrecognized Word baseline evidence status";
    }

    public static string NormalizeBaselinePath(string value) =>
        NormalizeManifestPath(value);

    public static string BuildBaselineMatchKey(FreeWVisualEvidenceNormalizedRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var policy = ResolveWordBaselinePolicy(row);
        var scenarioId = policy.BaselineScenarioId ?? row.ScenarioId;
        var outputName = ExpectedBaselineOutputName(row, scenarioId);
        return string.Join(
            '/',
            NormalizeCandidateSegment(scenarioId),
            "p" + Math.Max(1, row.PageNumber).ToString(CultureInfo.InvariantCulture),
            NormalizeCandidateSegment(outputName, keepExtension: true));
    }

    public static IReadOnlyList<string> BuildBaselineCandidateRelativePaths(FreeWVisualEvidenceNormalizedRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var policy = ResolveWordBaselinePolicy(row);
        if (!policy.IsComparable || string.IsNullOrWhiteSpace(policy.BaselineScenarioId))
            return [];

        var scenario = NormalizeCandidateSegment(policy.BaselineScenarioId);
        var outputName = NormalizeCandidateSegment(row.OutputName, keepExtension: true);
        var expectedOutputName = NormalizeCandidateSegment(
            ExpectedBaselineOutputName(row, policy.BaselineScenarioId),
            keepExtension: true);
        var candidates = new List<string>();

        AddCandidate(candidates, scenario, outputName);
        AddCandidate(candidates, string.Empty, outputName);
        if (!string.Equals(expectedOutputName, outputName, StringComparison.OrdinalIgnoreCase))
        {
            AddCandidate(candidates, scenario, expectedOutputName);
            AddCandidate(candidates, string.Empty, expectedOutputName);
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static FreeWVisualWordBaselinePolicy ResolveWordBaselinePolicy(FreeWVisualEvidenceNormalizedRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (BaselineScenarioAliases.TryGetValue(row.ScenarioId, out var baselineScenarioId))
        {
            return new FreeWVisualWordBaselinePolicy(
                IsComparable: true,
                BaselineScenarioId: baselineScenarioId,
                SkipReason: string.Empty);
        }

        if (row.ScenarioId.StartsWith("f2-", StringComparison.OrdinalIgnoreCase)
            || DirectWordBaselineScenarioIds.Contains(row.ScenarioId))
        {
            return new FreeWVisualWordBaselinePolicy(
                IsComparable: true,
                BaselineScenarioId: row.ScenarioId,
                SkipReason: string.Empty);
        }

        return new FreeWVisualWordBaselinePolicy(
            IsComparable: false,
            BaselineScenarioId: null,
            SkipReason: $"scenario '{row.ScenarioId}' has no direct MS Word PNG baseline mapping");
    }

    public static FreeWVisualBaselineComparison BuildMissingBaselineComparison(
        FreeWVisualEvidenceNormalizedRow row,
        FreeWVisualBaselineComparisonTolerance? tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(row);

        var candidatePaths = BuildBaselineCandidateRelativePaths(row);
        var matchKey = BuildBaselineMatchKey(row);
        var failure = "missing Word baseline PNG for match key '" + matchKey
            + "'; searched " + string.Join(", ", candidatePaths);
        return BuildFailure(
            row,
            baselinePath: string.Empty,
            candidatePaths,
            MissingBaselineStatus,
            tolerance ?? FreeWVisualBaselineComparisonTolerance.WordPngDefault,
            failure);
    }

    public static FreeWVisualBaselineComparison BuildWordBaselineUnavailableComparison(
        FreeWVisualEvidenceNormalizedRow row,
        FreeWVisualBaselineComparisonTolerance? tolerance = null,
        string? reasonOverride = null)
    {
        ArgumentNullException.ThrowIfNull(row);

        var policy = ResolveWordBaselinePolicy(row);
        var candidatePaths = BuildBaselineCandidateRelativePaths(row);
        var reason = string.IsNullOrWhiteSpace(reasonOverride)
            ? "MS Word baseline PNG generation was unavailable for this run"
            : reasonOverride.Trim();
        return new FreeWVisualBaselineComparison(
            row.EvidenceId,
            row.HostId,
            row.ScenarioId,
            Math.Max(1, row.PageNumber),
            row.OutputName,
            policy.BaselineScenarioId ?? row.ScenarioId,
            BuildBaselineMatchKey(row),
            string.Empty,
            candidatePaths,
            WordBaselineUnavailableStatus,
            reason,
            tolerance ?? FreeWVisualBaselineComparisonTolerance.WordPngDefault,
            Metrics: null,
            new FreeWVisualEvidenceTrust(true, []));
    }

    public static FreeWVisualBaselineComparison BuildSkippedBaselineComparison(
        FreeWVisualEvidenceNormalizedRow row,
        FreeWVisualBaselineComparisonTolerance? tolerance = null,
        string? reasonOverride = null)
    {
        ArgumentNullException.ThrowIfNull(row);

        var policy = ResolveWordBaselinePolicy(row);
        var reason = reasonOverride;
        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = string.IsNullOrWhiteSpace(policy.SkipReason)
                ? $"scenario '{row.ScenarioId}' was intentionally skipped for MS Word baseline comparison"
                : policy.SkipReason;
        }

        return new FreeWVisualBaselineComparison(
            row.EvidenceId,
            row.HostId,
            row.ScenarioId,
            Math.Max(1, row.PageNumber),
            row.OutputName,
            policy.BaselineScenarioId ?? row.ScenarioId,
            BuildBaselineMatchKey(row),
            string.Empty,
            BuildBaselineCandidateRelativePaths(row),
            SkippedStatus,
            reason,
            tolerance ?? FreeWVisualBaselineComparisonTolerance.WordPngDefault,
            Metrics: null,
            new FreeWVisualEvidenceTrust(true, []));
    }

    public static FreeWVisualBaselineComparison BuildDecodeFailure(
        FreeWVisualEvidenceNormalizedRow row,
        string baselinePath,
        IReadOnlyList<string> candidatePaths,
        FreeWVisualBaselineComparisonTolerance tolerance,
        string failure)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentException.ThrowIfNullOrWhiteSpace(failure);

        return BuildFailure(
            row,
            NormalizeManifestPath(baselinePath),
            candidatePaths,
            DecodeFailedStatus,
            tolerance,
            failure);
    }

    public static FreeWVisualBaselineComparison BuildBaselineComparison(
        FreeWVisualEvidenceNormalizedRow row,
        string baselinePath,
        IReadOnlyList<string> candidatePaths,
        FreeWVisualBaselineComparisonTolerance tolerance,
        ReadOnlySpan<byte> actualPixels,
        int actualWidth,
        int actualHeight,
        int actualStride,
        FreeWVisualEvidencePixelFormat actualFormat,
        ReadOnlySpan<byte> baselinePixels,
        int baselineWidth,
        int baselineHeight,
        int baselineStride,
        FreeWVisualEvidencePixelFormat baselineFormat,
        int? baselineSourceWidth = null,
        int? baselineSourceHeight = null,
        bool baselineResized = false)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(tolerance);

        var metrics = ComputeMetrics(
            actualPixels,
            actualWidth,
            actualHeight,
            actualStride,
            actualFormat,
            baselinePixels,
            baselineWidth,
            baselineHeight,
            baselineStride,
            baselineFormat,
            tolerance.ChangedPixelDeltaThreshold,
            baselineSourceWidth,
            baselineSourceHeight,
            baselineResized);
        var trust = EvaluateTolerance(metrics, tolerance);
        return new FreeWVisualBaselineComparison(
            row.EvidenceId,
            row.HostId,
            row.ScenarioId,
            row.PageNumber,
            row.OutputName,
            ResolveWordBaselinePolicy(row).BaselineScenarioId ?? row.ScenarioId,
            BuildBaselineMatchKey(row),
            NormalizeManifestPath(baselinePath),
            candidatePaths,
            trust.Passed ? PassedStatus : FailedStatus,
            string.Empty,
            tolerance,
            metrics,
            trust);
    }

    public static FreeWVisualBaselineComparisonMetrics ComputeMetrics(
        ReadOnlySpan<byte> actualPixels,
        int actualWidth,
        int actualHeight,
        int actualStride,
        FreeWVisualEvidencePixelFormat actualFormat,
        ReadOnlySpan<byte> baselinePixels,
        int baselineWidth,
        int baselineHeight,
        int baselineStride,
        FreeWVisualEvidencePixelFormat baselineFormat,
        int changedPixelDeltaThreshold,
        int? baselineSourceWidth = null,
        int? baselineSourceHeight = null,
        bool baselineResized = false)
    {
        var sourceWidth = Math.Max(0, baselineSourceWidth ?? baselineWidth);
        var sourceHeight = Math.Max(0, baselineSourceHeight ?? baselineHeight);
        var comparedWidth = Math.Min(Math.Max(0, actualWidth), Math.Max(0, baselineWidth));
        var comparedHeight = Math.Min(Math.Max(0, actualHeight), Math.Max(0, baselineHeight));
        var threshold = Math.Max(0, changedPixelDeltaThreshold);

        if (actualStride <= 0 || baselineStride <= 0 || actualPixels.IsEmpty || baselinePixels.IsEmpty)
        {
            return new FreeWVisualBaselineComparisonMetrics(
                Math.Max(0, actualWidth),
                Math.Max(0, actualHeight),
                sourceWidth,
                sourceHeight,
                DimensionsMatch(actualWidth, actualHeight, sourceWidth, sourceHeight),
                baselineResized,
                0,
                0,
                0,
                0,
                threshold,
                0,
                0,
                0);
        }

        long compared = 0;
        long changed = 0;
        double channelDelta = 0;
        double grayscaleDelta = 0;

        for (var y = 0; y < comparedHeight; y++)
        {
            var actualRow = y * actualStride;
            var baselineRow = y * baselineStride;
            if (actualRow < 0 || baselineRow < 0)
                break;

            for (var x = 0; x < comparedWidth; x++)
            {
                var actualOffset = actualRow + x * 4;
                var baselineOffset = baselineRow + x * 4;
                if (actualOffset + 3 >= actualPixels.Length || baselineOffset + 3 >= baselinePixels.Length)
                    break;

                var actual = ReadRgb(actualPixels, actualOffset, actualFormat);
                var baseline = ReadRgb(baselinePixels, baselineOffset, baselineFormat);
                var dr = Math.Abs(actual.R - baseline.R);
                var dg = Math.Abs(actual.G - baseline.G);
                var db = Math.Abs(actual.B - baseline.B);
                var maxDelta = Math.Max(dr, Math.Max(dg, db));

                channelDelta += dr + dg + db;
                grayscaleDelta += Math.Abs(ToGrayscale(actual) - ToGrayscale(baseline));
                compared++;
                if (maxDelta > threshold)
                    changed++;
            }
        }

        var denominator = Math.Max(1, compared);
        return new FreeWVisualBaselineComparisonMetrics(
            Math.Max(0, actualWidth),
            Math.Max(0, actualHeight),
            sourceWidth,
            sourceHeight,
            DimensionsMatch(actualWidth, actualHeight, sourceWidth, sourceHeight),
            baselineResized,
            comparedWidth,
            comparedHeight,
            compared,
            changed,
            threshold,
            RoundMetric(channelDelta / (denominator * 3.0)),
            RoundMetric(grayscaleDelta / denominator),
            RoundRatio((double)changed / denominator));
    }

    public static FreeWVisualEvidenceTrust EvaluateTolerance(
        FreeWVisualBaselineComparisonMetrics metrics,
        FreeWVisualBaselineComparisonTolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(tolerance);

        var failures = new List<string>();
        if (metrics.ComparedPixels <= 0)
            failures.Add("baseline comparison has no comparable pixels");
        if (tolerance.RequireDimensionMatch && !metrics.DimensionsMatch)
        {
            failures.Add(
                $"baseline dimensions {metrics.BaselineWidth.ToString(CultureInfo.InvariantCulture)}x{metrics.BaselineHeight.ToString(CultureInfo.InvariantCulture)} do not match evidence dimensions {metrics.ActualWidth.ToString(CultureInfo.InvariantCulture)}x{metrics.ActualHeight.ToString(CultureInfo.InvariantCulture)}");
        }
        if (metrics.MeanAbsoluteChannelDelta > tolerance.MaxMeanAbsoluteChannelDelta)
        {
            failures.Add(
                $"mean absolute channel delta {FormatMetric(metrics.MeanAbsoluteChannelDelta)} exceeds tolerance '{tolerance.Name}' maximum {FormatMetric(tolerance.MaxMeanAbsoluteChannelDelta)}");
        }
        if (metrics.MeanAbsoluteGrayscaleDelta > tolerance.MaxMeanAbsoluteGrayscaleDelta)
        {
            failures.Add(
                $"mean absolute grayscale delta {FormatMetric(metrics.MeanAbsoluteGrayscaleDelta)} exceeds tolerance '{tolerance.Name}' maximum {FormatMetric(tolerance.MaxMeanAbsoluteGrayscaleDelta)}");
        }
        if (metrics.ChangedPixelRatio > tolerance.MaxChangedPixelRatio)
        {
            failures.Add(
                $"changed pixel ratio {FormatPercent(metrics.ChangedPixelRatio)} exceeds tolerance '{tolerance.Name}' maximum {FormatPercent(tolerance.MaxChangedPixelRatio)}");
        }

        return new FreeWVisualEvidenceTrust(failures.Count == 0, failures);
    }

    private static FreeWVisualBaselineComparison BuildFailure(
        FreeWVisualEvidenceNormalizedRow row,
        string baselinePath,
        IReadOnlyList<string> candidatePaths,
        string status,
        FreeWVisualBaselineComparisonTolerance tolerance,
        string failure)
    {
        return new FreeWVisualBaselineComparison(
            row.EvidenceId,
            row.HostId,
            row.ScenarioId,
            row.PageNumber,
            row.OutputName,
            ResolveWordBaselinePolicy(row).BaselineScenarioId ?? row.ScenarioId,
            BuildBaselineMatchKey(row),
            baselinePath,
            candidatePaths,
            status,
            failure,
            tolerance,
            Metrics: null,
            new FreeWVisualEvidenceTrust(false, [failure]));
    }

    private static void AddCandidate(List<string> candidates, string scenario, string outputName)
    {
        if (string.IsNullOrWhiteSpace(outputName))
            return;

        var candidate = string.IsNullOrWhiteSpace(scenario)
            ? outputName
            : string.Concat(scenario, "/", outputName);
        candidates.Add(NormalizeManifestPath(candidate));
    }

    private static string ExpectedBaselineOutputName(FreeWVisualEvidenceNormalizedRow row, string scenarioId)
    {
        try
        {
            return FreeWVisualEvidencePlanner.ExpectedOutputName(
                scenarioId,
                Math.Max(1, row.PageNumber));
        }
        catch (ArgumentException)
        {
            return string.IsNullOrWhiteSpace(row.ExpectedOutputName)
                ? row.OutputName
                : row.ExpectedOutputName;
        }
    }

    private static string NormalizeCandidateSegment(string value, bool keepExtension = false)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var trimmed = value.Trim();
        var fileName = keepExtension
            ? Path.GetFileName(trimmed)
            : Path.GetFileNameWithoutExtension(trimmed);
        if (string.IsNullOrWhiteSpace(fileName))
            return "unknown";

        return fileName.Replace('\\', '_').Replace('/', '_');
    }

    private static string NormalizeManifestPath(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace('\\', '/');

    private static (int R, int G, int B) ReadRgb(
        ReadOnlySpan<byte> pixels,
        int offset,
        FreeWVisualEvidencePixelFormat format)
    {
        if (format == FreeWVisualEvidencePixelFormat.Bgra32)
            return (pixels[offset + 2], pixels[offset + 1], pixels[offset]);

        return (pixels[offset], pixels[offset + 1], pixels[offset + 2]);
    }

    private static double ToGrayscale((int R, int G, int B) rgb) =>
        rgb.R * 0.299 + rgb.G * 0.587 + rgb.B * 0.114;

    private static bool DimensionsMatch(int actualWidth, int actualHeight, int baselineWidth, int baselineHeight) =>
        actualWidth == baselineWidth && actualHeight == baselineHeight && actualWidth > 0 && actualHeight > 0;

    private static double RoundMetric(double value) =>
        double.IsFinite(value) ? Math.Round(value, 4, MidpointRounding.AwayFromZero) : 0;

    private static double RoundRatio(double value) =>
        double.IsFinite(value) ? Math.Round(value, 6, MidpointRounding.AwayFromZero) : 0;

    private static string FormatMetric(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string FormatPercent(double value) =>
        value.ToString("P3", CultureInfo.InvariantCulture);
}
