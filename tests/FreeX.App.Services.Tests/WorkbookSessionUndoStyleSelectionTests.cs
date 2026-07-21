using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for R63-services-undo-redo-6-2 (deferred from R62-services-undo-redo-6-1):
/// undoing a formatting-only style command (ApplyStyleCommand/GroupedApplyStyleCommand) must
/// restore the affected sheet and full range selection, matching the existing value-edit (Sort)
/// coverage in <see cref="WorkbookSessionUndoSelectionTests"/>. Before the fix, ApplyStyleCommand
/// reported no <c>AffectedCells</c> on its <c>CommandOutcome</c>, so
/// <c>WorkbookSession.ApplySuccessfulHistoryResult</c> had nothing to switch sheets or restore a
/// range selection with.
/// </summary>
public sealed class WorkbookSessionUndoStyleSelectionTests
{
    [Fact]
    public void UndoLastEdit_AfterApplyingStyleOnAnotherSheet_SwitchesBackAndRestoresFullRangeSelection()
    {
        // Bold A1:A3 on Sheet2, switch to Sheet1 (no new undo entry for the tab switch), then
        // Ctrl+Z. Excel switches the view back to Sheet2 and re-selects the full formatted range,
        // instead of leaving the view on Sheet1 with nothing restored.
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.AddSheet("Sheet2");
        workbook.ActiveSheetIndex = 0;
        var sheet2 = workbook.Sheets[1];
        var a1 = new CellAddress(sheet2.Id, 1, 1);
        var a2 = new CellAddress(sheet2.Id, 2, 1);
        var a3 = new CellAddress(sheet2.Id, 3, 1);
        sheet2.SetCell(a1, new NumberValue(2));
        sheet2.SetCell(a2, new NumberValue(3));
        sheet2.SetCell(a3, new NumberValue(1));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        session.SelectSheet(sheet2.Id);
        var range = new GridRange(a1, a3);
        session.SelectRange(range);
        session.SetSelectedRangeBold(true).Success.Should().BeTrue();

        session.SelectSheet(workbook.Sheets[0].Id);
        session.ActiveSheet.Id.Should().Be(workbook.Sheets[0].Id);

        var undoResult = session.UndoLastEdit();

        undoResult.Success.Should().BeTrue(undoResult.ErrorMessage);
        session.ActiveSheet.Id.Should().Be(sheet2.Id);
        session.SelectedRange.Should().Be(range);
        session.ActiveCell.Should().Be(a1);
    }

    [Fact]
    public void UndoLastEdit_AfterApplyingStyleSameSheet_RestoresFullRangeSelection()
    {
        // No-regression sibling: a same-sheet style undo must still restore the full affected
        // range, not just leave whatever was selected when Undo was invoked. The selection is
        // deliberately moved away (to a single unrelated cell) between applying the style and
        // undoing it, so this genuinely exercises the restore path rather than passing by
        // coincidence because the selection was never disturbed.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(3));
        sheet.SetCell(a3, new NumberValue(1));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var range = new GridRange(a1, a3);
        session.SelectRange(range);

        session.SetSelectedRangeBold(true).Success.Should().BeTrue();
        session.SelectedRange.Should().Be(range);

        // Move the selection elsewhere before undoing, mirroring the cross-sheet test's "switch
        // away" step but staying on the same sheet.
        var elsewhere = new CellAddress(sheet.Id, 10, 5);
        session.SelectRange(new GridRange(elsewhere, elsewhere));

        var undoResult = session.UndoLastEdit();

        undoResult.Success.Should().BeTrue(undoResult.ErrorMessage);
        session.ActiveSheet.Id.Should().Be(sheet.Id);
        session.SelectedRange.Should().Be(range);
        session.ActiveCell.Should().Be(a1);
    }

    private static WorkbookSession CreateSession(StartupWorkbookLoadResult source) =>
        new WorkbookSessionFactory().Create(source, viewportHeight: 240, viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
