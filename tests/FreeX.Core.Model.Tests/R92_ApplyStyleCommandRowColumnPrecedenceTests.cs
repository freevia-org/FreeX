using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R92-render-cellstyle-inheritance-5-3: Excel's row-vs-column format precedence at an
/// intersection is fixed (cell xf > row style > column style > sheet default) regardless of which
/// order the two format operations were applied in. Format whole row 5 Blue, then whole column C
/// Yellow (both via ApplyStyleCommand, the real command every row-header/column-header "apply
/// fill" action goes through) -- the still-blank intersection cell C5 must show Blue (the row's
/// format), not Yellow (the column's, applied more recently). Before this fix, ApplyStyleCommand
/// had no notion of row-vs-column provenance and always let the most-recently-applied diff win.
/// </summary>
public sealed class R92_ApplyStyleCommandRowColumnPrecedenceTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        // Establish a used range spanning rows 1-10 / cols 1-10 so the style-only create zone
        // (which clamps to the used range for unbounded row/column selections) covers the row
        // 5 / column 3 intersection cell under test.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(0));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 10), new NumberValue(0));
        return (wb, sheet, new TestCommandContext(wb));
    }

    private static GridRange WholeRow(SheetId sheetId, uint row) =>
        new(new CellAddress(sheetId, row, 1), new CellAddress(sheetId, row, CellAddress.MaxCol));

    private static GridRange WholeColumn(SheetId sheetId, uint col) =>
        new(new CellAddress(sheetId, 1, col), new CellAddress(sheetId, CellAddress.MaxRow, col));

    [Fact]
    public void RowFormatAppliedFirst_ThenColumnFormat_RowFillWinsAtIntersection()
    {
        var (wb, sheet, ctx) = Setup();
        var blue = new CellColor(0, 0, 255);
        var yellow = new CellColor(255, 255, 0);

        new ApplyStyleCommand(sheet.Id, WholeRow(sheet.Id, 5), new StyleDiff(FillColor: blue)).Apply(ctx).Success.Should().BeTrue();
        new ApplyStyleCommand(sheet.Id, WholeColumn(sheet.Id, 3), new StyleDiff(FillColor: yellow)).Apply(ctx).Success.Should().BeTrue();

        var intersection = new CellAddress(sheet.Id, 5, 3);
        var style = wb.GetStyle(sheet.GetStyleOnly(intersection.Row, intersection.Col)!.Value);
        style.FillColor.Should().Be(blue, "Excel's row-beats-column precedence means the row's fill still wins at the intersection, even though the column format was applied more recently");
    }

    [Fact]
    public void ColumnFormatAppliedFirst_ThenRowFormat_RowFillStillWinsAtIntersection()
    {
        // No-regression / order-independence sibling: applying the two operations in the OPPOSITE
        // order must produce the SAME result -- row beats column regardless of apply order. Before
        // this fix, this direction happened to already look "correct" by coincidence of being the
        // most-recently-applied write, not because row genuinely outranked column.
        var (wb, sheet, ctx) = Setup();
        var blue = new CellColor(0, 0, 255);
        var yellow = new CellColor(255, 255, 0);

        new ApplyStyleCommand(sheet.Id, WholeColumn(sheet.Id, 3), new StyleDiff(FillColor: yellow)).Apply(ctx).Success.Should().BeTrue();
        new ApplyStyleCommand(sheet.Id, WholeRow(sheet.Id, 5), new StyleDiff(FillColor: blue)).Apply(ctx).Success.Should().BeTrue();

        var intersection = new CellAddress(sheet.Id, 5, 3);
        var style = wb.GetStyle(sheet.GetStyleOnly(intersection.Row, intersection.Col)!.Value);
        style.FillColor.Should().Be(blue, "row format must win at the intersection regardless of application order");
    }

    [Fact]
    public void RowFormatThenColumnFormat_NonIntersectionCellsStillGetTheirOwnFormat()
    {
        // No-regression sibling: the precedence fix must only suppress the column op AT the
        // intersection -- every other blank cell in the row (still Blue) and every other blank
        // cell in the column (still Yellow, since they're not in row 5) must format normally.
        var (wb, sheet, ctx) = Setup();
        var blue = new CellColor(0, 0, 255);
        var yellow = new CellColor(255, 255, 0);

        new ApplyStyleCommand(sheet.Id, WholeRow(sheet.Id, 5), new StyleDiff(FillColor: blue)).Apply(ctx).Success.Should().BeTrue();
        new ApplyStyleCommand(sheet.Id, WholeColumn(sheet.Id, 3), new StyleDiff(FillColor: yellow)).Apply(ctx).Success.Should().BeTrue();

        // Row 5, column 7 (not column 3): only the row format applies -- Blue.
        var rowOnlyCell = new CellAddress(sheet.Id, 5, 7);
        wb.GetStyle(sheet.GetStyleOnly(rowOnlyCell.Row, rowOnlyCell.Col)!.Value).FillColor.Should().Be(blue);

        // Row 8 (not row 5), column 3: only the column format applies -- Yellow.
        var colOnlyCell = new CellAddress(sheet.Id, 8, 3);
        wb.GetStyle(sheet.GetStyleOnly(colOnlyCell.Row, colOnlyCell.Col)!.Value).FillColor.Should().Be(yellow);
    }

    [Fact]
    public void TwoRowFormatsOnSameRow_StillMergeNormally()
    {
        // No-regression sibling: same-axis re-application (two row-wide format ops on the same
        // row) is not a row-vs-column conflict and must keep merging as before -- Bold from the
        // first pass must survive the second pass's Italic.
        var (wb, sheet, ctx) = Setup();

        new ApplyStyleCommand(sheet.Id, WholeRow(sheet.Id, 5), new StyleDiff(Bold: true)).Apply(ctx).Success.Should().BeTrue();
        new ApplyStyleCommand(sheet.Id, WholeRow(sheet.Id, 5), new StyleDiff(Italic: true)).Apply(ctx).Success.Should().BeTrue();

        var addr = new CellAddress(sheet.Id, 5, 3);
        var style = wb.GetStyle(sheet.GetStyleOnly(addr.Row, addr.Col)!.Value);
        style.Bold.Should().BeTrue("same-axis (row-then-row) format ops must still merge, not replace");
        style.Italic.Should().BeTrue();
    }

    [Fact]
    public void BoundedRangeFormat_UnaffectedByRowColumnPrecedence()
    {
        // No-regression sibling: a bounded cell-range selection (not a whole-row/whole-column
        // header selection) is neither row- nor column-sourced and must keep its pre-existing
        // plain merge-on-top behavior regardless of any row/column tags already present.
        var (wb, sheet, ctx) = Setup();
        var blue = new CellColor(0, 0, 255);
        var green = new CellColor(0, 255, 0);

        new ApplyStyleCommand(sheet.Id, WholeRow(sheet.Id, 5), new StyleDiff(FillColor: blue)).Apply(ctx).Success.Should().BeTrue();

        var addr = new CellAddress(sheet.Id, 5, 3);
        var boundedRange = new GridRange(addr, addr);
        new ApplyStyleCommand(sheet.Id, boundedRange, new StyleDiff(FillColor: green)).Apply(ctx).Success.Should().BeTrue();

        var style = wb.GetStyle(sheet.GetStyleOnly(addr.Row, addr.Col)!.Value);
        style.FillColor.Should().Be(green, "a direct bounded-selection format always overrides, matching cell-xf precedence above both row and column");
    }

    [Fact]
    public void RowFormatThenColumnFormat_UndoColumnFormat_RestoresRowFillAndProvenance()
    {
        // No-regression sibling: undoing the column-format command must restore the intersection
        // cell exactly to its pre-column-op (row-only) state, including its row provenance -- a
        // THIRD format command afterward must still see it as row-sourced.
        var (wb, sheet, ctx) = Setup();
        var blue = new CellColor(0, 0, 255);
        var yellow = new CellColor(255, 255, 0);
        var green = new CellColor(0, 255, 0);

        new ApplyStyleCommand(sheet.Id, WholeRow(sheet.Id, 5), new StyleDiff(FillColor: blue)).Apply(ctx).Success.Should().BeTrue();
        var colCmd = new ApplyStyleCommand(sheet.Id, WholeColumn(sheet.Id, 3), new StyleDiff(FillColor: yellow));
        colCmd.Apply(ctx).Success.Should().BeTrue();
        colCmd.Revert(ctx);

        var addr = new CellAddress(sheet.Id, 5, 3);
        wb.GetStyle(sheet.GetStyleOnly(addr.Row, addr.Col)!.Value).FillColor.Should().Be(blue,
            "undoing the column format must restore the row's fill at the intersection");

        // A later column format must still be suppressed here -- provenance must have survived undo.
        new ApplyStyleCommand(sheet.Id, WholeColumn(sheet.Id, 3), new StyleDiff(FillColor: green)).Apply(ctx).Success.Should().BeTrue();
        wb.GetStyle(sheet.GetStyleOnly(addr.Row, addr.Col)!.Value).FillColor.Should().Be(blue,
            "row provenance must survive undo of the column command that was reverted");
    }
}
