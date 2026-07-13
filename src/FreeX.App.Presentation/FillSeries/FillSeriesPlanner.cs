using System.Globalization;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.FillSeries;

/// <summary>Whether a fill series runs across rows (down a column) or across columns (along a row).</summary>
public enum FillSeriesDirection
{
    Rows,
    Columns,
}

/// <summary>The kind of fill series: arithmetic, geometric, date increment, or copy/autofill.</summary>
public enum FillSeriesType
{
    Linear,
    Growth,
    Date,
    AutoFill,
}

/// <summary>The calendar unit a date series steps by.</summary>
public enum FillSeriesDateUnit
{
    Day,
    Weekday,
    Month,
    Year,
}

/// <summary>The validated options the Fill ▸ Series dialog produces.</summary>
public sealed record FillSeriesOptions(
    double Step,
    FillSeriesDirection SeriesIn = FillSeriesDirection.Columns,
    FillSeriesType Type = FillSeriesType.Linear,
    FillSeriesDateUnit DateUnit = FillSeriesDateUnit.Day,
    double? StopValue = null);

/// <summary>Why the Fill ▸ Series inputs could not be turned into options.</summary>
public enum FillSeriesInputError
{
    None,
    InvalidStep,
    InvalidStop,
}

/// <summary>The Fill Series input that should receive focus after a validation error.</summary>
public enum FillSeriesInputFocusTarget
{
    StepValue,
    StopValue,
}

/// <summary>
/// Portable (no UI) backing logic for the Fill ▸ Series dialog (Home ▸ Fill ▸ Series). It parses and
/// validates the step/stop inputs and builds the linear / growth / date cell edits over a range, reading the
/// seed value from the active sheet. Kept UI-free so any desktop or cross-platform shell can reuse it and so it is
/// unit-testable without a window.
/// </summary>
public static class FillSeriesPlanner
{
    public static FillSeriesOptions DefaultOptions { get; } = new(
        Step: 1,
        SeriesIn: FillSeriesDirection.Columns,
        Type: FillSeriesType.Linear,
        DateUnit: FillSeriesDateUnit.Day);

    public static FillSeriesOptions CreateDefaultOptions(double step) =>
        DefaultOptions with { Step = step };

    public static bool IsDateUnitEnabled(FillSeriesType type) =>
        type == FillSeriesType.Date;

    public static FillSeriesInputFocusTarget FocusTargetFor(FillSeriesInputError error) =>
        error == FillSeriesInputError.InvalidStop
            ? FillSeriesInputFocusTarget.StopValue
            : FillSeriesInputFocusTarget.StepValue;

    /// <summary>
    /// Parses a step value, accepting the invariant decimal form and the current UI culture (so a typed
    /// <c>1.5</c> or a locale's <c>1,5</c> both work). Rejects non-finite values.
    /// </summary>
    public static bool TryParseStep(string? input, out double step)
    {
        if (TryParseFiniteDouble(input, CultureInfo.InvariantCulture, out step))
            return true;
        if (TryParseFiniteDouble(input, CultureInfo.CurrentCulture, out step))
            return true;

        step = 0;
        return false;
    }

