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

        foreach (var addr in range.AllCells())
        {
            if (sheet.GetValue(addr) is NumberValue nv)
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
