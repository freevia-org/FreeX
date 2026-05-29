using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxWorksheetDiagnosticsMapper
{
    public static IReadOnlyList<CellAddress> ReadCellWatches(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var watchedCells = new List<CellAddress>();
        var seen = new HashSet<CellAddress>();
        var tempSheet = SheetId.New();
        foreach (var cellWatch in worksheetXml.Root?
                     .Element(worksheetNs + "cellWatches")?
                     .Elements(worksheetNs + "cellWatch") ?? [])
        {
            var reference = cellWatch.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(reference) ||
                !CellAddress.TryParse(reference, tempSheet, out var address) ||
                !seen.Add(address))
            {
                continue;
            }

            watchedCells.Add(address);
        }

        return watchedCells;
    }

    public static WorksheetCellWatchesMetadataModel? ReadCellWatchesMetadata(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var cellWatches = worksheetXml.Root?.Element(worksheetNs + "cellWatches");
        if (cellWatches is null)
            return null;

        var model = new WorksheetCellWatchesMetadataModel();
        foreach (var attribute in cellWatches.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        foreach (var cellWatch in cellWatches.Elements(worksheetNs + "cellWatch"))
        {
            var reference = cellWatch.Attribute("r")?.Value;
            if (!IsSupportedCellWatchReference(reference))
                continue;

            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var attribute in cellWatch.Attributes())
            {
                if (attribute.IsNamespaceDeclaration ||
                    string.Equals(attribute.Name.LocalName, "r", StringComparison.Ordinal))
                {
                    continue;
                }

                attributes[attribute.Name.ToString()] = attribute.Value;
            }

            if (attributes.Count > 0)
                model.WatchNativeAttributes[reference!] = attributes;
        }

        return model.NativeAttributes.Count == 0 && model.WatchNativeAttributes.Count == 0
            ? null
            : model;
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

                TrySetNativeAttribute(cellWatches, attribute.Key, attribute.Value);
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

                        TrySetNativeAttribute(cellWatch, attribute.Key, attribute.Value);
                    }
                }

                cellWatches.Add(cellWatch);
            }

            InsertWorksheetMetadataElementInOrder(root, workbookNs, cellWatches);

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    public static bool MergeCellWatches(
        XElement sourceCellWatches,
        XElement targetRoot,
        XNamespace workbookNs,
        HashSet<string> modeledReferences)
    {
        var targetCellWatches = targetRoot.Element(workbookNs + "cellWatches");
        if (targetCellWatches is null)
        {
            var retainedUnsupported = sourceCellWatches
                .Elements(workbookNs + "cellWatch")
                .Where(element => !IsSupportedCellWatchReference(element.Attribute("r")?.Value))
                .Select(element => new XElement(element))
                .ToList();
            if (retainedUnsupported.Count == 0)
                return false;

            InsertWorksheetMetadataElementInOrder(
                targetRoot,
                workbookNs,
                new XElement(workbookNs + "cellWatches", retainedUnsupported));
            return true;
        }

        var targetByReference = targetCellWatches
            .Elements(workbookNs + "cellWatch")
            .Where(element => !string.IsNullOrWhiteSpace(element.Attribute("r")?.Value))
            .GroupBy(element => element.Attribute("r")!.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var sourceCellWatch in sourceCellWatches.Elements(workbookNs + "cellWatch"))
        {
            var reference = sourceCellWatch.Attribute("r")?.Value;
            if (IsSupportedCellWatchReference(reference))
            {
                if (modeledReferences.Contains(reference!) &&
                    targetByReference.TryGetValue(reference!, out var targetCellWatch))
                {
                    changed |= MergeMissingAttributes(sourceCellWatch, targetCellWatch);
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(reference) &&
                targetByReference.ContainsKey(reference))
            {
                continue;
            }

            targetCellWatches.Add(new XElement(sourceCellWatch));
            if (!string.IsNullOrWhiteSpace(reference))
                targetByReference[reference] = targetCellWatches.Elements(workbookNs + "cellWatch").Last();
            changed = true;
        }

        return changed;
    }

    public static HashSet<string> GetModeledCellWatchReferences(Workbook workbook, string sheetName)
    {
        var sheet = workbook.GetSheet(sheetName);
        if (sheet is null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return workbook.WatchedCells
            .Where(address => address.Sheet == sheet.Id)
            .Select(address => address.ToA1())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSupportedCellWatchReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        return CellAddress.TryParse(reference, SheetId.New(), out _);
    }
}
