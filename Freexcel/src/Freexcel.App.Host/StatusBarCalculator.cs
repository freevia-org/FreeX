using System.Linq;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

/// <summary>Calculates aggregate statistics for a selection, for the status bar.</summary>
public static class StatusBarCalculator
{
    public record Stats(double Sum, int Count, double? Average, double? Min, double? Max);

    public static Stats Calculate(Sheet sheet, GridRange range)
    {
        double sum = 0;
        int count = 0;
        double? min = null, max = null;

        // For large ranges (full rows/cols/sheet) iterate only used cells to avoid
        // enumerating billions of empty addresses.
        long totalCells = (long)(range.End.Row - range.Start.Row + 1)
                        * (long)(range.End.Col - range.Start.Col + 1);

        IEnumerable<ScalarValue?> source = totalCells > 10_000
            ? sheet.GetUsedCells()
                   .Where(kvp => kvp.Key.Row >= range.Start.Row && kvp.Key.Row <= range.End.Row
                              && kvp.Key.Col >= range.Start.Col && kvp.Key.Col <= range.End.Col)
                   .Select(kvp => kvp.Value.Value)
            : range.AllCells().Select(a => sheet.GetValue(a));

        foreach (var value in source)
        {
            if (value is NumberValue nv)
            {
                sum += nv.Value;
                count++;
                min = min is null ? nv.Value : Math.Min(min.Value, nv.Value);
                max = max is null ? nv.Value : Math.Max(max.Value, nv.Value);
            }
        }

        double? average = count > 0 ? sum / count : null;
        return new Stats(sum, count, average, min, max);
    }
}
