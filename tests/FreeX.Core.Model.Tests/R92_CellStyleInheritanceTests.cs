using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R92-render-cellstyle-inheritance-5-1/-2: (1) typing a value into a style-only
/// (formatted-but-blank) cell must adopt the pre-existing style-only entry instead of the edit
/// silently wiping it (EditCellsCommand/GroupedEditCellsCommand), matching the existing fallback
/// pattern in ClearContentsCommand; and (2) Insert Sheet Rows/Columns -- the primary insert path --
/// must inherit the neighboring row-above/column-left format for the newly-vacated band, mirroring
/// the vacated-band format inheritance InsertCellsCommand already implements (R71).
/// </summary>
public class R92_CellStyleInheritanceTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    // ── Fix 1: EditCellsCommand must adopt a pre-existing style-only entry ────────────────────

    [Fact]
    public void EditCellsCommand_TypingIntoStyleOnlyCell_AdoptsExistingStyleOnlyFormat()
    {
        var (wb, sheet, ctx) = Setup();
        var bold = wb.RegisterStyle(new CellStyle { Bold = true });
        var addr = new CellAddress(sheet.Id, 5, 3); // C5, previously formatted (column-wide Bold), blank
        sheet.SetStyleOnly(addr.Row, addr.Col, bold);

        var command = new EditCellsCommand(sheet.Id, addr, new TextValue("Hello"));
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetCell(addr)!.StyleId.Should().Be(bold, "typing into a style-only cell must inherit its existing format, matching Excel");
        sheet.GetStyleOnly(addr.Row, addr.Col).Should().BeNull("the style-only entry is now superseded by a real cell carrying the same style");

        command.Revert(ctx);

        sheet.GetCell(addr).Should().BeNull();
        sheet.GetStyleOnly(addr.Row, addr.Col).Should().Be(bold, "undo must restore the pre-existing style-only entry");
    }

    [Fact]
    public void EditCellsCommand_TypingIntoStyleOnlyCell_WithAutoInferredNumberFormat_DoesNotOverrideDetectedFormat()
    {
        // No-regression sibling: R87-formula-number-parse-locale-5-3's guard (typing "50%" gets its
        // own inferred number-format StyleId from CellEntryParser) must still win over an existing
        // style-only entry -- the fallback added by this fix must only fire when the new cell's
        // StyleId is still Default.
        var (wb, sheet, ctx) = Setup();
        var bold = wb.RegisterStyle(new CellStyle { Bold = true });
        var percent = wb.RegisterStyle(new CellStyle { NumberFormat = "0%" });
        var addr = new CellAddress(sheet.Id, 5, 3);
        sheet.SetStyleOnly(addr.Row, addr.Col, bold);

        var newCell = Cell.FromValue(new NumberValue(0.5));
        newCell.StyleId = percent; // simulates CellEntryParser's auto-inferred number format
        var command = new EditCellsCommand(sheet.Id, [(addr, newCell)]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(addr)!.StyleId.Should().Be(percent, "an auto-inferred number format must still win over the style-only fallback");
    }

    [Fact]
    public void EditCellsCommand_TypingIntoPlainBlankCell_NoStyleOnlyEntry_StaysDefault()
    {
        // No-regression sibling: an ordinary blank cell (no style-only entry, no prior content)
        // must still end up at StyleId.Default -- this fix must not invent formatting out of thin air.
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 5, 3);

        var command = new EditCellsCommand(sheet.Id, addr, new TextValue("Hello"));
        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(addr)!.StyleId.Should().Be(StyleId.Default);
    }

    [Fact]
    public void GroupedEditCellsCommand_TypingIntoStyleOnlyCell_AdoptsExistingStyleOnlyFormat()
    {
        var (wb, sheet, ctx) = Setup();
        var sheet2 = wb.AddSheet("Sheet2");
        var bold = wb.RegisterStyle(new CellStyle { Bold = true });
        var source = new CellAddress(sheet.Id, 5, 3);
        var groupedTarget = new CellAddress(sheet2.Id, 5, 3);
        sheet2.SetStyleOnly(groupedTarget.Row, groupedTarget.Col, bold);

        var command = new GroupedEditCellsCommand(
            [sheet.Id, sheet2.Id],
            sheet.Id,
            [(source, Cell.FromValue(new TextValue("Hello")))]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet2.GetCell(groupedTarget)!.StyleId.Should().Be(bold, "grouped-sheet edit into a style-only cell must inherit its existing format");
    }

    // ── Fix 2: whole-row/whole-column Insert must inherit neighbor format ─────────────────────

    [Fact]
    public void InsertRowsCommand_NewRowInheritsFormatFromRowAbove()
    {
        var (wb, sheet, ctx) = Setup();
        var boldRedFill = wb.RegisterStyle(new CellStyle { Bold = true, FillColor = new CellColor(255, 0, 0) });
        var row2Cell = new CellAddress(sheet.Id, 2, 3); // C2
        sheet.SetCell(row2Cell, new Cell { Value = new TextValue("R2"), StyleId = boldRedFill });

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        var newRow3Cell = new CellAddress(sheet.Id, 3, 3); // C3, brand new blank row
        sheet.GetCell(newRow3Cell).Should().BeNull();
        sheet.GetStyleOnly(newRow3Cell.Row, newRow3Cell.Col).Should().Be(boldRedFill,
            "Excel's Insert Sheet Rows default inherits the row-above's format into the new row");

        cmd.Revert(ctx);

        sheet.GetStyleOnly(newRow3Cell.Row, newRow3Cell.Col).Should().BeNull("undo must remove the inherited style-only entry");
        sheet.GetCell(row2Cell)!.StyleId.Should().Be(boldRedFill);
    }

    [Fact]
    public void InsertRowsCommand_AtRow1_NewRowLeavesDefaultFormat()
    {
        // No-regression sibling: no row above row 1 -- new row must stay default.
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new TextValue("R1")));

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        var newRow1Cell = new CellAddress(sheet.Id, 1, 3);
        sheet.GetStyleOnly(newRow1Cell.Row, newRow1Cell.Col).Should().BeNull("row 1 has no row above to inherit format from");
    }

    [Fact]
    public void InsertColumnsCommand_NewColumnInheritsFormatFromColumnLeft()
    {
        var (wb, sheet, ctx) = Setup();
        var italic = wb.RegisterStyle(new CellStyle { Italic = true });
        var colBCell = new CellAddress(sheet.Id, 4, 2); // B4
        sheet.SetCell(colBCell, new Cell { Value = new TextValue("B4"), StyleId = italic });

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 3);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        var newColCCell = new CellAddress(sheet.Id, 4, 3); // C4, brand new blank column
        sheet.GetCell(newColCCell).Should().BeNull();
        sheet.GetStyleOnly(newColCCell.Row, newColCCell.Col).Should().Be(italic,
            "Excel's Insert Sheet Columns default inherits the column-to-the-left's format into the new column");

        cmd.Revert(ctx);

        sheet.GetStyleOnly(newColCCell.Row, newColCCell.Col).Should().BeNull("undo must remove the inherited style-only entry");
        sheet.GetCell(colBCell)!.StyleId.Should().Be(italic);
    }

    [Fact]
    public void InsertColumnsCommand_AtColumnA_NewColumnLeavesDefaultFormat()
    {
        // No-regression sibling: no column to the left of A -- new column must stay default.
        var (_, sheet, ctx) = Setup();
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromValue(new TextValue("A4")));

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 1);
        cmd.Apply(ctx).Success.Should().BeTrue();

        var newColACell = new CellAddress(sheet.Id, 4, 1);
        sheet.GetStyleOnly(newColACell.Row, newColACell.Col).Should().BeNull("column A has no column to the left to inherit format from");
    }
}
