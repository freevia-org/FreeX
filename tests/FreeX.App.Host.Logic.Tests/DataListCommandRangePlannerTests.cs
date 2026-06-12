using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class DataListCommandRangePlannerTests
{
    [Fact]
    public void Create_ExpandsSingleCellSelectionToCurrentRegion()
    {
        var sheet = CreateSalesList();
        var selected = Range(sheet, 3, 2, 3, 2);

        var range = DataListCommandRangePlanner.Create(sheet, selected);

        range.Should().Be(Range(sheet, 1, 1, 4, 3));
    }

    [Fact]
    public void Create_PreservesExplicitSelection()
    {
        var sheet = CreateSalesList();
        var selected = Range(sheet, 2, 1, 4, 3);

        var range = DataListCommandRangePlanner.Create(sheet, selected);

        range.Should().Be(selected);
    }

    [Fact]
    public void Create_PreservesSingleCellWhenNoCurrentRegionExists()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var selected = Range(sheet, 3, 2, 3, 2);

        var range = DataListCommandRangePlanner.Create(sheet, selected);

        range.Should().Be(selected);
    }

    private static Sheet CreateSalesList()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Region"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Sales"));
        sheet.SetCell(Address(sheet, 1, 3), new TextValue("Rep"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("East"));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(10));
        sheet.SetCell(Address(sheet, 2, 3), new TextValue("Ada"));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("East"));
        sheet.SetCell(Address(sheet, 3, 2), new NumberValue(10));
        sheet.SetCell(Address(sheet, 3, 3), new TextValue("Ada"));
        sheet.SetCell(Address(sheet, 4, 1), new TextValue("West"));
        sheet.SetCell(Address(sheet, 4, 2), new NumberValue(12));
        sheet.SetCell(Address(sheet, 4, 3), new TextValue("Beth"));
        return sheet;
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(Address(sheet, startRow, startCol), Address(sheet, endRow, endCol));

    private static CellAddress Address(Sheet sheet, uint row, uint col) =>
        new(sheet.Id, row, col);
}
