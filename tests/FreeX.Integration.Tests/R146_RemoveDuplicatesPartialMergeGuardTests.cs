using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R146-merged-structural-F1: RemoveDuplicateRowsCommand.Apply had no merge-overlap guard. Step 4
/// (row compaction) rewrites the full row content of every row inside the operated range
/// regardless of merge geometry, but step 5's merge remap only relocates merges FULLY CONTAINED in
/// the range -- leaving a merge that only PARTIALLY overlaps the range (e.g. starts above it)
/// untouched. That combination let a surviving row's real value get written into a cell the
/// straddling merge still marks as a covered, blank non-anchor cell: a live value invisible behind
/// the merge's anchor display, and desynced from the merge/value invariant. Verifies the fix: such
/// a command is now rejected outright (mirroring FillCellsCommand's "cannot partially cover a
/// merge" refusal) instead of silently corrupting the sheet.
/// </summary>
public sealed class R146_RemoveDuplicatesPartialMergeGuardTests
{
    [Fact]
    public void RemoveDuplicateRows_RangePartiallyOverlapsVerticalMerge_IsRejectedNotSilentlyCorrupted()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        // A1:A3 is a vertical merge (anchor A1 = "GroupX"); its rows 2-3 are covered cells.
        var mergeAnchor = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(mergeAnchor, Cell.FromValue(new TextValue("GroupX")));
        var mergedRegion = new GridRange(mergeAnchor, new CellAddress(sheet.Id, 3, 1));
        sheet.AddMergedRegion(mergedRegion);

        // Column B holds dedup keys for rows 1-6: k1,k2,k2(dup of row2),k4,k5,k6. Row3 (inside the
        // merge, a covered cell) is eliminated as a duplicate of row2.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new TextValue("k1")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new TextValue("k2")));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromValue(new TextValue("k2")));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromValue(new TextValue("RealValue4")));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), Cell.FromValue(new TextValue("k4")));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), Cell.FromValue(new TextValue("RealValue5")));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), Cell.FromValue(new TextValue("k5")));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 2), Cell.FromValue(new TextValue("k6")));

        // Selection A2:B6 starts one row below the merge's own top row, so it partially overlaps
        // (rows 2-3 of the merge fall inside the range; row 1 does not).
        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 6, 2));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        var outcome = command.Apply(ctx);

        // The command must refuse outright rather than proceed and hide a live value behind the
        // merge's covered cell.
        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().NotBeNullOrEmpty();

        // Nothing must have moved: the merge-covered cell A3 stays blank (not overwritten with
        // "RealValue4"), row4's data stays put, and the merge itself is untouched.
        sheet.GetValue(3, 1).Should().Be(BlankValue.Instance);
        sheet.GetValue(4, 1).Should().Be(new TextValue("RealValue4"));
        sheet.MergedRegions.Should().ContainSingle();
        sheet.MergedRegions.Should().Contain(mergedRegion);
    }

    [Fact]
    public void RemoveDuplicateRows_MergeFullyContainedInRange_StillCompactsNormally()
    {
        // Sibling/no-regression case: a merge FULLY inside the operated range (the case step 5's
        // remap already handled correctly, and R20_MergedRemoveDupTests covers in depth) must
        // continue to be accepted and remapped -- the new guard must not reject this shape too.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("Apple")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new TextValue("X")));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new TextValue("Apple")));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromValue(new TextValue("X")));

        var mergeAnchor = new CellAddress(sheet.Id, 4, 2);
        var mergeCovered = new CellAddress(sheet.Id, 4, 3);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromValue(new TextValue("Grape")));
        sheet.SetCell(mergeAnchor, Cell.FromValue(new TextValue("Yellow")));
        var mergedRegion = new GridRange(mergeAnchor, mergeCovered);
        sheet.AddMergedRegion(mergedRegion);

        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 4, 3));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        command.RemovedRowCount.Should().Be(1);
        sheet.GetValue(3, 1).Should().Be(new TextValue("Grape"));
        sheet.MergedRegions.Should().ContainSingle();
        sheet.MergedRegions.Should().Contain(new GridRange(
            new CellAddress(sheet.Id, 3, 2),
            new CellAddress(sheet.Id, 3, 3)));
    }
}
