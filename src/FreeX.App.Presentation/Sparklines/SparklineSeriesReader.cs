using FreeX.Core.Model;

namespace FreeX.App.Presentation.Sparklines;

/// <summary>
/// Pure, UI-free reader that turns each <see cref="SparklineModel"/>'s data range into the numeric
/// series the layout engine draws. Shared by every shell (Windows host and cross-platform port) so
/// the sheet -&gt; series step is single-sourced: data ranges over the supported cell cap are reported
/// as empty, hidden rows and columns are skipped, and only number / date / bool cells contribute.
/// </summary>
public static class SparklineSeriesReader
{
    /// <summary>
    /// Reads every sparkline on <paramref name="sheet"/> into its numeric series, keyed by id.
    /// Data ranges over the supported cell cap are reported as empty.
    /// </summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyList<double>> BuildValues(Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var values = new Dictionary<Guid, IReadOnlyList<double>>();
        foreach (var sparkline in sheet.Sparklines)
            values[sparkline.Id] = ReadSeries(sheet, sparkline);

        return values;
    }

    /// <summary>
    /// Reads a single sparkline's data range into its numeric series. Hidden rows and columns are
    /// skipped unless <see cref="SparklineModel.DisplayHidden"/> is set (Excel's "Show data in
    /// hidden rows and columns"), in which case hidden cells contribute to the series like any
    /// other; non-numeric cells are ignored.
    /// </summary>
    public static IReadOnlyList<double> ReadSeries(Sheet sheet, SparklineModel sparkline)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(sparkline);

        if (!SparklineRangeLimits.IsSupportedDataRange(sparkline.DataRange))
            return [];

        var series = new List<double>();
        var range = sparkline.DataRange;
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            if (!sparkline.DisplayHidden && sheet.IsRowEffectivelyHidden(row))
                continue;

            for (var col = range.Start.Col; col <= range.End.Col; col++)
            {
                if (!sparkline.DisplayHidden && sheet.IsColEffectivelyHidden(col))
                    continue;

                switch (sheet.GetValue(row, col))
                {
                    case NumberValue number:
                        series.Add(number.Value);
                        break;
                    case DateTimeValue date:
                        series.Add(date.Value);
                        break;
                    case BoolValue boolean:
                        series.Add(boolean.Value ? 1 : 0);
                        break;
                }
            }
        }

        return series;
    }
}
