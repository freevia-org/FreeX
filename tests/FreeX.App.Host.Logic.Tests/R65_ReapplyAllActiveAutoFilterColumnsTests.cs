using System.Reflection;
using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R65-services-autofilter-6-2
/// (src/FreeX.App.Host/MainWindow.DataFilterCommands.cs, ReapplyAutoFilter).
///
/// Before the fix: Data &gt; Reapply only replayed the SINGLE most-recently-applied per-column
/// AutoFilter command (_lastAutoFilterCommandFactory) -- if a value filter was applied on one
/// column and then a different (condition/number) filter on ANOTHER column, Reapply only
/// re-evaluated the second column's criterion; the first column's filter kept whatever hidden-row
/// decision it had from when it was originally applied, even though the underlying data had since
/// changed. Real Excel's Reapply re-evaluates EVERY active AutoFilter criterion on the sheet. Worse,
/// a last-used Sort (also routed through the same remembered slot) could be replayed by "Reapply"
/// instead of a filter at all.
///
/// After the fix, every column's remembered filter command is tracked independently (keyed by its
/// absolute column) in _activeAutoFilterColumnFactories, ReapplyAutoFilter rebuilds + runs ALL of
/// them together, and Sort actions are never inserted into that map.
/// </summary>
public sealed class R65_ReapplyAllActiveAutoFilterColumnsTests
{
    [Fact]
    public void ReapplyAutoFilter_ReEvaluatesEveryActiveColumnFilter_NotJustTheLastApplied()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                // Header row.
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Region"));
                sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Amount"));

                // Row 2: fails the Amount>100 filter only.
                sheet.SetCell(new CellAddress(sheetId, 2, 1), new TextValue("West"));
                sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(50));
                // Row 3: fails the Region=West filter only.
                sheet.SetCell(new CellAddress(sheetId, 3, 1), new TextValue("East"));
                sheet.SetCell(new CellAddress(sheetId, 3, 2), new NumberValue(200));
                // Row 4: passes both filters.
                sheet.SetCell(new CellAddress(sheetId, 4, 1), new TextValue("West"));
                sheet.SetCell(new CellAddress(sheetId, 4, 2), new NumberValue(200));

                var range = new GridRange(
                    new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2)); // A1:B4
                window.SheetGrid.SelectedRange = range;
                sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);

                var regionFilter = new AutoFilterDialogResult(
                    AutoFilterSortDirection.None,
                    SelectedValues: ["West"],
                    SearchText: "",
                    CriteriaText: "");
                var amountFilter = new AutoFilterDialogResult(
                    AutoFilterSortDirection.None,
                    SelectedValues: [],
                    SearchText: "",
                    CriteriaText: ">100");

                ((bool)R49MainWindowTestHarness.Invoke(
                        window, "ApplyAutoFilterDialogResult", range, (uint)0, regionFilter, "Filter")!)
                    .Should().BeTrue();
                ((bool)R49MainWindowTestHarness.Invoke(
                        window, "ApplyAutoFilterDialogResult", range, (uint)1, amountFilter, "Filter")!)
                    .Should().BeTrue();

                sheet.FilterHiddenRows.Should().BeEquivalentTo(
                    [2u, 3u],
                    "row 2 fails the Amount filter and row 3 fails the Region filter");

                // Change the data so BOTH previously-failing rows now pass both criteria.
                sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(200)); // row 2 Amount now > 100
                sheet.SetCell(new CellAddress(sheetId, 3, 1), new TextValue("West")); // row 3 Region now West

                R49MainWindowTestHarness.Invoke(window, "ReapplyAutoFilter");

                sheet.FilterHiddenRows.Should().BeEmpty(
                    "Reapply must re-evaluate BOTH the Region value-list filter and the Amount " +
                    "condition filter against the current data, not just whichever was applied last");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: a Sort triggered from the AutoFilter dropdown must never be stored as
    // (or replayed by) Data > Reapply, and must not clobber the remembered filter for the column it
    // was run on -- the remembered filter must still be reapplied correctly afterwards.
    [Fact]
    public void ReapplyAutoFilter_SortActionDoesNotReplaceRememberedFilter_AndFilterStillReapplies()
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
                sheet.SetCell(new CellAddress(sheetId, 4, 1), new TextValue("West"));

                var range = new GridRange(
                    new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 1)); // A1:A4
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

                var sessionField = typeof(MainWindow).GetField(
                        "_filterWorkflowSession", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException(nameof(MainWindow), "_filterWorkflowSession");
                var session = (WorksheetFilterWorkflowSession)sessionField.GetValue(window)!;
                var rememberedPlan = session.CreateReapplyPlan(sheet);
                rememberedPlan.Should().NotBeNull();
                rememberedPlan!.DefinitionCount.Should().Be(1);

                // Sort the SAME column from the AutoFilter dropdown -- must not replace the
                // remembered filter factory with a Sort factory.
                var sortResult = new AutoFilterDialogResult(
                    AutoFilterSortDirection.Ascending,
                    SelectedValues: [],
                    SearchText: "",
                    CriteriaText: "");
                ((bool)R49MainWindowTestHarness.Invoke(
                        window, "ApplyAutoFilterDialogResult", range, (uint)0, sortResult, "Sort")!)
                    .Should().BeTrue();

                var planAfterSort = session.CreateReapplyPlan(sheet);
                planAfterSort.Should().NotBeNull();
                planAfterSort!.DefinitionCount.Should().Be(
                    1,
                    "a Sort action must not add or replace entries in the remembered per-column filter map");
                planAfterSort.Commands.Should().ContainSingle()
                    .Which.Should().BeOfType<FilterCommand>(
                        "the remembered command for this column must still be the value-list filter, not a Sort");

                // Force row 3's Region back to "West" regardless of how the Sort reordered rows, so
                // Reapply has a concrete change to react to.
                sheet.SetCell(new CellAddress(sheetId, 3, 1), new TextValue("West"));

                R49MainWindowTestHarness.Invoke(window, "ReapplyAutoFilter");

                sheet.FilterHiddenRows.Should().BeEmpty(
                    "Reapply must still re-run the remembered Region filter against current data, not a stale Sort");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
