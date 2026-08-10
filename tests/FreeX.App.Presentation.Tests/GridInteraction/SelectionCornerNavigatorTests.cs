using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.GridInteraction;

public sealed class SelectionCornerNavigatorTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Fact]
    public void GetNextCorner_CyclesClockwiseThroughRectangleCorners()
    {
        var range = new GridRange(
            new CellAddress(SheetId, 2, 3),
            new CellAddress(SheetId, 5, 7));

        SelectionCornerNavigator.GetNextCorner(range, new CellAddress(SheetId, 2, 3))
            .Should()
            .Be(new CellAddress(SheetId, 2, 7));

        SelectionCornerNavigator.GetNextCorner(range, new CellAddress(SheetId, 2, 7))
            .Should()
            .Be(new CellAddress(SheetId, 5, 7));

        SelectionCornerNavigator.GetNextCorner(range, new CellAddress(SheetId, 5, 7))
            .Should()
            .Be(new CellAddress(SheetId, 5, 3));

        SelectionCornerNavigator.GetNextCorner(range, new CellAddress(SheetId, 5, 3))
            .Should()
            .Be(new CellAddress(SheetId, 2, 3));
    }

    [Fact]
    public void GetNextCorner_SkipsDuplicateCornersForSingleRowSelection()
    {
        var range = new GridRange(
            new CellAddress(SheetId, 4, 2),
            new CellAddress(SheetId, 4, 6));

        SelectionCornerNavigator.GetNextCorner(range, new CellAddress(SheetId, 4, 2))
            .Should()
            .Be(new CellAddress(SheetId, 4, 6));

        SelectionCornerNavigator.GetNextCorner(range, new CellAddress(SheetId, 4, 6))
            .Should()
            .Be(new CellAddress(SheetId, 4, 2));
    }

    [Fact]
    public void GetNextCorner_SkipsDuplicateCornersForSingleColumnSelection()
    {
        var range = new GridRange(
            new CellAddress(SheetId, 2, 4),
            new CellAddress(SheetId, 6, 4));

        SelectionCornerNavigator.GetNextCorner(range, new CellAddress(SheetId, 2, 4))
            .Should()
            .Be(new CellAddress(SheetId, 6, 4));

        SelectionCornerNavigator.GetNextCorner(range, new CellAddress(SheetId, 6, 4))
            .Should()
            .Be(new CellAddress(SheetId, 2, 4));
    }

    [Fact]
    public void GetNextCorner_KeepsSingleCellSelectionStable()
    {
        var cell = new CellAddress(SheetId, 5, 5);
        var range = new GridRange(cell, cell);

        SelectionCornerNavigator.GetNextCorner(range, cell)
            .Should()
            .Be(cell);
    }

    [Fact]
    public void GetNextCorner_ReturnsStartWhenCurrentCellIsNotASelectionCorner()
    {
        var range = new GridRange(
            new CellAddress(SheetId, 2, 3),
            new CellAddress(SheetId, 5, 7));

        SelectionCornerNavigator.GetNextCorner(range, new CellAddress(SheetId, 3, 4))
            .Should()
            .Be(range.Start);
    }
}
