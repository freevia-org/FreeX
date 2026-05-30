using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonAdaptiveLayoutEngineTests
{
    [Fact]
    public void Plan_ReturnsEmptyLayoutForEmptyGroupSet()
    {
        var layout = RibbonAdaptiveLayoutEngine.Plan(900, [], fixedChromeWidth: 36);

        layout.States.Should().BeEmpty();
        layout.PlannedWidth.Should().Be(0);
        layout.RequiresMeasuredCorrection.Should().BeFalse();
    }

    [Fact]
    public void Plan_CombinesMeasuredWidthsBreakpointsAndPriorityFallbacks()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Get & Transform Data", 170, 130, 78, 58),
            new RibbonAdaptiveGroup("Queries & Connections", 155, 118, 70, 58),
            new RibbonAdaptiveGroup("Data Types", 140, 110, 66, 58),
            new RibbonAdaptiveGroup("Sort & Filter", 150, 112, 72, 58),
            new RibbonAdaptiveGroup("Data Tools", 210, 168, 92, 58),
            new RibbonAdaptiveGroup("Forecast", 125, 96, 58, 58),
            new RibbonAdaptiveGroup("Outline", 120, 92, 54, 58)
        };

        var layout = RibbonAdaptiveLayoutEngine.Plan(1120, groups, fixedChromeWidth: 42);

        layout.States[Array.IndexOf(groups.Select(group => group.Name).ToArray(), "Queries & Connections")]
            .Should()
            .Be(RibbonAdaptiveGroupState.Collapsed);
        layout.States[Array.IndexOf(groups.Select(group => group.Name).ToArray(), "Data Tools")]
            .Should()
            .Be(RibbonAdaptiveGroupState.IconOnly);
        layout.States[Array.IndexOf(groups.Select(group => group.Name).ToArray(), "Forecast")]
            .Should()
            .Be(RibbonAdaptiveGroupState.Full);
        layout.PlannedWidth.Should().BeLessThanOrEqualTo(1120);
        layout.RequiresMeasuredCorrection.Should().BeTrue();
    }

    [Fact]
    public void Plan_AppliesDataRuntimeVisibilityStateBeforeMeasuringPlannedWidth()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Get & Transform Data", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Queries & Connections", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Data Types", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Sort & Filter", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Data Tools", 300, 200, 70, 40),
            new RibbonAdaptiveGroup("Forecast", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Outline", 100, 80, 60, 40)
        };

        var layout = RibbonAdaptiveLayoutEngine.Plan(900, groups, fixedChromeWidth: 20);

        layout.States.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.IconOnly,
            RibbonAdaptiveGroupState.IconOnly,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Collapsed);
        layout.PlannedWidth.Should().Be(470);
        layout.RequiresMeasuredCorrection.Should().BeTrue();
    }

    [Fact]
    public void Plan_UsesSelectedTabHeaderWhenOptionalDataGroupsAreHidden()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Get & Transform Data", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Sort & Filter", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Data Tools", 300, 200, 70, 40)
        };

        RibbonAdaptiveLayoutEngine.Plan(900, groups, fixedChromeWidth: 20)
            .States
            .Should()
            .Equal(
                RibbonAdaptiveGroupState.Full,
                RibbonAdaptiveGroupState.Collapsed,
                RibbonAdaptiveGroupState.Collapsed);

        var layout = RibbonAdaptiveLayoutEngine.Plan(900, groups, fixedChromeWidth: 20, selectedTabHeader: "Data");

        layout.States.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.IconOnly,
            RibbonAdaptiveGroupState.IconOnly);
        layout.PlannedWidth.Should().Be(250);
        layout.RequiresMeasuredCorrection.Should().BeTrue();
    }

    [Fact]
    public void Plan_AppliesInsertRuntimeVisibilityStateBeforeMeasuringPlannedWidth()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Tables", 650, 90, 60, 40),
            new RibbonAdaptiveGroup("Illustrations", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Add-ins", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Charts", 260, 180, 100, 40)
        };

        var layout = RibbonAdaptiveLayoutEngine.Plan(900, groups, fixedChromeWidth: 20);

        layout.States.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed);
        layout.PlannedWidth.Should().Be(850);
        layout.RequiresMeasuredCorrection.Should().BeTrue();
    }

    [Fact]
    public void Plan_ExpandsProtectedInsertTablesByCollapsingLowerPriorityGroups()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Tables", 320, 100, 60, 40),
            new RibbonAdaptiveGroup("Illustrations", 420, 100, 70, 40),
            new RibbonAdaptiveGroup("Add-ins", 150, 100, 70, 40),
            new RibbonAdaptiveGroup("Charts", 150, 100, 70, 40)
        };

        var layout = RibbonAdaptiveLayoutEngine.Plan(780, groups, fixedChromeWidth: 0, selectedTabHeader: "Insert");

        layout.States[0].Should().Be(RibbonAdaptiveGroupState.Full);
        layout.States[1].Should().NotBe(RibbonAdaptiveGroupState.Full);
        layout.States[2].Should().Be(RibbonAdaptiveGroupState.Collapsed);
        layout.States[3].Should().Be(RibbonAdaptiveGroupState.Collapsed);
        layout.PlannedWidth.Should().BeLessThanOrEqualTo(780);
    }

    [Fact]
    public void BuildResizeThresholds_UsesRuntimeVisibilityStatesFromPurePlan()
    {
        var dataGroups = new[]
        {
            new RibbonAdaptiveGroup("Get & Transform Data", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Queries & Connections", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Data Types", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Sort & Filter", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Data Tools", 300, 200, 70, 40),
            new RibbonAdaptiveGroup("Forecast", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Outline", 100, 80, 60, 40)
        };
        var insertGroups = new[]
        {
            new RibbonAdaptiveGroup("Tables", 650, 90, 60, 40),
            new RibbonAdaptiveGroup("Illustrations", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Add-ins", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Charts", 260, 180, 100, 40)
        };

        RibbonAdaptiveLayoutEngine.BuildResizeThresholds(dataGroups, fixedChromeWidth: 20)
            .Should()
            .Contain(470);
        RibbonAdaptiveLayoutEngine.BuildResizeThresholds(insertGroups, fixedChromeWidth: 20)
            .Should()
            .Contain(850);
    }

    [Fact]
    public void BuildResizeThresholds_ReturnsProfileSpecificSortedBreakpointsForResizeGate()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Clipboard", 120, 86, 62, 50),
            new RibbonAdaptiveGroup("Font", 220, 156, 96, 52),
            new RibbonAdaptiveGroup("Alignment", 190, 132, 88, 56),
            new RibbonAdaptiveGroup("Number", 150, 112, 76, 54)
        };

        var thresholds = RibbonAdaptiveLayoutEngine.BuildResizeThresholds(groups, fixedChromeWidth: 36);

        thresholds.Should().BeInAscendingOrder();
        thresholds.Should().Contain([700, 760, 900, 920, 1300, 1500]);
        thresholds.Should().NotContain(1120, "Home does not change adaptive state at 1120 once profile rules remove redundant breakpoint bands");
        thresholds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void BuildResizeThresholds_SourceAvoidsRedundantSortedSetLinqPasses()
    {
        var source = System.IO.File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RibbonAdaptiveLayoutEngine.cs"));
        var method = source.Substring(
            source.IndexOf("public static IReadOnlyList<double> BuildResizeThresholds", StringComparison.Ordinal),
            source.IndexOf("public static IReadOnlyList<int> GetExpandableGroupIndexes", StringComparison.Ordinal) -
            source.IndexOf("public static IReadOnlyList<double> BuildResizeThresholds", StringComparison.Ordinal));

        method.Should().Contain("new List<double>(thresholds.Count)");
        method.Should().NotContain(".Distinct()");
        method.Should().NotContain(".OrderBy(");
        method.Should().NotContain(".ToList()");
    }

    [Fact]
    public void Plan_SourceStaysFreeOfWpfVisualTreeMeasurementWork()
    {
        var source = System.IO.File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "RibbonAdaptiveLayoutEngine.cs"));

        source.Should().NotContain("System.Windows", "the adaptive planner should remain CI-safe and independent of WPF runtime state");
        source.Should().NotContain("FrameworkElement", "the adaptive planner should operate on measured group data rather than controls");
        source.Should().NotContain("VisualTreeHelper", "visual-tree walking belongs in the WPF adapter, not the pure layout planner");
        source.Should().NotContain("Dispatcher", "resize planning should not schedule UI work while computing a layout");
        source.Should().NotContain(".Measure(", "WPF measurement should stay outside the pure layout planner");
    }

    [Fact]
    public void BuildResizeThresholds_KeepsGenericFallbackBreakpointsForUnknownTabs()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Review", 120, 86, 62, 50),
            new RibbonAdaptiveGroup("Comments", 140, 100, 70, 52),
            new RibbonAdaptiveGroup("Protect", 150, 110, 76, 54)
        };

        var thresholds = RibbonAdaptiveLayoutEngine.BuildResizeThresholds(groups, fixedChromeWidth: 24);

        thresholds.Should().Contain([700, 760, 920, 1120, 1320]);
        thresholds.Should().BeInAscendingOrder();
        thresholds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TryCollapseOneMoreGroup_RespectsProtectedIndexes()
    {
        var states = new[]
        {
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full
        };

        var collapsed = RibbonAdaptiveLayoutEngine.TryCollapseOneMoreGroup(
            states,
            preserveFirstGroup: true,
            protectedGroupIndexes: new HashSet<int> { 2 });

        collapsed.Should().BeTrue();
        states.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Full);
    }

    [Fact]
    public void TryCollapseOneMoreGroup_PreservesFirstGroupWhenRequested()
    {
        var states = new[]
        {
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed
        };

        var collapsed = RibbonAdaptiveLayoutEngine.TryCollapseOneMoreGroup(
            states,
            preserveFirstGroup: true);

        collapsed.Should().BeFalse();
        states.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Collapsed,
            RibbonAdaptiveGroupState.Collapsed);
    }

    [Theory]
    [InlineData(RibbonAdaptiveGroupState.Collapsed, RibbonAdaptiveGroupState.IconOnly, true)]
    [InlineData(RibbonAdaptiveGroupState.IconOnly, RibbonAdaptiveGroupState.SmallWithLabels, true)]
    [InlineData(RibbonAdaptiveGroupState.SmallWithLabels, RibbonAdaptiveGroupState.Full, true)]
    [InlineData(RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.Full, false)]
    public void TryGetNextExpandedState_ExpandsOneStepUntilFull(
        RibbonAdaptiveGroupState currentState,
        RibbonAdaptiveGroupState expectedState,
        bool expectedResult)
    {
        var result = RibbonAdaptiveLayoutEngine.TryGetNextExpandedState(currentState, out var expandedState);

        result.Should().Be(expectedResult);
        expandedState.Should().Be(expectedState);
    }

    [Fact]
    public void Plan_RelaxesProtectedFallbacksWhenPriorityGroupsStillOverflow()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Get & Transform Data", 170, 130, 78, 58),
            new RibbonAdaptiveGroup("Queries & Connections", 155, 118, 70, 58),
            new RibbonAdaptiveGroup("Data Types", 140, 110, 66, 58),
            new RibbonAdaptiveGroup("Sort & Filter", 150, 112, 72, 58),
            new RibbonAdaptiveGroup("Data Tools", 500, 168, 92, 58),
            new RibbonAdaptiveGroup("Forecast", 420, 96, 58, 58),
            new RibbonAdaptiveGroup("Outline", 120, 92, 54, 58)
        };

        var layout = RibbonAdaptiveLayoutEngine.Plan(820, groups, fixedChromeWidth: 42);
        var groupNames = groups.Select(group => group.Name).ToArray();

        layout.PlannedWidth.Should().BeLessThanOrEqualTo(820);
        layout.States[Array.IndexOf(groupNames, "Data Tools")]
            .Should()
            .NotBe(RibbonAdaptiveGroupState.Full);
        layout.States[Array.IndexOf(groupNames, "Forecast")]
            .Should()
            .Be(RibbonAdaptiveGroupState.Full, "Forecast is a protected Data tab group and should remain expanded when lower-priority groups can be collapsed instead");
    }
}
