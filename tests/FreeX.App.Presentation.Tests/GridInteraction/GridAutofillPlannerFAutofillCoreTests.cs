using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.GridInteraction;

/// <summary>
/// Regression coverage for the F-autofill-core review findings that live in the shared planner:
///   J38 - CalculateClearRange for an inward fill-handle drag.
///   J54 - CalculateDoubleClickFillRange for a double-clicked fill handle.
/// </summary>
public sealed class GridAutofillPlannerFAutofillCoreTests
{
    // ---- J38: inward drag -> clear range -------------------------------------------------

    [Fact]
    public void CalculateClearRange_ReturnsTrailingRowsWhenDraggedUpwardInsideSource()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 1, 1),
            new CellAddress(sheet, 5, 1));

        GridAutofillPlanner.CalculateClearRange(source, new CellAddress(sheet, 3, 1))
            .Should()
            .Be(new GridRange(
                new CellAddress(sheet, 4, 1),
                new CellAddress(sheet, 5, 1)));
    }

    [Fact]
    public void CalculateClearRange_ReturnsTrailingColumnsWhenDraggedLeftwardInsideSource()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 1, 1),
            new CellAddress(sheet, 1, 4));

        GridAutofillPlanner.CalculateClearRange(source, new CellAddress(sheet, 1, 2))
            .Should()
            .Be(new GridRange(
                new CellAddress(sheet, 1, 3),
                new CellAddress(sheet, 1, 4)));
    }

    [Fact]
    public void CalculateClearRange_ReturnsNullWhenTargetSitsOnSourceEdgeWithNoMovement()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 1, 1),
            new CellAddress(sheet, 5, 1));

        // Target still at the original handle position (source.End): no shrink occurred.
        GridAutofillPlanner.CalculateClearRange(source, source.End)
            .Should()
            .BeNull();
    }

    [Fact]
    public void CalculateClearRange_ReturnsNullForMultiRowAndMultiColumnSource()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 1, 1),
            new CellAddress(sheet, 3, 3));

        // Neither a single-column nor a single-row source: no unambiguous shrink axis.
        GridAutofillPlanner.CalculateClearRange(source, new CellAddress(sheet, 2, 2))
            .Should()
            .BeNull();
    }

    [Fact]
    public void CalculateFillRangeThenCalculateClearRange_TogetherCoverEveryDragTarget()
    {
        // Mirrors the GridView.Input.cs mouse-up fallback: CalculateFillRange handles outward
        // drags, CalculateClearRange handles inward drags, and one of the two should always
        // resolve for any target that isn't a true no-op.
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 1),
            new CellAddress(sheet, 5, 1));

        // Outward (below source): handled by CalculateFillRange.
        GridAutofillPlanner.CalculateFillRange(source, new CellAddress(sheet, 7, 1))
            .Should()
            .NotBeNull();
        GridAutofillPlanner.CalculateClearRange(source, new CellAddress(sheet, 7, 1))
            .Should()
            .BeNull();

        // Inward (within source): handled by CalculateClearRange.
        GridAutofillPlanner.CalculateFillRange(source, new CellAddress(sheet, 4, 1))
            .Should()
            .BeNull();
        GridAutofillPlanner.CalculateClearRange(source, new CellAddress(sheet, 4, 1))
            .Should()
            .NotBeNull();
    }

    // ---- J54: double-click fill handle -> fill to adjacent column extent -----------------

    [Fact]
    public void CalculateDoubleClickFillRange_ExtendsDownToAdjacentColumnExtent()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 2, 2));

        GridAutofillPlanner.CalculateDoubleClickFillRange(source, adjacentColumnLastPopulatedRow: 6)
            .Should()
            .Be(new GridRange(
                new CellAddress(sheet, 3, 2),
                new CellAddress(sheet, 6, 2)));
    }

    [Fact]
    public void CalculateDoubleClickFillRange_SpansAllSelectedColumns()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 1, 1),
            new CellAddress(sheet, 1, 3));

        GridAutofillPlanner.CalculateDoubleClickFillRange(source, adjacentColumnLastPopulatedRow: 4)
            .Should()
            .Be(new GridRange(
                new CellAddress(sheet, 2, 1),
                new CellAddress(sheet, 4, 3)));
    }

    [Fact]
    public void CalculateDoubleClickFillRange_ReturnsNullWhenAdjacentColumnHasNoExtraData()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 4, 1),
            new CellAddress(sheet, 4, 1));

        GridAutofillPlanner.CalculateDoubleClickFillRange(source, adjacentColumnLastPopulatedRow: null)
            .Should()
            .BeNull();
        GridAutofillPlanner.CalculateDoubleClickFillRange(source, adjacentColumnLastPopulatedRow: 4)
            .Should()
            .BeNull();
    }

    [Fact]
    public void CalculateDoubleClickFillRange_FillsMultiRowSelectionToAdjacentExtent()
    {
        // Matches Excel: double-clicking the fill handle of a multi-row (pattern) selection
        // continues the fill immediately below the selection, down to the adjacent column's
        // populated extent -- it is not restricted to single-row selections.
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 1, 1),
            new CellAddress(sheet, 2, 1));

        GridAutofillPlanner.CalculateDoubleClickFillRange(source, adjacentColumnLastPopulatedRow: 10)
            .Should()
            .Be(new GridRange(
                new CellAddress(sheet, 3, 1),
                new CellAddress(sheet, 10, 1)));
    }

    [Fact]
    public void ResolveAdjacentColumnLastPopulatedRow_PrefersContiguousLeftNeighbor()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var source = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 2, 2));
        PopulateColumn(sheet, column: 1, firstRow: 3, lastRow: 5);
        PopulateColumn(sheet, column: 3, firstRow: 3, lastRow: 8);

        GridAutofillPlanner.ResolveAdjacentColumnLastPopulatedRow(sheet, source)
            .Should()
            .Be(5);
    }

    [Fact]
    public void ResolveAdjacentColumnLastPopulatedRow_FallsBackRightAndStopsAtFirstBlank()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var source = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 2, 2));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(40));
        PopulateColumn(sheet, column: 3, firstRow: 3, lastRow: 5);
        sheet.SetCell(new CellAddress(sheet.Id, 7, 3), new NumberValue(70));

        GridAutofillPlanner.ResolveAdjacentColumnLastPopulatedRow(sheet, source)
            .Should()
            .Be(5);
    }

    private static void PopulateColumn(Sheet sheet, uint column, uint firstRow, uint lastRow)
    {
        for (var row = firstRow; row <= lastRow; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, column), new NumberValue(row));
    }
}
