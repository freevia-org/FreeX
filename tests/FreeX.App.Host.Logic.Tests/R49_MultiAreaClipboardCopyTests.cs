using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R49-render-multiarea-selection-3-1
/// (src/FreeX.App.Host/MainWindow.ClipboardCommands.cs, ExecuteCopy).
///
/// Before the fix: Copy/Cut on a multi-area (Ctrl+click) selection only read
/// <c>SheetGrid.SelectedRange</c> (the active area) and never consulted
/// <c>SheetGrid.SelectedRanges</c> -- so pasting elsewhere silently reproduced only the LAST
/// Ctrl-clicked area; every other area's data was dropped with no error and no visual indication.
///
/// After the fix, ExecuteCopy resolves the full multi-area selection (GetCurrentSelectionRanges,
/// the same helper Clear/Format commands already use), captures cells from EVERY area, and stores
/// the bounding box of all areas as the internal clipboard's SourceRange -- so a plain internal
/// paste elsewhere places each area's own cells at the correctly shifted destination, leaving any
/// "gap" between the disjoint areas untouched (matching Excel's own non-contiguous-copy layout
/// preservation).
/// </summary>
public sealed class R49_MultiAreaClipboardCopyTests
{
    [Fact]
    public void ExecuteCopy_MultiAreaSelection_PastesBothAreasAtDestination_LeavesGapUntouched()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                // Column A (1,2,3) and column C (10,20,30) -- column B is a genuine gap between
                // the two Ctrl-clicked areas.
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(1));
                sheet.SetCell(new CellAddress(sheetId, 2, 1), new NumberValue(2));
                sheet.SetCell(new CellAddress(sheetId, 3, 1), new NumberValue(3));
                sheet.SetCell(new CellAddress(sheetId, 1, 3), new NumberValue(10));
                sheet.SetCell(new CellAddress(sheetId, 2, 3), new NumberValue(20));
                sheet.SetCell(new CellAddress(sheetId, 3, 3), new NumberValue(30));

                var areaA = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 1)); // A1:A3
                var areaC = new GridRange(new CellAddress(sheetId, 1, 3), new CellAddress(sheetId, 3, 3)); // C1:C3

                // Mirrors what Ctrl+click builds: SelectedRanges holds every area (active area
                // last), SelectedRange is just the active one (C1:C3).
                window.SheetGrid.SelectedRanges = new[] { areaA, areaC };
                window.SheetGrid.SelectedRange = areaC;

                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                // Paste at E1 (column 5) -- a single-cell destination selection, offset +4 columns
                // from the bounding box's own start column (A = column 1).
                window.SheetGrid.SelectedRanges = null;
                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 5), new CellAddress(sheetId, 1, 5));

                R49MainWindowTestHarness.Invoke(
                    window, "ExecutePaste", PasteMode.All, default(PasteSpecialOptions), false, false);

                // Column A's data (shifted to column E = 5) must be present.
                sheet.GetCell(1, 5)!.Value.Should().Be(new NumberValue(1));
                sheet.GetCell(2, 5)!.Value.Should().Be(new NumberValue(2));
                sheet.GetCell(3, 5)!.Value.Should().Be(new NumberValue(3));

                // Column C's data (shifted to column G = 7) must ALSO be present -- this is exactly
                // the area that was silently dropped before the fix.
                sheet.GetCell(1, 7)!.Value.Should().Be(new NumberValue(10));
                sheet.GetCell(2, 7)!.Value.Should().Be(new NumberValue(20));
                sheet.GetCell(3, 7)!.Value.Should().Be(new NumberValue(30));

                // The gap column B's shifted position (column F = 6) was never part of either
                // selected area, so it must be left untouched/blank.
                (sheet.GetCell(1, 6)?.Value ?? BlankValue.Instance).Should().Be(BlankValue.Instance);
                (sheet.GetCell(2, 6)?.Value ?? BlankValue.Instance).Should().Be(BlankValue.Instance);
                (sheet.GetCell(3, 6)?.Value ?? BlankValue.Instance).Should().Be(BlankValue.Instance);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: an ordinary single-area copy+paste (the overwhelmingly common case)
    // must still work exactly as before the fix.
    [Fact]
    public void ExecuteCopy_SingleAreaSelection_StillPastesAtDestination()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(7));
                sheet.SetCell(new CellAddress(sheetId, 2, 1), new NumberValue(8));

                window.SheetGrid.SelectedRanges = null;
                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 1)); // A1:A2

                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 4), new CellAddress(sheetId, 1, 4)); // D1

                for (var attempt = 0; attempt < 3 && sheet.GetCell(1, 4) is null; attempt++)
                {
                    R49MainWindowTestHarness.Invoke(
                        window, "ExecutePaste", PasteMode.All, default(PasteSpecialOptions), false, false);
                    if (sheet.GetCell(1, 4) is null)
                    {
                        // A transient OS clipboard read deliberately leaves the shared snapshot
                        // intact and asks the user to retry, so exercise that supported path.
                        R49MainWindowTestHarness.PumpDispatcher();
                        System.Threading.Thread.Sleep(25);
                    }
                }

                sheet.GetCell(1, 4)!.Value.Should().Be(new NumberValue(7));
                sheet.GetCell(2, 4)!.Value.Should().Be(new NumberValue(8));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
