using System.Reflection;
using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R139-freex-cell-editing-edit-commit-collapses-range-selection
/// (src/FreeX.App.Host/MainWindow.Editing.cs).
///
/// Before this fix, committing an in-cell/formula-bar edit with Enter or Tab always routed
/// through SetActiveCell(next) -- which unconditionally collapses any pre-existing multi-cell
/// selection down to a single cell (SetSelectedRangesIfChanged(null) +
/// SheetGrid.SelectedRange = new GridRange(addr, addr)) -- instead of cycling the active cell
/// WITHIN the pre-existing selection the way real Excel does (matching the app's own ready-mode
/// Enter/Tab handler, which already used GridSelectionNavigationPlanner.PlanCycle +
/// MoveActiveCellWithinSelection for exactly this reason). A user who pre-selects B2:B5, types a
/// value, and presses Enter lost the B2:B5 highlight after the very first commit.
/// </summary>
public sealed class R139_EditCommitPreservesRangeSelectionTests
{
    [Fact]
    public void InlineEditor_EnterCommit_WithinPreExistingRangeSelection_KeepsSelectionAndCyclesActiveCell()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var top = new CellAddress(sheetId, 2, 2);    // B2
                var bottom = new CellAddress(sheetId, 5, 2); // B5
                var range = new GridRange(top, bottom);

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", top);
                R49MainWindowTestHarness.Invoke(window, "ExtendSelection", top, bottom);
                window.SheetGrid.SelectedRange.Should().Be(range);

                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", top, (double?)null);
                var inlineEditor = GetInlineEditor(window);
                inlineEditor.Should().NotBeNull();
                inlineEditor!.Text = "10";

                PressInlineEditorKey(window, inlineEditor, Key.Enter);

                sheet.GetCell(top)!.Value.Should().Be(new NumberValue(10),
                    "the typed value must still commit to the cell that was being edited");
                window.SheetGrid.SelectedRange.Should().Be(range,
                    "Enter committing an edit must NOT collapse a pre-existing multi-cell selection " +
                    "(R139-freex-cell-editing-edit-commit-collapses-range-selection)");
                window.SheetGrid.ActiveCell.Should().Be(new CellAddress(sheetId, 3, 2), // B3
                    "Enter must advance the active cell to the next cell WITHIN the selection, " +
                    "exactly like the ready-mode Enter/Tab cycling handler");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling coverage for Tab, sharing the same CommitAndMove branch and the same pre-fix
    // unconditional SetActiveCell(next) collapse.
    [Fact]
    public void InlineEditor_TabCommit_WithinPreExistingRangeSelection_KeepsSelectionAndCyclesActiveCell()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var left = new CellAddress(sheetId, 2, 2);  // B2
                var right = new CellAddress(sheetId, 2, 4); // D2
                var range = new GridRange(left, right);

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", left);
                R49MainWindowTestHarness.Invoke(window, "ExtendSelection", left, right);
                window.SheetGrid.SelectedRange.Should().Be(range);

                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", left, (double?)null);
                var inlineEditor = GetInlineEditor(window);
                inlineEditor!.Text = "x";

                PressInlineEditorKey(window, inlineEditor, Key.Tab);

                window.SheetGrid.SelectedRange.Should().Be(range,
                    "Tab committing an edit must NOT collapse a pre-existing multi-cell selection");
                window.SheetGrid.ActiveCell.Should().Be(new CellAddress(sheetId, 2, 3), // C2
                    "Tab must advance the active cell WITHIN the selection, one column to the right");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Wrap-direction coverage: committing at the LAST cell of the range must wrap the active cell
    // back to the FIRST cell of the range, matching Excel, instead of walking off the selection.
    [Fact]
    public void InlineEditor_EnterCommit_AtEndOfRangeSelection_WrapsActiveCellBackToStart()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var top = new CellAddress(sheetId, 2, 2);    // B2
                var bottom = new CellAddress(sheetId, 3, 2); // B3
                var range = new GridRange(top, bottom);

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", top);
                R49MainWindowTestHarness.Invoke(window, "ExtendSelection", top, bottom);

