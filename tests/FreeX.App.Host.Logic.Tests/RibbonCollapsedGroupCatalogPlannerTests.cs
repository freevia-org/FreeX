using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class RibbonCollapsedGroupCatalogPlannerTests
{
    [Theory]
    [InlineData("DataSortFilterGroup", "Filters", null, "Sort & Filter")]
    [InlineData("DataToolsGroup", "Cleanup", null, "Data Tools")]
    [InlineData("FormulasFormulaAuditingGroup", "Auditing", null, "Formula Auditing")]
    [InlineData("ReviewCommentsGroup", "Notes", null, "Comments")]
    [InlineData("ViewWindowGroup", "Views", null, "Window")]
    [InlineData("TableDesignStyleOptionsGroup", "Style Options", "MainWindow_Content_Options", "Table Style Options")]
    [InlineData("TableDesignStylesGroup", "Table Looks", "MainWindow_Content_Styles", "Table Styles")]
    [InlineData("PivotTableAnalyzeCalculationsGroup", "Pivot Math", null, "Calculations")]
    [InlineData("PivotTableDesignStyleOptionsGroup", "Pivot Options", "MainWindow_Content_Options", "PivotTable Style Options")]
    [InlineData("PivotTableDesignStylesGroup", "Pivot Styles", "MainWindow_Content_Styles", "PivotTable Styles")]
    [InlineData("CustomGroup", "Custom Caption", null, "Custom Caption")]
    public void PlanPresentation_MapsFreeXCatalogIdsToNeutralCollapsedGroupPresentation(
        string? catalogId,
        string groupName,
        string? expectedResourceKey,
        string expectedIconKey)
    {
        var presentation = RibbonCollapsedGroupCatalogPlanner.PlanPresentation(catalogId, groupName);

        presentation.FallbackDisplayName.Should().Be(groupName);
        presentation.DisplayResourceKey.Should().Be(expectedResourceKey);
        presentation.IconKey.Should().Be(expectedIconKey);
    }

    [Fact]
    public void ResolveDisplayName_KeepsLocalizationInHostAdapter()
    {
        var presentation = RibbonCollapsedGroupCatalogPlanner.PlanPresentation(
            "TableDesignStyleOptionsGroup",
            "Style Options");

        presentation.ResolveDisplayName(key => $"localized:{key}")
            .Should()
            .Be("localized:MainWindow_Content_Options");

        RibbonCollapsedGroupCatalogPlanner
            .PlanPresentation("CustomGroup", "Custom Caption")
            .ResolveDisplayName(_ => throw new InvalidOperationException("Fallback captions should not resolve resources."))
            .Should()
            .Be("Custom Caption");
    }

    [Theory]
    [InlineData("DataSortFilterGroup", 1200, RibbonAdaptiveGroupState.Collapsed)]
    [InlineData("DataSortFilterGroup", 1320, RibbonAdaptiveGroupState.IconOnly)]
    [InlineData("DataToolsGroup", 900, RibbonAdaptiveGroupState.Full)]
    [InlineData("DataToolsGroup", 700, RibbonAdaptiveGroupState.SmallWithLabels)]
    [InlineData("InsertChartsGroup", 900, RibbonAdaptiveGroupState.SmallWithLabels)]
    [InlineData("CustomGroup", 900, RibbonAdaptiveGroupState.IconOnly)]
    public void NormalizeIconOnlyState_AppliesFreeXCatalogSpecificAdaptivePolicy(
        string catalogId,
        double availableWidth,
        RibbonAdaptiveGroupState expectedState)
    {
        RibbonCollapsedGroupCatalogPlanner
            .NormalizeIconOnlyState(catalogId, RibbonAdaptiveGroupState.IconOnly, availableWidth)
            .Should()
            .Be(expectedState);
    }

    [Fact]
    public void ShouldKeepLabelsAtIconWidth_PreservesTablesCaptionAtWideIconBreakpoints()
    {
        RibbonCollapsedGroupCatalogPlanner
            .ShouldKeepLabelsAtIconWidth("Tables", RibbonAdaptiveGroupState.IconOnly, 900)
            .Should()
            .BeTrue();

        RibbonCollapsedGroupCatalogPlanner
            .ShouldKeepLabelsAtIconWidth("Charts", RibbonAdaptiveGroupState.IconOnly, 900)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void IsDataAdaptiveSurface_UsesStableTabOrGroupIdentity()
    {
        RibbonCollapsedGroupCatalogPlanner.IsDataAdaptiveSurface([], "Data")
            .Should()
            .BeTrue();
        RibbonCollapsedGroupCatalogPlanner.IsDataAdaptiveSurface([], "DataTab")
            .Should()
            .BeTrue();
        RibbonCollapsedGroupCatalogPlanner.IsDataAdaptiveSurface(
                [Group("Cleanup", "DataToolsGroup")],
                selectedTabHeader: null)
            .Should()
            .BeTrue();
        RibbonCollapsedGroupCatalogPlanner.IsDataAdaptiveSurface(
                [Group("Data Tools")],
                selectedTabHeader: null)
            .Should()
            .BeTrue("the legacy caption remains a compatibility fallback");
        RibbonCollapsedGroupCatalogPlanner.IsDataAdaptiveSurface(
                [Group("Charts", "InsertChartsGroup")],
                selectedTabHeader: "InsertTab")
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData(760, RibbonAdaptiveGroupState.Collapsed, RibbonAdaptiveGroupState.IconOnly)]
    [InlineData(761, RibbonAdaptiveGroupState.Collapsed, RibbonAdaptiveGroupState.Full)]
    [InlineData(1300, RibbonAdaptiveGroupState.Collapsed, RibbonAdaptiveGroupState.Full)]
    [InlineData(1301, RibbonAdaptiveGroupState.Full, RibbonAdaptiveGroupState.Full)]
    public void NormalizeDataSurfaceStates_AppliesSharedDataThresholds(
        double availableWidth,
        RibbonAdaptiveGroupState expectedSortFilterState,
        RibbonAdaptiveGroupState expectedDataToolsState)
    {
        var groups = new[]
        {
            Group("Imported Data", "DataGetTransformGroup"),
            Group("Filters", "DataSortFilterGroup"),
            Group("Cleanup", "DataToolsGroup")
        };
        var plannedStates = new[]
        {
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.Full,
            RibbonAdaptiveGroupState.IconOnly
        };

        var normalizedStates = RibbonCollapsedGroupCatalogPlanner.NormalizeDataSurfaceStates(
            groups,
            plannedStates,
            availableWidth,
            selectedTabHeader: "DataTab");

        normalizedStates.Should().Equal(
            RibbonAdaptiveGroupState.Full,
            expectedSortFilterState,
            expectedDataToolsState);
        plannedStates.Should().Equal(
            [
                RibbonAdaptiveGroupState.Full,
                RibbonAdaptiveGroupState.Full,
                RibbonAdaptiveGroupState.IconOnly
            ],
            "normalization must not mutate the cached pure-layout result");
    }

    [Theory]
    [InlineData(760, -1)]
    [InlineData(761, 0)]
    public void PlanDataPrimaryCorrection_UsesSharedProtectionThreshold(
        double availableWidth,
        int expectedIndex)
    {
        var groups = new[]
        {
            Group("Imported Data", "DataGetTransformGroup"),
            Group("Cleanup", "DataToolsGroup")
        };

        var correction = RibbonCollapsedGroupCatalogPlanner.PlanDataPrimaryCorrection(
            groups,
            [RibbonAdaptiveGroupState.Collapsed, RibbonAdaptiveGroupState.Full],
            availableWidth,
            selectedTabHeader: "DataTab");

        correction?.Index.Should().Be(expectedIndex >= 0 ? expectedIndex : null);
        if (correction is not null)
            correction.Value.State.Should().Be(RibbonAdaptiveGroupState.Full);
    }

    [Fact]
    public void PlanMeasuredOverflowProtection_RelaxesDataGroupsInSharedPolicy()
    {
        var groups = new[]
        {
            Group("Imported Data", "DataGetTransformGroup"),
            Group("Filters", "DataSortFilterGroup"),
            Group("Cleanup", "DataToolsGroup"),
            Group("Forecast", "DataForecastGroup")
        };
        var groupProfileKeys = groups.Select(group => group.CatalogId!).ToArray();

        var mediumPlan = RibbonCollapsedGroupCatalogPlanner.PlanMeasuredOverflowProtection(
            groups,
            groupProfileKeys,
            availableWidth: 1120,
            selectedTabHeader: "DataTab");

        mediumPlan.RuntimeVisibilityProtectedGroupIndexes.Should().NotContain(1);
        mediumPlan.InitialFallbackProtectedGroupIndexes.Should().NotContain(1);
        mediumPlan.RuntimeVisibilityProtectedGroupIndexes.Should().Contain(2);
        mediumPlan.PreserveFirstGroupDuringInitialFallback.Should().BeTrue();

        var narrowPlan = RibbonCollapsedGroupCatalogPlanner.PlanMeasuredOverflowProtection(
            groups,
            groupProfileKeys,
            availableWidth: 760,
            selectedTabHeader: "DataTab");

        narrowPlan.RuntimeVisibilityProtectedGroupIndexes.Should().NotContain([1, 2]);
        narrowPlan.InitialFallbackProtectedGroupIndexes.Should().NotContain([1, 2]);
        narrowPlan.PreserveFirstGroupDuringInitialFallback.Should().BeFalse();
    }

    [Fact]
    public void CatalogPolicy_SourceLivesInRibbonDefinitionsInsteadOfWpfHost()
    {
        var hostAdaptiveSource = DialogSourceTestSupport.ReadHostSources("MainWindow.RibbonAdaptive.cs");
        var hostApplicatorSource = DialogSourceTestSupport.ReadHostSources("RibbonAdaptiveStateApplicator.cs");
        var definitionSource = DialogSourceTestSupport.ReadRibbonDefinitionSource(
            "RibbonCollapsedGroupCatalogPlanner.cs");

        hostAdaptiveSource.Should().Contain("RibbonCollapsedGroupCatalogPlanner.PlanPresentation(");
        hostAdaptiveSource.Should().Contain("RibbonCollapsedGroupCatalogPlanner.NormalizeDataSurfaceStates(");
        hostAdaptiveSource.Should().Contain("RibbonCollapsedGroupCatalogPlanner.PlanDataPrimaryCorrection(");
        hostAdaptiveSource.Should().Contain("RibbonCollapsedGroupCatalogPlanner.PlanMeasuredOverflowProtection(");
        hostAdaptiveSource.Should().NotContain("GetCollapsedRibbonGroupDisplayName");
        hostAdaptiveSource.Should().NotContain("GetCollapsedRibbonGroupIconKey");
        hostAdaptiveSource.Should().NotContain("IsDataRibbonAdaptiveSurface");
        hostAdaptiveSource.Should().NotContain("TryFindRibbonAdaptiveGroupIndex");
        hostAdaptiveSource.Should().NotContain("RelaxMeasuredDataOverflowProtection");
        hostAdaptiveSource.Should().NotContain("DataGetTransformGroup");
        hostAdaptiveSource.Should().NotContain("DataSortFilterGroup");
        hostAdaptiveSource.Should().NotContain("DataToolsGroup");
        hostApplicatorSource.Should().Contain("RibbonCollapsedGroupCatalogPlanner.NormalizeIconOnlyState(");
        hostApplicatorSource.Should().NotContain("catalogId is \"DataSortFilterGroup\"");
        definitionSource.Should().Contain("catalogId is \"DataSortFilterGroup\"");
        definitionSource.Should().NotContain("System.Windows");
        definitionSource.Should().NotContain("FrameworkElement");
    }

    private static RibbonAdaptiveGroup Group(string name, string? catalogId = null) =>
        new(name, 100, 80, 60, 40, catalogId);
}
