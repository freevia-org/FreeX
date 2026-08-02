using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R118 (grouped-sheet parity for R92-render-cellstyle-inheritance-5-3): Excel's row-vs-column
/// format precedence at an intersection is fixed (cell xf > row style > column style > sheet
/// default) regardless of which order the two format operations were applied in, and regardless of
/// whether the sheets are grouped. Format whole row 5 Blue, then whole column C Yellow, across two
/// GROUPED sheets (the real command a row-header/column-header "apply fill" click goes through when
/// tabs are Ctrl-selected) -- the still-blank intersection cell C5 must show Blue (the row's format)
/// on EVERY grouped sheet, not Yellow (the column's, applied more recently). Before this fix,
/// GroupedApplyStyleCommand never classified or tagged style-only entries with StyleOnlySource at
/// all, so the column format silently won at the intersection on every grouped sheet.
/// </summary>
public sealed class R118_GroupedApplyStyleCommandRowColumnPrecedenceTests
{
    private static (Workbook wb, Sheet sheet1, Sheet sheet2, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        // Establish a used range spanning rows 1-10 / cols 1-10 on both sheets so the style-only
        // create zone (which clamps to the used range for unbounded row/column selections) covers
        // the row 5 / column 3 intersection cell under test on each sheet.
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(0));
        sheet1.SetCell(new CellAddress(sheet1.Id, 10, 10), new NumberValue(0));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(0));
        sheet2.SetCell(new CellAddress(sheet2.Id, 10, 10), new NumberValue(0));
        return (wb, sheet1, sheet2, new TestCommandContext(wb));
    }

    private static GridRange WholeRow(SheetId sheetId, uint row) =>
        new(new CellAddress(sheetId, row, 1), new CellAddress(sheetId, row, CellAddress.MaxCol));

    private static GridRange WholeColumn(SheetId sheetId, uint col) =>
        new(new CellAddress(sheetId, 1, col), new CellAddress(sheetId, CellAddress.MaxRow, col));

    [Fact]
    public void RowFormatAppliedFirst_ThenColumnFormat_RowFillWinsAtIntersectionOnEveryGroupedSheet()
    {
        var (wb, sheet1, sheet2, ctx) = Setup();
        var blue = new CellColor(0, 0, 255);
        var yellow = new CellColor(255, 255, 0);
        var sheetIds = new[] { sheet1.Id, sheet2.Id };

        new GroupedApplyStyleCommand(sheetIds, WholeRow(sheet1.Id, 5), new StyleDiff(FillColor: blue))
            .Apply(ctx).Success.Should().BeTrue();
        new GroupedApplyStyleCommand(sheetIds, WholeColumn(sheet1.Id, 3), new StyleDiff(FillColor: yellow))
            .Apply(ctx).Success.Should().BeTrue();

        foreach (var sheet in new[] { sheet1, sheet2 })
        {
            var style = wb.GetStyle(sheet.GetStyleOnly(5, 3)!.Value);
            style.FillColor.Should().Be(blue,
                $"Excel's row-beats-column precedence means the row's fill still wins at the intersection on {sheet.Name}, even though the column format was applied more recently, and grouping must not change that");
        }
    }

    [Fact]
    public void ColumnFormatAppliedFirst_ThenRowFormat_RowFillStillWinsAtIntersectionOnEveryGroupedSheet()
    {
        // No-regression / order-independence sibling: applying the two operations in the OPPOSITE
        // order must produce the SAME result -- row beats column regardless of apply order.
        var (wb, sheet1, sheet2, ctx) = Setup();
        var blue = new CellColor(0, 0, 255);
        var yellow = new CellColor(255, 255, 0);
        var sheetIds = new[] { sheet1.Id, sheet2.Id };

        new GroupedApplyStyleCommand(sheetIds, WholeColumn(sheet1.Id, 3), new StyleDiff(FillColor: yellow))
            .Apply(ctx).Success.Should().BeTrue();
        new GroupedApplyStyleCommand(sheetIds, WholeRow(sheet1.Id, 5), new StyleDiff(FillColor: blue))
            .Apply(ctx).Success.Should().BeTrue();

        foreach (var sheet in new[] { sheet1, sheet2 })
        {
            var style = wb.GetStyle(sheet.GetStyleOnly(5, 3)!.Value);
            style.FillColor.Should().Be(blue,
                $"row format must win at the intersection on {sheet.Name} regardless of application order");
        }
    }

    [Fact]
    public void RowFormatThenColumnFormat_NonIntersectionCellsStillGetTheirOwnFormatOnGroupedSheets()
    {
        // No-regression sibling: the precedence fix must only suppress the column op AT the
        // intersection -- every other blank cell in the row (still Blue) and every other blank
        // cell in the column (still Yellow) must format normally, on every grouped sheet.
        var (wb, sheet1, sheet2, ctx) = Setup();
        var blue = new CellColor(0, 0, 255);
        var yellow = new CellColor(255, 255, 0);
        var sheetIds = new[] { sheet1.Id, sheet2.Id };

        new GroupedApplyStyleCommand(sheetIds, WholeRow(sheet1.Id, 5), new StyleDiff(FillColor: blue))
            .Apply(ctx).Success.Should().BeTrue();
        new GroupedApplyStyleCommand(sheetIds, WholeColumn(sheet1.Id, 3), new StyleDiff(FillColor: yellow))
            .Apply(ctx).Success.Should().BeTrue();

        foreach (var sheet in new[] { sheet1, sheet2 })
        {
            // Row 5, column 7 (not column 3): only the row format applies -- Blue.
            wb.GetStyle(sheet.GetStyleOnly(5, 7)!.Value).FillColor.Should().Be(blue);

            // Row 8 (not row 5), column 3: only the column format applies -- Yellow.
            wb.GetStyle(sheet.GetStyleOnly(8, 3)!.Value).FillColor.Should().Be(yellow);
        }
    }

    [Fact]
    public void RowFormatThenColumnFormat_UndoColumnFormat_RestoresRowFillAndProvenanceOnGroupedSheets()
    {
        // No-regression sibling: undoing the column-format command must restore the intersection
        // cell exactly to its pre-column-op (row-only) state, including its row provenance -- a
        // THIRD format command afterward must still see it as row-sourced, on every grouped sheet.
        var (wb, sheet1, sheet2, ctx) = Setup();
        var blue = new CellColor(0, 0, 255);
        var yellow = new CellColor(255, 255, 0);
        var green = new CellColor(0, 255, 0);
        var sheetIds = new[] { sheet1.Id, sheet2.Id };

        new GroupedApplyStyleCommand(sheetIds, WholeRow(sheet1.Id, 5), new StyleDiff(FillColor: blue))
            .Apply(ctx).Success.Should().BeTrue();
        var colCmd = new GroupedApplyStyleCommand(sheetIds, WholeColumn(sheet1.Id, 3), new StyleDiff(FillColor: yellow));
        colCmd.Apply(ctx).Success.Should().BeTrue();
        colCmd.Revert(ctx);

        foreach (var sheet in new[] { sheet1, sheet2 })
        {
            wb.GetStyle(sheet.GetStyleOnly(5, 3)!.Value).FillColor.Should().Be(blue,
                $"undoing the column format must restore the row's fill at the intersection on {sheet.Name}");
        }

        // A later column format must still be suppressed here -- provenance must have survived undo.
        new GroupedApplyStyleCommand(sheetIds, WholeColumn(sheet1.Id, 3), new StyleDiff(FillColor: green))
            .Apply(ctx).Success.Should().BeTrue();

        foreach (var sheet in new[] { sheet1, sheet2 })
        {
            wb.GetStyle(sheet.GetStyleOnly(5, 3)!.Value).FillColor.Should().Be(blue,
                $"row provenance must survive undo of the column command that was reverted, on {sheet.Name}");
        }
    }

    [Fact]
    public void TwoRowFormatsOnSameRow_StillMergeNormallyOnGroupedSheets()
    {
        // No-regression sibling: same-axis re-application (two row-wide format ops on the same
        // row) is not a row-vs-column conflict and must keep merging as before -- Bold from the
        // first pass must survive the second pass's Italic, on every grouped sheet.
        var (wb, sheet1, sheet2, ctx) = Setup();
        var sheetIds = new[] { sheet1.Id, sheet2.Id };

        new GroupedApplyStyleCommand(sheetIds, WholeRow(sheet1.Id, 5), new StyleDiff(Bold: true))
            .Apply(ctx).Success.Should().BeTrue();
        new GroupedApplyStyleCommand(sheetIds, WholeRow(sheet1.Id, 5), new StyleDiff(Italic: true))
            .Apply(ctx).Success.Should().BeTrue();

        foreach (var sheet in new[] { sheet1, sheet2 })
        {
            var style = wb.GetStyle(sheet.GetStyleOnly(5, 3)!.Value);
            style.Bold.Should().BeTrue("same-axis (row-then-row) format ops must still merge, not replace");
            style.Italic.Should().BeTrue();
        }
    }
}
