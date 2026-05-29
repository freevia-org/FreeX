using FreeX.Core.Model;

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
        workbook.CustomViews
            .Select(view => new CustomViewViewModel(
                view.Name,
                view.Sheets.Count,
                GetIncludedIndicator(view.IncludePrintSettings),
                GetIncludedIndicator(view.IncludeHiddenRowsColumnsAndFilterSettings)))
            .ToArray();

    public static string CreateDefaultViewName(int customViewCount) => $"Custom View {customViewCount + 1}";

    private static string GetIncludedIndicator(bool isIncluded) => isIncluded ? "Included" : "Not included";
}
