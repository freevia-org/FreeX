using System.Globalization;
using System.Text.Json;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum FreeWVisualEvidencePixelFormat
{
    Bgra32,
    Rgba32
}

public sealed record FreeWVisualEvidenceScenario(
    string ScenarioId,
    string Description,
    IReadOnlyList<string> ExpectedFeatureTags,
    string ExpectedOutputNamePattern,
    int MinimumExpectedOutputs,
    DocumentViewLayoutKind LayoutKind,
    FreeWVisualCompositionExpectation Composition);

public sealed record FreeWVisualCompositionExpectation(
    bool ExpectsPageChrome,
    bool ExpectsBodyText,
    bool ExpectsHeadersFooters,
    bool ExpectsFootnotes,
    bool ExpectsEndnotes,
    bool ExpectsColumns,
    bool ExpectsPageBorder,
    bool ExpectsWatermark,
    bool ExpectsFloatingObjects,
    bool ExpectsTrackedChanges,
    bool ExpectsComments,
    bool ExpectsSectionGeometryChange);

public sealed record FreeWVisualGeometryExpectation(
    double PageWidthDip,
    double PageHeightDip,
    double MarginLeftDip,
    double MarginTopDip,
    double MarginRightDip,
    double MarginBottomDip,
    double ContentWidthDip,
    double ContentHeightDip,
    double PageLeftDip,
    double ContentLeftDip,
    double TextAreaHeightDip,
    double DeskPaddingDip,
    double PageGapDip);

public sealed record FreeWVisualSectionExpectation(
    string OwnerId,
    int SectionOrdinal,
    int SectionRelativePageNumber);

public sealed record FreeWVisualColumnExpectation(
    int Count,
    double WidthDip,
    double GapDip,
    bool LineBetween,
    IReadOnlyList<double> WidthsDip);

public sealed record FreeWVisualPageBorderExpectation(
    bool Present,
    string? ColorHex,
    double WidthDip);

public sealed record FreeWVisualWatermarkExpectation(
    bool Present,
    string? Text,
    string? Layout,
    string? FontColorHex,
    double Opacity,
    bool IsPicture);

public sealed record FreeWVisualPageFeatureExpectation(
    FreeWVisualSectionExpectation Section,
    FreeWVisualColumnExpectation Columns,
    FreeWVisualPageBorderExpectation PageBorder,
    FreeWVisualWatermarkExpectation Watermark);

public sealed record FreeWVisualPageExpectation(
    int PageNumber,
    int PageCount,
    string LayoutKind,
    string ExpectedOutputName,
    FreeWVisualGeometryExpectation Geometry,
    FreeWVisualCompositionExpectation Composition,
    FreeWVisualPageFeatureExpectation Features,
    string? HeaderSlotName,
    string? FooterSlotName,
    bool HasFootnotes,
    bool HasEndnotes,
    bool IsSyntheticPage);

public sealed record FreeWVisualPixelStats(
    int Width,
    int Height,
    long SampledPixels,
    int DistinctSampledColors,
    string DominantColorHex,
    double DominantColorRatio,
    string BackgroundColorHex,
    long NonBackgroundSampledPixels,
    double NonBackgroundRatio);

public sealed record FreeWVisualEvidenceCapture(
    string ScenarioId,
    string HostId,
    string OutputName,
    string OutputPath,
    int PixelWidth,
    int PixelHeight,
    long ByteLength,
    FreeWVisualPixelStats PixelStats,
    FreeWVisualPageExpectation PageExpectation,
    IReadOnlyDictionary<string, string> HostMetadata);

public sealed record FreeWVisualEvidenceTrust(
    bool Passed,
    IReadOnlyList<string> Failures);

public sealed record FreeWVisualEvidenceTrustThresholds(
    long MinByteLength,
    int MinDistinctSampledColors,
    double MinNonBackgroundRatio,
    double MaxDominantColorRatio)
{
    public static FreeWVisualEvidenceTrustThresholds Default { get; } = new(
        MinByteLength: 512,
        MinDistinctSampledColors: 4,
        MinNonBackgroundRatio: 0.00025,
        MaxDominantColorRatio: 0.99975);
}

