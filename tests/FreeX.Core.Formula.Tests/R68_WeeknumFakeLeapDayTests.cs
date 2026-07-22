using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression test for R68-formula-datetime-basic-6-1: WeeknumScalar resolved the input
/// serial through TrySerialToDateTime before measuring day-of-year, which collapses the
/// 1900 phantom leap day (serial 60, "1900-02-29") onto the same real DateTime as serial 59
/// ("1900-02-28"), so both landed in the same week. The fix computes day-of-year from raw
/// serial arithmetic instead, matching how WEEKDAY/DATEDIF-D/DAYS/YEARFRAC already
/// distinguish serial 60 in this file.
/// </summary>
public class R68_WeeknumFakeLeapDayTests
{
    private readonly FormulaEvaluator _eval = new();

    private ScalarValue Eval(string formula)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        return _eval.Evaluate("=" + formula, sheet, wb);
    }

    [Fact]
    public void Weeknum_Serial60_ReturnType13_Returns10()
    {
        // Serial 60 is Excel's phantom "1900-02-29". Pre-fix this collapsed onto the same
        // week as serial 59 because both resolve to the same real DateTime (1900-02-28).
        Eval("WEEKNUM(60,13)").Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Weeknum_Serial59_ReturnType13_Returns9()
    {
        // Serial 59 ("1900-02-28") is the day immediately before the phantom leap day and
        // must remain in the prior week.
        Eval("WEEKNUM(59,13)").Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Weeknum_Serial61_ReturnType13_Returns10()
    {
        // Serial 61 ("1900-03-01", the first real day after the phantom leap day) falls in
        // the same week as serial 60.
        Eval("WEEKNUM(61,13)").Should().Be(new NumberValue(10));
    }

    [Fact]
    public void Weeknum_ModernDate_IsUnaffectedByFakeLeapDayFix()
    {
        // No-regression: a normal modern date (well outside the 1900 boundary) must compute
        // the same week number as before -- day-of-year via raw-serial arithmetic must agree
        // with the previous DateTime-based calculation for genuine (non-collapsed) dates.
        double jan8 = new DateTime(2024, 1, 8).ToOADate();
        var sheet2 = new Workbook().AddSheet("S");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(jan8));
        _eval.Evaluate("=WEEKNUM(A1)", sheet2).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Isoweeknum_Serial60_IsUnaffectedByWeeknumFix()
    {
        // ISOWEEKNUM uses a wholly separate code path (ExcelIsoWeeknum operating on the
        // resolved DateTime) that this fix must not touch.
        Eval("ISOWEEKNUM(60)").Should().Be(Eval("ISOWEEKNUM(59)"));
    }
}
