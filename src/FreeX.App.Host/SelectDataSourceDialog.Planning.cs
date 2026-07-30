using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record SelectDataSourceDialogResult(
    string SourceRangeText,
    bool FirstColumnIsCategories,
    bool SwitchRowColumn = false,
    IReadOnlyList<int>? PendingSeriesRemovals = null,
    ChartBlankDisplayMode BlankDisplayMode = ChartBlankDisplayMode.Gap,
    bool ShowDataInHiddenRowsAndColumns = false);

public sealed record SelectDataSourceRangeSelectionRequest(string CurrentText, bool CollapseDialog = true);

public sealed record SelectDataSourceSeriesPreview(string Name, string ValuesRangeText);

public sealed record SelectDataSourceCategoryPreview(string Label);

public sealed record SelectDataSourcePreview(
    IReadOnlyList<SelectDataSourceSeriesPreview> Series,
    IReadOnlyList<SelectDataSourceCategoryPreview> Categories,
    string CategoryRangeText);

public sealed partial class SelectDataSourceDialog
{
    public static SelectDataSourceDialogResult CreateResult(
        string sourceRangeText,
        bool firstColumnIsCategories,
        bool switchRowColumn = false)
    {
        var result = SelectDataSourcePlanner.CreateResult(sourceRangeText, firstColumnIsCategories, switchRowColumn);
        return new SelectDataSourceDialogResult(
            result.SourceRangeText,
            result.FirstColumnIsCategories,
            result.SwitchRowColumn);
    }

    public static SelectDataSourcePreview InferPreviewEntries(
        string sourceRangeText,
        bool firstColumnIsCategories,
        bool switchRowColumn = false)
    {
        var preview = SelectDataSourcePlanner.InferPreviewEntries(
            sourceRangeText,
            firstColumnIsCategories,
            index => UiText.Format("SelectDataSource_SeriesNameFormat", index),
            index => UiText.Format("SelectDataSource_CategoryNameFormat", index),
            UiText.Get("SelectDataSource_CategoryLabelsFallback"),
            switchRowColumn);

        return new SelectDataSourcePreview(
            preview.Series
                .Select(series => new SelectDataSourceSeriesPreview(series.Name, series.ValuesRangeText))
                .ToList(),
            preview.Categories
                .Select(category => new SelectDataSourceCategoryPreview(category.Label))
                .ToList(),
            preview.CategoryRangeText);
    }

    public static SelectDataSourceRangeSelectionRequest CreateRangeSelectionRequest(string currentText)
    {
        var request = SelectDataSourcePlanner.CreateRangeSelectionRequest(currentText);
        return new SelectDataSourceRangeSelectionRequest(request.CurrentText, request.CollapseDialog);
    }
}
