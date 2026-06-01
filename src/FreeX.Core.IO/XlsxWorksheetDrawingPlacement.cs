using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// Inserts the worksheet <c>&lt;drawing&gt;</c> element at the schema-correct position. Per
/// CT_Worksheet, <c>drawing</c> must precede <c>tableParts</c>/<c>extLst</c> (and the other trailing
/// elements); appending it at the end produces an out-of-order worksheet that Excel refuses to open.
/// </summary>
internal static class XlsxWorksheetDrawingPlacement
{
    // CT_Worksheet elements that must come after <drawing>. The drawing is inserted immediately
    // before the first of these that is present.
    private static readonly HashSet<string> ElementsAfterDrawing = new(StringComparer.Ordinal)
    {
        "legacyDrawing", "legacyDrawingHF", "drawingHF", "picture",
        "oleObjects", "controls", "webPublishItems", "tableParts", "extLst",
    };

    public static void SetWorksheetDrawing(
        XElement root,
        XNamespace worksheetNs,
        XNamespace relNs,
        string drawingRelId)
    {
        root.Elements(worksheetNs + "drawing").Remove();
        var drawing = new XElement(worksheetNs + "drawing", new XAttribute(relNs + "id", drawingRelId));

        var anchor = root.Elements()
            .FirstOrDefault(element => ElementsAfterDrawing.Contains(element.Name.LocalName));
        if (anchor is not null)
            anchor.AddBeforeSelf(drawing);
        else
            root.Add(drawing);
    }
}
