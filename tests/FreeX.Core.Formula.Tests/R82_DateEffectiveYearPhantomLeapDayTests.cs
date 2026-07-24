using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

// R82-formula-datetime-serial-5-1: DATE()'s 1900 phantom-leap-day correction previously keyed
// off the raw `year` argument, not the month-normalized *effective* year/month. When a negative
// (or otherwise out-of-range) month rolls the effective year/month back into Jan/Feb 1900 from a
// different literal year argument, the correction must still fire.
public sealed class R82_DateEffectiveYearPhantomLeapDayTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet Sheet() => new(SheetId.New(), "S");

    [Fact]
    public void Date_NegativeMonthRollsLiteralYearBackIntoPhantomLeapDay_ReturnsSerial60()
    {
        // DATE(1901,-10,29): month=-10 rolls 11 months back from Jan 1901, landing the
        // effective year/month on Feb 1900 (not the literal year 1901), so day 29 of that
        // effective month must resolve to the phantom leap day, serial 60 -- matching
        // DATE(1900,2,29)=60. Before the fix this returned 61 (one real day too high, because
        // .NET's real-calendar AddMonths/AddDays has no Feb 29, 1900 to land on).
        _eval.Evaluate("=DATE(1901,-10,29)", Sheet()).Should().Be(new NumberValue(60));
    }

    [Fact]
    public void Date_NegativeMonthRollsLiteralYearBackPastPhantomLeapDay_ReturnsSerial61()
    {
        // Sibling: DATE(1901,-10,30) is one day past the phantom leap day (effective Mar 1,
        // 1900), so it must return 61, not 62.
        _eval.Evaluate("=DATE(1901,-10,30)", Sheet()).Should().Be(new NumberValue(61));
    }

    [Fact]
    public void Date_NegativeMonthRollsLiteralYearBackToOrdinaryFeb1900Day_UnaffectedByFix()
    {
        // Sibling: DATE(1901,-10,15) rolls to effective Feb 15, 1900 -- an ordinary date, well
        // clear of the phantom-day boundary, so this must stay a plain, uncorrected serial (46).
        _eval.Evaluate("=DATE(1901,-10,15)", Sheet()).Should().Be(new NumberValue(46));
    }

    [Fact]
    public void Date_LiteralYear1900StillAppliesExistingPhantomLeapDayCorrection_UnaffectedByFix()
    {
        // Sibling: the original, already-covered literal-year-1900 case (no month rollover)
        // must remain correct after switching the guard from raw `year` to the effective year.
        _eval.Evaluate("=DATE(1900,2,29)", Sheet()).Should().Be(new NumberValue(60));
    }

    [Fact]
    public void Date_LiteralYear1900MonthRollsForwardOutOfPhantomRegion_UnaffectedByFix()
    {
        // Sibling: DATE(1900,14,5) rolls the *effective* year forward to 1901 (Feb 1901), well
        // past the 1900 phantom-day region, so no correction may fire even though the raw
        // literal year argument is 1900. Real Feb 1901 has 28 days, so Feb 5, 1901 is ordinary.
        _eval.Evaluate("=DATE(1900,14,5)", Sheet()).Should().Be(new NumberValue(402));
    }
}
