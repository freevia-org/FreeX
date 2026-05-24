using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static class XlsxWorksheetDiagnosticsMapper
{
    private const long MaxExpandedIgnoredErrorCells = 16384;

    public static IgnoredErrorLayout ReadIgnoredErrors(XDocument worksheetXml, XNamespace worksheetNs)
    {
        string[] supportedFlags =
        [
            "numberStoredAsText",
            "evalError",
            "formula",
            "formulaRange",
            "unlockedFormula",
            "emptyCellReference",
            "listDataValidation",
            "calculatedColumn",
            "twoDigitTextYear"
        ];

        var cells = new List<CellAddress>();
        var existingCellOnlyRanges = new List<GridRange>();
        var tempSheet = SheetId.New();
        foreach (var ignoredError in worksheetXml.Root?
                     .Element(worksheetNs + "ignoredErrors")?
                     .Elements(worksheetNs + "ignoredError") ?? [])
        {
            if (!supportedFlags.Any(flag => IsTruthy(ignoredError.Attribute(flag)?.Value)))
                continue;

            var sqref = ignoredError.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(sqref))
                continue;

            foreach (var token in sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!TryParseSqrefToken(token, tempSheet, out var range))
                    continue;

                if (range.CellCount > MaxExpandedIgnoredErrorCells)
                    existingCellOnlyRanges.Add(range);
                else
                    cells.AddRange(range.AllCells());
            }
        }

        return new IgnoredErrorLayout(cells, existingCellOnlyRanges);
    }

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
            InsertWorksheetMetadataElementInOrder(root, workbookNs, new XElement(
                workbookNs + "ignoredErrors",
                ignoredCells.Select(pair => new XElement(
                    workbookNs + "ignoredError",
                    new XAttribute("sqref", pair.Address.ToA1()),
                    new XAttribute("numberStoredAsText", "1"),
                    new XAttribute("evalError", "1"),
                    new XAttribute("formula", "1"),
                    new XAttribute("emptyCellReference", "1")))));

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
            InsertWorksheetMetadataElementInOrder(root, workbookNs, new XElement(
                workbookNs + "cellWatches",
                watchedCells.Select(address => new XElement(
                    workbookNs + "cellWatch",
                    new XAttribute("r", address.ToA1())))));

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

    public static bool MergeIgnoredErrors(XElement sourceIgnoredErrors, XElement targetRoot, XNamespace workbookNs)
    {
        var targetIgnoredErrors = targetRoot.Element(workbookNs + "ignoredErrors");
        if (targetIgnoredErrors is null)
        {
            InsertWorksheetMetadataElementInOrder(targetRoot, workbookNs, new XElement(sourceIgnoredErrors));
            return true;
        }

        var tempSheet = SheetId.New();
        var targetBySqref = targetIgnoredErrors
            .Elements(workbookNs + "ignoredError")
            .Where(element => !string.IsNullOrWhiteSpace(element.Attribute("sqref")?.Value))
            .GroupBy(element => element.Attribute("sqref")!.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var parsedTargets = targetIgnoredErrors
            .Elements(workbookNs + "ignoredError")
            .Select(element => new
            {
                Element = element,
                Parsed = TryParseSqrefCells(element.Attribute("sqref")?.Value, tempSheet, out var cells),
                Cells = cells
            })
            .Where(entry => entry.Parsed)
            .ToList();

        var changed = false;
        foreach (var sourceIgnoredError in sourceIgnoredErrors.Elements(workbookNs + "ignoredError"))
        {
            var sqref = sourceIgnoredError.Attribute("sqref")?.Value;
            if (!string.IsNullOrWhiteSpace(sqref) &&
                targetBySqref.TryGetValue(sqref, out var targetIgnoredError))
            {
                changed |= MergeMissingAttributes(sourceIgnoredError, targetIgnoredError);
                continue;
            }

            if (!TryParseSqrefCells(sqref, tempSheet, out var sourceCells))
            {
                targetIgnoredErrors.Add(new XElement(sourceIgnoredError));
                if (!string.IsNullOrWhiteSpace(sqref))
                    targetBySqref[sqref] = targetIgnoredErrors.Elements(workbookNs + "ignoredError").Last();
                changed = true;
                continue;
            }

            var overlappingTargets = parsedTargets
                .Where(target => target.Cells.Overlaps(sourceCells))
                .Select(target => target.Element)
                .ToList();
            if (overlappingTargets.Count > 0)
            {
                foreach (var overlappingTarget in overlappingTargets)
                    changed |= MergeMissingAttributes(sourceIgnoredError, overlappingTarget);

                continue;
            }

            targetIgnoredErrors.Add(new XElement(sourceIgnoredError));
            var addedIgnoredError = targetIgnoredErrors.Elements(workbookNs + "ignoredError").Last();
            if (!string.IsNullOrWhiteSpace(sqref))
                targetBySqref[sqref] = addedIgnoredError;
            parsedTargets.Add(new
            {
                Element = addedIgnoredError,
                Parsed = true,
                Cells = sourceCells
            });
            changed = true;
        }

        return changed;
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

    private static bool TryParseSqrefToken(string token, SheetId sheet, out GridRange range)
    {
        range = default;
        var parts = token.Split(':');
        if (parts.Length == 1)
        {
            if (!CellAddress.TryParse(parts[0], sheet, out var address))
                return false;

            range = new GridRange(address, address);
            return true;
        }

        if (parts.Length == 2 &&
            CellAddress.TryParse(parts[0], sheet, out var start) &&
            CellAddress.TryParse(parts[1], sheet, out var end))
        {
            range = new GridRange(start, end);
            return true;
        }

        return false;
    }

    private static bool TryParseSqrefCells(string? sqref, SheetId sheet, out HashSet<CellAddress> cells)
    {
        cells = [];
        if (string.IsNullOrWhiteSpace(sqref))
            return false;

        foreach (var token in sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParseSqrefToken(token, sheet, out var range))
                return false;
            if (range.CellCount > MaxExpandedIgnoredErrorCells ||
                (long)cells.Count + range.CellCount > MaxExpandedIgnoredErrorCells)
            {
                return false;
            }

            foreach (var cell in range.AllCells())
                cells.Add(cell);
        }

        return cells.Count > 0;
    }

    private static bool IsSupportedCellWatchReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        return CellAddress.TryParse(reference, SheetId.New(), out _);
    }

    private static bool MergeMissingAttributes(XElement sourceElement, XElement targetElement)
    {
        var changed = false;
        foreach (var attribute in sourceElement.Attributes())
        {
            if (targetElement.Attribute(attribute.Name) is not null)
                continue;

            targetElement.SetAttributeValue(attribute.Name, attribute.Value);
            changed = true;
        }

        return changed;
    }

    private static bool IsTruthy(string? value) =>
        value is "1" ||
        value is not null && bool.TryParse(value, out var parsed) && parsed;

    private static void InsertWorksheetMetadataElementInOrder(
        XElement worksheetRoot,
        XNamespace workbookNs,
        XElement metadataElement)
    {
        string[] laterWorksheetElements = metadataElement.Name.LocalName switch
        {
            "cellWatches" =>
            [
                "ignoredErrors",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            _ =>
            [
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ]
        };

        var insertionPoint = worksheetRoot.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == workbookNs &&
                laterWorksheetElements.Contains(element.Name.LocalName, StringComparer.Ordinal));
        if (insertionPoint is null)
            worksheetRoot.Add(metadataElement);
        else
            insertionPoint.AddBeforeSelf(metadataElement);
    }
}

internal sealed record IgnoredErrorLayout(
    IReadOnlyList<CellAddress> ExpandedCells,
    IReadOnlyList<GridRange> ExistingCellOnlyRanges);
