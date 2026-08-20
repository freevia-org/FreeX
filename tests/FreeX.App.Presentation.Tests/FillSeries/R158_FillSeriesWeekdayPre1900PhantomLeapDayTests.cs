using FluentAssertions;
using FreeX.App.Presentation.FillSeries;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FillSeries;

/// <summary>
/// Regression tests for round-158 F1: Fill &#9656; Series' Date/Weekday unit was the one sibling of
/// AddMonths/AddYears (fixed in round 157 for the same class of bug, see
/// <see cref="R157_FillSeriesPre1900PhantomLeapDayTests"/>) that round 157's enumeration missed --
/// <c>AddWeekdays</c> still read its anchor with a bare <see cref="DateTime.FromOADate(double)"/> and
/// wrote the result back with a bare <c>.ToOADate()</c>, instead of routing through
/// <see cref="DateTimeValue.ToDateTime"/> / <see cref="DateTimeValue.FromDateTime"/>. Excel's 1900
/// calendar contains a phantom 1900-02-29 that .NET's OADate does not, so every genuine date in
/// 1900-01-01..1900-02-28 sits one day later in OADate space than in Excel-serial space -- the
/// unadjusted round trip in AddWeekdays therefore read the seed one day early AND, independently,
/// mis-wrote the shifted result, so a weekend-anchored seed in that window landed a full day (and one
/// weekday) later than Excel produces.
/// </summary>
public sealed class R158_FillSeriesWeekdayPre1900PhantomLeapDayTests
{
    [Fact]
    public void BuildDateSeriesEdits_WeekdayUnit_FromSaturdayBeforeThePhantomLeapDay_LandsOnTheRealNextMonday()
    {
        // Seed = Excel serial 6 = the genuine 1900-01-06, a Saturday. One Weekday step must land on
        // the real next weekday, Monday 1900-01-08 (Excel serial 8) -- the finding's exact
        // reproduction. The pre-fix bug read the anchor a day early (landing the weekend skip one day
        // off) and re-encoded the result without the correction, producing serial 9 (1900-01-09,
        // Tuesday) instead.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        var seed = DateTimeValue.FromDateTime(new DateTime(1900, 1, 6));
        seed.Value.Should().Be(6); // sanity: matches the finding's reproduction exactly.
        sheet.SetCell(range.Start, seed);

        var edits = FillSeriesPlanner.BuildDateSeriesEdits(
            sheet, range, step: 1, FillSeriesDirection.Columns, FillSeriesDateUnit.Weekday);

        edits.Should().HaveCount(1);
        var filled = (DateTimeValue)edits[0].NewCell.Value;
        filled.Value.Should().Be(8, "the real Monday 1900-01-08, not 1900-01-09");
        filled.ToDateTime().Should().Be(new DateTime(1900, 1, 8));
        filled.ToDateTime().DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void BuildDateSeriesEdits_WeekdayUnit_FromAfterTheFakeLeapDayBoundary_IsUnaffected()
    {
        // Sibling/no-regression case: a seed on-or-after 1 March 1900 (here, an ordinary modern
        // Saturday) needs no correction at all -- Excel serial and OADate already agree there -- so
        // this must keep skipping weekends exactly as it did before the fix (this mirrors the
        // pre-existing BuildDateSeriesEdits_SkipsWeekendsForExcelWeekdayUnit coverage).
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 1));
        sheet.SetCell(range.Start, DateTimeValue.FromDateTime(new DateTime(2026, 5, 29))); // a Friday

        var edits = FillSeriesPlanner.BuildDateSeriesEdits(
            sheet, range, step: 1, FillSeriesDirection.Columns, FillSeriesDateUnit.Weekday);

        edits.Select(edit => ((DateTimeValue)edit.NewCell.Value).ToDateTime().Date)
            .Should()
            .Equal(new DateTime(2026, 6, 1), new DateTime(2026, 6, 2));
    }
}
