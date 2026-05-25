using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetPrimaryViewMetadataWriter
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static void Save(Stream xlsxStream, Workbook workbook, XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        foreach (var sheet in workbook.Sheets.Where(sheet => sheet.PrimaryViewMetadata is not null))
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

            var sheetViews = root.Element(WorksheetNs + "sheetViews");
            if (sheetViews is null)
            {
                sheetViews = new XElement(WorksheetNs + "sheetViews");
                root.AddFirst(sheetViews);
            }

            var sheetView = sheetViews.Elements(WorksheetNs + "sheetView")
                .FirstOrDefault(element => string.Equals(element.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal));
            if (sheetView is null)
            {
                sheetView = new XElement(WorksheetNs + "sheetView", new XAttribute("workbookViewId", "0"));
                sheetViews.AddFirst(sheetView);
            }

            foreach (var attribute in sheet.PrimaryViewMetadata!.NativeAttributes)
            {
                if (string.IsNullOrWhiteSpace(attribute.Key) || IsModeledPrimaryViewAttribute(attribute.Key))
                    continue;

                sheetView.SetAttributeValue(XName.Get(attribute.Key), attribute.Value);
            }

            if (sheet.PrimaryViewMetadata.NativeChildXmls.Count > 0)
            {
                sheetView.Elements()
                    .Where(element => !IsModeledPrimaryViewElement(element.Name.LocalName))
                    .Remove();

                foreach (var childXml in sheet.PrimaryViewMetadata.NativeChildXmls)
                {
                    if (string.IsNullOrWhiteSpace(childXml))
                        continue;

                    try
                    {
                        sheetView.Add(XElement.Parse(childXml));
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

    private static bool IsModeledPrimaryViewAttribute(string name) =>
        name is "workbookViewId" or "view" or "showGridLines" or "showRowColHeaders" or "showRuler" or
            "zoomScale" or "showFormulas" or "topLeftCell";

    private static bool IsModeledPrimaryViewElement(string name) =>
        name is "pane";
}
