using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R72-commands-sort-filter-4-1
/// (src/FreeX.App.Host/MainWindow.Viewport.cs, UpdateViewport/BuildActiveAutoFilterColumns).
///
/// Before the fix: the WPF host never populated GridView.ActiveAutoFilterColumns, so
/// GridView.Rendering.AutoFilter.cs's `ActiveAutoFilterColumns?.Contains(...)` check always saw
/// null, and the AutoFilter dropdown arrow never showed the filtered-state (funnel) icon for a
/// filtered column -- even though the column really was filtered (FilterHiddenRows reflected it).
///
/// After the fix, UpdateViewport derives the active column set from the sheet's AutoFilter model
/// (sheet.AutoFilter.FilterColumns, falling back to a structured table's own FilterColumns) and
/// feeds it to SheetGrid.ActiveAutoFilterColumns.
/// </summary>
public sealed class R72_ActiveAutoFilterColumnsTests
{
    [Fact]
    public void UpdateViewport_ColumnHasActiveFilter_IsIncludedInActiveAutoFilterColumns()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Region"));
                sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Amount"));
                sheet.SetCell(new CellAddress(sheetId, 2, 1), new TextValue("West"));
                sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(10));
                sheet.SetCell(new CellAddress(sheetId, 3, 1), new TextValue("East"));
                sheet.SetCell(new CellAddress(sheetId, 3, 2), new NumberValue(20));

                var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)); // A1:B3
                window.SheetGrid.SelectedRanges = null;
                window.SheetGrid.SelectedRange = range;

                // Turn on AutoFilter over A1:B3.
                R49MainWindowTestHarness.Invoke(window, "FilterButton_Click", null!, null!);

                // Filter column B (offset 1) to a single value.
                var amountFilter = new AutoFilterDialogResult(
                    AutoFilterSortDirection.None,
                    SelectedValues: ["10"],
                    SearchText: "",
                    CriteriaText: "");
                ((bool)R49MainWindowTestHarness.Invoke(
                        window, "ApplyAutoFilterDialogResult", range, (uint)1, amountFilter, "Filter")!)
                    .Should().BeTrue();

                R49MainWindowTestHarness.Invoke(window, "UpdateViewport");

                window.SheetGrid.ActiveAutoFilterColumns.Should().NotBeNull();
                window.SheetGrid.ActiveAutoFilterColumns!.Should().Contain(
                    1u, "column B (offset 1) carries the active Amount filter");
                window.SheetGrid.ActiveAutoFilterColumns!.Should().NotContain(
                    0u, "column A (offset 0) has no filter criterion of its own");

                // Clearing the filter must remove column B from the active set again.
                R49MainWindowTestHarness.Invoke(window, "ClearFilterButton_Click", null!, null!);
                R49MainWindowTestHarness.Invoke(window, "UpdateViewport");

                (window.SheetGrid.ActiveAutoFilterColumns is null ||
                 window.SheetGrid.ActiveAutoFilterColumns!.Count == 0)
                    .Should().BeTrue("clearing the filter must remove the column from the active set");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: a sheet with AutoFilter turned on but no column ever filtered must not
    // report any column as active.
    [Fact]
    public void UpdateViewport_AutoFilterOnNoColumnFiltered_ActiveAutoFilterColumnsIsEmpty()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Region"));
                sheet.SetCell(new CellAddress(sheetId, 2, 1), new TextValue("West"));

                var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 1)); // A1:A2
                window.SheetGrid.SelectedRanges = null;
                window.SheetGrid.SelectedRange = range;

                R49MainWindowTestHarness.Invoke(window, "FilterButton_Click", null!, null!);
                R49MainWindowTestHarness.Invoke(window, "UpdateViewport");

                (window.SheetGrid.ActiveAutoFilterColumns is null ||
                 window.SheetGrid.ActiveAutoFilterColumns!.Count == 0)
                    .Should().BeTrue("no column has an active filter criterion, so none should be reported active");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
