using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetSheetPropertiesMetadataWriter
{
    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        foreach (var sheet in workbook.Sheets.Where(sheet => sheet.SheetPropertiesMetadata is not null))
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

            var sheetProperties = root.Element(worksheetNs + "sheetPr");
            if (sheetProperties is null)
            {
                sheetProperties = new XElement(worksheetNs + "sheetPr");
                root.AddFirst(sheetProperties);
            }

            foreach (var attribute in sheet.SheetPropertiesMetadata!.NativeAttributes)
            {
                if (string.IsNullOrWhiteSpace(attribute.Key) || IsModeledSheetPropertiesAttribute(attribute.Key))
                    continue;

                sheetProperties.SetAttributeValue(XName.Get(attribute.Key), attribute.Value);
            }

            if (sheet.SheetPropertiesMetadata.NativeChildXmls.Count > 0)
            {
                sheetProperties.Elements()
                    .Where(element => !IsModeledSheetPropertiesElement(element.Name.LocalName))
                    .Remove();

                foreach (var childXml in sheet.SheetPropertiesMetadata.NativeChildXmls)
                {
                    if (string.IsNullOrWhiteSpace(childXml))
                        continue;

                    try
                    {
                        sheetProperties.Add(XElement.Parse(childXml));
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

    private static bool IsModeledSheetPropertiesAttribute(string name) =>
        name is "codeName";

    private static bool IsModeledSheetPropertiesElement(string name) =>
        name is "tabColor" or "outlinePr" or "pageSetUpPr";
}
