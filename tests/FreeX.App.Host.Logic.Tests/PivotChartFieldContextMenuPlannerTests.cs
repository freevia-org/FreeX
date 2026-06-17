using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class PivotChartFieldContextMenuPlannerTests
{
    private static PivotChartFieldContextMenuState FullyEnabledState() =>
        new(
            HasFilterState: true,
            OverallSummary: "Active filters for Region: Item filter: \"East\"",
            SelectItemsHeader: "Select Items... (1 selected)",
            LabelFilterHeader: "Label Filter... (equals \"East\")",
            ValueFilterHeader: "Value Filter...",
            ClearFilterHeader: "Clear Filters from \"Region\"",
            CanValueFilter: true,
            HasAnyFilter: true,
            CanValueFieldSettings: true);

    private static PivotChartFieldContextMenuState NoFilterStateNoValueField() =>
        new(
            HasFilterState: false,
            OverallSummary: "",
            SelectItemsHeader: "Select Items...",
            LabelFilterHeader: "Label Filter...",
            ValueFilterHeader: "Value Filter...",
            ClearFilterHeader: "Clear Filters from Field",
            CanValueFilter: false,
            HasAnyFilter: false,
            CanValueFieldSettings: false);

    [Fact]
    public void BuildCommands_WithFilterState_EmitsSummaryBannerThenSortFilterSettings()
    {
        var commands = PivotChartFieldContextMenuPlanner.BuildCommands(FullyEnabledState());

        commands
            .Select(command => command.IsSeparator ? "—" : command.Action.ToString())
            .Should()
            .Equal(
                "Summary",
                "—",
                "SortAscending",
                "SortDescending",
                "MoreSortOptions",
                "—",
                "SelectItems",
                "LabelFilter",
                "ValueFilter",
                "ClearFilter",
                "—",
                "ValueFieldSettings");
    }

    [Fact]
    public void BuildCommands_WithoutFilterState_OmitsSummaryBannerAndLeadingSeparator()
    {
        var commands = PivotChartFieldContextMenuPlanner.BuildCommands(NoFilterStateNoValueField());

        commands
            .Select(command => command.IsSeparator ? "—" : command.Action.ToString())
            .Should()
            .Equal(
                "SortAscending",
                "SortDescending",
                "MoreSortOptions",
                "—",
                "SelectItems",
                "LabelFilter",
                "ValueFilter",
                "ClearFilter",
                "—",
                "ValueFieldSettings");
    }

    [Fact]
    public void BuildCommands_SummaryBanner_IsDisabledAndCarriesOverallSummaryAndTooltip()
    {
        var banner = PivotChartFieldContextMenuPlanner.BuildCommands(FullyEnabledState())[0];

        banner.Action.Should().Be(PivotChartFieldContextMenuAction.Summary);
        banner.IsEnabled.Should().BeFalse();
        banner.Header.Should().Be("Active filters for Region: Item filter: \"East\"");
        banner.ToolTip.Should().Be("Current filter state for this PivotTable field.");
    }

    [Fact]
    public void BuildCommands_SortAToZAndZToA_AreAlwaysEnabledWithLiteralHeaders()
    {
        var enabled = PivotChartFieldContextMenuPlanner.BuildCommands(FullyEnabledState());
        var disabled = PivotChartFieldContextMenuPlanner.BuildCommands(NoFilterStateNoValueField());

        foreach (var commands in new[] { enabled, disabled })
        {
            var sortAsc = commands.Single(command => command.Action == PivotChartFieldContextMenuAction.SortAscending);
            var sortDesc = commands.Single(command => command.Action == PivotChartFieldContextMenuAction.SortDescending);

            sortAsc.Header.Should().Be("Sort A to Z");
            sortAsc.IsEnabled.Should().BeTrue();
            sortDesc.Header.Should().Be("Sort Z to A");
            sortDesc.IsEnabled.Should().BeTrue();
        }
    }

    [Fact]
    public void BuildCommands_MoreSortOptions_EnabledOnlyWithFilterStateAndCarriesTooltip()
    {
        var enabled = PivotChartFieldContextMenuPlanner.BuildCommands(FullyEnabledState())
            .Single(command => command.Action == PivotChartFieldContextMenuAction.MoreSortOptions);
        var disabled = PivotChartFieldContextMenuPlanner.BuildCommands(NoFilterStateNoValueField())
            .Single(command => command.Action == PivotChartFieldContextMenuAction.MoreSortOptions);

        enabled.Header.Should().Be("More Sort Options...");
        enabled.IsEnabled.Should().BeTrue();
        enabled.ToolTip.Should().Be("Open PivotTable sort options for this field.");
        disabled.IsEnabled.Should().BeFalse();
        disabled.ToolTip.Should().Be("Open PivotTable sort options for this field.");
    }

    [Fact]
    public void BuildCommands_FilterItems_UseDynamicHeadersAndEnablement()
    {
        var enabled = PivotChartFieldContextMenuPlanner.BuildCommands(FullyEnabledState());

        var selectItems = enabled.Single(command => command.Action == PivotChartFieldContextMenuAction.SelectItems);
        var labelFilter = enabled.Single(command => command.Action == PivotChartFieldContextMenuAction.LabelFilter);
        var valueFilter = enabled.Single(command => command.Action == PivotChartFieldContextMenuAction.ValueFilter);
        var clearFilter = enabled.Single(command => command.Action == PivotChartFieldContextMenuAction.ClearFilter);

        selectItems.Header.Should().Be("Select Items... (1 selected)");
        selectItems.IsEnabled.Should().BeTrue();
        labelFilter.Header.Should().Be("Label Filter... (equals \"East\")");
        labelFilter.IsEnabled.Should().BeTrue();
        valueFilter.Header.Should().Be("Value Filter...");
        valueFilter.IsEnabled.Should().BeTrue();
        clearFilter.Header.Should().Be("Clear Filters from \"Region\"");
        clearFilter.IsEnabled.Should().BeTrue();
        clearFilter.ToolTip.Should().BeNull();
    }

    [Fact]
    public void BuildCommands_FilterItems_DisabledWithoutFilterStateAndClearCarriesDisabledTooltip()
    {
        var disabled = PivotChartFieldContextMenuPlanner.BuildCommands(NoFilterStateNoValueField());

        disabled.Single(command => command.Action == PivotChartFieldContextMenuAction.SelectItems).IsEnabled.Should().BeFalse();
        disabled.Single(command => command.Action == PivotChartFieldContextMenuAction.LabelFilter).IsEnabled.Should().BeFalse();
        disabled.Single(command => command.Action == PivotChartFieldContextMenuAction.ValueFilter).IsEnabled.Should().BeFalse();

        var clearFilter = disabled.Single(command => command.Action == PivotChartFieldContextMenuAction.ClearFilter);
        clearFilter.IsEnabled.Should().BeFalse();
        clearFilter.Header.Should().Be("Clear Filters from Field");
        clearFilter.ToolTip.Should().Be("No item, label, or value filters are active for this field.");
    }

    [Fact]
    public void BuildCommands_ValueFilter_RequiresFilterStateAndDataField()
    {
        var stateWithoutDataField = FullyEnabledState() with { CanValueFilter = false };

        PivotChartFieldContextMenuPlanner.BuildCommands(stateWithoutDataField)
            .Single(command => command.Action == PivotChartFieldContextMenuAction.ValueFilter)
            .IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void BuildCommands_ValueFieldSettings_EnablementAndTooltipFollowCanValueFieldSettings()
    {
        var enabled = PivotChartFieldContextMenuPlanner.BuildCommands(FullyEnabledState())
            .Single(command => command.Action == PivotChartFieldContextMenuAction.ValueFieldSettings);
        var disabled = PivotChartFieldContextMenuPlanner.BuildCommands(NoFilterStateNoValueField())
            .Single(command => command.Action == PivotChartFieldContextMenuAction.ValueFieldSettings);

        enabled.Header.Should().Be("Value Field Settings...");
        enabled.IsEnabled.Should().BeTrue();
        enabled.ToolTip.Should().Be("Open settings for the relevant PivotTable value field.");

        disabled.IsEnabled.Should().BeFalse();
        disabled.ToolTip.Should().Be("Select a value field, the PivotChart Values button, or a PivotTable with one value field.");
    }

    [Fact]
    public void Separator_IsNeutralDisabledMarker()
    {
        var separator = PivotChartFieldContextMenuCommand.Separator;

        separator.IsSeparator.Should().BeTrue();
        separator.IsEnabled.Should().BeFalse();
        separator.Action.Should().Be(PivotChartFieldContextMenuAction.None);
        separator.ToolTip.Should().BeNull();
    }
}