public sealed record FreeWVisualEvidenceRow(
    string EvidenceId,
    string ScenarioId,
    string HostId,
    IReadOnlyList<string> ExpectedFeatureTags,
    string OutputName,
    string OutputPath,
    int PixelWidth,
    int PixelHeight,
    long ByteLength,
    FreeWVisualPixelStats PixelStats,
    FreeWVisualPageExpectation PageExpectation,
    FreeWVisualEvidenceTrust Trust,
    IReadOnlyDictionary<string, string> HostMetadata);

public sealed record FreeWVisualEvidenceManifest(
    string SchemaId,
    int SchemaVersion,
    string Product,
    string CreatedUtc,
    IReadOnlyList<FreeWVisualEvidenceScenario> Scenarios,
    IReadOnlyList<FreeWVisualEvidenceRow> Evidence);

public static class FreeWVisualEvidencePlanner
{
    public const string ManifestFileName = "freew_visual_evidence_manifest.json";
    public const string SchemaId = "freew.visual-evidence.v1";
    public const int SchemaVersion = 2;

    private const int MaxTrackedColorCount = 4096;

    private static readonly FreeWVisualCompositionExpectation BodyPrintComposition = new(
        ExpectsPageChrome: true,
        ExpectsBodyText: true,
        ExpectsHeadersFooters: false,
        ExpectsFootnotes: false,
        ExpectsEndnotes: false,
        ExpectsColumns: false,
        ExpectsPageBorder: false,
        ExpectsWatermark: false,
        ExpectsFloatingObjects: false,
        ExpectsTrackedChanges: false,
        ExpectsComments: false,
        ExpectsSectionGeometryChange: false);

