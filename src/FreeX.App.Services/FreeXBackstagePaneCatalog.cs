using Free.Shared.Ribbon;

namespace FreeX.App.Services;

public enum FreeXBackstageInfoSurface
{
    WpfInfoPane,
    AvaloniaInfoDialog,
    ParityCapture
}

public enum FreeXBackstageInfoActionId
{
    ProtectWorkbook,
    CheckAccessibility,
    WorkbookStatistics,
    ErrorChecking,
    ProtectSheet,
    InspectWorkbook
}

public sealed record FreeXBackstageInfoActionDefinition(
    FreeXBackstageInfoActionId Id,
    string LabelKey,
    string AutomationId,
    RibbonCommandIconKind Icon,
    string? KeyTip = null,
    string? AutomationHelpTextKey = null,
    string? TooltipTitleKey = null,
    string? TooltipDescriptionKey = null,
    string? DetailKey = null,
    bool UsesDynamicLabel = false);

public enum FreeXBackstageInfoDetailId
{
    WorkbookName,
    FilePath,
    SheetCount,
    Format,
    FileSize,
    LastModified,
    Share,
    Export,
    WorkbookProtection,
    ActiveSheetProtection
}

public sealed record FreeXBackstageInfoDetailDefinition(
    FreeXBackstageInfoDetailId Id,
    string LabelKey,
    string ValueAutomationId);

public enum FreeXBackstageAccountDetailId
{
    Product,
    Version,
    Device,
    User
}

public sealed record FreeXBackstageAccountDetailDefinition(
    FreeXBackstageAccountDetailId Id,
    string LabelKey,
    string ValueAutomationId);

public enum FreeXBackstageAccountActionId
{
    Options,
    LegalNotices
}

public sealed record FreeXBackstageAccountActionDefinition(
    FreeXBackstageAccountActionId Id,
    string LabelKey,
    string AutomationId);

public enum FreeXBackstageAccountNoticeId
{
    Trademark,
    License,
    Privacy
}

public sealed record FreeXBackstageAccountNoticeDefinition(
    FreeXBackstageAccountNoticeId Id,
    string AutomationId);

