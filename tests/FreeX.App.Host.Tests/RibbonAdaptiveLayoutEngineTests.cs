using FluentAssertions;
using System.Diagnostics;

namespace FreeX.App.Host.Tests;

public sealed class RibbonAdaptiveLayoutEngineTests
{
    [Fact]
    public void LayoutEngine_SourceLivesInRibbonDefinitions()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        System.IO.File.Exists(System.IO.Path.Combine(repoRoot, "src", "FreeX.App.Host", "RibbonAdaptiveLayoutEngine.cs"))
            .Should()
            .BeFalse("the pure adaptive planner should live outside the WPF host adapter");

        var source = DialogSourceTestSupport.ReadRibbonDefinitionSource("RibbonAdaptiveLayoutEngine.cs");

        source.Should().Contain("namespace FreeX.Ribbon.Definitions;");
        source.Should().Contain("public static class RibbonAdaptiveLayoutEngine");
    }

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
            new RibbonAdaptiveGroup("Sort & Filter", 150, 112, 72, 58),
            new RibbonAdaptiveGroup("Data Tools", 210, 168, 92, 58),
            new RibbonAdaptiveGroup("Forecast", 125, 96, 58, 58),
            new RibbonAdaptiveGroup("Outline", 120, 92, 54, 58)
        };

        var layout = RibbonAdaptiveLayoutEngine.Plan(1120, groups, fixedChromeWidth: 42);

        layout.States[Array.IndexOf(groups.Select(group => group.Name).ToArray(), "Queries & Connections")]
            .Should()
            .Be(RibbonAdaptiveGroupState.Full, "the planner should spend spare horizontal space after applying Data tab priority fallbacks");
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
            new RibbonAdaptiveGroup("Sort & Filter", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Data Tools", 300, 200, 70, 40),
            new RibbonAdaptiveGroup("Forecast", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Outline", 100, 80, 60, 40)
        };

        var layout = RibbonAdaptiveLayoutEngine.Plan(900, groups, fixedChromeWidth: 20);

        layout.States.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.IconOnly,
            RibbonAdaptiveGroupState.IconOnly,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full);
        layout.PlannedWidth.Should().Be(550);
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
                RibbonAdaptiveGroupState.Full,
                RibbonAdaptiveGroupState.Full);

        var layout = RibbonAdaptiveLayoutEngine.Plan(900, groups, fixedChromeWidth: 20, selectedTabHeader: "Data");

        layout.States.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.IconOnly,
            RibbonAdaptiveGroupState.IconOnly);
        layout.PlannedWidth.Should().Be(250);
        layout.RequiresMeasuredCorrection.Should().BeTrue();
    }

    [Fact]
    public void Plan_UsesCatalogIdsWhenDataGroupCaptionsChange()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Imported Data", 100, 80, 60, 40, CatalogId: "DataGetTransformGroup"),
            new RibbonAdaptiveGroup("Filters", 100, 80, 60, 40, CatalogId: "DataSortFilterGroup"),
            new RibbonAdaptiveGroup("Cleanup", 300, 200, 70, 40, CatalogId: "DataToolsGroup")
        };

        var layout = RibbonAdaptiveLayoutEngine.Plan(900, groups, fixedChromeWidth: 20, selectedTabHeader: "DataTab");

        layout.States.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.IconOnly,
            RibbonAdaptiveGroupState.IconOnly);
        layout.PlannedWidth.Should().Be(250);
        layout.RequiresMeasuredCorrection.Should().BeTrue();
    }

    [Fact]
    public void Plan_UsesCatalogIdsForDuplicateGroupCaptions()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Object Creation", 100, 80, 60, 40, CatalogId: "DrawIllustrationsGroup"),
            new RibbonAdaptiveGroup("Object Placement", 100, 80, 60, 40, CatalogId: "DrawArrangeGroup"),
            new RibbonAdaptiveGroup("Object Formatting", 100, 80, 60, 40, CatalogId: "DrawFormatGroup")
        };

        var layout = RibbonAdaptiveLayoutEngine.Plan(1120, groups, fixedChromeWidth: 0, selectedTabHeader: "DrawTab");

        layout.States.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full);
        layout.PlannedWidth.Should().Be(300);
    }

    [Fact]
    public void Plan_KeepsInsertChartsVisibleAtNormalNarrowWidths()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Tables", 650, 90, 60, 40),
            new RibbonAdaptiveGroup("Sparklines", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Charts", 260, 180, 100, 40)
        };

        var layout = RibbonAdaptiveLayoutEngine.Plan(900, groups, fixedChromeWidth: 20);

        layout.States[Array.IndexOf(groups.Select(group => group.Name).ToArray(), "Charts")]
            .Should()
            .NotBe(RibbonAdaptiveGroupState.Collapsed);
        layout.PlannedWidth.Should().BeLessThanOrEqualTo(900);
        layout.RequiresMeasuredCorrection.Should().BeTrue();
    }

    [Fact]
    public void Plan_ReopensFormulaGroupsAtNarrowWidthsWhenTheyFit()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Function Library", 180, 120, 70, 52),
            new RibbonAdaptiveGroup("Defined Names", 130, 96, 58, 52),
            new RibbonAdaptiveGroup("Formula Auditing", 180, 120, 70, 52),
            new RibbonAdaptiveGroup("Calculation", 130, 96, 58, 52)
        };

        var layout = RibbonAdaptiveLayoutEngine.Plan(750, groups, fixedChromeWidth: 36, selectedTabHeader: "Formulas");

        layout.States.Should().OnlyContain(state => state == RibbonAdaptiveGroupState.Full);
        layout.PlannedWidth.Should().BeLessThanOrEqualTo(750);
    }

    [Fact]
    public void Plan_ExpandsProtectedInsertTablesAndChartsByCollapsingLowerPriorityGroups()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Tables", 320, 100, 60, 40),
            new RibbonAdaptiveGroup("Charts", 150, 100, 70, 40),
            new RibbonAdaptiveGroup("Sparklines", 420, 100, 70, 40)
        };

        var layout = RibbonAdaptiveLayoutEngine.Plan(780, groups, fixedChromeWidth: 0, selectedTabHeader: "Insert");

        layout.States[0].Should().Be(RibbonAdaptiveGroupState.Full);
        layout.States[1].Should().Be(RibbonAdaptiveGroupState.Full);
        layout.States[2].Should().NotBe(RibbonAdaptiveGroupState.Full);
        layout.PlannedWidth.Should().BeLessThanOrEqualTo(780);
    }

    [Fact]
    public void BuildResizeThresholds_UsesRuntimeVisibilityStatesFromPurePlan()
    {
        var dataGroups = new[]
        {
            new RibbonAdaptiveGroup("Get & Transform Data", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Queries & Connections", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Sort & Filter", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Data Tools", 300, 200, 70, 40),
            new RibbonAdaptiveGroup("Forecast", 100, 80, 60, 40),
            new RibbonAdaptiveGroup("Outline", 100, 80, 60, 40)
        };
        var insertGroups = new[]
        {
            new RibbonAdaptiveGroup("Tables", 650, 90, 60, 40),
            new RibbonAdaptiveGroup("Charts", 260, 180, 100, 40),
            new RibbonAdaptiveGroup("Sparklines", 100, 80, 60, 40)
        };

        RibbonAdaptiveLayoutEngine.BuildResizeThresholds(dataGroups, fixedChromeWidth: 20)
            .Should()
            .Contain(430);
        RibbonAdaptiveLayoutEngine.BuildResizeThresholds(insertGroups, fixedChromeWidth: 20)
            .Should()
            .Contain(910);
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
        thresholds.Should().Contain([700, 900, 920, 1300, 1500]);
        thresholds.Should().NotContain(1120, "Home does not change adaptive state at 1120 once profile rules remove redundant breakpoint bands");
        thresholds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void BuildResizeThresholds_SourceAvoidsRedundantSortedSetLinqPasses()
    {
        var source = DialogSourceTestSupport.ReadRibbonDefinitionSource("RibbonAdaptiveLayoutEngine.cs");
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
    public void BreakpointThresholds_SourceAvoidsRedundantSortedSetLinqPasses()
    {
        var source = DialogSourceTestSupport.ReadRibbonDefinitionSource("RibbonAdaptiveTabProfiles.cs");
        var method = source.Substring(
            source.IndexOf("public static IReadOnlyList<double> GetBreakpointThresholds", StringComparison.Ordinal),
            source.IndexOf("public static string? ResolveProfileName", StringComparison.Ordinal) -
            source.IndexOf("public static IReadOnlyList<double> GetBreakpointThresholds", StringComparison.Ordinal));

        method.Should().Contain("new List<double>(thresholds.Count)");
        method.Should().NotContain(".Where(");
        method.Should().NotContain(".OrderBy(");
        method.Should().NotContain(".ToList()");
    }

    [Fact]
    public void Plan_SourceAppliesProfileOverridesInPlace()
    {
        var source = DialogSourceTestSupport.ReadRibbonDefinitionSource("RibbonAdaptiveLayoutEngine.cs");
        var planStart = source.IndexOf("public static RibbonAdaptiveLayoutResult Plan(", StringComparison.Ordinal);
        planStart = source.IndexOf("public static RibbonAdaptiveLayoutResult Plan(", planStart + 1, StringComparison.Ordinal);
        var method = source.Substring(
            planStart,
            source.IndexOf("public static IReadOnlyList<double> BuildResizeThresholds", StringComparison.Ordinal) - planStart);

        method.Should().Contain("ApplyPlanOverridesInPlace(");
        method.Should().Contain("Free.Shared.Ribbon.RibbonAdaptiveLayoutPlanner.Plan(");
        method.Should().NotContain("ApplyBreakpointOverrides(");
        method.Should().NotContain("ApplyRuntimePriorityStates(");
        method.Should().NotContain("ApplyRuntimeVisibilityStates(");
        method.Should().NotContain("states = RibbonAdaptiveTabProfiles");
        method.Should().NotContain("states = RibbonAdaptivePriorityPlanner");
        method.Should().NotContain("RibbonAdaptiveLayoutPlanner.Plan(availableWidth, groups, fixedChromeWidth).ToArray()");
    }

    [Fact]
    public void Plan_SourceUsesRollbackBuffersInsteadOfPerAttemptStateSnapshots()
    {
        var source = DialogSourceTestSupport.ReadRibbonDefinitionSource("RibbonAdaptiveLayoutEngine.cs");
        var method = source.Substring(
            source.IndexOf("private static void ExpandStatesIntoAvailableWidth", StringComparison.Ordinal),
            source.IndexOf("private static bool TryCollapseUnprotectedGroupsToFit", StringComparison.Ordinal) -
            source.IndexOf("private static void ExpandStatesIntoAvailableWidth", StringComparison.Ordinal));

        method.Should().Contain("rollbackIndexes");
        method.Should().Contain("RollbackStateChanges");
        method.Should().NotContain("states.ToArray()");
        method.Should().NotContain("Array.Copy(");
    }

    [Fact]
    public void Plan_ReviewTabDisabledPriorityExpansionPath_StaysAllocationLight()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Proofing", 140, 96, 62, 52),
            new RibbonAdaptiveGroup("Accessibility", 155, 108, 70, 52),
            new RibbonAdaptiveGroup("Comments", 170, 120, 76, 52),
            new RibbonAdaptiveGroup("Notes", 120, 86, 58, 52),
            new RibbonAdaptiveGroup("Protect", 130, 92, 60, 52)
        };
        var groupProfileKeys = new[]
        {
            "Proofing",
            "Accessibility",
            "Comments",
            "Notes",
            "Protect"
        };
        const int iterations = 10_000;

        for (var iteration = 0; iteration < 250; iteration++)
            RibbonAdaptiveLayoutEngine.Plan(1040 + iteration % 180, groups, groupProfileKeys, 24, "Review");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var layout = RibbonAdaptiveLayoutEngine.Plan(1040 + iteration % 180, groups, groupProfileKeys, 24, "Review");
            if (layout.States.Count != groups.Length)
                throw new InvalidOperationException("Ribbon adaptive layout returned an unexpected state count.");
        }

        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Console.WriteLine(
            "PERF RIBBON_REVIEW_PLAN_DISABLED_PRIORITY_EXPANSION " +
            $"steps={iterations:N0} total_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"allocated_bytes={allocated:N0}");

        allocated.Should().BeLessThan(12_000_000);
    }

    [Fact]
    public void Plan_SourceStaysFreeOfWpfVisualTreeMeasurementWork()
    {
        var source = DialogSourceTestSupport.ReadRibbonDefinitionSource("RibbonAdaptiveLayoutEngine.cs");

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
    public void TryCollapseOneMoreGroup_ReportsChangedIndexAndPreviousState()
    {
        var states = new[]
        {
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.IconOnly,
            RibbonAdaptiveGroupState.SmallWithLabels
        };

        var collapsed = RibbonAdaptiveLayoutEngine.TryCollapseOneMoreGroup(
            states,
            preserveFirstGroup: false,
            protectedGroupIndexes: null,
            out var changedIndex,
            out var previousState);

        collapsed.Should().BeTrue();
        changedIndex.Should().Be(2);
        previousState.Should().Be(RibbonAdaptiveGroupState.SmallWithLabels);
        states.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.IconOnly,
            RibbonAdaptiveGroupState.Collapsed);
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

    [Fact]
    public void TryFallbackOneMoreGroup_UsesWidthAwareStagedFallbacks()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Clipboard", 100, 100, 62, 50),
            new RibbonAdaptiveGroup("Font", 200, 150, 96, 52),
            new RibbonAdaptiveGroup("Alignment", 180, 132, 88, 56)
        };
        var states = new[]
        {
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full
        };

        RibbonAdaptiveLayoutEngine.TryFallbackOneMoreGroup(
            states,
            groups,
            preserveFirstGroup: false,
            availableWidth: 900,
            protectedGroupIndexes: null,
            changedIndex: out var firstChangedIndex,
            previousState: out var firstPreviousState)
            .Should()
            .BeTrue();
        RibbonAdaptiveLayoutEngine.TryFallbackOneMoreGroup(
            states,
            groups,
            preserveFirstGroup: false,
            availableWidth: 900,
            protectedGroupIndexes: null,
            changedIndex: out var secondChangedIndex,
            previousState: out var secondPreviousState)
            .Should()
            .BeTrue();

        firstChangedIndex.Should().Be(2);
        firstPreviousState.Should().Be(RibbonAdaptiveGroupState.Full);
        secondChangedIndex.Should().Be(1);
        secondPreviousState.Should().Be(RibbonAdaptiveGroupState.Full);
        states.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.SmallWithLabels,
            RibbonAdaptiveGroupState.SmallWithLabels);
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
    public void StateTransition_SourceDelegatesNeutralPolicyToSharedRibbon()
    {
        var source = DialogSourceTestSupport.ReadRibbonDefinitionSource("RibbonAdaptiveLayoutEngine.cs");
        var transitionSource = WorkspaceFileLocator.ReadAllText(
            "shared",
            "Free.Shared.Ribbon",
            "Layout",
            "RibbonAdaptiveStateTransitions.cs");

        source.Should().Contain("RibbonAdaptiveStateTransitions.TryGetNextExpandedState(");
        source.Should().Contain("RibbonAdaptiveStateTransitions.TryFindNextFallback(");
        source.Should().Contain("RibbonAdaptiveStateTransitions.TryApplyNextCollapse(");
        source.Should().NotContain("stateValue <= (int)RibbonAdaptiveGroupState.IconOnly");
        transitionSource.Should().Contain("stateValue <= (int)RibbonAdaptiveGroupState.IconOnly");
    }

    [Fact]
    public void Plan_RelaxesProtectedFallbacksWhenPriorityGroupsStillOverflow()
    {
        var groups = new[]
        {
            new RibbonAdaptiveGroup("Get & Transform Data", 170, 130, 78, 58),
            new RibbonAdaptiveGroup("Queries & Connections", 155, 118, 70, 58),
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
