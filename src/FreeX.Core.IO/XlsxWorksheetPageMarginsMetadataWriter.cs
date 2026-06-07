using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPageMarginsMetadataWriter
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
            var metadata = sheet.PageMarginsMetadata;
            if (metadata is null)
                continue;

            if (!session.TryGetWorksheet(sheet, out var worksheetEdit))
                continue;

            var root = worksheetEdit.Root;
            var pageMargins = root.Element(worksheetNs + "pageMargins");
            if (pageMargins is null)
            {
                pageMargins = new XElement(worksheetNs + "pageMargins");
                InsertPageMargins(root, worksheetNs, pageMargins);
            }

            var (pmAttrs, pmChildren) = XmlNativeBagSerializer.Deserialize(metadata.Get("pageMargins"));
            XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(
                pageMargins,
                pmAttrs,
                ["left", "right", "top", "bottom", "header", "footer"]);

            if (pmChildren.Count > 0)
            {
                pageMargins.Elements().Remove();
                foreach (var childXml in pmChildren)
                {
                    XlsxWorksheetNativeMetadataHelpers.TryAddNativeChildElement(pageMargins, childXml);
                }
            }

            XlsxWorksheetPageLayoutNormalizer.NormalizePageMargins(pageMargins);
            session.MarkDirty(worksheetEdit);
        }
    }

    private static void InsertPageMargins(XElement root, XNamespace worksheetNs, XElement pageMargins)
    {
        var pageSetup = root.Element(worksheetNs + "pageSetup");
        if (pageSetup is not null)
        {
            pageSetup.AddBeforeSelf(pageMargins);
            return;
        }

        var printOptions = root.Element(worksheetNs + "printOptions");
        if (printOptions is not null)
        {
            printOptions.AddAfterSelf(pageMargins);
            return;
        }

        root.Add(pageMargins);
    }
}
