using FluentAssertions;
using Freexcel.App.UI;
using Freexcel.Core.Model;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewEditingCellTests
{
    [Fact]
    public void ShouldDrawCellContent_HidesEditedCellDisplayText()
    {
        var sheetId = SheetId.New();
        var cell = new DisplayCell(4, 5, null, "=SUM(E5:E7)", null, StyleId.Default, null);

        GridView.ShouldDrawCellContent(cell, new CellAddress(sheetId, 4, 5))
            .Should().BeFalse();
    }

    [Fact]
    public void ShouldDrawCellContent_DrawsOtherCells()
    {
        var sheetId = SheetId.New();
        var cell = new DisplayCell(5, 5, new NumberValue(1), "1", null, StyleId.Default, null);

        GridView.ShouldDrawCellContent(cell, new CellAddress(sheetId, 4, 5))
            .Should().BeTrue();
    }

    [Fact]
    public void BuildOccupiedCellSet_TreatsEmptyEditedCellAsOccupied()
    {
        var sheetId = SheetId.New();
        var cells = new[]
        {
            new DisplayCell(1, 1, new TextValue("long text"), "long text", null, StyleId.Default, null),
            new DisplayCell(1, 2, null, "", null, StyleId.Default, null),
        };

        var occupied = GridView.BuildOccupiedCellSet(cells, new CellAddress(sheetId, 1, 2));

        occupied.Should().Contain((1u, 1u));
        occupied.Should().Contain((1u, 2u));
    }
}
