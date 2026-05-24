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
    public void GroupRows_RejectsProtectedSheetWithoutFormatRowsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;

        var outcome = new GroupRowsCommand(sheet.Id, 2, 5, 1).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.RowOutlineLevels.Should().BeEmpty();
    }

    [Fact]
    public void GroupRows_AllowsProtectedSheetWithFormatRowsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatRows);

        var outcome = new GroupRowsCommand(sheet.Id, 2, 5, 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
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
    public void CollapseRows_RejectsProtectedSheetWithoutFormatRowsPermission()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        sheet.IsProtected = true;

        var outcome = new CollapseRowGroupCommand(sheet.Id, 1).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GroupHiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void CollapseRows_AllowsProtectedSheetWithFormatRowsPermission()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatRows);

        var outcome = new CollapseRowGroupCommand(sheet.Id, 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GroupHiddenRows.Should().Contain(2).And.Contain(4);
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
    public void ExpandRows_RejectsProtectedSheetWithoutFormatRowsPermission()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new CollapseRowGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.IsProtected = true;

        var outcome = new ExpandRowGroupCommand(sheet.Id, 1).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GroupHiddenRows.Should().Contain(2).And.Contain(4);
    }

    [Fact]
    public void ExpandRows_AllowsProtectedSheetWithFormatRowsPermission()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new CollapseRowGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatRows);

        var outcome = new ExpandRowGroupCommand(sheet.Id, 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GroupHiddenRows.Should().BeEmpty();
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
    public void GroupColumns_RejectsProtectedSheetWithoutFormatColumnsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;

        var outcome = new GroupColumnsCommand(sheet.Id, 2, 4, 1).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.ColOutlineLevels.Should().BeEmpty();
    }

    [Fact]
    public void GroupColumns_AllowsProtectedSheetWithFormatColumnsPermission()
    {
        var (_, sheet, ctx) = Setup();
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatColumns);

        var outcome = new GroupColumnsCommand(sheet.Id, 2, 4, 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
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
    public void CollapseColumns_RejectsProtectedSheetWithoutFormatColumnsPermission()
    {
        var (_, sheet, ctx) = Setup();
        new GroupColumnsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        sheet.IsProtected = true;

        var outcome = new CollapseColGroupCommand(sheet.Id, 1).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GroupHiddenCols.Should().BeEmpty();
    }

    [Fact]
    public void CollapseColumns_AllowsProtectedSheetWithFormatColumnsPermission()
    {
        var (_, sheet, ctx) = Setup();
        new GroupColumnsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatColumns);

        var outcome = new CollapseColGroupCommand(sheet.Id, 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GroupHiddenCols.Should().Contain(2).And.Contain(4);
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

    [Fact]
    public void ExpandColumns_RejectsProtectedSheetWithoutFormatColumnsPermission()
    {
        var (_, sheet, ctx) = Setup();
        new GroupColumnsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new CollapseColGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.IsProtected = true;

        var outcome = new ExpandColGroupCommand(sheet.Id, 1).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.GroupHiddenCols.Should().Contain(2).And.Contain(4);
    }

    [Fact]
    public void ExpandColumns_AllowsProtectedSheetWithFormatColumnsPermission()
    {
        var (_, sheet, ctx) = Setup();
        new GroupColumnsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new CollapseColGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.FormatColumns);

        var outcome = new ExpandColGroupCommand(sheet.Id, 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GroupHiddenCols.Should().BeEmpty();
    }

    [Fact]
    public void UngroupRows_WhileCollapsed_ShowsRows()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new CollapseRowGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.IsRowEffectivelyHidden(3).Should().BeTrue();

        new GroupRowsCommand(sheet.Id, 2, 4, 0).Apply(ctx);

        sheet.IsRowEffectivelyHidden(3).Should().BeFalse();
        sheet.RowOutlineLevels.Should().NotContainKey(3);
    }

    [Fact]
    public void UngroupRows_WhileCollapsed_Revert_RestoresGroupAndHiddenState()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new CollapseRowGroupCommand(sheet.Id, 1).Apply(ctx);

        var ungroupCmd = new GroupRowsCommand(sheet.Id, 2, 4, 0);
        ungroupCmd.Apply(ctx);
        ungroupCmd.Revert(ctx);

        sheet.RowOutlineLevels[3].Should().Be(1);
        sheet.IsRowEffectivelyHidden(3).Should().BeTrue();
    }

    [Fact]
    public void GroupRows_InvalidLevel_Throws()
    {
        var (_, sheet, _) = Setup();
        var act = () => new GroupRowsCommand(sheet.Id, 1, 3, 9);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
