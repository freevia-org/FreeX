using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for finding R84-calc-crosssheet-3d-5-1: Undo/Redo of a structural sheet
/// command (Add/Delete/Move/Duplicate Sheet) never recalculated 3-D span aggregates (e.g.
/// =SUM(Sheet1:Sheet3!A1)), because <see cref="ICommandBus.Undo"/>/<see cref="ICommandBus.Redo"/>
/// call straight into the command bus and bypass the <c>WorkbookSession</c> wrapper methods
/// (DeleteActiveSheet/MoveActiveSheetTo/DuplicateActiveSheet) that are the only place the explicit
/// post-op <c>RecalculateWorkbook()</c> compensation lived. RemoveSheetCommand/MoveSheetCommand/
/// DuplicateSheetCommand/AddSheetCommand report no <see cref="IAffectedCellsCommand.AffectedCells"/>
/// of their own, so <c>RecalcEngine.Recalculate</c> short-circuited to an empty report and left the
/// span formula showing its stale pre-undo/redo value.
/// </summary>
public sealed class R84_SheetOpUndoRedoThreeDSpanRecalcTests
{
    [Fact]
    public void UndoLastEdit_AfterDeleteActiveSheet_RecalculatesThreeDSpanFormulaBackToTheRestoredSum()
    {
        var workbook = new Workbook("Book1");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var sheet3 = workbook.AddSheet("Sheet3");
        var sheet4 = workbook.AddSheet("Sheet4");
        workbook.ActiveSheetIndex = 0;

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(2));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(3));
        sheet4.SetFormula(new CellAddress(sheet4.Id, 1, 1), "SUM(Sheet1:Sheet3!A1)");

        var session = CreateSession(workbook);
        session.RecalculateWorkbook();
        sheet4.GetValue(1, 1).Should().Be(new NumberValue(6));

        // Delete Sheet2 (right-click > Delete): the forward path's own explicit
        // RecalculateWorkbook() call contracts the span to 1 + 3 = 4.
        session.SelectSheet(sheet2.Id);
        var deleteResult = session.DeleteActiveSheet();
        deleteResult.Success.Should().BeTrue();
        sheet4.GetValue(1, 1).Should().Be(new NumberValue(4));

        // Ctrl+Z: Sheet2 reappears (with its original A1 = 2) via RemoveSheetCommand.Revert, but
        // Undo calls straight into the command bus -- without this fix, Sheet4!A1 would keep
        // showing the stale post-delete 4 instead of recomputing to 1 + 2 + 3 = 6.
        var undoResult = session.UndoLastEdit();

        undoResult.Success.Should().BeTrue();
        workbook.Sheets.Select(s => s.Name).Should().Equal("Sheet1", "Sheet2", "Sheet3", "Sheet4");
        sheet4.GetValue(1, 1).Should().Be(
            new NumberValue(6),
            "undoing the Delete Sheet must recompute the 3-D span SUM now that Sheet2 is back, not keep showing the stale post-delete total");
    }

    [Fact]
    public void RedoLastEdit_AfterUndoOfDeleteActiveSheet_RecalculatesThreeDSpanFormulaBackToTheContractedSum()
    {
        var workbook = new Workbook("Book1");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var sheet3 = workbook.AddSheet("Sheet3");
        var sheet4 = workbook.AddSheet("Sheet4");
        workbook.ActiveSheetIndex = 0;

        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(2));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(3));
        sheet4.SetFormula(new CellAddress(sheet4.Id, 1, 1), "SUM(Sheet1:Sheet3!A1)");

        var session = CreateSession(workbook);
        session.RecalculateWorkbook();

        session.SelectSheet(sheet2.Id);
        session.DeleteActiveSheet().Success.Should().BeTrue();
        sheet4.GetValue(1, 1).Should().Be(new NumberValue(4));

        session.UndoLastEdit().Success.Should().BeTrue();
        sheet4.GetValue(1, 1).Should().Be(new NumberValue(6));

        // Redo the delete again: CommandBus.Redo calls RemoveSheetCommand.Apply directly, which
        // also reports no AffectedCells -- without this fix the span SUM would keep showing the
        // stale (restored) 6 instead of contracting back to 1 + 3 = 4.
        var redoResult = session.RedoLastEdit();

        redoResult.Success.Should().BeTrue();
        workbook.Sheets.Select(s => s.Name).Should().Equal("Sheet1", "Sheet3", "Sheet4");
        sheet4.GetValue(1, 1).Should().Be(
            new NumberValue(4),
            "redoing the Delete Sheet must recompute the 3-D span SUM now that Sheet2 is gone again, not keep showing the stale restored total");
    }

    [Fact]
    public void UndoLastEdit_AfterOrdinaryCellEdit_StillReportsOnlyTheEditedCellAsAffected()
    {
        // No-regression sibling: undoing a plain cell edit (an EditCellsCommand, which DOES
        // implement IAffectedCellsCommand) must keep reporting exactly that cell as affected --
        // RequiresFullRecalc must stay false for every command other than the structural sheet ops
        // this fix targets, so this path's existing targeted-recalc behavior is unchanged.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var address = new CellAddress(sheet.Id, 1, 1);

        var session = CreateSession(workbook);
        session.SelectCell(address);
        session.CommitCellText("1");
        session.CommitCellText("2");
        sheet.GetValue(address).Should().Be(new NumberValue(2));

        var undoResult = session.UndoLastEdit();

        undoResult.Success.Should().BeTrue();
        undoResult.AffectedCells.Should().Equal(address);
        sheet.GetValue(address).Should().Be(new NumberValue(1));
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, workbook.Name, "Opened.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
