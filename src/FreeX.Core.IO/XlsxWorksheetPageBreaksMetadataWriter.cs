using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPageBreaksMetadataWriter
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.RowPageBreaksMetadata is null &&
                sheet.ColumnPageBreaksMetadata is null)
            {
                continue;
            }

            if (!worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
                continue;

            var entry = archive.GetEntry(worksheetPath);
            if (entry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(entry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var changed = false;
            changed |= ApplyMetadata(root, "rowBreaks", sheet.RowPageBreaks, sheet.RowPageBreaksMetadata);
            changed |= ApplyMetadata(root, "colBreaks", sheet.ColumnPageBreaks, sheet.ColumnPageBreaksMetadata);
            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static bool ApplyMetadata(
        XElement root,
        string elementName,
        IEnumerable<uint> modeledBreaks,
        WorksheetPageBreaksMetadataModel? metadata)
    {
        if (metadata is null)
            return false;

        var changed = false;
        var pageBreaks = root.Element(WorksheetNs + elementName);
        if (pageBreaks is null)
        {
            pageBreaks = new XElement(WorksheetNs + elementName);
            root.Add(pageBreaks);
            changed = true;
        }

        var breaksById = BuildBreaksById(pageBreaks);
        foreach (var id in modeledBreaks)
        {
            if (id < 2)
                continue;

            var idText = id.ToString(CultureInfo.InvariantCulture);
            if (breaksById.ContainsKey(idText))
                continue;

            var breakElement = new XElement(
                WorksheetNs + "brk",
                new XAttribute("id", idText),
                new XAttribute("man", "1"));
            pageBreaks.Add(breakElement);
            breaksById[idText] = breakElement;
            changed = true;
        }

        foreach (var attribute in metadata.NativeAttributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Key) || string.Equals(attribute.Key, "count", StringComparison.Ordinal))
                continue;

            changed |= TrySetNativeAttributeIfDifferent(pageBreaks, attribute.Key, attribute.Value);
        }

        foreach (var (breakId, attributes) in metadata.BreakNativeAttributes)
        {
            if (!breaksById.TryGetValue(breakId.ToString(CultureInfo.InvariantCulture), out var breakElement))
                continue;

            foreach (var attribute in attributes)
            {
                if (string.IsNullOrWhiteSpace(attribute.Key) || string.Equals(attribute.Key, "id", StringComparison.Ordinal))
                    continue;

                changed |= TrySetNativeAttributeIfDifferent(breakElement, attribute.Key, attribute.Value);
            }
        }

        return changed;
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
