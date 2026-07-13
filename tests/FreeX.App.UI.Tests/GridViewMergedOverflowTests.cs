using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R40-render-merged-overflow-3-1: the text-overflow "occupied neighbor" check must treat a cell that
/// belongs to a merged region as occupied, even when the merged region is blank — Excel never lets
/// overflow text slide across a merged range. See GridView.ConditionalIcons.cs (IsOverflowOccupied /
/// BuildOccupiedCellSet).
/// </summary>
public sealed class GridViewMergedOverflowTests
{
    [Fact]
    public void IsOverflowOccupied_TreatsBlankMergedCellAsOccupied()
    {
        // B1 is blank (no text/formula/icon/data-bar) but belongs to a merged region B1:C1.
        var sheetId = SheetId.New();
        var blankMergedCell = new DisplayCell(1, 2, null, "", null, StyleId.Default, null);
        var merge = new GridRange(new CellAddress(sheetId, 1, 2), new CellAddress(sheetId, 1, 3));

        GridView.IsOverflowOccupied(blankMergedCell, editingCell: null, merge)
            .Should().BeTrue();
    }

    [Fact]
    public void IsOverflowOccupied_BlankUnmergedCellStillNotOccupied()
    {
        // Sibling/no-regression case: a genuinely blank, non-merged neighbor must remain "not occupied"
        // so overflow text can still spill across ordinary blank cells as before.
        var blankCell = new DisplayCell(1, 2, null, "", null, StyleId.Default, null);

        GridView.IsOverflowOccupied(blankCell, editingCell: null, merge: null)
            .Should().BeFalse();
    }

    [Fact]
    public void BuildOccupiedCellSet_BlockOverflowAcrossBlankMergedRegion()
    {
        // A1: long overflowing text, not merged. B1:C1: a separate, blank merged region.
        // Excel stops A1's overflow at B1's left edge instead of sliding across the blank merge.
        var sheetId = SheetId.New();
        var cells = new[]
        {
            new DisplayCell(1, 1, new TextValue("This is a long title that overflows the column"), "This is a long title that overflows the column", null, StyleId.Default, null),
            new DisplayCell(1, 2, null, "", null, StyleId.Default, null),
            new DisplayCell(1, 3, null, "", null, StyleId.Default, null),
        };
        var merge = new GridRange(new CellAddress(sheetId, 1, 2), new CellAddress(sheetId, 1, 3));

        GridRange? FindMerge(uint row, uint col) =>
            row == 1 && col is 2 or 3 ? merge : null;

        var occupied = GridView.BuildOccupiedCellSet(cells, editingCell: null, FindMerge);

        occupied.Should().Contain((1u, 2u), "B1 belongs to a merged region and must block overflow like an occupied cell");
        occupied.Should().Contain((1u, 3u), "C1 belongs to a merged region and must block overflow like an occupied cell");
    }

    [Fact]
    public void BuildOccupiedCellSet_NoRegression_UnmergedBlankNeighborStaysUnoccupied()
    {
        // Sibling/no-regression case: without merge data (or when a cell isn't part of any merge),
        // BuildOccupiedCellSet must keep its pre-existing behavior of not marking blank cells occupied.
        var cells = new[]
        {
            new DisplayCell(5, 1, new TextValue("overflowing text"), "overflowing text", null, StyleId.Default, null),
            new DisplayCell(5, 2, null, "", null, StyleId.Default, null),
        };

        var occupied = GridView.BuildOccupiedCellSet(cells, editingCell: null);

        occupied.Should().Contain((5u, 1u));
        occupied.Should().NotContain((5u, 2u));
    }
}
