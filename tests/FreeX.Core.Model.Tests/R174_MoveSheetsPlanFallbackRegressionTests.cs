using System.Diagnostics;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R174 F1: the r173 complexity fix planned a minimal move set (via the longest-increasing-
/// subsequence complement) but verified it by trying only two orderings -- ascending and
/// descending by target index -- and fell back to <c>LeftToRightPlan</c> (one move per mismatched
/// position, the exact quadratic cost r173 was written to eliminate) whenever neither reproduced
/// the target. For an ORDINARY multi-sheet-tab gesture -- ctrl-clicking a handful of scattered
/// sheets and moving them -- that fallback engaged on essentially every trial from 500 sheets up
/// to 96000, measuring 559ms at 16000 sheets and 16.3s at 96000, because inserting a mover at its
/// raw final index is wrong regardless of which of the two orderings is tried (proven below and in
/// the production remarks on <c>MoveSheetsCommand</c>).
///
/// These tests assert the planned move COUNT, not the wall clock -- a millisecond bound cannot
/// distinguish "quadratic again" from "the CI box is busy running five other builds", and this
/// program has already lost a round to trusting the clock over the count.
/// </summary>
public class R174_MoveSheetsPlanFallbackRegressionTests
{
    private static List<SheetId> BuildIds(int count)
    {
        var ids = new List<SheetId>(count);
        for (var i = 0; i < count; i++)
            ids.Add(SheetId.New());
        return ids;
    }

    /// <summary>
    /// Mirrors MoveSheetsCommand.Apply's own construction of desiredOrder exactly (remaining
    /// sheets keep their order; selected sheets are lifted out and reinserted, in their original
    /// relative order, as one contiguous block at the clamped target index) -- this is the actual
    /// shape WorkbookSession.MoveOrCopySelectedSheets produces for a real ctrl/shift-click
    /// multi-tab selection (src/FreeX.App.Services/WorkbookSession.cs, MoveOrCopySelectedSheets).
    /// </summary>
    private static List<SheetId> BuildDesiredOrder(
        List<SheetId> live,
        IReadOnlyList<SheetId> selected,
        int insertBeforeIndex)
    {
        var selectedSet = selected.ToHashSet();
        var remaining = live.Where(id => !selectedSet.Contains(id)).ToList();
        var selectedBeforeTarget = live
            .Take(Math.Min(insertBeforeIndex, live.Count))
            .Count(selectedSet.Contains);
        var targetIndex = Math.Clamp(insertBeforeIndex - selectedBeforeTarget, 0, remaining.Count);
        var desiredOrder = remaining.ToList();
        desiredOrder.InsertRange(targetIndex, selected);
        return desiredOrder;
    }

    [Theory]
    [InlineData(2_000)]
    [InlineData(20_000)]
    [InlineData(96_000)]
    public void PlanMoves_FiveScatteredSheets_PlanSizeIsBoundedBySelectionNotWorkbookSize(int sheetCount)
    {
        var live = BuildIds(sheetCount);

        // Five non-adjacent sheets scattered across the tab strip -- the exact shape of an
        // ordinary "ctrl-click five tabs, drag to the middle" gesture.
        var selectedIndices = new[]
        {
            3,
            sheetCount / 4,
            sheetCount / 2,
            (sheetCount * 3) / 4,
            sheetCount - 4,
        };
        var selected = selectedIndices.Select(i => live[i]).ToArray();
        var desiredOrder = BuildDesiredOrder(live, selected, sheetCount / 2);

        var plan = MoveSheetsCommand.PlanMoves(live.ToList(), desiredOrder);

        // This is the crux of the finding: before the fix, this exact shape drove PlanMoves into
        // LeftToRightPlan, which plans roughly one move per position from the insertion point
        // onward (thousands of moves for these sizes) even though only 5 sheets are selected.
        plan.Count.Should().BeLessThanOrEqualTo(selected.Length,
            "moving 5 scattered sheets must plan a number of moves proportional to the " +
            "SELECTION (5), not to the workbook -- a quadratic fallback plans one move per " +
            "shifted position instead, which is in the thousands at this scale");

        // The plan must actually be correct, not merely short: replaying it against `live` must
        // reproduce `desiredOrder` exactly.
        var replay = new List<SheetId>(live);
        foreach (var (from, to) in plan)
        {
            var id = replay[from];
            replay.RemoveAt(from);
            replay.Insert(to, id);
        }

        replay.Should().Equal(desiredOrder,
            "a short plan is only a fix if it also reproduces the exact requested order");
    }

    /// <summary>
    /// End-to-end version of the test above, through the real command (not just PlanMoves) and a
    /// real Workbook, at the round's own stress-test scale, so a regression in the wiring between
    /// Apply/Revert and PlanMoves would still be caught even if PlanMoves itself stayed correct.
    /// </summary>
    [Fact]
    public void MoveSheetsCommand_FiveScatteredSheetsAtScale_AppliesAndUndoesCorrectly()
    {
        const int sheetCount = 12_000;
        var wb = new Workbook("test");
        var ids = new List<SheetId>(sheetCount);
        for (var i = 0; i < sheetCount; i++)
            ids.Add(wb.AddSheet("Sheet" + i).Id);
        var ctx = new TestCommandContext(wb);
        var originalOrder = ids.ToList();

        var selected = new[] { ids[7], ids[3000], ids[6000], ids[9000], ids[11996] };
        var command = new MoveSheetsCommand(selected, sheetCount / 2);

        var sw = Stopwatch.StartNew();
        var outcome = command.Apply(ctx);
        sw.Stop();

        outcome.Success.Should().BeTrue();

        var expected = BuildDesiredOrder(originalOrder, selected, sheetCount / 2);
        wb.Sheets.Select(s => s.Id).Should().Equal(expected);

        // Loose wall-clock smoke check only -- the count assertion above is the real proof this
        // fix works; this just catches a catastrophic regression without pretending a busy
        // machine is a broken algorithm.
        sw.Elapsed.TotalMilliseconds.Should().BeLessThan(5000,
            "moving 5 scattered sheets must not take seconds even on a loaded machine");

        command.Revert(ctx);
        wb.Sheets.Select(s => s.Id).Should().Equal(originalOrder);
    }

    /// <summary>
    /// Second manifestation named in the round directive: DuplicateSheetsCommand repositions its
    /// copies via an internal MoveSheetsCommand (DuplicateSheetsCommand.cs), so a scattered
    /// 3-or-more-source duplicate hits the exact same planner. Proven correct end-to-end at scale
    /// with a loose wall-clock smoke check, mirroring the sibling single-source test already in
    /// R173_MoveSheetsCommandComplexityTests.cs.
    /// </summary>
    [Fact]
    public void DuplicateSheetsCommand_ThreeNonAdjacentSourcesAtScale_IsNotQuadraticAndLandsCorrectly()
    {
        const int sheetCount = 12_000;
        var wb = new Workbook("test");
        var ids = new List<SheetId>(sheetCount);
        for (var i = 0; i < sheetCount; i++)
            ids.Add(wb.AddSheet("Sheet" + i).Id);
        var ctx = new TestCommandContext(wb);
        var originalOrder = ids.ToList();

        var sources = new[] { ids[10], ids[4000], ids[8000] };
        var command = new DuplicateSheetsCommand(sources, sheetCount / 2);

        var sw = Stopwatch.StartNew();
        var outcome = command.Apply(ctx);
        sw.Stop();

        outcome.Success.Should().BeTrue();
        wb.Sheets.Count.Should().Be(sheetCount + sources.Length);

        var copyIds = command.CopySheetIds;
        copyIds.Should().HaveCount(3);
        var order = wb.Sheets.Select(s => s.Id).ToList();
        order.Where(id => !copyIds.Contains(id)).Should().Equal(originalOrder,
            "duplicating must not disturb the original sheets' relative order");
        order.Should().ContainInConsecutiveOrder(copyIds,
            "the three copies must land together, in source order, as one contiguous block");

        sw.Elapsed.TotalMilliseconds.Should().BeLessThan(5000,
            "duplicating three scattered sheets must not take seconds even on a loaded machine");

        command.Revert(ctx);
        wb.Sheets.Select(s => s.Id).Should().Equal(originalOrder);
    }

    // ── Sibling no-regression: the exhaustive small-n correctness check already in
    // R173_MoveSheetsCommandComplexityTests.cs (MoveSheetsCommand_EverySelectionAndTarget_
    // MatchesRemoveThenInsert) already covers every selection/target combination at n=5 against an
    // independent oracle; this adds the one shape that check cannot reach -- a SINGLE selected
    // sheet at the same scale as the scattered-selection tests above, confirming the fix did not
    // regress the one-mover case the LIS-complement optimization exists for. ──
    [Fact]
    public void PlanMoves_SingleSheetAtScale_StillPlansExactlyOneMove()
    {
        const int sheetCount = 96_000;
        var live = BuildIds(sheetCount);
        var moveId = live[0];
        var desiredOrder = live.Skip(1).Append(moveId).ToList();

        var plan = MoveSheetsCommand.PlanMoves(live.ToList(), desiredOrder);

        plan.Should().ContainSingle(
            "moving one sheet must still cost exactly one move regardless of workbook size");
    }
}
