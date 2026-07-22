using System.Globalization;

namespace Free.Shared.AppServices;

public static class WorkbookSelectionStatsFormatter
{
    public static string Format(WorkbookSelectionStats stats)
    {
        if (stats.IsEmpty)
            return "";

        // Real Excel propagates an error cell in the selection into the Sum/Average/Min/Max
        // status-bar readouts (matching SUM/AVERAGE/MIN/MAX's own error-propagation) instead of
        // silently excluding it from the computation. Count/Numerical Count keep their normal
        // counts either way.
        var errorCode = stats.AggregateErrorCode;

        var parts = new List<string>(6);
        if (errorCode is not null)
            parts.Add(FormatStatusText("Average", errorCode));
        else if (stats.Average.HasValue)
            parts.Add(FormatStatusText("Average", FormatNumber(stats.Average.Value)));

        parts.Add(FormatStatusText("Count", stats.Count));
        parts.Add(FormatStatusText("Numerical Count", stats.NumericalCount));

        if (errorCode is not null)
            parts.Add(FormatStatusText("Sum", errorCode));
        else if (stats.HasNumericalValues)
            parts.Add(FormatStatusText("Sum", FormatNumber(stats.Sum)));

        if (errorCode is not null)
            parts.Add(FormatStatusText("Min", errorCode));
        else if (stats.Min.HasValue)
            parts.Add(FormatStatusText("Min", FormatNumber(stats.Min.Value)));

        if (errorCode is not null)
            parts.Add(FormatStatusText("Max", errorCode));
        else if (stats.Max.HasValue)
            parts.Add(FormatStatusText("Max", FormatNumber(stats.Max.Value)));

        return string.Join("   ", parts);
    }

    public static string FormatNumber(double value)
    {
        if (value == Math.Floor(value) && Math.Abs(value) < 1e15)
            return value.ToString("N0", CultureInfo.CurrentCulture);

        return value.ToString("G10", CultureInfo.CurrentCulture);
    }

    private static string FormatStatusText(string label, object value) =>
        string.Format(CultureInfo.CurrentCulture, "{0}: {1}", label, value);
}