                // Commit #1 at B2 -> advances to B3 (still inside the range).
                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", top, (double?)null);
                var inlineEditor = GetInlineEditor(window);
                inlineEditor!.Text = "1";
                PressInlineEditorKey(window, inlineEditor, Key.Enter);
                window.SheetGrid.ActiveCell.Should().Be(bottom);
                window.SheetGrid.SelectedRange.Should().Be(range);

                // Commit #2 at B3 (the range's last cell in travel direction) -> must WRAP back to
                // B2, not fall through to plain SetActiveCell(B4), and must keep the whole B2:B3
                // selection intact.
                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", bottom, (double?)null);
                inlineEditor = GetInlineEditor(window);
                inlineEditor!.Text = "2";
                PressInlineEditorKey(window, inlineEditor, Key.Enter);

                window.SheetGrid.ActiveCell.Should().Be(top,
                    "Enter from the last cell of the selected range must wrap back to the first cell");
                window.SheetGrid.SelectedRange.Should().Be(range,
                    "wrapping must not collapse or otherwise change the selected range");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: when there is NO multi-cell selection (a single selected cell),
    // Enter committing an edit must keep behaving exactly as before this fix -- the active cell
    // simply advances via SetActiveCell, collapsing to (and staying on) a single cell.
    [Fact]
    public void InlineEditor_EnterCommit_WithNoRangeSelection_StillAdvancesActiveCellNormally()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var addr = new CellAddress(sheetId, 2, 2); // B2

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", addr);
                window.SheetGrid.SelectedRange.Should().Be(new GridRange(addr, addr));

                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", addr, (double?)null);
                var inlineEditor = GetInlineEditor(window);
                inlineEditor!.Text = "42";

                PressInlineEditorKey(window, inlineEditor, Key.Enter);

                sheet.GetCell(addr)!.Value.Should().Be(new NumberValue(42));
                var expectedNext = new CellAddress(sheetId, 3, 2); // B3
                window.SheetGrid.ActiveCell.Should().Be(expectedNext,
                    "with no multi-cell selection to cycle within, Enter must still just advance " +
                    "the active cell downward, unchanged from the pre-fix behavior");
                window.SheetGrid.SelectedRange.Should().Be(new GridRange(expectedNext, expectedNext),
                    "a single-cell selection has nothing to preserve, so it stays a single cell");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Family coverage: the Formula Bar's own CommitAndMove branch (FormulaBar_KeyDown) shares the
    // exact same pre-fix bug (SetActiveCell(target) unconditionally) as the in-cell editor's.
    [Fact]
    public void FormulaBar_EnterCommit_WithinPreExistingRangeSelection_KeepsSelectionAndCyclesActiveCell()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var top = new CellAddress(sheetId, 2, 2);    // B2
                var bottom = new CellAddress(sheetId, 5, 2); // B5
                var range = new GridRange(top, bottom);

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", top);
                R49MainWindowTestHarness.Invoke(window, "ExtendSelection", top, bottom);

                R49MainWindowTestHarness.Invoke(window, "EditActiveCellInFormulaBar");
                window.FormulaBar.Text = "7";

                var source = PresentationSource.FromVisual(window)
                    ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
                var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter)
                {
                    RoutedEvent = Keyboard.KeyDownEvent
                };
                R49MainWindowTestHarness.Invoke(window, "FormulaBar_KeyDown", window.FormulaBar, args);
                R49MainWindowTestHarness.PumpDispatcher();

                sheet.GetCell(top)!.Value.Should().Be(new NumberValue(7));
                window.SheetGrid.SelectedRange.Should().Be(range,
                    "the Formula Bar's own Enter-commit path must also preserve a pre-existing " +
                    "multi-cell selection instead of collapsing it");
                window.SheetGrid.ActiveCell.Should().Be(new CellAddress(sheetId, 3, 2)); // B3
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static void PressInlineEditorKey(MainWindow window, System.Windows.Controls.TextBox inlineEditor, Key key)
    {
        var source = PresentationSource.FromVisual(window)
            ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
        R49MainWindowTestHarness.Invoke(window, "InlineEditor_KeyDown", inlineEditor, args);
        R49MainWindowTestHarness.PumpDispatcher();
    }

    private static System.Windows.Controls.TextBox? GetInlineEditor(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_inlineEditor", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_inlineEditor");
        return (System.Windows.Controls.TextBox?)field.GetValue(window);
    }
}
