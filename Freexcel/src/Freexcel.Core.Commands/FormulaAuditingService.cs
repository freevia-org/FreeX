using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public static partial class FormulaAuditingService
{
    public const string FormulaRefersToBlankCellsErrorCode = "FormulaRefersToBlankCells";
    public const string NumberStoredAsTextErrorCode = "NumberStoredAsText";
    public const string TwoDigitYearTextDateErrorCode = "TwoDigitYearTextDate";
    public const string InconsistentFormulaErrorCode = "InconsistentFormula";
    public const string FormulaOmitsAdjacentCellsErrorCode = "FormulaOmitsAdjacentCells";
    public const string UnlockedFormulaCellsErrorCode = "UnlockedFormulaCells";

    public static IReadOnlyList<CellAddress> GetDirectPrecedents(Workbook workbook, CellAddress formulaAddress)
    {
        var sheet = workbook.GetSheet(formulaAddress.Sheet);
        var cell = sheet?.GetCell(formulaAddress);
        if (cell?.HasFormula != true || string.IsNullOrWhiteSpace(cell.FormulaText))
            return [];

        return ExtractPrecedents(workbook, formulaAddress.Sheet, cell.FormulaText);
    }

    public static IReadOnlyList<FormulaTraceArrow> GetPrecedentTraceArrows(Workbook workbook, CellAddress formulaAddress)
    {
        var result = new List<FormulaTraceArrow>();
        var visited = new HashSet<CellAddress>();
        CollectPrecedentTraceArrows(workbook, formulaAddress, result, visited);
        return result;
    }

    public static IReadOnlyList<CellAddress> GetDirectDependents(Workbook workbook, CellAddress address)
    {
        var result = new HashSet<CellAddress>();

        foreach (var sheet in workbook.Sheets)
        {
            foreach (var (formulaAddress, cell) in sheet.EnumerateCells())
            {
                if (cell.HasFormula != true || string.IsNullOrWhiteSpace(cell.FormulaText))
                    continue;

                var precedents = ExtractPrecedents(workbook, sheet.Id, cell.FormulaText);
                if (precedents.Contains(address))
                    result.Add(formulaAddress);
            }
        }

        return SortByWorkbookOrder(workbook, result).ToList();
    }

    public static IReadOnlyList<FormulaTraceArrow> GetDependentTraceArrows(Workbook workbook, CellAddress address)
    {
        var result = new List<FormulaTraceArrow>();
        var visited = new HashSet<CellAddress>();
        CollectDependentTraceArrows(workbook, address, result, visited);
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

        foreach (var precedent in GetDirectPrecedents(workbook, formulaAddress))
        {
            result.Add(new FormulaTraceArrow(precedent, formulaAddress));
            CollectPrecedentTraceArrows(workbook, precedent, result, visited);
        }
    }

    private static void CollectDependentTraceArrows(
        Workbook workbook,
        CellAddress address,
        List<FormulaTraceArrow> result,
        HashSet<CellAddress> visited)
    {
        if (!visited.Add(address))
            return;

        foreach (var dependent in GetDirectDependents(workbook, address))
        {
            result.Add(new FormulaTraceArrow(address, dependent));
            CollectDependentTraceArrows(workbook, dependent, result, visited);
        }
    }
}
