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

    public static IEnumerable<object[]> EveryStateCombination()
    {
        foreach (var hasFilterState in new[] { false, true })
        foreach (var canValueFilter in new[] { false, true })
        foreach (var hasAnyFilter in new[] { false, true })
        foreach (var canValueFieldSettings in new[] { false, true })
            yield return new object[]
            {
                hasFilterState,
                canValueFilter,
                hasAnyFilter,
                canValueFieldSettings
            };
    }

    [Theory]
    [MemberData(nameof(EveryStateCombination))]
    public void BuildCommands_ExhaustiveStateFlagsPreserveTopologyAndEnablement(
        bool hasFilterState,
        bool canValueFilter,
        bool hasAnyFilter,
        bool canValueFieldSettings)
    {
        const string summary = "Filter summary";
        const string selectItemsHeader = "Select Items... (2 selected)";
        const string labelFilterHeader = "Label Filter... (contains 'A')";
        const string valueFilterHeader = "Value Filter... (> 10)";
        const string clearFilterHeader = "Clear Filters from Region";
        var state = new PivotChartFieldContextMenuState(
            hasFilterState,
            summary,
            selectItemsHeader,
            labelFilterHeader,
            valueFilterHeader,
            clearFilterHeader,
            canValueFilter,
            hasAnyFilter,
            canValueFieldSettings);

        var commands = PivotChartFieldContextMenuPlanner.BuildCommands(state);

        commands
            .Select(command => command.IsSeparator ? "—" : command.Action.ToString())
            .Should()
            .Equal(hasFilterState
                ? new[]
                {
                    "Summary", "—", "SortAscending", "SortDescending", "MoreSortOptions", "—",
                    "SelectItems", "LabelFilter", "ValueFilter", "ClearFilter", "—", "ValueFieldSettings"
                }
                : new[]
                {
                    "SortAscending", "SortDescending", "MoreSortOptions", "—", "SelectItems",
                    "LabelFilter", "ValueFilter", "ClearFilter", "—", "ValueFieldSettings"
                });

        var byAction = commands.Where(command => !command.IsSeparator)
            .ToDictionary(command => command.Action);
        byAction[PivotChartFieldContextMenuAction.SortAscending].IsEnabled.Should().BeTrue();
        byAction[PivotChartFieldContextMenuAction.SortDescending].IsEnabled.Should().BeTrue();
        byAction[PivotChartFieldContextMenuAction.MoreSortOptions].IsEnabled.Should().Be(hasFilterState);
        byAction[PivotChartFieldContextMenuAction.SelectItems].IsEnabled.Should().Be(hasFilterState);
        byAction[PivotChartFieldContextMenuAction.LabelFilter].IsEnabled.Should().Be(hasFilterState);
        byAction[PivotChartFieldContextMenuAction.ValueFilter].IsEnabled.Should().Be(canValueFilter);
        byAction[PivotChartFieldContextMenuAction.ClearFilter].IsEnabled.Should().Be(hasAnyFilter);
        byAction[PivotChartFieldContextMenuAction.ValueFieldSettings].IsEnabled.Should().Be(canValueFieldSettings);

        if (hasFilterState)
        {
            byAction[PivotChartFieldContextMenuAction.Summary].Header.Should().Be(summary);
            byAction[PivotChartFieldContextMenuAction.Summary].IsEnabled.Should().BeFalse();
        }

        byAction[PivotChartFieldContextMenuAction.SelectItems].Header.Should().Be(selectItemsHeader);
        byAction[PivotChartFieldContextMenuAction.LabelFilter].Header.Should().Be(labelFilterHeader);
        byAction[PivotChartFieldContextMenuAction.ValueFilter].Header.Should().Be(valueFilterHeader);
        byAction[PivotChartFieldContextMenuAction.ClearFilter].Header.Should().Be(clearFilterHeader);
        byAction[PivotChartFieldContextMenuAction.ClearFilter].ToolTip.Should().Be(hasAnyFilter
            ? null
            : PivotChartFieldContextMenuPlanner.ClearFilterDisabledToolTip);
        byAction[PivotChartFieldContextMenuAction.ValueFieldSettings].ToolTip.Should().Be(canValueFieldSettings
            ? PivotChartFieldContextMenuPlanner.ValueFieldSettingsEnabledToolTip
            : PivotChartFieldContextMenuPlanner.ValueFieldSettingsDisabledToolTip);
    }
}
