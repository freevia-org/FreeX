using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using System.Xml;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetPageBreaksMetadataWriter
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        foreach (var sheet in workbook.Sheets.Where(sheet =>
                     sheet.RowPageBreaksMetadata is not null ||
                     sheet.ColumnPageBreaksMetadata is not null))
        {
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

        var validBreaks = modeledBreaks
            .Where(id => id >= 2)
            .OrderBy(id => id)
            .ToList();
        foreach (var id in validBreaks)
        {
            if (pageBreaks.Elements(WorksheetNs + "brk")
                .Any(element => string.Equals(element.Attribute("id")?.Value, id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)))
            {
                continue;
            }

            pageBreaks.Add(new XElement(
                WorksheetNs + "brk",
                new XAttribute("id", id.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("man", "1")));
            changed = true;
        }

        foreach (var attribute in metadata.NativeAttributes)
        {
            if (string.IsNullOrWhiteSpace(attribute.Key) || string.Equals(attribute.Key, "count", StringComparison.Ordinal))
                continue;

            changed |= TrySetNativeAttribute(pageBreaks, attribute.Key, attribute.Value);
        }

        var breaksById = pageBreaks.Elements(WorksheetNs + "brk")
            .Where(element => !string.IsNullOrWhiteSpace(element.Attribute("id")?.Value))
            .GroupBy(element => element.Attribute("id")!.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var (breakId, attributes) in metadata.BreakNativeAttributes)
        {
            if (!breaksById.TryGetValue(breakId.ToString(CultureInfo.InvariantCulture), out var breakElement))
                continue;

            foreach (var attribute in attributes)
            {
                if (string.IsNullOrWhiteSpace(attribute.Key) || string.Equals(attribute.Key, "id", StringComparison.Ordinal))
                    continue;

                changed |= TrySetNativeAttribute(breakElement, attribute.Key, attribute.Value);
            }
        }

        return changed;
    }

    private static bool SetAttributeIfDifferent(XElement element, XName name, string value)
    {
        if (string.Equals(element.Attribute(name)?.Value, value, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(name, value);
        return true;
    }

    private static bool TrySetNativeAttribute(XElement element, string name, string value)
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
