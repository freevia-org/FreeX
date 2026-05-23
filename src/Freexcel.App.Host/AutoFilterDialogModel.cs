using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum AutoFilterSortDirection
{
    None,
    Ascending,
    Descending
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
    AutoFilterColorFilter? ColorFilter = null);

public sealed record AutoFilterColorFilter(AutoFilterColorFilterKind Kind, CellColor? Color);

public sealed record AutoFilterCriteriaOption(string Label, string CriteriaPrefix, bool RequiresValue = true);
