using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class AdvancedFilterDefaultListRangePlannerTests
{
    [Fact]
    public void Create_ExpandsSingleCellSelectionToCurrentRegion()
    {
        var sheet = CreateSheetWithList();
        var selectedCell = Address(sheet, 3, 2);

        var range = AdvancedFilterDefaultListRangePlanner.Create(sheet, new GridRange(selectedCell, selectedCell));

        range.Should().Be(new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 3)));
    }

    [Fact]
    public void Create_PreservesExplicitSelection()
    {
        var sheet = CreateSheetWithList();
        var selectedRange = new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 3));

        AdvancedFilterDefaultListRangePlanner.Create(sheet, selectedRange).Should().Be(selectedRange);
    }

    [Fact]
    public void Create_PreservesSingleCellWhenNoListRegionExists()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var selectedCell = Address(sheet, 8, 4);

        AdvancedFilterDefaultListRangePlanner.Create(sheet, new GridRange(selectedCell, selectedCell))
            .Should()
            .Be(new GridRange(selectedCell, selectedCell));
    }

    private static Sheet CreateSheetWithList()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Name"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Score"));
        sheet.SetCell(Address(sheet, 1, 3), new TextValue("Team"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("Ada"));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(1));
        sheet.SetCell(Address(sheet, 2, 3), new TextValue("East"));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("Beth"));
        sheet.SetCell(Address(sheet, 3, 2), new NumberValue(2));
        sheet.SetCell(Address(sheet, 3, 3), new TextValue("West"));
        sheet.SetCell(Address(sheet, 4, 1), new TextValue("Cy"));
        sheet.SetCell(Address(sheet, 4, 2), new NumberValue(3));
        sheet.SetCell(Address(sheet, 4, 3), new TextValue("North"));
        return sheet;
    }

    private static CellAddress Address(Sheet sheet, uint row, uint col) =>
        new(sheet.Id, row, col);
}
