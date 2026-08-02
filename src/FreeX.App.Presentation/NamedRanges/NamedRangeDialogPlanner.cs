using FreeX.Core.Model;

namespace FreeX.App.Presentation.NamedRanges;

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
    /// <summary>
    /// R114-app-name-manager-workbook-sentinel-3-2: Workbook/Worksheet filtering must key off the
    /// item's actual <see cref="NamedRangeViewModel.ScopeSheetId"/> identity (null = workbook-global),
    /// not the display-label string. A worksheet can legally be named exactly "Workbook" (nothing in
    /// <see cref="FreeX.Core.Model.Workbook.ValidateSheetNameStructure"/> reserves that text), so a
    /// name actually scoped to such a sheet would otherwise carry the display label "Workbook" too and
    /// get misclassified as workbook-scoped by a string comparison.
    /// </summary>
    public static IReadOnlyList<NamedRangeViewModel> FilterItems(
        IEnumerable<NamedRangeViewModel> items,
        NamedRangeFilterOption filter) =>
        filter switch
        {
            NamedRangeFilterOption.Workbook => items
                .Where(item => item.ScopeSheetId is null)
                .ToList(),
            NamedRangeFilterOption.Worksheet => items
                .Where(item => item.ScopeSheetId is not null)
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
/// <param name="scopeSheetId">
///   The row's actual scope identity: null for a workbook-global name, or the owning sheet's
///   <see cref="SheetId"/> for a sheet-scoped name (Excel "localSheetId") -- tracked separately
///   from <paramref name="scope"/> (the display label) because a sheet can legally be named
///   exactly "Workbook", which would otherwise make the display label alone ambiguous with the
///   workbook-global scope sentinel.
/// </param>
public sealed class NamedRangeViewModel(
    string name,
    string value,
    string refersTo,
    string scope,
    string comment,
    SheetId? scopeSheetId = null)
{
    public string Name { get; } = name;
    public string Value { get; } = value;
    public string RefersTo { get; } = refersTo;
    public string Scope { get; } = scope;
    public string Comment { get; } = comment;
    public SheetId? ScopeSheetId { get; } = scopeSheetId;

    public string Address => RefersTo;
}
