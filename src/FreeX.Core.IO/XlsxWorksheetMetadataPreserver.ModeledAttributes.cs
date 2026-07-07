using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static partial class XlsxWorksheetMetadataPreserver
{
    private static readonly HashSet<string> ModeledPrintOptionsAttributes = new(StringComparer.Ordinal)
    {
        "gridLines",
        "headings",
        "horizontalCentered",
        "verticalCentered"
    };

    private static readonly HashSet<string> ModeledDimensionAttributes = new(StringComparer.Ordinal)
    {
        "ref"
    };

    private static readonly HashSet<string> ModeledPageMarginsAttributes = new(StringComparer.Ordinal)
    {
        "left",
        "right",
        "top",
        "bottom"
    };

    private static readonly HashSet<string> ModeledPageSetupAttributes = new(StringComparer.Ordinal)
    {
        "paperSize",
        "scale",
        "firstPageNumber",
        "fitToWidth",
        "fitToHeight",
        "pageOrder",
        "orientation",
        "useFirstPageNumber",
        "usePrinterDefaults",
        "copies",
        "blackAndWhite",
        "draft",
        "cellComments",
        "errors",
        "horizontalDpi",
        "verticalDpi"
    };

    private static readonly HashSet<string> ModeledHeaderFooterAttributes = new(StringComparer.Ordinal)
    {
        "differentOddEven",
        "differentFirst",
        "scaleWithDoc",
        "alignWithMargins"
    };

    private static readonly HashSet<string> ModeledMergeCellsAttributes = new(StringComparer.Ordinal)
    {
        "count"
    };

    private static readonly HashSet<string> ModeledMergeCellAttributes = new(StringComparer.Ordinal)
    {
        "ref"
    };

    // Every attribute XlsxWorksheetViewWriter.UpdateSheetView sets-or-removes from live Sheet state
    // (SetOrRemoveAttributeIfChanged, XlsxWorksheetViewWriter.cs). A "cleared" boolean flag (gridlines
    // shown, normal view, showFormulas off, rightToLeft off, ...) is encoded as attribute ABSENCE, so
    // it is indistinguishable from "the source never wrote this attribute" once the writer has already
    // run. MergeWorksheetSheetViews' call into XlsxNativeXmlMerger.MergeElementNativeAttributesAndChildren
    // must exclude every one of these names, or the merge treats the writer's intentional removal as a
    // gap and copies the value straight back from the untouched source sheetView (review P31).
    private static readonly IReadOnlyCollection<XName> ModeledSheetViewMergeAttributes =
    [
        "view",
        "showGridLines",
        "showRowColHeaders",
        "showRuler",
        "zoomScale",
        "showFormulas",
        "showZeros",
        "rightToLeft",
        "topLeftCell"
    ];
}
