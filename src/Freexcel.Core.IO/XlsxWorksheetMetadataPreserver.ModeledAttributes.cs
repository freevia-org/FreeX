namespace Freexcel.Core.IO;

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
}
