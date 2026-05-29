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
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed);
    }

    [Fact]
    public void ApplyBreakpointOverrides_KeepsHomeCellsBeforeEditingAtWideWidths()
    {
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            1366,
            ["Clipboard", "Font", "Alignment", "Number", "Styles", "Cells", "Editing"],
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, 7).ToArray());

        states[5].Should().Be(RibbonAdaptiveGroupState.Full);
        states[6].Should().Be(RibbonAdaptiveGroupState.Collapsed);
    }

    [Fact]
    public void ApplyBreakpointOverrides_KeepsFormulasFunctionLibraryExpandedAtNormalWideWidths()
    {
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            1120,
            ["Function Library", "Defined Names", "Formula Auditing", "Calculation"],
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, 4).ToArray());

        states.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed);
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
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed);
    }

    [Fact]
    public void ApplyBreakpointOverrides_KeepsInsertChartsBeforeUtilityGroups()
    {
        var groupNames = new[] { "Tables", "Illustrations", "Add-ins", "Charts", "Tours", "Sparklines", "Filters", "Links", "Text", "Symbols", "Comments" };
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            1320,
            groupNames,
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, groupNames.Length).ToArray());

        states[Array.IndexOf(groupNames, "Charts")].Should().Be(RibbonAdaptiveGroupState.Full);
        states[Array.IndexOf(groupNames, "Add-ins")].Should().Be(RibbonAdaptiveGroupState.Collapsed);
        states[Array.IndexOf(groupNames, "Tours")].Should().Be(RibbonAdaptiveGroupState.Collapsed);
    }

    [Fact]
    public void ApplyBreakpointOverrides_KeepsPageSetupBeforeThemesAtMediumWidths()
    {
        var groupNames = new[] { "Themes", "Page Setup", "Scale to Fit", "Sheet Options", "Arrange" };
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            1120,
            groupNames,
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, groupNames.Length).ToArray());

        states[Array.IndexOf(groupNames, "Page Setup")].Should().Be(RibbonAdaptiveGroupState.Full);
        states[Array.IndexOf(groupNames, "Themes")].Should().Be(RibbonAdaptiveGroupState.Collapsed);
    }

    [Fact]
    public void ApplyBreakpointOverrides_RecognizesPageLayoutProfileWhenSheetOptionsAreAbsent()
    {
        var groupNames = new[] { "Themes", "Page Setup", "Scale to Fit", "Arrange" };
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            1120,
            groupNames,
            Enumerable.Repeat(RibbonAdaptiveGroupState.Collapsed, groupNames.Length).ToArray());

        RibbonAdaptiveTabProfiles.ResolveProfileName(groupNames).Should().Be("Page Layout");
        states[Array.IndexOf(groupNames, "Page Setup")].Should().Be(
            RibbonAdaptiveGroupState.Full,
            "Page Setup is the primary Page Layout command block and should not depend on Sheet Options being detected");
    }

    [Fact]
    public void ApplyBreakpointOverrides_RestoresPageSetupAfterPlannerCollapse()
    {
        var groupNames = new[] { "Themes", "Page Setup", "Scale to Fit", "Sheet Options", "Arrange" };
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            900,
            groupNames,
            Enumerable.Repeat(RibbonAdaptiveGroupState.Collapsed, groupNames.Length).ToArray());

        states[Array.IndexOf(groupNames, "Page Setup")].Should().Be(
            RibbonAdaptiveGroupState.Full,
            "Page Layout should preserve direct access to the primary Page Setup commands before utility groups");
        states[Array.IndexOf(groupNames, "Themes")].Should().Be(RibbonAdaptiveGroupState.Collapsed);
        states[Array.IndexOf(groupNames, "Arrange")].Should().Be(RibbonAdaptiveGroupState.Collapsed);
    }

    [Fact]
    public void ApplyBreakpointOverrides_KeepsDataSortAndFilterBeforeConnectionsAtMediumWidths()
    {
        var groupNames = new[] { "Get & Transform Data", "Queries & Connections", "Data Types", "Sort & Filter", "Data Tools", "Forecast", "Outline" };
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            1120,
            groupNames,
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, groupNames.Length).ToArray());

        states[Array.IndexOf(groupNames, "Sort & Filter")].Should().NotBe(RibbonAdaptiveGroupState.Collapsed);
        states[Array.IndexOf(groupNames, "Queries & Connections")].Should().Be(RibbonAdaptiveGroupState.Collapsed);
        states[Array.IndexOf(groupNames, "Data Types")].Should().Be(RibbonAdaptiveGroupState.Collapsed);
        states[Array.IndexOf(groupNames, "Data Tools")].Should().Be(RibbonAdaptiveGroupState.Full);
        states[Array.IndexOf(groupNames, "Forecast")].Should().Be(RibbonAdaptiveGroupState.Full);
        states[Array.IndexOf(groupNames, "Outline")].Should().Be(RibbonAdaptiveGroupState.Collapsed);
    }

    [Fact]
    public void GetPriorityProtectedGroupNames_PrefersDataToolsAndForecastAtNarrowWidths()
    {
        var groupNames = new[] { "Get & Transform Data", "Queries & Connections", "Data Types", "Sort & Filter", "Data Tools", "Forecast", "Outline" };

        var protectedGroups = RibbonAdaptivePriorityPlanner.GetPriorityProtectedGroupNames(groupNames, 900);

        protectedGroups.Should().Equal("Data Tools", "Forecast");
    }

    [Fact]
    public void ApplyBreakpointOverrides_KeepsReviewAccessibilityVisibleAtMediumWidths()
    {
        var groupNames = new[] { "Proofing", "Accessibility", "Comments", "Notes", "Protect" };
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            1120,
            groupNames,
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, groupNames.Length).ToArray());

        states[Array.IndexOf(groupNames, "Proofing")].Should().Be(RibbonAdaptiveGroupState.Full);
        states[Array.IndexOf(groupNames, "Comments")].Should().Be(RibbonAdaptiveGroupState.Full);
        states[Array.IndexOf(groupNames, "Accessibility")].Should().Be(RibbonAdaptiveGroupState.Full);
        states[Array.IndexOf(groupNames, "Notes")].Should().Be(RibbonAdaptiveGroupState.Collapsed);
        states[Array.IndexOf(groupNames, "Protect")].Should().Be(RibbonAdaptiveGroupState.Collapsed);
    }

    [Fact]
    public void ApplyBreakpointOverrides_RestoresViewShowWithPriorityGroupsAfterPlannerCollapse()
    {
        var groupNames = new[] { "Workbook Views", "Show", "Zoom", "Window", "Macros" };
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            1366,
            groupNames,
            Enumerable.Repeat(RibbonAdaptiveGroupState.Collapsed, groupNames.Length).ToArray());

        states[Array.IndexOf(groupNames, "Workbook Views")].Should().Be(RibbonAdaptiveGroupState.Full);
        states[Array.IndexOf(groupNames, "Show")].Should().Be(RibbonAdaptiveGroupState.Full);
        states[Array.IndexOf(groupNames, "Zoom")].Should().Be(RibbonAdaptiveGroupState.Full);
        states[Array.IndexOf(groupNames, "Window")].Should().Be(RibbonAdaptiveGroupState.Full);
        states[Array.IndexOf(groupNames, "Macros")].Should().Be(RibbonAdaptiveGroupState.Collapsed);
    }

    [Theory]
    [InlineData(1120, new[] { "Tools", "Pens", "Convert", "Arrange", "Format" }, 3)]
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

    [Fact]
    public void ApplyBreakpointOverrides_KeepsViewShowWithZoomAndWindowAtMediumWidths()
    {
        var groupNames = new[] { "Workbook Views", "Show", "Zoom", "Window", "Macros" };
        var states = RibbonAdaptiveLayoutPlanner.ApplyBreakpointOverrides(
            1120,
            groupNames,
            Enumerable.Repeat(RibbonAdaptiveGroupState.Collapsed, groupNames.Length).ToArray());

        states[Array.IndexOf(groupNames, "Workbook Views")].Should().Be(RibbonAdaptiveGroupState.Full);
        states[Array.IndexOf(groupNames, "Show")].Should().Be(RibbonAdaptiveGroupState.Full);
        states[Array.IndexOf(groupNames, "Zoom")].Should().Be(RibbonAdaptiveGroupState.Full);
        states[Array.IndexOf(groupNames, "Window")].Should().Be(RibbonAdaptiveGroupState.Full);
        states[Array.IndexOf(groupNames, "Macros")].Should().Be(RibbonAdaptiveGroupState.Collapsed);
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

    [Theory]
    [InlineData("Clipboard|Font|Alignment|Number|Styles|Cells|Editing", "Home")]
    [InlineData("Tables|Illustrations|Charts|Links", "Insert")]
    [InlineData("Function Library|Defined Names|Formula Auditing|Calculation", "Formulas")]
    [InlineData("Get & Transform Data|Queries & Connections|Data Types|Sort & Filter|Data Tools|Forecast|Outline", "Data")]
    [InlineData("Themes|Page Setup|Scale to Fit|Sheet Options|Arrange", "Page Layout")]
    [InlineData("Proofing|Accessibility|Comments|Notes|Protect", "Review")]
    [InlineData("Workbook Views|Show|Zoom|Window|Macros", "View")]
    [InlineData("Tools|Pens|Convert|Arrange|Format", "Draw")]
    [InlineData("Help", "Tiny")]
    public void Profiles_ResolveKnownRibbonTabGroupSets(string groupNameList, string expectedProfile)
    {
        RibbonAdaptiveTabProfiles
            .ResolveProfileName(groupNameList.Split('|'))
            .Should()
            .Be(expectedProfile);
    }

    [Fact]
    public void Profiles_ResolveSelectedTabHeaderWhenRequiredGroupsAreHidden()
    {
        var groupNames = new[] { "Get & Transform Data", "Sort & Filter", "Data Tools" };

        RibbonAdaptiveTabProfiles.ResolveProfileName(groupNames)
            .Should()
            .BeNull("the reduced group set no longer carries the old required group-name signature");
        RibbonAdaptiveTabProfiles.ResolveProfileName(groupNames, selectedTabHeader: "Data")
            .Should()
            .Be("Data");
    }

    [Fact]
    public void ApplyBreakpointOverrides_UsesSelectedTabHeaderWhenInsertOptionalGroupsAreHidden()
    {
        var groupNames = new[] { "Tables", "Charts", "Links" };

        var states = RibbonAdaptiveTabProfiles.ApplyBreakpointOverrides(
            1120,
            groupNames,
            Enumerable.Repeat(RibbonAdaptiveGroupState.Full, groupNames.Length).ToArray(),
            selectedTabHeader: "Insert");

        states[Array.IndexOf(groupNames, "Tables")].Should().Be(RibbonAdaptiveGroupState.SmallWithLabels);
        states[Array.IndexOf(groupNames, "Charts")].Should().Be(RibbonAdaptiveGroupState.Full);
        states[Array.IndexOf(groupNames, "Links")].Should().Be(RibbonAdaptiveGroupState.Collapsed);
    }

    [Fact]
    public void Profiles_ExposeTabHeaderBreakpointsWhenRequiredGroupsAreHidden()
    {
        var thresholds = RibbonAdaptiveTabProfiles.GetBreakpointThresholds(
            ["Get & Transform Data", "Sort & Filter", "Data Tools"],
            selectedTabHeader: "Data");

        thresholds.Should().Contain([700, 760, 900, 1120, 1320]);
        thresholds.Should().BeInAscendingOrder();
        thresholds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Profiles_ExposeTabAndCollapsedPresentationBreakpointsForResizeGate()
    {
        var thresholds = RibbonAdaptiveTabProfiles.GetBreakpointThresholds(
            ["Get & Transform Data", "Queries & Connections", "Data Types", "Sort & Filter", "Data Tools", "Forecast", "Outline"]);

        thresholds.Should().Contain([700, 760, 900, 920, 1120, 1320]);
        thresholds.Should().BeInAscendingOrder();
        thresholds.Should().OnlyHaveUniqueItems();
    }
}
