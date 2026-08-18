using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R141-commands-slicer-timeline-multipivot-merge-loss: the r140 remediation's multi-target
/// growth-guard rollback (RestoreAllSlicerTargets / RestoreAllTimelineTargets) calls
/// PivotTableRefreshService.ClearRenderedRange for EVERY connected pivot target, including ones that
/// already refreshed successfully before a LATER target's growth conflict forced the whole command to
/// fail atomically. ClearRenderedRange unmerges every merged region overlapping the cleared footprint,
/// but the rollback only ever replays cell VALUES (AddPivotTableCommand.Restore) -- it never re-adds
/// the merges, so a rejected multi-pivot slicer/timeline change permanently destroyed merge formatting
/// that was never supposed to change, and because the command reports failure it is never pushed to the
/// undo stack, so there was no Undo to recover the loss either. Same shape independently duplicated in
/// SetTimelineRangeCommand's RestoreAllTimelineTargets, and in both commands' ordinary Revert (undo)
/// path for a SUCCESSFUL change.
///
/// This uses the same 2-connected-pivot growth-guard-conflict scaffold as
/// R140_RemediationPivotRefreshGrowthGuardAllCallSitesTests's
/// "...MultiPivotEarlierGrowthRolledBackWhenLaterPivotConflicts..." tests (the real user path: a slicer
/// or timeline driving Excel "Report Connections" against the real PivotTableRefreshService/RefreshGuarded
/// collaborator, not a stub), adding a merged region inside the EARLIER (successfully-refreshing) pivot's
/// rendered footprint to prove it survives the rollback.
/// </summary>
public sealed class R141_PivotSlicerTimelineMergeLossTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static void SeedThreeCategoryData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
    }

    private static PivotTableModel CreateNamedTwoCategoryPivot(Sheet sheet, string name, GridRange targetRange, IReadOnlyList<string>? selectedRowItems = null)
    {
        var pivot = new PivotTableModel
        {
            Name = name,
            CacheId = 1,
            SourceRange = Range(sheet, "A1", "B4"),
            TargetRange = targetRange,
            ReportLayout = PivotReportLayout.Tabular
        };
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: selectedRowItems));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        return pivot;
    }

    // ── SetSlicerSelectionCommand ────────────────────────────────────────────────────────────────

    [Fact]
    public void SetSlicerSelectionCommand_MultiPivotRollback_RestoresMergedRegionOnEarlierSucceededPivot()
    {
        var workbook = new Workbook("SlicerMergeLossTest");
        var sheet = workbook.AddSheet("Data");
        SeedThreeCategoryData(sheet);
        var ctx = new TestCommandContext(workbook);

        // Same 2-connected-pivot shape as the r140 GAP(b) test: PivotTable1's growth path is blank (its
        // own refresh will succeed and commit), PivotTable2's growth path already holds unrelated content
        // (its refresh will conflict), forcing the whole command to fail atomically.
        var pivot1 = CreateNamedTwoCategoryPivot(sheet, "PivotTable1", Range(sheet, "D3", "F6"), selectedRowItems: ["A", "B"]);
        var pivot2 = CreateNamedTwoCategoryPivot(sheet, "PivotTable2", Range(sheet, "H3", "J6"), selectedRowItems: ["A", "B"]);
        sheet.PivotTables.Add(pivot1);
        sheet.PivotTables.Add(pivot2);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot1);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot2);
        pivot1.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));
        pivot2.LastRenderedRange.Should().Be(Range(sheet, "H3", "I6"));

        // A merged label cell sitting inside PivotTable1's already-rendered footprint -- exactly what
        // MergeAndCenterLabels/compact-layout row-label merges look like in a real pivot render.
        var mergedRegion = Range(sheet, "D4", "D5");
        sheet.AddMergedRegion(mergedRegion);
        sheet.IsMerged(Addr(sheet, "D4")).Should().BeTrue("test setup must actually create the merge");

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Category Slicer",
            CacheName = "Slicer_Category",
            SourcePivotTableName = "PivotTable1",
            ConnectedPivotTableNames = { "PivotTable2" },
            SourceFieldName = "Category",
            SelectedItems = { "A", "B" },
            SelectionCaptured = true
        });

        sheet.GetCell(Addr(sheet, "D7")).Should().BeNull();
        var conflictNote = Addr(sheet, "H7");
        sheet.SetCell(conflictNote, new TextValue("Notes: Q4 budget"));

        // Clearing the slicer's filter reveals "C" for BOTH connected pivots at once; PivotTable2's
        // growth conflicts, so the whole command must fail and roll back PivotTable1's already-committed
        // growth too.
        var command = new SetSlicerSelectionCommand("Category Slicer", []);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("overwrite");

        // PivotTable1's pre-Apply cell content must be back (already covered by the r140 test)...
        sheet.GetCell(Addr(sheet, "D4"))!.Value.Should().Be(new TextValue("A"));
        sheet.GetCell(Addr(sheet, "D5"))!.Value.Should().Be(new TextValue("B"));
        // ...AND the merged region that existed there before the rejected selection change must survive
        // the rollback -- this is the R141 bug: RestoreAllSlicerTargets's ClearRenderedRange call for
        // PivotTable1 dropped this merge, and nothing ever put it back.
        sheet.IsMerged(Addr(sheet, "D4")).Should().BeTrue("merged-region formatting must survive a rolled-back multi-pivot slicer change");
        sheet.MergedRegions.Should().Contain(mergedRegion);
    }

    /// <summary>
    /// Sibling: a merge that lives OUTSIDE every connected pivot's footprint must come through the
    /// same whole-command rollback completely untouched -- proving the fix's snapshot/restore is a
    /// faithful per-sheet round-trip, not a blunt instrument that could duplicate or otherwise disturb
    /// merges the rollback was never supposed to touch.
    /// </summary>
    [Fact]
    public void SetSlicerSelectionCommand_MultiPivotRollback_LeavesUnrelatedMergedRegionUntouched()
    {
        var workbook = new Workbook("SlicerMergeLossSiblingTest");
        var sheet = workbook.AddSheet("Data");
        SeedThreeCategoryData(sheet);
        var ctx = new TestCommandContext(workbook);

        var pivot1 = CreateNamedTwoCategoryPivot(sheet, "PivotTable1", Range(sheet, "D3", "F6"), selectedRowItems: ["A", "B"]);
        var pivot2 = CreateNamedTwoCategoryPivot(sheet, "PivotTable2", Range(sheet, "H3", "J6"), selectedRowItems: ["A", "B"]);
        sheet.PivotTables.Add(pivot1);
        sheet.PivotTables.Add(pivot2);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot1);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot2);

        // Nowhere near either pivot's rendered/growth footprint.
        var unrelatedRegion = Range(sheet, "A20", "B20");
        sheet.AddMergedRegion(unrelatedRegion);

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Category Slicer",
            CacheName = "Slicer_Category",
            SourcePivotTableName = "PivotTable1",
            ConnectedPivotTableNames = { "PivotTable2" },
            SourceFieldName = "Category",
            SelectedItems = { "A", "B" },
            SelectionCaptured = true
        });
        sheet.SetCell(Addr(sheet, "H7"), new TextValue("Notes: Q4 budget"));

        var command = new SetSlicerSelectionCommand("Category Slicer", []);
        command.Apply(ctx).Success.Should().BeFalse();

        sheet.MergedRegions.Should().ContainSingle(r => r == unrelatedRegion);
    }

    /// <summary>
    /// The same merge-loss bug independently affects ordinary Undo of a SUCCESSFUL selection change
    /// (Revert also calls ClearRenderedRange + cell-values-only restore). Single pivot, no growth
    /// conflict: the command succeeds, then Undo must put the pre-Apply merge back.
    /// </summary>
    [Fact]
    public void SetSlicerSelectionCommand_Revert_RestoresMergedRegionAfterSuccessfulSelectionChange()
    {
        var workbook = new Workbook("SlicerMergeLossRevertTest");
        var sheet = workbook.AddSheet("Data");
        SeedThreeCategoryData(sheet);
        var ctx = new TestCommandContext(workbook);

        // Unfiltered pivot (all 3 categories) so narrowing the slicer SHRINKS the footprint without
        // hitting the growth guard at all -- a plain, ordinary successful selection change.
        var pivot = CreateNamedTwoCategoryPivot(sheet, "PivotTable1", Range(sheet, "D3", "F7"));
        sheet.PivotTables.Add(pivot);
        PivotTableRefreshService.Refresh(workbook, sheet, pivot);
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E7"));

        var mergedRegion = Range(sheet, "D4", "D6");
        sheet.AddMergedRegion(mergedRegion);

        workbook.Slicers.Add(new SlicerModel
        {
            Name = "Category Slicer",
            CacheName = "Slicer_Category",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Category",
            SelectedItems = { "A", "B", "C" },
            SelectionCaptured = true
        });

        var command = new SetSlicerSelectionCommand("Category Slicer", ["A", "B"]);
        command.Apply(ctx).Success.Should().BeTrue();
        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E6"));
        sheet.IsMerged(Addr(sheet, "D4")).Should().BeFalse("the successful refresh itself legitimately re-renders the footprint and drops the old merge");

        command.Revert(ctx);

        pivot.LastRenderedRange.Should().Be(Range(sheet, "D3", "E7"));
        sheet.GetCell(Addr(sheet, "D4"))!.Value.Should().Be(new TextValue("A"));
        sheet.IsMerged(Addr(sheet, "D4")).Should().BeTrue("undoing a successful slicer selection change must restore merge formatting, not just cell values");
        sheet.MergedRegions.Should().Contain(mergedRegion);
    }

    // ── SetTimelineRangeCommand ──────────────────────────────────────────────────────────────────

    [Fact]
    public void SetTimelineRangeCommand_MultiPivotRollback_RestoresMergedRegionOnEarlierSucceededPivot()
    {
        var workbook = new Workbook("TimelineMergeLossTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Date"));
        sheet.SetCell(Addr(sheet, "C1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 5)));
        sheet.SetCell(Addr(sheet, "C2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), DateTimeValue.FromDateTime(new DateTime(2026, 1, 10)));
        sheet.SetCell(Addr(sheet, "C3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), DateTimeValue.FromDateTime(new DateTime(2026, 2, 2)));
        sheet.SetCell(Addr(sheet, "C4"), new NumberValue(30));
        var ctx = new TestCommandContext(workbook);

        PivotTableModel MakePivot(string name, GridRange targetRange)
        {
            var pivot = new PivotTableModel
            {
                Name = name,
                CacheId = 1,
                SourceRange = Range(sheet, "A1", "C4"),
                TargetRange = targetRange,
                ReportLayout = PivotReportLayout.Tabular
            };
            pivot.RowFields.Add(new PivotFieldModel(0));
            pivot.DataFields.Add(new PivotDataFieldModel(2, "Sum of Amount", "sum"));
            return pivot;
        }

        var pivot1 = MakePivot("PivotTable1", Range(sheet, "E3", "G6"));
        var pivot2 = MakePivot("PivotTable2", Range(sheet, "I3", "K6"));
        sheet.PivotTables.Add(pivot1);
        sheet.PivotTables.Add(pivot2);

        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            SourcePivotTableName = "PivotTable1",
            ConnectedPivotTableNames = { "PivotTable2" },
            SourceFieldName = "Date"
        });

        // Narrow both pivots to January only (A, B) first -- succeeds, shrinks both footprints by one row.
        var narrow = new SetTimelineRangeCommand("Date Timeline", "2026-01-01", "2026-01-31");
        narrow.Apply(ctx).Success.Should().BeTrue();
        pivot1.LastRenderedRange.Should().Be(Range(sheet, "E3", "F6"));
        pivot2.LastRenderedRange.Should().Be(Range(sheet, "I3", "J6"));

        // A merged label cell inside PivotTable1's already-rendered (post-narrow) footprint.
        var mergedRegion = Range(sheet, "E4", "E5");
        sheet.AddMergedRegion(mergedRegion);

        // PivotTable1's growth path (E7) is blank -- widening will succeed and commit for it.
        // PivotTable2's growth path (I7) already holds unrelated content -- widening will conflict.
        sheet.GetCell(Addr(sheet, "E7")).Should().BeNull();
        var conflictNote = Addr(sheet, "I7");
        sheet.SetCell(conflictNote, new TextValue("Notes: Q4 budget"));

        var widen = new SetTimelineRangeCommand("Date Timeline", "2026-01-01", "2026-02-28");
        var outcome = widen.Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("overwrite");
        sheet.GetCell(Addr(sheet, "E7")).Should().BeNull("PivotTable1's committed growth must be undone when a LATER connected pivot conflicts");
        sheet.GetCell(Addr(sheet, "E4"))!.Value.Should().Be(new TextValue("A"));
        sheet.GetCell(Addr(sheet, "E5"))!.Value.Should().Be(new TextValue("B"));
        sheet.IsMerged(Addr(sheet, "E4")).Should().BeTrue("merged-region formatting must survive a rolled-back multi-pivot timeline change");
        sheet.MergedRegions.Should().Contain(mergedRegion);
        sheet.GetCell(conflictNote)!.Value.Should().Be(new TextValue("Notes: Q4 budget"));
    }
}
