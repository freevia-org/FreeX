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
        => BuildLinearSeriesEdits(sheet, range, step, FillSeriesDirection.Rows);

    public static List<(CellAddress Address, Cell NewCell)> BuildLinearSeriesEdits(
        Sheet sheet,
        GridRange range,
        double step,
        FillSeriesDirection seriesIn,
        double? stopValue = null)
    {
        if (sheet.GetValue(range.Start.Row, range.Start.Col) is not NumberValue startValue)
            return [];

        var edits = new List<(CellAddress, Cell)>();
        var value = startValue.Value;
        foreach (var address in EnumerateSeriesAddresses(sheet.Id, range, seriesIn))
        {
            if (address.Row == range.Start.Row && address.Col == range.Start.Col)
            {
                value += step;
                continue;
            }

            if (IsPastStopValue(value, step, stopValue))
                break;

            edits.Add((address, Cell.FromValue(new NumberValue(value))));
            value += step;
        }

        return edits;
    }

    private static IEnumerable<CellAddress> EnumerateSeriesAddresses(SheetId sheetId, GridRange range, FillSeriesDirection seriesIn)
    {
        if (seriesIn == FillSeriesDirection.Columns)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
            {
                for (var row = range.Start.Row; row <= range.End.Row; row++)
                    yield return new CellAddress(sheetId, row, col);
            }

            yield break;
        }

        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            for (var col = range.Start.Col; col <= range.End.Col; col++)
                yield return new CellAddress(sheetId, row, col);
        }
    }

    private static bool IsPastStopValue(double value, double step, double? stopValue)
    {
        if (stopValue is not { } stop)
            return false;

        return step < 0 ? value < stop : value > stop;
    }
}