/// <summary>
/// Renderer-neutral metadata for FreeX's domain-specific Backstage panes. Shells attach controls and
/// callbacks; this catalog owns stable ordering, string keys, automation ids, and shared option labels.
/// </summary>
public static class FreeXBackstagePaneCatalog
{
    public static IReadOnlyList<FreeXBackstageInfoActionDefinition> BuildInfoActions(
        FreeXBackstageInfoSurface surface) =>
        surface switch
        {
            FreeXBackstageInfoSurface.AvaloniaInfoDialog => AvaloniaInfoActions,
            FreeXBackstageInfoSurface.WpfInfoPane or FreeXBackstageInfoSurface.ParityCapture => WpfInfoActions,
            _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, null)
        };

    public static IReadOnlyList<FreeXBackstageInfoDetailDefinition> BuildInfoDetails(
        FreeXBackstageInfoSurface surface) =>
        surface switch
        {
            FreeXBackstageInfoSurface.AvaloniaInfoDialog => AvaloniaInfoDetails,
            FreeXBackstageInfoSurface.ParityCapture => ParityInfoDetails,
            FreeXBackstageInfoSurface.WpfInfoPane => WpfInfoDetails,
            _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, null)
        };

    public static string GetExportScopeLabelKey(WorkbookExportPrintScope scope, bool isAvailable) =>
        scope switch
        {
            WorkbookExportPrintScope.SelectedRange => isAvailable
                ? "Backstage_Export_ScopeSelection"
                : "Backstage_Export_ScopeSelectionUnavailable",
            WorkbookExportPrintScope.VisibleWorkbook => "Backstage_Export_ScopeWorkbook",
            WorkbookExportPrintScope.ActiveSheet => "Backstage_Export_ScopeActiveSheet",
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
        };

    public static string GetExportScopeAutomationId(WorkbookExportPrintScope scope) =>
        "BackstageExportScope_" + scope;

    public static string GetExportOutputKindLabelKey(WorkbookExportPrintOutputKind outputKind) =>
        outputKind switch
        {
            WorkbookExportPrintOutputKind.Xps => "Backstage_Export_FormatXps",
            WorkbookExportPrintOutputKind.Pdf => "Backstage_Export_FormatPdf",
            _ => throw new ArgumentOutOfRangeException(nameof(outputKind), outputKind, null)
        };

    public static string GetExportOutputKindAutomationId(WorkbookExportPrintOutputKind outputKind) =>
        "BackstageExportFormat_" + outputKind;

    public static IReadOnlyList<FreeXBackstageAccountDetailDefinition> BuildAccountDetails() =>
        AccountDetails;

    public static IReadOnlyList<FreeXBackstageAccountActionDefinition> BuildAccountActions(
        bool optionsAvailable) =>
        optionsAvailable
            ? AccountActions
            : AccountActionsWithoutOptions;

    public static IReadOnlyList<FreeXBackstageAccountNoticeDefinition> BuildAccountNotices() =>
        AccountNotices;

    private static readonly FreeXBackstageInfoActionDefinition[] WpfInfoActions =
    [
        new(
            FreeXBackstageInfoActionId.ProtectWorkbook,
            "MainWindow_Content_ProtectWorkbook",
            "BackstageInfoProtectWorkbookButton",
            RibbonCommandIconKind.Protect,
            KeyTip: "PW",
            DetailKey: "MainWindow_Text_ControlWhatTypesOfChangesOthersCanMake",
            UsesDynamicLabel: true),

        new(
            FreeXBackstageInfoActionId.CheckAccessibility,
            "MainWindow_Text_CheckAccessibility",
            "BackstageInfoCheckAccessibilityButton",
            RibbonCommandIconKind.Accessibility,
            KeyTip: "CA",
            AutomationHelpTextKey: "MainWindow_AutomationHelpText_FindMergedCellsBlankTableHeadersObjectsMissingAlternateTextAndChartsWith_AD813E90",
            TooltipTitleKey: "MainWindow_TooltipTitle_CheckAccessibility",
            TooltipDescriptionKey: "MainWindow_TooltipDescription_FindMergedCellsBlankTableHeadersObjectsMissingAlternateTextAndChartsWith_4FECDB20",
            DetailKey: "MainWindow_Text_FindMergedCellsAndObjectsMissingAltText"),

        new(
            FreeXBackstageInfoActionId.WorkbookStatistics,
            "MainWindow_Content_WorkbookStatistics",
            "BackstageInfoWorkbookStatisticsButton",
            RibbonCommandIconKind.Info,
            KeyTip: "W",
            AutomationHelpTextKey: "MainWindow_AutomationHelpText_ShowWorkbookCountsForSheetsCellsFormulasCommentsAndObjects",
            TooltipTitleKey: "MainWindow_TooltipTitle_WorkbookStatistics",
            TooltipDescriptionKey: "MainWindow_TooltipDescription_ShowWorkbookCountsForSheetsCellsFormulasCommentsAndObjects"),

        new(
            FreeXBackstageInfoActionId.ErrorChecking,
            "MainWindow_Content_ErrorChecking",
            "BackstageInfoErrorCheckingButton",
            RibbonCommandIconKind.Warning,
            KeyTip: "EC",
            AutomationHelpTextKey: "MainWindow_TooltipDescription_CheckForCommonErrorsInTheFormulasOnThisSheetOrOpenErrorCheckingOptions",
            TooltipTitleKey: "MainWindow_TooltipTitle_ErrorChecking",
            TooltipDescriptionKey: "MainWindow_TooltipDescription_CheckForCommonErrorsInTheFormulasOnThisSheetOrOpenErrorCheckingOptions",
            DetailKey: "MainWindow_Text_ReviewLocalFileStatusAndUnsupportedWorkbookFeatureWarnings"),
    ];

    private static readonly FreeXBackstageInfoActionDefinition[] AvaloniaInfoActions =
    [
        new(
            FreeXBackstageInfoActionId.ProtectSheet,
            "Backstage_Info_ProtectSheetAction",
            "BackstageInfoProtectSheetButton",
            RibbonCommandIconKind.Protect),

        new(
            FreeXBackstageInfoActionId.ProtectWorkbook,
            "Backstage_Info_ProtectWorkbookAction",
            "BackstageInfoProtectWorkbookButton",
            RibbonCommandIconKind.Protect),

        new(
            FreeXBackstageInfoActionId.InspectWorkbook,
            "Backstage_Info_InspectAction",
            "BackstageInfoInspectButton",
            RibbonCommandIconKind.Accessibility),
    ];

    private static readonly FreeXBackstageInfoDetailDefinition[] AvaloniaInfoDetails =
    [
        new(FreeXBackstageInfoDetailId.WorkbookName, "Backstage_Info_NameLabel", "BackstageInfoName"),
        new(FreeXBackstageInfoDetailId.FilePath, "Backstage_Info_PathLabel", "BackstageInfoPath"),
        new(FreeXBackstageInfoDetailId.Format, "Backstage_Info_FormatLabel", "BackstageInfoFormat"),
        new(FreeXBackstageInfoDetailId.FileSize, "Backstage_Info_SizeLabel", "BackstageInfoSize"),
        new(FreeXBackstageInfoDetailId.LastModified, "Backstage_Info_ModifiedLabel", "BackstageInfoModified"),
        new(FreeXBackstageInfoDetailId.SheetCount, "Backstage_Info_SheetsLabel", "BackstageInfoSheets"),
    ];

    private static readonly FreeXBackstageInfoDetailDefinition[] WpfInfoDetails =
    [
        new(FreeXBackstageInfoDetailId.WorkbookName, "MainWindow_Text_WorkbookName", "BackstageInfoWorkbookName"),
        new(FreeXBackstageInfoDetailId.FilePath, "MainWindow_Text_FilePath", "BackstageInfoFilePath"),
        new(FreeXBackstageInfoDetailId.SheetCount, "MainWindow_Text_Sheets", "BackstageInfoSheetCount"),
        new(FreeXBackstageInfoDetailId.Format, "MainWindow_Text_Format", "BackstageInfoFormat"),
        new(FreeXBackstageInfoDetailId.FileSize, "MainWindow_Text_FileSize", "BackstageInfoFileSize"),
        new(FreeXBackstageInfoDetailId.LastModified, "MainWindow_Text_LastModified", "BackstageInfoLastModified"),
        new(FreeXBackstageInfoDetailId.Share, "MainWindow_Text_Share", "BackstageInfoShareStatus"),
        new(FreeXBackstageInfoDetailId.Export, "MainWindow_Text_Export", "BackstageInfoExportStatus"),
        new(FreeXBackstageInfoDetailId.WorkbookProtection, "MainWindow_Text_WorkbookProtection", "BackstageInfoWorkbookProtection"),
        new(FreeXBackstageInfoDetailId.ActiveSheetProtection, "MainWindow_Text_ActiveSheetProtection", "BackstageInfoActiveSheetProtection"),
    ];

    private static readonly FreeXBackstageInfoDetailDefinition[] ParityInfoDetails = WpfInfoDetails;

    private static readonly FreeXBackstageAccountDetailDefinition[] AccountDetails =
    [
        new(FreeXBackstageAccountDetailId.Product, "Backstage_Account_ProductLabel", "BackstageAccountProduct"),
        new(FreeXBackstageAccountDetailId.Version, "Backstage_Account_VersionLabel", "BackstageAccountVersion"),
        new(FreeXBackstageAccountDetailId.Device, "Backstage_Account_DeviceLabel", "BackstageAccountDevice"),
        new(FreeXBackstageAccountDetailId.User, "Backstage_Account_UserLabel", "BackstageAccountUser"),
    ];

    private static readonly FreeXBackstageAccountActionDefinition[] AccountActions =
    [
        new(FreeXBackstageAccountActionId.Options, "Backstage_Account_OptionsButton", "BackstageAccountOptionsButton"),
        new(FreeXBackstageAccountActionId.LegalNotices, "Backstage_Account_LegalNoticesButton", "BackstageAccountLegalNoticesButton"),
    ];

    private static readonly FreeXBackstageAccountActionDefinition[] AccountActionsWithoutOptions =
    [
        new(FreeXBackstageAccountActionId.LegalNotices, "Backstage_Account_LegalNoticesButton", "BackstageAccountLegalNoticesButton"),
    ];

    private static readonly FreeXBackstageAccountNoticeDefinition[] AccountNotices =
    [
        new(FreeXBackstageAccountNoticeId.Trademark, "BackstageAccountTrademark"),
        new(FreeXBackstageAccountNoticeId.License, "BackstageAccountLicense"),
        new(FreeXBackstageAccountNoticeId.Privacy, "BackstageAccountPrivacy"),
    ];
}
