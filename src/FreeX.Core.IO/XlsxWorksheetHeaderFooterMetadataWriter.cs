using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetHeaderFooterMetadataWriter
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
            var metadata = sheet.HeaderFooterMetadata;
            if (metadata is null)
                continue;

            if (!session.TryGetWorksheet(sheet, out var worksheetEdit))
                continue;

            var root = worksheetEdit.Root;
            var headerFooter = root.Element(worksheetNs + "headerFooter");
            if (headerFooter is null)
            {
                headerFooter = new XElement(worksheetNs + "headerFooter");
                root.Add(headerFooter);
            }

            var (hhAttrs, hhChildren) = XmlNativeBagSerializer.Deserialize(metadata.Get("headerFooter"));
            XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(
                headerFooter,
                hhAttrs,
                ["differentOddEven", "differentFirst", "scaleWithDoc", "alignWithMargins"]);

            foreach (var childXml in hhChildren)
            {
                XlsxWorksheetNativeMetadataHelpers.TryAddNativeChildElement(
                    headerFooter,
                    childXml,
                    ["oddHeader", "oddFooter", "evenHeader", "evenFooter", "firstHeader", "firstFooter"]);
            }

            XlsxWorksheetPageLayoutNormalizer.NormalizeHeaderFooter(headerFooter);
            session.MarkDirty(worksheetEdit);
        }
    }
}
