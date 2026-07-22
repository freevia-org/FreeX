using System.Reflection;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R68-app-selection-navigation-6-1 (WPF half)
/// (src/FreeX.App.Host/MainWindow.Selection.cs, SheetGrid_MouseDown's cell-area click branch,
/// extracted into TryHandleCellAreaExtendClick).
///
/// Before the fix: F8 "Extend Selection" mode (_selectionMode == ExcelSelectionMode.Extend) had no
/// effect on mouse clicks -- SheetGrid_MouseDown's first branch only checked Shift, so a plain click
/// while F8 was active fell through to the ordinary click branch (SetActiveCell) and collapsed the
/// selection to the clicked cell instead of extending it from the anchor, unlike the keyboard path
/// (arrow keys), which already honored F8 via ExcelSelectionModePlanner.ShouldExtendSelection.
///
/// After the fix, the branch uses ExcelSelectionModePlanner.ShouldExtendSelection(_selectionMode,
/// Keyboard.Modifiers) -- the same predicate the keyboard path uses -- so an F8-mode plain click
/// extends from the anchor exactly like Shift+click, and leaves F8 mode active afterward.
///
/// Driving an actual pixel-accurate WPF MouseButtonEventArgs through SheetGrid's real hit-testing
/// isn't a reliable/deterministic unit-test surface (see R49_MultiAreaHeaderSelectionTests for the
/// established precedent), so these tests exercise the extracted TryHandleCellAreaExtendClick unit
/// directly with Keyboard.Modifiers naturally at None (no physical keys held during a test run).
/// </summary>
public sealed class R68_F8ExtendSelectionMouseClickTests
{
    [Fact]
    public void TryHandleCellAreaExtendClick_F8ExtendModeActive_ExtendsFromAnchorAndKeepsF8Active()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = workbook.GetSheetAt(0).Id;
                var a1 = new CellAddress(sheetId, 1, 1);
                var d5 = new CellAddress(sheetId, 5, 4);

                // Click A1 (plain click; establishes the anchor).
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", a1);

                // Enter F8 "Extend Selection" mode.
                SetSelectionMode(window, ExcelSelectionMode.Extend);

                // A plain click on D5 while F8 is active.
                var handled = (bool)R49MainWindowTestHarness.Invoke(window, "TryHandleCellAreaExtendClick", d5)!;

                handled.Should().BeTrue("an F8-mode click must be handled as an extend, not fall through to a plain click");
                window.SheetGrid.SelectedRange.Should().Be(new GridRange(a1, d5), "F8 must extend the selection from the anchor to the clicked cell");
                GetSelectionMode(window).Should().Be(ExcelSelectionMode.Extend, "F8 extend mode must remain active after the click");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void TryHandleCellAreaExtendClick_NoF8AndNoShift_DoesNothing()
    {
        // Sibling/no-regression: without F8 (or Shift), the extend-click helper must be a no-op,
        // leaving SheetGrid_MouseDown's ordinary click branch (SetActiveCell) to collapse the
        // selection to the clicked cell exactly as before this fix.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheetId = workbook.GetSheetAt(0).Id;
                var a1 = new CellAddress(sheetId, 1, 1);
                var d5 = new CellAddress(sheetId, 5, 4);

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", a1);
                GetSelectionMode(window).Should().Be(ExcelSelectionMode.Normal, "F8 must not be active for this case");

                var handled = (bool)R49MainWindowTestHarness.Invoke(window, "TryHandleCellAreaExtendClick", d5)!;

                handled.Should().BeFalse("without F8 or Shift, the extend-click helper must not handle a plain click");
                window.SheetGrid.SelectedRange.Should().Be(new GridRange(a1, a1), "a no-op extend-click must leave the existing selection untouched");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static void SetSelectionMode(MainWindow window, ExcelSelectionMode mode)
    {
        var field = typeof(MainWindow).GetField("_selectionMode", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_selectionMode");
        field.SetValue(window, mode);
    }

    private static ExcelSelectionMode GetSelectionMode(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_selectionMode", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_selectionMode");
        return (ExcelSelectionMode)field.GetValue(window)!;
    }
}
