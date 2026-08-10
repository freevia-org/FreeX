using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class SortDialog
{
    public static IReadOnlyList<SortKey> BuildSortKeys(IEnumerable<SortDialogLevel> levels) =>
        SortDialogPlanner.BuildSortKeys(levels, PlannerText);

    public static SortDialogCommandPlan CreateCommandPlan(
        IEnumerable<SortDialogLevel> levels,
        SortDialogOptions options,
        bool hasHeaders) =>
        SortDialogPlanner.CreateCommandPlan(levels, options, hasHeaders, PlannerText);

    public static IReadOnlyList<SortDirectionChoice> BuildOrderChoices(string? sortOn) =>
        SortDialogPlanner.BuildOrderChoices(sortOn, PlannerText);

    public static IReadOnlyList<SortDialogLevel> AddLevel(
        IEnumerable<SortDialogLevel> levels,
        uint columnOffset = 0,
        bool ascending = true) =>
        SortDialogPlanner.AddLevel(levels, columnOffset, ascending, PlannerText);

    public static IReadOnlyList<SortDialogLevel> RemoveLevel(IEnumerable<SortDialogLevel> levels, int index) =>
        SortDialogPlanner.RemoveLevel(levels, index, PlannerText);

    public static IReadOnlyList<SortDialogLevel> CopyLevel(IEnumerable<SortDialogLevel> levels, int index) =>
        SortDialogPlanner.CopyLevel(levels, index, PlannerText);

    public static IReadOnlyList<SortDialogLevel> MoveLevel(IEnumerable<SortDialogLevel> levels, int index, int direction) =>
        SortDialogPlanner.MoveLevel(levels, index, direction, PlannerText);

    public static IReadOnlyList<SortDialogLevel> UpdateLevel(
        IEnumerable<SortDialogLevel> levels,
        int index,
        uint columnOffset,
        bool ascending) =>
        SortDialogPlanner.UpdateLevel(levels, index, columnOffset, ascending, PlannerText);

    public static IReadOnlyList<SortColumnChoice> BuildColumnChoices(GridRange range) =>
        SortDialogPlanner.BuildColumnChoices(range, PlannerText);

    public static IReadOnlyList<SortColumnChoice> BuildColumnChoices(Sheet? sheet, GridRange range, bool hasHeaders) =>
        SortDialogPlanner.BuildColumnChoices(sheet, range, hasHeaders, PlannerText);

    public static IReadOnlyList<SortColumnChoice> BuildRowChoices(GridRange range) =>
        SortDialogPlanner.BuildRowChoices(range, PlannerText);

    public static IReadOnlyList<SortColorChoice> BuildColorChoices(Workbook workbook, Sheet? sheet, GridRange range) =>
        SortDialogPlanner.BuildColorChoices(workbook, sheet, range);

    public static IReadOnlyList<SortColorChoice> BuildColorChoices(
        Workbook workbook,
        Sheet? sheet,
        GridRange range,
        SortOn sortOn) =>
        SortDialogPlanner.BuildColorChoices(workbook, sheet, range, sortOn);

    public static GridRange ExcludeHeaderRow(GridRange range, bool hasHeaders) =>
        SortDialogPlanner.ExcludeHeaderRow(range, hasHeaders);

    private static IReadOnlyList<SortDialogLevel> NormalizeLevels(IEnumerable<SortDialogLevel>? levels) =>
        SortDialogPlanner.NormalizeLevels(levels, PlannerText);

    private static IReadOnlyList<SortColumnChoice> NormalizeColumnChoices(IEnumerable<SortColumnChoice>? choices) =>
        SortDialogPlanner.NormalizeColumnChoices(choices, PlannerText);

    private static IReadOnlyList<SortColorChoice> NormalizeColorChoices(IEnumerable<SortColorChoice>? choices) =>
        SortDialogPlanner.NormalizeColorChoices(choices);
}
