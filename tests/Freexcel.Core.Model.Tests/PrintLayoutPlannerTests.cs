using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public sealed class PrintLayoutPlannerTests
{
    [Fact]
    public void BuildRowPlans_RepeatsTitleRowsAndKeepsThemOutOfBody()
    {
        var sheetId = SheetId.New();
        var printRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 6, 3));

        var pages = PrintLayoutPlanner.BuildRowPlans(
            printRange,
            new WorksheetRepeatRange(1, 1),
            rowsPerPage: 3);

        pages.Should().HaveCount(3);
        pages[0].TitleRows.Should().Equal(1u);
        pages[0].BodyRows.Should().Equal(2u, 3u);
        pages[1].TitleRows.Should().Equal(1u);
        pages[1].BodyRows.Should().Equal(4u, 5u);
        pages[2].TitleRows.Should().Equal(1u);
        pages[2].BodyRows.Should().Equal(6u);
    }

    [Fact]
    public void BuildRowPlans_UsesAllRowsAsBodyWhenNoPrintTitles()
    {
        var sheetId = SheetId.New();
        var printRange = new GridRange(
            new CellAddress(sheetId, 2, 1),
            new CellAddress(sheetId, 5, 3));

        var pages = PrintLayoutPlanner.BuildRowPlans(printRange, repeatRows: null, rowsPerPage: 3);

        pages.Should().HaveCount(2);
        pages[0].TitleRows.Should().BeEmpty();
        pages[0].BodyRows.Should().Equal(2u, 3u, 4u);
        pages[1].TitleRows.Should().BeEmpty();
        pages[1].BodyRows.Should().Equal(5u);
    }

    [Fact]
    public void BuildColumnPlans_RepeatsTitleColumnsAndKeepsThemOutOfBody()
    {
        var sheetId = SheetId.New();
        var printRange = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 6));

        var pages = PrintLayoutPlanner.BuildColumnPlans(
            printRange,
            new WorksheetRepeatRange(1, 1),
            columnsPerPage: 3);

        pages.Should().HaveCount(3);
        pages[0].TitleColumns.Should().Equal(1u);
        pages[0].BodyColumns.Should().Equal(2u, 3u);
        pages[1].TitleColumns.Should().Equal(1u);
        pages[1].BodyColumns.Should().Equal(4u, 5u);
        pages[2].TitleColumns.Should().Equal(1u);
        pages[2].BodyColumns.Should().Equal(6u);
    }

    [Fact]
    public void MeasurePrintableGrid_IncludesHeadingSlotsWhenEnabled()
    {
        var size = PrintLayoutPlanner.MeasurePrintableGrid(
            printableWidth: 400,
            printableHeight: 200,
            rowCount: 5,
            columnCount: 4,
            printHeadings: true);

        size.HeaderWidth.Should().Be(40);
        size.HeaderHeight.Should().Be(20);
        size.ColumnWidth.Should().Be(90);
        size.RowHeight.Should().Be(20);
    }
}
