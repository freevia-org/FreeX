using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R71-commands-insert-delete-cells-4-1/-2: Insert Cells (Shift Right / Shift Down) must (1) reject
/// an insert that would push an annotation-only or style-only (formatted-but-empty) cell — one with
/// no Cell entry, so it is invisible to the existing value/merge-only overflow guard — past the last
/// column/row, mirroring the guard's existing behavior for value-bearing cells and merges; and (2)
/// have the newly-vacated blank band inherit the neighboring cell's format (left for Shift-Right,
/// above for Shift-Down), matching Excel's "Insert Options" smart-tag default instead of leaving the
/// new cells at General/default formatting.
/// </summary>
public class R71_insert_cells_annotation_overflow_and_format_inherit_Tests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    // ── Fix 1: annotation/style-only overflow guard ──────────────────────────────────────────

    [Fact]
    public void InsertCellsShiftRight_CommentAtLastColumn_RejectsInsteadOfOverflowing()
    {
        var (_, sheet, ctx) = Setup();
        var lastCol = new CellAddress(sheet.Id, 5, CellAddress.MaxCol);
        sheet.Comments[lastCol] = "note at last column";

        var range = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 1));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeFalse("a comment anchored at the last column would be pushed off the sheet");
        outcome.ErrorMessage.Should().Contain("last column");
        sheet.Comments.Should().ContainKey(lastCol);
        sheet.Comments[lastCol].Should().Be("note at last column");
        sheet.Comments.Should().NotContainKey(new CellAddress(sheet.Id, 5, CellAddress.MaxCol + 1),
            "a rejected insert must never relocate the comment past the last column");

        cmd.Revert(ctx);

        sheet.Comments.Should().ContainKey(lastCol);
        sheet.Comments[lastCol].Should().Be("note at last column");
    }

    [Fact]
    public void InsertCellsShiftRight_HyperlinkAtLastColumn_RejectsInsteadOfOverflowing()
    {
        var (_, sheet, ctx) = Setup();
        var lastCol = new CellAddress(sheet.Id, 5, CellAddress.MaxCol);
        sheet.Hyperlinks[lastCol] = "https://example.com";

        var range = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 1));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeFalse("a hyperlink anchored at the last column would be pushed off the sheet");
        sheet.Hyperlinks.Should().ContainKey(lastCol);
        sheet.Hyperlinks[lastCol].Should().Be("https://example.com");

        cmd.Revert(ctx);

        sheet.Hyperlinks.Should().ContainKey(lastCol);
    }

    [Fact]
    public void InsertCellsShiftRight_StyleOnlyCellAtLastColumn_RejectsInsteadOfOverflowing()
    {
        var (wb, sheet, ctx) = Setup();
        var styleId = wb.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(5, CellAddress.MaxCol, styleId);

        var range = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 5, 1));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeFalse("a style-only cell anchored at the last column would be pushed off the sheet");
        sheet.GetStyleOnly(5, CellAddress.MaxCol).Should().Be(styleId);

        cmd.Revert(ctx);

        sheet.GetStyleOnly(5, CellAddress.MaxCol).Should().Be(styleId);
    }

    [Fact]
    public void InsertCellsShiftDown_CommentAtLastRow_RejectsInsteadOfOverflowing()
    {
        // Mirrored last-row + Shift-Down case: comment at (MaxRow, col 5); band is col 5, row >= 1.
        var (_, sheet, ctx) = Setup();
        var lastRow = new CellAddress(sheet.Id, CellAddress.MaxRow, 5);
        sheet.Comments[lastRow] = "note at last row";

        var range = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 1, 5));
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeFalse("a comment anchored at the last row would be pushed off the sheet");
        outcome.ErrorMessage.Should().Contain("last row");
        sheet.Comments.Should().ContainKey(lastRow);
        sheet.Comments.Should().NotContainKey(new CellAddress(sheet.Id, CellAddress.MaxRow + 1, 5),
            "a rejected insert must never relocate the comment past the last row");

        cmd.Revert(ctx);

        sheet.Comments.Should().ContainKey(lastRow);
    }

    [Fact]
    public void InsertCellsShiftRight_NormalInsertWithCommentInBounds_StillSucceedsAndRelocatesComment()
    {
        // No-regression sibling: an in-bounds comment must still move as before; the new overflow
        // guard must not block a normal insert.
        var (_, sheet, ctx) = Setup();
        var commentAddr = new CellAddress(sheet.Id, 3, 2); // B3
        sheet.Comments[commentAddr] = "hello";

        var range = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 3, 1)); // A3
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);

        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue("an in-bounds insert must not be blocked by the new overflow guard");
        sheet.Comments.Should().NotContainKey(commentAddr);
        sheet.Comments[new CellAddress(sheet.Id, 3, 3)].Should().Be("hello");

        cmd.Revert(ctx);

        sheet.Comments.Should().ContainKey(commentAddr);
        sheet.Comments[commentAddr].Should().Be("hello");
    }

    // ── Fix 2: vacated-band format inheritance ────────────────────────────────────────────────

    [Fact]
    public void InsertCellsShiftRight_NewBlankCellInheritsLeftNeighborFormat()
    {
        var (wb, sheet, ctx) = Setup();
        var a3 = new CellAddress(sheet.Id, 3, 1);
        var styleId = wb.RegisterStyle(new CellStyle { Bold = true, FillColor = new CellColor(255, 0, 0) });
        sheet.SetCell(a3, new Cell { Value = new TextValue("A3"), StyleId = styleId });

        var b3 = new CellAddress(sheet.Id, 3, 2);
        var range = new GridRange(b3, b3);
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);

        cmd.Apply(ctx).Success.Should().BeTrue();

        // New B3 is blank but must inherit A3's format (Excel's Insert Options default).
        sheet.GetCell(b3.Row, b3.Col).Should().BeNull();
        sheet.GetStyleOnly(b3.Row, b3.Col).Should().Be(styleId);

        cmd.Revert(ctx);

        sheet.GetStyleOnly(b3.Row, b3.Col).Should().BeNull("undo must remove the inherited style-only entry");
        sheet.GetCell(a3.Row, a3.Col)!.StyleId.Should().Be(styleId);
    }

    [Fact]
    public void InsertCellsShiftRight_AtColumnA_NewBlankCellLeavesDefaultFormat()
    {
        // No-regression sibling: no column to the left of A -- new cell must stay default.
        var (_, sheet, ctx) = Setup();
        var a3 = new CellAddress(sheet.Id, 3, 1);
        var range = new GridRange(a3, a3);
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Right);

        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(a3.Row, a3.Col).Should().BeNull();
        sheet.GetStyleOnly(a3.Row, a3.Col).Should().BeNull("column A has no left neighbor to inherit format from");

        cmd.Revert(ctx);

        sheet.GetStyleOnly(a3.Row, a3.Col).Should().BeNull();
    }

    [Fact]
    public void InsertCellsShiftDown_NewBlankCellInheritsAboveNeighborFormat()
    {
        var (wb, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var styleId = wb.RegisterStyle(new CellStyle { Italic = true });
        sheet.SetCell(a1, new Cell { Value = new TextValue("A1"), StyleId = styleId });

        var a2 = new CellAddress(sheet.Id, 2, 1);
        var range = new GridRange(a2, a2);
        var cmd = new InsertCellsCommand(sheet.Id, range, InsertCellsShiftDirection.Down);

        cmd.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(a2.Row, a2.Col).Should().BeNull();
        sheet.GetStyleOnly(a2.Row, a2.Col).Should().Be(styleId);

        cmd.Revert(ctx);

        sheet.GetStyleOnly(a2.Row, a2.Col).Should().BeNull("undo must remove the inherited style-only entry");
        sheet.GetCell(a1.Row, a1.Col)!.StyleId.Should().Be(styleId);
    }
}
