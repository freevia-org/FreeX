using System.Reflection;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R57-stale-cache-not-invalidated-sweep-1
/// (src/FreeX.App.Host/MainWindow.ScenarioCommands.cs, ShowScenarioByName).
///
/// Before the fix: Scenario Manager "Show" applied the scenario's cell values via
/// _commandBus.ExecuteRepeatable directly and then only called RecalculateIfAutomatic, which is a
/// no-op outside Automatic/AutomaticExceptDataTables calculation mode -- so in Manual mode,
/// _navigationCacheRevision (which SparklineValueCache/WorkbookSelectionStatsCache are keyed on) never
/// advanced, leaving sparklines and status-bar stats over the changed cells stale even though the
/// cell values themselves were written immediately (matching real Excel, which always applies a
/// scenario's values right away regardless of calculation mode).
///
/// After the fix, ShowScenarioByName unconditionally invalidates the navigation caches when the
/// workbook is in a manual calculation mode, mirroring the existing Goal Seek fix.
/// </summary>
public sealed class R57_ScenarioShowManualCalcNavCacheTests
{
    [Fact]
    public void ShowScenarioByName_ManualCalcMode_InvalidatesNavigationCaches()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var changingCell = new CellAddress(sheetId, 3, 1); // A3

                workbook.CalculationMode = WorkbookCalculationMode.Manual;
                workbook.Scenarios.Add(new WorkbookScenario(
                    "Scenario1",
                    [new ScenarioCellValue(changingCell, new NumberValue(50))]));

                var revisionField = typeof(MainWindow).GetField(
                    "_navigationCacheRevision", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var before = (ulong)revisionField.GetValue(window)!;

                R49MainWindowTestHarness.Invoke(window, "ShowScenarioByName", "Scenario1");

                var after = (ulong)revisionField.GetValue(window)!;
                after.Should().BeGreaterThan(before,
                    "Excel always reflects a scenario's applied values immediately regardless of " +
                    "calculation mode, so the navigation-cache revision must advance even in Manual mode");

                sheet.GetCell(changingCell)!.Value.Should().Be(new NumberValue(50));
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
    public void ShowScenarioByName_AutomaticCalcMode_StillInvalidatesNavigationCaches()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var changingCell = new CellAddress(sheetId, 3, 1); // A3

                workbook.CalculationMode = WorkbookCalculationMode.Automatic;
                workbook.Scenarios.Add(new WorkbookScenario(
                    "Scenario1",
                    [new ScenarioCellValue(changingCell, new NumberValue(50))]));

                var revisionField = typeof(MainWindow).GetField(
                    "_navigationCacheRevision", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var before = (ulong)revisionField.GetValue(window)!;

                R49MainWindowTestHarness.Invoke(window, "ShowScenarioByName", "Scenario1");

                var after = (ulong)revisionField.GetValue(window)!;
                after.Should().BeGreaterThan(before);
                sheet.GetCell(changingCell)!.Value.Should().Be(new NumberValue(50));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
