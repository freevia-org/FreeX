using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static class FillSeriesPlanner
{
    public static bool TryParseStep(string input, out double step)
    {
        if (double.TryParse(input.Trim(), out var parsed) &&
            double.IsFinite(parsed))
        {
            step = parsed;
            return true;
        }

        step = 0;
        return false;
    }

    public static bool CanFill(GridRange range, FillCellsDirection direction) =>
        direction is FillCellsDirection.Down or FillCellsDirection.Up
            ? range.RowCount >= 2
            : range.ColCount >= 2;

    public static List<(CellAddress Address, Cell NewCell)> BuildLinearSeriesEdits(Sheet sheet, GridRange range, double step)
    {
        if (sheet.GetValue(range.Start.Row, range.Start.Col) is not NumberValue startValue)
            return [];

        var edits = new List<(CellAddress, Cell)>();
        var value = startValue.Value;
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
            {
                if (row == range.Start.Row && col == range.Start.Col)
                {
                    value += step;
                    continue;
                }

                edits.Add((new CellAddress(sheet.Id, row, col), Cell.FromValue(new NumberValue(value))));
                value += step;
            }
        }

        return edits;
    }
}
