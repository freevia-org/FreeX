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
        XlsxWorksheetNativeMetadataHelpers.ReadNativeAttributes(ignoredErrors, model.NativeAttributes, []);

        foreach (var ignoredError in ignoredErrors.Elements(worksheetNs + "ignoredError"))
        {
            var sqref = ignoredError.Attribute("sqref")?.Value;
            if (string.IsNullOrWhiteSpace(sqref) || !IsSupportedIgnoredErrorElement(ignoredError))
                continue;

            Dictionary<string, string>? attributes = null;
            foreach (var attribute in ignoredError.Attributes())
            {
                if (attribute.IsNamespaceDeclaration ||
                    ShouldSkipIgnoredErrorNativeAttribute(attribute.Name.ToString()))
                {
                    continue;
                }

                attributes ??= new Dictionary<string, string>(StringComparer.Ordinal);
                attributes[attribute.Name.ToString()] = attribute.Value;
            }

            if (attributes?.Count > 0)
                model.ErrorNativeAttributes[sqref] = attributes;
        }

        return model.NativeAttributes.Count == 0 && model.ErrorNativeAttributes.Count == 0
            ? null
            : model;
    }

    public static void SaveIgnoredErrors(Stream packageStream, Workbook workbook)
    {
        XlsxWorkbookWorksheetPathMap? worksheetPathMap;
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true))
            worksheetPathMap = XlsxWorkbookWorksheetPathMap.TryCreate(archive);

        if (packageStream.CanSeek)
            packageStream.Position = 0;

        SaveIgnoredErrors(packageStream, workbook, worksheetPathMap);
    }

    public static void SaveIgnoredErrors(
        Stream packageStream,
        Workbook workbook,
        XlsxWorkbookWorksheetPathMap? worksheetPathMap)
    {
        if (worksheetPathMap is null)
            return;

        using var session = new XlsxWorksheetXmlEditSession(packageStream, worksheetPathMap);
        SaveIgnoredErrors(session, workbook);
    }

    internal static void SaveIgnoredErrors(XlsxWorksheetXmlEditSession session, Workbook workbook)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var sheet in workbook.Sheets)
        {
            var ignoredErrorRuns = BuildIgnoredErrorRuns(sheet);
            if (ignoredErrorRuns.Count == 0)
                continue;

            if (!session.TryGetWorksheet(sheet, out var edit))
                continue;

            var root = edit.Root;
            root.Element(workbookNs + "ignoredErrors")?.Remove();
            var ignoredErrors = new XElement(workbookNs + "ignoredErrors");
            if (sheet.IgnoredErrorsMetadata is not null)
                XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(ignoredErrors, sheet.IgnoredErrorsMetadata.NativeAttributes, []);

            foreach (var run in ignoredErrorRuns)
            {
                var ignoredError = new XElement(workbookNs + "ignoredError", new XAttribute("sqref", run.ToSqref()));
                if (run.NativeAttributes is { Count: > 0 })
                {
                    // Fidelity path: re-emit exactly the ignoredError flags that were present on the
                    // originating cell(s) (captured in ReadIgnoredErrorsMetadata), instead of broadening
                    // to every supported flag. A cell whose author ignored only e.g. unlockedFormula must
                    // not come back out suppressing evalError/formula/numberStoredAsText/etc. too.
                    XlsxWorksheetNativeMetadataHelpers.ApplyNativeAttributes(ignoredError, run.NativeAttributes, ["sqref"]);
                }
                else
                {
                    // No source flag fidelity available (e.g. a cell newly marked "Ignore Error" via the
                    // UI, which does not yet track which specific rule was ignored) -- fall back to the
                    // historical broad default so the ignore still takes effect.
                    ignoredError.SetAttributeValue("numberStoredAsText", "1");
                    ignoredError.SetAttributeValue("evalError", "1");
                    ignoredError.SetAttributeValue("formula", "1");
                    ignoredError.SetAttributeValue("emptyCellReference", "1");
                }

                ignoredErrors.Add(ignoredError);
            }

            InsertWorksheetMetadataElementInOrder(root, workbookNs, ignoredErrors);
            session.MarkDirty(edit);
        }
    }

    private static List<IgnoredErrorRun> BuildIgnoredErrorRuns(Sheet sheet)
    {
        var occupiedCells = sheet.GetOccupiedCellMap();
        var ignoredCellCount = 0;
        foreach (var pair in occupiedCells)
        {
            if (pair.Value.IgnoreFormulaError)
                ignoredCellCount++;
        }

        if (ignoredCellCount == 0)
            return [];

        var ignoredCells = new List<(uint Row, uint Col)>(ignoredCellCount);
        var ignoredCellsAreRowMajor = true;
        var hasPreviousIgnoredCell = false;
        uint previousRow = 0;
        uint previousCol = 0;
        foreach (var pair in occupiedCells)
        {
            if (!pair.Value.IgnoreFormulaError)
                continue;

            var (row, col) = pair.Key;
            if (hasPreviousIgnoredCell &&
                (row < previousRow || (row == previousRow && col < previousCol)))
            {
                ignoredCellsAreRowMajor = false;
            }

            ignoredCells.Add((row, col));
            hasPreviousIgnoredCell = true;
            previousRow = row;
            previousCol = col;
        }

        if (!ignoredCellsAreRowMajor)
        {
            ignoredCells.Sort(static (left, right) =>
            {
                var rowCompare = left.Row.CompareTo(right.Row);
                return rowCompare != 0 ? rowCompare : left.Col.CompareTo(right.Col);
            });
        }

        var nativeAttributeLookup = IgnoredErrorNativeAttributeLookup.Create(sheet.IgnoredErrorsMetadata);
        var runs = new List<IgnoredErrorRun>(Math.Min(ignoredCells.Count, 1024));
        var first = ignoredCells[0];
        var currentRun = new IgnoredErrorRun(
            first.Row,
            first.Row,
            first.Col,
            first.Col,
            nativeAttributeLookup.GetNativeAttributes(sheet.Id, first.Row, first.Col));

        for (var i = 1; i < ignoredCells.Count; i++)
        {
            var address = ignoredCells[i];
            var nativeAttributes = nativeAttributeLookup.GetNativeAttributes(sheet.Id, address.Row, address.Col);
            if (address.Row == currentRun.StartRow &&
                address.Col == currentRun.EndCol + 1 &&
                HaveSameIgnoredErrorNativeAttributes(currentRun.NativeAttributes, nativeAttributes))
            {
                currentRun = currentRun with { EndCol = address.Col };
                continue;
            }

            AddMergedIgnoredErrorRun(runs, currentRun);
            currentRun = new IgnoredErrorRun(address.Row, address.Row, address.Col, address.Col, nativeAttributes);
        }

        AddMergedIgnoredErrorRun(runs, currentRun);
        return runs;
    }

    private static void AddMergedIgnoredErrorRun(List<IgnoredErrorRun> runs, IgnoredErrorRun run)
    {
        if (runs.Count > 0)
        {
            var previous = runs[^1];
            if (previous.EndRow + 1 == run.StartRow &&
                previous.StartCol == run.StartCol &&
                previous.EndCol == run.EndCol &&
                HaveSameIgnoredErrorNativeAttributes(previous.NativeAttributes, run.NativeAttributes))
            {
                runs[^1] = previous with { EndRow = run.EndRow };
                return;
            }
        }

        runs.Add(run);
    }

    private static bool HaveSameIgnoredErrorNativeAttributes(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) ||
                !string.Equals(pair.Value, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
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

    private static bool IsSupportedIgnoredErrorElement(XElement ignoredError)
    {
        foreach (var flag in SupportedIgnoredErrorFlags)
        {
            if (IsTruthy(ignoredError.Attribute(flag)?.Value))
                return true;
        }

        return false;
    }

    private static bool ShouldSkipIgnoredErrorNativeAttribute(string key) =>
        string.IsNullOrWhiteSpace(key) ||
        string.Equals(key, "sqref", StringComparison.Ordinal);

    private static string[] SplitSqrefTokens(string sqref) =>
        sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private readonly record struct IgnoredErrorRun(
        uint StartRow,
        uint EndRow,
        uint StartCol,
        uint EndCol,
        IReadOnlyDictionary<string, string>? NativeAttributes)
    {
        public string ToSqref()
        {
            var start = new CellAddress(default, StartRow, StartCol).ToA1();
            if (StartRow == EndRow && StartCol == EndCol)
                return start;

            var end = new CellAddress(default, EndRow, EndCol).ToA1();
            return $"{start}:{end}";
        }
    }

    private sealed class IgnoredErrorNativeAttributeLookup
    {
        public static IgnoredErrorNativeAttributeLookup Empty { get; } = new(SheetId.New(), null, []);

        private readonly SheetId _lookupSheet;
        private readonly WorksheetIgnoredErrorsMetadataModel? _metadata;
        private readonly List<(GridRange Range, Dictionary<string, string> Attributes)> _ranges;

        private IgnoredErrorNativeAttributeLookup(
            SheetId lookupSheet,
            WorksheetIgnoredErrorsMetadataModel? metadata,
            List<(GridRange Range, Dictionary<string, string> Attributes)> ranges)
        {
            _lookupSheet = lookupSheet;
            _metadata = metadata;
            _ranges = ranges;
        }

        public static IgnoredErrorNativeAttributeLookup Create(WorksheetIgnoredErrorsMetadataModel? metadata)
        {
            if (metadata is null || metadata.ErrorNativeAttributes.Count == 0)
                return Empty;

            var lookupSheet = SheetId.New();
            var ranges = new List<(GridRange Range, Dictionary<string, string> Attributes)>();
            foreach (var pair in metadata.ErrorNativeAttributes)
            {
                foreach (var token in SplitSqrefTokens(pair.Key))
                {
                    if (TryParseSqrefToken(token, lookupSheet, out var range))
                        ranges.Add((range, pair.Value));
                }
            }

            return new IgnoredErrorNativeAttributeLookup(lookupSheet, metadata, ranges);
        }

        public IReadOnlyDictionary<string, string>? GetNativeAttributes(SheetId sheetId, uint row, uint col)
        {
            if (_metadata is null)
                return null;

            var address = new CellAddress(sheetId, row, col);
            var reference = address.ToA1();
            if (_metadata.ErrorNativeAttributes.TryGetValue(reference, out var attributes))
                return attributes;

            if (_ranges.Count == 0)
                return null;

            var lookupAddress = new CellAddress(_lookupSheet, row, col);
            foreach (var range in _ranges)
            {
                if (range.Range.Contains(lookupAddress))
                    return range.Attributes;
            }

            return null;
        }
    }
}
