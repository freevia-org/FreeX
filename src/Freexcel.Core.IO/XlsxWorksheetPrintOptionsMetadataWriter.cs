using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetPrintOptionsMetadataWriter
{
    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var sheet in workbook.Sheets.Where(sheet => sheet.PrintOptionsMetadata is not null))
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

            var printOptions = root.Element(worksheetNs + "printOptions");
            if (printOptions is null)
            {
                printOptions = new XElement(worksheetNs + "printOptions");
                InsertPrintOptions(root, worksheetNs, printOptions);
            }

            foreach (var attribute in sheet.PrintOptionsMetadata!.NativeAttributes)
            {
                if (string.IsNullOrWhiteSpace(attribute.Key) || IsModeledPrintOptionsAttribute(attribute.Key))
                    continue;

                printOptions.SetAttributeValue(XName.Get(attribute.Key), attribute.Value);
            }

            if (sheet.PrintOptionsMetadata.NativeChildXmls.Count > 0)
            {
                printOptions.Elements().Remove();
                foreach (var childXml in sheet.PrintOptionsMetadata.NativeChildXmls)
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

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
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
