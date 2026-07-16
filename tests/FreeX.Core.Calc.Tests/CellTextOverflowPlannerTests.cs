using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

public sealed class CellTextOverflowPlannerTests
{
    [Fact]
    public void CanOverflowCellText_AllowsPlainTextButNotNumericOrWrappedCells()
    {
        CellTextOverflowPlanner.CanOverflowCellText(null, new TextValue("Title"), "Title", null)
            .Should().BeTrue();
        CellTextOverflowPlanner.CanOverflowCellText(null, new NumberValue(42), "42", null)
            .Should().BeFalse();
        CellTextOverflowPlanner.CanOverflowCellText(
                new CellStyle { WrapText = true },
                new TextValue("Title"),
                "Title",
                null)
            .Should().BeFalse();
    }

    [Fact]
    public void IsOverflowOccupied_TreatsEditedAndMergedCellsAsOccupied()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 1, 2);
        var blank = new DisplayCell(1, 2, null, "", null, StyleId.Default, null);

        CellTextOverflowPlanner.IsOverflowOccupied(blank, address).Should().BeTrue();
        CellTextOverflowPlanner.IsOverflowOccupied(blank, null, new GridRange(address, address))
            .Should().BeTrue();
        CellTextOverflowPlanner.IsOverflowOccupied(blank, null).Should().BeFalse();
    }
}
