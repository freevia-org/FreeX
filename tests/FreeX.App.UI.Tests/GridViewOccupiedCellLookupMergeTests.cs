using System.Reflection;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R53-render-cell-text-overflow-3-1: the real render call site (GridView.Rendering.cs's
/// GetOccupiedCellLookup, used by the main-grid overflow scan) must pass its merge-lookup callback
/// into BuildOccupiedCellSet so a blank merged cell blocks overflow just like Excel does -- a
/// merged range (even blank) always stops overflow text from sliding across it. Earlier tests
/// (GridViewMergedOverflowTests) only exercised BuildOccupiedCellSet directly with a manually
/// supplied findMerge lambda; they never went through this actual call site, which previously
/// omitted the findMerge argument entirely (defaulting to null and never marking blank merged
/// cells as occupied).
/// </summary>
public sealed class GridViewOccupiedCellLookupMergeTests
{
    private static HashSet<(uint Row, uint Col)> InvokeGetOccupiedCellLookup(
        GridView grid, ViewportModel viewport, CellAddress? editingCell)
    {
        return (HashSet<(uint Row, uint Col)>)grid.GetOccupiedCellLookup(viewport, editingCell)!;
    }

    private static void SetMergeLookup(GridView grid, Dictionary<(uint Row, uint Col), GridRange> mergeLookup)
    {
        var field = typeof(GridView).GetField("_mergeLookup", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(grid, mergeLookup);
    }

    [Fact]
    public void GetOccupiedCellLookup_MarksBlankMergedCellsAsOccupied()
    {
        // A1: long overflowing text, not merged. B1:C1: a separate, blank merged region -- Excel
        // stops A1's overflow dead at B1's left edge instead of sliding across the blank merge.
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var merge = new GridRange(new CellAddress(sheetId, 1, 2), new CellAddress(sheetId, 1, 3));
            var grid = new GridView();
            SetMergeLookup(grid, new Dictionary<(uint Row, uint Col), GridRange>
            {
                [(1u, 2u)] = merge,
                [(1u, 3u)] = merge,
            });

            var viewport = new ViewportModel(
                [
                    new DisplayCell(1, 1, new TextValue("This is a long title that overflows the column"), "This is a long title that overflows the column", null, StyleId.Default, null),
                    new DisplayCell(1, 2, null, "", null, StyleId.Default, null),
                    new DisplayCell(1, 3, null, "", null, StyleId.Default, null),
                ],
                [new RowMetric(1, 20, 0)],
                [
                    new ColMetric(1, 64, 0),
                    new ColMetric(2, 64, 64),
                    new ColMetric(3, 64, 128),
                ]);

            var occupied = InvokeGetOccupiedCellLookup(grid, viewport, editingCell: null);

            occupied.Should().Contain((1u, 2u), "B1 belongs to a merged region and must block overflow like an occupied cell");
            occupied.Should().Contain((1u, 3u), "C1 belongs to a merged region and must block overflow like an occupied cell");
        });
    }

    [Fact]
    public void GetOccupiedCellLookup_LeavesGenuinelyBlankUnmergedCellsUnoccupied_NoRegression()
    {
        // Sibling/no-regression: without any merge data, an ordinary blank neighbor must remain
        // unoccupied so overflow text can still spill across genuinely blank cells as before.
        WpfTestThread.Run(() =>
        {
            var grid = new GridView();
            SetMergeLookup(grid, []);

            var viewport = new ViewportModel(
                [
                    new DisplayCell(5, 1, new TextValue("overflowing text"), "overflowing text", null, StyleId.Default, null),
                    new DisplayCell(5, 2, null, "", null, StyleId.Default, null),
                ],
                [new RowMetric(5, 20, 0)],
                [
                    new ColMetric(1, 64, 0),
                    new ColMetric(2, 64, 64),
                ]);

            var occupied = InvokeGetOccupiedCellLookup(grid, viewport, editingCell: null);

            occupied.Should().Contain((5u, 1u));
            occupied.Should().NotContain((5u, 2u));
        });
    }
}
