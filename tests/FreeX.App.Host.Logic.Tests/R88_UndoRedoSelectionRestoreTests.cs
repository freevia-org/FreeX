using System.Reflection;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R88-commands-undo-redo-coalescing-5-1 (MainWindow.CommandExecution.cs):
/// Undo/Redo never restored the active sheet or selection to where the edit happened. Real Excel
/// switches back to the edited sheet and reselects the edited range on Ctrl+Z/Ctrl+Y; FreeX's
/// <c>ExecuteUndo</c>/<c>ExecuteRedo</c> reverted the model correctly but left <c>_currentSheetId</c>
/// and <c>SheetGrid.SelectedRange</c> completely untouched, so navigating away before undoing gave no
/// visible indication anything had changed.
/// </summary>
public sealed class R88_UndoRedoSelectionRestoreTests
{
    [Fact]
    public void ExecuteUndo_AfterNavigatingToDifferentSheet_RestoresEditedSheetAndSelection()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet1 = workbook.GetSheetAt(0);
                var sheet2 = workbook.AddSheet("Sheet2");
                var a1 = new CellAddress(sheet1.Id, 1, 1);

                window.SheetGrid.SelectedRange = new GridRange(a1, a1);
                R49MainWindowTestHarness.Invoke(window, "ApplyStyleDiff", new StyleDiff(Bold: true));

                sheet1.GetStyleOnly(1, 1).Should().NotBeNull("sanity: the bold command must actually have applied");

                // Navigate away to a different sheet and a different selection before undoing --
                // exactly the scenario the finding describes.
                var b5OnSheet2 = new CellAddress(sheet2.Id, 5, 2);
                SetCurrentSheetId(window, sheet2.Id);
                window.SheetGrid.SelectedRange = new GridRange(b5OnSheet2, b5OnSheet2);

                R49MainWindowTestHarness.Invoke(window, "ExecuteUndo");

                sheet1.GetStyleOnly(1, 1).Should().BeNull("the undo itself must still revert the bold");
                GetCurrentSheetId(window).Should().Be(
                    sheet1.Id, "undo must switch the active sheet back to where the edit happened, matching Excel");
                window.SheetGrid.SelectedRange.Should().Be(
                    new GridRange(a1, a1),
                    "undo must reselect the edited cell so the reverted value is immediately visible");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: undoing an edit made on the sheet the user is STILL viewing must keep
    // working exactly as before -- reverting the model and reselecting the affected cell.
    [Fact]
    public void ExecuteUndo_OnSameSheet_StillRevertsAndReselectsAffectedCell()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet1 = workbook.GetSheetAt(0);
                var a1 = new CellAddress(sheet1.Id, 1, 1);
                var b5 = new CellAddress(sheet1.Id, 5, 2);

                window.SheetGrid.SelectedRange = new GridRange(a1, a1);
                R49MainWindowTestHarness.Invoke(window, "ApplyStyleDiff", new StyleDiff(Bold: true));

                // Select something else on the SAME sheet before undoing.
                window.SheetGrid.SelectedRange = new GridRange(b5, b5);

                R49MainWindowTestHarness.Invoke(window, "ExecuteUndo");

                sheet1.GetStyleOnly(1, 1).Should().BeNull();
                GetCurrentSheetId(window).Should().Be(sheet1.Id);
                window.SheetGrid.SelectedRange.Should().Be(new GridRange(a1, a1));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static void SetCurrentSheetId(MainWindow window, SheetId sheetId)
    {
        var field = typeof(MainWindow).GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_currentSheetId");
        field.SetValue(window, sheetId);
    }

    private static SheetId GetCurrentSheetId(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_currentSheetId");
        return (SheetId)field.GetValue(window)!;
    }
}
