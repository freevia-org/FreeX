using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static class FillSeriesPlanner
{
    public static bool TryParseStep(string input, out double step)
    {
        if (double.TryParse(input.Trim(), out var parsed) &&
            double.IsFinite(parsed))
        {
            step = parsed;
            return true;
        }

        step = 0;
        return false;
    }

    public static bool CanFill(GridRange range, FillCellsDirection direction) =>
        direction is FillCellsDirection.Down or FillCellsDirection.Up
            ? range.RowCount >= 2
            : range.ColCount >= 2;

    public static List<(CellAddress Address, Cell NewCell)> BuildLinearSeriesEdits(Sheet sheet, GridRange range, double step)
        => BuildLinearSeriesEdits(sheet, range, step, FillSeriesDirection.Rows);

    public static List<(CellAddress Address, Cell NewCell)> BuildSeriesEdits(
        Sheet sheet,
        GridRange range,
        FillSeriesStepDialogResult result)
    {
        return result.Type switch
        {
            FillSeriesType.Growth => BuildGrowthSeriesEdits(sheet, range, result.Step, result.SeriesIn, result.StopValue),
            FillSeriesType.Date => BuildDateSeriesEdits(sheet, range, result.Step, result.SeriesIn, result.DateUnit, result.StopValue),
            _ => BuildLinearSeriesEdits(sheet, range, result.Step, result.SeriesIn, result.StopValue)
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
            _ => value + step
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
