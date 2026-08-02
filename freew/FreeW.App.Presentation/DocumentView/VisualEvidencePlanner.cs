using System.Globalization;
using System.Text.Json;
using FreeW.App.Presentation.Dialogs;
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
    IReadOnlyList<string> TableCellFillSignatures,
    IReadOnlyList<DocumentTableLayoutPlan> Tables,
    IReadOnlyList<DocumentTablePaginationPlan> PaginationPlans);

public sealed record FreeWVisualDrawingObjectGroupChildExpectation(
    int ChildCount,
    int ImageChildCount,
    int ShapeChildCount,
    int ChartChildCount,
    int SmartArtChildCount,
    int WordArtChildCount,
    IReadOnlyList<string> ChildKindSummaries,
    IReadOnlyList<string> ChildVisualSignatures)
{
    public bool HasMixedTypedChildren => ImageChildCount > 0 && ChartChildCount > 0 && SmartArtChildCount > 0;
}

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
    int AltTextObjectCount,
    IReadOnlyList<string> AltTextSummaries,
    FreeWVisualDrawingObjectGroupChildExpectation GroupChildren,
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
    IReadOnlyList<string> ChartVisualSignatures,
    IReadOnlyList<string> ChartDataSignatures,
    IReadOnlyList<string> SmartArtVisualSignatures,
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
    IReadOnlyList<string> ComplexFieldResultSignatures,
    IReadOnlyList<string> HeaderFooterSlotNames,
    IReadOnlyList<string> HeaderFooterResolvedFieldSignatures);

public sealed record FreeWVisualTableOfAuthoritiesPageReference(
    string Category,
    string EntryText,
    string PageReferenceText,
    IReadOnlyList<int> PageNumbers,
    IReadOnlyList<string> DisplayedPageReferences,
    string PageReferenceKind,
    bool HasPageReferenceSentinel,
    string StableSignature);

public sealed record FreeWVisualTableOfAuthoritiesExpectation(
    int EntryCount,
    int EntryWithPageReferenceCount,
    int CategoryCount,
    IReadOnlyList<string> Categories,
    bool HasGeneratedTable,
    bool HasPageReferences,
    bool HasExplicitPageNumbers,
    bool HasPassimReferences,
    IReadOnlyList<string> PageReferenceSignatures,
    IReadOnlyList<FreeWVisualTableOfAuthoritiesPageReference> PageReferences);

public sealed record FreeWVisualProofingDiagnosticSignature(
    string Kind,
    string Word,
    string NormalizedWord,
    string? LanguageTag,
    int BlockIndex,
    int RunIndex,
    int RunOffset,
    int ParagraphOffset,
    int Length,
    string StableSignature);

public sealed record FreeWVisualProofingAdornmentExpectation(
    string DiagnosticStableSignature,
    string Kind,
    string AdornmentKind,
    string UnderlineStyle,
    string ColorHex,
    int BlockIndex,
    int RunIndex,
    int RunOffset,
    int ParagraphStartOffset,
    int ParagraphEndOffset,
    int Length,
    string StableSignature);

public sealed record FreeWVisualProofingDiagnosticExpectation(
    int DiagnosticCount,
    int SpellingCount,
    int GrammarCount,
    bool HasSpelling,
    bool HasGrammar,
    IReadOnlyList<string> Kinds,
    IReadOnlyList<string> LanguageTags,
    IReadOnlyList<string> StableSignatures,
    IReadOnlyList<FreeWVisualProofingDiagnosticSignature> Diagnostics,
    int AdornmentCount,
    int SpellingAdornmentCount,
    int GrammarAdornmentCount,
    bool HasSpellingUnderline,
    bool HasGrammarUnderline,
    IReadOnlyList<string> AdornmentStableSignatures,
    IReadOnlyList<FreeWVisualProofingAdornmentExpectation> Adornments);

public sealed record FreeWVisualProtectionOperationExpectation(
    string Operation,
    string MutationKind,
    bool IsAllowed,
    bool RequiresTrackedChanges,
    string BlockReason,
    string ProtectionMode,
    string StableSignature);

public sealed record FreeWVisualReviewProtectionExpectation(
    string ProtectionMode,
    bool IsProtected,
    bool IsMarkedAsFinal,
    ReviewProtectionCommandState MarkAsFinal,
    ReviewProtectionCommandState RestrictEditing,
    bool IsBodyEditingLocked,
    bool IsBodyFormattingLocked,
    bool IsCommentWorkflowAllowed,
    bool IsHistoryLocked,
    bool ShouldForceTrackChanges,
    IReadOnlyList<FreeWVisualProtectionOperationExpectation> Operations,
    IReadOnlyList<string> StableSignatures);

public sealed record FreeWVisualReviewMarkupExpectation(
    int RevisionCount,
    int InsertionCount,
    int DeletionCount,
    int FormattingRevisionCount,
    int AuthorCount,
    int CommentCount,
    int ReplyCount,
    int ResolvedCommentCount,
    int CommentAnchorCount,
    int CommentReferenceCount,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> RevisionStableSignatures,
    IReadOnlyList<string> CommentStableSignatures)
{
    public static FreeWVisualReviewMarkupExpectation Empty { get; } = new(
        RevisionCount: 0,
        InsertionCount: 0,
        DeletionCount: 0,
        FormattingRevisionCount: 0,
        AuthorCount: 0,
        CommentCount: 0,
        ReplyCount: 0,
        ResolvedCommentCount: 0,
        CommentAnchorCount: 0,
        CommentReferenceCount: 0,
        Authors: [],
        RevisionStableSignatures: [],
        CommentStableSignatures: []);
}

