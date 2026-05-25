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
                PruneSelectionsForModeledActiveCell(sheetView, sheet);

                sheetView.Elements()
                    .Where(element => !IsModeledPrimaryViewElement(element.Name.LocalName))
                    .Remove();

                foreach (var childXml in sheet.PrimaryViewMetadata.NativeChildXmls)
                {
                    if (string.IsNullOrWhiteSpace(childXml))
                        continue;

                try
                {
                    var nativeChild = XElement.Parse(childXml);
                    if (nativeChild.Name == WorksheetNs + "selection")
                    {
                        MergeMatchingSelectionNativeAttributes(sheetView, nativeChild);
                        continue;
                    }

                    sheetView.Add(nativeChild);
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
        name is "pane" or "selection";

    private static void PruneSelectionsForModeledActiveCell(XElement sheetView, Sheet sheet)
    {
        if (sheet.ActiveRow is not { } row || sheet.ActiveCol is not { } col)
            return;

        var activeCell = new CellAddress(sheet.Id, row, col).ToA1();
        var matchingSelectionKept = false;
        foreach (var selection in sheetView.Elements(WorksheetNs + "selection").ToList())
        {
            var isModeledSelection =
                string.Equals(selection.Attribute("activeCell")?.Value, activeCell, StringComparison.Ordinal) &&
                string.Equals(selection.Attribute("sqref")?.Value, activeCell, StringComparison.Ordinal);
            if (!isModeledSelection || matchingSelectionKept)
                selection.Remove();
            else
                matchingSelectionKept = true;
        }
    }

    private static void MergeMatchingSelectionNativeAttributes(XElement sheetView, XElement nativeSelection)
    {
        var nativeActiveCell = nativeSelection.Attribute("activeCell")?.Value;
        var nativeSelectionRef = nativeSelection.Attribute("sqref")?.Value;
        if (string.IsNullOrWhiteSpace(nativeActiveCell) || string.IsNullOrWhiteSpace(nativeSelectionRef))
            return;

        var targetSelection = sheetView.Elements(WorksheetNs + "selection")
            .FirstOrDefault(selection =>
                string.Equals(selection.Attribute("activeCell")?.Value, nativeActiveCell, StringComparison.Ordinal) &&
                string.Equals(selection.Attribute("sqref")?.Value, nativeSelectionRef, StringComparison.Ordinal));
        if (targetSelection is null)
            return;

        foreach (var attribute in nativeSelection.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || attribute.Name.LocalName is "activeCell" or "sqref")
                continue;

            targetSelection.SetAttributeValue(attribute.Name, attribute.Value);
        }
    }
}
