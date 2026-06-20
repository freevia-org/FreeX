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

/// <summary>
/// Portable (no UI) backing logic for the Fill ▸ Series dialog (Home ▸ Fill ▸ Series). It parses and
/// validates the step/stop inputs and builds the linear / growth / date cell edits over a range, reading the
/// seed value from the active sheet. Kept UI-free so any desktop or cross-platform shell can reuse it and so it is
/// unit-testable without a window.
/// </summary>
public static class FillSeriesPlanner
{
    /// <summary>
    /// Parses a step value, accepting the invariant decimal form and the current UI culture (so a typed
    /// <c>1.5</c> or a locale's <c>1,5</c> both work). Rejects non-finite values.
    /// </summary>
    public static bool TryParseStep(string? input, out double step)
    {
        const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands;
        var text = (input ?? string.Empty).Trim();
        if (double.TryParse(text, styles, CultureInfo.InvariantCulture, out step) && double.IsFinite(step))
            return true;
        if (double.TryParse(text, styles, CultureInfo.CurrentCulture, out step) && double.IsFinite(step))
            return true;

        step = 0;
        return false;
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
        var ascending = stopValue is not { } stop || startValue.Value <= stop;
        foreach (var address in EnumerateSeriesAddresses(sheet.Id, range, seriesIn))
        {
            if (address.Row == range.Start.Row && address.Col == range.Start.Col)
            {
                value *= step;
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
        var value = startValue.Value;
        var preserveEndOfMonth = IsLastDayOfMonth(startValue.ToDateTime());
        foreach (var address in EnumerateSeriesAddresses(sheet.Id, range, seriesIn))
        {
            if (address.Row == range.Start.Row && address.Col == range.Start.Col)
            {
                value = NextDateSerial(value, step, dateUnit, preserveEndOfMonth);
                continue;
            }

            if (IsPastStopValue(value, step, stopValue))
                break;

            edits.Add((address, Cell.FromValue(new DateTimeValue(value))));
            value = NextDateSerial(value, step, dateUnit, preserveEndOfMonth);
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

    private static bool IsPastStopValue(double value, bool ascending, double? stopValue)
    {
        if (stopValue is not { } stop)
            return false;

        return ascending ? value > stop : value < stop;
    }

    private static double NextDateSerial(double value, double step, FillSeriesDateUnit dateUnit, bool preserveEndOfMonth)
    {
        if (dateUnit == FillSeriesDateUnit.Day)
            return value + step;

        var wholeStep = (int)Math.Truncate(step);
        if (wholeStep == 0)
            return value;

        return dateUnit switch
        {
            FillSeriesDateUnit.Weekday => AddWeekdays(value, wholeStep),
            FillSeriesDateUnit.Month => AddMonths(value, wholeStep, preserveEndOfMonth),
            FillSeriesDateUnit.Year => AddYears(value, wholeStep, preserveEndOfMonth),
            _ => value + step,
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
}
