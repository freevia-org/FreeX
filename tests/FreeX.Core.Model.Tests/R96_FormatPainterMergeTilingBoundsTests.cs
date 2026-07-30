using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// R96-commands-formatpainter-mergetiling-bounds-2: FormatPainterCommandFactory's merge-tiling path
// (ExpandTargetToMergeMultiple + AddTiledMerges) rounds a single-cell target's row/column footprint
// UP to a whole multiple of the source merge's own span, anchored at the target's own start. When
// that target start sits close enough to the worksheet's row/column ceiling that the rounded-up
// footprint would spill past CellAddress.MaxRow/MaxCol, nothing validated the result -- unlike
// CopyRangeCommand/MoveRangeCommand, which reject an out-of-bounds destination via
// WorksheetBounds.TryGetRectangleEnd before ever touching the sheet.
public sealed class R96_FormatPainterMergeTilingBoundsTests
{
    [Fact]
    public void CreateApplyFormatPainterCommand_MergeTilingWouldExceedMaxRow_FailsAndAddsNoMergedRegion()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Source is a 1x2 vertical merge (rows 1-2, col 1).
        var sourceTop = new CellAddress(sheet.Id, 1, 1);
        var sourceBottom = new CellAddress(sheet.Id, 2, 1);
        var mergeRange = new GridRange(sourceTop, sourceBottom);
        sheet.AddMergedRegion(mergeRange);

        var anchorStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(sourceTop.Row, sourceTop.Col, anchorStyle);

        // Target is a single cell sitting exactly on the worksheet's last row. Expanding a 1-row
        // target up to the merge's own 2-row span (CeilToMultiple(1, 2) == 2) pushes the expanded
        // range's End.Row to CellAddress.MaxRow + 1 -- one past the sheet's row ceiling.
        var target = new CellAddress(sheet.Id, CellAddress.MaxRow, 1);
        var targetRange = new GridRange(target, target);

        var command = FormatPainterCommandFactory.Create(wb, sheet, mergeRange, targetRange);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeFalse();

        // The out-of-bounds merge must never have reached the sheet, and the whole operation must
        // be atomic: only the original 1x2 source merge should remain -- no partially-applied style
        // or out-of-bounds region left behind by the failed composite.
        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(mergeRange);
        sheet.MergedRegions.Should().NotContain(r => r.End.Row > CellAddress.MaxRow || r.End.Col > CellAddress.MaxCol);
    }

    [Fact]
    public void CreateApplyFormatPainterCommand_MergeTilingWithinBounds_StillSucceeds_NoRegression()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Same 1x2 vertical merge source, but this time painted onto an ordinary in-bounds target
        // near the top of the sheet -- the sibling behavior that must keep working.
        var sourceTop = new CellAddress(sheet.Id, 1, 1);
        var sourceBottom = new CellAddress(sheet.Id, 2, 1);
        var mergeRange = new GridRange(sourceTop, sourceBottom);
        sheet.AddMergedRegion(mergeRange);

        var anchorStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(sourceTop.Row, sourceTop.Col, anchorStyle);

        var target = new CellAddress(sheet.Id, 10, 1);
        var targetRange = new GridRange(target, target);

        var command = FormatPainterCommandFactory.Create(wb, sheet, mergeRange, targetRange);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();

        var expectedMerge = new GridRange(target, new CellAddress(sheet.Id, 11, 1));
        sheet.MergedRegions.Should().Contain(expectedMerge);
    }
}
