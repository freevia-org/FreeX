using System.Diagnostics;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public class GroupCommandTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
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
    public void GroupRows_AcrossExistingSiblingGroups_CreatesOuterParentAndPreservesSubgroups()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 3, 1).Apply(ctx);
        new GroupRowsCommand(sheet.Id, 5, 6, 1).Apply(ctx);

        var command = new GroupRowsCommand(sheet.Id, 2, 6, 2, preserveExistingHierarchy: true);
        command.Apply(ctx);

        sheet.RowOutlineLevels.Should().BeEquivalentTo(new Dictionary<uint, int>
        {
            [2] = 2,
            [3] = 2,
            [4] = 1,
            [5] = 2,
            [6] = 2
        });

        new CollapseRowGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.GroupHiddenRows.Should().BeEquivalentTo([2u, 3u, 4u, 5u, 6u]);

        new ExpandRowGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.GroupHiddenRows.Should().BeEmpty();

        command.Revert(ctx);

        sheet.RowOutlineLevels.Should().BeEquivalentTo(new Dictionary<uint, int>
        {
            [2] = 1,
            [3] = 1,
            [5] = 1,
            [6] = 1
        });
    }

    [Fact]
    public void GroupRows_OuterFirstThenSubgroup_RemainsNested()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 6, 1, preserveExistingHierarchy: true).Apply(ctx);

        new GroupRowsCommand(sheet.Id, 3, 4, 2, preserveExistingHierarchy: true).Apply(ctx);

        sheet.RowOutlineLevels.Should().BeEquivalentTo(new Dictionary<uint, int>
        {
            [2] = 1,
            [3] = 2,
            [4] = 2,
            [5] = 1,
            [6] = 1
        });
    }

    [Fact]
    public void CollapseRows_HidesGroupedRows()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new CollapseRowGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.GroupHiddenRows.Should().Contain(2).And.Contain(4);
        sheet.CollapsedAnchorRows.Should().ContainSingle().Which.Should().Be(5);
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
        sheet.CollapsedAnchorRows.Should().BeEmpty();
        sheet.IsRowEffectivelyHidden(2).Should().BeFalse();
    }

    [Fact]
    public void SetRowOutlineGroupCollapsed_CollapsesOnlyRequestedRangeAndReverts()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new GroupRowsCommand(sheet.Id, 8, 9, 1).Apply(ctx);

        var command = new SetRowOutlineGroupCollapsedCommand(sheet.Id, 2, 4, 1, collapsed: true);
        command.Apply(ctx);

        sheet.GroupHiddenRows.Should().BeEquivalentTo([2u, 3u, 4u]);
        sheet.GroupHiddenRows.Should().NotContain(8u);
        sheet.CollapsedAnchorRows.Should().ContainSingle().Which.Should().Be(5u);

        command.Revert(ctx);

        sheet.GroupHiddenRows.Should().BeEmpty();
        sheet.CollapsedAnchorRows.Should().BeEmpty();
    }

    [Fact]
    public void SetRowOutlineGroupCollapsed_ExpandsRequestedNestedLevelOnly()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 5, 1).Apply(ctx);
        new GroupRowsCommand(sheet.Id, 3, 4, 2).Apply(ctx);
        new SetRowOutlineGroupCollapsedCommand(sheet.Id, 2, 5, 1, collapsed: true).Apply(ctx);

        var command = new SetRowOutlineGroupCollapsedCommand(sheet.Id, 3, 4, 2, collapsed: false);
        command.Apply(ctx);

        sheet.GroupHiddenRows.Should().Contain(2u).And.Contain(5u);
        sheet.GroupHiddenRows.Should().NotContain(3u);
        sheet.GroupHiddenRows.Should().NotContain(4u);
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
        sheet.CollapsedAnchorRows.Should().BeEmpty();
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
    public void GroupColumns_AcrossExistingSiblingGroups_CreatesOuterParentAndPreservesSubgroups()
    {
        var (_, sheet, ctx) = Setup();
        new GroupColumnsCommand(sheet.Id, 2, 3, 1).Apply(ctx);
        new GroupColumnsCommand(sheet.Id, 5, 6, 1).Apply(ctx);

        var command = new GroupColumnsCommand(sheet.Id, 2, 6, 2, preserveExistingHierarchy: true);
        command.Apply(ctx);

        sheet.ColOutlineLevels.Should().BeEquivalentTo(new Dictionary<uint, int>
        {
            [2] = 2,
            [3] = 2,
            [4] = 1,
            [5] = 2,
            [6] = 2
        });

        new CollapseColGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.GroupHiddenCols.Should().BeEquivalentTo([2u, 3u, 4u, 5u, 6u]);

        new ExpandColGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.GroupHiddenCols.Should().BeEmpty();

        command.Revert(ctx);

        sheet.ColOutlineLevels.Should().BeEquivalentTo(new Dictionary<uint, int>
        {
            [2] = 1,
            [3] = 1,
            [5] = 1,
            [6] = 1
        });
    }

    [Fact]
    public void GroupColumns_OuterFirstThenSubgroup_RemainsNested()
    {
        var (_, sheet, ctx) = Setup();
        new GroupColumnsCommand(sheet.Id, 2, 6, 1, preserveExistingHierarchy: true).Apply(ctx);

        new GroupColumnsCommand(sheet.Id, 3, 4, 2, preserveExistingHierarchy: true).Apply(ctx);

        sheet.ColOutlineLevels.Should().BeEquivalentTo(new Dictionary<uint, int>
        {
            [2] = 1,
            [3] = 2,
            [4] = 2,
            [5] = 1,
            [6] = 1
        });
    }

    [Fact]
    public void CollapseColumns_HidesGroupedColumns()
    {
        var (_, sheet, ctx) = Setup();
        new GroupColumnsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new CollapseColGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.GroupHiddenCols.Should().Contain(2).And.Contain(4);
        sheet.CollapsedAnchorCols.Should().ContainSingle().Which.Should().Be(5);
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
        sheet.CollapsedAnchorCols.Should().BeEmpty();
        sheet.IsColEffectivelyHidden(2).Should().BeFalse();
    }

    [Fact]
    public void SetColumnOutlineGroupCollapsed_CollapsesOnlyRequestedRangeAndReverts()
    {
        var (_, sheet, ctx) = Setup();
        new GroupColumnsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new GroupColumnsCommand(sheet.Id, 8, 9, 1).Apply(ctx);

        var command = new SetColumnOutlineGroupCollapsedCommand(sheet.Id, 2, 4, 1, collapsed: true);
        command.Apply(ctx);

        sheet.GroupHiddenCols.Should().BeEquivalentTo([2u, 3u, 4u]);
        sheet.GroupHiddenCols.Should().NotContain(8u);
        sheet.CollapsedAnchorCols.Should().ContainSingle().Which.Should().Be(5u);

        command.Revert(ctx);

        sheet.GroupHiddenCols.Should().BeEmpty();
        sheet.CollapsedAnchorCols.Should().BeEmpty();
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
        sheet.CollapsedAnchorRows.Should().BeEmpty();
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
        sheet.CollapsedAnchorRows.Should().ContainSingle().Which.Should().Be(5u);
    }

    [Fact]
    public void ClearOutline_ClearsAndRestoresCollapsedAnchorsWithHiddenDetails()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new GroupColumnsCommand(sheet.Id, 2, 3, 1).Apply(ctx);
        new CollapseRowGroupCommand(sheet.Id, 1).Apply(ctx);
        new CollapseColGroupCommand(sheet.Id, 1).Apply(ctx);
        var command = new ClearWorksheetOutlineCommand(sheet.Id);

        command.Apply(ctx);

        sheet.GroupHiddenRows.Should().BeEmpty();
        sheet.GroupHiddenCols.Should().BeEmpty();
        sheet.CollapsedAnchorRows.Should().BeEmpty();
        sheet.CollapsedAnchorCols.Should().BeEmpty();

        command.Revert(ctx);

        sheet.GroupHiddenRows.Should().BeEquivalentTo([2u, 3u, 4u]);
        sheet.GroupHiddenCols.Should().BeEquivalentTo([2u, 3u]);
        sheet.CollapsedAnchorRows.Should().ContainSingle().Which.Should().Be(5u);
        sheet.CollapsedAnchorCols.Should().ContainSingle().Which.Should().Be(4u);
    }

    [Fact]
    public void GroupRows_InvalidLevel_Throws()
    {
        var (_, sheet, _) = Setup();
        var act = () => new GroupRowsCommand(sheet.Id, 1, 3, 9);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // R75-commands-outline-group-4-2: expanding an outer outline group must not force-expand an
    // independently-collapsed nested subgroup -- Excel keeps the inner subgroup collapsed.

    [Fact]
    public void SetRowOutlineGroupCollapsed_ExpandOuter_LeavesStillCollapsedNestedSubgroupHidden()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 9, 1, preserveExistingHierarchy: true).Apply(ctx);
        new GroupRowsCommand(sheet.Id, 4, 6, 2, preserveExistingHierarchy: true).Apply(ctx);

        new SetRowOutlineGroupCollapsedCommand(sheet.Id, 4, 6, 2, collapsed: true).Apply(ctx);
        new SetRowOutlineGroupCollapsedCommand(sheet.Id, 2, 9, 1, collapsed: true).Apply(ctx);
        sheet.GroupHiddenRows.Should().BeEquivalentTo([2u, 3u, 4u, 5u, 6u, 7u, 8u, 9u]);
        sheet.CollapsedAnchorRows.Should().BeEquivalentTo([7u, 10u]);

        new SetRowOutlineGroupCollapsedCommand(sheet.Id, 2, 9, 1, collapsed: false).Apply(ctx);

        sheet.GroupHiddenRows.Should().BeEquivalentTo([4u, 5u, 6u]);
        sheet.CollapsedAnchorRows.Should().ContainSingle().Which.Should().Be(7u);
        sheet.IsRowEffectivelyHidden(2).Should().BeFalse();
        sheet.IsRowEffectivelyHidden(3).Should().BeFalse();
        sheet.IsRowEffectivelyHidden(7).Should().BeFalse();
        sheet.IsRowEffectivelyHidden(8).Should().BeFalse();
        sheet.IsRowEffectivelyHidden(9).Should().BeFalse();
        sheet.IsRowEffectivelyHidden(4).Should().BeTrue();
        sheet.IsRowEffectivelyHidden(6).Should().BeTrue();
    }

    [Fact]
    public void SetRowOutlineGroupCollapsed_ExpandOuter_NoNestedSubgroup_RevealsAllRows()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 9, 1).Apply(ctx);
        new SetRowOutlineGroupCollapsedCommand(sheet.Id, 2, 9, 1, collapsed: true).Apply(ctx);

        new SetRowOutlineGroupCollapsedCommand(sheet.Id, 2, 9, 1, collapsed: false).Apply(ctx);

        sheet.GroupHiddenRows.Should().BeEmpty();
        sheet.CollapsedAnchorRows.Should().BeEmpty();
    }

    [Fact]
    public void ExpandRowGroupCommand_SelectionScoped_ExpandOuter_LeavesStillCollapsedNestedSubgroupHidden()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 9, 1, preserveExistingHierarchy: true).Apply(ctx);
        new GroupRowsCommand(sheet.Id, 4, 6, 2, preserveExistingHierarchy: true).Apply(ctx);

        new SetRowOutlineGroupCollapsedCommand(sheet.Id, 4, 6, 2, collapsed: true).Apply(ctx);
        new SetRowOutlineGroupCollapsedCommand(sheet.Id, 2, 9, 1, collapsed: true).Apply(ctx);

        new ExpandRowGroupCommand(sheet.Id, 1, selectionStart: 2, selectionEnd: 2).Apply(ctx);

        sheet.GroupHiddenRows.Should().BeEquivalentTo([4u, 5u, 6u]);
        sheet.CollapsedAnchorRows.Should().ContainSingle().Which.Should().Be(7u);
        sheet.IsRowEffectivelyHidden(2).Should().BeFalse();
        sheet.IsRowEffectivelyHidden(9).Should().BeFalse();
        sheet.IsRowEffectivelyHidden(5).Should().BeTrue();
    }

    [Fact]
    public void ExpandRowGroupCommand_SelectionScoped_NoNestedSubgroup_RevealsAllRows()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new CollapseRowGroupCommand(sheet.Id, 1, selectionStart: 2).Apply(ctx);

        new ExpandRowGroupCommand(sheet.Id, 1, selectionStart: 2, selectionEnd: 2).Apply(ctx);

        sheet.GroupHiddenRows.Should().BeEmpty();
        sheet.CollapsedAnchorRows.Should().BeEmpty();
    }

    [Fact]
    public void SetColumnOutlineGroupCollapsed_ExpandOuter_LeavesStillCollapsedNestedSubgroupHidden()
    {
        var (_, sheet, ctx) = Setup();
        new GroupColumnsCommand(sheet.Id, 2, 9, 1, preserveExistingHierarchy: true).Apply(ctx);
        new GroupColumnsCommand(sheet.Id, 4, 6, 2, preserveExistingHierarchy: true).Apply(ctx);

        new SetColumnOutlineGroupCollapsedCommand(sheet.Id, 4, 6, 2, collapsed: true).Apply(ctx);
        new SetColumnOutlineGroupCollapsedCommand(sheet.Id, 2, 9, 1, collapsed: true).Apply(ctx);
        sheet.GroupHiddenCols.Should().BeEquivalentTo([2u, 3u, 4u, 5u, 6u, 7u, 8u, 9u]);
        sheet.CollapsedAnchorCols.Should().BeEquivalentTo([7u, 10u]);

        new SetColumnOutlineGroupCollapsedCommand(sheet.Id, 2, 9, 1, collapsed: false).Apply(ctx);

        sheet.GroupHiddenCols.Should().BeEquivalentTo([4u, 5u, 6u]);
        sheet.CollapsedAnchorCols.Should().ContainSingle().Which.Should().Be(7u);
        sheet.IsColEffectivelyHidden(2).Should().BeFalse();
        sheet.IsColEffectivelyHidden(3).Should().BeFalse();
        sheet.IsColEffectivelyHidden(7).Should().BeFalse();
        sheet.IsColEffectivelyHidden(8).Should().BeFalse();
        sheet.IsColEffectivelyHidden(9).Should().BeFalse();
        sheet.IsColEffectivelyHidden(4).Should().BeTrue();
        sheet.IsColEffectivelyHidden(6).Should().BeTrue();
    }

    [Fact]
    public void SetColumnOutlineGroupCollapsed_ExpandOuter_NoNestedSubgroup_RevealsAllColumns()
    {
        var (_, sheet, ctx) = Setup();
        new GroupColumnsCommand(sheet.Id, 2, 9, 1).Apply(ctx);
        new SetColumnOutlineGroupCollapsedCommand(sheet.Id, 2, 9, 1, collapsed: true).Apply(ctx);

        new SetColumnOutlineGroupCollapsedCommand(sheet.Id, 2, 9, 1, collapsed: false).Apply(ctx);

        sheet.GroupHiddenCols.Should().BeEmpty();
        sheet.CollapsedAnchorCols.Should().BeEmpty();
    }

    [Fact]
    public void ExpandColGroupCommand_SelectionScoped_ExpandOuter_LeavesStillCollapsedNestedSubgroupHidden()
    {
        var (_, sheet, ctx) = Setup();
        new GroupColumnsCommand(sheet.Id, 2, 9, 1, preserveExistingHierarchy: true).Apply(ctx);
        new GroupColumnsCommand(sheet.Id, 4, 6, 2, preserveExistingHierarchy: true).Apply(ctx);

        new SetColumnOutlineGroupCollapsedCommand(sheet.Id, 4, 6, 2, collapsed: true).Apply(ctx);
        new SetColumnOutlineGroupCollapsedCommand(sheet.Id, 2, 9, 1, collapsed: true).Apply(ctx);

        new ExpandColGroupCommand(sheet.Id, 1, selectionStart: 2, selectionEnd: 2).Apply(ctx);

        sheet.GroupHiddenCols.Should().BeEquivalentTo([4u, 5u, 6u]);
        sheet.CollapsedAnchorCols.Should().ContainSingle().Which.Should().Be(7u);
        sheet.IsColEffectivelyHidden(2).Should().BeFalse();
        sheet.IsColEffectivelyHidden(9).Should().BeFalse();
        sheet.IsColEffectivelyHidden(5).Should().BeTrue();
    }

    [Fact]
    public void ExpandColGroupCommand_SelectionScoped_NoNestedSubgroup_RevealsAllColumns()
    {
        var (_, sheet, ctx) = Setup();
        new GroupColumnsCommand(sheet.Id, 2, 4, 1).Apply(ctx);
        new CollapseColGroupCommand(sheet.Id, 1, selectionStart: 2).Apply(ctx);

        new ExpandColGroupCommand(sheet.Id, 1, selectionStart: 2, selectionEnd: 2).Apply(ctx);

        sheet.GroupHiddenCols.Should().BeEmpty();
        sheet.CollapsedAnchorCols.Should().BeEmpty();
    }

    // R76-perf-recursion-sweep-2: the r75 nested-collapsed-group guard used to re-walk run
    // boundaries from scratch for every qualifying column/row, per level -- O(N columns * levels *
    // O(N) walk). On a heavily nested/grouped sheet this made a single Expand-group command
    // O(N^2), freezing the UI. The fix precomputes the still-collapsed nested-run coverage once
    // per Apply. These tests use a large N (well beyond what an O(N^2) walk could finish quickly)
    // and assert both correctness (the inner, still-independently-collapsed subgroup stays hidden)
    // and that the call completes fast.

    private static (Sheet sheet, ICommandContext ctx, uint n) SetupLargeNestedColumnSheet()
    {
        var (_, sheet, ctx) = Setup();
        const uint n = 8000;

        // Outer group spans the whole range at level 1; columns 2..n-1 are nested seven levels
        // deep (level 7), so expanding the outer group must, for every one of those ~n-2 columns,
        // determine whether a still-collapsed nested subgroup covers it.
        new GroupColumnsCommand(sheet.Id, 1, n, 1).Apply(ctx);
        for (var lvl = 1; lvl < 7; lvl++)
            new GroupColumnsCommand(sheet.Id, 2, n - 1, 1, preserveExistingHierarchy: true).Apply(ctx);

        new SetColumnOutlineGroupCollapsedCommand(sheet.Id, 2, n - 1, 7, collapsed: true).Apply(ctx);
        new SetColumnOutlineGroupCollapsedCommand(sheet.Id, 1, n, 1, collapsed: true).Apply(ctx);

        return (sheet, ctx, n);
    }

    [Fact]
    public void SetColumnOutlineGroupCollapsed_ExpandOuter_LargeNestedSheet_CompletesFastAndLeavesNestedSubgroupHidden()
    {
        var (sheet, ctx, n) = SetupLargeNestedColumnSheet();

        var sw = Stopwatch.StartNew();
        new SetColumnOutlineGroupCollapsedCommand(sheet.Id, 1, n, 1, collapsed: false).Apply(ctx);
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "expanding the outer group must not re-walk O(N) run boundaries per column (O(N^2))");

        sheet.IsColEffectivelyHidden(1).Should().BeFalse();
        sheet.IsColEffectivelyHidden(n).Should().BeFalse();
        sheet.IsColEffectivelyHidden(2).Should().BeTrue();
        sheet.IsColEffectivelyHidden(n - 1).Should().BeTrue();
        sheet.IsColEffectivelyHidden(n / 2).Should().BeTrue();
    }

    [Fact]
    public void ExpandColGroupCommand_SelectionScoped_LargeNestedSheet_CompletesFastAndLeavesNestedSubgroupHidden()
    {
        var (sheet, ctx, n) = SetupLargeNestedColumnSheet();

        var sw = Stopwatch.StartNew();
        new ExpandColGroupCommand(sheet.Id, 1, selectionStart: 1, selectionEnd: 1).Apply(ctx);
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "expanding the outer group must not re-walk O(N) run boundaries per column (O(N^2))");

        sheet.IsColEffectivelyHidden(1).Should().BeFalse();
        sheet.IsColEffectivelyHidden(n).Should().BeFalse();
        sheet.IsColEffectivelyHidden(2).Should().BeTrue();
        sheet.IsColEffectivelyHidden(n - 1).Should().BeTrue();
        sheet.IsColEffectivelyHidden(n / 2).Should().BeTrue();
    }

    private static (Sheet sheet, ICommandContext ctx, uint n) SetupLargeNestedRowSheet()
    {
        var (_, sheet, ctx) = Setup();
        const uint n = 8000;

        new GroupRowsCommand(sheet.Id, 1, n, 1).Apply(ctx);
        for (var lvl = 1; lvl < 7; lvl++)
            new GroupRowsCommand(sheet.Id, 2, n - 1, 1, preserveExistingHierarchy: true).Apply(ctx);

        new SetRowOutlineGroupCollapsedCommand(sheet.Id, 2, n - 1, 7, collapsed: true).Apply(ctx);
        new SetRowOutlineGroupCollapsedCommand(sheet.Id, 1, n, 1, collapsed: true).Apply(ctx);

        return (sheet, ctx, n);
    }

    [Fact]
    public void SetRowOutlineGroupCollapsed_ExpandOuter_LargeNestedSheet_CompletesFastAndLeavesNestedSubgroupHidden()
    {
        var (sheet, ctx, n) = SetupLargeNestedRowSheet();

        var sw = Stopwatch.StartNew();
        new SetRowOutlineGroupCollapsedCommand(sheet.Id, 1, n, 1, collapsed: false).Apply(ctx);
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "expanding the outer group must not re-walk O(N) run boundaries per row (O(N^2))");

        sheet.IsRowEffectivelyHidden(1).Should().BeFalse();
        sheet.IsRowEffectivelyHidden(n).Should().BeFalse();
        sheet.IsRowEffectivelyHidden(2).Should().BeTrue();
        sheet.IsRowEffectivelyHidden(n - 1).Should().BeTrue();
        sheet.IsRowEffectivelyHidden(n / 2).Should().BeTrue();
    }

    [Fact]
    public void ExpandRowGroupCommand_SelectionScoped_LargeNestedSheet_CompletesFastAndLeavesNestedSubgroupHidden()
    {
        var (sheet, ctx, n) = SetupLargeNestedRowSheet();

        var sw = Stopwatch.StartNew();
        new ExpandRowGroupCommand(sheet.Id, 1, selectionStart: 1, selectionEnd: 1).Apply(ctx);
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "expanding the outer group must not re-walk O(N) run boundaries per row (O(N^2))");

        sheet.IsRowEffectivelyHidden(1).Should().BeFalse();
        sheet.IsRowEffectivelyHidden(n).Should().BeFalse();
        sheet.IsRowEffectivelyHidden(2).Should().BeTrue();
        sheet.IsRowEffectivelyHidden(n - 1).Should().BeTrue();
        sheet.IsRowEffectivelyHidden(n / 2).Should().BeTrue();
    }
}
