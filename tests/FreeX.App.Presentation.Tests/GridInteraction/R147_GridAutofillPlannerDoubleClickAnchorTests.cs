using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.GridInteraction;

/// <summary>
/// Regression coverage for round-147 finding autofill-series F2: for a multi-row fill-handle
/// seed, <see cref="GridAutofillPlanner.ResolveAdjacentColumnLastPopulatedRow"/> used to anchor
/// its blank/non-blank probe on the source block's TOP row (source.Start.Row), while the fill
/// itself (<see cref="GridAutofillPlanner.CalculateDoubleClickFillRange"/>) anchors on the
/// block's BOTTOM row (source.End.Row). If the adjacent column happened to be blank beside the
/// block's first row -- even though it was solidly populated beside the rest of the block and
/// below it -- the probe returned null and the whole double-click silently no-op'd.
/// </summary>
public sealed class R147_GridAutofillPlannerDoubleClickAnchorTests
{
    [Fact]
    public void ResolveAdjacentColumnLastPopulatedRow_MultiRowSource_IgnoresGapBesideBlockTopRow()
    {
        // Source is a 3-row block, B2:B4. Column A is blank beside row 3 (the block's own top
        // row) but solidly populated beside rows 4-12 (the block's bottom row and below).
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var source = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 4, 2));
        PopulateColumn(sheet, column: 1, firstRow: 4, lastRow: 12);

        GridAutofillPlanner.ResolveAdjacentColumnLastPopulatedRow(sheet, source)
            .Should()
            .Be(12, "the probe must anchor on the block's bottom row (source.End.Row), matching " +
                "the anchor CalculateDoubleClickFillRange itself uses, not the block's top row");
    }

    [Fact]
    public void CalculateDoubleClickFillRange_EndToEnd_FillsPastGapBesideBlockTopRow()
    {
        // End-to-end: resolve against the sheet, then feed straight into the fill-range
        // calculation, mirroring MainWindow.xaml.cs's OnAutofillHandleDoubleClicked.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var source = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 4, 2));
        PopulateColumn(sheet, column: 1, firstRow: 4, lastRow: 12);

        var adjacentLastRow = GridAutofillPlanner.ResolveAdjacentColumnLastPopulatedRow(sheet, source);
        var fillRange = GridAutofillPlanner.CalculateDoubleClickFillRange(source, adjacentLastRow);

        fillRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 5, 2),
            new CellAddress(sheet.Id, 12, 2)));
    }

    // ── No-regression sibling: a genuine gap immediately below the block's bottom row (not ──
    // just beside its top row) must still correctly report "no fill" / stop exactly there. ──

    [Fact]
    public void ResolveAdjacentColumnLastPopulatedRow_MultiRowSource_StopsAtGapBelowBlockBottomRow()
    {
        // Column A is populated beside the block's top row (row 2) but blank immediately below
        // the block's bottom row (row 5) -- the row the fill would actually anchor on. There is
        // genuinely nothing to extend into, so this must resolve to null, not to row 2's data.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var source = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 4, 2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(20));

        GridAutofillPlanner.ResolveAdjacentColumnLastPopulatedRow(sheet, source)
            .Should()
            .BeNull();

        GridAutofillPlanner.CalculateDoubleClickFillRange(source, adjacentColumnLastPopulatedRow: null)
            .Should()
            .BeNull();
    }

    [Fact]
    public void ResolveAdjacentColumnLastPopulatedRow_SingleRowSource_UnaffectedByAnchorChange()
    {
        // Single-row source: Start.Row == End.Row, so the anchor fix is a no-op here. Keeps the
        // pre-existing single-cell-source behavior pinned.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var source = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 2, 2));
        PopulateColumn(sheet, column: 1, firstRow: 3, lastRow: 7);

        GridAutofillPlanner.ResolveAdjacentColumnLastPopulatedRow(sheet, source)
            .Should()
            .Be(7);
    }

    private static void PopulateColumn(Sheet sheet, uint column, uint firstRow, uint lastRow)
    {
        for (var row = firstRow; row <= lastRow; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, column), new NumberValue(row));
    }
}
