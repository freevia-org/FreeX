using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r223: the outline Expand/Collapse family. Clicking the outline +/- gutter, or the 1/2/3 level
/// buttons, on a group that is already in the requested state is an everyday gesture -- and one of
/// the four commands already knew it. <c>CollapseColGroupCommand</c> reported IsNoOp on its
/// unresolvable-scope path; its three siblings had the same path and returned a plain success. That
/// asymmetry inside one family is what the round found.
/// <para>
/// The guards use the technique r221 arrived at: decide on the record the command already keeps.
/// Both snapshots exist for Revert and are taken before anything is touched, so comparing the live
/// sets against them at every exit says exactly whether the outline moved -- across all three
/// mutation paths, with no separate predicate to keep in step.
/// </para>
/// </summary>
public sealed class R223_OutlineGroupNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 2; row <= 4; row++)
            sheet.RowOutlineLevels[row] = 1;
        for (uint col = 2; col <= 4; col++)
            sheet.ColOutlineLevels[col] = 1;
        return (sheet, new TestCommandContext(workbook));
    }

    [Fact]
    public void ExpandingRowsThatAreAlreadyVisible_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        sheet.GroupHiddenRows.Should().BeEmpty();

        new ExpandRowGroupCommand(sheet.Id, 1).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ExpandingACollapsedRowGroup_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        new CollapseRowGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.GroupHiddenRows.Should().NotBeEmpty();

        var outcome = new ExpandRowGroupCommand(sheet.Id, 1).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.GroupHiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void CollapsingARowGroupTwice_ReportsNoOpTheSecondTime()
    {
        var (sheet, ctx) = Fixture();

        new CollapseRowGroupCommand(sheet.Id, 1).Apply(ctx)
            .IsNoOp.Should().BeFalse("the first collapse hides the detail rows");

        new CollapseRowGroupCommand(sheet.Id, 1).Apply(ctx)
            .IsNoOp.Should().BeTrue("there is nothing left to hide");
    }

    [Fact]
    public void ExpandingColumnsThatAreAlreadyVisible_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new ExpandColGroupCommand(sheet.Id, 1).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void CollapsingAColumnGroupTwice_ReportsNoOpTheSecondTime()
    {
        var (sheet, ctx) = Fixture();

        new CollapseColGroupCommand(sheet.Id, 1).Apply(ctx).IsNoOp.Should().BeFalse();
        new CollapseColGroupCommand(sheet.Id, 1).Apply(ctx).IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ExpandingASheetWithNoOutlineAtAll_ReportsNoOp()
    {
        // The path CollapseColGroupCommand already reported and its siblings did not: nothing at the
        // requested level, so the loops run over an empty set.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Plain");
        var ctx = new TestCommandContext(workbook);

        new ExpandRowGroupCommand(sheet.Id, 1).Apply(ctx).IsNoOp.Should().BeTrue();
        new ExpandColGroupCommand(sheet.Id, 1).Apply(ctx).IsNoOp.Should().BeTrue();
        new CollapseRowGroupCommand(sheet.Id, 1).Apply(ctx).IsNoOp.Should().BeTrue();
    }
}
