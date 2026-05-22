using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class QuickAnalysisTotalsPlanner
{
    public static IReadOnlyList<(CellAddress Address, Cell NewCell)> BuildPercentTotalEdits(GridRange range)
    {
        var denominator = AbsoluteRangeReference(range);
        return BuildAdjacentFormulaEdits(
            range,
            row =>
            {
                var rowRange = RowRangeReference(range, row);
                return $"SUM({rowRange})/SUM({denominator})";
            });
    }

    public static IReadOnlyList<(CellAddress Address, Cell NewCell)> BuildRunningTotalEdits(GridRange range) =>
        BuildAdjacentFormulaEdits(
            range,
            row => $"SUM({AbsoluteAddress(range.Start)}:{AbsoluteAddress(new CellAddress(range.Start.Sheet, row, range.End.Col))})");

    private static IReadOnlyList<(CellAddress Address, Cell NewCell)> BuildAdjacentFormulaEdits(
        GridRange range,
        Func<uint, string> formulaForRow)
    {
        var edits = new List<(CellAddress Address, Cell NewCell)>();
        var targetCol = range.End.Col + 1;
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            var address = new CellAddress(range.Start.Sheet, row, targetCol);
            edits.Add((address, Cell.FromFormula(formulaForRow(row))));
        }

        return edits;
    }

    private static string RowRangeReference(GridRange range, uint row)
    {
        var start = new CellAddress(range.Start.Sheet, row, range.Start.Col);
        var end = new CellAddress(range.Start.Sheet, row, range.End.Col);
        return $"{RelativeAddress(start)}:{RelativeAddress(end)}";
    }

    private static string AbsoluteRangeReference(GridRange range) =>
        $"{AbsoluteAddress(range.Start)}:{AbsoluteAddress(range.End)}";

    private static string RelativeAddress(CellAddress address) =>
        $"{CellAddress.NumberToColumnName(address.Col)}{address.Row}";

    private static string AbsoluteAddress(CellAddress address) =>
        $"${CellAddress.NumberToColumnName(address.Col)}${address.Row}";
}