public sealed record FreeWVisualReviewCompareCombineExpectation(
    string Operation,
    int RevisionCount,
    int InsertionCount,
    int DeletionCount,
    int FormattingCount,
    int AuthorCount,
    int PreservedPartCount,
    int PreservedContentTypeDefaultCount,
    bool HasPreservedSettings,
    bool HasPreservedCustomProperties,
    bool HasRetainedModelSafety,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> StableSignatures,
    IReadOnlyList<string> RetainedModelSafetySignatures,
    bool HasCompareSemantics,
    bool HasCombineSemantics)
{
    public static FreeWVisualReviewCompareCombineExpectation Empty { get; } = new(
        Operation: "none",
        RevisionCount: 0,
        InsertionCount: 0,
        DeletionCount: 0,
        FormattingCount: 0,
        AuthorCount: 0,
        PreservedPartCount: 0,
        PreservedContentTypeDefaultCount: 0,
        HasPreservedSettings: false,
        HasPreservedCustomProperties: false,
        HasRetainedModelSafety: false,
        Authors: [],
        StableSignatures: [],
        RetainedModelSafetySignatures: [],
        HasCompareSemantics: false,
        HasCombineSemantics: false);
}

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
    FreeWVisualHeaderFooterExpectation HeaderFooters,
    FreeWVisualTableOfAuthoritiesExpectation TableOfAuthorities,
    FreeWVisualProofingDiagnosticExpectation ProofingDiagnostics,
    FreeWVisualReviewProtectionExpectation ReviewProtection,
    FreeWVisualReviewMarkupExpectation ReviewMarkup,
    string? HeaderSlotName,
    string? FooterSlotName,
    bool HasFootnotes,
    bool HasEndnotes,
    bool IsSyntheticPage)
{
    public FreeWVisualEquationExpectation Equations { get; init; } = FreeWVisualEquationExpectation.Empty;
    public FreeWVisualReviewCompareCombineExpectation ReviewCompareCombine { get; init; } =
        FreeWVisualReviewCompareCombineExpectation.Empty;
}

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
    public const int SchemaVersion = 24;
    public const string SectionGeometryPageSurfaceRenderStatus = "section-page-surface";

    private const int MaxTrackedColorCount = 4096;
    private const string ProofingUnderlineStyle = "wavy";
    private const string SpellingAdornmentKind = "spelling-squiggle";
    private const string GrammarAdornmentKind = "grammar-squiggle";
    private const string SpellingAdornmentColorHex = "#D13438";
    private const string GrammarAdornmentColorHex = "#2B579A";

    private static readonly RunFieldKind[] DocumentPropertyFieldKinds =
    [
        RunFieldKind.Author,
        RunFieldKind.Title,
        RunFieldKind.Subject,
        RunFieldKind.Keywords,
        RunFieldKind.DocComments
    ];

    private static readonly RestrictEditingEvidenceOperation[] ReviewProtectionEvidenceOperations =
    [
        new(RestrictEditingOperationKind.BodyTextEdit),
        new(RestrictEditingOperationKind.BodyTextDelete),
        new(RestrictEditingOperationKind.BodyFormatting),
        new(RestrictEditingOperationKind.ProofingReplacement),
        new(RestrictEditingOperationKind.HistoryUndo, DocumentCommandMutationKind.BodyText),
        new(RestrictEditingOperationKind.HistoryRedo, DocumentCommandMutationKind.BodyFormatting),
        new(RestrictEditingOperationKind.CommentInsert),
        new(RestrictEditingOperationKind.CommentReply),
        new(RestrictEditingOperationKind.CommentResolve),
        new(RestrictEditingOperationKind.CommentDelete),
        new(RestrictEditingOperationKind.HistoryUndo, DocumentCommandMutationKind.Comment),
        new(RestrictEditingOperationKind.HistoryRedo, DocumentCommandMutationKind.Comment)
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
            "f2-hf-images",
            "F2 multi-section header image page composition.",
            ["f2", "page-composition", "print-layout", "header-footer", "header-footer-images", "multi-section", "body-text"],
            "f2-hf-images_p{page}.png",
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
                "resolved-header-footer-field-text",
                "chapter-prefixed-page-number-fields",
                "page-composition",
                "print-layout",
                "header-footer",
                "multi-page",
                "body-text"
            ],
            "field-page-number-variants_p{page}.png",
            4,
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
            "legal-reference-section-page-numbers",
            "Legal/reference Table of Authorities evidence with section-formatted displayed page references.",
            [
                "references",
                "toa-fields",
                "cached-toa-page-number-sentinel",
                "generated-toa-page-references",
                "section-formatted-page-numbers",
                "legal-authorities",
                "generated-table-of-authorities",
                "page-composition",
                "print-layout",
                "multi-section",
                "multi-page",
                "body-text"
            ],
            "legal-reference-section-page-numbers_p{page}.png",
            2,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition),
        new(
            "equation-structures",
            "OfficeMath equation visual structure evidence for shared WPF and Avalonia rendering.",
            [
                "equations",
                "officemath",
                "math-run-structures",
                "shared-equation-visual-planner",
                "scripts",
                "fractions",
                "radicals",
                "n-ary-operators",
                "matrices",
                "equation-arrays",
                "accents",
                "bars",
                "delimiters",
                "group-characters",
                "function-apply",
                "page-composition",
                "print-layout",
                "body-text"
            ],
            "equation-structures_p{page}.png",
            1,
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
            ["f2", "page-composition", "print-layout", "endnotes", "final-body-page", "body-text"],
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
            "f2-01-float-wrap",
            "Floating image square/tight wrap visual fidelity capture.",
            [
                "f2",
                "page-composition",
                "print-layout",
                "floating-image",
                "floating-objects",
                "square-wrap",
                "tight-wrap",
                "text-wrap-around",
                "body-text"
            ],
            "f2-01-float-wrap_p{page}.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsFloatingObjects = true }),
        new(
            "review-proofing-visual-depth",
            "Shared Review and Proofing visual depth composition.",
            [
                "review",
                "proofing",
                "print-layout",
                "tracked-changes",
                "revision-marks",
                "format-revisions",
                "comments",
                "comment-anchors",
                "comment-replies",
                "resolved-comments",
                "table-comment-anchors",
                "proofing-language",
                "proofing-diagnostics",
                "proofing-adornments",
                "proofing-underline-intent",
                "body-text"
            ],
            "review-proofing-visual-depth_p{page}.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsTrackedChanges = true, ExpectsComments = true }),
        new(
            "review-protection-proofing-comments-only",
            "Shared Review proofing and CommentsOnly protection command evidence.",
            [
                "review",
                "proofing",
                "protection",
                "restrict-editing",
                "comments-only-protection",
                "marked-as-final",
                "final-advisory-read-only",
                "review-protection-state",
                "protection-command-matrix",
                "proofing-replacement-blocked",
                "body-edit-blocked",
                "body-formatting-blocked",
                "history-blocked",
                "comment-workflow-blocked",
                "print-layout",
                "tracked-changes",
                "revision-marks",
                "format-revisions",
                "comments",
                "comment-anchors",
                "comment-replies",
                "resolved-comments",
                "table-comment-anchors",
                "proofing-language",
                "proofing-diagnostics",
                "proofing-adornments",
                "proofing-underline-intent",
                "body-text"
            ],
            "review-protection-proofing-comments-only_p{page}.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsTrackedChanges = true, ExpectsComments = true }),
        new(
            "review-compare-visual-proof",
            "Shared Review Compare blackline visual proof evidence.",
            [
                "review",
                "compare",
                "document-compare",
                "compare-result",
                "tracked-changes",
                "revision-marks",
                "compare-semantics",
                "compare-authorship",
                "print-layout",
                "body-text"
            ],
            "review-compare-visual-proof_p{page}.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsTrackedChanges = true }),
        new(
            "review-combine-visual-proof",
            "Shared Review Combine multi-author blackline visual proof evidence.",
            [
                "review",
                "combine",
                "document-combine",
                "combine-result",
                "tracked-changes",
                "revision-marks",
                "combine-semantics",
                "multi-author-revisions",
                "compare-authorship",
                "print-layout",
                "body-text"
            ],
            "review-combine-visual-proof_p{page}.png",
            1,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with { ExpectsTrackedChanges = true }),
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
                "table-fill-signatures",
                "style-derived-header-fill",
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
            "table-page-composition-stress",
            "Three-page table pagination and page-composition fidelity capture.",
            [
                "table-layout",
                "table-pagination",
                "tables",
                "repeat-header-row",
                "keep-rows",
                "banded-rows",
                "cell-shading",
                "table-fill-signatures",
                "style-derived-header-fill",
                "cell-borders",
                "cell-margins",
                "cell-spacing",
                "named-table-style",
                "fields",
                "page-number-fields",
                "numpages-fields",
                "header-footer-fields",
                "caption",
                "footnotes",
                "page-composition",
                "print-layout",
                "header-footer",
                "page-border",
                "watermark",
                "body-text"
            ],
            "table-page-composition-stress_p{page}.png",
            3,
            DocumentViewLayoutKind.PrintLayout,
            BodyPrintComposition with
            {
                ExpectsTables = true,
                ExpectsHeadersFooters = true,
                ExpectsPageBorder = true,
                ExpectsWatermark = true
            }),
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
                "chart-visual-signature",
                "chart-data",
                "smartart-layout",
                "smartart-colors",
                "smartart-style",
                "smartart-node-fills",
                "smartart-polygon-geometry",
                "smartart-visual-signature"
            ],
            "chart-smartart-complex_p{page}.png",
            2,
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
                "grouped-child-wordart-effects",
                "grouped-mixed-children",
                "grouped-child-images",
                "grouped-child-charts",
                "grouped-child-smartart",
                "grouped-child-visual-signature",
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
            "object-format-position-size-style",
            "Selected drawing-object position, size, style, alt-text, wrap, and z-order fidelity capture.",
            [
                "drawing-objects",
                "object-format",
                "floating-objects",
                "print-layout",
                "body-text",
                "position-size",
                "alt-text",
                "drawing-effects",
                "shapes",
                "images",
                "wordart",
                "shape-effects",
                "image-effects",
                "wordart-effects",
                "shadow",
                "glow",
                "reflection",
                "soft-edge",
                "bevel",
                "artistic-effect",
                "square-wrap",
                "top-bottom-wrap",
                "behind-text",
                "in-front",
                "z-order"
            ],
            "object-format-position-size-style_p{page}.png",
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
            ["page-composition", "avalonia", "print-layout", "floating-image", "floating-objects", "top-bottom-wrap", "behind-text", "in-front", "z-order", "body-text"],
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
        var equations = BuildEquationExpectation(document);
        var headerFooters = BuildHeaderFooterExpectation(document, pageNumber, pageCount);
        var fields = BuildFieldExpectation(document, headerFooters);
        var tableOfAuthorities = BuildTableOfAuthoritiesExpectation(document);
        var proofingDiagnostics = BuildProofingDiagnosticExpectation(document);
        var reviewProtection = BuildReviewProtectionExpectation(document);
        var reviewMarkup = BuildReviewMarkupExpectation(document);
        var reviewCompareCombine = BuildReviewCompareCombineExpectation(document, scenario.ScenarioId);

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
            headerFooters,
            tableOfAuthorities,
            proofingDiagnostics,
            reviewProtection,
            reviewMarkup,
            headerSlotName,
            footerSlotName,
            hasFootnotes,
            hasEndnotes,
            isSyntheticPage)
        {
            Equations = equations,
            ReviewCompareCombine = reviewCompareCombine
        };
    }

    public static FreeWVisualHeaderFooterExpectation BuildHeaderFooterExpectation(
        TextDocument? document,
        int pageNumber,
        int pageCount,
        IReadOnlyList<int>? blockPageAssignments = null) =>
        HeaderFooterVisualPlanner.BuildExpectation(document, pageNumber, pageCount, blockPageAssignments);

    public static FreeWVisualEquationExpectation BuildEquationExpectation(TextDocument? document) =>
        EquationVisualPlanner.BuildEvidence(document);

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
                BuildSectionGeometrySurfaceDocument(document, pagePlan, sourceBlockIndexes),
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

        var tableCellFillSignatures = BuildTableCellFillSignatures(tables);
        var expectation = new FreeWVisualTableExpectation(
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
            TableCellFillSignatures: tableCellFillSignatures,
            Tables: tables,
            PaginationPlans: tables.Select(table => table.Pagination).ToList());

        return NormalizeTableFillEvidence(expectation);
    }

    public static FreeWVisualTableExpectation NormalizeTableFillEvidence(FreeWVisualTableExpectation tableExpectation)
    {
        ArgumentNullException.ThrowIfNull(tableExpectation);

        if (tableExpectation.Tables.Count == 0)
            return tableExpectation;

        var tables = tableExpectation.Tables
            .Select(table => NormalizeTableFillEvidence(tableExpectation, table))
            .ToList();
        return tableExpectation with
        {
            HasCellShading = tables.Any(table => table.HasCellShading),
            TableCellFillSignatures = BuildTableCellFillSignatures(tables),
            Tables = tables,
            PaginationPlans = tables.Select(table => table.Pagination).ToList()
        };
    }

    public static IReadOnlyList<string> BuildTableCellFillSignatures(
        IEnumerable<DocumentTableLayoutPlan> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);

        return tables
            .SelectMany(BuildTableCellFillSignatures)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();
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

        var altTextSummaries = BuildDrawingObjectAltTextSummaries(document, objects);
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
            AltTextObjectCount: altTextSummaries.Count,
            AltTextSummaries: altTextSummaries,
            GroupChildren: BuildDrawingObjectGroupChildExpectation(document, objects),
            Effects: BuildDrawingObjectEffectExpectation(document, objects),
            Objects: objects);
    }

    private static IReadOnlyList<string> BuildDrawingObjectAltTextSummaries(
        TextDocument document,
        IReadOnlyList<DocumentFloatingObjectSnapshot> objects)
    {
        var summaries = new List<string>();
        foreach (var snapshot in objects)
        {
            if (!TryGetRun(document, snapshot, out var run))
                continue;

            var altText = snapshot.Kind switch
            {
                DocumentFloatingObjectKind.Image => run.Image?.AltText,
                DocumentFloatingObjectKind.Shape => run.Shape?.AltText,
                DocumentFloatingObjectKind.WordArt => run.WordArt?.AltText,
                _ => null
            };
            if (string.IsNullOrWhiteSpace(altText))
                continue;

            summaries.Add(snapshot.TypeTag + ":" + altText.Trim());
        }

        return summaries
            .OrderBy(summary => summary, StringComparer.Ordinal)
            .ToList();
    }

    private static FreeWVisualDrawingObjectGroupChildExpectation BuildDrawingObjectGroupChildExpectation(
        TextDocument document,
        IReadOnlyList<DocumentFloatingObjectSnapshot> objects)
    {
        var kindSummaries = new List<string>();
        var visualSignatures = new List<string>();
        var imageChildren = 0;
        var shapeChildren = 0;
        var chartChildren = 0;
        var smartArtChildren = 0;
        var wordArtChildren = 0;
        var groupOrdinal = 0;

        foreach (var snapshot in objects.Where(o => o.Kind == DocumentFloatingObjectKind.Group))
        {
            if (!TryGetRun(document, snapshot, out var run) || run.DrawingGroup is not { } group)
                continue;

            var groupPlan = DrawingObjectVisualPlanner.BuildVisualPlan(group, snapshot);
            foreach (var child in groupPlan.GroupChildren)
            {
                switch (child.Visual.Kind)
                {
                    case DrawingObjectVisualKind.Image:
                        imageChildren++;
                        break;
                    case DrawingObjectVisualKind.Shape:
                        shapeChildren++;
                        break;
                    case DrawingObjectVisualKind.Chart:
                        chartChildren++;
                        break;
                    case DrawingObjectVisualKind.SmartArt:
                        smartArtChildren++;
                        break;
                    case DrawingObjectVisualKind.WordArt:
                        wordArtChildren++;
                        break;
                }

                kindSummaries.Add(BuildGroupChildKindSummary(groupOrdinal, child));
                visualSignatures.Add(BuildGroupChildVisualSignature(groupOrdinal, child));
            }

            groupOrdinal++;
        }

        return new FreeWVisualDrawingObjectGroupChildExpectation(
            kindSummaries.Count,
            imageChildren,
            shapeChildren,
            chartChildren,
            smartArtChildren,
            wordArtChildren,
            kindSummaries,
            visualSignatures);
    }

    private static string BuildGroupChildKindSummary(
        int groupOrdinal,
        DrawingObjectGroupChildVisualPlan child) =>
        "Group"
        + groupOrdinal.ToString(CultureInfo.InvariantCulture)
        + "Child"
        + child.ChildIndex.ToString(CultureInfo.InvariantCulture)
        + ":"
        + child.Visual.Kind;

    private static string BuildGroupChildVisualSignature(
        int groupOrdinal,
        DrawingObjectGroupChildVisualPlan child)
    {
        var prefix = BuildGroupChildKindSummary(groupOrdinal, child);
        return child.Visual.Kind switch
        {
            DrawingObjectVisualKind.Image when child.Visual.Image is { } image =>
                prefix
                + ":format=" + image.Format
                + ";bytes=" + image.ByteLength.ToString(CultureInfo.InvariantCulture)
                + ";crop=" + EvidenceBool(image.HasCrop)
                + ";adjustments=" + EvidenceBool(image.HasAdjustments)
                + ";recolor=" + EvidenceBool(image.HasRecolor)
                + ";effects=" + EvidenceBool(image.HasEffects)
                + ";artistic=" + EvidenceBool(image.HasArtisticEffect),
            DrawingObjectVisualKind.Chart when child.Visual.Chart is { } chart =>
                prefix + ":" + ChartSmartArtVisualPlanner.BuildChartVisualSignature(chart),
            DrawingObjectVisualKind.SmartArt when child.Visual.SmartArt is { } smartArt =>
                prefix + ":" + ChartSmartArtVisualPlanner.BuildSmartArtVisualSignature(smartArt),
            _ =>
                prefix
                + ":rect="
                + EvidenceDouble(child.Visual.Rect.WidthDip)
                + "x"
                + EvidenceDouble(child.Visual.Rect.HeightDip)
                + ";effects="
                + child.Visual.Effects.Summary.Replace(", ", "+", StringComparison.Ordinal)
        };
    }

    private static string EvidenceBool(bool value) => value ? "1" : "0";

    private static string EvidenceDouble(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

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
        visual.Kind is DrawingObjectVisualKind.Shape or DrawingObjectVisualKind.WordArt
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
            .Select(smartArt => ChartSmartArtVisualPlanner.BuildSmartArtPlan(smartArt))
            .ToList();
        var smartArtNodeCount = smartArts.Sum(plan => plan.Nodes.Count);
        var chartVisualSignatures = ChartSmartArtVisualPlanner.BuildChartVisualSignatures(charts);
        var chartDataSignatures = ChartSmartArtVisualPlanner.BuildChartDataSignatures(charts);
        var smartArtVisualSignatures = ChartSmartArtVisualPlanner.BuildSmartArtVisualSignatures(smartArts);

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
            ChartVisualSignatures: chartVisualSignatures,
            ChartDataSignatures: chartDataSignatures,
            SmartArtVisualSignatures: smartArtVisualSignatures,
            Charts: charts,
            SmartArts: smartArts);
    }

    public static FreeWVisualFieldExpectation BuildFieldExpectation(
        TextDocument? document,
        FreeWVisualHeaderFooterExpectation? headerFooters = null)
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
        var complexResultSignatures = complexRuns
            .Select(item => BuildComplexFieldResultSignature(item.Run.ComplexField!.Keyword, item.Run.Text))
            .Where(signature => !string.IsNullOrWhiteSpace(signature))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(signature => signature, StringComparer.OrdinalIgnoreCase)
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
            ComplexFieldResultSignatures: complexResultSignatures,
            HeaderFooterSlotNames: fieldRuns
                .Where(item => item.HeaderFooter && !string.IsNullOrWhiteSpace(item.HeaderFooterSlotName))
                .Select(item => item.HeaderFooterSlotName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(slot => slot, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            HeaderFooterResolvedFieldSignatures: BuildHeaderFooterResolvedFieldSignatures(headerFooters));
    }

    public static IReadOnlyList<string> BuildHeaderFooterResolvedFieldSignatures(
        FreeWVisualHeaderFooterExpectation? headerFooters) =>
        (headerFooters?.Slots ?? [])
            .SelectMany(slot => slot.Lines.SelectMany(line => line.Runs
                .Where(run => string.Equals(run.Kind, HeaderFooterVisualPlanner.FieldRunKind, StringComparison.Ordinal)
                    && IsPageNumberFieldKind(run.FieldKind))
                .Select(run => string.Join(
                    "|",
                    $"slot={slot.SlotName}",
                    $"page={slot.PageNumber.ToString(CultureInfo.InvariantCulture)}",
                    $"section={slot.SectionOrdinal.ToString(CultureInfo.InvariantCulture)}",
                    $"sectionPage={slot.SectionRelativePageNumber.ToString(CultureInfo.InvariantCulture)}",
                    $"paragraph={run.ParagraphIndex.ToString(CultureInfo.InvariantCulture)}",
                    $"run={run.RunIndex.ToString(CultureInfo.InvariantCulture)}",
                    $"field={NormalizePageNumberFieldKind(run.FieldKind)}",
                    $"text={NormalizeEvidenceSignatureText(run.Text)}"))))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();

    public static FreeWVisualTableOfAuthoritiesExpectation BuildTableOfAuthoritiesExpectation(TextDocument? document)
    {
        if (document is null)
            return EmptyTableOfAuthoritiesExpectation;

        var entryCount = 0;
        var pageReferences = new List<FreeWVisualTableOfAuthoritiesPageReference>();
        var sourcePageReferences = BuildSourceToaPageReferenceEvidence(document);
        var categories = new List<string>();
        var currentCategory = string.Empty;
        foreach (var paragraph in document.Blocks.OfType<Paragraph>())
        {
            if (string.Equals(paragraph.StyleId, TableOfAuthorities.CategoryStyleId, StringComparison.Ordinal))
            {
                currentCategory = paragraph.PlainText.Trim();
                if (!string.IsNullOrWhiteSpace(currentCategory)
                    && !categories.Contains(currentCategory, StringComparer.OrdinalIgnoreCase))
                {
                    categories.Add(currentCategory);
                }

                continue;
            }

            if (!string.Equals(paragraph.StyleId, TableOfAuthorities.EntryStyleId, StringComparison.Ordinal))
                continue;

            entryCount++;
            var extracted = ExtractTableOfAuthoritiesPageReference(paragraph);
            if (extracted is null)
                continue;

            var pageNumbers = ParsePageNumbers(extracted.Value.PageReferenceText);
            var displayedPageReferences = ParseDisplayedPageReferences(extracted.Value.PageReferenceText);
            var category = string.IsNullOrWhiteSpace(currentCategory) ? "Uncategorized" : currentCategory;
            var sourceKey = BuildToaEvidenceKey(category, extracted.Value.EntryText);
            if (sourcePageReferences.TryGetValue(sourceKey, out var sourceReferences))
            {
                var matchingPhysicalPages = sourceReferences
                    .Where(reference => displayedPageReferences.Contains(reference.DisplayText, StringComparer.OrdinalIgnoreCase))
                    .Select(reference => reference.PhysicalPageNumber)
                    .Distinct()
                    .OrderBy(page => page)
                    .ToList();
                if (matchingPhysicalPages.Count > 0)
                    pageNumbers = matchingPhysicalPages;
            }

            var referenceKind = ClassifyTableOfAuthoritiesPageReference(
                extracted.Value.PageReferenceText,
                pageNumbers,
                displayedPageReferences);
            var stableSignature = BuildTableOfAuthoritiesPageReferenceSignature(
                category,
                extracted.Value.EntryText,
                extracted.Value.PageReferenceText,
                pageNumbers,
                referenceKind);
            pageReferences.Add(new FreeWVisualTableOfAuthoritiesPageReference(
                category,
                extracted.Value.EntryText,
                extracted.Value.PageReferenceText,
                pageNumbers,
                displayedPageReferences,
                referenceKind,
                IsStrongTableOfAuthoritiesPageReference(referenceKind, pageNumbers),
                stableSignature));
        }

        return new FreeWVisualTableOfAuthoritiesExpectation(
            EntryCount: entryCount,
            EntryWithPageReferenceCount: pageReferences.Count,
            CategoryCount: categories.Count,
            Categories: categories
                .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            HasGeneratedTable: entryCount > 0,
            HasPageReferences: pageReferences.Count > 0,
            HasExplicitPageNumbers: pageReferences.Any(reference => reference.PageNumbers.Count > 0),
            HasPassimReferences: pageReferences.Any(reference =>
                string.Equals(reference.PageReferenceText, "passim", StringComparison.OrdinalIgnoreCase)),
            PageReferenceSignatures: pageReferences
                .Select(reference => reference.StableSignature)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(signature => signature, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            PageReferences: pageReferences);
    }

    public static FreeWVisualProofingDiagnosticExpectation BuildProofingDiagnosticExpectation(TextDocument? document)
    {
        if (document is null)
            return EmptyProofingDiagnosticExpectation;

        var diagnostics = ProofingDiagnosticPlanner.BuildVisibleIndicators(document, spellCheckEnabled: true);
        if (diagnostics.Count == 0)
            return EmptyProofingDiagnosticExpectation;

        var signatures = diagnostics
            .Select(BuildProofingDiagnosticSignature)
            .OrderBy(signature => signature.StableSignature, StringComparer.Ordinal)
            .ToList();
        var adornments = signatures
            .Select(BuildProofingAdornmentExpectation)
            .OrderBy(adornment => adornment.StableSignature, StringComparer.Ordinal)
            .ToList();
        var kinds = signatures
            .Select(signature => signature.Kind)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var languageTags = signatures
            .Select(signature => signature.LanguageTag)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FreeWVisualProofingDiagnosticExpectation(
            DiagnosticCount: signatures.Count,
            SpellingCount: signatures.Count(signature =>
                string.Equals(signature.Kind, nameof(ProofingDiagnosticKind.Spelling), StringComparison.Ordinal)),
            GrammarCount: signatures.Count(signature =>
                string.Equals(signature.Kind, nameof(ProofingDiagnosticKind.Grammar), StringComparison.Ordinal)),
            HasSpelling: signatures.Any(signature =>
                string.Equals(signature.Kind, nameof(ProofingDiagnosticKind.Spelling), StringComparison.Ordinal)),
            HasGrammar: signatures.Any(signature =>
                string.Equals(signature.Kind, nameof(ProofingDiagnosticKind.Grammar), StringComparison.Ordinal)),
            Kinds: kinds,
            LanguageTags: languageTags,
            StableSignatures: signatures.Select(signature => signature.StableSignature).ToList(),
            Diagnostics: signatures,
            AdornmentCount: adornments.Count,
            SpellingAdornmentCount: adornments.Count(adornment =>
                string.Equals(adornment.Kind, nameof(ProofingDiagnosticKind.Spelling), StringComparison.Ordinal)),
            GrammarAdornmentCount: adornments.Count(adornment =>
                string.Equals(adornment.Kind, nameof(ProofingDiagnosticKind.Grammar), StringComparison.Ordinal)),
            HasSpellingUnderline: adornments.Any(adornment =>
                string.Equals(adornment.Kind, nameof(ProofingDiagnosticKind.Spelling), StringComparison.Ordinal)),
            HasGrammarUnderline: adornments.Any(adornment =>
                string.Equals(adornment.Kind, nameof(ProofingDiagnosticKind.Grammar), StringComparison.Ordinal)),
            AdornmentStableSignatures: adornments.Select(adornment => adornment.StableSignature).ToList(),
            Adornments: adornments);
    }

    private static FreeWVisualProofingDiagnosticSignature BuildProofingDiagnosticSignature(
        ProofingDiagnostic diagnostic)
    {
        var kind = diagnostic.Kind.ToString();
        var languageTag = string.IsNullOrWhiteSpace(diagnostic.LanguageTag)
            ? null
            : diagnostic.LanguageTag.Trim();
        var stableSignature = string.Join(
            "|",
            "kind=" + kind,
            "word=" + diagnostic.Word,
            "normalized=" + diagnostic.NormalizedWord,
            "language=" + (languageTag ?? string.Empty),
            "block=" + diagnostic.BlockIndex.ToString(CultureInfo.InvariantCulture),
            "run=" + diagnostic.RunIndex.ToString(CultureInfo.InvariantCulture),
            "runOffset=" + diagnostic.RunOffset.ToString(CultureInfo.InvariantCulture),
            "paragraphOffset=" + diagnostic.ParagraphOffset.ToString(CultureInfo.InvariantCulture),
            "length=" + diagnostic.Length.ToString(CultureInfo.InvariantCulture));

        return new FreeWVisualProofingDiagnosticSignature(
            Kind: kind,
            Word: diagnostic.Word,
            NormalizedWord: diagnostic.NormalizedWord,
            LanguageTag: languageTag,
            BlockIndex: diagnostic.BlockIndex,
            RunIndex: diagnostic.RunIndex,
            RunOffset: diagnostic.RunOffset,
            ParagraphOffset: diagnostic.ParagraphOffset,
            Length: diagnostic.Length,
            StableSignature: stableSignature);
    }

    private static FreeWVisualProofingAdornmentExpectation BuildProofingAdornmentExpectation(
        FreeWVisualProofingDiagnosticSignature diagnostic)
    {
        var isGrammar = string.Equals(diagnostic.Kind, nameof(ProofingDiagnosticKind.Grammar), StringComparison.Ordinal);
        var adornmentKind = isGrammar ? GrammarAdornmentKind : SpellingAdornmentKind;
        var colorHex = isGrammar ? GrammarAdornmentColorHex : SpellingAdornmentColorHex;
        var paragraphEndOffset = diagnostic.ParagraphOffset + diagnostic.Length;
        var stableSignature = string.Join(
            "|",
            "diagnostic=" + diagnostic.StableSignature,
            "adornment=" + adornmentKind,
            "style=" + ProofingUnderlineStyle,
            "color=" + colorHex,
            "block=" + diagnostic.BlockIndex.ToString(CultureInfo.InvariantCulture),
            "run=" + diagnostic.RunIndex.ToString(CultureInfo.InvariantCulture),
            "runOffset=" + diagnostic.RunOffset.ToString(CultureInfo.InvariantCulture),
            "paragraphStart=" + diagnostic.ParagraphOffset.ToString(CultureInfo.InvariantCulture),
            "paragraphEnd=" + paragraphEndOffset.ToString(CultureInfo.InvariantCulture),
            "length=" + diagnostic.Length.ToString(CultureInfo.InvariantCulture));

        return new FreeWVisualProofingAdornmentExpectation(
            DiagnosticStableSignature: diagnostic.StableSignature,
            Kind: diagnostic.Kind,
            AdornmentKind: adornmentKind,
            UnderlineStyle: ProofingUnderlineStyle,
            ColorHex: colorHex,
            BlockIndex: diagnostic.BlockIndex,
            RunIndex: diagnostic.RunIndex,
            RunOffset: diagnostic.RunOffset,
            ParagraphStartOffset: diagnostic.ParagraphOffset,
            ParagraphEndOffset: paragraphEndOffset,
            Length: diagnostic.Length,
            StableSignature: stableSignature);
    }

    public static FreeWVisualReviewProtectionExpectation BuildReviewProtectionExpectation(TextDocument? document)
    {
        var protection = document?.Protection ?? ProtectionSettings.Unprotected;
        var isMarkedAsFinal = document?.MarkedAsFinal ?? false;
        var statePlan = ReviewProtectionStatePlanner.Build(protection, isMarkedAsFinal);
        var policy = RestrictEditingEnforcementPolicy.From(protection, isMarkedAsFinal);
        var operations = ReviewProtectionEvidenceOperations
            .Select(operation => BuildReviewProtectionOperationExpectation(policy, operation))
            .ToList();

        return new FreeWVisualReviewProtectionExpectation(
            ProtectionMode: protection.Mode.ToString(),
            IsProtected: protection.IsProtected,
            IsMarkedAsFinal: isMarkedAsFinal,
            MarkAsFinal: statePlan.MarkAsFinal,
            RestrictEditing: statePlan.RestrictEditing,
            IsBodyEditingLocked: policy.IsBodyEditingLocked,
            IsBodyFormattingLocked: policy.IsBodyFormattingLocked,
            IsCommentWorkflowAllowed: policy.IsCommentWorkflowAllowed,
            IsHistoryLocked: policy.IsHistoryLocked,
            ShouldForceTrackChanges: policy.ShouldForceTrackChanges,
            Operations: operations,
            StableSignatures: operations
                .Select(operation => operation.StableSignature)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList());
    }

    public static FreeWVisualReviewMarkupExpectation BuildReviewMarkupExpectation(TextDocument? document)
    {
        if (document is null)
            return FreeWVisualReviewMarkupExpectation.Empty;

        var revisionEntries = RevisionList.Enumerate(document);
        var formattingEntries = document.Blocks
            .OfType<Paragraph>()
            .SelectMany((paragraph, blockIndex) => paragraph.Runs
                .Select((run, runIndex) => new
                {
                    BlockIndex = blockIndex,
                    RunIndex = runIndex,
                    Run = run
                }))
            .Where(entry => entry.Run.FormatRevision is not null)
            .ToList();
        var authors = revisionEntries
            .Select(entry => entry.Author)
            .Concat(formattingEntries.Select(entry => entry.Run.FormatRevision?.Author))
            .Concat(document.Comments.Values
                .SelectMany(comment => comment.ThreadInOrder())
                .Select(comment => comment.Author))
            .Where(author => !string.IsNullOrWhiteSpace(author))
            .Select(author => author!.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(author => author, StringComparer.Ordinal)
            .ToList();
        var revisionSignatures = revisionEntries
            .Select(entry => string.Join(
                "|",
                "kind=" + entry.Kind,
                "author=" + NormalizeEvidenceSignatureText(entry.Author),
                "block=" + entry.BlockIndex.ToString(CultureInfo.InvariantCulture),
                "text=" + NormalizeEvidenceSignatureText(entry.Text)))
            .Concat(formattingEntries.Select(entry => string.Join(
                "|",
                "kind=Formatting",
                "author=" + NormalizeEvidenceSignatureText(entry.Run.FormatRevision?.Author),
                "block=" + entry.BlockIndex.ToString(CultureInfo.InvariantCulture),
                "run=" + entry.RunIndex.ToString(CultureInfo.InvariantCulture),
                "text=" + NormalizeEvidenceSignatureText(entry.Run.Text))))
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();
        var commentAnchors = document.Blocks
            .OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.CommentId is not null && !run.IsCommentReference)
            .Count();
        var commentReferences = document.Blocks
            .OfType<Paragraph>()
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.CommentId is not null && run.IsCommentReference)
            .Count();
        var commentSignatures = document.Comments.Values
            .SelectMany(comment => comment.ThreadInOrder()
                .Select(threadComment => string.Join(
                    "|",
                    "id=" + threadComment.Id.ToString(CultureInfo.InvariantCulture),
                    "parent=" + comment.Id.ToString(CultureInfo.InvariantCulture),
                    "author=" + NormalizeEvidenceSignatureText(threadComment.Author),
                    "resolved=" + BoolFlag(comment.Resolved),
                    "reply=" + BoolFlag(!ReferenceEquals(threadComment, comment)),
                    "text=" + NormalizeEvidenceSignatureText(threadComment.PlainText))))
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();

        return new FreeWVisualReviewMarkupExpectation(
            RevisionCount: revisionEntries.Count,
            InsertionCount: revisionEntries.Count(entry => entry.Kind == RevisionEntryKind.Insertion),
            DeletionCount: revisionEntries.Count(entry => entry.Kind == RevisionEntryKind.Deletion),
            FormattingRevisionCount: formattingEntries.Count,
            AuthorCount: authors.Count,
            CommentCount: document.Comments.Count,
            ReplyCount: document.Comments.Values.Sum(comment => comment.Replies.Count),
            ResolvedCommentCount: document.Comments.Values.Count(comment => comment.Resolved),
            CommentAnchorCount: commentAnchors,
            CommentReferenceCount: commentReferences,
            Authors: authors,
            RevisionStableSignatures: revisionSignatures,
            CommentStableSignatures: commentSignatures);
    }

    public static FreeWVisualReviewCompareCombineExpectation BuildReviewCompareCombineExpectation(
        TextDocument? document,
        string scenarioId)
    {
        if (document is null)
            return FreeWVisualReviewCompareCombineExpectation.Empty;

        var operation = NormalizeScenarioId(scenarioId) switch
        {
            "review-compare-visual-proof" => "compare",
            "review-combine-visual-proof" => "combine",
            _ => "none"
        };
        if (string.Equals(operation, "none", StringComparison.Ordinal))
            return FreeWVisualReviewCompareCombineExpectation.Empty;

        var entries = RevisionList.Enumerate(document);
        var authors = entries
            .Select(entry => entry.Author)
            .Where(author => !string.IsNullOrWhiteSpace(author))
            .Select(author => author!.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(author => author, StringComparer.Ordinal)
            .ToList();
        var stableSignatures = entries
            .Select(entry => string.Join(
                "|",
                "operation=" + operation,
                "kind=" + entry.Kind,
                "author=" + NormalizeEvidenceSignatureText(entry.Author),
                "block=" + entry.BlockIndex.ToString(CultureInfo.InvariantCulture),
                "text=" + NormalizeEvidenceSignatureText(entry.Text)))
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();
        var retainedModelSafetySignatures = BuildReviewRetainedModelSafetySignatures(document, operation);

        return new FreeWVisualReviewCompareCombineExpectation(
            Operation: operation,
            RevisionCount: entries.Count,
            InsertionCount: entries.Count(entry => entry.Kind == RevisionEntryKind.Insertion),
            DeletionCount: entries.Count(entry => entry.Kind == RevisionEntryKind.Deletion),
            FormattingCount: entries.Count(entry => entry.Kind == RevisionEntryKind.Formatting),
            AuthorCount: authors.Count,
            PreservedPartCount: document.Preserved.Parts.Count,
            PreservedContentTypeDefaultCount: document.Preserved.ContentTypeDefaults.Count,
            HasPreservedSettings: document.Preserved.OriginalSettings is not null,
            HasPreservedCustomProperties: document.Preserved.OriginalCustomProperties is not null,
            HasRetainedModelSafety: retainedModelSafetySignatures.Count > 0,
            Authors: authors,
            StableSignatures: stableSignatures,
            RetainedModelSafetySignatures: retainedModelSafetySignatures,
            HasCompareSemantics: string.Equals(operation, "compare", StringComparison.Ordinal)
                && entries.Count > 0
                && authors.Count == 1,
            HasCombineSemantics: string.Equals(operation, "combine", StringComparison.Ordinal)
                && entries.Count > 0
                && authors.Count >= 2);
    }

    private static IReadOnlyList<string> BuildReviewRetainedModelSafetySignatures(
        TextDocument document,
        string operation)
    {
        var signatures = new List<string>();
        if (document.Preserved.OriginalSettings is not null)
            signatures.Add("operation=" + operation + "|preserved=settings");
        if (document.Preserved.OriginalCustomProperties is not null)
            signatures.Add("operation=" + operation + "|preserved=custom-properties");

        signatures.AddRange(document.Preserved.Parts
            .Select(part => "operation=" + operation + "|preserved=part:" + NormalizeEvidenceSignatureText(part.PartName)));
        signatures.AddRange(document.Preserved.ContentTypeDefaults
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => "operation=" + operation + "|preserved=content-type-default:" + NormalizeEvidenceSignatureText(pair.Key)));

        return signatures
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();
    }

    private static FreeWVisualProtectionOperationExpectation BuildReviewProtectionOperationExpectation(
        RestrictEditingEnforcementPolicy policy,
        RestrictEditingEvidenceOperation operation)
    {
        var decision = operation.MutationKind is null
            ? policy.DecisionFor(operation.Operation)
            : policy.DecisionForHistory(operation.Operation, operation.MutationKind);
        var mutationKind = operation.MutationKind?.ToString() ?? "None";
        var stableSignature = string.Join(
            "|",
            $"operation={operation.Operation}",
            $"mutation={mutationKind}",
            $"allowed={BoolFlag(decision.IsAllowed)}",
            $"requiresTrackedChanges={BoolFlag(decision.RequiresTrackedChanges)}",
            $"blockReason={decision.BlockReason}",
            $"protection={decision.ProtectionMode}");

        return new FreeWVisualProtectionOperationExpectation(
            Operation: operation.Operation.ToString(),
            MutationKind: mutationKind,
            IsAllowed: decision.IsAllowed,
            RequiresTrackedChanges: decision.RequiresTrackedChanges,
            BlockReason: decision.BlockReason.ToString(),
            ProtectionMode: decision.ProtectionMode.ToString(),
            StableSignature: stableSignature);
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
        TableCellFillSignatures: [],
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
        AltTextObjectCount: 0,
        AltTextSummaries: [],
        GroupChildren: new FreeWVisualDrawingObjectGroupChildExpectation(
            ChildCount: 0,
            ImageChildCount: 0,
            ShapeChildCount: 0,
            ChartChildCount: 0,
            SmartArtChildCount: 0,
            WordArtChildCount: 0,
            ChildKindSummaries: [],
            ChildVisualSignatures: []),
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
        ChartVisualSignatures: [],
        ChartDataSignatures: [],
        SmartArtVisualSignatures: [],
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
        ComplexFieldResultSignatures: [],
        HeaderFooterSlotNames: [],
        HeaderFooterResolvedFieldSignatures: []);

    private static FreeWVisualTableOfAuthoritiesExpectation EmptyTableOfAuthoritiesExpectation { get; } = new(
        EntryCount: 0,
        EntryWithPageReferenceCount: 0,
        CategoryCount: 0,
        Categories: [],
        HasGeneratedTable: false,
        HasPageReferences: false,
        HasExplicitPageNumbers: false,
        HasPassimReferences: false,
        PageReferenceSignatures: [],
        PageReferences: []);

    private static FreeWVisualProofingDiagnosticExpectation EmptyProofingDiagnosticExpectation { get; } = new(
        DiagnosticCount: 0,
        SpellingCount: 0,
        GrammarCount: 0,
        HasSpelling: false,
        HasGrammar: false,
        Kinds: [],
        LanguageTags: [],
        StableSignatures: [],
        Diagnostics: [],
        AdornmentCount: 0,
        SpellingAdornmentCount: 0,
        GrammarAdornmentCount: 0,
        HasSpellingUnderline: false,
        HasGrammarUnderline: false,
        AdornmentStableSignatures: [],
        Adornments: []);

    private sealed record RestrictEditingEvidenceOperation(
        RestrictEditingOperationKind Operation,
        DocumentCommandMutationKind? MutationKind = null);

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

    private static string? NormalizeHexColorOrNull(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;

        var value = hex.Trim();
        if (value.StartsWith('#'))
            value = value[1..];
        if (value.Length == 8)
            value = value[2..];

        if (value.Length != 6)
            return hex.Trim().ToUpperInvariant();

        return int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)
            ? ToHex(rgb)
            : hex.Trim().ToUpperInvariant();
    }

    private static IEnumerable<string> BuildTableCellFillSignatures(DocumentTableLayoutPlan table)
    {
        foreach (var cell in table.Cells)
        {
            var fillPlan = DocumentViewLayoutPlanner.BuildTableCellEffectiveFillPlan(table, cell);

            if (fillPlan.StyleDerivedFillHex is not null
                && fillPlan.StyleDerivedFillSource is not null)
            {
                yield return BuildTableCellFillSignature(
                    table,
                    cell,
                    fillPlan.StyleDerivedFillSource,
                    fillPlan.StyleDerivedFillHex);
            }

            if (fillPlan.ExplicitFillHex is not null)
            {
                yield return BuildTableCellFillSignature(
                    table,
                    cell,
                    "explicit-cell",
                    fillPlan.ExplicitFillHex);
            }
        }
    }

    private static string BuildTableCellFillSignature(
        DocumentTableLayoutPlan table,
        DocumentTableCellLayoutPlan cell,
        string source,
        string fillHex) =>
        string.Join(
            "|",
            "table=" + table.TableIndex.ToString(CultureInfo.InvariantCulture),
            "row=" + cell.RowIndex.ToString(CultureInfo.InvariantCulture),
            "cell=" + cell.CellIndex.ToString(CultureInfo.InvariantCulture),
            "grid=" + cell.GridColumnIndex.ToString(CultureInfo.InvariantCulture),
            "gridSpan=" + cell.GridSpan.ToString(CultureInfo.InvariantCulture),
            "rowSpan=" + cell.RowSpan.ToString(CultureInfo.InvariantCulture),
            "vMergeContinue=" + BoolFlag(cell.IsVerticalMergeContinuation),
            "source=" + source,
            "fill=" + fillHex);

    private static DocumentTableLayoutPlan NormalizeTableFillEvidence(
        FreeWVisualTableExpectation tableExpectation,
        DocumentTableLayoutPlan table)
    {
        var cells = table.Cells
            .Select(cell => ShouldClearMaterializedStyleFill(tableExpectation, table, cell)
                ? cell with { ShadingColorHex = null }
                : cell)
            .ToList();

        return table with
        {
            HasCellShading = cells.Any(cell => !string.IsNullOrWhiteSpace(cell.ShadingColorHex)),
            Cells = cells
        };
    }

    private static bool ShouldClearMaterializedStyleFill(
        FreeWVisualTableExpectation tableExpectation,
        DocumentTableLayoutPlan table,
        DocumentTableCellLayoutPlan cell)
    {
        if (HasExplicitCellFillSignature(tableExpectation, table, cell))
            return false;

        var normalizedShading = NormalizeFillHex(cell.ShadingColorHex);
        if (normalizedShading is null)
            return false;

        var styleOnlyCell = cell with { ShadingColorHex = null };
        var styleOnlyFill = DocumentViewLayoutPlanner.BuildTableCellEffectiveFillPlan(table, styleOnlyCell);
        var plannedFill = NormalizeFillHex(styleOnlyFill.EffectiveFillHex);
        if (plannedFill is null)
            return false;

        var plannedSource = styleOnlyFill.EffectiveFillSource ?? string.Empty;
        if (!plannedSource.StartsWith("style-derived-", StringComparison.Ordinal)
            && !plannedSource.StartsWith("legacy-", StringComparison.Ordinal))
            return false;

        return string.Equals(normalizedShading, plannedFill, StringComparison.Ordinal);
    }

    private static bool HasExplicitCellFillSignature(
        FreeWVisualTableExpectation tableExpectation,
        DocumentTableLayoutPlan table,
        DocumentTableCellLayoutPlan cell)
    {
        var signatures = tableExpectation.TableCellFillSignatures ?? [];
        if (signatures.Count == 0)
            return false;

        var prefix = string.Join(
            "|",
            "table=" + table.TableIndex.ToString(CultureInfo.InvariantCulture),
            "row=" + cell.RowIndex.ToString(CultureInfo.InvariantCulture),
            "cell=" + cell.CellIndex.ToString(CultureInfo.InvariantCulture),
            "grid=" + cell.GridColumnIndex.ToString(CultureInfo.InvariantCulture));

        return signatures.Any(signature =>
            signature.StartsWith(prefix, StringComparison.Ordinal)
            && signature.Contains("|source=explicit-cell|", StringComparison.Ordinal));
    }

    private static string? NormalizeFillHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.StartsWith('#'))
            trimmed = trimmed[1..];

        return trimmed.Length == 6
            ? "#" + trimmed.ToUpperInvariant()
            : value.Trim().ToUpperInvariant();
    }

    private static string BoolFlag(bool value) => value ? "1" : "0";

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

    private static string BuildComplexFieldResultSignature(string? keyword, string? resultText)
    {
        var normalizedKeyword = NormalizeEvidenceSignatureText(keyword).ToUpperInvariant();
        var normalizedResult = NormalizeEvidenceSignatureText(resultText);
        return string.IsNullOrWhiteSpace(normalizedKeyword) || string.IsNullOrWhiteSpace(normalizedResult)
            ? string.Empty
            : normalizedKeyword + "=" + normalizedResult;
    }

    private static bool IsPageNumberFieldKind(string? fieldKind) =>
        string.Equals(fieldKind, nameof(RunFieldKind.PageNumber), StringComparison.OrdinalIgnoreCase)
        || string.Equals(fieldKind, nameof(RunFieldKind.NumPages), StringComparison.OrdinalIgnoreCase)
        || string.Equals(fieldKind, "PAGE", StringComparison.OrdinalIgnoreCase)
        || string.Equals(fieldKind, "NUMPAGES", StringComparison.OrdinalIgnoreCase);

    private static string NormalizePageNumberFieldKind(string? fieldKind)
    {
        if (string.Equals(fieldKind, nameof(RunFieldKind.PageNumber), StringComparison.OrdinalIgnoreCase)
            || string.Equals(fieldKind, "PAGE", StringComparison.OrdinalIgnoreCase))
        {
            return "PAGE";
        }

        if (string.Equals(fieldKind, nameof(RunFieldKind.NumPages), StringComparison.OrdinalIgnoreCase)
            || string.Equals(fieldKind, "NUMPAGES", StringComparison.OrdinalIgnoreCase))
        {
            return "NUMPAGES";
        }

        return NormalizeEvidenceSignatureText(fieldKind).ToUpperInvariant();
    }

    private static string ClassifyTableOfAuthoritiesPageReference(
        string pageReferenceText,
        IReadOnlyList<int> pageNumbers,
        IReadOnlyList<string> displayedPageReferences)
    {
        if (pageNumbers.Count > 0
            && displayedPageReferences.Any(reference =>
                !int.TryParse(reference, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            return "section-formatted-page-numbers";
        }
        if (pageNumbers.Count > 0)
            return "explicit-page-numbers";
        if (string.Equals(pageReferenceText.Trim(), "passim", StringComparison.OrdinalIgnoreCase))
            return "passim";

        return "weak-page-reference-text";
    }

    private static bool IsStrongTableOfAuthoritiesPageReference(
        string referenceKind,
        IReadOnlyList<int> pageNumbers) =>
        pageNumbers.Count > 0
        || string.Equals(referenceKind, "passim", StringComparison.OrdinalIgnoreCase);

    private static string BuildTableOfAuthoritiesPageReferenceSignature(
        string category,
        string entryText,
        string pageReferenceText,
        IReadOnlyList<int> pageNumbers,
        string referenceKind)
    {
        var pages = pageNumbers.Count == 0
            ? "-"
            : string.Join(",", pageNumbers.Select(page => page.ToString(CultureInfo.InvariantCulture)));
        return string.Join(
            "|",
            "category=" + NormalizeEvidenceSignatureText(category),
            "entry=" + NormalizeEvidenceSignatureText(entryText),
            "kind=" + NormalizeEvidenceSignatureText(referenceKind),
            "pages=" + pages,
            "text=" + NormalizeEvidenceSignatureText(pageReferenceText));
    }

    private static string NormalizeEvidenceSignatureText(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Trim()
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal)
            .Replace("|", "/", StringComparison.Ordinal);

        return string.Join(
            " ",
            normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

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

    private static IReadOnlyList<string> ParseDisplayedPageReferences(string pageReferenceText) =>
        pageReferenceText
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(segment => segment, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static Dictionary<string, IReadOnlyList<SourceToaPageReferenceEvidence>> BuildSourceToaPageReferenceEvidence(
        TextDocument document)
    {
        var references = PageNumberFormatDialogPlanner.BuildCitationPageReferencePlans(document)
            .Where(plan => !string.IsNullOrWhiteSpace(plan.Citation.LongCitation))
            .GroupBy(
                plan => BuildToaEvidenceKey(
                    TableOfAuthorities.CategoryHeading(plan.Citation.Category),
                    plan.Citation.LongCitation),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SourceToaPageReferenceEvidence>)group
                    .GroupBy(
                        plan => (plan.PhysicalPageNumber, plan.DisplayText),
                        plan => plan,
                        EqualityComparer<(int PhysicalPageNumber, string DisplayText)>.Default)
                    .Select(g => new SourceToaPageReferenceEvidence(
                        g.Key.PhysicalPageNumber,
                        g.Key.DisplayText))
                    .OrderBy(reference => reference.PhysicalPageNumber)
                    .ThenBy(reference => reference.DisplayText, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        return references;
    }

    private static string BuildToaEvidenceKey(string category, string entryText) =>
        NormalizeEvidenceSignatureText(category) + "|" + NormalizeEvidenceSignatureText(entryText);

    private sealed record SourceToaPageReferenceEvidence(int PhysicalPageNumber, string DisplayText);

    private static TextDocument BuildSectionGeometrySurfaceDocument(
        TextDocument source,
        FreeWVisualSectionGeometryPagePlan pagePlan,
        IReadOnlyList<int> sourceBlockIndexes)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        CopyDocumentShell(source, document);
        CopyPageSettings(pagePlan.Page, document.Page);
        CopySectionHeadersFooters(source, document, pagePlan.SectionOrdinal);

        foreach (var blockIndex in sourceBlockIndexes)
        {
            if (blockIndex >= 0 && blockIndex < source.Blocks.Count)
            {
                var block = DocumentMerge.CloneBlock(source.Blocks[blockIndex]);
                // The surface already represents the resolved section. Retaining a source section
                // break here would create an additional page boundary inside the isolated capture.
                if (block is Paragraph paragraph)
                    paragraph.SectionBreak = null;
                document.Blocks.Add(block);
            }
        }

        if (document.Blocks.Count == 0)
            document.Blocks.Add(new Paragraph());

        return document;
    }

    private static void CopySectionHeadersFooters(
        TextDocument source,
        TextDocument target,
        int sectionOrdinal)
    {
        var sections = source.Sections;
        var sectionIndex = Math.Clamp(sectionOrdinal - 1, 0, Math.Max(0, sections.Count - 1));
        var headersFooters = sections.Count == 0
            ? source.FinalSectionHeadersFooters
            : sections[sectionIndex].HeadersFooters;

        CopyHeaderFooterSlots(headersFooters, target.FinalSectionHeadersFooters);
    }

    private static void CopyHeaderFooterSlots(
        SectionHeadersFooters source,
        SectionHeadersFooters target)
    {
        target.Header = CloneHeaderFooter(source.Header);
        target.Footer = CloneHeaderFooter(source.Footer);
        target.EvenHeader = CloneHeaderFooter(source.EvenHeader);
        target.EvenFooter = CloneHeaderFooter(source.EvenFooter);
        target.FirstHeader = CloneHeaderFooter(source.FirstHeader);
        target.FirstFooter = CloneHeaderFooter(source.FirstFooter);
    }

    private static HeaderFooter? CloneHeaderFooter(HeaderFooter? source)
    {
        if (source is null)
            return null;

        var clone = new HeaderFooter();
        foreach (var paragraph in source.Paragraphs)
        {
            var clonedParagraph = (Paragraph)DocumentMerge.CloneBlock(paragraph);
            CopyHeaderFooterImageMetadata(paragraph, clonedParagraph);
            clone.Paragraphs.Add(clonedParagraph);
        }
        return clone;
    }

    private static void CopyHeaderFooterImageMetadata(Paragraph source, Paragraph target)
    {
        var count = Math.Min(source.Runs.Count, target.Runs.Count);
        for (var i = 0; i < count; i++)
        {
            if (source.Runs[i].Image is not { } sourceImage || target.Runs[i].Image is not { } targetImage)
                continue;

            targetImage.AltText = sourceImage.AltText;
            targetImage.Wrapping = sourceImage.Wrapping;
            targetImage.HorizontalOffsetPt = sourceImage.HorizontalOffsetPt;
            targetImage.VerticalOffsetPt = sourceImage.VerticalOffsetPt;
            targetImage.HorizontalAnchor = sourceImage.HorizontalAnchor;
            targetImage.VerticalAnchor = sourceImage.VerticalAnchor;
            targetImage.ZOrderIndex = sourceImage.ZOrderIndex;
            targetImage.RotationAngle = sourceImage.RotationAngle;
            targetImage.FlipH = sourceImage.FlipH;
            targetImage.FlipV = sourceImage.FlipV;
            targetImage.CropLeft = sourceImage.CropLeft;
            targetImage.CropRight = sourceImage.CropRight;
            targetImage.CropTop = sourceImage.CropTop;
            targetImage.CropBottom = sourceImage.CropBottom;
            targetImage.BorderColorHex = sourceImage.BorderColorHex;
            targetImage.BorderWidthPt = sourceImage.BorderWidthPt;
            targetImage.BorderDash = sourceImage.BorderDash;
            targetImage.OriginalPixelWidth = sourceImage.OriginalPixelWidth;
            targetImage.OriginalPixelHeight = sourceImage.OriginalPixelHeight;
            targetImage.BrightnessPct = sourceImage.BrightnessPct;
            targetImage.ContrastPct = sourceImage.ContrastPct;
            targetImage.SaturationPct = sourceImage.SaturationPct;
            targetImage.TransparencyPct = sourceImage.TransparencyPct;
            targetImage.RecolorMode = sourceImage.RecolorMode;
            targetImage.ColorTemperature = sourceImage.ColorTemperature;
            targetImage.ShadowPreset = sourceImage.ShadowPreset;
            targetImage.GlowSizePt = sourceImage.GlowSizePt;
            targetImage.GlowColorHex = sourceImage.GlowColorHex;
            targetImage.ReflectionPreset = sourceImage.ReflectionPreset;
            targetImage.SoftEdgePt = sourceImage.SoftEdgePt;
            targetImage.BevelPreset = sourceImage.BevelPreset;
            targetImage.ArtisticEffect = sourceImage.ArtisticEffect;
            targetImage.PictureStylePreset = sourceImage.PictureStylePreset;
        }
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
        target.GutterAtTop = copy.GutterAtTop;
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
        target.PageNumberFormat = copy.PageNumberFormat;
        target.PageNumberStartAt = copy.PageNumberStartAt;
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
        && left.Landscape == right.Landscape
        && left.PageNumberFormat == right.PageNumberFormat
        && left.PageNumberStartAt == right.PageNumberStartAt;

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
