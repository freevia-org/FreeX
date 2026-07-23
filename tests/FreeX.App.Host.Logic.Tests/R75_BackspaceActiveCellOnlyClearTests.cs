using System.Reflection;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R75-commands-clear-delete-4-1
/// (<c>MainWindow.KeyboardCommands.cs</c>'s <c>ClearSelectionAndEdit</c> shortcut ->
/// <c>MainWindow.ClipboardCommands.cs</c>'s new <c>ExecuteClearActiveCell</c>): Backspace on a
/// multi-cell selection previously cleared the WHOLE selection (via the shared
/// <c>ExecuteClearSelection</c>) before entering edit -- but Excel's Backspace clears ONLY the
/// active cell (it is not Delete). The Delete key's full-selection clear
/// (<see cref="R54_ClipboardMarqueeAndCutMoveTests"/>'s sibling, <c>ExecuteClearSelection</c>)
/// must remain unchanged.
/// </summary>
public sealed class R75_BackspaceActiveCellOnlyClearTests
{
    [Fact]
    public void Backspace_OnMultiCellSelection_ClearsOnlyActiveCell_LeavesRestUntouched()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var a1 = new CellAddress(sheet.Id, 1, 1);
                var a2 = new CellAddress(sheet.Id, 2, 1);
                var a3 = new CellAddress(sheet.Id, 3, 1);
                sheet.SetCell(a1, new NumberValue(1));
                sheet.SetCell(a2, new NumberValue(2));
                sheet.SetCell(a3, new NumberValue(3));

                // Active cell is the range Start (A1), matching how the WPF host's own
                // ExecuteClearSelection/ExecuteClearActiveCell treat SheetGrid.SelectedRange.Start.
                SetSelectedRange(window, new GridRange(a1, a3));

                InvokePrivate(window, "ExecuteClearActiveCell");

                sheet.GetValue(a1).Should().Be(BlankValue.Instance, "Backspace must clear the active cell (A1)");
                sheet.GetCell(a2)!.Value.Should().Be(new NumberValue(2), "Backspace must NOT touch A2 -- it is not Delete/Clear Contents");
                sheet.GetCell(a3)!.Value.Should().Be(new NumberValue(3), "Backspace must NOT touch A3 -- it is not Delete/Clear Contents");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void DeleteKey_OnMultiCellSelection_StillClearsWholeSelection()
    {
        // Sibling no-regression: the pre-existing Delete-key (ExecuteClearSelection) full-selection
        // clear must be completely unaffected by adding the Backspace-only-active-cell path.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var a1 = new CellAddress(sheet.Id, 1, 1);
                var a2 = new CellAddress(sheet.Id, 2, 1);
                var a3 = new CellAddress(sheet.Id, 3, 1);
                sheet.SetCell(a1, new NumberValue(1));
                sheet.SetCell(a2, new NumberValue(2));
                sheet.SetCell(a3, new NumberValue(3));

                SetSelectedRange(window, new GridRange(a1, a3));

                InvokePrivate(window, "ExecuteClearSelection");

                sheet.GetValue(a1).Should().Be(BlankValue.Instance, "Delete must still clear the whole selection");
                sheet.GetValue(a2).Should().Be(BlankValue.Instance, "Delete must still clear the whole selection");
                sheet.GetValue(a3).Should().Be(BlankValue.Instance, "Delete must still clear the whole selection");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void Backspace_OnSingleCellSelection_StillClearsThatCell()
    {
        // Sibling no-regression: the common single-cell case (no multi-cell selection at all)
        // must keep working exactly as before.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var a1 = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(a1, new NumberValue(99));

                SetSelectedRange(window, new GridRange(a1, a1));

                InvokePrivate(window, "ExecuteClearActiveCell");

                sheet.GetValue(a1).Should().Be(BlankValue.Instance, "Backspace on a single-cell selection must clear that cell");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static void SetSelectedRange(MainWindow window, GridRange range)
    {
        window.SheetGrid.SelectedRanges = null;
        window.SheetGrid.SelectedRange = range;
    }

    private static void InvokePrivate(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic, [])
            ?? throw new MissingMethodException(nameof(MainWindow), methodName);
        method.Invoke(window, []);
        R49MainWindowTestHarness.PumpDispatcher();
    }
}
