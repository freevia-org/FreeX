namespace FreeX.App.Presentation.PivotUI;

/// <summary>
/// Which layout area a pivot header dropdown belongs to. Mirrors the axis classification used by the
/// header-target planning logic in the desktop hosts.
/// </summary>
public enum PivotHeaderArea
{
    Row,
    Column,
    Page,
    Value
}

/// <summary>
/// The kind of action a header-dropdown menu item performs. Renderers map each kind to a host menu entry
/// and dispatch the corresponding pivot operation when chosen.
/// </summary>
public enum PivotHeaderMenuAction
{
    /// <summary>Sentinel for a non-actionable separator row; carries no behavior.</summary>
    Separator,
    SortAscending,
    SortDescending,
    MoreSortOptions,
    ClearSort,
    LabelFilter,
    ValueFilter,
    ClearFilter,
    MoveToRows,
    MoveToColumns,
    MoveToFilters,
    MoveToValues,
    MoveUp,
    MoveDown,
    FieldSettings,
    ValueFieldSettings,
    RemoveField
}

/// <summary>
/// A single header-dropdown menu entry as a portable descriptor. <see cref="IsSeparator"/> entries carry no
/// action and group the items visually; <see cref="IsEnabled"/> is false for actions that are not currently
/// applicable (e.g. clearing a filter when none is set). <see cref="IsChecked"/> marks the currently-applied
/// sort direction.
/// </summary>
public sealed record PivotHeaderMenuItemModel(
    PivotHeaderMenuAction Action,
    string Label,
    bool IsEnabled = true,
    bool IsChecked = false,
    bool IsSeparator = false)
{
    public static PivotHeaderMenuItemModel Separator { get; } =
        new(PivotHeaderMenuAction.Separator, string.Empty, IsSeparator: true);
}

/// <summary>
/// Identifies a header cell that should carry a dropdown: which pivot, which source field, which layout
/// area, and whether the field currently has an active sort/filter/selection (so the renderer can badge it).
/// Ported from the header-target planning logic in the desktop hosts; the cell coordinate the renderer
/// attaches the dropdown to stays a host concern.
/// </summary>
public sealed record PivotHeaderDropdownTargetModel(
    string PivotTableName,
    string FieldCaption,
    int SourceFieldIndex,
    PivotHeaderArea Area,
    bool IsActive,
    int? DataFieldIndex = null);

/// <summary>The full menu for one header dropdown: the target it belongs to and its ordered items.</summary>
public sealed record PivotHeaderDropdownMenuModel(
    PivotHeaderDropdownTargetModel Target,
    IReadOnlyList<PivotHeaderMenuItemModel> Items);
