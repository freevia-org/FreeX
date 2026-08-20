using FluentAssertions;
using FreeX.App.Presentation.FillSeries;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FillSeries;

/// <summary>
/// Regression tests for sweep95-F1: Fill ▸ Series' Date/Month and Date/Year units converted the
/// seed's Excel serial to a <see cref="DateTime"/> with a bare <c>DateTime.FromOADate</c> and wrote
/// the shifted result back with a bare <c>.ToOADate()</c>. Excel and .NET's OADate disagree by
/// exactly one day for any date before 1 March 1900, because Excel keeps a phantom 29 February 1900
/// that never existed and OADate does not -- so for a seed in that range, the unadjusted round trip
/// landed one day off, and for a Month/Year fill starting in January 1900 that off-by-one lands
/// exactly on the phantom leap day itself (serial 60) instead of the real last day of February
/// (serial 59). <see cref="DateTimeValue.ToDateTime"/> / <see cref="DateTimeValue.FromDateTime"/>
/// apply the correction; <c>AddMonths</c>/<c>AddYears</c> in FillSeriesPlanner now route through
/// them instead of the bare OADate calls, matching the seed conversion
/// (<c>lineSeed.ToDateTime()</c>) already used three lines above their call site in
/// <see cref="FillSeriesPlanner.BuildDateSeriesEdits"/>.
/// </summary>
public sealed class R157_FillSeriesPre1900PhantomLeapDayTests
{
    [Fact]
    public void BuildDateSeriesEdits_MonthUnit_FromLateJanuary1900_LandsOnRealEndOfFebruaryNotThePhantomLeapDay()
    {
        // Seed = Excel serial 31 = the genuine 1900-01-31 (DateTimeValue.FromDateTime applies the
        // same -1 correction BuildDateSeriesEdits' own seed read relies on). Filling one month
        // forward must clamp to the real last day of February 1900 -- Excel serial 59 (1900-02-28)
        // -- not the phantom leap day, Excel serial 60 (1900-02-29, a date that never existed).
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        var seed = DateTimeValue.FromDateTime(new DateTime(1900, 1, 31));
        seed.Value.Should().Be(31); // sanity: matches the finding's reproduction exactly.
        sheet.SetCell(range.Start, seed);

        var edits = FillSeriesPlanner.BuildDateSeriesEdits(
            sheet, range, step: 1, FillSeriesDirection.Columns, FillSeriesDateUnit.Month);

        edits.Should().HaveCount(1);
        var filled = (DateTimeValue)edits[0].NewCell.Value;
        filled.Value.Should().Be(59, "the real 1900-02-28, not the phantom 1900-02-29 (serial 60)");
        filled.ToDateTime().Should().Be(new DateTime(1900, 2, 28));
    }

    [Fact]
    public void BuildDateSeriesEdits_YearUnit_FromMidFebruary1900_RoundTripsThroughTheSameDay()
    {
        // Seed = genuine 1900-02-15 (still before the phantom leap day). A one-year fill must land
        // on genuine 1901-02-15 -- reading the seed one day short (the pre-fix bug) would instead
        // shift 1900-02-14 forward a year to 1901-02-14.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        sheet.SetCell(range.Start, DateTimeValue.FromDateTime(new DateTime(1900, 2, 15)));

        var edits = FillSeriesPlanner.BuildDateSeriesEdits(
            sheet, range, step: 1, FillSeriesDirection.Columns, FillSeriesDateUnit.Year);

        edits.Should().HaveCount(1);
        var filled = (DateTimeValue)edits[0].NewCell.Value;
        filled.ToDateTime().Should().Be(new DateTime(1901, 2, 15));
        filled.Value.Should().Be(DateTimeValue.FromDateTime(new DateTime(1901, 2, 15)).Value);
    }

    [Fact]
    public void BuildDateSeriesEdits_MonthUnit_FromAfterTheFakeLeapDayBoundary_IsUnaffected()
    {
        // Sibling/no-regression case: a seed on-or-after 1 March 1900 (here, an ordinary modern
        // date) needs no correction at all -- Excel serial and OADate already agree there, so this
        // must keep behaving exactly as it did before the fix.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 1));
        sheet.SetCell(range.Start, DateTimeValue.FromDateTime(new DateTime(2026, 1, 31)));

        var edits = FillSeriesPlanner.BuildDateSeriesEdits(
            sheet, range, step: 1, FillSeriesDirection.Columns, FillSeriesDateUnit.Month);

        edits.Select(edit => ((DateTimeValue)edit.NewCell.Value).ToDateTime().Date)
            .Should()
            .Equal(
                new DateTime(2026, 2, 28),
                new DateTime(2026, 3, 31),
                new DateTime(2026, 4, 30));
    }
}
