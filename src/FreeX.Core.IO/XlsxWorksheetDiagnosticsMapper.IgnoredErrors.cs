using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static partial class XlsxWorksheetDiagnosticsMapper
{
    private const long MaxExpandedIgnoredErrorCells = 16384;
    private static readonly string[] SupportedIgnoredErrorFlags =
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

    public static IgnoredErrorLayout ReadIgnoredErrors(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var cells = new List<CellAddress>();
        var existingCellOnlyRanges = new List<GridRange>();
        var tempSheet = SheetId.New();
        foreach (var ignoredError in worksheetXml.Root?
                     .Element(worksheetNs + "ignoredErrors")?
                     .Elements(worksheetNs + "ignoredError") ?? [])
        {
            if (!IsSupportedIgnoredErrorElement(ignoredError))
                continue;

            var sqref = ignoredError.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(sqref))
                continue;

            foreach (var token in SplitSqrefTokens(sqref))
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

    public static WorksheetIgnoredErrorsMetadataModel? ReadIgnoredErrorsMetadata(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var ignoredErrors = worksheetXml.Root?.Element(worksheetNs + "ignoredErrors");
        if (ignoredErrors is null)
            return null;

        var model = new WorksheetIgnoredErrorsMetadataModel();
        foreach (var attribute in ignoredErrors.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
                continue;

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        foreach (var ignoredError in ignoredErrors.Elements(worksheetNs + "ignoredError"))
        {
            var sqref = ignoredError.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(sqref) || !IsSupportedIgnoredErrorElement(ignoredError))
                continue;

            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var attribute in ignoredError.Attributes())
            {
                if (attribute.IsNamespaceDeclaration ||
                    string.Equals(attribute.Name.LocalName, "sqref", StringComparison.Ordinal))
                {
                    continue;
                }

                attributes[attribute.Name.ToString()] = attribute.Value;
            }

            if (attributes.Count > 0)
                model.ErrorNativeAttributes[sqref] = attributes;
        }

        return model.NativeAttributes.Count == 0 && model.ErrorNativeAttributes.Count == 0
            ? null
            : model;
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
            var ignoredErrors = new XElement(workbookNs + "ignoredErrors");
            foreach (var attribute in sheet.IgnoredErrorsMetadata?.NativeAttributes ?? [])
            {
                if (string.IsNullOrWhiteSpace(attribute.Key))
                    continue;

                TrySetNativeAttribute(ignoredErrors, attribute.Key, attribute.Value);
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

                        TrySetNativeAttribute(ignoredError, attribute.Key, attribute.Value);
                    }
                }

                ignoredErrors.Add(ignoredError);
            }

            InsertWorksheetMetadataElementInOrder(root, workbookNs, ignoredErrors);

            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
        }
    }

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

    private static bool TryParseSqrefCells(string? sqref, SheetId sheet, out HashSet<CellAddress> cells)
    {
        cells = [];
        if (string.IsNullOrWhiteSpace(sqref))
            return false;

        foreach (var token in SplitSqrefTokens(sqref))
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

    private static bool IsSupportedIgnoredErrorElement(XElement ignoredError) =>
        SupportedIgnoredErrorFlags.Any(flag => IsTruthy(ignoredError.Attribute(flag)?.Value));

    private static bool TryGetIgnoredErrorNativeAttributes(
        WorksheetIgnoredErrorsMetadataModel? metadata,
        string reference,
        out Dictionary<string, string> attributes)
    {
        attributes = [];
        if (metadata is null)
            return false;

        if (metadata.ErrorNativeAttributes.TryGetValue(reference, out attributes!))
            return true;

        var tempSheet = SheetId.New();
        if (!CellAddress.TryParse(reference, tempSheet, out var address))
            return false;

        foreach (var pair in metadata.ErrorNativeAttributes)
        {
            foreach (var token in SplitSqrefTokens(pair.Key))
            {
                if (TryParseSqrefToken(token, tempSheet, out var range) && range.Contains(address))
                {
                    attributes = pair.Value;
                    return true;
                }
            }
        }

        return false;
    }

    private static string[] SplitSqrefTokens(string sqref) =>
        sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
