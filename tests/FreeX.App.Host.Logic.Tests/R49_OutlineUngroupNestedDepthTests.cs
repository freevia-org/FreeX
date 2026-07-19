using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R49-commands-outline-group-3-2
/// (src/FreeX.App.Host/MainWindow.OutlineCommands.cs, CreateUngroupCommand).
///
/// Before the fix: Ungroup on a selection spanning three or more distinct nesting depths computed
/// ONE uniform target level (the deepest level found anywhere in the selection, minus one, via
/// OutlineGroupingPlanner.GetUngroupedOutlineLevel over the WHOLE selection) and force-set EVERY
/// row in the selection to that single level via GroupRowsCommand(..., preserveExistingHierarchy:
/// false). A row that was only ever grouped at a shallower level than the deepest one present got
/// bumped UP instead of being left alone or decremented -- e.g. selecting rows 3-9 across a 3-deep
/// nested outline (rows 2-19 @1, 5-15 @2, 8-12 @3) computed target level = 3-1 = 2 and force-set
/// EVERY row 3..9 to level 2, so rows 3-4 (only ever grouped at level 1) were WRONGLY deepened to
/// level 2 instead of being ungrouped to 0, and rows 5-7 (level 2) incorrectly stayed at 2 instead
/// of decrementing to 1.
///
/// After the fix, Ungroup splits the selection into contiguous same-level runs and calls the SHARED
/// OutlineGroupingPlanner.GetUngroupedOutlineLevel once per run (each run's own rows all share one
/// source level, so that call's result over just that run is exactly "this run's level minus one"),
/// matching Excel: Ungroup never increases any row's outline level. Reusing the shared helper (not
/// reimplementing its arithmetic locally) keeps this in agreement with FreeX.App.Avalonia's
/// identical Ungroup path, per FreeXBehaviorDedupSourceBoundaryTests.
/// OutlineAndDiagnosticsPolicies_AreSharedAcrossShells.
/// </summary>
public sealed class R49_OutlineUngroupNestedDepthTests
{
    [Fact]
    public void UngroupRowsBtn_Click_SelectionSpanningThreeNestingDepths_OnlyDecrementsEachRowsOwnLevel()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var ctx = new TestCommandContext(workbook);

                // Build the 3-deep nested outline from the finding's failure scenario.
                new GroupRowsCommand(sheetId, 2, 19, 1, preserveExistingHierarchy: true).Apply(ctx);
                new GroupRowsCommand(sheetId, 5, 15, 2, preserveExistingHierarchy: true).Apply(ctx);
                new GroupRowsCommand(sheetId, 8, 12, 3, preserveExistingHierarchy: true).Apply(ctx);

                // Select rows 3-9 (straddling all three depths) and click Ungroup.
                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 3, 1),
                    new CellAddress(sheetId, 9, 1));

                R49MainWindowTestHarness.Invoke(window, "UngroupRowsBtn_Click", null, null);

                // Rows 3-4 were ONLY ever grouped at level 1 -- Ungroup must fully ungroup them
                // (decrement to 0), never bump them UP to match the selection's deepest row.
                sheet.RowOutlineLevels.Should().NotContainKey(3u, "row 3 was only level 1 and must decrement to 0, not increase");
                sheet.RowOutlineLevels.Should().NotContainKey(4u, "row 4 was only level 1 and must decrement to 0, not increase");

                // Rows 5-7 were level 2 -> decrement to 1.
                sheet.RowOutlineLevels.Should().ContainKey(5u).WhoseValue.Should().Be(1);
                sheet.RowOutlineLevels.Should().ContainKey(6u).WhoseValue.Should().Be(1);
                sheet.RowOutlineLevels.Should().ContainKey(7u).WhoseValue.Should().Be(1);

                // Rows 8-9 were level 3 -> decrement to 2.
                sheet.RowOutlineLevels.Should().ContainKey(8u).WhoseValue.Should().Be(2);
                sheet.RowOutlineLevels.Should().ContainKey(9u).WhoseValue.Should().Be(2);

                // Rows outside the selection must be completely untouched.
                sheet.RowOutlineLevels.Should().ContainKey(2u).WhoseValue.Should().Be(1);
                for (uint r = 10; r <= 12; r++)
                    sheet.RowOutlineLevels.Should().ContainKey(r).WhoseValue.Should().Be(3);
                for (uint r = 13; r <= 15; r++)
                    sheet.RowOutlineLevels.Should().ContainKey(r).WhoseValue.Should().Be(2);
                for (uint r = 16; r <= 19; r++)
                    sheet.RowOutlineLevels.Should().ContainKey(r).WhoseValue.Should().Be(1);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: an ordinary single-depth Ungroup (no nesting at all) must still fully
    // ungroup the selected rows, exactly as before, and must not disturb an unrelated group
    // elsewhere on the sheet.
    [Fact]
    public void UngroupRowsBtn_Click_SingleDepthSelection_StillFullyUngroupsSelectedRows()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                var ctx = new TestCommandContext(workbook);

                new GroupRowsCommand(sheetId, 3, 6, 1).Apply(ctx);
                new GroupRowsCommand(sheetId, 20, 25, 1).Apply(ctx);

                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 3, 1),
                    new CellAddress(sheetId, 6, 1));

                R49MainWindowTestHarness.Invoke(window, "UngroupRowsBtn_Click", null, null);

                for (uint r = 3; r <= 6; r++)
                    sheet.RowOutlineLevels.Should().NotContainKey(r);

                // The unrelated group elsewhere on the sheet must survive untouched.
                for (uint r = 20; r <= 25; r++)
                    sheet.RowOutlineLevels.Should().ContainKey(r).WhoseValue.Should().Be(1);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
