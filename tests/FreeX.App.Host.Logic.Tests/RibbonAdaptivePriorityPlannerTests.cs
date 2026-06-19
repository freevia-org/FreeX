using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonAdaptivePriorityPlannerTests
{
    [Fact]
    public void ApplyRuntimePriorityStates_KeepsInsertChartsVisibleAtNarrowWidths()
    {
        var groupNames = new[] { "Tables", "Charts", "Sparklines" };

        var states = RibbonAdaptivePriorityPlanner.ApplyRuntimePriorityStates(
            900,
            groupNames,
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, groupNames.Length).ToArray());

        states[Array.IndexOf(groupNames, "Charts")].Should().Be(RibbonAdaptiveGroupState.Full);
        states[Array.IndexOf(groupNames, "Tables")].Should().Be(RibbonAdaptiveGroupState.Full);
    }

    [Fact]
    public void ApplyRuntimePriorityStates_UsesCatalogIdsAsStableInsertGroupKeys()
    {
        var groupKeys = new[] { "InsertTablesGroup", "InsertChartsGroup", "InsertSparklinesGroup" };

        var states = RibbonAdaptivePriorityPlanner.ApplyRuntimePriorityStates(
            900,
            groupKeys,
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, groupKeys.Length).ToArray(),
            selectedTabHeader: "InsertTab");

        states[Array.IndexOf(groupKeys, "InsertChartsGroup")].Should().Be(RibbonAdaptiveGroupState.Full);
        states[Array.IndexOf(groupKeys, "InsertTablesGroup")].Should().Be(RibbonAdaptiveGroupState.Full);
    }

    [Fact]
    public void ApplyRuntimePriorityStates_IgnoresOverridesOutsidePlannedStateRange()
    {
        var states = RibbonAdaptivePriorityPlanner.ApplyRuntimePriorityStates(
            900,
            ["Tables", "Illustrations", "Charts"],
            [RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.Full]);

        states.Should().Equal(RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.Full);
    }

    [Fact]
    public void RuntimeVisibilityOverrides_KeepMediumDataPriorityGroupsIconOnly()
    {
        var groupNames = new[] { "Get & Transform Data", "Queries & Connections", "Sort & Filter", "Data Tools", "Forecast" };

        var decisions = RibbonAdaptivePriorityPlanner.GetRuntimeVisibilityOverrides(1120, groupNames);

        decisions.Should().Contain(decision =>
            decision.Index == Array.IndexOf(groupNames, "Sort & Filter") &&
            decision.State == RibbonAdaptiveGroupState.IconOnly);
        decisions.Should().Contain(decision =>
            decision.Index == Array.IndexOf(groupNames, "Data Tools") &&
            decision.State == RibbonAdaptiveGroupState.IconOnly);
    }

    [Fact]
    public void RuntimeVisibilityOverrides_UseSelectedTabHeaderWhenOptionalDataGroupsAreHidden()
    {
        var groupNames = new[] { "Get & Transform Data", "Sort & Filter", "Data Tools" };

        RibbonAdaptivePriorityPlanner.GetRuntimeVisibilityOverrides(1120, groupNames)
            .Should()
            .BeEmpty("the reduced group set no longer carries the full Data tab signature");

        var decisions = RibbonAdaptivePriorityPlanner.GetRuntimeVisibilityOverrides(
            1120,
            groupNames,
            selectedTabHeader: "Data");

        decisions.Should().Contain(decision =>
            decision.Index == Array.IndexOf(groupNames, "Sort & Filter") &&
            decision.State == RibbonAdaptiveGroupState.IconOnly);
        decisions.Should().Contain(decision =>
            decision.Index == Array.IndexOf(groupNames, "Data Tools") &&
            decision.State == RibbonAdaptiveGroupState.IconOnly);
    }

    [Fact]
    public void RuntimeVisibilityOverrides_UseDataCatalogIdsAsStableGroupKeys()
    {
        var groupKeys = new[] { "DataGetTransformGroup", "DataSortFilterGroup", "DataToolsGroup" };

        var decisions = RibbonAdaptivePriorityPlanner.GetRuntimeVisibilityOverrides(
            1120,
            groupKeys,
            selectedTabHeader: "DataTab");

        decisions.Should().Contain(decision =>
            decision.Index == Array.IndexOf(groupKeys, "DataSortFilterGroup") &&
            decision.State == RibbonAdaptiveGroupState.IconOnly);
        decisions.Should().Contain(decision =>
            decision.Index == Array.IndexOf(groupKeys, "DataToolsGroup") &&
            decision.State == RibbonAdaptiveGroupState.IconOnly);
        RibbonAdaptivePriorityPlanner.RequiresMeasuredCorrection(groupKeys, selectedTabHeader: "DataTab")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ApplyRuntimeVisibilityStates_ContributesMediumDataPriorityIconOnlyStatesToPurePlan()
    {
        var groupNames = new[] { "Get & Transform Data", "Queries & Connections", "Sort & Filter", "Data Tools", "Forecast" };

        var states = RibbonAdaptivePriorityPlanner.ApplyRuntimeVisibilityStates(
            1120,
            groupNames,
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, groupNames.Length).ToArray());

        var sortFilterIndex = Array.IndexOf(groupNames, "Sort & Filter");
        var dataToolsIndex = Array.IndexOf(groupNames, "Data Tools");

        states[sortFilterIndex].Should().Be(RibbonAdaptiveGroupState.IconOnly);
        states[dataToolsIndex].Should().Be(RibbonAdaptiveGroupState.IconOnly);
        states.Where((_, index) => index != sortFilterIndex && index != dataToolsIndex)
            .Should()
            .OnlyContain(state => state == RibbonAdaptiveGroupState.Full);
    }

    [Fact]
    public void FallbackProtectedGroupIndexes_ProtectPriorityGroupsButRelaxAtVeryNarrowWidths()
    {
        var groupNames = new[] { "Get & Transform Data", "Queries & Connections", "Sort & Filter", "Data Tools", "Forecast" };

        RibbonAdaptivePriorityPlanner.GetFallbackProtectedGroupIndexes(groupNames, 1120)
            .Should()
            .BeEquivalentTo(
                [
                    Array.IndexOf(groupNames, "Sort & Filter"),
                    Array.IndexOf(groupNames, "Data Tools"),
                    Array.IndexOf(groupNames, "Forecast")
                ]);

        RibbonAdaptivePriorityPlanner.GetFallbackProtectedGroupIndexes(groupNames, 760)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void FallbackProtectedGroupIndexes_UseSelectedTabHeaderWhenOptionalDataGroupsAreHidden()
    {
        var groupNames = new[] { "Get & Transform Data", "Sort & Filter", "Data Tools" };

        RibbonAdaptivePriorityPlanner.GetFallbackProtectedGroupIndexes(groupNames, 1120)
            .Should()
            .BeEmpty();

        RibbonAdaptivePriorityPlanner.GetFallbackProtectedGroupIndexes(groupNames, 1120, selectedTabHeader: "Data")
            .Should()
            .BeEquivalentTo(
                [
                    Array.IndexOf(groupNames, "Sort & Filter"),
                    Array.IndexOf(groupNames, "Data Tools")
                ]);
    }

    [Fact]
    public void ExpandableGroupIndexes_SkipReviewAndPreferProtectedPriorityGroups()
    {
        RibbonAdaptivePriorityPlanner.GetExpandableGroupIndexes(
                ["Proofing", "Accessibility", "Comments", "Notes", "Protect"],
                1120)
            .Should()
            .BeEmpty("Review keeps its proofing/accessibility/comment block stable after measured fallback");

        var pageLayoutGroups = new[] { "Themes", "Page Setup", "Scale to Fit", "Sheet Options" };
        RibbonAdaptivePriorityPlanner.GetExpandableGroupIndexes(pageLayoutGroups, 1120)
            .Should()
            .Equal(
                Array.IndexOf(pageLayoutGroups, "Themes"),
                Array.IndexOf(pageLayoutGroups, "Page Setup"));
    }

    [Fact]
    public void ExpandableGroupIndexes_ExcludeRuntimeVisibilityStateOverrides()
    {
        var groupNames = new[] { "Get & Transform Data", "Queries & Connections", "Sort & Filter", "Data Tools", "Forecast" };

        RibbonAdaptivePriorityPlanner.GetExpandableGroupIndexes(groupNames, 1120)
            .Should()
            .NotContain(Array.IndexOf(groupNames, "Sort & Filter"))
            .And
            .NotContain(Array.IndexOf(groupNames, "Data Tools"));
    }

    [Fact]
    public void SpaceFillingExpandableGroupIndexes_UseSpareWidthWithoutUndoingRuntimeOverrides()
    {
        var groupNames = new[] { "Get & Transform Data", "Queries & Connections", "Sort & Filter", "Data Tools", "Forecast", "Outline" };

        RibbonAdaptivePriorityPlanner.GetSpaceFillingExpandableGroupIndexes(groupNames, 1120)
            .Should()
            .Equal(
                Array.IndexOf(groupNames, "Get & Transform Data"),
                Array.IndexOf(groupNames, "Queries & Connections"),
                Array.IndexOf(groupNames, "Forecast"),
                Array.IndexOf(groupNames, "Outline"));
    }

    [Fact]
    public void SpaceFillingExpandableGroupIndexes_StillUseAvailableSpaceAtNarrowWidths()
    {
        var groupNames = new[] { "Function Library", "Defined Names", "Formula Auditing", "Calculation" };

        RibbonAdaptivePriorityPlanner.GetSpaceFillingExpandableGroupIndexes(groupNames, 750)
            .Should()
            .Equal(0, 1, 2, 3);
    }

    [Fact]
    public void RuntimeVisibilityProtectedGroupIndexes_ProtectOnlyVisibleRuntimeOverrides()
    {
        var dataGroups = new[] { "Get & Transform Data", "Queries & Connections", "Sort & Filter", "Data Tools", "Forecast" };
        var insertGroups = new[] { "Tables", "Charts", "Sparklines" };

        RibbonAdaptivePriorityPlanner.GetRuntimeVisibilityProtectedGroupIndexes(dataGroups, 1120)
            .Should()
            .Equal(
                Array.IndexOf(dataGroups, "Sort & Filter"),
                Array.IndexOf(dataGroups, "Data Tools"));
        RibbonAdaptivePriorityPlanner.GetRuntimeVisibilityProtectedGroupIndexes(insertGroups, 900)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void RequiresMeasuredCorrection_DetectsTabsThatNeedMeasuredOverflowGuard()
    {
        RibbonAdaptivePriorityPlanner.RequiresMeasuredCorrection(
                ["Get & Transform Data", "Queries & Connections", "Sort & Filter", "Data Tools"])
            .Should()
            .BeTrue();

        RibbonAdaptivePriorityPlanner.RequiresMeasuredCorrection(
                ["Tables", "Charts", "Sparklines"])
            .Should()
            .BeTrue("Insert needs measured correction to avoid clipping at common Excel widths");

        RibbonAdaptivePriorityPlanner.RequiresMeasuredCorrection(
                ["Illustrations", "Arrange", "Format"])
            .Should()
            .BeFalse("Draw keeps a compact object creation/arrange/format surface instead of the wide out-of-scope ink groups");

        RibbonAdaptivePriorityPlanner.RequiresMeasuredCorrection(
                ["Properties", "Tools", "Table Style Options", "Table Styles"])
            .Should()
            .BeTrue("unknown contextual tabs need a measured overflow guard after the pure planner spends spare width");

        RibbonAdaptivePriorityPlanner.RequiresMeasuredCorrection(
                ["Clipboard", "Font", "Alignment", "Number", "Styles", "Cells", "Editing"])
            .Should()
            .BeTrue("Home needs the measured overflow guard at common desktop widths where WPF command rows are wider than the deterministic profile");
    }
}
