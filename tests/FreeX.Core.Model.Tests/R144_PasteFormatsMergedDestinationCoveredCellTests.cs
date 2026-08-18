using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R144-paste-formats-merge-covered-cells: Paste Special &gt; Formats (and the arithmetic-Operation
/// format-edit path, both funneled through PasteFormatsCommand) wrote the pasted style directly
/// into every destination address, including a non-anchor (hidden/covered) cell of a pre-existing
/// merged region. MergeCellsCommand deliberately preserves each covered cell's own pre-merge style
/// so Unmerge can restore it -- PasteFormatsCommand was silently clobbering that hidden style,
/// which only became visible after Unmerge Cells. Matches the guard PasteCellsCommand/
/// PasteSpecialCellsCommand/EditCellsCommand already apply: only the merge's top-left anchor cell
/// is ever actually restyled.
/// </summary>
public sealed class R144_PasteFormatsMergedDestinationCoveredCellTests
{
    [Fact]
    public void PasteFormatsCommand_IntoMergedDestination_LeavesCoveredCellStyleUntouched()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        // Existing merge B2:C2 (anchor B2). C2 (covered) keeps its own pre-merge style hidden,
        // exactly like MergeCellsCommand leaves behind so Unmerge can restore it.
        var anchor = new CellAddress(sheet.Id, 2, 2);
        var covered = new CellAddress(sheet.Id, 2, 3);
        sheet.AddMergedRegion(new GridRange(anchor, covered));

        var preMergeCoveredStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(covered.Row, covered.Col, preMergeCoveredStyle);

        var pastedAnchorStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        var pastedCoveredStyle = wb.RegisterStyle(new CellStyle { Underline = true });

        var command = new PasteFormatsCommand(sheet.Id, [(anchor, pastedAnchorStyle), (covered, pastedCoveredStyle)]);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetCell(anchor)?.StyleId.Should().Be(pastedAnchorStyle, "the merge's anchor cell takes the pasted style");
        sheet.GetStyleOnly(covered.Row, covered.Col).Should().Be(preMergeCoveredStyle,
            "a merge's covered (non-anchor) cell must keep its own hidden pre-merge style, not the pasted one");

        // Undo must restore both the pre-paste style AND leave the merge region itself intact.
        command.Revert(ctx);
        sheet.GetStyleOnly(covered.Row, covered.Col).Should().Be(preMergeCoveredStyle);
        sheet.MergedRegions.Should().ContainSingle(r => r.Start.Equals(anchor) && r.End.Equals(covered));

        // Unmerging afterward must reveal the real pre-merge style, not the pasted-in one.
        sheet.RemoveMergedRegion(new GridRange(anchor, covered));
        sheet.GetStyleOnly(covered.Row, covered.Col).Should().Be(preMergeCoveredStyle);
    }

    // No-regression sibling: pasting Formats into an UNMERGED destination must still restyle
    // every cell normally -- the merge-anchor guard must only skip covered cells of an existing
    // merge, never a plain destination.
    [Fact]
    public void PasteFormatsCommand_IntoUnmergedDestination_RestylesEveryCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var addr1 = new CellAddress(sheet.Id, 5, 2);
        var addr2 = new CellAddress(sheet.Id, 5, 3);
        var style1 = wb.RegisterStyle(new CellStyle { Italic = true });
        var style2 = wb.RegisterStyle(new CellStyle { Underline = true });

        var command = new PasteFormatsCommand(sheet.Id, [(addr1, style1), (addr2, style2)]);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.GetCell(addr1)!.StyleId.Should().Be(style1);
        sheet.GetCell(addr2)!.StyleId.Should().Be(style2);

        command.Revert(ctx);
        sheet.GetCell(addr1).Should().BeNull();
        sheet.GetCell(addr2).Should().BeNull();
    }
}
