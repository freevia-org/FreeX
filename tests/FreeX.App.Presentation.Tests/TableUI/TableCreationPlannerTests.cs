using FluentAssertions;
using FreeX.App.Presentation.TableUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.TableUI;

public sealed class TableCreationPlannerTests
{
    [Fact]
    public void PlanSourceRange_ExpandsSingleCellSelectionToCurrentRegion()
    {
        var sheet = CreateSheetWithList();
        var selectedCell = Address(sheet, 3, 2);

        var range = TableCreationPlanner.PlanSourceRange(sheet, new GridRange(selectedCell, selectedCell));

        range.Should().Be(new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 3)));
    }

    [Fact]
    public void PlanSourceRange_KeepsExplicitRange()
    {
        var sheet = CreateSheetWithList();
        var selectedRange = new GridRange(Address(sheet, 2, 1), Address(sheet, 4, 3));

        TableCreationPlanner.PlanSourceRange(sheet, selectedRange).Should().Be(selectedRange);
    }

    [Fact]
    public void PlanSourceRange_ExpandsHeaderRowSelectionToCurrentRegion()
    {
        var sheet = CreateSheetWithList();
        var selectedRange = new GridRange(Address(sheet, 1, 1), Address(sheet, 1, 3));

        TableCreationPlanner.PlanSourceRange(sheet, selectedRange)
            .Should()
            .Be(new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 3)));
    }

    [Fact]
    public void PlanSourceRange_DoesNotBridgeAcrossBlankSeparatorRows()
    {
        var sheet = CreateSheetWithList();
        sheet.SetCell(Address(sheet, 6, 2), new TextValue("Separate"));
        var selectedCell = Address(sheet, 5, 2);

        TableCreationPlanner.PlanSourceRange(sheet, new GridRange(selectedCell, selectedCell))
            .Should()
            .Be(new GridRange(selectedCell, selectedCell));
    }

    [Fact]
    public void PlanSourceRange_KeepsBlankSingleCell()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var selectedCell = Address(sheet, 8, 4);

        TableCreationPlanner.PlanSourceRange(sheet, new GridRange(selectedCell, selectedCell))
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
