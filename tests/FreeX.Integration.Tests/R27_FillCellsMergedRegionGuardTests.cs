using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// Regression test for R27-cut-copy-fill-remaining-1: Ctrl+D/Ctrl+R (FillCellsCommand) had no
/// merged-region guard, unlike its siblings AutofillCommand and MoveRangeCommand. Filling over a
/// selection that partially covers a merge (e.g. B1:B2 merged, fill B1:B4 down) wrote an
/// independent value directly into B2 -- the merge's hidden non-anchor cell -- desyncing the
/// merge's data model (real Excel refuses the whole operation). FillCellsCommand must now reject
/// any fill whose range overlaps a merged region, matching AutofillCommand's guard.
/// </summary>
public class R27_FillCellsMergedRegionGuardTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    [Fact]
    public void FillDown_RejectedWhenSelectionPartiallyCoversAMergedRegion()
    {
        var (_, sheet, ctx) = Setup();

        // B1:B2 is merged; B1 (the anchor) holds "X".
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(b1, Cell.FromValue(new TextValue("X")));
        sheet.AddMergedRegion(new GridRange(b1, b2));

        // Select B1:B4 (B3:B4 blank/unmerged) and Ctrl+D.
        var range = new GridRange(b1, new CellAddress(sheet.Id, 4, 2));
        var outcome = new FillCellsCommand(sheet.Id, range, FillCellsDirection.Down).Apply(ctx);

        outcome.Success.Should().BeFalse();

        // B2 must remain the merge's hidden non-anchor cell -- no independent "X" written into it.
        sheet.GetCell(b2).Should().BeNull();
        sheet.MergedRegions.Should().ContainSingle(r => r == new GridRange(b1, b2));
    }

    [Fact]
    public void FillRight_RejectedWhenSelectionPartiallyCoversAMergedRegion()
    {
        var (_, sheet, ctx) = Setup();

        // B1:C1 is merged (horizontal merge); B1 (the anchor) holds "Y".
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(b1, Cell.FromValue(new TextValue("Y")));
        sheet.AddMergedRegion(new GridRange(b1, c1));

        // Select B1:D1 (D1 blank/unmerged) and Ctrl+R.
        var range = new GridRange(b1, new CellAddress(sheet.Id, 1, 4));
        var outcome = new FillCellsCommand(sheet.Id, range, FillCellsDirection.Right).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.GetCell(c1).Should().BeNull();
    }

    [Fact]
    public void FillDown_StillSucceedsWhenNoMergedRegionIsInvolved()
    {
        // Sanity check: the existing sibling behavior (plain fill down, no merges anywhere) must
        // keep working -- this guard must not over-reject ordinary fills.
        var (_, sheet, ctx) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, Cell.FromValue(new TextValue("X")));

        var range = new GridRange(a1, new CellAddress(sheet.Id, 3, 1)); // A1:A3
        var outcome = new FillCellsCommand(sheet.Id, range, FillCellsDirection.Down).Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(2, 1).Should().Be(new TextValue("X"));
        sheet.GetValue(3, 1).Should().Be(new TextValue("X"));
    }
}
