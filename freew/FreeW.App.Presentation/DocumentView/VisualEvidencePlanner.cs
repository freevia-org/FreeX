using System.Globalization;
using System.Text.Json;
using FreeW.Core.Model;
using FreeW.App.Presentation.Ribbon;

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
    bool ExpectsTables,
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

public sealed record FreeWVisualSectionGeometryPagePlan(
    int PageNumber,
    int PageCount,
    PageSettings Page,
    int SectionOrdinal,
    int SectionRelativePageNumber,
    string SectionOwnerId,
    string Orientation);

public sealed record FreeWVisualSectionGeometrySurfacePlan(
    FreeWVisualSectionGeometryPagePlan PagePlan,
    TextDocument Document,
    IReadOnlyList<int> SourceBlockIndexes,
    double CaptureWidthDip,
    double CaptureHeightDip,
    double PageLeftDip,
    double PageTopDip,
    string RenderStatus)
{
    public int PageNumber => PagePlan.PageNumber;
    public int PageCount => PagePlan.PageCount;
    public PageSettings Page => PagePlan.Page;
    public int SectionOrdinal => PagePlan.SectionOrdinal;
    public int SectionRelativePageNumber => PagePlan.SectionRelativePageNumber;
    public string SectionOwnerId => PagePlan.SectionOwnerId;
    public string Orientation => PagePlan.Orientation;
}

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

public sealed record FreeWVisualTableExpectation(
    int TableCount,
    int TotalRows,
    int TotalCells,
    int MaxGridColumnCount,
    int EstimatedPageCount,
    bool HasHeaderRow,
    bool RepeatsHeaderRow,
    bool HasPaginationPlan,
    bool HasMultiPageTables,
    bool HasRepeatedHeaderPages,
    bool HasKeepTogetherRows,
    bool HasBandedRows,
    bool HasBandedColumns,
    bool HasMergedCells,
    bool HasVerticalMerges,
    bool HasCellShading,
    bool HasCustomCellBorders,
    bool HasCellMargins,
    bool HasCellSpacing,
    bool HasVerticalText,
    bool HasVerticalAlignment,
    bool HasPreferredWidths,
    bool HasNamedStyle,
    bool HasFloatingTextWrap,
    IReadOnlyList<DocumentTableLayoutPlan> Tables,
    IReadOnlyList<DocumentTablePaginationPlan> PaginationPlans);

public sealed record FreeWVisualDrawingObjectExpectation(
    int FloatingObjectCount,
    int BehindTextCount,
    int InFrontCount,
    bool HasImages,
    bool HasShapes,
    bool HasCharts,
    bool HasSmartArt,
    bool HasWordArt,
    bool HasGroups,
    bool HasSquareWrap,
    bool HasTopAndBottomWrap,
    bool HasZOrder,
    FreeWVisualDrawingObjectEffectExpectation Effects,
    IReadOnlyList<DocumentFloatingObjectSnapshot> Objects);

public sealed record FreeWVisualDrawingObjectEffectExpectation(
    int EffectObjectCount,
    int ShapeEffectObjectCount,
    int ImageEffectObjectCount,
    int WordArtEffectObjectCount,
    int RenderedGroupChildEffectObjectCount,
    int RenderedGroupChildShapeEffectObjectCount,
    int RenderedGroupChildWordArtEffectObjectCount,
    int PlannedGroupChildEffectObjectCount,
    int PlannedGroupChildShapeEffectObjectCount,
    int PlannedGroupChildWordArtEffectObjectCount,
    bool HasShadow,
    bool HasGlow,
    bool HasReflection,
    bool HasSoftEdge,
    bool HasBevel,
    bool HasArtisticEffect,
    IReadOnlyList<string> EffectSummaries,
    IReadOnlyList<string> RenderedGroupChildEffectSummaries,
    IReadOnlyList<string> PlannedGroupChildEffectSummaries)
{
    public bool HasAny => EffectObjectCount > 0 || RenderedGroupChildEffectObjectCount > 0;
    public bool HasRenderedGroupChildEffects => RenderedGroupChildEffectObjectCount > 0;
    public bool HasPlannedGroupChildEffects => PlannedGroupChildEffectObjectCount > 0;
}

public sealed record FreeWVisualChartSmartArtExpectation(
    int ChartCount,
    int SmartArtCount,
    bool HasChartPalette,
    bool HasChartQuickLayout,
    bool HasMarkerOnlyScatter,
    bool HasLegend,
    bool HasGridlines,
    bool HasDataLabels,
    bool HasAxisTitles,
    bool HasPlotAreaFill,
    bool HasSmartArtLayout,
    bool HasSmartArtColorScheme,
    bool HasSmartArtStyle,
    int SmartArtNodeCount,
    int DistinctSmartArtFillCount,
    IReadOnlyList<ChartVisualPlan> Charts,
    IReadOnlyList<SmartArtVisualPlan> SmartArts);

public sealed record FreeWVisualFieldExpectation(
    int SimpleFieldCount,
    int ComplexFieldCount,
    int BodyFieldCount,
    int HeaderFooterFieldCount,
    int PageFieldCount,
    int NumPagesFieldCount,
    int DocumentPropertyFieldCount,
    bool HasPageFields,
    bool HasNumPagesFields,
    bool HasDocumentPropertyFields,
    bool HasComplexFields,
    bool HasComplexResultFields,
    bool HasHeaderFooterFields,
    IReadOnlyList<string> FieldKinds,
    IReadOnlyList<string> ComplexFieldKeywords,
    IReadOnlyList<string> HeaderFooterSlotNames);

public sealed record FreeWVisualTableOfAuthoritiesPageReference(
    string Category,
    string EntryText,
    string PageReferenceText,
    IReadOnlyList<int> PageNumbers);

public sealed record FreeWVisualTableOfAuthoritiesExpectation(
    int EntryCount,
    int EntryWithPageReferenceCount,
    bool HasGeneratedTable,
    bool HasPageReferences,
    bool HasExplicitPageNumbers,
    bool HasPassimReferences,
    IReadOnlyList<FreeWVisualTableOfAuthoritiesPageReference> PageReferences);

