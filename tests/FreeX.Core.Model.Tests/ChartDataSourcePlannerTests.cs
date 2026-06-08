using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class ChartDataSourcePlannerTests
{
    [Fact]
    public void ResolveInsertionRange_UsesExplicitMultiCellSelection()
    {
        var sheet = CreateSheetWithChartTable();
        var selected = Range(sheet, 2, 2, 4, 3);

        var resolved = ChartDataSourcePlanner.ResolveInsertionRange(sheet, selected);

        resolved.Should().Be(selected);
    }

    [Fact]
    public void ResolveInsertionRange_ExpandsSingleCellToCurrentRegion()
    {
        var sheet = CreateSheetWithChartTable();

        var resolved = ChartDataSourcePlanner.ResolveInsertionRange(sheet, Range(sheet, 3, 2, 3, 2));

        resolved.Should().Be(Range(sheet, 1, 1, 5, 4));
    }

    [Fact]
    public void ResolveInsertionRange_UsesStructuredTableRangeForSingleCellInsideTable()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var tableRange = Range(sheet, 3, 2, 8, 4);
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "SalesTable",
            DisplayName = "SalesTable",
            Range = tableRange
        });

        var resolved = ChartDataSourcePlanner.ResolveInsertionRange(sheet, Range(sheet, 5, 3, 5, 3));

        resolved.Should().Be(tableRange);
    }

    [Fact]
    public void ResolveInsertionRange_PreservesFilteredHiddenRowsAndColumnsInCurrentRegion()
    {
        var sheet = CreateSheetWithChartTable();
        sheet.HiddenRows.Add(3);
        sheet.FilterHiddenRows.Add(4);
        sheet.HiddenCols.Add(2);
        sheet.GroupHiddenCols.Add(4);

        var resolved = ChartDataSourcePlanner.ResolveInsertionRange(sheet, Range(sheet, 2, 3, 2, 3));

        resolved.Should().Be(Range(sheet, 1, 1, 5, 4));
        sheet.HiddenRows.Should().Equal(3u);
        sheet.FilterHiddenRows.Should().Equal(4u);
        sheet.HiddenCols.Should().Equal(2u);
        sheet.GroupHiddenCols.Should().Equal(4u);
    }

    private static Sheet CreateSheetWithChartTable()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 5; row++)
        {
            for (uint col = 1; col <= 4; col++)
            {
                ScalarValue value = row == 1 || col == 1
                    ? new TextValue(row == 1 ? $"Header {col}" : $"Row {row}")
                    : new NumberValue(row * 10 + col);
                sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
            }
        }

        return sheet;
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol));
}