    private static readonly FreeWVisualEvidenceScenario[] ScenarioCatalog =
    [
        new(
            "f2-hf-basic",
            "F2 repeating header and footer page composition.",
            ["f2", "page-composition", "print-layout", "header-footer", "multi-page", "body-text"],
            "f2-hf-basic_p{page}.png",
            3,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsHeadersFooters = true }),
        new(
            "f2-hf-firstpage",
            "F2 first-page header/footer page composition.",
            ["f2", "page-composition", "print-layout", "header-footer", "first-page", "multi-page", "body-text"],
            "f2-hf-firstpage_p{page}.png",
            2,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsHeadersFooters = true }),
        new(
            "f2-hf-oddeven",
            "F2 odd/even header/footer page composition.",
            ["f2", "page-composition", "print-layout", "header-footer", "odd-even-pages", "multi-page", "body-text"],
            "f2-hf-oddeven_p{page}.png",
            2,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsHeadersFooters = true }),
        new(
            "f2-footnotes",
            "F2 footnote page composition.",
            ["f2", "page-composition", "print-layout", "footnotes", "multi-page", "body-text"],
            "f2-footnotes_p{page}.png",
            2,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsFootnotes = true }),
        new(
            "f2-endnotes",
            "F2 endnote page composition.",
            ["f2", "page-composition", "print-layout", "endnotes", "synthetic-endnotes-page", "body-text"],
            "f2-endnotes_p{page}.png",
            2,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsEndnotes = true }),
        new(
            "f2-columns",
            "F2 multi-column page composition.",
            ["f2", "page-composition", "print-layout", "columns", "multi-column", "body-text"],
            "f2-columns_p{page}.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsColumns = true }),
        new(
            "f2-border-watermark",
            "F2 page border and watermark composition.",
            ["f2", "page-composition", "print-layout", "page-border", "watermark", "body-text"],
            "f2-border-watermark_p{page}.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsPageBorder = true, ExpectsWatermark = true }),
        new(
            "f2-section-landscape",
            "F2 section-break page geometry change.",
            ["f2", "page-composition", "print-layout", "section-geometry", "portrait-landscape", "body-text"],
            "f2-section-landscape_p{page}.png",
            2,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsSectionGeometryChange = true }),
        new(
            "f2-tracked-changes",
            "F2 tracked-change visual composition.",
            ["f2", "page-composition", "print-layout", "tracked-changes", "revision-marks", "body-text"],
            "f2-tracked-changes_p{page}.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsTrackedChanges = true }),
        new(
            "f2-comments",
            "F2 anchored comment visual composition.",
            ["f2", "page-composition", "print-layout", "comments", "comment-anchors", "body-text"],
            "f2-comments_p{page}.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsComments = true }),
        new(
            "page-composition-print-layout",
            "Avalonia print-layout page composition shot.",
            ["page-composition", "avalonia", "print-layout", "page-chrome", "multi-page", "body-text"],
            "freew_print_layout.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition),
        new(
            "page-composition-columns",
            "Avalonia multi-column print-layout composition shot.",
            ["page-composition", "avalonia", "print-layout", "columns", "multi-column", "body-text"],
            "freew_columns_layout.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsColumns = true }),
        new(
            "page-composition-border-watermark",
            "Avalonia page border and watermark composition shot.",
            ["page-composition", "avalonia", "print-layout", "page-border", "watermark", "body-text"],
            "freew_border_watermark.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsPageBorder = true, ExpectsWatermark = true }),
        new(
            "page-composition-web-layout",
            "Avalonia web-layout page composition shot.",
            ["page-composition", "avalonia", "web-layout", "continuous-surface", "body-text"],
            "freew_web_layout.png",
            1,
            DocumentViewLayoutKind.WebLayout,
            BodyPrintComposition with { ExpectsPageChrome = false }),
        new(
            "page-composition-draft",
            "Avalonia draft-layout page composition shot.",
            ["page-composition", "avalonia", "draft", "continuous-surface", "body-text"],
            "freew_draft_layout.png",
            1,
            DocumentViewLayoutKind.Draft,
            BodyPrintComposition with { ExpectsPageChrome = false }),
        new(
            "page-composition-floating-image",
            "Avalonia floating-image page composition shot.",
            ["page-composition", "avalonia", "print-layout", "floating-image", "body-text"],
            "freew_floating_image.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsFloatingObjects = true }),
        new(
            "backstage-print-preview-fidelity",
            "Backstage Print Preview fixed-layout fidelity capture.",
            ["backstage", "print-preview", "print-layout", "fixed-layout", "page-chrome", "body-text"],
            "backstage-print-preview_p{page}.png",
            2,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition),
        new(
            "backstage-pdf-export-fidelity",
            "Backstage PDF export rasterized fixed-layout fidelity capture.",
            ["backstage", "pdf-export", "pdf-rasterized", "print-layout", "fixed-layout", "body-text"],
            "backstage-pdf-export_p{page}.png",
            2,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition)
    ];

    private static readonly IReadOnlyDictionary<string, FreeWVisualEvidenceScenario> ScenarioById =
        ScenarioCatalog.ToDictionary(s => s.ScenarioId, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<FreeWVisualEvidenceScenario> Scenarios => ScenarioCatalog;

    public static FreeWVisualEvidenceScenario ResolveScenario(string scenarioId)
    {
        var normalized = NormalizeScenarioId(scenarioId);
        if (ScenarioById.TryGetValue(normalized, out var scenario))
            return scenario;

        return new FreeWVisualEvidenceScenario(
            normalized,
            "Ad hoc FreeW visual evidence capture.",
            ["page-composition", "ad-hoc", "body-text"],
            normalized + "_p{page}.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition);
    }

    public static string NormalizeScenarioId(string scenarioId)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
            return "unknown";

        var id = Path.GetFileNameWithoutExtension(scenarioId.Trim());
        return string.IsNullOrWhiteSpace(id) ? "unknown" : id;
    }

    public static string ExpectedOutputName(string scenarioId, int pageNumber, string? actualOutputName = null)
    {
        var scenario = ResolveScenario(scenarioId);
        var pattern = scenario.ExpectedOutputNamePattern;
        if (!pattern.Contains("{page}", StringComparison.Ordinal))
            return pattern;

        var page = Math.Max(1, pageNumber).ToString(CultureInfo.InvariantCulture);
        var expected = pattern.Replace("{page}", page, StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(expected) ? actualOutputName ?? string.Empty : expected;
    }

    public static FreeWVisualPageExpectation BuildPageExpectation(
        string scenarioId,
        PageSettings page,
        int pageNumber,
        int pageCount,
        string outputName,
        DocumentViewLayoutKind? layoutKind = null,
        double? availableWidthDip = null,
        string? headerSlotName = null,
        string? footerSlotName = null,
        bool hasFootnotes = false,
        bool hasEndnotes = false,
        bool isSyntheticPage = false,
        int? sectionOrdinal = null,
        int? sectionRelativePageNumber = null,
        string? sectionOwnerId = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        var scenario = ResolveScenario(scenarioId);
        var kind = layoutKind ?? scenario.LayoutKind;
        var metrics = DocumentViewLayoutPlanner.BuildPageMetrics(page);
        var surface = DocumentViewLayoutPlanner.BuildSurfacePlan(
            page,
            kind,
            availableWidthDip ?? metrics.PageWidthDip);
        var geometry = new FreeWVisualGeometryExpectation(
            RoundDip(metrics.PageWidthDip),
            RoundDip(metrics.PageHeightDip),
            RoundDip(metrics.MarginLeftDip),
            RoundDip(metrics.MarginTopDip),
            RoundDip(metrics.MarginRightDip),
            RoundDip(metrics.MarginBottomDip),
            RoundDip(metrics.ContentWidthDip),
            RoundDip(metrics.ContentHeightDip),
            RoundDip(surface.PageLeftDip),
            RoundDip(surface.ContentLeftDip),
            RoundDip(surface.TextAreaHeightDip),
            RoundDip(surface.DeskPaddingDip),
            RoundDip(surface.PageGapDip));
        var features = BuildPageFeatures(
            page,
            kind,
            metrics.ContentWidthDip,
            sectionOrdinal,
            sectionRelativePageNumber,
            sectionOwnerId);

        var expectedOutputName = ExpectedOutputName(scenario.ScenarioId, pageNumber, outputName);
        return new FreeWVisualPageExpectation(
            Math.Max(1, pageNumber),
            Math.Max(1, pageCount),
            kind.ToString(),
            expectedOutputName,
            geometry,
            scenario.Composition,
            features,
            headerSlotName,
            footerSlotName,
            hasFootnotes,
            hasEndnotes,
            isSyntheticPage);
    }

    public static FreeWVisualPageFeatureExpectation BuildPageFeatures(
        PageSettings page,
        DocumentViewLayoutKind layoutKind = DocumentViewLayoutKind.PrintLayout,
        double? contentWidthDip = null,
        int? sectionOrdinal = null,
        int? sectionRelativePageNumber = null,
        string? sectionOwnerId = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        var metrics = DocumentViewLayoutPlanner.BuildPageMetrics(page);
        var contentWidth = contentWidthDip is > 0 ? contentWidthDip.Value : metrics.ContentWidthDip;
        var columns = DocumentViewLayoutPlanner.BuildColumnPlan(
            page,
            contentWidth,
            usePageColumns: layoutKind == DocumentViewLayoutKind.PrintLayout);
        var widths = Enumerable
            .Repeat(RoundDip(columns.WidthDip), Math.Max(1, columns.Count))
            .ToList();
        var safeSectionOrdinal = Math.Max(1, sectionOrdinal ?? 1);
        var section = new FreeWVisualSectionExpectation(
            string.IsNullOrWhiteSpace(sectionOwnerId)
                ? BuildSectionOwnerId(safeSectionOrdinal)
                : sectionOwnerId,
            safeSectionOrdinal,
            Math.Max(1, sectionRelativePageNumber ?? 1));
        var border = page.PageBorder is { } pageBorder
            ? new FreeWVisualPageBorderExpectation(
                true,
                NormalizeHexColor(pageBorder.ColorHex),
                RoundDip(PageLayout.PointsToDip(Math.Max(0, pageBorder.WidthPt))))
            : new FreeWVisualPageBorderExpectation(false, null, 0);
        var watermark = page.EffectiveWatermark is { } wm
            ? new FreeWVisualWatermarkExpectation(
                true,
                wm.Text,
                wm.Layout.ToString(),
                NormalizeHexColor(wm.FontColorHex),
                Math.Clamp(wm.Opacity, 0, 1),
                wm.IsPicture)
            : new FreeWVisualWatermarkExpectation(false, null, null, null, 0, false);

        return new FreeWVisualPageFeatureExpectation(
            section,
            new FreeWVisualColumnExpectation(
                columns.Count,
                RoundDip(columns.WidthDip),
                RoundDip(columns.GapDip),
                columns.LineBetween,
                widths),
            border,
            watermark);
    }

    public static int ResolveSectionOrdinal(TextDocument document, PageSettings page)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(page);

        var sections = document.Sections;
        for (var i = 0; i < sections.Count; i++)
        {
            if (ReferenceEquals(sections[i].Page, page))
                return i + 1;
        }

        for (var i = 0; i < sections.Count; i++)
        {
            if (PageSettingsMatch(sections[i].Page, page))
                return i + 1;
        }

        return 1;
    }

    public static string BuildSectionOwnerId(int sectionOrdinal) =>
        "section-" + Math.Max(1, sectionOrdinal).ToString(CultureInfo.InvariantCulture);

    public static FreeWVisualEvidenceRow BuildEvidenceRow(
        FreeWVisualEvidenceCapture capture,
        FreeWVisualEvidenceTrustThresholds? thresholds = null)
    {
        ArgumentNullException.ThrowIfNull(capture);

        var scenario = ResolveScenario(capture.ScenarioId);
        var trust = EvaluateTrust(capture, thresholds ?? FreeWVisualEvidenceTrustThresholds.Default);
        return new FreeWVisualEvidenceRow(
            EvidenceId: $"{capture.HostId}:{scenario.ScenarioId}:{capture.OutputName}",
            ScenarioId: scenario.ScenarioId,
            HostId: capture.HostId,
            ExpectedFeatureTags: scenario.ExpectedFeatureTags,
            OutputName: capture.OutputName,
            OutputPath: capture.OutputPath,
            PixelWidth: capture.PixelWidth,
            PixelHeight: capture.PixelHeight,
            ByteLength: capture.ByteLength,
            PixelStats: capture.PixelStats,
            PageExpectation: capture.PageExpectation,
            Trust: trust,
            HostMetadata: capture.HostMetadata);
    }

    public static FreeWVisualEvidenceRow BuildEvidenceRow(
        string scenarioId,
        string hostId,
        string outputPath,
        int pixelWidth,
        int pixelHeight,
        long byteLength,
        FreeWVisualPixelStats pixelStats,
        PageSettings page,
        int pageNumber,
        int pageCount,
        DocumentViewLayoutKind? layoutKind = null,
        double? availableWidthDip = null,
        string? headerSlotName = null,
        string? footerSlotName = null,
        bool hasFootnotes = false,
        bool hasEndnotes = false,
        bool isSyntheticPage = false,
        int? sectionOrdinal = null,
        int? sectionRelativePageNumber = null,
        string? sectionOwnerId = null,
        IReadOnlyDictionary<string, string>? hostMetadata = null,
        FreeWVisualEvidenceTrustThresholds? thresholds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(pixelStats);
        ArgumentNullException.ThrowIfNull(page);

        var outputName = Path.GetFileName(outputPath);
        var expectation = BuildPageExpectation(
            scenarioId,
            page,
            pageNumber,
            pageCount,
            outputName,
            layoutKind,
            availableWidthDip,
            headerSlotName,
            footerSlotName,
            hasFootnotes,
            hasEndnotes,
            isSyntheticPage,
            sectionOrdinal,
            sectionRelativePageNumber,
            sectionOwnerId);
        var capture = new FreeWVisualEvidenceCapture(
            ScenarioId: scenarioId,
            HostId: hostId,
            OutputName: outputName,
            OutputPath: Path.GetFullPath(outputPath),
            PixelWidth: pixelWidth,
            PixelHeight: pixelHeight,
            ByteLength: byteLength,
            PixelStats: pixelStats,
            PageExpectation: expectation,
            HostMetadata: hostMetadata ?? new Dictionary<string, string>());

        return BuildEvidenceRow(capture, thresholds);
    }

    public static FreeWVisualEvidenceTrust EvaluateTrust(
        FreeWVisualEvidenceCapture capture,
        FreeWVisualEvidenceTrustThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(thresholds);

        var failures = new List<string>();
        if (capture.ByteLength < thresholds.MinByteLength)
            failures.Add($"capture byte length {capture.ByteLength} is below {thresholds.MinByteLength}");
        if (capture.PixelWidth <= 0 || capture.PixelHeight <= 0)
            failures.Add("capture pixel dimensions must be positive");
        if (capture.PixelStats.Width != capture.PixelWidth || capture.PixelStats.Height != capture.PixelHeight)
            failures.Add("pixel stats dimensions do not match capture dimensions");
        if (capture.PixelStats.SampledPixels <= 0)
            failures.Add("pixel stats contain no sampled pixels");
        if (capture.PixelStats.DistinctSampledColors < thresholds.MinDistinctSampledColors)
        {
            failures.Add(
                $"distinct sampled colors {capture.PixelStats.DistinctSampledColors} is below {thresholds.MinDistinctSampledColors}");
        }
        if (capture.PixelStats.NonBackgroundRatio < thresholds.MinNonBackgroundRatio)
        {
            failures.Add(
                $"non-background pixel ratio {capture.PixelStats.NonBackgroundRatio.ToString("0.#####", CultureInfo.InvariantCulture)} is below {thresholds.MinNonBackgroundRatio.ToString("0.#####", CultureInfo.InvariantCulture)}");
        }
        if (capture.PixelStats.DominantColorRatio > thresholds.MaxDominantColorRatio)
        {
            failures.Add(
                $"dominant color ratio {capture.PixelStats.DominantColorRatio.ToString("0.#####", CultureInfo.InvariantCulture)} exceeds {thresholds.MaxDominantColorRatio.ToString("0.#####", CultureInfo.InvariantCulture)}");
        }

        return new FreeWVisualEvidenceTrust(failures.Count == 0, failures);
    }

    public static void EnsureTrusted(FreeWVisualEvidenceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.Trust.Passed)
            return;

        throw new InvalidOperationException(
            $"Visual evidence '{row.OutputName}' failed trust checks: {string.Join("; ", row.Trust.Failures)}");
    }

    public static FreeWVisualPixelStats ComputePixelStats(
        ReadOnlySpan<byte> pixels,
        int width,
        int height,
        int stride,
        FreeWVisualEvidencePixelFormat format,
        string backgroundColorHex = "#FFFFFF")
    {
        if (width <= 0 || height <= 0 || stride <= 0 || pixels.IsEmpty)
        {
            return new FreeWVisualPixelStats(
                Math.Max(0, width),
                Math.Max(0, height),
                0,
                0,
                "#000000",
                0,
                NormalizeHexColor(backgroundColorHex),
                0,
                0);
        }

        var background = ParseRgb(backgroundColorHex, fallback: 0xFFFFFF);
        var counts = new Dictionary<int, long>();
        long sampled = 0;
        long nonBackground = 0;
        long dominantCount = 0;
        var dominantColor = 0;
        var distinctOverflow = false;

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * stride;
            if (rowOffset < 0 || rowOffset + 3 >= pixels.Length)
                break;

            for (var x = 0; x < width; x++)
            {
                var offset = rowOffset + x * 4;
                if (offset + 3 >= pixels.Length)
                    break;

                int r;
                int g;
                int b;
                if (format == FreeWVisualEvidencePixelFormat.Bgra32)
                {
                    b = pixels[offset];
                    g = pixels[offset + 1];
                    r = pixels[offset + 2];
                }
                else
                {
                    r = pixels[offset];
                    g = pixels[offset + 1];
                    b = pixels[offset + 2];
                }

                var color = (r << 16) | (g << 8) | b;
                sampled++;
                if (ColorDistanceSquared(color, background) > 64)
                    nonBackground++;

                if (counts.TryGetValue(color, out var count))
                {
                    count++;
                    counts[color] = count;
                    if (count > dominantCount)
                    {
                        dominantCount = count;
                        dominantColor = color;
                    }
                }
                else if (counts.Count < MaxTrackedColorCount)
                {
                    counts[color] = 1;
                    if (dominantCount == 0)
                    {
                        dominantCount = 1;
                        dominantColor = color;
                    }
                }
                else
                {
                    distinctOverflow = true;
                }
            }
        }

        var distinct = counts.Count + (distinctOverflow ? 1 : 0);
        return new FreeWVisualPixelStats(
            width,
            height,
            sampled,
            distinct,
            ToHex(dominantColor),
            sampled > 0 ? (double)dominantCount / sampled : 0,
            NormalizeHexColor(backgroundColorHex),
            nonBackground,
            sampled > 0 ? (double)nonBackground / sampled : 0);
    }

    public static FreeWVisualEvidenceManifest BuildManifest(
        IReadOnlyList<FreeWVisualEvidenceRow> evidence,
        DateTimeOffset? createdUtc = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var scenarioIds = evidence
            .Select(e => e.ScenarioId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Select(ResolveScenario)
            .ToList();

        return new FreeWVisualEvidenceManifest(
            SchemaId,
            SchemaVersion,
            "FreeW",
            (createdUtc ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            scenarioIds,
            evidence);
    }

    public static string ToJson(FreeWVisualEvidenceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    public static void WriteManifest(
        string outputDirectory,
        IReadOnlyList<FreeWVisualEvidenceRow> evidence,
        DateTimeOffset? createdUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var manifest = BuildManifest(evidence, createdUtc);
        File.WriteAllText(Path.Combine(outputDirectory, ManifestFileName), ToJson(manifest));
    }

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static double RoundDip(double value) =>
        double.IsFinite(value) ? Math.Round(value, 3, MidpointRounding.AwayFromZero) : 0;

    private static string NormalizeHexColor(string hex) => ToHex(ParseRgb(hex, fallback: 0xFFFFFF));

    private static int ParseRgb(string hex, int fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return fallback;

        var value = hex.Trim();
        if (value.StartsWith('#'))
            value = value[1..];
        if (value.Length == 8)
            value = value[2..];
        if (value.Length != 6)
            return fallback;

        return int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? rgb & 0xFFFFFF
            : fallback;
    }

    private static int ColorDistanceSquared(int color, int other)
    {
        var dr = ((color >> 16) & 0xFF) - ((other >> 16) & 0xFF);
        var dg = ((color >> 8) & 0xFF) - ((other >> 8) & 0xFF);
        var db = (color & 0xFF) - (other & 0xFF);
        return dr * dr + dg * dg + db * db;
    }

    private static string ToHex(int rgb) =>
        "#" + (rgb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);

    private static bool PageSettingsMatch(PageSettings left, PageSettings right) =>
        Math.Abs(left.WidthPt - right.WidthPt) < 0.001
        && Math.Abs(left.HeightPt - right.HeightPt) < 0.001
        && Math.Abs(left.MarginLeftPt - right.MarginLeftPt) < 0.001
        && Math.Abs(left.MarginTopPt - right.MarginTopPt) < 0.001
        && Math.Abs(left.MarginRightPt - right.MarginRightPt) < 0.001
        && Math.Abs(left.MarginBottomPt - right.MarginBottomPt) < 0.001
        && left.Landscape == right.Landscape;
}
