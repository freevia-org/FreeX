using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R107-commands-sheetrename-undo-recalc-1: RenameSheetCommand (and the sheet-tab-reorder
/// MoveSheetsCommand) rewrite cross-sheet formula text and reorder sheets respectively -- either
/// of which can change which sheets fall inside a 3-D span reference (e.g.
/// <c>=SUM(Sheet1:Sheet3!A1)</c>) -- but neither implemented <see cref="IWholeWorkbookRecalcCommand"/>.
/// The forward Execute path is covered by an explicit <c>RecalculateWorkbook()</c> call in
/// <c>WorkbookSession.RenameActiveSheet</c> (and, for MoveSheetsCommand, in the WPF host's sheet-tab
/// handlers), but <see cref="ICommandBus.Undo"/>/<see cref="ICommandBus.Redo"/> call straight into the
/// command bus and never reach those wrapper methods -- so Undo/Redo of either operation left 3-D
/// span formulas showing a stale value forever (until the next F9), unlike Excel, which always
/// recalculates immediately. See R30_PasteOperationTileAndSheetOpsRecalcTests for the sibling
/// forward-path coverage this suite does not duplicate.
/// </summary>
public sealed class R107_RenameAndMoveSheetsUndoRedoRecalcTests
{
    [Fact]
    public void RenameActiveSheetUndo_RevertsThreeDSpanFormulaBackToRefError()
    {
        var workbook = new Workbook("Book1");
        var host = workbook.AddSheet("Host");
        var toRename = workbook.AddSheet("Beta");
        var sheet2 = workbook.AddSheet("Sheet2");
        var sheet3 = workbook.AddSheet("Sheet3");
        workbook.ActiveSheetIndex = 0;
        var hostB1 = new CellAddress(host.Id, 1, 2);
        host.SetCell(hostB1, Cell.FromFormula("SUM(Sheet1:Sheet3!A1)"));
        toRename.SetCell(new CellAddress(toRename.Id, 1, 1), new NumberValue(5));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(1));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(2));

        var session = CreateSession(workbook);
        session.RecalculateWorkbook();

        // Before the rename, "Sheet1" does not exist, so the span cannot resolve -- matches
        // real Excel (see R30's RenameActiveSheet_RecalculatesThreeDSpanFormulaThatBecomesResolvable).
        host.GetValue(hostB1).Should().Be(ErrorValue.Ref);

        session.SelectSheet(toRename.Id);
        var renameResult = session.RenameActiveSheet("Sheet1");
        renameResult.Success.Should().BeTrue();
        host.GetValue(hostB1).Should().Be(new NumberValue(5 + 1 + 2));

        // Undo the rename: "Sheet1" no longer exists (the sheet is back to "Beta"), so the span
        // reference must once again fail to resolve, exactly like real Excel undoing a sheet
        // rename that a 3-D span formula only happened to resolve through. Without a forced
        // full recalc on Undo (RenameSheetCommand's own AffectedCells is empty), this would
        // keep showing the stale post-rename sum of 8 instead of reverting to #REF!.
        var undoResult = session.UndoLastEdit();

        undoResult.Success.Should().BeTrue();
        workbook.GetSheet(toRename.Id)!.Name.Should().Be("Beta");
        host.GetValue(hostB1).Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void RenameActiveSheetUndoThenRedo_ReappliesThreeDSpanFormulaValue()
    {
        var workbook = new Workbook("Book1");
        var host = workbook.AddSheet("Host");
        var toRename = workbook.AddSheet("Beta");
        var sheet2 = workbook.AddSheet("Sheet2");
        var sheet3 = workbook.AddSheet("Sheet3");
        workbook.ActiveSheetIndex = 0;
        var hostB1 = new CellAddress(host.Id, 1, 2);
        host.SetCell(hostB1, Cell.FromFormula("SUM(Sheet1:Sheet3!A1)"));
        toRename.SetCell(new CellAddress(toRename.Id, 1, 1), new NumberValue(5));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(1));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(2));

        var session = CreateSession(workbook);
        session.RecalculateWorkbook();
        session.SelectSheet(toRename.Id);
        session.RenameActiveSheet("Sheet1").Success.Should().BeTrue();
        session.UndoLastEdit().Success.Should().BeTrue();
        host.GetValue(hostB1).Should().Be(ErrorValue.Ref);

        // Redo must reapply the rename AND force a recalc of its own (CommandBus.Redo sets
        // RequiresFullRecalc from the same IWholeWorkbookRecalcCommand marker) -- otherwise the
        // span formula would keep showing the #REF! left over from the Undo above instead of
        // the resolved sum.
        var redoResult = session.RedoLastEdit();

        redoResult.Success.Should().BeTrue();
        workbook.GetSheet(toRename.Id)!.Name.Should().Be("Sheet1");
        host.GetValue(hostB1).Should().Be(new NumberValue(5 + 1 + 2));
    }

    [Fact]
    public void DeleteActiveSheetUndo_RestoresThreeDSpanFormulaToOriginalSum()
    {
        // No-regression sibling: RemoveSheetCommand already implemented IWholeWorkbookRecalcCommand
        // before this fix, so its Undo compensation must keep working exactly as before -- this
        // guards against the RenameSheetCommand/MoveSheetsCommand changes accidentally disturbing
        // the shared CommandBus.Undo/ApplyHistoryOutcome machinery the already-fixed sheet commands
        // rely on.
        var workbook = new Workbook("Book1");
        var host = workbook.AddSheet("Host");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var sheet3 = workbook.AddSheet("Sheet3");
        workbook.ActiveSheetIndex = 0;
        var hostB1 = new CellAddress(host.Id, 1, 2);
        host.SetCell(hostB1, Cell.FromFormula("SUM(Sheet1:Sheet3!A1)"));
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(10));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(20));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(30));

        var session = CreateSession(workbook);
        session.RecalculateWorkbook();
        host.GetValue(hostB1).Should().Be(new NumberValue(60));

        session.SelectSheet(sheet2.Id);
        session.DeleteActiveSheet().Success.Should().BeTrue();
        host.GetValue(hostB1).Should().Be(new NumberValue(40));

        var undoResult = session.UndoLastEdit();

        undoResult.Success.Should().BeTrue();
        workbook.Sheets.Select(s => s.Name).Should().Equal("Host", "Sheet1", "Sheet2", "Sheet3");
        host.GetValue(hostB1).Should().Be(new NumberValue(60));
    }

    [Fact]
    public void MoveSheetsCommandUndo_RevertsThreeDSpanFormulaAfterReorder()
    {
        var (workbook, commandBus, service, recalcEngine) = CreateEditService();
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        var sheet3 = workbook.AddSheet("Sheet3");
        var sheetX = workbook.AddSheet("SheetX");
        workbook.ActiveSheetIndex = 0;

        var a1Sheet1 = new CellAddress(sheet1.Id, 1, 1);
        var b1Sheet1 = new CellAddress(sheet1.Id, 1, 2);
        sheet1.SetCell(a1Sheet1, new NumberValue(10));
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(10));
        sheet3.SetCell(new CellAddress(sheet3.Id, 1, 1), new NumberValue(10));
        sheetX.SetCell(new CellAddress(sheetX.Id, 1, 1), new NumberValue(100));
        sheet1.SetFormula(b1Sheet1, "SUM(Sheet1:Sheet3!A1)");
        recalcEngine.RecalculateAllFormulas(workbook);

        // SheetX starts outside the Sheet1:Sheet3 span (order: Sheet1, Sheet2, Sheet3, SheetX).
        sheet1.GetValue(b1Sheet1).Should().Be(new NumberValue(30));

        // Move SheetX to before Sheet2 -- inside the span. MoveSheetsCommand itself reports no
        // AffectedCells, so mirror the real WPF host's forward-path compensation
        // (MainWindow.SheetTabs.cs calls RecalculateWorkbook() right after the command) with an
        // explicit recalc here, exactly like the product does.
        var moveResult = service.ExecuteEditCommand(
            workbook, new MoveSheetsCommand([sheetX.Id], insertBeforeIndex: 1));
        moveResult.Success.Should().BeTrue();
        recalcEngine.RecalculateAllFormulas(workbook);
        sheet1.GetValue(b1Sheet1).Should().Be(new NumberValue(130), "SheetX now sits inside the Sheet1:Sheet3 span");

        // Undo the reorder via the generic command-bus path (exactly what a sheet-tab drag-reorder
        // or "Move or Copy Sheet" Undo goes through) -- with no manual recalc call afterward. Only
        // MoveSheetsCommand's own forced-full-recalc compensation (IWholeWorkbookRecalcCommand) can
        // put the span formula back to its pre-move value.
        var undoResult = service.UndoLastEdit(workbook);

        undoResult.Success.Should().BeTrue();
        workbook.Sheets.Select(s => s.Name).Should().Equal("Sheet1", "Sheet2", "Sheet3", "SheetX");
        sheet1.GetValue(b1Sheet1).Should().Be(new NumberValue(30), "SheetX must be excluded from the span again after Undo");
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, workbook.Name, "Opened.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static (
        Workbook Workbook,
        CommandBus CommandBus,
        WorkbookCellEditService Service,
        RecalcEngine RecalcEngine) CreateEditService()
    {
        var workbook = new Workbook("Book1");
        var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var commandBus = new CommandBus(_ => new WorkbookCommandContext(workbook));
        var service = new WorkbookCellEditService(commandBus, recalcEngine);
        return (workbook, commandBus, service, recalcEngine);
    }
}
