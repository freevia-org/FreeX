using Free.Shared.Shell;

namespace FreeX.App.Presentation.Backstage;

public enum FreeXBackstageNavigationEntryKind
{
    Pane,
    Command,
    Divider
}

public enum FreeXBackstagePaneId
{
    Home,
    Info,
    Print
}

public enum FreeXBackstageCommandId
{
    New,
    Open,
    Share,
    Save,
    SaveAs,
    Export,
    Close,
    Account,
    Options
}

public sealed record FreeXBackstageNavigationEntry(
    FreeXBackstageNavigationEntryKind Kind,
    FreeXBackstagePaneId? Pane,
    FreeXBackstageCommandId? Command,
    string? LabelKey,
    BackstageIconKind? Icon,
    string? IconCommandName,
    bool DockBottom = false,
    string? KeyTip = null,
    string? AutomationId = null,
    string? AutomationNameKey = null,
    string? AutomationHelpTextKey = null,
    string? TooltipTitleKey = null,
    string? TooltipDescriptionKey = null);

/// <summary>
/// Platform-neutral catalog for the FreeX File/Backstage rail. Renderers attach callbacks and pane
/// factories; this planner owns ordering, command identity, icons, keytips, automation ids, and string keys.
/// </summary>
public static class FreeXBackstageNavigationPlanner
{
    public const string HomePaneAutomationId = "BackstageHomeButton";
    public const string InfoPaneAutomationId = "BackstageInfoButton";
    public const string PrintPaneAutomationId = "BackstagePrintButton";

    public static IReadOnlyList<FreeXBackstageNavigationEntry> Build() =>
    [
        Pane(
            FreeXBackstagePaneId.Home,
            "MainWindow_Text_Home",
            BackstageIconKind.Grid,
            "Home",
            "H",
            HomePaneAutomationId,
            "MainWindow_Text_Home",
            "MainWindow_TooltipTitle_Home",
            "MainWindow_TooltipTitle_Home"),

        Command(
            FreeXBackstageCommandId.New,
            "Common_New",
            BackstageIconKind.Insert,
            "New",
            "N",
            "BackstageNewButton",
            "Common_New",
            "MainWindow_TooltipDescription_CreateANewWorkbook",
            "MainWindow_TooltipTitle_New"),

        Command(
            FreeXBackstageCommandId.Open,
            "MainWindow_Text_Open",
            BackstageIconKind.GetData,
            "Open",
            "O",
            "BackstageOpenButton",
            "MainWindow_Text_Open",
            "MainWindow_TooltipDescription_OpenAnExistingWorkbook",
            "MainWindow_TooltipTitle_Open"),

        Command(
            FreeXBackstageCommandId.Share,
            "MainWindow_Text_Share",
            BackstageIconKind.Share,
            "Share",
            "R",
            "BackstageShareButton",
            "MainWindow_Text_Share",
            "MainWindow_TooltipDescription_SaveTheWorkbookIfNeededAndOpenWindowsShareForTheFile",
            "MainWindow_TooltipTitle_Share",
            "MainWindow_TooltipDescription_SaveTheWorkbookIfNeededAndOpenWindowsShareForTheFile"),

        Divider(),

        Pane(
            FreeXBackstagePaneId.Info,
            "MainWindow_Text_Info",
            BackstageIconKind.Info,
            "Info",
            "I",
            InfoPaneAutomationId,
            "MainWindow_Text_Info",
            "MainWindow_Text_ReviewLocalFileStatusAndUnsupportedWorkbookFeatureWarnings",
            "MainWindow_TooltipTitle_Info"),

        Command(
            FreeXBackstageCommandId.Save,
            "MainWindow_Text_Save",
            BackstageIconKind.Save,
            "Save",
            "S",
            "BackstageSaveButton",
            "MainWindow_AutomationName_Save",
            "MainWindow_TooltipDescription_SaveTheWorkbook",
            "MainWindow_TooltipTitle_Save"),

        Command(
            FreeXBackstageCommandId.SaveAs,
            "MainWindow_Text_SaveAs",
            BackstageIconKind.Save,
            "Save As",
            "A",
            "BackstageSaveAsButton",
            "MainWindow_TooltipTitle_SaveAs",
            "MainWindow_TooltipDescription_SaveTheWorkbookWithANewNameOrFormat",
            "MainWindow_TooltipTitle_SaveAs"),

        Pane(
            FreeXBackstagePaneId.Print,
            "MainWindow_Text_Print",
            BackstageIconKind.Print,
            "Print",
            "P",
            PrintPaneAutomationId,
            "MainWindow_AutomationName_Print",
            "MainWindow_AutomationHelpText_OpenPrintPreviewWithWorksheetSettingsAndNativePrintAccess",
            "MainWindow_TooltipTitle_Print",
            "MainWindow_TooltipDescription_OpenThePrintPreviewAndNativePrintDialogForTheRenderedWorksheet"),

        Command(
            FreeXBackstageCommandId.Export,
            "MainWindow_Text_Export",
            BackstageIconKind.Share,
            "Export",
            "E",
            "BackstageExportButton",
            "MainWindow_TooltipTitle_ExportPDFXPS",
            "MainWindow_TooltipDescription_SaveSheetsTheCurrentSelectionOrTheWorkbookAsAPDFFileOrAnXPSPackage",
            "MainWindow_TooltipTitle_ExportPDFXPS",
            "MainWindow_TooltipDescription_SaveSheetsTheCurrentSelectionOrTheWorkbookAsAPDFFileOrAnXPSPackage"),

        Command(
            FreeXBackstageCommandId.Close,
            "MainWindow_Text_Close",
            BackstageIconKind.WindowClose,
            "Close",
            "C",
            "BackstageCloseButton",
            "MainWindow_AutomationName_Close",
            "MainWindow_TooltipTitle_Close",
            "MainWindow_TooltipTitle_Close"),

        Divider(dockBottom: true),

        Command(
            FreeXBackstageCommandId.Account,
            "MainWindow_Text_Account",
            BackstageIconKind.Info,
            "Account",
            "D",
            "BackstageAccountButton",
            "MainWindow_AutomationName_Account",
            "MainWindow_AutomationHelpText_ShowLocalAccountInformationForFreeX",
            "MainWindow_TooltipTitle_LocalAccount",
            "MainWindow_TooltipDescription_MicrosoftAccountIntegrationIsNotImplementedFreeXUsesLocalFilesAndLocalOp_EC989658",
            dockBottom: true),

        Command(
            FreeXBackstageCommandId.Options,
            "MainWindow_Text_Options",
            BackstageIconKind.View,
            "Options",
            "T",
            "BackstageOptionsButton",
            "MainWindow_AutomationName_Options",
            "MainWindow_AutomationHelpText_OpenFreeXSettingsAndFormulaErrorCheckingOptions",
            "MainWindow_TooltipTitle_Options",
            dockBottom: true),
    ];