public sealed record FreeWVisualPageExpectation(
    int PageNumber,
    int PageCount,
    string LayoutKind,
    string ExpectedOutputName,
    FreeWVisualGeometryExpectation Geometry,
    FreeWVisualCompositionExpectation Composition,
    FreeWVisualPageFeatureExpectation Features,
    FreeWVisualTableExpectation Tables,
    FreeWVisualDrawingObjectExpectation DrawingObjects,
    FreeWVisualChartSmartArtExpectation ChartSmartArt,
    FreeWVisualFieldExpectation Fields,
    FreeWVisualTableOfAuthoritiesExpectation TableOfAuthorities,
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
    public const int SchemaVersion = 10;
    public const string SectionGeometryPageSurfaceRenderStatus = "section-page-surface";

    private const int MaxTrackedColorCount = 4096;

    private static readonly RunFieldKind[] DocumentPropertyFieldKinds =
    [
        RunFieldKind.Author,
        RunFieldKind.Title,
        RunFieldKind.Subject,
        RunFieldKind.Keywords,
        RunFieldKind.DocComments
    ];

    private static readonly FreeWVisualCompositionExpectation BodyPrintComposition = new(
        ExpectsPageChrome: true,
        ExpectsBodyText: true,
        ExpectsTables: false,
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

    private static readonly FreeWVisualCompositionExpectation BackstagePrintExportComposition =
        BodyPrintComposition with
        {
            ExpectsHeadersFooters = true,
            ExpectsColumns = true,
            ExpectsPageBorder = true,
            ExpectsWatermark = true
        };

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
            "field-page-number-variants",
            "PAGE, NUMPAGES, document-property, and complex field visual composition.",
            [
                "fields",
                "page-number-fields",
                "numpages-fields",
                "document-property-fields",
                "complex-fields",
                "header-footer-fields",
                "page-composition",
                "print-layout",
                "header-footer",
                "multi-page",
                "body-text"
            ],
            "field-page-number-variants_p{page}.png",
            3,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsHeadersFooters = true }),
        new(
            "references-heavy-fields",
            "References-heavy CITATION, BIBLIOGRAPHY, cached TOA, and shared generated TOA page-reference composition.",
            [
                "references",
                "source-manager",
                "citation-fields",
                "bibliography-fields",
                "toa-fields",
                "cached-toa-page-number-sentinel",
                "generated-toa-page-references",
                "complex-fields",
                "legal-authorities",
                "generated-bibliography",
                "generated-table-of-authorities",
                "page-composition",
                "print-layout",
                "multi-page",
                "body-text"
            ],
            "references-heavy-fields_p{page}.png",
            2,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition),
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
            "table-layout-complex",
            "Complex Word-style table layout fidelity capture.",
            [
                "table-layout",
                "tables",
                "print-layout",
                "body-text",
                "merged-cells",
                "vertical-merge",
                "repeat-header-row",
                "banded-rows",
                "cell-shading",
                "cell-borders",
                "cell-margins",
                "cell-spacing",
                "vertical-text",
                "named-table-style"
            ],
            "table-layout-complex_p{page}.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsTables = true }),
        new(
            "table-pagination-repeat-header",
            "Two-page table pagination with repeated header and keep-together rows.",
            [
                "table-layout",
                "table-pagination",
                "repeat-header-row",
                "keep-rows",
                "banded-rows",
                "print-layout",
                "body-text"
            ],
            "table-pagination-repeat-header_p{page}.png",
            2,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsTables = true }),
        new(
            "chart-smartart-complex",
            "Complex Word-style chart and SmartArt fidelity capture.",
            [
                "chart-smartart",
                "charts",
                "smartart",
                "print-layout",
                "body-text",
                "chart-palette",
                "quick-layout",
                "scatter-markers",
                "chart-legend",
                "chart-gridlines",
                "data-labels",
                "axis-titles",
                "plot-area-fill",
                "smartart-layout",
                "smartart-colors",
                "smartart-style"
            ],
            "chart-smartart-complex_p{page}.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition),
        new(
            "drawing-objects-complex",
            "Complex Word-style drawing-object fidelity capture.",
            [
                "drawing-objects",
                "floating-objects",
                "print-layout",
                "body-text",
                "drawing-effects",
                "shapes",
                "images",
                "charts",
                "smartart",
                "wordart",
                "drawing-groups",
                "shape-effects",
                "image-effects",
                "wordart-effects",
                "grouped-child-effects",
                "grouped-child-shape-effects",
                "shadow",
                "glow",
                "reflection",
                "artistic-effect",
                "square-wrap",
                "top-bottom-wrap",
                "behind-text",
                "in-front",
                "z-order"
            ],
            "drawing-objects-complex_p{page}.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsFloatingObjects = true }),
        new(
            "wordart-watermark-stress",
            "WordArt over watermark and page-border fidelity capture.",
            [
                "drawing-objects",
                "floating-objects",
                "print-layout",
                "body-text",
                "drawing-effects",
                "wordart",
                "shape-effects",
                "wordart-effects",
                "shadow",
                "glow",
                "watermark",
                "page-border",
                "square-wrap",
                "in-front",
                "z-order"
            ],
            "wordart-watermark-stress_p{page}.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with
            {
                ExpectsPageBorder = true,
                ExpectsWatermark = true,
                ExpectsFloatingObjects = true
            }),
        new(
            "wordart-picture-watermark-layout",
            "WordArt, picture watermark, page-layout stress fidelity capture.",
            [
                "wordart-watermark-layout",
                "drawing-objects",
                "wordart",
                "picture-watermark",
                "watermark",
                "page-border",
                "columns",
                "print-layout",
                "body-text",
                "in-front"
            ],
            "wordart-picture-watermark-layout_p{page}.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with
            {
                ExpectsColumns = true,
                ExpectsPageBorder = true,
                ExpectsWatermark = true,
                ExpectsFloatingObjects = true
            }),
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
            ["backstage", "print-preview", "print-layout", "fixed-layout", "page-chrome", "header-footer", "columns", "page-border", "watermark", "body-text"],
            "backstage-print-preview_p{page}.png",
            2,
            DocumentViewLayoutKind.PrintLayout,
            BackstagePrintExportComposition),
        new(
            "backstage-pdf-export-fidelity",
            "Backstage PDF export rasterized fixed-layout fidelity capture.",
            ["backstage", "pdf-export", "pdf-rasterized", "print-layout", "fixed-layout", "header-footer", "columns", "page-border", "watermark", "body-text"],
            "backstage-pdf-export_p{page}.png",
            2,
            DocumentViewLayoutKind.PrintLayout,
            BackstagePrintExportComposition)
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
        string? sectionOwnerId = null,
        TextDocument? document = null)
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
        var tables = BuildTableExpectation(document);
        var drawingObjects = BuildDrawingObjectExpectation(document, surface, features.Columns.Count);
        var chartSmartArt = BuildChartSmartArtExpectation(document);
        var fields = BuildFieldExpectation(document);
        var tableOfAuthorities = BuildTableOfAuthoritiesExpectation(document);

        var expectedOutputName = ExpectedOutputName(scenario.ScenarioId, pageNumber, outputName);
        return new FreeWVisualPageExpectation(
            Math.Max(1, pageNumber),
            Math.Max(1, pageCount),
            kind.ToString(),
            expectedOutputName,
            geometry,
            scenario.Composition,
            features,
            tables,
            drawingObjects,
            chartSmartArt,
            fields,
            tableOfAuthorities,
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

    public static IReadOnlyList<FreeWVisualSectionGeometryPagePlan> BuildSectionGeometryPagePlans(
        TextDocument document,
        int pageCount,
        IReadOnlyList<int>? blockPageAssignments = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var safePageCount = Math.Max(1, pageCount);
        var assignments = blockPageAssignments ?? BuildSectionBreakPageAssignments(document, safePageCount);
        var pageSections = HeaderFooterPagePlanner.MapPagesToSections(
            document,
            assignments,
            safePageCount);

        return pageSections
            .Select((pageSection, index) =>
            {
                var sectionOrdinal = pageSection.SectionIndex + 1;
                return new FreeWVisualSectionGeometryPagePlan(
                    PageNumber: index + 1,
                    PageCount: safePageCount,
                    Page: pageSection.PageSettings,
                    SectionOrdinal: sectionOrdinal,
                    SectionRelativePageNumber: pageSection.SectionRelativePageNumber,
                    SectionOwnerId: BuildSectionOwnerId(sectionOrdinal),
                    Orientation: pageSection.PageSettings.Landscape ? "landscape" : "portrait");
            })
            .ToList();
    }

    public static IReadOnlyList<FreeWVisualSectionGeometrySurfacePlan> BuildSectionGeometrySurfacePlans(
        TextDocument document,
        int pageCount,
        DocumentViewLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        options ??= DocumentViewLayoutOptions.AvaloniaDefault;
        var safePageCount = Math.Max(1, pageCount);
        var assignments = BuildSectionBreakPageAssignments(document, safePageCount);
        var pagePlans = BuildSectionGeometryPagePlans(document, safePageCount, assignments);
        var surfacePlans = new List<FreeWVisualSectionGeometrySurfacePlan>(pagePlans.Count);

        foreach (var pagePlan in pagePlans)
        {
            var pageIndex = pagePlan.PageNumber - 1;
            var sourceBlockIndexes = assignments
                .Select((assignedPage, blockIndex) => new { assignedPage, blockIndex })
                .Where(item => item.assignedPage == pageIndex)
                .Select(item => item.blockIndex)
                .ToList();
            if (sourceBlockIndexes.Count == 0 && document.Blocks.Count > 0)
                sourceBlockIndexes.Add(Math.Clamp(pageIndex, 0, document.Blocks.Count - 1));

            var captureWidth = Math.Ceiling(PageLayout.PointsToDip(pagePlan.Page.WidthPt) + options.DeskPaddingDip * 2);
            var surface = DocumentViewLayoutPlanner.BuildSurfacePlan(
                pagePlan.Page,
                DocumentViewLayoutKind.PrintLayout,
                captureWidth,
                options);
            var captureHeight = Math.Ceiling(surface.PageTopDip(0) + surface.PageHeightDip + surface.DeskPaddingDip);

            surfacePlans.Add(new FreeWVisualSectionGeometrySurfacePlan(
                pagePlan,
                BuildSectionGeometrySurfaceDocument(document, pagePlan.Page, sourceBlockIndexes),
                sourceBlockIndexes,
                captureWidth,
                captureHeight,
                surface.PageLeftDip,
                surface.PageTopDip(0),
                SectionGeometryPageSurfaceRenderStatus));
        }

        return surfacePlans;
    }

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
        FreeWVisualEvidenceTrustThresholds? thresholds = null,
        TextDocument? document = null)
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
            sectionOwnerId,
            document);
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

    public static FreeWVisualTableExpectation BuildTableExpectation(TextDocument? document)
    {
        if (document is null)
            return EmptyTableExpectation;

        var tables = DocumentViewLayoutPlanner.BuildTableLayoutPlans(document);
        if (tables.Count == 0)
            return EmptyTableExpectation;

        return new FreeWVisualTableExpectation(
            TableCount: tables.Count,
            TotalRows: tables.Sum(table => table.RowCount),
            TotalCells: tables.Sum(table => table.Cells.Count),
            MaxGridColumnCount: tables.Max(table => table.GridColumnCount),
            EstimatedPageCount: tables.Max(table => table.Pagination.EstimatedPageCount),
            HasHeaderRow: tables.Any(table => table.HasHeaderRow),
            RepeatsHeaderRow: tables.Any(table => table.RepeatsHeaderRow),
            HasPaginationPlan: tables.Any(table => table.Pagination.Pages.Count > 0),
            HasMultiPageTables: tables.Any(table => table.Pagination.EstimatedPageCount > 1),
            HasRepeatedHeaderPages: tables.Any(table =>
                table.Pagination.Pages.Any(page => page.IncludesRepeatedHeader)),
            HasKeepTogetherRows: tables.Any(table => table.Pagination.HasKeepTogetherRows),
            HasBandedRows: tables.Any(table => table.HasBandedRows),
            HasBandedColumns: tables.Any(table => table.HasBandedColumns),
            HasMergedCells: tables.Any(table => table.HasMergedCells),
            HasVerticalMerges: tables.Any(table => table.HasVerticalMerges),
            HasCellShading: tables.Any(table => table.HasCellShading),
            HasCustomCellBorders: tables.Any(table => table.HasCustomCellBorders),
            HasCellMargins: tables.Any(table => table.HasCellMargins),
            HasCellSpacing: tables.Any(table => table.HasCellSpacing),
            HasVerticalText: tables.Any(table => table.HasVerticalText),
            HasVerticalAlignment: tables.Any(table => table.HasVerticalAlignment),
            HasPreferredWidths: tables.Any(table => table.HasPreferredWidths),
            HasNamedStyle: tables.Any(table => table.HasNamedStyle),
            HasFloatingTextWrap: tables.Any(table => table.HasFloatingTextWrap),
            Tables: tables,
            PaginationPlans: tables.Select(table => table.Pagination).ToList());
    }

    public static FreeWVisualDrawingObjectExpectation BuildDrawingObjectExpectation(
        TextDocument? document,
        DocumentViewSurfacePlan surface,
        int columnCount)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (document is null)
            return EmptyDrawingObjectExpectation;

        var objects = DocumentViewLayoutPlanner
            .BuildFloatingObjectSnapshots(document, surface, Math.Max(1, columnCount))
            .ToList();
        if (objects.Count == 0)
            return EmptyDrawingObjectExpectation;

        return new FreeWVisualDrawingObjectExpectation(
            FloatingObjectCount: objects.Count,
            BehindTextCount: objects.Count(o => o.BehindText),
            InFrontCount: objects.Count(o => !o.BehindText),
            HasImages: objects.Any(o => o.Kind == DocumentFloatingObjectKind.Image),
            HasShapes: objects.Any(o => o.Kind == DocumentFloatingObjectKind.Shape),
            HasCharts: objects.Any(o => o.Kind == DocumentFloatingObjectKind.Chart),
            HasSmartArt: objects.Any(o => o.Kind == DocumentFloatingObjectKind.SmartArt),
            HasWordArt: objects.Any(o => o.Kind == DocumentFloatingObjectKind.WordArt),
            HasGroups: objects.Any(o => o.Kind == DocumentFloatingObjectKind.Group),
            HasSquareWrap: objects.Any(o => o.Wrapping == ImageWrapping.Square),
            HasTopAndBottomWrap: objects.Any(o => o.Wrapping == ImageWrapping.TopAndBottom),
            HasZOrder: objects.Select(o => o.ZOrderIndex).Distinct().Count() > 1,
            Effects: BuildDrawingObjectEffectExpectation(document, objects),
            Objects: objects);
    }

    private static FreeWVisualDrawingObjectEffectExpectation BuildDrawingObjectEffectExpectation(
        TextDocument document,
        IReadOnlyList<DocumentFloatingObjectSnapshot> objects)
    {
        var summaries = new List<string>();
        var renderedGroupChildSummaries = new List<string>();
        var plannedGroupChildSummaries = new List<string>();
        var shapeEffectObjects = 0;
        var imageEffectObjects = 0;
        var wordArtEffectObjects = 0;
        var renderedGroupChildShapeEffectObjects = 0;
        var renderedGroupChildWordArtEffectObjects = 0;
        var plannedGroupChildShapeEffectObjects = 0;
        var plannedGroupChildWordArtEffectObjects = 0;
        var hasShadow = false;
        var hasGlow = false;
        var hasReflection = false;
        var hasSoftEdge = false;
        var hasBevel = false;
        var hasArtisticEffect = false;

        foreach (var snapshot in objects)
        {
            if (!TryGetRun(document, snapshot, out var run))
                continue;

            switch (snapshot.Kind)
            {
                case DocumentFloatingObjectKind.Image when run.Image is { } image:
                    AddImageEffects(image, summaries, ref imageEffectObjects, ref hasShadow, ref hasGlow,
                        ref hasReflection, ref hasSoftEdge, ref hasBevel, ref hasArtisticEffect);
                    break;
                case DocumentFloatingObjectKind.Shape when run.Shape is { } shape:
                    AddVisualEffects(
                        "Shape",
                        DrawingObjectVisualPlanner.BuildVisualPlan(shape, snapshot),
                        summaries,
                        ref shapeEffectObjects,
                        ref hasShadow,
                        ref hasGlow,
                        ref hasReflection,
                        ref hasSoftEdge,
                        ref hasBevel,
                        countAsWordArt: false,
                        wordArtEffectObjects: ref wordArtEffectObjects);
                    break;
                case DocumentFloatingObjectKind.WordArt when run.WordArt is { } wordArt:
                    AddVisualEffects(
                        "WordArt",
                        DrawingObjectVisualPlanner.BuildVisualPlan(wordArt, snapshot),
                        summaries,
                        ref shapeEffectObjects,
                        ref hasShadow,
                        ref hasGlow,
                        ref hasReflection,
                        ref hasSoftEdge,
                        ref hasBevel,
                        countAsWordArt: true,
                        wordArtEffectObjects: ref wordArtEffectObjects);
                    break;
                case DocumentFloatingObjectKind.Group when run.DrawingGroup is { } group:
                    AddGroupChildEffects(
                        DrawingObjectVisualPlanner.BuildVisualPlan(group, snapshot),
                        renderedGroupChildSummaries,
                        plannedGroupChildSummaries,
                        ref renderedGroupChildShapeEffectObjects,
                        ref renderedGroupChildWordArtEffectObjects,
                        ref plannedGroupChildShapeEffectObjects,
                        ref plannedGroupChildWordArtEffectObjects,
                        ref hasShadow,
                        ref hasGlow);
                    break;
            }
        }

        return new FreeWVisualDrawingObjectEffectExpectation(
            shapeEffectObjects + imageEffectObjects + wordArtEffectObjects,
            shapeEffectObjects,
            imageEffectObjects,
            wordArtEffectObjects,
            renderedGroupChildShapeEffectObjects + renderedGroupChildWordArtEffectObjects,
            renderedGroupChildShapeEffectObjects,
            renderedGroupChildWordArtEffectObjects,
            plannedGroupChildShapeEffectObjects + plannedGroupChildWordArtEffectObjects,
            plannedGroupChildShapeEffectObjects,
            plannedGroupChildWordArtEffectObjects,
            hasShadow,
            hasGlow,
            hasReflection,
            hasSoftEdge,
            hasBevel,
            hasArtisticEffect,
            summaries,
            renderedGroupChildSummaries,
            plannedGroupChildSummaries);
    }

    private static void AddImageEffects(
        InlineImage image,
        List<string> summaries,
        ref int imageEffectObjects,
        ref bool hasShadow,
        ref bool hasGlow,
        ref bool hasReflection,
        ref bool hasSoftEdge,
        ref bool hasBevel,
        ref bool hasArtisticEffect)
    {
        if (!image.HasEffects && !image.HasArtisticEffect)
            return;

        imageEffectObjects++;
        hasShadow |= image.ShadowPreset != 0;
        hasGlow |= image.GlowSizePt > 0;
        hasReflection |= image.ReflectionPreset != 0;
        hasSoftEdge |= image.SoftEdgePt > 0;
        hasBevel |= image.BevelPreset != 0;
        hasArtisticEffect |= image.HasArtisticEffect;

        var parts = new List<string>();
        if (image.ShadowPreset != 0) parts.Add("shadow");
        if (image.GlowSizePt > 0) parts.Add("glow");
        if (image.ReflectionPreset != 0) parts.Add("reflection");
        if (image.SoftEdgePt > 0) parts.Add("soft-edge");
        if (image.BevelPreset != 0) parts.Add("bevel");
        if (image.HasArtisticEffect) parts.Add("artistic:" + image.ArtisticEffect);
        summaries.Add("Image:" + string.Join("+", parts));
    }

    private static void AddVisualEffects(
        string source,
        DrawingObjectVisualPlan visual,
        List<string> summaries,
        ref int shapeEffectObjects,
        ref bool hasShadow,
        ref bool hasGlow,
        ref bool hasReflection,
        ref bool hasSoftEdge,
        ref bool hasBevel,
        bool countAsWordArt,
        ref int wordArtEffectObjects)
    {
        if (!visual.Effects.HasAny)
            return;

        if (countAsWordArt || visual.Kind == DrawingObjectVisualKind.WordArt)
            wordArtEffectObjects++;
        else
            shapeEffectObjects++;

        hasShadow |= visual.Effects.HasShadow;
        hasGlow |= visual.Effects.HasGlow;
        hasReflection |= visual.Effects.HasReflection;
        hasSoftEdge |= visual.Effects.HasSoftEdge;
        hasBevel |= visual.Effects.HasBevel;
        summaries.Add(source + ":" + visual.Effects.Summary.Replace(", ", "+", StringComparison.Ordinal));
    }

    private static void AddGroupChildEffects(
        DrawingObjectVisualPlan groupPlan,
        List<string> renderedGroupChildSummaries,
        List<string> plannedGroupChildSummaries,
        ref int renderedGroupChildShapeEffectObjects,
        ref int renderedGroupChildWordArtEffectObjects,
        ref int plannedGroupChildShapeEffectObjects,
        ref int plannedGroupChildWordArtEffectObjects,
        ref bool hasShadow,
        ref bool hasGlow)
    {
        foreach (var child in groupPlan.GroupChildren)
        {
            if (!child.Visual.Effects.HasAny)
                continue;

            var summary = BuildGroupChildEffectSummary(child);
            if (IsRenderedGroupChildEffect(child.Visual))
            {
                if (child.Visual.Kind == DrawingObjectVisualKind.WordArt)
                    renderedGroupChildWordArtEffectObjects++;
                else
                    renderedGroupChildShapeEffectObjects++;

                hasShadow |= child.Visual.Effects.HasShadow;
                hasGlow |= child.Visual.Effects.HasGlow;
                renderedGroupChildSummaries.Add(summary);
            }
            else
            {
                if (child.Visual.Kind == DrawingObjectVisualKind.WordArt)
                    plannedGroupChildWordArtEffectObjects++;
                else
                    plannedGroupChildShapeEffectObjects++;

                plannedGroupChildSummaries.Add(summary);
            }
        }
    }

    private static bool IsRenderedGroupChildEffect(DrawingObjectVisualPlan visual) =>
        visual.Kind == DrawingObjectVisualKind.Shape
        && (visual.Effects.HasGlow || visual.Effects.HasShadow);

    private static string BuildGroupChildEffectSummary(DrawingObjectGroupChildVisualPlan child) =>
        "GroupChild"
        + child.ChildIndex.ToString(CultureInfo.InvariantCulture)
        + ":"
        + child.Visual.Kind
        + ":"
        + child.Visual.Effects.Summary.Replace(", ", "+", StringComparison.Ordinal);

    private static bool TryGetRun(
        TextDocument document,
        DocumentFloatingObjectSnapshot snapshot,
        out Run run)
    {
        run = null!;
        if (snapshot.BlockIndex < 0 || snapshot.BlockIndex >= document.Blocks.Count)
            return false;
        if (document.Blocks[snapshot.BlockIndex] is not Paragraph paragraph)
            return false;
        if (snapshot.RunIndex < 0 || snapshot.RunIndex >= paragraph.Runs.Count)
            return false;

        run = paragraph.Runs[snapshot.RunIndex];
        return true;
    }

    public static FreeWVisualChartSmartArtExpectation BuildChartSmartArtExpectation(TextDocument? document)
    {
        if (document is null)
            return EmptyChartSmartArtExpectation;

        var chartModels = EnumerateRuns(document)
            .Select(run => run.Chart)
            .Where(chart => chart is not null)
            .Cast<Chart>()
            .ToList();
        var smartArtModels = EnumerateRuns(document)
            .Select(run => run.SmartArt)
            .Where(smartArt => smartArt is not null)
            .Cast<SmartArt>()
            .ToList();

        if (chartModels.Count == 0 && smartArtModels.Count == 0)
            return EmptyChartSmartArtExpectation;

        var charts = chartModels
            .Select(ChartSmartArtVisualPlanner.BuildChartPlan)
            .ToList();
        var smartArts = smartArtModels
            .Select(ChartSmartArtVisualPlanner.BuildSmartArtPlan)
            .ToList();
        var smartArtNodeCount = smartArts.Sum(plan => plan.Nodes.Count);

        return new FreeWVisualChartSmartArtExpectation(
            ChartCount: charts.Count,
            SmartArtCount: smartArts.Count,
            HasChartPalette: charts.Any(plan => plan.PaletteHex.Count > 0),
            HasChartQuickLayout: chartModels.Any(chart => chart.QuickLayoutId > 0),
            HasMarkerOnlyScatter: charts.Any(plan => plan.GeometryKind == ChartVisualGeometryKind.MarkerOnly),
            HasLegend: charts.Any(plan => plan.ShowLegend),
            HasGridlines: charts.Any(plan => plan.ShowGridlines),
            HasDataLabels: charts.Any(plan => plan.ShowDataLabels),
            HasAxisTitles: charts.Any(plan => plan.ShowAxisTitles),
            HasPlotAreaFill: charts.Any(plan => plan.PlotAreaFill),
            HasSmartArtLayout: smartArts.Any(plan => !string.IsNullOrWhiteSpace(plan.LayoutId)),
            HasSmartArtColorScheme: smartArts.Any(plan => !string.IsNullOrWhiteSpace(plan.ColorScheme.Id)),
            HasSmartArtStyle: smartArts.Any(plan => !string.IsNullOrWhiteSpace(plan.Style.Id)),
            SmartArtNodeCount: smartArtNodeCount,
            DistinctSmartArtFillCount: smartArts
                .SelectMany(plan => plan.Nodes.Select(node => node.FillHex))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            Charts: charts,
            SmartArts: smartArts);
    }

    public static FreeWVisualFieldExpectation BuildFieldExpectation(TextDocument? document)
    {
        if (document is null)
            return EmptyFieldExpectation;

        var fieldRuns = EnumerateFieldRunSnapshots(document).ToList();
        if (fieldRuns.Count == 0)
            return EmptyFieldExpectation;

        var simpleRuns = fieldRuns
            .Where(item => item.Run.FieldKind != RunFieldKind.None)
            .ToList();
        var complexRuns = fieldRuns
            .Where(item => item.Run.ComplexField is not null)
            .ToList();
        var complexKeywords = complexRuns
            .Select(item => item.Run.ComplexField!.Keyword)
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(keyword => keyword, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var fieldKinds = simpleRuns
            .Select(item => item.Run.FieldKind.ToString())
            .Concat(complexKeywords.Select(keyword => "Complex:" + keyword))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var pageFieldCount = simpleRuns.Count(item => item.Run.FieldKind == RunFieldKind.PageNumber)
            + complexRuns.Count(item => string.Equals(item.Run.ComplexField!.Keyword, "PAGE", StringComparison.OrdinalIgnoreCase));
        var numPagesFieldCount = simpleRuns.Count(item => item.Run.FieldKind == RunFieldKind.NumPages)
            + complexRuns.Count(item => string.Equals(item.Run.ComplexField!.Keyword, "NUMPAGES", StringComparison.OrdinalIgnoreCase));
        var documentPropertyFieldCount = simpleRuns.Count(item => DocumentPropertyFieldKinds.Contains(item.Run.FieldKind))
            + complexRuns.Count(item => IsDocumentPropertyFieldKeyword(item.Run.ComplexField!.Keyword));

        return new FreeWVisualFieldExpectation(
            SimpleFieldCount: simpleRuns.Count,
            ComplexFieldCount: complexRuns.Count,
            BodyFieldCount: fieldRuns.Count(item => !item.HeaderFooter),
            HeaderFooterFieldCount: fieldRuns.Count(item => item.HeaderFooter),
            PageFieldCount: pageFieldCount,
            NumPagesFieldCount: numPagesFieldCount,
            DocumentPropertyFieldCount: documentPropertyFieldCount,
            HasPageFields: pageFieldCount > 0,
            HasNumPagesFields: numPagesFieldCount > 0,
            HasDocumentPropertyFields: documentPropertyFieldCount > 0,
            HasComplexFields: complexRuns.Count > 0,
            HasComplexResultFields: complexRuns.Any(item => !string.IsNullOrWhiteSpace(item.Run.Text)),
            HasHeaderFooterFields: fieldRuns.Any(item => item.HeaderFooter),
            FieldKinds: fieldKinds,
            ComplexFieldKeywords: complexKeywords,
            HeaderFooterSlotNames: fieldRuns
                .Where(item => item.HeaderFooter && !string.IsNullOrWhiteSpace(item.HeaderFooterSlotName))
                .Select(item => item.HeaderFooterSlotName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(slot => slot, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    public static FreeWVisualTableOfAuthoritiesExpectation BuildTableOfAuthoritiesExpectation(TextDocument? document)
    {
        if (document is null)
            return EmptyTableOfAuthoritiesExpectation;

        var entryCount = 0;
        var pageReferences = new List<FreeWVisualTableOfAuthoritiesPageReference>();
        var currentCategory = string.Empty;
        foreach (var paragraph in document.Blocks.OfType<Paragraph>())
        {
            if (string.Equals(paragraph.StyleId, TableOfAuthorities.CategoryStyleId, StringComparison.Ordinal))
            {
                currentCategory = paragraph.PlainText.Trim();
                continue;
            }

            if (!string.Equals(paragraph.StyleId, TableOfAuthorities.EntryStyleId, StringComparison.Ordinal))
                continue;

            entryCount++;
            var extracted = ExtractTableOfAuthoritiesPageReference(paragraph);
            if (extracted is null)
                continue;

            var pageNumbers = ParsePageNumbers(extracted.Value.PageReferenceText);
            pageReferences.Add(new FreeWVisualTableOfAuthoritiesPageReference(
                string.IsNullOrWhiteSpace(currentCategory) ? "Uncategorized" : currentCategory,
                extracted.Value.EntryText,
                extracted.Value.PageReferenceText,
                pageNumbers));
        }

        return new FreeWVisualTableOfAuthoritiesExpectation(
            EntryCount: entryCount,
            EntryWithPageReferenceCount: pageReferences.Count,
            HasGeneratedTable: entryCount > 0,
            HasPageReferences: pageReferences.Count > 0,
            HasExplicitPageNumbers: pageReferences.Any(reference => reference.PageNumbers.Count > 0),
            HasPassimReferences: pageReferences.Any(reference =>
                string.Equals(reference.PageReferenceText, "passim", StringComparison.OrdinalIgnoreCase)),
            PageReferences: pageReferences);
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

    private static FreeWVisualTableExpectation EmptyTableExpectation { get; } = new(
        TableCount: 0,
        TotalRows: 0,
        TotalCells: 0,
        MaxGridColumnCount: 0,
        EstimatedPageCount: 0,
        HasHeaderRow: false,
        RepeatsHeaderRow: false,
        HasPaginationPlan: false,
        HasMultiPageTables: false,
        HasRepeatedHeaderPages: false,
        HasKeepTogetherRows: false,
        HasBandedRows: false,
        HasBandedColumns: false,
        HasMergedCells: false,
        HasVerticalMerges: false,
        HasCellShading: false,
        HasCustomCellBorders: false,
        HasCellMargins: false,
        HasCellSpacing: false,
        HasVerticalText: false,
        HasVerticalAlignment: false,
        HasPreferredWidths: false,
        HasNamedStyle: false,
        HasFloatingTextWrap: false,
        Tables: [],
        PaginationPlans: []);

    private static FreeWVisualDrawingObjectExpectation EmptyDrawingObjectExpectation { get; } = new(
        FloatingObjectCount: 0,
        BehindTextCount: 0,
        InFrontCount: 0,
        HasImages: false,
        HasShapes: false,
        HasCharts: false,
        HasSmartArt: false,
        HasWordArt: false,
        HasGroups: false,
        HasSquareWrap: false,
        HasTopAndBottomWrap: false,
        HasZOrder: false,
        Effects: new FreeWVisualDrawingObjectEffectExpectation(
            EffectObjectCount: 0,
            ShapeEffectObjectCount: 0,
            ImageEffectObjectCount: 0,
            WordArtEffectObjectCount: 0,
            RenderedGroupChildEffectObjectCount: 0,
            RenderedGroupChildShapeEffectObjectCount: 0,
            RenderedGroupChildWordArtEffectObjectCount: 0,
            PlannedGroupChildEffectObjectCount: 0,
            PlannedGroupChildShapeEffectObjectCount: 0,
            PlannedGroupChildWordArtEffectObjectCount: 0,
            HasShadow: false,
            HasGlow: false,
            HasReflection: false,
            HasSoftEdge: false,
            HasBevel: false,
            HasArtisticEffect: false,
            EffectSummaries: [],
            RenderedGroupChildEffectSummaries: [],
            PlannedGroupChildEffectSummaries: []),
        Objects: []);

    private static FreeWVisualChartSmartArtExpectation EmptyChartSmartArtExpectation { get; } = new(
        ChartCount: 0,
        SmartArtCount: 0,
        HasChartPalette: false,
        HasChartQuickLayout: false,
        HasMarkerOnlyScatter: false,
        HasLegend: false,
        HasGridlines: false,
        HasDataLabels: false,
        HasAxisTitles: false,
        HasPlotAreaFill: false,
        HasSmartArtLayout: false,
        HasSmartArtColorScheme: false,
        HasSmartArtStyle: false,
        SmartArtNodeCount: 0,
        DistinctSmartArtFillCount: 0,
        Charts: [],
        SmartArts: []);

    private static FreeWVisualFieldExpectation EmptyFieldExpectation { get; } = new(
        SimpleFieldCount: 0,
        ComplexFieldCount: 0,
        BodyFieldCount: 0,
        HeaderFooterFieldCount: 0,
        PageFieldCount: 0,
        NumPagesFieldCount: 0,
        DocumentPropertyFieldCount: 0,
        HasPageFields: false,
        HasNumPagesFields: false,
        HasDocumentPropertyFields: false,
        HasComplexFields: false,
        HasComplexResultFields: false,
        HasHeaderFooterFields: false,
        FieldKinds: [],
        ComplexFieldKeywords: [],
        HeaderFooterSlotNames: []);

    private static FreeWVisualTableOfAuthoritiesExpectation EmptyTableOfAuthoritiesExpectation { get; } = new(
        EntryCount: 0,
        EntryWithPageReferenceCount: 0,
        HasGeneratedTable: false,
        HasPageReferences: false,
        HasExplicitPageNumbers: false,
        HasPassimReferences: false,
        PageReferences: []);

    private static IEnumerable<(Run Run, bool HeaderFooter, string? HeaderFooterSlotName)> EnumerateFieldRunSnapshots(
        TextDocument document)
    {
        foreach (var run in EnumerateRuns(document))
        {
            if (IsFieldRun(run))
                yield return (run, false, null);
        }

        foreach (var (slotName, headerFooter) in EnumerateHeaderFooterSlots(document))
        {
            foreach (var paragraph in headerFooter.Paragraphs)
            {
                foreach (var run in paragraph.Runs)
                {
                    if (IsFieldRun(run))
                        yield return (run, true, slotName);
                }
            }
        }
    }

    private static IEnumerable<(string SlotName, HeaderFooter HeaderFooter)> EnumerateHeaderFooterSlots(
        TextDocument document)
    {
        var seen = new HashSet<SectionHeadersFooters>();
        foreach (var section in document.Sections)
        {
            if (!seen.Add(section.HeadersFooters))
                continue;

            foreach (var item in EnumerateHeaderFooterSlots(section.HeadersFooters))
                yield return item;
        }
    }

    private static IEnumerable<(string SlotName, HeaderFooter HeaderFooter)> EnumerateHeaderFooterSlots(
        SectionHeadersFooters headersFooters)
    {
        if (headersFooters.Header is { IsEmpty: false } header)
            yield return ("header", header);
        if (headersFooters.Footer is { IsEmpty: false } footer)
            yield return ("footer", footer);
        if (headersFooters.FirstHeader is { IsEmpty: false } firstHeader)
            yield return ("first-header", firstHeader);
        if (headersFooters.FirstFooter is { IsEmpty: false } firstFooter)
            yield return ("first-footer", firstFooter);
        if (headersFooters.EvenHeader is { IsEmpty: false } evenHeader)
            yield return ("even-header", evenHeader);
        if (headersFooters.EvenFooter is { IsEmpty: false } evenFooter)
            yield return ("even-footer", evenFooter);
    }

    private static bool IsFieldRun(Run run) =>
        run.FieldKind != RunFieldKind.None || run.ComplexField is not null;

    private static bool IsDocumentPropertyFieldKeyword(string keyword) =>
        keyword.Equals("AUTHOR", StringComparison.OrdinalIgnoreCase)
        || keyword.Equals("TITLE", StringComparison.OrdinalIgnoreCase)
        || keyword.Equals("SUBJECT", StringComparison.OrdinalIgnoreCase)
        || keyword.Equals("KEYWORDS", StringComparison.OrdinalIgnoreCase)
        || keyword.Equals("COMMENTS", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<Run> EnumerateRuns(TextDocument document)
    {
        foreach (var paragraph in EnumerateParagraphs(document))
        {
            foreach (var run in paragraph.Runs)
                yield return run;
        }
    }

    private static IEnumerable<Paragraph> EnumerateParagraphs(TextDocument document)
    {
        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    yield return paragraph;
                    break;
                case Table table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            foreach (var paragraph in cell.Paragraphs)
                                yield return paragraph;
                        }
                    }
                    break;
            }
        }
    }

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

    private static (string EntryText, string PageReferenceText)? ExtractTableOfAuthoritiesPageReference(
        Paragraph paragraph)
    {
        if (paragraph.Runs.Count > 0)
        {
            var before = new List<string>();
            for (var i = 0; i < paragraph.Runs.Count; i++)
            {
                var text = paragraph.Runs[i].Text ?? string.Empty;
                var tabIndex = text.IndexOf('\t', StringComparison.Ordinal);
                if (tabIndex < 0)
                {
                    before.Add(text);
                    continue;
                }

                var entryText = string.Concat(before) + text[..tabIndex];
                var pageReferenceText = text[(tabIndex + 1)..] + string.Concat(
                    paragraph.Runs
                        .Skip(i + 1)
                        .Select(run => run.Text ?? string.Empty));
                return NormalizeTableOfAuthoritiesPageReference(entryText, pageReferenceText);
            }
        }

        var plainText = paragraph.PlainText;
        var plainTabIndex = plainText.LastIndexOf('\t');
        return plainTabIndex < 0
            ? null
            : NormalizeTableOfAuthoritiesPageReference(
                plainText[..plainTabIndex],
                plainText[(plainTabIndex + 1)..]);
    }

    private static (string EntryText, string PageReferenceText)? NormalizeTableOfAuthoritiesPageReference(
        string entryText,
        string pageReferenceText)
    {
        var entry = entryText.Trim();
        var reference = pageReferenceText.Trim();
        return string.IsNullOrWhiteSpace(entry) || string.IsNullOrWhiteSpace(reference)
            ? null
            : (entry, reference);
    }

    private static IReadOnlyList<int> ParsePageNumbers(string pageReferenceText) =>
        pageReferenceText
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page)
                ? Math.Max(1, page)
                : 0)
            .Where(page => page > 0)
            .Distinct()
            .OrderBy(page => page)
            .ToList();

    private static TextDocument BuildSectionGeometrySurfaceDocument(
        TextDocument source,
        PageSettings page,
        IReadOnlyList<int> sourceBlockIndexes)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        CopyDocumentShell(source, document);
        CopyPageSettings(page, document.Page);

        foreach (var blockIndex in sourceBlockIndexes)
        {
            if (blockIndex >= 0 && blockIndex < source.Blocks.Count)
                document.Blocks.Add(DocumentMerge.CloneBlock(source.Blocks[blockIndex]));
        }

        if (document.Blocks.Count == 0)
            document.Blocks.Add(new Paragraph());

        return document;
    }

    private static void CopyDocumentShell(TextDocument source, TextDocument target)
    {
        target.DefaultRun = source.DefaultRun;
        target.DefaultParagraph = source.DefaultParagraph;
        target.Styles.Clear();
        foreach (var (id, style) in source.Styles)
            target.Styles[id] = style;
    }

    private static void CopyPageSettings(PageSettings source, PageSettings target)
    {
        var copy = source.Clone();
        target.WidthPt = copy.WidthPt;
        target.HeightPt = copy.HeightPt;
        target.MarginLeftPt = copy.MarginLeftPt;
        target.MarginRightPt = copy.MarginRightPt;
        target.MarginTopPt = copy.MarginTopPt;
        target.MarginBottomPt = copy.MarginBottomPt;
        target.Landscape = copy.Landscape;
        target.GutterPt = copy.GutterPt;
        target.HeaderDistancePt = copy.HeaderDistancePt;
        target.FooterDistancePt = copy.FooterDistancePt;
        target.MirrorMargins = copy.MirrorMargins;
        target.ColumnCount = copy.ColumnCount;
        target.ColumnSpacingPt = copy.ColumnSpacingPt;
        target.ColumnsLineBetween = copy.ColumnsLineBetween;
        target.ColumnWidthsPt = copy.ColumnWidthsPt is null ? null : new List<double>(copy.ColumnWidthsPt);
        target.PageBorder = copy.PageBorder;
        target.Watermark = copy.Watermark;
        target.WatermarkOptions = PageSettings.CloneWatermarkOptions(copy.WatermarkOptions);
        target.LineNumberMode = copy.LineNumberMode;
        target.LineNumberCountBy = copy.LineNumberCountBy;
        target.LineNumberStartAt = copy.LineNumberStartAt;
        target.AutoHyphenation = copy.AutoHyphenation;
        target.HyphenationZonePt = copy.HyphenationZonePt;
        target.ConsecutiveHyphenLimit = copy.ConsecutiveHyphenLimit;
        target.DoNotHyphenateCaps = copy.DoNotHyphenateCaps;
        target.DefaultTabStopPt = copy.DefaultTabStopPt;
        target.VerticalAlignment = copy.VerticalAlignment;
        target.DifferentFirstPage = copy.DifferentFirstPage;
        target.DifferentOddEvenPages = copy.DifferentOddEvenPages;
        target.BackgroundColorHex = copy.BackgroundColorHex;
    }

    private static bool PageSettingsMatch(PageSettings left, PageSettings right) =>
        Math.Abs(left.WidthPt - right.WidthPt) < 0.001
        && Math.Abs(left.HeightPt - right.HeightPt) < 0.001
        && Math.Abs(left.MarginLeftPt - right.MarginLeftPt) < 0.001
        && Math.Abs(left.MarginTopPt - right.MarginTopPt) < 0.001
        && Math.Abs(left.MarginRightPt - right.MarginRightPt) < 0.001
        && Math.Abs(left.MarginBottomPt - right.MarginBottomPt) < 0.001
        && left.Landscape == right.Landscape;

    private static int[] BuildSectionBreakPageAssignments(TextDocument document, int pageCount)
    {
        var assignments = new int[document.Blocks.Count];
        var pageIndex = 0;
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            assignments[blockIndex] = Math.Clamp(pageIndex, 0, Math.Max(0, pageCount - 1));
            if (document.Blocks[blockIndex] is Paragraph { SectionBreak: { } section }
                && section.BreakKind is SectionBreakKind.NextPage
                    or SectionBreakKind.EvenPage
                    or SectionBreakKind.OddPage)
            {
                pageIndex++;
            }
        }

        return assignments;
    }
}
