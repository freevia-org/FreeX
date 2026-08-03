namespace FreeX.App.Services;

public static class AddWatchDialogPlanner
{
    public const string TitleKey = "AddWatch_Title";
    public const string SelectedRangeLabelKey = "AddWatch_SelectedRangeLabel";
    public const string SelectedRangeAutomationNameKey = "AddWatch_SelectedRangeAutomationName";
    public const string SelectedRangeHelpTextKey = "AddWatch_SelectedRangeHelpText";
    public const string BodyTextKey = "AddWatch_BodyText";
    public const string AddButtonKey = "AddWatch_AddButton";
    public const string CancelButtonKey = "Common_Cancel";
    public const string AddAutomationNameKey = "AddWatch_AddAutomationName";
    public const string AddHelpTextKey = "AddWatch_AddHelpText";
    public const string CancelAutomationNameKey = "AddWatch_CancelAutomationName";
    public const string CancelHelpTextKey = "AddWatch_CancelHelpText";

    public const string DialogAutomationId = "AddWatchDialog";
    public const string SelectedRangeAutomationId = "AddWatchSelectedRangeBox";
    public const string AddButtonAutomationId = "AddWatchAddButton";
    public const string CancelButtonAutomationId = "AddWatchCancelButton";

    public const double Width = 360;
    public const double Height = 170;
    public const double ButtonWidth = 76;
    public const double RootMargin = 12;
    public const double RangeBottomMargin = 8;
    public const double ActionRowTopMargin = 12;

    // Avalonia's compact chrome needs an explicit equivalent of the WPF shared minimum.
    public const double ButtonMinWidth = 84;

    // Avalonia-only compensation: Window.Width includes the WPF non-client frame, so
    // preserve the equivalent right-side client inset in the 360x170 capture surface.
    public const double AvaloniaWpfClientRightInset = 16;
    public const double AvaloniaRangeBottomMargin = 11;
    public const double AvaloniaActionRowTopMargin = 14;

    // Both shells must render the same deterministic capture fixture. Production callers
    // continue to pass their live formatted selection text.
    public const string ParitySelectedRangeText = "Sheet1!$B$2";
}
