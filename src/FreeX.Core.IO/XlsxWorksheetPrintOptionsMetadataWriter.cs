using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPrintOptionsMetadataWriter
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
            var metadata = sheet.PrintOptionsMetadata;
            if (metadata is null)
                continue;

            if (!session.TryGetWorksheet(sheet, out var worksheetEdit))
                continue;

            var root = worksheetEdit.Root;
            var printOptions = root.Element(worksheetNs + "printOptions");
            if (printOptions is null)
            {
                printOptions = new XElement(worksheetNs + "printOptions");
                InsertPrintOptions(root, worksheetNs, printOptions);
            }

            var (poAttrs, poChildren) = XmlNativeBagSerializer.Deserialize(metadata.Get("printOptions"));
            foreach (var attribute in poAttrs)
            {
                if (string.IsNullOrWhiteSpace(attribute.Key) || IsModeledPrintOptionsAttribute(attribute.Key))
                    continue;

                XlsxWorksheetNativeMetadataHelpers.TrySetNativeAttribute(printOptions, attribute.Key, attribute.Value);
            }

            if (poChildren.Count > 0)
            {
                printOptions.Elements().Remove();
                foreach (var childXml in poChildren)
                {
                    if (string.IsNullOrWhiteSpace(childXml))
                        continue;

                    try
                    {
                        printOptions.Add(XElement.Parse(childXml));
                    }
                    catch
                    {
                        // Skip malformed native payloads in authored native JSON files.
                    }
                }
            }

            session.MarkDirty(worksheetEdit);
        }
    }

    private static bool IsModeledPrintOptionsAttribute(string name) =>
        name is "gridLines" or "headings" or "horizontalCentered" or "verticalCentered";

    private static void InsertPrintOptions(XElement root, XNamespace worksheetNs, XElement printOptions)
    {
        var pageMargins = root.Element(worksheetNs + "pageMargins");
        if (pageMargins is not null)
        {
            pageMargins.AddBeforeSelf(printOptions);
            return;
        }

        var sheetData = root.Element(worksheetNs + "sheetData");
        if (sheetData is not null)
        {
            sheetData.AddAfterSelf(printOptions);
            return;
        }

        root.Add(printOptions);
    }
}
