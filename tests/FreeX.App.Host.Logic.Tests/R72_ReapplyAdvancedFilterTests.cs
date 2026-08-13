using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R72-commands-sort-filter-4-3
/// (src/FreeX.App.Host/MainWindow.DataCommands.cs ApplyAdvancedFilterResult +
/// src/FreeX.App.Host/MainWindow.DataFilterCommands.cs ReapplyAutoFilter).
///
/// Before the fix: Data &gt; Reapply only knew about AutoFilter (_activeAutoFilterColumnFactories)
/// and was completely blind to an active IN-PLACE Advanced Filter (Data &gt; Advanced with no "Copy
/// to another location" destination) -- editing a row so it no longer matched the stored criteria
/// left that row visible until the user manually re-ran Data &gt; Advanced, and Reapply reported
/// "nothing to reapply" even when an in-place Advanced Filter really was active.
///
/// After the fix, ApplyAdvancedFilterResult remembers the list/criteria ranges of an in-place
/// Advanced Filter, and ReapplyAutoFilter re-runs it (together with any active AutoFilter columns)
/// as part of the same Reapply operation.
/// </summary>
public sealed class R72_ReapplyAdvancedFilterTests
{
    [Fact]
    public void ReapplyAutoFilter_InPlaceAdvancedFilterActive_ReRunsAgainstEditedData()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                // List range A1:B4 (header + 3 data rows).
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Region"));
                sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Amount"));
                sheet.SetCell(new CellAddress(sheetId, 2, 1), new TextValue("West"));
                sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(200));
                sheet.SetCell(new CellAddress(sheetId, 3, 1), new TextValue("East"));
                sheet.SetCell(new CellAddress(sheetId, 3, 2), new NumberValue(50));
                sheet.SetCell(new CellAddress(sheetId, 4, 1), new TextValue("West"));
                sheet.SetCell(new CellAddress(sheetId, 4, 2), new NumberValue(300));

                // Criteria range D1:D2 -- Region = West.
                sheet.SetCell(new CellAddress(sheetId, 1, 4), new TextValue("Region"));
                sheet.SetCell(new CellAddress(sheetId, 2, 4), new TextValue("West"));

                var listRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2));
                var criteriaRange = new GridRange(new CellAddress(sheetId, 1, 4), new CellAddress(sheetId, 2, 4));

                var advancedFilterResult = new AdvancedFilterDialogResult(
                    listRange, criteriaRange, CopyToCell: null, UniqueRecordsOnly: false);

                R49MainWindowTestHarness.Invoke(window, "ApplyAdvancedFilterResult", advancedFilterResult);

                // Row 3 (East) fails the Region=West criterion and is hidden in-place.
                sheet.FilterHiddenRows.Should().Contain(3u);
                sheet.FilterHiddenRows.Should().NotContain(2u).And.NotContain(4u);

                // Edit row 4 so it no longer matches (West -> East).
                sheet.SetCell(new CellAddress(sheetId, 4, 1), new TextValue("East"));

                R49MainWindowTestHarness.Invoke(window, "ReapplyAutoFilter");

                sheet.FilterHiddenRows.Should().Contain(
                    4u, "Reapply must re-run the in-place Advanced Filter against the edited data, hiding row 4 now that it no longer matches Region=West");
                sheet.FilterHiddenRows.Should().Contain(3u, "row 3 still fails the criterion");
                sheet.FilterHiddenRows.Should().NotContain(2u, "row 2 still matches Region=West");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: an AutoFilter-only Reapply (no Advanced Filter ever applied) must keep
    // working exactly as before.
    [Fact]
    public void ReapplyAutoFilter_AutoFilterOnly_StillReapplies()
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
                sheet.SetCell(new CellAddress(sheetId, 3, 1), new TextValue("East"));

                var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 1)); // A1:A3
                window.SheetGrid.SelectedRange = range;
                sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
                var regionFilter = new AutoFilterDialogResult(
                    AutoFilterSortDirection.None,
                    SelectedValues: ["West"],
                    SearchText: "",
                    CriteriaText: "");
                ((bool)R49MainWindowTestHarness.Invoke(
                        window, "ApplyAutoFilterDialogResult", range, (uint)0, regionFilter, "Filter")!)
                    .Should().BeTrue();

                sheet.FilterHiddenRows.Should().Contain(3u);

                sheet.SetCell(new CellAddress(sheetId, 3, 1), new TextValue("West"));
                R49MainWindowTestHarness.Invoke(window, "ReapplyAutoFilter");

                sheet.FilterHiddenRows.Should().BeEmpty("the AutoFilter-only Reapply path must still work unchanged");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: with no AutoFilter and no Advanced Filter ever applied, Reapply must
    // still report "nothing to reapply" (no exception, no FilterHiddenRows mutation).
    [Fact]
    public void ReapplyAutoFilter_NoActiveFilterOfEitherKind_DoesNothing()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));

                R49MainWindowTestHarness.Invoke(window, "ReapplyAutoFilter");

                sheet.FilterHiddenRows.Should().BeEmpty();
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
