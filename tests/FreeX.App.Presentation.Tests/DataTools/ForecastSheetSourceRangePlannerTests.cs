using FreeX.Core.Model;
using FluentAssertions;
using FreeX.App.Services;

namespace FreeX.App.Presentation.Tests.DataTools;

public sealed class ForecastSheetSourceRangePlannerTests
{
    [Fact]
    public void Create_ExpandsSingleCellInsideTwoColumnCurrentRegion()
    {
        var sheet = CreateForecastSourceSheet();
        var selected = Range(sheet, 3, 2, 3, 2);

        var planned = ForecastSheetSourceRangePlanner.Create(sheet, selected);

        planned.Should().Be(Range(sheet, 1, 1, 4, 2));
    }

    [Fact]
    public void Create_PreservesExplicitTwoColumnSelection()
    {
        var sheet = CreateForecastSourceSheet();
        var selected = Range(sheet, 1, 1, 4, 2);

        var planned = ForecastSheetSourceRangePlanner.Create(sheet, selected);

        planned.Should().Be(selected);
    }

    [Theory]
    [InlineData(2, 1, 2, 2)]
    [InlineData(4, 1, 3, 2)]
    public void Create_DoesNotExpandUnsupportedCurrentRegionShapes(
        uint endRow,
        uint startCol,
        uint endCol,
        uint selectedCol)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sales");
        for (uint row = 1; row <= endRow; row++)
        {
            for (uint col = startCol; col <= endCol; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row * col));
        }

        var selected = Range(sheet, Math.Min(endRow, 2), selectedCol, Math.Min(endRow, 2), selectedCol);

        var planned = ForecastSheetSourceRangePlanner.Create(sheet, selected);

        planned.Should().Be(selected);
    }

    private static Sheet CreateForecastSourceSheet()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sales");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        return sheet;
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol));
}
