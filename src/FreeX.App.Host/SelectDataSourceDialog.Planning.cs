using FreeX.App.Presentation.Charts.Editing;
namespace FreeX.App.Host;

public sealed partial class SelectDataSourceDialog
{
    public static SelectDataSourcePreview InferPreviewEntries(
        string sourceRangeText,
        bool firstColumnIsCategories,
        bool switchRowColumn = false)
    {
        return SelectDataSourcePlanner.InferPreviewEntries(
            sourceRangeText,
            firstColumnIsCategories,
            index => UiText.Format("SelectDataSource_SeriesNameFormat", index),
            index => UiText.Format("SelectDataSource_CategoryNameFormat", index),
            UiText.Get("SelectDataSource_CategoryLabelsFallback"),
            switchRowColumn);
    }
}
