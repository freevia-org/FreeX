using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class XlsxWorksheetDiagnosticsMapper
{
    public static void SaveIgnoredErrors(Stream packageStream, Workbook workbook)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetPaths = GetSheetPaths(archive, workbookEntry, workbookNs);

        foreach (var sheet in workbook.Sheets)
        {
            var ignoredCells = sheet.EnumerateCells()
                .Where(pair => pair.Cell.IgnoreFormulaError)
                .OrderBy(pair => pair.Address.Row)
                .ThenBy(pair => pair.Address.Col)
                .ToList();
            if (ignoredCells.Count == 0)
                continue;

            if (!sheetPaths.TryGetValue(sheet.Name, out var worksheetPath))
                continue;

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            root.Element(workbookNs + "ignoredErrors")?.Remove();
            var ignoredErrors = new XElement(workbookNs + "ignoredErrors");
            foreach (var attribute in sheet.IgnoredErrorsMetadata?.NativeAttributes ?? [])
            {
                if (string.IsNullOrWhiteSpace(attribute.Key))
                    continue;

                ignoredErrors.SetAttributeValue(XName.Get(attribute.Key), attribute.Value);
            }

            foreach (var pair in ignoredCells)
            {
                var reference = pair.Address.ToA1();
                var ignoredError = new XElement(
                    workbookNs + "ignoredError",
                    new XAttribute("sqref", reference),
                    new XAttribute("numberStoredAsText", "1"),
                    new XAttribute("evalError", "1"),
                    new XAttribute("formula", "1"),
                    new XAttribute("emptyCellReference", "1"));
                if (TryGetIgnoredErrorNativeAttributes(sheet.IgnoredErrorsMetadata, reference, out var attributes))
                {
                    foreach (var attribute in attributes)
                    {
                        if (string.IsNullOrWhiteSpace(attribute.Key) ||
                            string.Equals(attribute.Key, "sqref", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        ignoredError.SetAttributeValue(XName.Get(attribute.Key), attribute.Value);
                    }
                }

                ignoredErrors.Add(ignoredError);
            }

            InsertWorksheetMetadataElementInOrder(root, workbookNs, ignoredErrors);

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    public static void SaveCellWatches(Stream packageStream, Workbook workbook)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetPaths = GetSheetPaths(archive, workbookEntry, workbookNs);
        var watchedCellsBySheet = workbook.WatchedCells
            .GroupBy(address => address.Sheet)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Distinct()
                    .OrderBy(address => address.Row)
                    .ThenBy(address => address.Col)
                    .ToList());

        foreach (var sheet in workbook.Sheets)
        {
            if (!watchedCellsBySheet.TryGetValue(sheet.Id, out var watchedCells) ||
                watchedCells.Count == 0 ||
                !sheetPaths.TryGetValue(sheet.Name, out var worksheetPath))
            {
                continue;
            }

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            root.Element(workbookNs + "cellWatches")?.Remove();
            var cellWatches = new XElement(workbookNs + "cellWatches");
            foreach (var attribute in sheet.CellWatchesMetadata?.NativeAttributes ?? [])
            {
                if (string.IsNullOrWhiteSpace(attribute.Key))
                    continue;

                cellWatches.SetAttributeValue(XName.Get(attribute.Key), attribute.Value);
            }

            foreach (var address in watchedCells)
            {
                var reference = address.ToA1();
                var cellWatch = new XElement(workbookNs + "cellWatch", new XAttribute("r", reference));
                if (sheet.CellWatchesMetadata?.WatchNativeAttributes.TryGetValue(reference, out var attributes) == true)
                {
                    foreach (var attribute in attributes)
                    {
                        if (string.IsNullOrWhiteSpace(attribute.Key) ||
                            string.Equals(attribute.Key, "r", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        cellWatch.SetAttributeValue(XName.Get(attribute.Key), attribute.Value);
                    }
                }

                cellWatches.Add(cellWatch);
            }

            InsertWorksheetMetadataElementInOrder(root, workbookNs, cellWatches);

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    private static Dictionary<string, string> GetSheetPaths(
        ZipArchive archive,
        ZipArchiveEntry workbookEntry,
        XNamespace workbookNs)
    {
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var workbookRels = XlsxRelationshipReader.LoadTargets(
            archive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        return XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(workbookXml, workbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);
    }
}
