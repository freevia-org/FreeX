using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>Calculates aggregate statistics for a selection, for the status bar.</summary>
public static class StatusBarCalculator
{
    public readonly record struct Stats(double Sum, int Count, int NumericalCount, double? Average, double? Min, double? Max);

    private static readonly Stats EmptyStats = new(0, 0, 0, null, null, null);

    public static Stats Calculate(Sheet sheet, GridRange range)
    {
        if (sheet.GetUsedRange() is not { } usedRange || !usedRange.Overlaps(range))
            return EmptyStats;

        double sum = 0;
        int count = 0;
        int numericalCount = 0;
        double? min = null, max = null;

        var scanRange = Intersect(range, usedRange);

        long totalCells = scanRange.CellCount;

        if (sheet.CellCount < totalCells)
        {
            foreach (var (row, col) in sheet.GetOccupiedCells())
            {
                if (Contains(scanRange, row, col) && sheet.GetCell(row, col) is { } cell)
                    Accumulate(cell.Value, ref sum, ref count, ref numericalCount, ref min, ref max);
            }
        }
        else
        {
            for (var row = scanRange.Start.Row; row <= scanRange.End.Row; row++)
            {
                for (var col = scanRange.Start.Col; col <= scanRange.End.Col; col++)
                    Accumulate(sheet.GetValue(row, col), ref sum, ref count, ref numericalCount, ref min, ref max);
            }
        }

        double? average = numericalCount > 0 ? sum / numericalCount : null;
        return new Stats(sum, count, numericalCount, average, min, max);
    }

    private static GridRange Intersect(GridRange range, GridRange usedRange)
    {
        return new GridRange(
            new CellAddress(
                range.Start.Sheet,
                Math.Max(range.Start.Row, usedRange.Start.Row),
                Math.Max(range.Start.Col, usedRange.Start.Col)),
            new CellAddress(
                range.Start.Sheet,
                Math.Min(range.End.Row, usedRange.End.Row),
                Math.Min(range.End.Col, usedRange.End.Col)));
    }

    private static bool Contains(GridRange range, uint row, uint col) =>
        row >= range.Start.Row && row <= range.End.Row &&
        col >= range.Start.Col && col <= range.End.Col;

    private static void Accumulate(
        ScalarValue value,
        ref double sum,
        ref int count,
        ref int numericalCount,
        ref double? min,
        ref double? max)
    {
        if (value is not BlankValue)
            count++;

        if (value is NumberValue nv)
        {
            sum += nv.Value;
            numericalCount++;
            min = min is null ? nv.Value : Math.Min(min.Value, nv.Value);
            max = max is null ? nv.Value : Math.Max(max.Value, nv.Value);
        }
    }

    public static string FormatNumber(double value)
    {
        if (value == Math.Floor(value) && Math.Abs(value) < 1e15)
            return value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

        return value.ToString("G10", System.Globalization.CultureInfo.CurrentCulture);
    }

    public static string GetReadyStatusText(Sheet sheet, CellAddress activeCell)
    {
        var prompt = DataValidationService.GetInputPrompt(sheet, activeCell);
        if (prompt is null)
            return "Ready";

        if (prompt.Title.Length == 0)
            return prompt.Message;

        if (prompt.Message.Length == 0)
            return prompt.Title;

        return $"{prompt.Title}: {prompt.Message}";
    }
}
