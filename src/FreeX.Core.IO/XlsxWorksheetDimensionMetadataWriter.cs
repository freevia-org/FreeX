using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetDimensionMetadataWriter
{
    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var session = new XlsxWorksheetXmlEditSession(xlsxStream, worksheetPathMap);
        Save(session, workbook);
    }

    internal static void Save(XlsxWorksheetXmlEditSession session, Workbook workbook)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var sheet in workbook.Sheets)
        {
            var metadata = sheet.DimensionMetadata;
            if (metadata is null)
                continue;

            if (!session.TryGetWorksheet(sheet, out var worksheetEdit))
                continue;

            var root = worksheetEdit.Root;
            var dimension = root.Element(worksheetNs + "dimension");
            if (dimension is null)
            {
                dimension = new XElement(worksheetNs + "dimension");
                InsertDimension(root, dimension);
            }

            var (dimAttrs, _) = XmlNativeBagSerializer.Deserialize(metadata.Get("dimension"));
            foreach (var attribute in dimAttrs)
            {
                if (string.IsNullOrWhiteSpace(attribute.Key) || string.Equals(attribute.Key, "ref", StringComparison.Ordinal))
                    continue;

                XlsxWorksheetNativeMetadataHelpers.TrySetNativeAttribute(dimension, attribute.Key, attribute.Value);
            }

            session.MarkDirty(worksheetEdit);
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
