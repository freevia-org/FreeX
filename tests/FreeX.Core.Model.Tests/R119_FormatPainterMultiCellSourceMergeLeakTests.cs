using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// ---- R119-commands-format-painter-multicell-merge-leak -----------------------------------
// FormatPainterCommandFactory.Create only special-cased a merged source when the ENTIRE source
// selection collapsed to exactly one merged region. A multi-cell source selection that merely
// CONTAINED a merged region as part of a larger block fell into the generic tiling loop, which
// read a merge-covered (non-anchor) cell's own StyleId -- the hidden pre-merge leftover
// MergeCellsCommand.Apply deliberately preserves purely so a later Unmerge can restore it. That
// leaked invisible formatting onto the target and never recreated the merge shape there either.

public sealed class R119_FormatPainterMultiCellSourceMergeLeakTests
{
    [Fact]
    public void CreateApplyFormatPainterCommand_MultiCellSourceContainsMerge_PaintsAnchorStyleNotHiddenCoveredStyle()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // A1:A2 vertically merged. A1 is Bold; A2's blanked Cell still carries a hidden red-fill
        // StyleId from before the merge (preserved by MergeCellsCommand purely for Unmerge).
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var b2 = new CellAddress(sheet.Id, 2, 2);

        var boldStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        var hiddenRedStyle = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 0, 0) });
        sheet.SetStyleOnly(a2.Row, a2.Col, hiddenRedStyle);
        var mergeCommand = new MergeCellsCommand(sheet.Id, new GridRange(a1, a2));
        mergeCommand.Apply(ctx).Success.Should().BeTrue();
        // The anchor (A1) gets its Bold style AFTER merging, matching how a user would format the
        // merged block once it already exists.
        new ApplyStyleCommand(sheet.Id, new GridRange(a1, a1), StyleDiff.FromStyle(wb.GetStyle(boldStyle)))
            .Apply(ctx).Success.Should().BeTrue();
        // B column stays plain/unformatted.

        var sourceRange = new GridRange(a1, b2); // A1:B2, a 2x2 selection containing the A1:A2 merge
        var targetTopLeft = new CellAddress(sheet.Id, 1, 4);
        var targetBottomRight = new CellAddress(sheet.Id, 4, 5); // D1:E4 -- an exact 2-row vertical tiling
        var targetRange = new GridRange(targetTopLeft, targetBottomRight);

        var command = FormatPainterCommandFactory.Create(wb, sheet, sourceRange, targetRange);
        command.Apply(ctx).Success.Should().BeTrue();

        StyleId StyleAt(CellAddress addr) =>
            sheet.GetCell(addr)?.StyleId ?? sheet.GetStyleOnly(addr.Row, addr.Col) ?? StyleId.Default;

        // D1, D3 (mapped from the merge's covered address A2 under the old bug) must carry the
        // anchor's Bold style, never the hidden red leftover.
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var d2 = new CellAddress(sheet.Id, 2, 4);
        var d3 = new CellAddress(sheet.Id, 3, 4);
        var d4 = new CellAddress(sheet.Id, 4, 4);

        wb.GetStyle(StyleAt(d1)).Bold.Should().BeTrue();
        wb.GetStyle(StyleAt(d1)).FillColor.Should().BeNull("the anchor's own style has no fill");
        wb.GetStyle(StyleAt(d2)).FillColor.Should().BeNull(
            "D2 must never receive A2's hidden pre-merge red fill -- that formatting was never visible to the user");
        wb.GetStyle(StyleAt(d3)).Bold.Should().BeTrue();
        wb.GetStyle(StyleAt(d4)).FillColor.Should().BeNull(
            "D4 must never receive A2's hidden pre-merge red fill -- that formatting was never visible to the user");
    }

    [Fact]
    public void CreateApplyFormatPainterCommand_MultiCellSourceContainsMerge_RecreatesMergeShapeInTarget()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        sheet.AddMergedRegion(new GridRange(a1, a2));

        var sourceRange = new GridRange(a1, new CellAddress(sheet.Id, 2, 2)); // A1:B2
        var targetTopLeft = new CellAddress(sheet.Id, 1, 4);
        var targetBottomRight = new CellAddress(sheet.Id, 2, 5); // D1:E2 -- one exact tile
        var targetRange = new GridRange(targetTopLeft, targetBottomRight);

        var command = FormatPainterCommandFactory.Create(wb, sheet, sourceRange, targetRange);
        command.Apply(ctx).Success.Should().BeTrue();

        var expectedTargetMerge = new GridRange(
            new CellAddress(sheet.Id, 1, 4),
            new CellAddress(sheet.Id, 2, 4)); // D1:D2, the same relative position as A1:A2 in the source
        sheet.MergedRegions.Should().Contain(expectedTargetMerge);
    }

    // ---- No-regression sibling: a multi-cell source with NO merge at all must still tile
    // per-cell exactly as before -- the merge-awareness added above must not affect ordinary
    // multi-cell painting when there is nothing merged in the source selection.
    [Fact]
    public void CreateApplyFormatPainterCommand_MultiCellSourceWithNoMerge_StillTilesPerCell_NoRegression()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sourceTopLeft = new CellAddress(sheet.Id, 1, 1);
        var sourceBottomRight = new CellAddress(sheet.Id, 2, 2);
        var targetTopLeft = new CellAddress(sheet.Id, 4, 4);
        var targetBottomRight = new CellAddress(sheet.Id, 5, 5);
        var red = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 199, 206) });
        var green = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(198, 239, 206) });
        var blue = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(189, 215, 238) });
        var yellow = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 235, 156) });
        sheet.SetStyleOnly(1, 1, red);
        sheet.SetStyleOnly(1, 2, green);
        sheet.SetStyleOnly(2, 1, blue);
        sheet.SetStyleOnly(2, 2, yellow);

        var command = FormatPainterCommandFactory.Create(
            wb,
            sheet,
            new GridRange(sourceTopLeft, sourceBottomRight),
            new GridRange(targetTopLeft, targetBottomRight));

        command.Apply(ctx).Success.Should().BeTrue();

        StyleId StyleAt(uint row, uint col) =>
            sheet.GetCell(new CellAddress(sheet.Id, row, col))?.StyleId
            ?? sheet.GetStyleOnly(row, col)
            ?? StyleId.Default;

        StyleAt(4, 4).Should().Be(red);
        StyleAt(4, 5).Should().Be(green);
        StyleAt(5, 4).Should().Be(blue);
        StyleAt(5, 5).Should().Be(yellow);
        sheet.MergedRegions.Should().BeEmpty("no merge existed in the source, so none should appear in the target");
    }
}
