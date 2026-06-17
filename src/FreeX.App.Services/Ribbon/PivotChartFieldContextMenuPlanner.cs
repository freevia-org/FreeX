namespace FreeX.App.Services.Ribbon;

/// <summary>
/// Platform-neutral planner that describes the PivotChart field-button (and pivot header-dropdown) context
/// menu as a declarative command tree, mirroring <see cref="StatusBarCustomizeContextMenuPlanner"/> and
/// <see cref="QuickAccessToolbarContextMenuPlanner"/>. Unlike the static field-area menu modelled by
/// <see cref="PivotFieldContextMenuPlanner"/>, this menu is built dynamically from the clicked field's live
/// filter/sort state: it shows an optional disabled summary banner, dynamic sort/filter headers, and
/// per-item enablement. All of that state is threaded in explicitly via <see cref="PivotChartFieldContextMenuState"/>
/// so the planner stays platform-neutral; the host (WPF today) renders the literal <see cref="PivotChartFieldContextMenuCommand.Header"/>,
/// applies the carried tooltip, dispatches the <see cref="PivotChartFieldContextMenuAction"/> to the existing
/// pivot-field Click handlers, and assigns keytips at render time exactly as the hand-authored menu did.
/// </summary>
public static class PivotChartFieldContextMenuPlanner
{
    public const string SummaryToolTip = "Current filter state for this PivotTable field.";
    public const string MoreSortOptionsToolTip = "Open PivotTable sort options for this field.";
    public const string ValueFieldSettingsEnabledToolTip = "Open settings for the relevant PivotTable value field.";
    public const string ValueFieldSettingsDisabledToolTip =
        "Select a value field, the PivotChart Values button, or a PivotTable with one value field.";
    public const string ClearFilterDisabledToolTip = "No item, label, or value filters are active for this field.";

    public const string SortAscendingHeader = "Sort A to Z";
    public const string SortDescendingHeader = "Sort Z to A";
    public const string MoreSortOptionsHeader = "More Sort Options...";
    public const string ValueFieldSettingsHeader = "Value Field Settings...";

    /// <summary>
    /// Builds the PivotChart field-button context menu for the supplied live field <paramref name="state"/>.
    /// When the field resolves to a filterable PivotTable source field (<see cref="PivotChartFieldContextMenuState.HasFilterState"/>),
    /// the menu leads with a disabled summary banner + separator; otherwise those are omitted and the sort/filter
    /// items render disabled. Item order, headers, enablement, and tooltips reproduce the previous hand-authored
    /// <c>ContextMenu</c> exactly.
    /// </summary>
    public static IReadOnlyList<PivotChartFieldContextMenuCommand> BuildCommands(PivotChartFieldContextMenuState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var commands = new List<PivotChartFieldContextMenuCommand>();

        if (state.HasFilterState)
        {
            commands.Add(new PivotChartFieldContextMenuCommand(
                PivotChartFieldContextMenuAction.Summary,
                Header: state.OverallSummary,
                IsEnabled: false,
                ToolTip: SummaryToolTip));
            commands.Add(PivotChartFieldContextMenuCommand.Separator);
        }

        commands.Add(new PivotChartFieldContextMenuCommand(
            PivotChartFieldContextMenuAction.SortAscending,
            Header: SortAscendingHeader));
        commands.Add(new PivotChartFieldContextMenuCommand(
            PivotChartFieldContextMenuAction.SortDescending,
            Header: SortDescendingHeader));
        commands.Add(new PivotChartFieldContextMenuCommand(
            PivotChartFieldContextMenuAction.MoreSortOptions,
            Header: MoreSortOptionsHeader,
            IsEnabled: state.HasFilterState,
            ToolTip: MoreSortOptionsToolTip));

        commands.Add(PivotChartFieldContextMenuCommand.Separator);

        commands.Add(new PivotChartFieldContextMenuCommand(
            PivotChartFieldContextMenuAction.SelectItems,
            Header: state.SelectItemsHeader,
            IsEnabled: state.HasFilterState));
        commands.Add(new PivotChartFieldContextMenuCommand(
            PivotChartFieldContextMenuAction.LabelFilter,
            Header: state.LabelFilterHeader,
            IsEnabled: state.HasFilterState));
        commands.Add(new PivotChartFieldContextMenuCommand(
            PivotChartFieldContextMenuAction.ValueFilter,
            Header: state.ValueFilterHeader,
            IsEnabled: state.CanValueFilter));
        commands.Add(new PivotChartFieldContextMenuCommand(
            PivotChartFieldContextMenuAction.ClearFilter,
            Header: state.ClearFilterHeader,
            IsEnabled: state.HasAnyFilter,
            ToolTip: state.HasAnyFilter ? null : ClearFilterDisabledToolTip));

        commands.Add(PivotChartFieldContextMenuCommand.Separator);

        commands.Add(new PivotChartFieldContextMenuCommand(
            PivotChartFieldContextMenuAction.ValueFieldSettings,
            Header: ValueFieldSettingsHeader,
            IsEnabled: state.CanValueFieldSettings,
            ToolTip: state.CanValueFieldSettings ? ValueFieldSettingsEnabledToolTip : ValueFieldSettingsDisabledToolTip));

        return Array.AsReadOnly(commands.ToArray());
    }
}

/// <summary>
/// Live state for the PivotChart field-button context menu. <see cref="HasFilterState"/> is true when the
/// clicked field resolves to a filterable PivotTable source field; the header strings are the already-formatted
/// dynamic labels (computed by the host from the field's filter summary), and the can-* flags carry the
/// per-item enablement the hand-authored menu derived inline.
/// </summary>
public sealed record PivotChartFieldContextMenuState(
    bool HasFilterState,
    string OverallSummary,
    string SelectItemsHeader,
    string LabelFilterHeader,
    string ValueFilterHeader,
    string ClearFilterHeader,
    bool CanValueFilter,
    bool HasAnyFilter,
    bool CanValueFieldSettings);

public sealed record PivotChartFieldContextMenuCommand(
    PivotChartFieldContextMenuAction Action,
    bool IsSeparator = false,
    string? Header = null,
    bool IsEnabled = true,
    string? ToolTip = null)
{
    public static PivotChartFieldContextMenuCommand Separator { get; } =
        new(PivotChartFieldContextMenuAction.None, IsSeparator: true, IsEnabled: false);

    /// <summary>Literal, already-localized/formatted header text (these menu labels were never resource keys).</summary>
    public string Header { get; init; } = Header ?? "";
}

public enum PivotChartFieldContextMenuAction
{
    None,
    Summary,
    SortAscending,
    SortDescending,
    MoreSortOptions,
    SelectItems,
    LabelFilter,
    ValueFilter,
    ClearFilter,
    ValueFieldSettings
}
