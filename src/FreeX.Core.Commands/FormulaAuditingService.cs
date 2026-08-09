using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public static partial class FormulaAuditingService
{
    public const string FormulaRefersToBlankCellsErrorCode = "FormulaRefersToBlankCells";
    public const string NumberStoredAsTextErrorCode = "NumberStoredAsText";
    public const string TwoDigitYearTextDateErrorCode = "TwoDigitYearTextDate";
    public const string FormulaStoredAsTextErrorCode = "FormulaStoredAsText";
    public const string InconsistentCalculatedColumnFormulaErrorCode = "InconsistentCalculatedColumnFormula";
    public const string InconsistentFormulaErrorCode = "InconsistentFormula";
    public const string FormulaOmitsAdjacentCellsErrorCode = "FormulaOmitsAdjacentCells";
    public const string UnlockedFormulaCellsErrorCode = "UnlockedFormulaCells";
    public const string DataValidationErrorCode = "DataValidation";

    public static IReadOnlyList<CellAddress> GetDirectPrecedents(Workbook workbook, CellAddress formulaAddress)
    {
        var sheet = workbook.GetSheet(formulaAddress.Sheet);
        var cell = sheet?.GetCell(formulaAddress);
        if (cell?.HasFormula != true || string.IsNullOrWhiteSpace(cell.FormulaText))
            return [];

        return ExtractPrecedents(workbook, formulaAddress.Sheet, cell.FormulaText);
    }

    /// <summary>
    /// Like <see cref="GetDirectPrecedents"/>, but a multi-cell range/named-range/structured
    /// reference is returned as ONE contiguous <see cref="GridRange"/> region instead of being
    /// flattened into individual cells. Used only for building trace arrows (see
    /// <see cref="CollectPrecedentTraceArrows"/> and <c>FormulaTraceArrowPlanner</c>) so a range
    /// precedent draws as a single arrow rather than one arrow per cell in the range
    /// (R88-app-formula-auditing-5-3).
    /// </summary>
    public static IReadOnlyList<GridRange> GetDirectPrecedentRegions(Workbook workbook, CellAddress formulaAddress)
    {
        var sheet = workbook.GetSheet(formulaAddress.Sheet);
        var cell = sheet?.GetCell(formulaAddress);
        if (cell?.HasFormula != true || string.IsNullOrWhiteSpace(cell.FormulaText))
            return [];

        return ExtractPrecedentRegions(workbook, formulaAddress.Sheet, cell.FormulaText);
    }

    public static IReadOnlyList<FormulaTraceArrow> GetPrecedentTraceArrows(Workbook workbook, CellAddress formulaAddress)
    {
        var result = new List<FormulaTraceArrow>();
        var visited = new HashSet<CellAddress>();
        CollectPrecedentTraceArrows(workbook, formulaAddress, result, visited);
        return result;
    }

    public static IReadOnlyList<CellAddress> GetDirectDependents(Workbook workbook, CellAddress address)
        => GetDirectDependents(workbook, new GridRange(address, address));

    public static IReadOnlyList<CellAddress> GetDirectDependents(Workbook workbook, GridRange precedentRange)
    {
        var result = new HashSet<CellAddress>();

        foreach (var sheet in workbook.Sheets)
        {
            foreach (var formulaAddress in sheet.EnumerateFormulaCells())
            {
                var cell = sheet.GetCell(formulaAddress);
                if (cell?.HasFormula != true || string.IsNullOrWhiteSpace(cell.FormulaText))
                    continue;

                if (TryFormulaContainsLocalReferenceInRange(
                        cell.FormulaText,
                        sheet.Id,
                        precedentRange,
                        out var containsLocalReference))
                {
                    if (containsLocalReference)
                        result.Add(formulaAddress);
                    continue;
                }

                var precedents = ExtractPrecedents(workbook, sheet.Id, cell.FormulaText);
                if (ContainsAny(precedents, precedentRange))
                    result.Add(formulaAddress);
            }
        }

        return SortByWorkbookOrder(workbook, result).ToList();
    }

    private static bool ContainsAny(IReadOnlyList<CellAddress> addresses, GridRange range)
    {
        foreach (var address in addresses)
            if (range.Contains(address))
                return true;

        return false;
    }

    private static bool TryFormulaContainsLocalReferenceInRange(
        string formulaText,
        SheetId hostSheetId,
        GridRange range,
        out bool containsReference)
    {
        containsReference = false;

        for (var index = 0; index < formulaText.Length; index++)
        {
            if (formulaText[index] is '"' or '\'' or '!' or ':' or '[' or ']')
                return false;
        }

        for (var index = 0; index < formulaText.Length; index++)
        {
            var ch = formulaText[index];
            if (ch != '$' && !IsAsciiLetter(ch) && ch != '_')
                continue;

            if (IsFormulaReferenceBoundaryBefore(formulaText, index) &&
                TryReadFormulaReference(formulaText, index, out var end, out var row, out var col))
            {
                if (hostSheetId == range.Start.Sheet &&
                    range.Contains(new CellAddress(hostSheetId, row, col)))
                {
                    containsReference = true;
                }

                index = end - 1;
                continue;
            }

            if (ch == '$')
                return false;

            var identifierEnd = index + 1;
            while (identifierEnd < formulaText.Length &&
                   (IsAsciiLetterDigitOrUnderscore(formulaText[identifierEnd]) || formulaText[identifierEnd] == '.'))
            {
                identifierEnd++;
            }

            var next = identifierEnd;
            while (next < formulaText.Length && char.IsWhiteSpace(formulaText[next]))
                next++;

            if (next < formulaText.Length && formulaText[next] == '(')
            {
                index = identifierEnd - 1;
                continue;
            }

            return false;
        }

        return true;
    }

    public static IReadOnlyList<FormulaTraceArrow> GetDependentTraceArrows(Workbook workbook, CellAddress address)
    {
        var result = new List<FormulaTraceArrow>();
        var visited = new HashSet<CellAddress>();
        // Build the reverse-dependency index ONCE for the whole (potentially multi-level) trace,
        // instead of every recursive step re-scanning/re-parsing the entire workbook via the
        // public GetDirectDependents(Workbook, GridRange) (R123-core-commands-formula-auditing-
        // all-levels-perf). See FormulaDependentsIndex for why this is safe: region-overlap on the
        // precomputed precedent regions is exactly equivalent to the old flattened-cell containment
        // check.
        var index = BuildDependentsIndex(workbook);
        CollectDependentTraceArrows(workbook, address, result, visited, index);
        return result;
    }

    private static void CollectPrecedentTraceArrows(
        Workbook workbook,
        CellAddress formulaAddress,
        List<FormulaTraceArrow> result,
        HashSet<CellAddress> visited)
    {
        if (!visited.Add(formulaAddress))
            return;

        // Use the region form (GetDirectPrecedentRegions), not the flattened per-cell
        // GetDirectPrecedents, so a multi-cell range precedent produces ONE arrow anchored at the
        // range's top-left cell instead of one arrow per cell in the range
        // (R88-app-formula-auditing-5-3). Recursion still visits every individual cell in the
        // region so deeper precedent chains are unaffected.
        foreach (var region in GetDirectPrecedentRegions(workbook, formulaAddress))
        {
            result.Add(new FormulaTraceArrow(region.Start, formulaAddress, FormulaTraceArrowKind.Precedent));
            foreach (var precedentCell in region.AllCells())
                CollectPrecedentTraceArrows(workbook, precedentCell, result, visited);
        }
    }

    private static void CollectDependentTraceArrows(
        Workbook workbook,
        CellAddress address,
        List<FormulaTraceArrow> result,
        HashSet<CellAddress> visited,
        FormulaDependentsIndex index)
    {
        if (!visited.Add(address))
            return;

        foreach (var dependent in GetDirectDependents(workbook, index, new GridRange(address, address)))
        {
            result.Add(new FormulaTraceArrow(address, dependent, FormulaTraceArrowKind.Dependent));
            CollectDependentTraceArrows(workbook, dependent, result, visited, index);
        }
    }
}
