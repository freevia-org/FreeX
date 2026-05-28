using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class FillSeriesPlannerTests
{
    [Theory]
    [InlineData("1", true, 1)]
    [InlineData(" -2.5 ", true, -2.5)]
    [InlineData("0", true, 0)]
    [InlineData("", false, 0)]
    [InlineData("NaN", false, 0)]
    [InlineData("Infinity", false, 0)]
    [InlineData("step", false, 0)]
    public void TryParseStep_ParsesFiniteNumericStep(string input, bool expected, double expectedStep)
    {
        var result = FillSeriesPlanner.TryParseStep(input, out var step);

        result.Should().Be(expected);
        step.Should().Be(expectedStep);
    }

    [Theory]
    [InlineData(FillCellsDirection.Down, 2, 1, true)]
    [InlineData(FillCellsDirection.Up, 2, 1, true)]
    [InlineData(FillCellsDirection.Right, 1, 2, true)]
    [InlineData(FillCellsDirection.Left, 1, 2, true)]
    [InlineData(FillCellsDirection.Down, 1, 2, false)]
    [InlineData(FillCellsDirection.Right, 2, 1, false)]
    public void CanFill_RequiresMultipleCellsInFillDirection(FillCellsDirection direction, uint rows, uint columns, bool expected)
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, rows, columns));

        FillSeriesPlanner.CanFill(range, direction).Should().Be(expected);
    }

    [Fact]
    public void BuildLinearSeriesEdits_FillsRowMajorCellsAfterStartingCell()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3));
        sheet.SetCell(range.Start, new NumberValue(10));

        var edits = FillSeriesPlanner.BuildLinearSeriesEdits(sheet, range, step: 2);

        edits.Should().HaveCount(3);
        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 3, 2),
            new CellAddress(sheet.Id, 3, 3));
        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(12, 14, 16);
    }

    [Fact]
    public void BuildLinearSeriesEdits_UsesColumnMajorOrderForExcelSeriesInColumns()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3));
        sheet.SetCell(range.Start, new NumberValue(10));

        var edits = FillSeriesPlanner.BuildLinearSeriesEdits(
            sheet,
            range,
            step: 2,
            FillSeriesDirection.Columns);

        edits.Select(edit => edit.Address).Should().Equal(
            new CellAddress(sheet.Id, 3, 2),
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 3, 3));
        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(12, 14, 16);
    }

    [Fact]
    public void BuildLinearSeriesEdits_StopsAtAscendingExcelStopValue()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 5));
        sheet.SetCell(range.Start, new NumberValue(0));

        var edits = FillSeriesPlanner.BuildLinearSeriesEdits(
            sheet,
            range,
            step: 1,
            FillSeriesDirection.Rows,
            stopValue: 3);

        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void BuildLinearSeriesEdits_StopsAtDescendingExcelStopValue()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 5));
        sheet.SetCell(range.Start, new NumberValue(0));

        var edits = FillSeriesPlanner.BuildLinearSeriesEdits(
            sheet,
            range,
            step: -1,
            FillSeriesDirection.Rows,
            stopValue: -3);

        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(-1, -2, -3);
    }

    [Fact]
    public void BuildSeriesEdits_RoutesGrowthTypeThroughExcelGrowthSeries()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 4));
        sheet.SetCell(range.Start, new NumberValue(2));

        var edits = FillSeriesPlanner.BuildSeriesEdits(
            sheet,
            range,
            new FillSeriesStepDialogResult(
                Step: 2,
                SeriesIn: FillSeriesDirection.Rows,
                Type: FillSeriesType.Growth));

        edits.Select(edit => ((NumberValue)edit.NewCell.Value).Value).Should().Equal(4, 8, 16);
    }

    [Fact]
    public void BuildSeriesEdits_RoutesDateTypeThroughExcelMonthSeries()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 4));
        sheet.SetCell(range.Start, DateTimeValue.FromDateTime(new DateTime(2026, 1, 31)));

        var edits = FillSeriesPlanner.BuildSeriesEdits(
            sheet,
            range,
            new FillSeriesStepDialogResult(
                Step: 1,
                SeriesIn: FillSeriesDirection.Rows,
                Type: FillSeriesType.Date,
                DateUnit: FillSeriesDateUnit.Month));

        edits.Select(edit => ((DateTimeValue)edit.NewCell.Value).ToDateTime().Date)
            .Should()
            .Equal(
                new DateTime(2026, 2, 28),
                new DateTime(2026, 3, 31),
                new DateTime(2026, 4, 30));
    }

    [Fact]
    public void BuildDateSeriesEdits_SkipsWeekendsForExcelWeekdayUnit()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 3));
        sheet.SetCell(range.Start, DateTimeValue.FromDateTime(new DateTime(2026, 5, 29)));

        var edits = FillSeriesPlanner.BuildDateSeriesEdits(
            sheet,
            range,
            step: 1,
            seriesIn: FillSeriesDirection.Rows,
            dateUnit: FillSeriesDateUnit.Weekday);

        edits.Select(edit => ((DateTimeValue)edit.NewCell.Value).ToDateTime().Date)
            .Should()
            .Equal(new DateTime(2026, 6, 1), new DateTime(2026, 6, 2));
    }

    [Fact]
    public void BuildLinearSeriesEdits_ReturnsNoEditsWhenStartingCellIsNotNumeric()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 3));
        sheet.SetCell(range.Start, new TextValue("Start"));

        FillSeriesPlanner.BuildLinearSeriesEdits(sheet, range, step: 1).Should().BeEmpty();
    }
}
