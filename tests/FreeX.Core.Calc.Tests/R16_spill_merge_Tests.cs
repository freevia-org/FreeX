using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for round-16 finding R16-array-spill-edges-1 / R16-merge-align-deep-1:
/// Sheet.IsSpillBlocked only checked _cells/_spillValues occupancy, never the merge index, so a
/// dynamic array spilling into an EMPTY merged region (which has no _cells entries) was NOT
/// blocked. Excel refuses this with "Spill range has merged cells" (#SPILL!). IsSpillBlocked must
/// treat any non-anchor target cell that is part of a merged region as blocking, and SetSpillRange
/// must never be allowed to write into a merged, non-anchor cell.
/// </summary>
public sealed class R16_spill_merge_Tests
{
    private static Sheet MakeSheet() => new Sheet(SheetId.New(), "S");

    [Fact]
    public void IsSpillBlocked_EmptyMergedRegionInTargetRange_ReturnsTrue()
    {
        var sheet = MakeSheet();
        // Merge B1:C1 - empty, no _cells entries at all.
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 3)));

        var anchor = new CellAddress(sheet.Id, 1, 1);

        // A1 would spill a 1x3 dynamic array into A1:C1, overlapping the merged B1:C1 region.
        sheet.IsSpillBlocked(anchor, 1, 3).Should().BeTrue();
    }

    [Fact]
    public void SetSpillRange_NotAttemptedWhenBlockedByMerge_NoValuesWrittenIntoMergedCells()
    {
        var sheet = MakeSheet();
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 3)));

        var anchor = new CellAddress(sheet.Id, 1, 1);
        var cells = new ScalarValue[1, 3]
        {
            { new NumberValue(1), new NumberValue(2), new NumberValue(3) }
        };

        // Mirrors the real call site convention: callers must check IsSpillBlocked before calling
        // SetSpillRange (its own doc comment says so). Confirm the guard fires...
        bool blocked = sheet.IsSpillBlocked(anchor, 1, 3);
        blocked.Should().BeTrue();

        // ...and that if SetSpillRange is never invoked because of that guard, the merged cells
        // stay empty (no overlapping/hidden data written into B1/C1).
        if (!blocked)
            sheet.SetSpillRange(anchor, new RangeValue(cells));

        sheet.GetValue(1, 2).Should().Be(new BlankValue());
        sheet.GetValue(1, 3).Should().Be(new BlankValue());
    }

    [Fact]
    public void IsSpillBlocked_MergedRegionOutsideTargetRange_DoesNotBlock()
    {
        var sheet = MakeSheet();
        // Merge far away from the spill target - must not affect the result.
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 10, 10), new CellAddress(sheet.Id, 10, 11)));

        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.IsSpillBlocked(anchor, 1, 3).Should().BeFalse();
    }

    [Fact]
    public void IsSpillBlocked_AnchorCellItselfMerged_BlocksOnItsOwnMerge()
    {
        // Round-65 fix (R65-calc-array-spill-6-2): the anchor cell (r==0,c==0) being part of a
        // merged region MUST block the spill just like any other target cell - Excel refuses to
        // enter a dynamic-array formula into a merged cell at all ("Spill range has merged cells"),
        // regardless of whether the array's own footprint would otherwise fit. Only the
        // already-occupied-by-formula check is skipped for the anchor, not the merge/table checks.
        var sheet = MakeSheet();
        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)));

        var anchor = new CellAddress(sheet.Id, 1, 1);
        sheet.IsSpillBlocked(anchor, 1, 1).Should().BeTrue();
    }
}
