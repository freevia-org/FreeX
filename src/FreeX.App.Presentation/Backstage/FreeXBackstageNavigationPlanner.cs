using Free.Shared.Ribbon;

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
    RibbonCommandIconKind? Icon,
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
            RibbonCommandIconKind.Grid,
            "Home",
            "H",
            HomePaneAutomationId,
            "MainWindow_Text_Home",
            "MainWindow_TooltipTitle_Home",
            "MainWindow_TooltipTitle_Home"),

        Command(
            FreeXBackstageCommandId.New,
            "MainWindow_Text_New",
            RibbonCommandIconKind.Insert,
            "New",
            "N",
            "BackstageNewButton",
            "MainWindow_Text_New",
            "MainWindow_TooltipDescription_CreateANewWorkbook",
            "MainWindow_TooltipTitle_New"),

        Command(
            FreeXBackstageCommandId.Open,
            "MainWindow_Text_Open",
            RibbonCommandIconKind.GetData,
            "Open",
            "O",
            "BackstageOpenButton",
            "MainWindow_Text_Open",
            "MainWindow_TooltipDescription_OpenAnExistingWorkbook",
            "MainWindow_TooltipTitle_Open"),

        Command(
            FreeXBackstageCommandId.Share,
            "MainWindow_Text_Share",
            RibbonCommandIconKind.Share,
            "Share",
            "SH",
            "BackstageShareButton",
            "MainWindow_Text_Share",
            "MainWindow_TooltipDescription_SaveTheWorkbookIfNeededAndOpenWindowsShareForTheFile",
            "MainWindow_TooltipTitle_Share",
            "MainWindow_TooltipDescription_SaveTheWorkbookIfNeededAndOpenWindowsShareForTheFile"),

        Divider(),

        Pane(
            FreeXBackstagePaneId.Info,
            "MainWindow_Text_Info",
            RibbonCommandIconKind.Info,
            "Info",
            "I",
            InfoPaneAutomationId,
            "MainWindow_Text_Info",
            "MainWindow_Text_ReviewLocalFileStatusAndUnsupportedWorkbookFeatureWarnings",
            "MainWindow_TooltipTitle_Info"),

        Command(
            FreeXBackstageCommandId.Save,
            "MainWindow_Text_Save",
            RibbonCommandIconKind.Save,
            "Save",
            "S",
            "BackstageSaveButton",
            "MainWindow_AutomationName_Save",
            "MainWindow_TooltipDescription_SaveTheWorkbook",
            "MainWindow_TooltipTitle_Save"),

        Command(
            FreeXBackstageCommandId.SaveAs,
            "MainWindow_Text_SaveAs",
            RibbonCommandIconKind.Save,
            "Save As",
            "A",
            "BackstageSaveAsButton",
            "MainWindow_TooltipTitle_SaveAs",
            "MainWindow_TooltipDescription_SaveTheWorkbookWithANewNameOrFormat",
            "MainWindow_TooltipTitle_SaveAs"),

        Pane(
            FreeXBackstagePaneId.Print,
            "MainWindow_Text_Print",
            RibbonCommandIconKind.Print,
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
            RibbonCommandIconKind.Share,
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
            RibbonCommandIconKind.WindowClose,
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
            RibbonCommandIconKind.Info,
            "Account",
            "AC",
            "BackstageAccountButton",
            "MainWindow_AutomationName_Account",
            "MainWindow_AutomationHelpText_ShowLocalAccountInformationForFreeX",
            "MainWindow_TooltipTitle_LocalAccount",
            "MainWindow_TooltipDescription_MicrosoftAccountIntegrationIsNotImplementedFreeXUsesLocalFilesAndLocalOp_EC989658",
            dockBottom: true),

        Command(
            FreeXBackstageCommandId.Options,
            "MainWindow_Text_Options",
            RibbonCommandIconKind.View,
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
        RibbonCommandIconKind icon,
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
        RibbonCommandIconKind icon,
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
