using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R40-commands-group-outline-3-2: ribbon Hide Detail/Show Detail for COLUMNS used to
/// collapse/expand every column group on the sheet (CollapseColGroupCommand/ExpandColGroupCommand
/// had no selection/range parameter and iterated every column with outlineLevel >= level,
/// sheet-wide) instead of scoping to the group at the current selection, matching Excel. This is
/// the column-axis twin of the row-side fix pinned by R32_OutlineGroupScopeTests.
/// </summary>
public sealed class R40_ColumnOutlineGroupScopeTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    [Fact]
    public void Collapse_WithSelectionInsideOneOfTwoIndependentGroups_OnlyCollapsesThatGroup()
    {
        var (_, sheet, ctx) = Setup();

        // Group A: columns 2-5 (B:E), Group B: columns 10-13. Both independent, level 1.
        new GroupColumnsCommand(sheet.Id, 2, 5, 1).Apply(ctx);
        new GroupColumnsCommand(sheet.Id, 10, 13, 1).Apply(ctx);

        // Selection sits inside Group A only (column 3).
        var outcome = new CollapseColGroupCommand(sheet.Id, 1, selectionStart: 3, selectionEnd: 3).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GroupHiddenCols.Should().BeEquivalentTo([2u, 3u, 4u, 5u], "only Group A should collapse");
        sheet.GroupHiddenCols.Should().NotContain([10u, 11u, 12u, 13u], "Group B is unrelated to the selection and must stay expanded");
    }

    [Fact]
    public void Expand_WithSelectionInsideOneOfTwoIndependentGroups_OnlyExpandsThatGroup()
    {
        var (_, sheet, ctx) = Setup();

        new GroupColumnsCommand(sheet.Id, 2, 5, 1).Apply(ctx);
        new GroupColumnsCommand(sheet.Id, 10, 13, 1).Apply(ctx);

        // Collapse both sheet-wide first (legacy call shape), then expand only Group A via selection.
        new CollapseColGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.GroupHiddenCols.Should().BeEquivalentTo([2u, 3u, 4u, 5u, 10u, 11u, 12u, 13u]);

        var outcome = new ExpandColGroupCommand(sheet.Id, 1, selectionStart: 4, selectionEnd: 4).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GroupHiddenCols.Should().BeEquivalentTo([10u, 11u, 12u, 13u], "Group B must remain collapsed");
    }

    [Fact]
    public void Collapse_WithSelectionOutsideAnyGroup_IsNoOp()
    {
        var (_, sheet, ctx) = Setup();
        new GroupColumnsCommand(sheet.Id, 2, 5, 1).Apply(ctx);

        var outcome = new CollapseColGroupCommand(sheet.Id, 1, selectionStart: 20, selectionEnd: 20).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GroupHiddenCols.Should().BeEmpty("the selection isn't associated with any outline group");
    }

    [Fact]
    public void Collapse_WithoutSelection_StillCollapsesWholeSheetForBackwardCompatibility()
    {
        // Sibling already-working case: existing callers that pass no selection (e.g. other
        // command-bus paths / older call sites) must keep the original sheet-wide behavior.
        var (_, sheet, ctx) = Setup();
        new GroupColumnsCommand(sheet.Id, 2, 5, 1).Apply(ctx);
        new GroupColumnsCommand(sheet.Id, 10, 13, 1).Apply(ctx);

        var outcome = new CollapseColGroupCommand(sheet.Id, 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GroupHiddenCols.Should().BeEquivalentTo([2u, 3u, 4u, 5u, 10u, 11u, 12u, 13u]);
    }
}
