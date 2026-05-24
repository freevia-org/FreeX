namespace Freexcel.App.Host;

public enum NamedRangeFilterOption
{
    All,
    Workbook,
    Worksheet,
    Errors,
    NoErrors
}

public static class NamedRangeDialogPlanner
{
    public static IReadOnlyList<NamedRangeViewModel> FilterItems(
        IEnumerable<NamedRangeViewModel> items,
        NamedRangeFilterOption filter) =>
        filter switch
        {
            NamedRangeFilterOption.Workbook => items
                .Where(item => string.Equals(item.Scope, "Workbook", StringComparison.OrdinalIgnoreCase))
                .ToList(),
            NamedRangeFilterOption.Worksheet => items
                .Where(item => !string.Equals(item.Scope, "Workbook", StringComparison.OrdinalIgnoreCase))
                .ToList(),
            NamedRangeFilterOption.Errors => items
                .Where(HasFormulaError)
                .ToList(),
            NamedRangeFilterOption.NoErrors => items
                .Where(item => !HasFormulaError(item))
                .ToList(),
            _ => items.ToList()
        };

    private static bool HasFormulaError(NamedRangeViewModel item) =>
        ContainsFormulaError(item.Value) || ContainsFormulaError(item.RefersTo);

    private static bool ContainsFormulaError(string text) =>
        text.Contains("#REF!", StringComparison.OrdinalIgnoreCase)
        || text.Contains("#NAME?", StringComparison.OrdinalIgnoreCase)
        || text.Contains("#VALUE!", StringComparison.OrdinalIgnoreCase)
        || text.Contains("#DIV/0!", StringComparison.OrdinalIgnoreCase)
        || text.Contains("#N/A", StringComparison.OrdinalIgnoreCase)
        || text.Contains("#NUM!", StringComparison.OrdinalIgnoreCase)
        || text.Contains("#NULL!", StringComparison.OrdinalIgnoreCase);
}

/// <summary>View model for a row in the named ranges list.</summary>
public sealed class NamedRangeViewModel(string name, string value, string refersTo, string scope, string comment)
{
    public string Name { get; } = name;
    public string Value { get; } = value;
    public string RefersTo { get; } = refersTo;
    public string Scope { get; } = scope;
    public string Comment { get; } = comment;

    public string Address => RefersTo;
}
