using System.Reflection;
using FluentAssertions;
using FreeX.App.Presentation.Editing;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R57-stale-cache-not-invalidated-sweep-2
/// (src/FreeX.App.Host/MainWindow.ClipboardCommands.cs, ExecutePaste's internal-clipboard branch).
///
/// Before the fix: Paste (Ctrl+V) applied the pasted cell values via _commandBus.ExecuteRepeatable
/// directly and then only called RecalculateIfAutomatic, which is a no-op outside Automatic/
/// AutomaticExceptDataTables calculation mode -- so in Manual mode, _navigationCacheRevision (which
/// SparklineValueCache/WorkbookSelectionStatsCache are keyed on) never advanced even though the pasted
/// values were written to the grid immediately (matching real Excel, which always applies a plain
/// value paste right away regardless of calculation mode).
///
/// After the fix, ExecutePaste's internal-clipboard branch unconditionally invalidates the
/// navigation caches when the workbook is in a manual calculation mode.
/// </summary>
public sealed class R57_PasteManualCalcNavCacheTests
{
    [Fact]
    public void ExecutePaste_ManualCalcMode_InvalidatesNavigationCaches()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(99)); // A1

                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));
                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                workbook.CalculationMode = WorkbookCalculationMode.Manual;

                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 5, 1), new CellAddress(sheetId, 5, 1)); // A5

                var revisionField = typeof(MainWindow).GetField(
                    "_navigationCacheRevision", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var before = (ulong)revisionField.GetValue(window)!;

                R49MainWindowTestHarness.Invoke(
                    window, "ExecutePaste", PasteMode.All, default(PasteSpecialOptions), false, false);

                var after = (ulong)revisionField.GetValue(window)!;
                after.Should().BeGreaterThan(before,
                    "Excel always reflects a pasted value immediately regardless of calculation " +
                    "mode, so the navigation-cache revision must advance even in Manual mode");

                sheet.GetCell(new CellAddress(sheetId, 5, 1))!.Value.Should().Be(new NumberValue(99));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: the overwhelmingly common Automatic-mode case (already correct before
    // the fix, via RecalculateIfAutomatic) must still invalidate the navigation caches.
    [Fact]
    public void ExecutePaste_AutomaticCalcMode_StillInvalidatesNavigationCaches()
    {
        StaTestRunner.RunClipboardIsolated(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(99)); // A1

                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));
                R49MainWindowTestHarness.Invoke(window, "ExecuteCopy", false);

                workbook.CalculationMode = WorkbookCalculationMode.Automatic;

                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 5, 1), new CellAddress(sheetId, 5, 1)); // A5

                var revisionField = typeof(MainWindow).GetField(
                    "_navigationCacheRevision", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var before = (ulong)revisionField.GetValue(window)!;

                R49MainWindowTestHarness.Invoke(
                    window, "ExecutePaste", PasteMode.All, default(PasteSpecialOptions), false, false);

                var after = (ulong)revisionField.GetValue(window)!;
                after.Should().BeGreaterThan(before);
                sheet.GetCell(new CellAddress(sheetId, 5, 1))!.Value.Should().Be(new NumberValue(99));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
