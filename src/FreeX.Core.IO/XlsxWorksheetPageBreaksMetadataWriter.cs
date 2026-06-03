using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPageBreaksMetadataWriter
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const uint RowBreakSpanMax = CellAddress.MaxCol - 1;
    private const uint ColumnBreakSpanMax = CellAddress.MaxRow - 1;

    public static bool HasModeledBreaksOrMetadata(Sheet sheet) =>
        sheet.RowPageBreaks.Any(id => IsSupportedBreakId(id, CellAddress.MaxRow)) ||
        sheet.ColumnPageBreaks.Any(id => IsSupportedBreakId(id, CellAddress.MaxCol)) ||
        sheet.RowPageBreaksMetadata is not null ||
        sheet.ColumnPageBreaksMetadata is not null;

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap);
        Save(session, workbook);
    }

    internal static void Save(XlsxWorksheetXmlEditSession session, Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (!HasModeledBreaksOrMetadata(sheet))
                continue;

            if (!session.TryGetWorksheet(sheet, out var worksheetEdit))
                continue;

            var root = worksheetEdit.Root;
            var changed = false;
            changed |= ApplyBreaks(
                root,
                "rowBreaks",
                sheet.RowPageBreaks,
                sheet.RowPageBreaksMetadata,
                CellAddress.MaxRow,
                RowBreakSpanMax);
            changed |= ApplyBreaks(
                root,
                "colBreaks",
                sheet.ColumnPageBreaks,
                sheet.ColumnPageBreaksMetadata,
                CellAddress.MaxCol,
                ColumnBreakSpanMax);
            if (changed)
                session.MarkDirty(worksheetEdit);
        }
    }

    private static bool ApplyBreaks(
        XElement root,
        string elementName,
        IEnumerable<uint> modeledBreaks,
        WorksheetPageBreaksMetadataModel? metadata,
        uint maxBreakId,
        uint defaultSpanMax)
    {
        var validModeledBreaks = modeledBreaks
            .Where(id => IsSupportedBreakId(id, maxBreakId))
            .Distinct()
            .ToArray();
        if (metadata is null && validModeledBreaks.Length == 0)
            return false;

        var changed = false;
        var pageBreaks = root.Element(WorksheetNs + elementName);
        if (pageBreaks is null)
        {
            pageBreaks = new XElement(WorksheetNs + elementName);
            InsertPageBreaksInOrder(root, pageBreaks);
            changed = true;
        }

        var breaksById = BuildBreaksById(pageBreaks);
        foreach (var id in validModeledBreaks)
        {
            var idText = id.ToString(CultureInfo.InvariantCulture);
            if (!breaksById.TryGetValue(idText, out var breakElement))
            {
                breakElement = new XElement(WorksheetNs + "brk", new XAttribute("id", idText));
                pageBreaks.Add(breakElement);
                breaksById[idText] = breakElement;
                changed = true;
            }

            changed |= SetAttributeIfDifferent(
                breakElement,
                "max",
                defaultSpanMax.ToString(CultureInfo.InvariantCulture));
            changed |= SetAttributeIfDifferent(breakElement, "man", "1");
        }

        if (metadata is not null)
        {
            foreach (var attribute in metadata.NativeAttributes)
            {
                if (string.IsNullOrWhiteSpace(attribute.Key) ||
                    string.Equals(attribute.Key, "count", StringComparison.Ordinal))
                {
                    continue;
                }

                changed |= TrySetNativeAttributeIfDifferent(pageBreaks, attribute.Key, attribute.Value);
            }

            foreach (var (breakId, attributes) in metadata.BreakNativeAttributes)
            {
                if (!breaksById.TryGetValue(breakId.ToString(CultureInfo.InvariantCulture), out var breakElement))
                    continue;

                foreach (var attribute in attributes)
                {
                    if (string.IsNullOrWhiteSpace(attribute.Key) ||
                        string.Equals(attribute.Key, "id", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    changed |= TrySetNativeAttributeIfDifferent(breakElement, attribute.Key, attribute.Value);
                }
            }
        }

        var breakCount = pageBreaks.Elements(WorksheetNs + "brk").Count();
        changed |= SetAttributeIfDifferent(pageBreaks, "count", breakCount.ToString(CultureInfo.InvariantCulture));
        if (metadata?.NativeAttributes.ContainsKey("manualBreakCount") != true)
        {
            var manualBreakCount = pageBreaks
                .Elements(WorksheetNs + "brk")
                .Count(element => !string.Equals(element.Attribute("man")?.Value, "0", StringComparison.Ordinal));
            changed |= SetAttributeIfDifferent(
                pageBreaks,
                "manualBreakCount",
                manualBreakCount.ToString(CultureInfo.InvariantCulture));
        }

        return changed;
    }

    private static bool IsSupportedBreakId(uint id, uint maxBreakId) => id is >= 2 && id <= maxBreakId;

    private static void InsertPageBreaksInOrder(XElement root, XElement pageBreaks)
    {
        var elementName = pageBreaks.Name.LocalName;
        if (string.Equals(elementName, "rowBreaks", StringComparison.Ordinal))
        {
            var columnBreaks = root.Element(WorksheetNs + "colBreaks");
            if (columnBreaks is not null)
            {
                columnBreaks.AddBeforeSelf(pageBreaks);
                return;
            }
        }

        if (string.Equals(elementName, "colBreaks", StringComparison.Ordinal))
        {
            var rowBreaks = root.Element(WorksheetNs + "rowBreaks");
            if (rowBreaks is not null)
            {
                rowBreaks.AddAfterSelf(pageBreaks);
                return;
            }
        }

        string[] laterWorksheetElements =
        [
            "customProperties",
            "cellWatches",
            "ignoredErrors",
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
        ];

        var insertionPoint = root.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == WorksheetNs &&
                laterWorksheetElements.Contains(element.Name.LocalName, StringComparer.Ordinal));
        if (insertionPoint is null)
            root.Add(pageBreaks);
        else
            insertionPoint.AddBeforeSelf(pageBreaks);
    }

    private static Dictionary<string, XElement> BuildBreaksById(XElement pageBreaks)
    {
        var breaksById = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var breakElement in pageBreaks.Elements(WorksheetNs + "brk"))
        {
            var id = breakElement.Attribute("id")?.Value;
            if (!string.IsNullOrWhiteSpace(id) && !breaksById.ContainsKey(id))
                breaksById[id] = breakElement;
        }

        return breaksById;
    }

    private static bool SetAttributeIfDifferent(XElement element, XName name, string value)
    {
        if (string.Equals(element.Attribute(name)?.Value, value, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(name, value);
        return true;
    }

    private static bool TrySetNativeAttributeIfDifferent(XElement element, string name, string value)
    {
        try
        {
            return SetAttributeIfDifferent(element, XName.Get(name), value);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (XmlException)
        {
            return false;
        }
    }
}
