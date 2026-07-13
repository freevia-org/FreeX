using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.GridInteraction;

/// <summary>
/// Regression coverage for round-40 finding R40-commands-autofill-flashfill-3-3: double-clicking
/// the fill handle used to be a no-op for any source selection taller than one row. Excel supports
/// double-click autofill for any rectangular selection, continuing the fill immediately below the
/// selection down to the adjacent column's populated extent.
/// </summary>
public sealed class R40_GridAutofillPlannerDoubleClickMultiRowTests
{
    [Fact]
    public void CalculateDoubleClickFillRange_MultiRowSource_FillsBelowSelectionToAdjacentExtent()
    {
        // B2:B3 hold a learned 2-row pattern; column A is populated through row 10.
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 3, 2));

        var result = GridAutofillPlanner.CalculateDoubleClickFillRange(source, adjacentColumnLastPopulatedRow: 10);

        result.Should().Be(new GridRange(
            new CellAddress(sheet, 4, 2),
            new CellAddress(sheet, 10, 2)));
    }

    [Fact]
    public void CalculateDoubleClickFillRange_MultiRowMultiColumnSource_SpansAllSelectedColumns()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 1, 1),
            new CellAddress(sheet, 2, 3));

        var result = GridAutofillPlanner.CalculateDoubleClickFillRange(source, adjacentColumnLastPopulatedRow: 5);

        result.Should().Be(new GridRange(
            new CellAddress(sheet, 3, 1),
            new CellAddress(sheet, 5, 3)));
    }

    // ── No-regression sibling: single-row source keeps its original behavior. ──────────────

    [Fact]
    public void CalculateDoubleClickFillRange_SingleRowSource_StillFillsBelowSeedRow()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 2, 2),
            new CellAddress(sheet, 2, 2));

        var result = GridAutofillPlanner.CalculateDoubleClickFillRange(source, adjacentColumnLastPopulatedRow: 6);

        result.Should().Be(new GridRange(
            new CellAddress(sheet, 3, 2),
            new CellAddress(sheet, 6, 2)));
    }

    [Fact]
    public void CalculateDoubleClickFillRange_MultiRowSource_ReturnsNullWhenNoAdjacentExtraData()
    {
        var sheet = SheetId.New();
        var source = new GridRange(
            new CellAddress(sheet, 4, 1),
            new CellAddress(sheet, 5, 1));

        GridAutofillPlanner.CalculateDoubleClickFillRange(source, adjacentColumnLastPopulatedRow: null)
            .Should()
            .BeNull();

        // Adjacent data only reaches the selection's own last row -- nothing new to fill.
        GridAutofillPlanner.CalculateDoubleClickFillRange(source, adjacentColumnLastPopulatedRow: 5)
            .Should()
            .BeNull();
    }
}
