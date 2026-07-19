using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R49-render-multiarea-selection-3-3
/// (src/FreeX.App.Host/MainWindow.Editing.cs, CommitEditAcrossSelection).
///
/// Before the fix: Ctrl+Enter (fill every selected cell with one entry) on a multi-area (Ctrl+click)
/// selection only read SheetGrid.SelectedRange (the active area) and iterated `range.AllCells()`,
/// never consulting SheetGrid.SelectedRanges -- so only the active area got filled; every other
/// Ctrl-added area was silently skipped with no indication anything was omitted.
///
/// After the fix, CommitEditAcrossSelection resolves the full multi-area selection via
/// GetCurrentSelectionRanges (the same helper Clear/Format commands already use for this scenario)
/// and fills EVERY selected area with the same entry.
/// </summary>
public sealed class R49_MultiAreaCtrlEnterFillTests
{
    [Fact]
    public void CommitEditAcrossSelection_MultiAreaSelection_FillsEveryArea()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                var areaA = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 1)); // A1:A2
                var areaC = new GridRange(new CellAddress(sheetId, 1, 3), new CellAddress(sheetId, 2, 3)); // C1:C2

                // Mirrors what Ctrl+click builds: SelectedRanges holds every area (active area
                // last), SelectedRange is just the active one (C1:C2).
                window.SheetGrid.SelectedRanges = new[] { areaA, areaC };
                window.SheetGrid.SelectedRange = areaC;

                window.FormulaBar.Text = "5";

                R49MainWindowTestHarness.Invoke(window, "CommitEditAcrossSelection", false);

                // The active area (C1:C2) was always filled, even before the fix.
                sheet.GetCell(1, 3)!.Value.Should().Be(new NumberValue(5));
                sheet.GetCell(2, 3)!.Value.Should().Be(new NumberValue(5));

                // The OTHER Ctrl-added area (A1:A2) must ALSO be filled -- this is exactly what was
                // silently skipped before the fix.
                sheet.GetCell(1, 1)!.Value.Should().Be(new NumberValue(5));
                sheet.GetCell(2, 1)!.Value.Should().Be(new NumberValue(5));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: an ordinary single-area Ctrl+Enter fill must still fill every cell in
    // that one selected range, exactly as before the fix.
    [Fact]
    public void CommitEditAcrossSelection_SingleAreaSelection_StillFillsWholeRange()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                window.SheetGrid.SelectedRanges = null;
                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 2), new CellAddress(sheetId, 3, 2)); // B1:B3

                window.FormulaBar.Text = "9";

                R49MainWindowTestHarness.Invoke(window, "CommitEditAcrossSelection", false);

                sheet.GetCell(1, 2)!.Value.Should().Be(new NumberValue(9));
                sheet.GetCell(2, 2)!.Value.Should().Be(new NumberValue(9));
                sheet.GetCell(3, 2)!.Value.Should().Be(new NumberValue(9));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
