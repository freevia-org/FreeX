using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-11 fix bucket R1: date/time functions returning pre-epoch serials
/// instead of #NUM!, and WORKDAY/WORKDAY.INTL leaking the start date's
/// time-of-day fraction into the returned serial.
/// </summary>
public sealed class FreeXR11B1Tests
{
    private readonly FormulaEvaluator _eval = new();

    // R11-formula-functions-2: EDATE/EOMONTH must return #NUM! when the
    // result predates the 1900 date system's epoch (serial < 1), matching
    // the guard already present on the fake-leap-day code path.
    [Fact]
    public void Edate_ReturnsNumWhenResultPredatesEpoch()
    {
        _eval.Evaluate("=EDATE(DATE(1900,1,15),-1)", Sheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Eomonth_ReturnsNumWhenResultPredatesEpoch()
    {
        _eval.Evaluate("=EOMONTH(DATE(1900,1,15),-1)", Sheet()).Should().Be(ErrorValue.Num);
    }

    // R11-formula-functions-3: WORKDAY / WORKDAY.INTL must always return a
    // whole-day serial, discarding any time-of-day fraction on the start date.
    [Fact]
    public void Workday_DropsStartDateTimeFraction()
    {
        _eval.Evaluate("=WORKDAY(43831.6,1)", Sheet()).Should().Be(new NumberValue(43832));
    }

    [Fact]
    public void WorkdayIntl_DropsStartDateTimeFraction()
    {
        _eval.Evaluate("=WORKDAY.INTL(43831.6,1)", Sheet()).Should().Be(new NumberValue(43832));
    }

    private static Sheet Sheet(params (int Row, int Column, double Value)[] values)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, column, value) in values)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)column), new NumberValue(value));
        return sheet;
    }
}
