using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R20-merged-cells-deep-1: pasting a block that overlaps an existing merged region used to write
/// real values into the merge's hidden non-anchor cells via <see cref="PasteCellsCommand"/>. The grid
/// only renders the merge anchor, so the user never sees the write happen, yet formulas referencing
/// the covered cell (or a later unmerge) would suddenly surface the hidden value. Excel instead only
/// ever writes to the merge anchor and leaves covered cells empty. Verifies the fix: pasting a
/// two-cell block over an existing B1:C1 merge (anchor B1) writes the pasted value only to the
/// anchor B1 and leaves the covered cell C1 empty, and that undo still cleanly restores prior state.
/// </summary>
public sealed class R20_merged_paste_Tests
{
    [Fact]
    public void PasteCellsCommand_OverMergedRegion_WritesOnlyToAnchor_LeavesCoveredCellEmpty()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        // Merge B1:C1; anchor B1 shows "Total", C1 is the hidden/covered non-anchor cell.
        var anchor = new CellAddress(sheet.Id, 1, 2);
        var covered = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(anchor, Cell.FromValue(new TextValue("Total")));
        sheet.AddMergedRegion(new GridRange(anchor, covered));

        // Copy two plain cells (10, 20) and paste them directly onto the merge's footprint (B1:C1),
        // mirroring the direct (non-tiled) 2-column paste path PasteCommandFactory takes when the
        // selection has auto-expanded to match the merge.
        var pastedAnchorCell = Cell.FromValue(new NumberValue(10));
        var pastedCoveredCell = Cell.FromValue(new NumberValue(20));
        var command = new PasteCellsCommand(sheet.Id,
        [
            (anchor, pastedAnchorCell),
            (covered, pastedCoveredCell)
        ]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetValue(anchor).Should().Be(new NumberValue(10));

        // The covered non-anchor cell must remain empty -- Excel never plants a real value there.
        sheet.GetCell(covered).Should().BeNull();
        sheet.GetValue(covered).Should().Be(BlankValue.Instance);

        command.Revert(ctx);

        sheet.GetValue(anchor).Should().Be(new TextValue("Total"));
        sheet.GetCell(covered).Should().BeNull();
    }
}
