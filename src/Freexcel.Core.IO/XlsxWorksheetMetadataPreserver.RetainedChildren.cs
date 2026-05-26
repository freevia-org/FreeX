using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static partial class XlsxWorksheetMetadataPreserver
{
    private static XName[] GetRetainedWorksheetChildNames(XNamespace workbookNs) =>
    [
        workbookNs + "customSheetViews",
        workbookNs + "scenarios",
        workbookNs + "ignoredErrors",
        workbookNs + "cellWatches",
        workbookNs + "sheetCalcPr",
        workbookNs + "phoneticPr",
        workbookNs + "sortState",
        workbookNs + "dataConsolidate",
        workbookNs + "legacyDrawing",
        workbookNs + "legacyDrawingHF",
        workbookNs + "picture",
        workbookNs + "customProperties",
        workbookNs + "smartTags",
        workbookNs + "singleXmlCells",
        workbookNs + "autoFilter",
        workbookNs + "protectedRanges",
        workbookNs + "rowBreaks",
        workbookNs + "colBreaks",
        workbookNs + "queryTableParts",
        workbookNs + "webPublishItems",
        workbookNs + "oleObjects",
        workbookNs + "controls"
    ];
}
