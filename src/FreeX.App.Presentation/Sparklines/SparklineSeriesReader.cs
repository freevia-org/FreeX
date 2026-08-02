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
    /// Data ranges over the supported cell cap are reported as empty. <paramref name="workbook"/> is
    /// used to resolve each sparkline's <see cref="SparklineModel.DataRange"/> to its own source
    /// sheet (Excel allows a sparkline hosted on one sheet to read its data from another), so it
    /// must be the workbook that owns <paramref name="sheet"/>.
    /// </summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyList<double>> BuildValues(Workbook workbook, Sheet sheet)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);

        var values = new Dictionary<Guid, IReadOnlyList<double>>();
        foreach (var sparkline in sheet.Sparklines)
            values[sparkline.Id] = ReadSeries(workbook, sheet, sparkline);

        return values;
    }

    /// <summary>
    /// Reads a single sparkline's data range into its numeric series. Hidden rows and columns are
    /// skipped unless <see cref="SparklineModel.DisplayHidden"/> is set (Excel's "Show data in
    /// hidden rows and columns"), in which case hidden cells contribute to the series like any
    /// other; non-numeric (text) cells are always treated as blank. Blank cells are handled per
    /// <see cref="SparklineModel.DisplayEmptyCellsAs"/>, and every mode keeps the cell's slot/index
    /// in the returned series so downstream x-axis spacing (<see cref="SparklineLayoutEngine"/>'s
    /// <c>i / (values.Count - 1)</c> layout) lines up with Excel regardless of how many blanks
    /// appear: <c>Gap</c> stores <see cref="double.NaN"/> so the layout engine breaks the line at
    /// that slot, <c>Zero</c> stores <c>0</c>, and <c>Span</c> ("Connect data points with line")
    /// stores the value linearly interpolated between the nearest real values before and after the
    /// blank run, which keeps the point on the straight line the two real points would otherwise
    /// draw between them -- i.e. it renders as a direct connection, not a break -- while leaving the
    /// blank's own slot (and every later slot) at its original position.
    /// </summary>
    /// <remarks>
    /// Excel's "Edit Data" dialog lets a sparkline's Data Range live on a different sheet than the
    /// one it is anchored/displayed on (<see cref="SparklineModel.DataRange"/>'s
    /// <c>Start.Sheet</c> vs. <see cref="SparklineModel.Location"/>'s sheet), and
    /// <c>XlsxSparklineMapper</c> round-trips that cross-sheet reference. <paramref name="workbook"/>
    /// resolves the range's own <c>Start.Sheet</c> to the sheet actually holding the source values,
    /// falling back to the host <paramref name="sheet"/> only if that sheet id no longer resolves
    /// (e.g. the source sheet was deleted) -- hidden-row/col checks and cell reads both happen
    /// against that resolved sheet, never unconditionally against the host.
    /// </remarks>
    public static IReadOnlyList<double> ReadSeries(Workbook workbook, Sheet sheet, SparklineModel sparkline)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(sparkline);

        if (!SparklineRangeLimits.IsSupportedDataRange(sparkline.DataRange))
            return [];

        var range = sparkline.DataRange;
        var dataSheet = workbook.GetSheet(range.Start.Sheet) ?? sheet;

        var raw = new List<double?>();
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            if (!sparkline.DisplayHidden && dataSheet.IsRowEffectivelyHidden(row))
                continue;

            for (var col = range.Start.Col; col <= range.End.Col; col++)
            {
                if (!sparkline.DisplayHidden && dataSheet.IsColEffectivelyHidden(col))
                    continue;

                raw.Add(dataSheet.GetValue(row, col) switch
                {
                    NumberValue number => number.Value,
                    DateTimeValue date => date.Value,
                    BoolValue boolean => boolean.Value ? 1 : 0,
                    _ => null,
                });
            }
        }

        var series = new List<double>(raw.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            if (raw[i] is { } v)
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
                    series.Add(InterpolateSpanValue(raw, i));
                    break;
                case SparklineEmptyCellDisplay.Gap:
                default:
                    series.Add(double.NaN);
                    break;
            }
        }

        return series;
    }

    /// <summary>
    /// Computes the value a blank cell should contribute under "Connect data points with line": the
    /// linear interpolation, weighted by slot distance, between the nearest real value before
    /// <paramref name="index"/> and the nearest real value after it. When one side has no real value
    /// (a leading or trailing run of blanks) there is nothing to connect to, so this falls back to
    /// <see cref="double.NaN"/> just like <c>Gap</c> -- the layout engine already skips leading/
    /// trailing non-finite slots without disturbing the spacing of the real points.
    /// </summary>
    private static double InterpolateSpanValue(IReadOnlyList<double?> raw, int index)
    {
        var before = -1;
        for (var i = index - 1; i >= 0; i--)
        {
            if (raw[i] is not null)
            {
                before = i;
                break;
            }
        }

        var after = -1;
        for (var i = index + 1; i < raw.Count; i++)
        {
            if (raw[i] is not null)
            {
                after = i;
                break;
            }
        }

        if (before < 0 || after < 0)
            return double.NaN;

        var t = (double)(index - before) / (after - before);
        return raw[before]!.Value + ((raw[after]!.Value - raw[before]!.Value) * t);
    }
}
