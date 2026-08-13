using FreeX.App.Services;

namespace FreeX.App.Host;

public sealed partial class SortDialog
{
    internal static SortDialogPlannerText PlannerText => new(
        UiText.Get("Sort_SortOnCellValues"),
        UiText.Get("Sort_SortOnCellColor"),
        UiText.Get("Common_FontColor"),
        UiText.Get("Sort_OrderAToZ"),
        UiText.Get("Sort_OrderZToA"),
        UiText.Get("Sort_OrderOnTop"),
        UiText.Get("Sort_OrderOnBottom"),
        UiText.Get("Sort_ColumnLabel"),
        UiText.Get("Sort_RowLabel"),
        UiText.Get("Sort_SortOnCellIcon"));

    private static IReadOnlyList<SortOnChoice> SortOnChoices =>
        [
            new(PlannerText.SortOnCellValues),
            new(PlannerText.SortOnCellColor),
            new(PlannerText.SortOnFontColor),
            new(PlannerText.SortOnCellIcon)
        ];
}
