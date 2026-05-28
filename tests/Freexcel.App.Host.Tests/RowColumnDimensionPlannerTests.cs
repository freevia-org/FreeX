using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class RowColumnDimensionPlannerTests
{
    [Fact]
    public void GetDialogValues_UseExplicitDimensionThenSheetDefaultThenFallback()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var range = Range(sheet.Id, 3, 4, 5, 6);
        sheet.DefaultRowHeight = 18;
        sheet.DefaultColumnWidth = 9.25;
        sheet.RowHeights[3] = 24;
        sheet.ColumnWidths[4] = 14.5;

        RowColumnDimensionPlanner.GetRowHeightDialogValue(sheet, range).Should().Be(24);
        RowColumnDimensionPlanner.GetColumnWidthDialogValue(sheet, range).Should().Be(14.5);
        RowColumnDimensionPlanner.GetRowHeightDialogValue(sheet, Range(sheet.Id, 7, 4, 7, 4)).Should().Be(18);
        RowColumnDimensionPlanner.GetColumnWidthDialogValue(sheet, Range(sheet.Id, 3, 8, 3, 8)).Should().Be(9.25);
        RowColumnDimensionPlanner.GetRowHeightDialogValue(null, range).Should().Be(20);
        RowColumnDimensionPlanner.GetColumnWidthDialogValue(null, range).Should().Be(8.43);
    }

    [Fact]
    public void CreateDimensionCommands_ApplyToSelectionSpans()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new SimpleCommandContext(workbook);

        RowColumnDimensionPlanner.CreateRowHeightCommand(sheet.Id, Range(sheet.Id, 2, 3, 4, 5), 30)
            .Apply(context)
            .Success.Should()
            .BeTrue();
        RowColumnDimensionPlanner.CreateColumnWidthCommand(sheet.Id, Range(sheet.Id, 2, 3, 4, 5), 12)
            .Apply(context)
            .Success.Should()
            .BeTrue();

        sheet.RowHeights.Should().ContainKeys(2u, 3u, 4u);
        sheet.RowHeights.Values.Should().OnlyContain(height => height == 30);
        sheet.ColumnWidths.Should().ContainKeys(3u, 4u, 5u);
        sheet.ColumnWidths.Values.Should().OnlyContain(width => width == 12);
    }

    [Fact]
    public void CreateAutoFitCommands_ApplySingleAndCompositePlans()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new SimpleCommandContext(workbook);

        RowColumnDimensionPlanner.CreateAutoFitRowHeightCommand(
                sheet.Id,
                [new AutoFitSizePlan(2, 21), new AutoFitSizePlan(3, 34)])
            .Apply(context)
            .Success.Should()
            .BeTrue();
        RowColumnDimensionPlanner.CreateAutoFitColumnWidthCommand(
                sheet.Id,
                [new AutoFitSizePlan(5, 18)])
            .Apply(context)
            .Success.Should()
            .BeTrue();

        sheet.RowHeights[2].Should().Be(21);
        sheet.RowHeights[3].Should().Be(34);
        sheet.ColumnWidths[5].Should().Be(18);
    }

    [Fact]
    public void CreateHiddenCommands_ApplyToSelectionSpans()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var context = new SimpleCommandContext(workbook);

        RowColumnDimensionPlanner.CreateRowsHiddenCommand(sheet.Id, Range(sheet.Id, 2, 4, 3, 6), hidden: true)
            .Apply(context)
            .Success.Should()
            .BeTrue();
        RowColumnDimensionPlanner.CreateColumnsHiddenCommand(sheet.Id, Range(sheet.Id, 2, 4, 3, 6), hidden: true)
            .Apply(context)
            .Success.Should()
            .BeTrue();

        sheet.HiddenRows.Should().Contain([2u, 3u]);
        sheet.HiddenCols.Should().Contain([4u, 5u, 6u]);
    }

    private static GridRange Range(SheetId sheetId, uint row1, uint col1, uint row2, uint col2) =>
        new(new CellAddress(sheetId, row1, col1), new CellAddress(sheetId, row2, col2));

    private sealed class SimpleCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook => workbook;
        public Sheet GetSheet(SheetId sheetId) => workbook.GetSheet(sheetId)!;
    }
}
