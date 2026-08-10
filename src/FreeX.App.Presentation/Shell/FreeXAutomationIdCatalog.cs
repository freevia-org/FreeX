namespace FreeX.App.Presentation.Shell;

/// <summary>Stable cross-renderer automation identifiers owned by the FreeX application frame.</summary>
public static class FreeXAutomationIdCatalog
{
    public const string ActivateSheetList = "ActivateSheetList";
    public const string ActivateSheetOkButton = "ActivateSheetOkButton";
    public const string ActivateSheetCancelButton = "ActivateSheetCancelButton";

    public const string QuickAccessToolbarImportExportButton = "QuickAccessToolbarImportExportButton";
    public const string QuickAccessToolbarImportCustomizationMenuItem = "QuickAccessToolbarImportCustomizationMenuItem";
    public const string QuickAccessToolbarExportCustomizationMenuItem = "QuickAccessToolbarExportCustomizationMenuItem";

    public const string MergeCellsContentWarningDialog = "MergeCellsContentWarningDialog";
    public const string MergeCellsKeepFirstButton = "MergeCellsKeepFirstButton";
    public const string MergeCellsConcatenateButton = "MergeCellsConcatenateButton";
    public const string MergeCellsCancelButton = "MergeCellsCancelButton";

    public const string WorkbookStatisticsSummary = "WorkbookStatisticsSummary";
    public const string WorkbookStatisticsCopyButton = "WorkbookStatisticsCopyButton";

    public static class SelectionPane
    {
        public const string Dialog = "SelectionPaneDialog";
        public const string ObjectList = "SelectionPaneObjectList";
        public const string SearchBox = "SelectionPaneSearchBox";
        public const string FilterBox = "SelectionPaneFilterBox";
        public const string RenameBox = "SelectionPaneRenameBox";
        public const string RenameButton = "SelectionPaneRenameButton";
        public const string ToggleVisibilityButton = "SelectionPaneToggleVisibilityButton";
        public const string BringForwardButton = "SelectionPaneBringForwardButton";
        public const string SendBackwardButton = "SelectionPaneSendBackwardButton";
        public const string ShowAllButton = "SelectionPaneShowAllButton";
        public const string HideAllButton = "SelectionPaneHideAllButton";
        public const string DeleteButton = "SelectionPaneDeleteButton";
        public const string OkButton = "SelectionPaneOkButton";
        public const string CancelButton = "SelectionPaneCancelButton";

        public static string WpfItem(string kind, Guid id) => $"SelectionPaneItem{kind}{id:N}";
        public static string WpfVisibility(string itemAutomationId) => itemAutomationId + "VisibilityBox";
        public static string WpfName(string itemAutomationId) => itemAutomationId + "NameBox";
        public static string AvaloniaVisibility(Guid id) => "SelectionPaneVisibility_" + id.ToString("N");
        public static string AvaloniaName(Guid id) => "SelectionPaneName_" + id.ToString("N");
    }
}
