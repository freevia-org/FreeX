using FluentAssertions;
using FreeX.App.Presentation.FillSeries;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FillSeries;

/// <summary>
/// Month and Year fills re-anchor from the seed and multiply the step by the row offset, so the
/// target date grows with the size of the selection. Filling a few thousand rows by year — or a
/// whole column by month — walks past year 9999, where DateTime.AddYears/AddMonths throw. That
/// aborted the entire fill from the Fill ▸ Series click handler. Excel stops the series at the
/// boundary instead, and so do we.
/// </summary>
public sealed class FillSeriesDateRangeOverflowTests
{
    private static (Sheet Sheet, GridRange Range) SheetWithSeed(DateTime seed, uint rowCount)
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, rowCount, 1));
        sheet.SetCell(range.Start, DateTimeValue.FromDateTime(seed));
        return (sheet, range);
    }

    [Fact]
    public void BuildSeriesEdits_YearFillRunningPastTheMaximumYear_StopsInsteadOfThrowing()
    {
        var (sheet, range) = SheetWithSeed(new DateTime(2026, 6, 1), rowCount: 10_000u);

        var build = () => FillSeriesPlanner.BuildSeriesEdits(
            sheet,
            range,
            new FillSeriesOptions(
                Step: 1,
                SeriesIn: FillSeriesDirection.Columns,
                Type: FillSeriesType.Date,
                DateUnit: FillSeriesDateUnit.Year));

        build.Should().NotThrow();
    }

    [Fact]
    public void BuildSeriesEdits_YearFillRunningPastTheMaximumYear_KeepsEveryDateItDidWrite()
    {
        var (sheet, range) = SheetWithSeed(new DateTime(2026, 6, 1), rowCount: 10_000u);

        var edits = FillSeriesPlanner.BuildSeriesEdits(
            sheet,
            range,
            new FillSeriesOptions(
                Step: 1,
                SeriesIn: FillSeriesDirection.Columns,
                Type: FillSeriesType.Date,
                DateUnit: FillSeriesDateUnit.Year));

        // The fill stops at the calendar boundary rather than failing, and everything it did write
        // is a real date.
        edits.Should().NotBeEmpty();
        edits.Should().OnlyContain(edit => ((DateTimeValue)edit.NewCell.Value).ToDateTime().Year <= 9999);
    }

    [Fact]
    public void BuildSeriesEdits_MonthFillRunningPastTheMaximumYear_StopsInsteadOfThrowing()
    {
        var (sheet, range) = SheetWithSeed(new DateTime(2026, 6, 1), rowCount: 120_000u);

        var build = () => FillSeriesPlanner.BuildSeriesEdits(
            sheet,
            range,
            new FillSeriesOptions(
                Step: 1,
                SeriesIn: FillSeriesDirection.Columns,
                Type: FillSeriesType.Date,
                DateUnit: FillSeriesDateUnit.Month));

        build.Should().NotThrow();
    }

    [Fact]
    public void BuildSeriesEdits_YearFillWithAHugeStep_StopsInsteadOfOverflowing()
    {
        // step * rowOffset overflowed int before it ever reached the calendar check.
        var (sheet, range) = SheetWithSeed(new DateTime(2026, 6, 1), rowCount: 50u);

        var build = () => FillSeriesPlanner.BuildSeriesEdits(
            sheet,
            range,
            new FillSeriesOptions(
                Step: 500_000_000,
                SeriesIn: FillSeriesDirection.Columns,
                Type: FillSeriesType.Date,
                DateUnit: FillSeriesDateUnit.Year));

        build.Should().NotThrow();
    }

    [Fact]
    public void BuildSeriesEdits_OrdinaryYearFill_IsUnchanged()
    {
        var (sheet, range) = SheetWithSeed(new DateTime(2026, 6, 1), rowCount: 4u);

        var edits = FillSeriesPlanner.BuildSeriesEdits(
            sheet,
            range,
            new FillSeriesOptions(
                Step: 1,
                SeriesIn: FillSeriesDirection.Columns,
                Type: FillSeriesType.Date,
                DateUnit: FillSeriesDateUnit.Year));

        edits.Select(edit => ((DateTimeValue)edit.NewCell.Value).ToDateTime().Date)
            .Should()
            .Equal(
                new DateTime(2027, 6, 1),
                new DateTime(2028, 6, 1),
                new DateTime(2029, 6, 1));
    }
}
