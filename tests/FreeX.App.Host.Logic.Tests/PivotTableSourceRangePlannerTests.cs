using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class PivotTableSourceRangePlannerTests
{
    [Fact]
    public void Create_ExpandsSingleCellSelectionToCurrentRegion()
    {
        var sheet = CreateSheetWithList();
        var selectedCell = Address(sheet, 3, 2);

        var range = PivotTableSourceRangePlanner.Create(sheet, new GridRange(selectedCell, selectedCell));

        range.Should().Be(new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 3)));
    }

    [Fact]
    public void Create_KeepsExplicitRange()
    {
        var sheet = CreateSheetWithList();
        var selectedRange = new GridRange(Address(sheet, 2, 1), Address(sheet, 4, 3));

        PivotTableSourceRangePlanner.Create(sheet, selectedRange).Should().Be(selectedRange);
    }

    [Fact]
    public void CreatePlan_ExpandsSingleColumnSelectionToWiderCurrentRegion()
    {
        var sheet = CreateSheetWithList();
        var selectedRange = new GridRange(Address(sheet, 1, 2), Address(sheet, 4, 2));

        var plan = PivotTableSourceRangePlanner.CreatePlan(sheet, selectedRange);

        plan.IsValid.Should().BeTrue();
        plan.SourceRange.Should().Be(new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 3)));
        plan.Error.Should().Be(PivotTableSourceRangeError.None);
    }

    [Fact]
    public void CreatePlan_RejectsSourcesWithBlankHeaderCells()
    {
        var sheet = CreateSheetWithList();
        sheet.ClearCell(1, 2);

        var plan = PivotTableSourceRangePlanner.CreatePlan(sheet, new GridRange(Address(sheet, 3, 2), Address(sheet, 3, 2)));

        plan.IsValid.Should().BeFalse();
        plan.SourceRange.Should().Be(new GridRange(Address(sheet, 1, 1), Address(sheet, 4, 3)));
        plan.Error.Should().Be(PivotTableSourceRangeError.MissingHeaders);
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(1, 4)]
    public void Create_KeepsInvalidOneDimensionalCurrentRegion(uint rows, uint columns)
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= rows; row++)
        {
            for (uint col = 1; col <= columns; col++)
                sheet.SetCell(Address(sheet, row, col), new NumberValue(row + col));
        }

        var selectedCell = Address(sheet, 1, 1);

        PivotTableSourceRangePlanner.Create(sheet, new GridRange(selectedCell, selectedCell))
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
