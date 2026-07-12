using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R30-clipboard-paste-special-ops-2: WorkbookSession.ShouldFillSelectedDestinationRange (the
/// Avalonia shell's local copy) still required <c>options.Operation == PasteSpecialOperation.None</c>
/// as a conjunct -- the exact stale/pre-R16 condition already removed from the WPF-side
/// ClipboardPastePlanner counterpart (see its comment citing R16-paste-special-matrix-1). Because of
/// that stale conjunct, a Paste Special with an arithmetic Operation (Add/Subtract/Multiply/Divide)
/// only ever touched the anchor cell instead of tiling across the whole selected destination range,
/// diverging from both real Excel and the already-fixed WPF host.
///
/// R30-commands-structural-3dref-1: DeleteActiveSheet/RenameActiveSheet never forced a recalc, so
/// 3-D span formulas (e.g. =SUM(Sheet1:Sheet3!A1)) kept showing a stale cached value after either
/// operation -- unlike MoveActiveSheetTo/DuplicateActiveSheet, which already call RecalculateWorkbook()
/// with that exact rationale.
/// </summary>
public sealed class R30_PasteOperationTileAndSheetOpsRecalcTests
{
    [Fact]
    public void PasteSpecialWithOperation_OverMultiCellSelection_TilesAcrossWholeDestinationRange()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, new NumberValue(5));

        var destinationCells = Enumerable.Range(1, 10)
            .Select(row => new CellAddress(sheet.Id, (uint)row, 2))
            .ToList();
        for (var i = 0; i < destinationCells.Count; i++)
            sheet.SetCell(destinationCells[i], new NumberValue(i + 1));

        var session = CreateSession(workbook);
        session.SelectCell(source);
        var clipboardText = session.CopySelectedRangeText();

        session.SelectRange(new GridRange(destinationCells[0], destinationCells[^1]));
        var options = new PasteSpecialOptions(Operation: PasteSpecialOperation.Add);

        var result = session.PasteSpecialClipboardAtActiveCell(clipboardText, PasteCellsMode.All, options);

        result.Success.Should().BeTrue();

        // Every cell in the selected B1:B10 destination must get the arithmetic operation applied
        // (5 added to its existing value), not just the anchor B1 -- matching real Excel and the
        // WPF host's already-fixed ClipboardPastePlanner.ShouldFillSelectedDestinationRange.
        for (var i = 0; i < destinationCells.Count; i++)
            sheet.GetValue(destinationCells[i]).Should().Be(new NumberValue(i + 1 + 5));
    }

    [Fact]
    public void PasteSpecialWithoutOperation_OverMultiCellSelection_StillTilesAcrossWholeDestinationRange()
    {
        // Sibling already-working case: a plain Paste Special (Operation.None) must keep tiling
        // across the selected destination exactly as before this fix.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, new NumberValue(7));

        var destinationCells = Enumerable.Range(1, 3)
            .Select(row => new CellAddress(sheet.Id, (uint)row, 2))
            .ToList();

        var session = CreateSession(workbook);
        session.SelectCell(source);
        var clipboardText = session.CopySelectedRangeText();

        session.SelectRange(new GridRange(destinationCells[0], destinationCells[^1]));
        var result = session.PasteSpecialClipboardAtActiveCell(clipboardText, PasteCellsMode.All, default);

        result.Success.Should().BeTrue();
        foreach (var cell in destinationCells)
            sheet.GetValue(cell).Should().Be(new NumberValue(7));
    }

    [Fact]
    public void DeleteActiveSheet_RecalculatesThreeDSpanFormulaToTheContractedSum()
    {
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

        // Delete the middle sheet of the span (Sheet2). The span contracts to Sheet1:Sheet3's
        // surviving members, so the SUM must drop to 10 + 30 = 40. Without a post-delete recalc,
        // the cached value would still show the stale pre-delete 60.
        session.SelectSheet(sheet2.Id);
        var result = session.DeleteActiveSheet();

        result.Success.Should().BeTrue();
        workbook.Sheets.Select(s => s.Name).Should().Equal("Host", "Sheet1", "Sheet3");
        host.GetValue(hostB1).Should().Be(new NumberValue(40));
    }

    [Fact]
    public void RenameActiveSheet_RecalculatesThreeDSpanFormulaThatBecomesResolvable()
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

        // Before the rename, "Sheet1" does not exist, so the span reference cannot resolve --
        // real Excel would show #REF! here too.
        host.GetValue(hostB1).Should().Be(ErrorValue.Ref);

        // Renaming "Beta" to "Sheet1" makes the span resolvable. RenameSheetCommand's own
        // AffectedCells only covers cells whose formula TEXT literally referenced the old name
        // "Beta" (none do here), so the span formula is only picked up by the post-rename
        // RecalculateWorkbook() call -- without it, B1 would keep showing the stale #REF!.
        session.SelectSheet(toRename.Id);
        var result = session.RenameActiveSheet("Sheet1");

        result.Success.Should().BeTrue();
        host.GetValue(hostB1).Should().Be(new NumberValue(5 + 1 + 2));
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
