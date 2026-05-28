using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

/// <summary>Calculates aggregate statistics for a selection, for the status bar.</summary>
public static class StatusBarCalculator
{
    public record Stats(double Sum, int Count, int NumericalCount, double? Average, double? Min, double? Max);

    public static Stats Calculate(Sheet sheet, GridRange range)
    {
        if (sheet.GetUsedRange() is not { } usedRange || !usedRange.Overlaps(range))
            return new Stats(0, 0, 0, null, null, null);

        double sum = 0;
        int count = 0;
        int numericalCount = 0;
        double? min = null, max = null;

        // For large ranges (full rows/cols/sheet) iterate only used cells to avoid
        // enumerating billions of empty addresses.
        long totalCells = (long)(range.End.Row - range.Start.Row + 1)
                        * (long)(range.End.Col - range.Start.Col + 1);

        if (totalCells > 10_000)
        {
            foreach (var (address, cell) in sheet.EnumerateCells())
            {
                if (range.Contains(address))
                    Accumulate(cell.Value, ref sum, ref count, ref numericalCount, ref min, ref max);
            }
        }
        else
        {
            foreach (var address in range.AllCells())
                Accumulate(sheet.GetValue(address), ref sum, ref count, ref numericalCount, ref min, ref max);
        }

        double? average = numericalCount > 0 ? sum / numericalCount : null;
        return new Stats(sum, count, numericalCount, average, min, max);
    }

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