    private static FreeXBackstageNavigationEntry Pane(
        FreeXBackstagePaneId pane,
        string labelKey,
        BackstageIconKind icon,
        string iconCommandName,
        string keyTip,
        string automationId,
        string automationNameKey,
        string automationHelpTextKey,
        string tooltipTitleKey,
        string? tooltipDescriptionKey = null,
        bool dockBottom = false) =>
        new(
            FreeXBackstageNavigationEntryKind.Pane,
            pane,
            null,
            labelKey,
            icon,
            iconCommandName,
            dockBottom,
            keyTip,
            automationId,
            automationNameKey,
            automationHelpTextKey,
            tooltipTitleKey,
            tooltipDescriptionKey);

    private static FreeXBackstageNavigationEntry Command(
        FreeXBackstageCommandId command,
        string labelKey,
        BackstageIconKind icon,
        string iconCommandName,
        string keyTip,
        string automationId,
        string automationNameKey,
        string automationHelpTextKey,
        string tooltipTitleKey,
        string? tooltipDescriptionKey = null,
        bool dockBottom = false) =>
        new(
            FreeXBackstageNavigationEntryKind.Command,
            null,
            command,
            labelKey,
            icon,
            iconCommandName,
            dockBottom,
            keyTip,
            automationId,
            automationNameKey,
            automationHelpTextKey,
            tooltipTitleKey,
            tooltipDescriptionKey);

    private static FreeXBackstageNavigationEntry Divider(bool dockBottom = false) =>
        new(
            FreeXBackstageNavigationEntryKind.Divider,
            null,
            null,
            null,
            null,
            null,
            dockBottom);
}
