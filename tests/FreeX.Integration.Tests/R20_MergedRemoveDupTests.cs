using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R20-merged-cells-deep-2: Remove Duplicates compacted surviving rows upward without adjusting
/// <see cref="Sheet.MergedRegions"/>, orphaning merges over vacated rows and splitting a merge
/// apart from its own (relocated) data. Verifies the fix: a merge over a row that survives (and
/// gets compacted upward) travels with its data to the new row, and a merge over a row that gets
/// removed as a duplicate is cleaned up rather than left as a phantom over now-blank rows. Also
/// verifies Undo restores the original merge layout.
/// </summary>
public sealed class R20_merged_removedup_Tests
{
    [Fact]
    public void RemoveDuplicateRows_MergeOverSurvivingRow_ShiftsWithCompactedData()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        // Row2 = ("Apple","X","Y") is an exact duplicate of Row3 = ("Apple","X","Y"), so Row3 is
        // removed. Row4 is unique and has B4:C4 merged (anchor B4 = "Yellow", C4 is the covered
        // cell); Row4 survives and is compacted up to row3 by the write-back loop.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("Apple")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new TextValue("X")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), Cell.FromValue(new TextValue("Y")));

        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new TextValue("Apple")));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), Cell.FromValue(new TextValue("X")));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), Cell.FromValue(new TextValue("Y")));

        var mergeAnchor = new CellAddress(sheet.Id, 4, 2); // B4
        var mergeCovered = new CellAddress(sheet.Id, 4, 3); // C4
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

        // Row4's data ("Grape","Yellow") compacted up to row3.
        sheet.GetValue(3, 1).Should().Be(new TextValue("Grape"));
        sheet.GetValue(3, 2).Should().Be(new TextValue("Yellow"));

        // The merge must have moved with its data: B3:C3 is now merged, and the OLD region
        // B4:C4 must no longer be registered (it would otherwise be a phantom merge sitting over
        // a now-blank, vacated row).
        var expectedNewRegion = new GridRange(
            new CellAddress(sheet.Id, 3, 2),
            new CellAddress(sheet.Id, 3, 3));
        sheet.MergedRegions.Should().ContainSingle();
        sheet.MergedRegions.Should().Contain(expectedNewRegion);
        sheet.MergedRegions.Should().NotContain(mergedRegion);

        // Undo must restore the original merge layout (B4:C4) and drop the shifted one.
        command.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle();
        sheet.MergedRegions.Should().Contain(mergedRegion);
        sheet.MergedRegions.Should().NotContain(expectedNewRegion);
        sheet.GetValue(4, 2).Should().Be(new TextValue("Yellow"));
    }

    [Fact]
    public void RemoveDuplicateRows_MergeOverRemovedDuplicateRow_IsDroppedNotOrphaned()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        // Row2 is unique; Row3 duplicates Row2 and is removed. Row3 itself carries a merge
        // (B3:C3) — since every row that merge covered was removed as a duplicate, the merge
        // must be dropped entirely rather than left dangling over the vacated trailing row.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("Apple")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new TextValue("X")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), Cell.FromValue(new TextValue("Y")));

        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new TextValue("Apple")));
        var removedMergeAnchor = new CellAddress(sheet.Id, 3, 2);
        var removedMergeCovered = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(removedMergeAnchor, Cell.FromValue(new TextValue("X")));
        sheet.SetCell(removedMergeCovered, Cell.FromValue(new TextValue("Y")));
        var removedRegion = new GridRange(removedMergeAnchor, removedMergeCovered);
        sheet.AddMergedRegion(removedRegion);

        var range = new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 3));
        var command = new RemoveDuplicateRowsCommand(sheet.Id, range);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        command.RemovedRowCount.Should().Be(1);

        // No merge should remain at all: row3 (and its merge) was removed as a duplicate, and
        // row2 (the survivor) was never merged.
        sheet.MergedRegions.Should().BeEmpty();

        // Undo restores the original merge.
        command.Revert(ctx);

        sheet.MergedRegions.Should().ContainSingle();
        sheet.MergedRegions.Should().Contain(removedRegion);
    }
}