    /// <summary>Parses a finite step value using only the supplied culture.</summary>
    public static bool TryParseStep(string? input, CultureInfo culture, out double step)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return TryParseFiniteDouble(input, culture, out step);
    }

    /// <summary>
    /// Parses and validates the dialog inputs into <see cref="FillSeriesOptions"/>. The stop value is
    /// optional (blank leaves it open); a present-but-unparseable stop or an unparseable step is rejected.
    /// </summary>
    public static bool TryCreateOptions(
        FillSeriesDirection seriesIn,
        FillSeriesType type,
        FillSeriesDateUnit dateUnit,
        string? stepText,
        string? stopText,
        out FillSeriesOptions options,
        out FillSeriesInputError error)
    {
        options = new FillSeriesOptions(1, seriesIn, type, dateUnit);
        error = FillSeriesInputError.None;

        if (!TryParseStep(stepText, out var step))
        {
            error = FillSeriesInputError.InvalidStep;
            return false;
        }

        double? stopValue = null;
        if (!string.IsNullOrWhiteSpace(stopText))
        {
            if (!TryParseStep(stopText, out var parsedStop))
            {
                error = FillSeriesInputError.InvalidStop;
                return false;
            }

            stopValue = parsedStop;
        }

        options = new FillSeriesOptions(step, seriesIn, type, dateUnit, stopValue);
        return true;
    }

    /// <summary>
    /// Parses and validates dialog inputs with one explicit culture. Use this when a shell must preserve
    /// current-culture-only decimal handling instead of the invariant-first portable default.
    /// </summary>
    public static bool TryCreateOptions(
        FillSeriesDirection seriesIn,
        FillSeriesType type,
        FillSeriesDateUnit dateUnit,
        string? stepText,
        string? stopText,
        CultureInfo culture,
        out FillSeriesOptions options,
        out FillSeriesInputError error)
    {
        ArgumentNullException.ThrowIfNull(culture);

        options = new FillSeriesOptions(1, seriesIn, type, dateUnit);
        error = FillSeriesInputError.None;

        if (!TryParseStep(stepText, culture, out var step))
        {
            error = FillSeriesInputError.InvalidStep;
            return false;
        }

        double? stopValue = null;
        if (!string.IsNullOrWhiteSpace(stopText))
        {
            if (!TryParseStep(stopText, culture, out var parsedStop))
            {
                error = FillSeriesInputError.InvalidStop;
                return false;
            }

            stopValue = parsedStop;
        }

        options = new FillSeriesOptions(step, seriesIn, type, dateUnit, stopValue);
        return true;
    }

    /// <summary>True when the selection is big enough to fill in the requested direction.</summary>
    public static bool CanFill(GridRange range, FillCellsDirection direction) =>
        direction is FillCellsDirection.Down or FillCellsDirection.Up
            ? range.RowCount >= 2
            : range.ColCount >= 2;

    /// <summary>Builds the series cell edits for the given options, dispatching by series type.</summary>
    public static List<(CellAddress Address, Cell NewCell)> BuildSeriesEdits(
        Sheet sheet,
        GridRange range,
        FillSeriesOptions options)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(options);

        return options.Type switch
        {
            FillSeriesType.Growth => BuildGrowthSeriesEdits(sheet, range, options.Step, options.SeriesIn, options.StopValue),
            FillSeriesType.Date => BuildDateSeriesEdits(sheet, range, options.Step, options.SeriesIn, options.DateUnit, options.StopValue),
            FillSeriesType.AutoFill => BuildAutoFillSeriesEdits(sheet, range, options.SeriesIn),
            _ => BuildLinearSeriesEdits(sheet, range, options.Step, options.SeriesIn, options.StopValue),
        };
    }

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

            // Excel treats every row/column in the selection as its own series line: if that
            // line's own leading cell (the top cell of its column for "Series in Columns", or the
            // leading cell of its row for "Series in Rows") already holds a number, that value is
            // the seed for this line and is preserved as-is rather than being overwritten and
            // chained into from the previous line's running value. Just like the range's own start
            // cell (handled above), the running value for the REST of that line must advance one
            // step past this seed, not sit on the seed itself -- otherwise the next cell in the line
            // would be written with the seed's own value instead of seed + step.
            if (IsSeriesLineStart(address, range, seriesIn) &&
                sheet.GetValue(address.Row, address.Col) is NumberValue lineSeed)
            {
                value = lineSeed.Value + step;
                continue;
            }

            if (IsPastStopValue(value, step, stopValue))
                break;

            edits.Add((address, Cell.FromValue(new NumberValue(value))));
            value += step;
        }

        return edits;
    }

    public static List<(CellAddress Address, Cell NewCell)> BuildGrowthSeriesEdits(
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
        var ascending = IsGrowthAscending(value, step);
        foreach (var address in EnumerateSeriesAddresses(sheet.Id, range, seriesIn))
        {
            if (address.Row == range.Start.Row && address.Col == range.Start.Col)
            {
                value *= step;
                continue;
            }

            // Same per-line-seed handling as BuildLinearSeriesEdits: every row/column in the
            // selection is its own independent series line, so a line whose own leading cell
            // already holds a number reseeds THAT line -- both the running value and the
            // ascending/descending direction used for the Stop Value clamp below, since two
            // different lines' own seeds can trend in opposite directions for the same step.
            if (IsSeriesLineStart(address, range, seriesIn) &&
                sheet.GetValue(address.Row, address.Col) is NumberValue lineSeed)
            {
                ascending = IsGrowthAscending(lineSeed.Value, step);
                value = lineSeed.Value * step;
                continue;
            }

            if (IsPastStopValue(value, ascending, stopValue))
                break;

            edits.Add((address, Cell.FromValue(new NumberValue(value))));
            value *= step;
        }

        return edits;
    }

    public static List<(CellAddress Address, Cell NewCell)> BuildDateSeriesEdits(
        Sheet sheet,
        GridRange range,
        double step,
        FillSeriesDirection seriesIn,
        FillSeriesDateUnit dateUnit,
        double? stopValue = null)
    {
        if (sheet.GetValue(range.Start.Row, range.Start.Col) is not DateTimeValue startValue)
            return [];

        var edits = new List<(CellAddress, Cell)>();
        var seed = startValue.Value;
        var value = seed;
        var preserveEndOfMonth = IsLastDayOfMonth(startValue.ToDateTime());
        var stepIndex = 0;
        foreach (var address in EnumerateSeriesAddresses(sheet.Id, range, seriesIn))
        {
            if (address.Row == range.Start.Row && address.Col == range.Start.Col)
            {
                stepIndex++;
                value = NextDateSerial(seed, value, step, dateUnit, preserveEndOfMonth, stepIndex);
                continue;
            }

            // Same per-line-seed handling as BuildLinearSeriesEdits/BuildGrowthSeriesEdits: a
            // line (column, for "Series in Columns"; row, for "Series in Rows") whose own
            // leading cell already holds a date restarts the Month/Year end-of-month clamp and
            // step count from THAT date, instead of continuing the running value chained from a
            // previous, unrelated line.
            if (IsSeriesLineStart(address, range, seriesIn) &&
                sheet.GetValue(address.Row, address.Col) is DateTimeValue lineSeed)
            {
                seed = lineSeed.Value;
                preserveEndOfMonth = IsLastDayOfMonth(lineSeed.ToDateTime());
                stepIndex = 1;
                value = NextDateSerial(seed, seed, step, dateUnit, preserveEndOfMonth, stepIndex);
                continue;
            }

            if (IsPastStopValue(value, step, stopValue))
                break;

            edits.Add((address, Cell.FromValue(new DateTimeValue(value))));
            stepIndex++;
            value = NextDateSerial(seed, value, step, dateUnit, preserveEndOfMonth, stepIndex);
        }

        return edits;
    }

    /// <summary>
    /// Builds the AutoFill series-type edits for Fill ▸ Series: replays the exact same
    /// fill-handle text-list detection <see cref="AutofillCommand"/> uses for a fill-handle drag
    /// (a trailing number, e.g. "Item 1" -&gt; "Item 2", or membership in one of Excel's
    /// built-in weekday/month lists) instead of routing through the numeric-only Linear builder,
    /// which silently no-ops on any non-numeric seed. Each line (column, for "Series in
    /// Columns"; row, for "Series in Rows") in the selection is its own independent series,
    /// seeded from that line's own leading cell -- matching how Linear, Growth, and Date all
    /// treat lines.
    /// </summary>
    public static List<(CellAddress Address, Cell NewCell)> BuildAutoFillSeriesEdits(
        Sheet sheet,
        GridRange range,
        FillSeriesDirection seriesIn)
    {
        var edits = new List<(CellAddress, Cell)>();
        Func<int, ScalarValue>? lineSeries = null;
        var offset = 0;
        foreach (var address in EnumerateSeriesAddresses(sheet.Id, range, seriesIn))
        {
            if (IsSeriesLineStart(address, range, seriesIn))
            {
                lineSeries = sheet.GetValue(address.Row, address.Col) is TextValue seed
                    ? AutofillCommand.TryCreateAutoFillTextSeries([seed.Value])
                    : null;
                offset = 1;
                continue;
            }

            if (lineSeries is null)
                continue;

            edits.Add((address, Cell.FromValue(lineSeries(offset))));
            offset++;
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

    /// <summary>
    /// True when <paramref name="address"/> is the leading cell of its own series line: the top cell of a
    /// column when filling "Series in Columns", or the leading cell of a row when filling "Series in Rows".
    /// </summary>
    private static bool IsSeriesLineStart(CellAddress address, GridRange range, FillSeriesDirection seriesIn) =>
        seriesIn == FillSeriesDirection.Columns
            ? address.Row == range.Start.Row
            : address.Col == range.Start.Col;

    private static bool IsPastStopValue(double value, double step, double? stopValue)
    {
        if (stopValue is not { } stop)
            return false;

        return step < 0 ? value < stop : value > stop;
    }

    private static bool IsPastStopValue(double value, bool ascending, double? stopValue)
    {
        if (stopValue is not { } stop)
            return false;

        return ascending ? value > stop : value < stop;
    }

    /// <summary>
    /// Whether a growth series' terms trend upward, derived from the STEP's effect on the seed
    /// (comparing the first computed term against the seed) rather than from comparing the seed
    /// to the user's Stop Value -- e.g. seed=10/step=3 (10 -&gt; 30) or seed=-10/step=0.5
    /// (-10 -&gt; -5) are ascending; seed=10/step=0.5 (10 -&gt; 5) or seed=-10/step=3
    /// (-10 -&gt; -30) are descending. Mirrors BuildLinearSeriesEdits' step-sign-derived
    /// direction: a mismatched Stop Value must clamp immediately rather than run away because
    /// the direction was inferred from start-vs-stop instead of from the step itself.
    /// </summary>
    private static bool IsGrowthAscending(double seed, double step) => seed * step >= seed;

    /// <summary>
    /// Computes the next date serial in a Fill ▸ Series ▸ Date sequence. Day and Weekday units have no
    /// per-target clamping, so they safely chain off <paramref name="previousValue"/>. Month and Year units
    /// clamp the day-of-month when the target month is shorter than the seed's day (e.g. 30-Jan + 1 month =
    /// 28-Feb), and Excel always measures that clamp against the ORIGINAL seed date, not the previous
    /// (already-clamped) result — otherwise a short month like February permanently truncates every later
    /// date in the series. <paramref name="stepIndex"/> is the 1-based count of calendar-unit steps from the
    /// seed to this target, so Month/Year compute <c>seed.AddMonths(step * stepIndex)</c> directly.
    /// </summary>
    private static double NextDateSerial(double seedValue, double previousValue, double step, FillSeriesDateUnit dateUnit, bool preserveEndOfMonth, int stepIndex)
    {
        if (dateUnit == FillSeriesDateUnit.Day)
            return previousValue + step;

        var wholeStep = (int)Math.Truncate(step);
        if (wholeStep == 0)
            return previousValue;

        return dateUnit switch
        {
            FillSeriesDateUnit.Weekday => AddWeekdays(previousValue, wholeStep),
            FillSeriesDateUnit.Month => AddMonths(seedValue, wholeStep * stepIndex, preserveEndOfMonth),
            FillSeriesDateUnit.Year => AddYears(seedValue, wholeStep * stepIndex, preserveEndOfMonth),
            _ => previousValue + step,
        };
    }

    private static double AddMonths(double value, int months, bool preserveEndOfMonth)
    {
        var date = DateTime.FromOADate(value).AddMonths(months);
        return PreserveEndOfMonth(date, preserveEndOfMonth).ToOADate();
    }

    private static double AddYears(double value, int years, bool preserveEndOfMonth)
    {
        var date = DateTime.FromOADate(value).AddYears(years);
        return PreserveEndOfMonth(date, preserveEndOfMonth).ToOADate();
    }

    private static DateTime PreserveEndOfMonth(DateTime date, bool preserveEndOfMonth) =>
        preserveEndOfMonth
            ? new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month), date.Hour, date.Minute, date.Second, date.Millisecond, date.Kind)
            : date;

    private static double AddWeekdays(double value, int weekdays)
    {
        var date = DateTime.FromOADate(value);
        var direction = Math.Sign(weekdays);
        for (var remaining = Math.Abs(weekdays); remaining > 0;)
        {
            date = date.AddDays(direction);
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            remaining--;
        }

        return date.ToOADate();
    }

    private static bool IsLastDayOfMonth(DateTime date) =>
        date.Day == DateTime.DaysInMonth(date.Year, date.Month);

    private static bool TryParseFiniteDouble(string? input, CultureInfo culture, out double value)
    {
        const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands;
        return double.TryParse((input ?? string.Empty).Trim(), styles, culture, out value) &&
               double.IsFinite(value);
    }
}
