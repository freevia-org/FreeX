using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public class GroupCommandTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new SimpleCtx(wb));
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook => wb;
        public Sheet GetSheet(SheetId id) => wb.GetSheet(id)!;
    }

    [Fact]
    public void GroupRows_SetsOutlineLevel()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 5, 1).Apply(ctx);
        sheet.RowOutlineLevels[2].Should().Be(1);
        sheet.RowOutlineLevels[5].Should().Be(1);
    }

    [Fact]
    public void UngroupRows_ClearsOutlineLevel()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 5, 1).Apply(ctx);
        new GroupRowsCommand(sheet.Id, 2, 5, 0).Apply(ctx);
        sheet.RowOutlineLevels.Should().NotContainKey(2);
    }

    [Fact]
    public void GroupRows_Revert_RestoresPreviousLevels()
    {
        var (_, sheet, ctx) = Setup();
        var cmd = new GroupRowsCommand(sheet.Id, 2, 4, 1);
        cmd.Apply(ctx);
        cmd.Revert(ctx);
        sheet.RowOutlineLevels.Should().NotContainKey(2);
    }

    [Fact]
    public void CollapseRows_HidesGroupedRows()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new CollapseRowGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.GroupHiddenRows.Should().Contain(2).And.Contain(4);
        sheet.IsRowEffectivelyHidden(3).Should().BeTrue();
    }

    [Fact]
    public void ExpandRows_ShowsCollapsedRows()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new CollapseRowGroupCommand(sheet.Id, 1).Apply(ctx);
        new ExpandRowGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.GroupHiddenRows.Should().NotContain(2);
        sheet.IsRowEffectivelyHidden(2).Should().BeFalse();
    }

    [Fact]
    public void CollapseRows_Revert_RestoresVisibility()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        var collapseCmd = new CollapseRowGroupCommand(sheet.Id, 1);
        collapseCmd.Apply(ctx);
        collapseCmd.Revert(ctx);
        sheet.GroupHiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void GroupColumns_SetsOutlineLevel()
    {
        var (_, sheet, ctx) = Setup();
        new GroupColumnsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        sheet.ColOutlineLevels[2].Should().Be(1);
        sheet.ColOutlineLevels[4].Should().Be(1);
    }

    [Fact]
    public void CollapseColumns_HidesGroupedColumns()
    {
        var (_, sheet, ctx) = Setup();
        new GroupColumnsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new CollapseColGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.GroupHiddenCols.Should().Contain(2).And.Contain(4);
        sheet.IsColEffectivelyHidden(3).Should().BeTrue();
    }

    [Fact]
    public void ExpandColumns_ShowsCollapsedColumns()
    {
        var (_, sheet, ctx) = Setup();
        new GroupColumnsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new CollapseColGroupCommand(sheet.Id, 1).Apply(ctx);
        new ExpandColGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.GroupHiddenCols.Should().NotContain(2);
        sheet.IsColEffectivelyHidden(2).Should().BeFalse();
    }
}
