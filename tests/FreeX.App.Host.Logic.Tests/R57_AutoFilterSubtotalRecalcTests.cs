using System.Reflection;
using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R57-formula-subtotal-aggregate-5-1
/// (src/FreeX.App.Host/MainWindow.DataFilterCommands.cs).
///
/// Before the fix: applying an AutoFilter (or any of the sort/filter ribbon commands in this file)
/// dispatched only through TryExecuteRepeatableCurrentRangeCommand/TryExecuteRememberedAutoFilterCommand
/// (MainWindow.CommandExecution.cs), whose success path marks the workbook dirty and bumps the
/// navigation-cache revision but NEVER recalculates formulas. Filtering hides rows, and
/// SUBTOTAL(101-111)/AGGREGATE ignore-hidden formulas depend on that hidden-row visibility, so their
/// cached value stayed stale until an unrelated later edit happened to trigger a recalc pass that
/// touched them. Real Excel always recalculates SUBTOTAL/AGGREGATE the instant filter visibility
/// changes.
///
/// After the fix, every filter/sort mutation in MainWindow.DataFilterCommands.cs forces a full
/// recalculation (RecalculateAfterFilterOrSort) after a successful command.
/// </summary>
public sealed class R57_AutoFilterSubtotalRecalcTests
{
    [Fact]
    public void ApplyAutoFilter_HidesRows_RecalculatesSubtotalOverFilteredRange()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                sheet.SetCell(new CellAddress(sheetId, 2, 1), new NumberValue(10)); // A2
                sheet.SetCell(new CellAddress(sheetId, 3, 1), new NumberValue(20)); // A3
                sheet.SetCell(new CellAddress(sheetId, 4, 1), new NumberValue(30)); // A4
                sheet.SetCell(new CellAddress(sheetId, 5, 1), new NumberValue(40)); // A5
                sheet.SetCell(new CellAddress(sheetId, 6, 1), new NumberValue(50)); // A6
                sheet.SetFormula(new CellAddress(sheetId, 1, 2), "SUBTOTAL(109,A2:A6)"); // B1

                var recalcMethod = typeof(MainWindow).GetMethod(
                    "RecalculateWorkbook", BindingFlags.Instance | BindingFlags.NonPublic, [])
                    ?? throw new MissingMethodException(nameof(MainWindow), "RecalculateWorkbook");
                recalcMethod.Invoke(window, []);

                sheet.GetValue(1, 2).Should().Be(new NumberValue(150), "10+20+30+40+50 before any filter");

                var filterRange = new GridRange(
                    new CellAddress(sheetId, 2, 1), new CellAddress(sheetId, 6, 1)); // A2:A6
                window.SheetGrid.SelectedRange = filterRange;
                var result = new AutoFilterDialogResult(
                    AutoFilterSortDirection.None,
                    ["10", "20", "30"],
                    SearchText: "",
                    CriteriaText: "");
                var success = (bool)R49MainWindowTestHarness.Invoke(
                    window,
                    "ApplyAutoFilterDialogResult",
                    filterRange,
                    (uint)0,
                    result,
                    "Filter")!;
                success.Should().BeTrue();

                // Rows for 40 and 50 (A5/A6) are now hidden by the value-list filter.
                sheet.FilterHiddenRows.Should().Contain(5u).And.Contain(6u);

                sheet.GetValue(1, 2).Should().Be(
                    new NumberValue(60),
                    "SUBTOTAL(109,...) must immediately reflect the filtered-out rows, matching Excel");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: a plain quick Sort (no hidden rows involved) must still leave a SUM
    // formula over the sorted range showing the correct total, and must not throw/regress the
    // existing sort behavior now that a recalc call was added after it.
    [Fact]
    public void QuickSortAscending_StillSortsRangeAndKeepsDependentFormulaCorrect()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(30)); // A1
                sheet.SetCell(new CellAddress(sheetId, 2, 1), new NumberValue(10)); // A2
                sheet.SetCell(new CellAddress(sheetId, 3, 1), new NumberValue(20)); // A3
                sheet.SetFormula(new CellAddress(sheetId, 1, 2), "SUM(A1:A3)"); // B1

                var recalcMethod = typeof(MainWindow).GetMethod(
                    "RecalculateWorkbook", BindingFlags.Instance | BindingFlags.NonPublic, [])
                    ?? throw new MissingMethodException(nameof(MainWindow), "RecalculateWorkbook");
                recalcMethod.Invoke(window, []);

                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 1)); // A1:A3

                R49MainWindowTestHarness.Invoke(window, "SortAscButton_Click", null!, null!);

                sheet.GetValue(1, 1).Should().Be(new NumberValue(10));
                sheet.GetValue(2, 1).Should().Be(new NumberValue(20));
                sheet.GetValue(3, 1).Should().Be(new NumberValue(30));
                sheet.GetValue(1, 2).Should().Be(new NumberValue(60));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
