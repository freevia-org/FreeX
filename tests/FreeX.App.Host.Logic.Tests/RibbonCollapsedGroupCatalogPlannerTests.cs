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
    public void CatalogPolicy_SourceLivesInRibbonDefinitionsInsteadOfWpfHost()
    {
        var hostAdaptiveSource = DialogSourceTestSupport.ReadHostSources("MainWindow.RibbonAdaptive.cs");
        var hostApplicatorSource = DialogSourceTestSupport.ReadHostSources("RibbonAdaptiveStateApplicator.cs");
        var definitionSource = DialogSourceTestSupport.ReadRibbonDefinitionSource(
            "RibbonCollapsedGroupCatalogPlanner.cs");

        hostAdaptiveSource.Should().Contain("RibbonCollapsedGroupCatalogPlanner.PlanPresentation(");
        hostAdaptiveSource.Should().NotContain("GetCollapsedRibbonGroupDisplayName");
        hostAdaptiveSource.Should().NotContain("GetCollapsedRibbonGroupIconKey");
        hostApplicatorSource.Should().Contain("RibbonCollapsedGroupCatalogPlanner.NormalizeIconOnlyState(");
        hostApplicatorSource.Should().NotContain("catalogId is \"DataSortFilterGroup\"");
        definitionSource.Should().Contain("catalogId is \"DataSortFilterGroup\"");
        definitionSource.Should().NotContain("System.Windows");
        definitionSource.Should().NotContain("FrameworkElement");
    }
}
