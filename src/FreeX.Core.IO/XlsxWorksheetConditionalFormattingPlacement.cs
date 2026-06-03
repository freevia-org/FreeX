using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Inserts worksheet <c>&lt;conditionalFormatting&gt;</c> elements at the schema-correct position. Per
/// CT_Worksheet, <c>conditionalFormatting</c> must appear after <c>mergeCells</c>/<c>phoneticPr</c> and
/// before <c>dataValidations</c>, <c>hyperlinks</c>, <c>printOptions</c>, <c>pageMargins</c>,
/// <c>pageSetup</c>, and every trailing element. Appending it at the end of the worksheet (after the
/// page-setup elements ClosedXML already wrote) produces an out-of-order worksheet that Excel refuses
/// to open; the Open XML SDK validator flags it as an unexpected child element.
/// </summary>
internal static class XlsxWorksheetConditionalFormattingPlacement
{
    // CT_Worksheet elements that must come after <conditionalFormatting>. A new conditionalFormatting
    // element is inserted immediately before the first of these that is present, so it joins any
    // existing conditionalFormatting block and stays ahead of the trailing worksheet content.
    private static readonly HashSet<string> ElementsAfterConditionalFormatting = new(StringComparer.Ordinal)
    {
        "dataValidations", "hyperlinks", "printOptions", "pageMargins", "pageSetup", "headerFooter",
        "rowBreaks", "colBreaks", "customProperties", "cellWatches", "ignoredErrors", "singleXmlCells",
        "smartTags", "drawing", "legacyDrawing", "legacyDrawingHF", "drawingHF", "picture", "oleObjects",
        "controls", "webPublishItems", "tableParts", "extLst",
    };

    public static void AddConditionalFormatting(XElement root, XNamespace worksheetNs, XElement conditionalFormatting)
    {
        // Keep new rules immediately after any conditionalFormatting already present so the block stays
        // contiguous; otherwise anchor before the first trailing element, or append when none exist.
        var lastExisting = root.Elements(worksheetNs + "conditionalFormatting").LastOrDefault();
        if (lastExisting is not null)
        {
            lastExisting.AddAfterSelf(conditionalFormatting);
            return;
        }

        var anchor = root.Elements()
            .FirstOrDefault(element => ElementsAfterConditionalFormatting.Contains(element.Name.LocalName));
        if (anchor is not null)
            anchor.AddBeforeSelf(conditionalFormatting);
        else
            root.Add(conditionalFormatting);
    }
}
