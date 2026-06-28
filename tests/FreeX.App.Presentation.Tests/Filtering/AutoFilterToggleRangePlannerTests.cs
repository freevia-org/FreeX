using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Filtering;

public sealed class AutoFilterToggleRangePlannerTests
{
    [Fact]
    public void Create_UsesWorksheetAutoFilterRangeWhenPresent()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:C9", null);
        var selectedCell = Address(sheet, 5, 2);

        var range = AutoFilterToggleRangePlanner.Create(sheet, new GridRange(selectedCell, selectedCell));

        range.Should().Be(new GridRange(Address(sheet, 1, 1), Address(sheet, 9, 3)));
    }

    [Fact]
    public void Create_ExpandsSingleCellSelectionToCurrentRegion()
    {
        var sheet = CreateSheetWithList();
        var selectedCell = Address(sheet, 3, 2);

        var range = AutoFilterToggleRangePlanner.Create(sheet, new GridRange(selectedCell, selectedCell));

        range.Should().Be(new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 2)));
    }

    [Fact]
    public void Create_PreservesExplicitSelection()
    {
        var sheet = CreateSheetWithList();
        var selectedRange = new GridRange(Address(sheet, 2, 2), Address(sheet, 3, 2));

        AutoFilterToggleRangePlanner.Create(sheet, selectedRange).Should().Be(selectedRange);
    }

    private static Sheet CreateSheetWithList()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(Address(sheet, 1, 1), new TextValue("Name"));
        sheet.SetCell(Address(sheet, 1, 2), new TextValue("Score"));
        sheet.SetCell(Address(sheet, 2, 1), new TextValue("Ada"));
        sheet.SetCell(Address(sheet, 2, 2), new NumberValue(1));
        sheet.SetCell(Address(sheet, 3, 1), new TextValue("Beth"));
        sheet.SetCell(Address(sheet, 3, 2), new NumberValue(2));
        sheet.SetCell(Address(sheet, 4, 1), new TextValue("Cy"));
        sheet.SetCell(Address(sheet, 4, 2), new NumberValue(3));
        return sheet;
    }

    private static CellAddress Address(Sheet sheet, uint row, uint col) =>
        new(sheet.Id, row, col);
}
