using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetElementOrder
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly IReadOnlyDictionary<string, int> SchemaOrder = new[]
    {
        "sheetPr",
        "dimension",
        "sheetViews",
        "sheetFormatPr",
        "cols",
        "sheetData",
        "sheetCalcPr",
        "sheetProtection",
        "protectedRanges",
        "scenarios",
        "autoFilter",
        "sortState",
        "dataConsolidate",
        "customSheetViews",
        "mergeCells",
        "phoneticPr",
        "conditionalFormatting",
        "dataValidations",
        "hyperlinks",
        "printOptions",
        "pageMargins",
        "pageSetup",
        "headerFooter",
        "rowBreaks",
        "colBreaks",
        "customProperties",
        "cellWatches",
        "ignoredErrors",
        "singleXmlCells",
        "smartTags",
        "drawing",
        "legacyDrawing",
        "legacyDrawingHF",
        "picture",
        "oleObjects",
        "controls",
        "webPublishItems",
        "tableParts",
        "extLst"
    }.Select((name, index) => (name, index))
        .ToDictionary(item => item.name, item => item.index, StringComparer.Ordinal);

    public static void Insert(XElement worksheetRoot, XElement element)
    {
        if (element.Name.Namespace != WorksheetNs ||
            !SchemaOrder.TryGetValue(element.Name.LocalName, out var elementOrder))
        {
            worksheetRoot.Add(element);
            return;
        }

        var insertionPoint = worksheetRoot.Elements()
            .FirstOrDefault(child =>
                child.Name.Namespace == WorksheetNs &&
                SchemaOrder.TryGetValue(child.Name.LocalName, out var childOrder) &&
                childOrder > elementOrder);

        if (insertionPoint is null)
            worksheetRoot.Add(element);
        else
            insertionPoint.AddBeforeSelf(element);
    }
}
