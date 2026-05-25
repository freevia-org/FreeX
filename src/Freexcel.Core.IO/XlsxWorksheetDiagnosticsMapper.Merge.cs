using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

internal static partial class XlsxWorksheetDiagnosticsMapper
{
    public static bool MergeIgnoredErrors(
        XElement sourceIgnoredErrors,
        XElement targetRoot,
        XNamespace workbookNs,
        HashSet<CellAddress> modeledCells)
    {
        var targetIgnoredErrors = targetRoot.Element(workbookNs + "ignoredErrors");
        if (targetIgnoredErrors is null)
        {
            var retained = sourceIgnoredErrors
                .Elements(workbookNs + "ignoredError")
                .Where(element => !IsSupportedIgnoredErrorElement(element))
                .Select(element => new XElement(element))
                .ToList();
            if (retained.Count == 0)
                return false;

            InsertWorksheetMetadataElementInOrder(targetRoot, workbookNs, new XElement(workbookNs + "ignoredErrors", retained));
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
            if (IsSupportedIgnoredErrorElement(sourceIgnoredError) &&
                TryParseSqrefCells(sqref, tempSheet, out var parsedSourceCells) &&
                !parsedSourceCells.Overlaps(modeledCells))
            {
                continue;
            }

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

    public static HashSet<CellAddress> GetModeledIgnoredErrorCells(Workbook workbook, string sheetName)
    {
        var sheet = workbook.GetSheet(sheetName);
        if (sheet is null)
            return [];

        var tempSheet = SheetId.New();
        return sheet.EnumerateCells()
            .Where(pair => pair.Cell.IgnoreFormulaError)
            .Select(pair => new CellAddress(tempSheet, pair.Address.Row, pair.Address.Col))
            .ToHashSet();
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
}
