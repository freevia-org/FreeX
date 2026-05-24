using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class RibbonAdaptiveLayoutPlannerTests
{
    [Fact]
    public void Plan_KeepsGroupsFullWhenTheyFit()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Clipboard", 100, 80, 56, 50),
            new RibbonAdaptiveGroup("Font", 200, 150, 96, 52)
        };

        var states = RibbonAdaptiveLayoutPlanner.Plan(320, groups);

        states.Should().Equal(RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.Full);
    }

    [Fact]
    public void Plan_CompressesGroupsFromRightToLeftBeforeCollapsing()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Clipboard", 100, 86, 62, 50),
            new RibbonAdaptiveGroup("Font", 200, 150, 96, 52),
            new RibbonAdaptiveGroup("Alignment", 180, 132, 88, 56)
        };

        var states = RibbonAdaptiveLayoutPlanner.Plan(390, groups);

        states.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.IconOnly);
    }

    [Fact]
    public void Plan_CollapsesGroupsWhenIconOnlyFootprintStillDoesNotFit()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Clipboard", 100, 86, 62, 50),
            new RibbonAdaptiveGroup("Font", 200, 150, 96, 52),
            new RibbonAdaptiveGroup("Alignment", 180, 132, 88, 56),
            new RibbonAdaptiveGroup("Number", 150, 112, 76, 54)
        };

        var states = RibbonAdaptiveLayoutPlanner.Plan(270, groups);

        states.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed);
    }

    [Fact]
    public void Plan_ReservesFixedChromeWidthForDividersAndPadding()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Clipboard", 100, 100, 62, 50),
            new RibbonAdaptiveGroup("Font", 200, 200, 96, 52),
            new RibbonAdaptiveGroup("Alignment", 180, 180, 88, 56)
        };

        var states = RibbonAdaptiveLayoutPlanner.Plan(500, groups, fixedChromeWidth: 50);

        states.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.IconOnly);
    }

    [Fact]
    public void ApplyBreakpointOverrides_CollapsesAllGroupsAtVeryNarrowWidths()
    {
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            700,
            ["Clipboard", "Font", "Alignment"],
            [RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.IconOnly]);

        states.Should().Equal(
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed);
    }

    [Fact]
    public void ApplyBreakpointOverrides_AppliesHomeTabExcelLikeBreakpoints()
    {
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            1120,
            ["Clipboard", "Font", "Alignment", "Number", "Styles", "Cells", "Editing"],
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, 7).ToArray());

        states.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed);
    }

    [Fact]
    public void ApplyBreakpointOverrides_AppliesFormulasFunctionLibraryBreakpoint()
    {
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            1500,
            ["Function Library", "Defined Names", "Formula Auditing", "Calculation"],
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, 4).ToArray());

        states.Should().Equal(
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full);
    }

    [Fact]
    public void ApplyBreakpointOverrides_KeepsInsertTablesVisibleAtNormalNarrowWidths()
    {
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            1120,
            ["Tables", "Illustrations", "Add-ins", "Charts", "Tours", "Sparklines", "Filters", "Links", "Text", "Symbols"],
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, 10).ToArray());

        states.Should().Equal(
            RibbonAdaptiveGroupState.SmallWithLabels,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed);
    }

    [Theory]
    [InlineData(1120, new[] { "Get & Transform", "Sort & Filter", "Data Tools", "What-If Analysis", "Outline", "Named Ranges" }, 2)]
    [InlineData(1120, new[] { "Workbook Views", "Show", "Freeze Panes", "Zoom", "Window" }, 2)]
    [InlineData(1120, new[] { "Themes", "Page Setup", "Sheet Options" }, 1)]
    [InlineData(1120, new[] { "Proofing", "Accessibility", "Comments", "Protect" }, 2)]
    [InlineData(1120, new[] { "Draw", "Arrange", "Format" }, 1)]
    public void ApplyBreakpointOverrides_AppliesExcelLikeTabSpecificCollapseOrder(
        double availableWidth,
        string[] groupNames,
        int firstCollapsedIndex)
    {
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            availableWidth,
            groupNames,
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, groupNames.Length).ToArray());

        states.Take(firstCollapsedIndex).Should().OnlyContain(state => state == RibbonAdaptiveGroupState.Full);
        states.Skip(firstCollapsedIndex).Should().OnlyContain(state => state == RibbonAdaptiveGroupState.Collapsed);
    }

    [Theory]
    [InlineData("PivotTable|Data")]
    [InlineData("Layout|PivotTable Style Options")]
    [InlineData("Help")]
    public void ApplyBreakpointOverrides_KeepsTinyTabsExpandedUntilVeryNarrow(string groupNameList)
    {
        var groupNames = groupNameList.Split('|');

        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            1120,
            groupNames,
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, groupNames.Length).ToArray());

        states.Should().OnlyContain(state => state == RibbonAdaptiveGroupState.Full);
    }

    [Fact]
    public void ApplyBreakpointOverrides_AppliesGenericCollapseFromRules()
    {
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            1120,
            ["Review", "Comments", "Protect"],
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, 3).ToArray());

        states.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed);
    }
}
