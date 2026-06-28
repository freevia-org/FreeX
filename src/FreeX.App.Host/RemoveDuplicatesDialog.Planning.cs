using FreeX.Core.Model;
using ServicesRemoveDuplicateColumnChoice = FreeX.App.Services.RemoveDuplicateColumnChoice;
using ServicesRemoveDuplicatesPlanner = FreeX.App.Services.RemoveDuplicatesPlanner;
using ServicesRemoveDuplicatesPlannerText = FreeX.App.Services.RemoveDuplicatesPlannerText;

namespace FreeX.App.Host;

public sealed partial class RemoveDuplicatesDialog
{
    public static IReadOnlyList<RemoveDuplicateColumnChoice> SelectAll(int columnCount) =>
        ToHostChoices(ServicesRemoveDuplicatesPlanner.BuildColumnChoices(columnCount, isSelected: true, PlannerText));

    public static IReadOnlyList<RemoveDuplicateColumnChoice> SelectAll(IEnumerable<RemoveDuplicateColumnChoice> columns) =>
        ToHostChoices(ServicesRemoveDuplicatesPlanner.SelectAll(ToServiceChoices(columns)));

    public static IReadOnlyList<RemoveDuplicateColumnChoice> ClearAll(IEnumerable<RemoveDuplicateColumnChoice> columns) =>
        ToHostChoices(ServicesRemoveDuplicatesPlanner.ClearAll(ToServiceChoices(columns)));

    public static RemoveDuplicatesDialogResult CreateResult(IEnumerable<RemoveDuplicateColumnChoice> columns)
    {
        var offsets = ServicesRemoveDuplicatesPlanner.GetSelectedColumnOffsets(ToServiceChoices(columns));
        return new RemoveDuplicatesDialogResult(offsets);
    }

    public static GridRange ExcludeHeaderRow(GridRange range, bool hasHeaders) =>
        ServicesRemoveDuplicatesPlanner.ExcludeHeaderRow(range, hasHeaders);

    private static IReadOnlyList<RemoveDuplicateColumnChoice> BuildColumnChoices(int columnCount, bool isSelected) =>
        ToHostChoices(ServicesRemoveDuplicatesPlanner.BuildColumnChoices(columnCount, isSelected, PlannerText));

    public static IReadOnlyList<RemoveDuplicateColumnChoice> BuildColumnChoices(GridRange range) =>
        ToHostChoices(ServicesRemoveDuplicatesPlanner.BuildColumnChoices(range, PlannerText));

    public static IReadOnlyList<RemoveDuplicateColumnChoice> BuildColumnChoices(Sheet sheet, GridRange range) =>
        BuildColumnChoices(sheet, range, hasHeaders: true);

    public static IReadOnlyList<RemoveDuplicateColumnChoice> BuildColumnChoices(Sheet sheet, GridRange range, bool hasHeaders) =>
        ToHostChoices(ServicesRemoveDuplicatesPlanner.BuildColumnChoices(sheet, range, hasHeaders, PlannerText));

    public static bool GuessHasHeaders(Sheet sheet, GridRange range) =>
        ServicesRemoveDuplicatesPlanner.GuessHasHeaders(sheet, range);

    private static ServicesRemoveDuplicatesPlannerText PlannerText =>
        new(UiText.Get("RemoveDuplicates_ColumnLabel"));

    private static IReadOnlyList<ServicesRemoveDuplicateColumnChoice> ToServiceChoices(
        IEnumerable<RemoveDuplicateColumnChoice> columns) =>
        columns
            .Select(static column => new ServicesRemoveDuplicateColumnChoice(
                column.Offset,
                column.Header,
                column.IsSelected))
            .ToArray();

    private static IReadOnlyList<RemoveDuplicateColumnChoice> ToHostChoices(
        IEnumerable<ServicesRemoveDuplicateColumnChoice> columns) =>
        columns
            .Select(static column => new RemoveDuplicateColumnChoice(
                column.Offset,
                column.Label,
                column.IsSelected))
            .ToArray();
}
