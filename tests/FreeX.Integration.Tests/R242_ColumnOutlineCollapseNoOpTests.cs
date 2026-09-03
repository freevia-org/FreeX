using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r242: SetColumnOutlineGroupCollapsedCommand -- an r208 tier-2 entry, and one of the pair the
/// original census deliberately split. Its ROW twin is judged sound because the caller passes
/// <c>!group.IsCollapsed</c>, a negation gate; the column one has no such caller guarantee, so
/// collapsing a group that is already collapsed reaches Apply and writes the state it already has.
/// <para>
/// The decision is the one r223 introduced for the Expand/Collapse commands in the same file, and
/// this is the third command there to use it: compare the live outline state against the snapshots
/// taken for Revert before anything moved.
/// </para>
/// </summary>
public sealed class R242_ColumnOutlineCollapseNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint col = 2; col <= 4; col++)
            sheet.ColOutlineLevels[col] = 1;
        return (sheet, new TestCommandContext(workbook));
    }

    [Fact]
    public void CollapsingAGroupThatIsAlreadyCollapsed_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new SetColumnOutlineGroupCollapsedCommand(sheet.Id, 2, 4, 1, collapsed: true).Apply(ctx)
            .IsNoOp.Should().BeFalse("the first collapse hides the columns");

        new SetColumnOutlineGroupCollapsedCommand(sheet.Id, 2, 4, 1, collapsed: true).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ExpandingACollapsedGroup_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        new SetColumnOutlineGroupCollapsedCommand(sheet.Id, 2, 4, 1, collapsed: true).Apply(ctx);

        var outcome = new SetColumnOutlineGroupCollapsedCommand(sheet.Id, 2, 4, 1, collapsed: false)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.GroupHiddenCols.Should().BeEmpty();
    }

    [Fact]
    public void ExpandingAGroupThatIsAlreadyExpanded_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new SetColumnOutlineGroupCollapsedCommand(sheet.Id, 2, 4, 1, collapsed: false).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }
}
