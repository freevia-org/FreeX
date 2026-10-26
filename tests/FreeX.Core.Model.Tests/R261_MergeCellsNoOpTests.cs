using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r261: MergeCells. r232 recorded it as "net effect nil, but establishing that means reasoning
/// through five loops rather than adding a guard". The post-hoc form reasons through nothing: it
/// compares the record of what the loops did.
///
/// <para>RefreshPivotTable was attempted in the same round and REVERTED -- see the round notes: its
/// decision has a clause that never settles in a fixture with a real pivot cache, and shipping a
/// guard whose no-op direction cannot be demonstrated is what this program keeps declining.</para>
/// </summary>
public sealed class R261_MergeCellsNoOpTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static (Sheet Sheet, TestCommandContext Ctx) SetUp()
    {
        var wb = new Workbook("R261");
        return (wb.AddSheet("Sheet1"), new TestCommandContext(wb));
    }

    [Fact]
    public void MergeCells_OverARangeAlreadyMergedExactlyThatWayIsANoOp()
    {
        var (sheet, ctx) = SetUp();
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Title"));
        var range = Range(sheet, "A1", "C1");

        new MergeCellsCommand(sheet.Id, range).Apply(ctx)
            .IsNoOp.Should().BeFalse("the range was not merged");

        new MergeCellsCommand(sheet.Id, range).Apply(ctx)
            .IsNoOp.Should().BeTrue(
                "the existing region is absorbed and re-added, and the covered cells are already blank");
        sheet.MergedRegions.Should().ContainSingle();
    }

    [Fact]
    public void MergeCells_OverUnmergedCellsWithContentIsNotANoOp()
    {
        var (sheet, ctx) = SetUp();
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Title"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("covered"));

        new MergeCellsCommand(sheet.Id, Range(sheet, "A1", "C1")).Apply(ctx)
            .IsNoOp.Should().BeFalse("B1's text is blanked by the merge");
        sheet.GetValue(1, 2).Should().Be(BlankValue.Instance);
    }

    /// <summary>
    /// The comment half of the decision, which no cell comparison can see: every covered cell is
    /// already blank, so only the migration of the note onto the anchor distinguishes this from a
    /// no-op.
    /// </summary>
    [Fact]
    public void MergeCells_ThatMigratesACommentOntoTheAnchorIsNotANoOp()
    {
        var (sheet, ctx) = SetUp();
        var range = Range(sheet, "A1", "C1");
        new MergeCellsCommand(sheet.Id, range).Apply(ctx);

        // Unmerge-free re-merge: the region already exists and every covered cell is blank, so the
        // comment is the only thing left that the second merge can move.
        sheet.Comments[Addr(sheet, "B1")] = "check this";

        new MergeCellsCommand(sheet.Id, range).Apply(ctx)
            .IsNoOp.Should().BeFalse("the note moves from B1 onto the anchor A1");
        sheet.Comments.ContainsKey(Addr(sheet, "A1")).Should().BeTrue();
        sheet.Comments.ContainsKey(Addr(sheet, "B1")).Should().BeFalse();
    }
}
