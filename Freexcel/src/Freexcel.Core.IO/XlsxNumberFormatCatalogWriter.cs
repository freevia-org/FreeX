using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxNumberFormatCatalogWriter
{
    public static IReadOnlyDictionary<int, int> Save(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var stylesEntry = archive.GetEntry("xl/styles.xml") ?? archive.CreateEntry("xl/styles.xml");
        var stylesXml = XlsxPackageXmlEditor.LoadXml(stylesEntry);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var root = stylesXml.Root;
        if (root is null)
            return new Dictionary<int, int>();

        var catalog = BuildNumberFormatCatalog(workbook);
        if (catalog.Count == 0)
            return new Dictionary<int, int>();

        var numFmts = root.Element(workbookNs + "numFmts");
        if (numFmts is null)
        {
            numFmts = new XElement(workbookNs + "numFmts");
            var firstFormatPeer = root.Elements()
                .FirstOrDefault(element => element.Name == workbookNs + "fonts" ||
                                           element.Name == workbookNs + "fills" ||
                                           element.Name == workbookNs + "borders" ||
                                           element.Name == workbookNs + "cellStyleXfs" ||
                                           element.Name == workbookNs + "cellXfs");
            if (firstFormatPeer is null)
                root.AddFirst(numFmts);
            else
                firstFormatPeer.AddBeforeSelf(numFmts);
        }

        var remap = new Dictionary<int, int>();
        var usedIds = numFmts.Elements(workbookNs + "numFmt")
            .Select(element => XlsxXmlAttributeReader.ReadIntAttribute(element, "numFmtId"))
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();
        var nextId = Math.Max(164, usedIds.Count == 0 ? 164 : usedIds.Max() + 1);
        foreach (var (numberFormatId, formatCode) in catalog.OrderBy(pair => pair.Key))
        {
            var existing = numFmts.Elements(workbookNs + "numFmt")
                .FirstOrDefault(element => XlsxXmlAttributeReader.ReadIntAttribute(element, "numFmtId") == numberFormatId);
            if (existing is not null &&
                string.Equals(existing.Attribute("formatCode")?.Value, formatCode, StringComparison.Ordinal))
            {
                remap[numberFormatId] = numberFormatId;
                continue;
            }

            if (existing is not null)
            {
                var equivalent = numFmts.Elements(workbookNs + "numFmt")
                    .FirstOrDefault(element =>
                        string.Equals(element.Attribute("formatCode")?.Value, formatCode, StringComparison.Ordinal) &&
                        XlsxXmlAttributeReader.ReadIntAttribute(element, "numFmtId") is { } equivalentId &&
                        equivalentId >= 164);
                if (equivalent is not null && XlsxXmlAttributeReader.ReadIntAttribute(equivalent, "numFmtId") is { } equivalentId)
                {
                    remap[numberFormatId] = equivalentId;
                    continue;
                }

                while (usedIds.Contains(nextId))
                    nextId++;
                remap[numberFormatId] = nextId;
                usedIds.Add(nextId);
                numFmts.Add(new XElement(
                    workbookNs + "numFmt",
                    new XAttribute("numFmtId", nextId.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("formatCode", formatCode)));
                nextId++;
                continue;
            }

            remap[numberFormatId] = numberFormatId;
            usedIds.Add(numberFormatId);
            numFmts.Add(new XElement(
                workbookNs + "numFmt",
                new XAttribute("numFmtId", numberFormatId.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("formatCode", formatCode)));
        }

        numFmts.SetAttributeValue("count", numFmts.Elements(workbookNs + "numFmt").Count().ToString(CultureInfo.InvariantCulture));
        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/styles.xml", stylesXml);
        return remap;
    }

    public static void RemapPivotTableNumberFormats(
        Stream xlsxStream,
        IReadOnlyDictionary<int, int> numberFormatIdMap)
    {
        var effectiveMap = numberFormatIdMap
            .Where(pair => pair.Key != pair.Value)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        if (effectiveMap.Count == 0)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        foreach (var pivotEntry in archive.Entries
                     .Where(entry =>
                         entry.FullName.StartsWith("xl/pivotTables/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            var pivotXml = XlsxPackageXmlEditor.LoadXml(pivotEntry);
            var changed = false;
            foreach (var dataField in pivotXml.Descendants().Where(element => element.Name.LocalName == "dataField"))
            {
                if (XlsxXmlAttributeReader.ReadIntAttribute(dataField, "numFmtId") is not { } numberFormatId ||
                    !effectiveMap.TryGetValue(numberFormatId, out var mappedId))
                {
                    continue;
                }

                dataField.SetAttributeValue("numFmtId", mappedId.ToString(CultureInfo.InvariantCulture));
                changed = true;
            }

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, pivotEntry.FullName, pivotXml);
        }
    }

    private static Dictionary<int, string> BuildNumberFormatCatalog(Workbook workbook)
    {
        var catalog = workbook.NumberFormatCatalog
            .Where(pair => pair.Key >= 164 && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        foreach (var field in workbook.Sheets
                     .SelectMany(sheet => sheet.PivotTables)
                     .SelectMany(pivot => pivot.DataFields))
        {
            if (field.NumberFormatId is >= 164 and var numberFormatId &&
                !string.IsNullOrWhiteSpace(field.NumberFormatCode))
            {
                catalog[numberFormatId] = field.NumberFormatCode;
            }
        }

        return catalog;
    }

}