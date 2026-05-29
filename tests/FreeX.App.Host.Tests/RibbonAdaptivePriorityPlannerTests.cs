using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonAdaptivePriorityPlannerTests
{
    [Fact]
    public void ApplyRuntimePriorityStates_CollapsesInsertChartsAtNarrowWidths()
    {
        var groupNames = new[] { "Tables", "Illustrations", "Add-ins", "Charts" };

        var states = RibbonAdaptivePriorityPlanner.ApplyRuntimePriorityStates(
            900,
            groupNames,
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, groupNames.Length).ToArray());

        states[Array.IndexOf(groupNames, "Charts")].Should().Be(RibbonAdaptiveGroupState.Collapsed);
        states[Array.IndexOf(groupNames, "Tables")].Should().Be(RibbonAdaptiveGroupState.Full);
    }

    [Fact]
    public void ApplyRuntimePriorityStates_IgnoresOverridesOutsidePlannedStateRange()
    {
        var states = RibbonAdaptivePriorityPlanner.ApplyRuntimePriorityStates(
            900,
            ["Tables", "Illustrations", "Add-ins", "Charts"],
            [RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.Full]);

        states.Should().Equal(RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.Full);
    }

    [Fact]
    public void RuntimeVisibilityOverrides_KeepDataToolsIconOnlyAtMediumWidths()
    {
        var groupNames = new[] { "Get & Transform Data", "Queries & Connections", "Data Types", "Sort & Filter", "Data Tools", "Forecast" };

        var decisions = RibbonAdaptivePriorityPlanner.GetRuntimeVisibilityOverrides(1120, groupNames);

        decisions.Should().ContainSingle(decision =>
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

        decisions.Should().ContainSingle(decision =>
            decision.Index == Array.IndexOf(groupNames, "Data Tools") &&
            decision.State == RibbonAdaptiveGroupState.IconOnly);
    }

    [Fact]
    public void ApplyRuntimeVisibilityStates_ContributesDataToolsIconOnlyStateToPurePlan()
    {
        var groupNames = new[] { "Get & Transform Data", "Queries & Connections", "Data Types", "Sort & Filter", "Data Tools", "Forecast" };

        var states = RibbonAdaptivePriorityPlanner.ApplyRuntimeVisibilityStates(
            1120,
            groupNames,
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, groupNames.Length).ToArray());

        states[Array.IndexOf(groupNames, "Data Tools")].Should().Be(RibbonAdaptiveGroupState.IconOnly);
        states.Where((_, index) => index != Array.IndexOf(groupNames, "Data Tools"))
            .Should()
            .OnlyContain(state => state == RibbonAdaptiveGroupState.Full);
    }

    [Fact]
    public void FallbackProtectedGroupIndexes_ProtectPriorityGroupsButRelaxAtVeryNarrowWidths()
    {
        var groupNames = new[] { "Get & Transform Data", "Queries & Connections", "Data Types", "Sort & Filter", "Data Tools", "Forecast" };

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

        var pageLayoutGroups = new[] { "Themes", "Page Setup", "Scale to Fit", "Sheet Options", "Arrange" };
        RibbonAdaptivePriorityPlanner.GetExpandableGroupIndexes(pageLayoutGroups, 1120)
            .Should()
            .Equal(Array.IndexOf(pageLayoutGroups, "Page Setup"));
    }

    [Fact]
    public void ExpandableGroupIndexes_ExcludeRuntimeVisibilityStateOverrides()
    {
        var groupNames = new[] { "Get & Transform Data", "Queries & Connections", "Data Types", "Sort & Filter", "Data Tools", "Forecast" };

        RibbonAdaptivePriorityPlanner.GetExpandableGroupIndexes(groupNames, 1120)
            .Should()
            .NotContain(Array.IndexOf(groupNames, "Data Tools"));
    }

    [Fact]
    public void RuntimeVisibilityProtectedGroupIndexes_ProtectOnlyVisibleRuntimeOverrides()
    {
        var dataGroups = new[] { "Get & Transform Data", "Queries & Connections", "Data Types", "Sort & Filter", "Data Tools", "Forecast" };
        var insertGroups = new[] { "Tables", "Illustrations", "Add-ins", "Charts" };

        RibbonAdaptivePriorityPlanner.GetRuntimeVisibilityProtectedGroupIndexes(dataGroups, 1120)
            .Should()
            .Equal(Array.IndexOf(dataGroups, "Data Tools"));
        RibbonAdaptivePriorityPlanner.GetRuntimeVisibilityProtectedGroupIndexes(insertGroups, 900)
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void RequiresMeasuredCorrection_DetectsTabsThatNeedMeasuredOverflowGuard()
    {
        RibbonAdaptivePriorityPlanner.RequiresMeasuredCorrection(
                ["Get & Transform Data", "Queries & Connections", "Data Types", "Sort & Filter", "Data Tools"])
            .Should()
            .BeTrue();

        RibbonAdaptivePriorityPlanner.RequiresMeasuredCorrection(
                ["Tables", "Illustrations", "Charts"])
            .Should()
            .BeTrue("Insert needs measured correction to avoid clipping at common Excel widths");

        RibbonAdaptivePriorityPlanner.RequiresMeasuredCorrection(
                ["Tools", "Pens", "Convert", "Arrange", "Format"])
            .Should()
            .BeTrue("Draw needs measured correction to avoid clipping at common Excel widths");

        RibbonAdaptivePriorityPlanner.RequiresMeasuredCorrection(
                ["Clipboard", "Font", "Alignment", "Number", "Styles", "Cells", "Editing"])
            .Should()
            .BeFalse("Home should trust its deterministic profile so transient measured overflow does not collapse primary groups");
    }
}
