using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R32-commands-outline-group-deep-2: Ribbon "Hide Detail"/"Show Detail" used to collapse or
/// expand the ENTIRE sheet's outline (CollapseRowGroupCommand/ExpandRowGroupCommand had no
/// selection/range parameter and iterated every row with outlineLevel >= level, sheet-wide).
/// Excel scopes Hide/Show Detail to the single contiguous group at the current selection.
/// These tests pin the new selection-scoped behavior while also covering the pre-existing
/// no-selection (sheet-wide, backward-compatible) call shape still used elsewhere.
/// </summary>
public sealed class R32_OutlineGroupScopeTests
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

        // Group A: rows 2-5, Group B: rows 10-13. Both independent, level 1.
        new GroupRowsCommand(sheet.Id, 2, 5, 1).Apply(ctx);
        new GroupRowsCommand(sheet.Id, 10, 13, 1).Apply(ctx);

        // Selection sits inside Group A only (row 3).
        var outcome = new CollapseRowGroupCommand(sheet.Id, 1, selectionStart: 3, selectionEnd: 3).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GroupHiddenRows.Should().BeEquivalentTo([2u, 3u, 4u, 5u], "only Group A should collapse");
        sheet.GroupHiddenRows.Should().NotContain([10u, 11u, 12u, 13u], "Group B is unrelated to the selection and must stay expanded");
    }

    [Fact]
    public void Expand_WithSelectionInsideOneOfTwoIndependentGroups_OnlyExpandsThatGroup()
    {
        var (_, sheet, ctx) = Setup();

        new GroupRowsCommand(sheet.Id, 2, 5, 1).Apply(ctx);
        new GroupRowsCommand(sheet.Id, 10, 13, 1).Apply(ctx);

        // Collapse both sheet-wide first (legacy call shape), then expand only Group A via selection.
        new CollapseRowGroupCommand(sheet.Id, 1).Apply(ctx);
        sheet.GroupHiddenRows.Should().BeEquivalentTo([2u, 3u, 4u, 5u, 10u, 11u, 12u, 13u]);

        var outcome = new ExpandRowGroupCommand(sheet.Id, 1, selectionStart: 4, selectionEnd: 4).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GroupHiddenRows.Should().BeEquivalentTo([10u, 11u, 12u, 13u], "Group B must remain collapsed");
    }

    [Fact]
    public void Collapse_WithSelectionInNestedLevel2Subgroup_OnlyCollapsesTheSubgroup()
    {
        var (_, sheet, ctx) = Setup();

        // Outer level-1 group rows 2-9, with a level-2 subgroup at rows 4-6.
        new GroupRowsCommand(sheet.Id, 2, 9, 1, preserveExistingHierarchy: true).Apply(ctx);
        new GroupRowsCommand(sheet.Id, 4, 6, 2, preserveExistingHierarchy: true).Apply(ctx);

        sheet.RowOutlineLevels.Should().BeEquivalentTo(new Dictionary<uint, int>
        {
            [2] = 1, [3] = 1, [4] = 2, [5] = 2, [6] = 2, [7] = 1, [8] = 1, [9] = 1
        });

        // Selection sits inside the level-2 subgroup (row 5).
        var outcome = new CollapseRowGroupCommand(sheet.Id, 1, selectionStart: 5, selectionEnd: 5).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GroupHiddenRows.Should().BeEquivalentTo([4u, 5u, 6u], "only the level-2 subgroup should collapse");
        sheet.GroupHiddenRows.Should().NotContain([2u, 3u, 7u, 8u, 9u], "the outer level-1 group must remain expanded");
    }

    [Fact]
    public void Collapse_WithSelectionOutsideAnyGroup_IsNoOp()
    {
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 5, 1).Apply(ctx);

        var outcome = new CollapseRowGroupCommand(sheet.Id, 1, selectionStart: 20, selectionEnd: 20).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GroupHiddenRows.Should().BeEmpty("the selection isn't associated with any outline group");
    }

    [Fact]
    public void Collapse_WithoutSelection_StillCollapsesWholeSheetForBackwardCompatibility()
    {
        // Sibling already-working case: existing callers that pass no selection (e.g. other
        // command-bus paths / older call sites) must keep the original sheet-wide behavior.
        var (_, sheet, ctx) = Setup();
        new GroupRowsCommand(sheet.Id, 2, 5, 1).Apply(ctx);
        new GroupRowsCommand(sheet.Id, 10, 13, 1).Apply(ctx);

        var outcome = new CollapseRowGroupCommand(sheet.Id, 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GroupHiddenRows.Should().BeEquivalentTo([2u, 3u, 4u, 5u, 10u, 11u, 12u, 13u]);
    }
}
