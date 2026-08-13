using FreeX.App.Presentation.DefinedNames;

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
    /// Compatibility filter for callers that still use the older option enum. Scope and error decisions
    /// delegate to the canonical portable row projector.
    /// </summary>
    public static IReadOnlyList<DefinedNameRow> FilterItems(
        IEnumerable<DefinedNameRow> items,
        NamedRangeFilterOption filter) =>
        DefinedNameListProjector.Filter(
            items,
            filter switch
            {
                NamedRangeFilterOption.Workbook => DefinedNameFilter.Workbook,
                NamedRangeFilterOption.Worksheet => DefinedNameFilter.Worksheet,
                NamedRangeFilterOption.Errors => DefinedNameFilter.Errors,
                NamedRangeFilterOption.NoErrors => DefinedNameFilter.NoErrors,
                _ => DefinedNameFilter.All
            });
}
