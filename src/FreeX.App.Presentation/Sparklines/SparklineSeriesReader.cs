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
    /// other; non-numeric (text) cells are always treated as blank. Blank cells are handled per
    /// <see cref="SparklineModel.DisplayEmptyCellsAs"/>: <c>Gap</c> and <c>Zero</c> keep the cell's
    /// position in the series (as <see cref="double.NaN"/> so the layout engine breaks the line, or
    /// as <c>0</c>), while <c>Span</c> drops the position entirely so the surrounding points connect
    /// across it, matching Excel's "Connect data points with line" option.
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

                double? value = sheet.GetValue(row, col) switch
                {
                    NumberValue number => number.Value,
                    DateTimeValue date => date.Value,
                    BoolValue boolean => boolean.Value ? 1 : 0,
                    _ => null,
                };

                if (value is { } v)
                {
                    series.Add(v);
                    continue;
                }

                switch (sparkline.DisplayEmptyCellsAs)
                {
                    case SparklineEmptyCellDisplay.Zero:
                        series.Add(0);
                        break;
                    case SparklineEmptyCellDisplay.Span:
                        // Drop the position so the line connects across the blank cell.
                        break;
                    case SparklineEmptyCellDisplay.Gap:
                    default:
                        series.Add(double.NaN);
                        break;
                }
            }
        }

        return series;
    }
}
