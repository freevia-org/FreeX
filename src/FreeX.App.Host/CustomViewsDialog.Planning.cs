using FreeX.Core.Model;
using FreeX.App.Presentation.CustomViews;

namespace FreeX.App.Host;

internal sealed class CustomViewViewModel(string name, int sheetCount, string printSettingsIndicator, string filterSettingsIndicator)
{
    public string Name { get; } = name;
    public int SheetCount { get; } = sheetCount;
    public string PrintSettingsIndicator { get; } = printSettingsIndicator;
    public string FilterSettingsIndicator { get; } = filterSettingsIndicator;
}

internal static class CustomViewsDialogPlanner
{
    public static IReadOnlyList<CustomViewViewModel> BuildItems(Workbook workbook) =>
        CustomViewsPlanner.BuildDialogRows(
                workbook,
                UiText.Get("CustomViews_Included"),
                UiText.Get("CustomViews_NotIncluded"))
            .Select(row => new CustomViewViewModel(
                row.Name,
                row.SheetCount,
                row.PrintSettingsIndicator,
                row.FilterSettingsIndicator))
            .ToArray();

    public static string CreateDefaultViewName(int customViewCount) =>
        CustomViewsPlanner.SuggestDefaultName(
            customViewCount,
            UiText.Get("CustomViews_DefaultName"));
}
