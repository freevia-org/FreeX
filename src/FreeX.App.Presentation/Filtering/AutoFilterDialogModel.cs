using FreeX.Core.Model;

namespace FreeX.App.Presentation.Filtering;

public enum AutoFilterSortDirection
{
    None,
    Ascending,
    Descending
}

public enum AutoFilterDialogAction
{
    Apply,
    ClearFilter
}

public sealed record AutoFilterDialogItem(string DisplayText, string Value, bool IsSelected)
{
    public bool IsSelected { get; set; } = IsSelected;
}

public sealed record AutoFilterDialogResult(
    AutoFilterSortDirection SortDirection,
    IReadOnlyList<string> SelectedValues,
    string SearchText,
    string CriteriaText,
    AutoFilterColorFilter? ColorFilter = null,
    AutoFilterDialogAction Action = AutoFilterDialogAction.Apply,
    // R76-render-autofilter-dropdown-4-2: distinct from ColorFilter (which FILTERS the column to
    // that color) -- this SORTS the column, moving rows matching this color to the top, mirroring
    // Excel's "Sort by Color" swatch picker sitting alongside Filter by Color.
    AutoFilterColorFilter? SortByColorFilter = null);

public sealed record AutoFilterColorFilter(AutoFilterColorFilterKind Kind, CellColor? Color);

public sealed record AutoFilterCriteriaOption(string Label, string CriteriaPrefix, bool RequiresValue = true);
