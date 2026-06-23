namespace FreeX.App.Services;

public enum RecommendedPivotTablesDialogResult
{
    None,
    BlankPivotTable
}

public static class RecommendedPivotTablesDialogPlanner
{
    public const string TitleKey = "MainWindow_Header_RecommendedPivotTables";
    public const string NoRecommendationsHeadingKey = "RecommendedPivotTables_NoRecommendationsHeading";
    public const string NoRecommendationsBodyKey = "RecommendedPivotTables_NoRecommendationsBody";
    public const string BlankPivotTableKey = "RecommendedPivotTables_BlankPivotTable";
    public const string BlankPivotTableAutomationNameKey = "RecommendedPivotTables_BlankPivotTableAutomationName";
    public const string BlankPivotTableAutomationHelpTextKey = "RecommendedPivotTables_BlankPivotTableAutomationHelpText";
    public const string DialogAutomationId = "RecommendedPivotTablesDialog";
    public const string BlankPivotTableAutomationId = "RecommendedPivotTablesBlankPivotTableButton";
    public const double Width = 560;
    public const double MinHeight = 340;
    public const double BlankPivotTableButtonWidth = 132;
    public const double CancelButtonWidth = 80;
}
