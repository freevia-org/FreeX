using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetDimensionMetadataWriter
{
    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var sheet in workbook.Sheets.Where(sheet => sheet.DimensionMetadata is not null))
        {
            if (!worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
                continue;

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var dimension = root.Element(worksheetNs + "dimension");
            if (dimension is null)
            {
                dimension = new XElement(worksheetNs + "dimension");
                InsertDimension(root, dimension);
            }

            foreach (var attribute in sheet.DimensionMetadata!.NativeAttributes)
            {
                if (string.IsNullOrWhiteSpace(attribute.Key) || string.Equals(attribute.Key, "ref", StringComparison.Ordinal))
                    continue;

                dimension.SetAttributeValue(XName.Get(attribute.Key), attribute.Value);
            }

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static void InsertDimension(XElement root, XElement dimension)
    {
        var firstChild = root.Elements().FirstOrDefault();
        if (firstChild is not null)
        {
            firstChild.AddBeforeSelf(dimension);
            return;
        }

        root.Add(dimension);
    }
}
