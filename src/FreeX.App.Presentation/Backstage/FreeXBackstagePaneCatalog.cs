using Free.Shared.Ribbon;

namespace FreeX.App.Presentation.Backstage;

public enum FreeXBackstageInfoSurface
{
    WpfInfoPane,
    AvaloniaInfoDialog,
    AvaloniaLivePane,
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
    ActiveSheetProtection,
    WorkbookStatistics,
    Accessibility,
    FormulaErrors
}

public sealed record FreeXBackstageInfoDetailDefinition(
    FreeXBackstageInfoDetailId Id,
    string LabelKey,
    string ValueAutomationId);

public enum FreeXBackstageExportScopeId
{
    ActiveSheet,
    SelectedRange,
    VisibleWorkbook
}

public enum FreeXBackstageExportOutputKindId
{
    Pdf,
    Xps
}

public enum FreeXBackstageAccountDetailId
{
    FreeXUserName,
    LocalOsAccount,
    Device,
    AppVersion,
    OptionsFile,
    CurrentWorkbook,
    Sharing,
    Export
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
            FreeXBackstageInfoSurface.AvaloniaLivePane => [],
            FreeXBackstageInfoSurface.WpfInfoPane or FreeXBackstageInfoSurface.ParityCapture => WpfInfoActions,
            _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, null)
        };

    public static IReadOnlyList<FreeXBackstageInfoDetailDefinition> BuildInfoDetails(
        FreeXBackstageInfoSurface surface) =>
        surface switch
        {
            FreeXBackstageInfoSurface.AvaloniaInfoDialog => AvaloniaInfoDetails,
            FreeXBackstageInfoSurface.AvaloniaLivePane => AvaloniaLiveInfoDetails,
            FreeXBackstageInfoSurface.ParityCapture => ParityInfoDetails,
            FreeXBackstageInfoSurface.WpfInfoPane => WpfInfoDetails,
            _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, null)
        };

    public static string GetExportScopeLabelKey(FreeXBackstageExportScopeId scope, bool isAvailable) =>
        scope switch
        {
            FreeXBackstageExportScopeId.SelectedRange => isAvailable
                ? "Backstage_Export_ScopeSelection"
                : "Backstage_Export_ScopeSelectionUnavailable",
            FreeXBackstageExportScopeId.VisibleWorkbook => "Backstage_Export_ScopeWorkbook",
            FreeXBackstageExportScopeId.ActiveSheet => "Backstage_Export_ScopeActiveSheet",
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
        };

    public static string GetExportScopeAutomationId(FreeXBackstageExportScopeId scope) =>
        "BackstageExportScope_" + scope;

    public static string GetExportOutputKindLabelKey(FreeXBackstageExportOutputKindId outputKind) =>
        outputKind switch
        {
            FreeXBackstageExportOutputKindId.Xps => "Backstage_Export_FormatXps",
            FreeXBackstageExportOutputKindId.Pdf => "Backstage_Export_FormatPdf",
            _ => throw new ArgumentOutOfRangeException(nameof(outputKind), outputKind, null)
        };

    public static string GetExportOutputKindAutomationId(FreeXBackstageExportOutputKindId outputKind) =>
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
        // R129-model-avalonia-info-formula-issues-1: matches the WPF host's WpfInfoDetails
        // FormulaErrors row -- File > Info must surface circular-reference/formula-issue counts on
        // this shell too, not just Windows.
        new(FreeXBackstageInfoDetailId.FormulaErrors, "Backstage_Info_FormulaErrorsLabel", "BackstageInfoFormulaErrors"),
    ];

    private static readonly FreeXBackstageInfoDetailDefinition[] AvaloniaLiveInfoDetails =
    [
        new(FreeXBackstageInfoDetailId.WorkbookName, "Backstage_LiveInfo_WorkbookLabel", "BackstageLiveInfoWorkbook"),
        new(FreeXBackstageInfoDetailId.FilePath, "Backstage_LiveInfo_LocationLabel", "BackstageLiveInfoLocation"),
        new(FreeXBackstageInfoDetailId.Format, "Backstage_LiveInfo_FormatLabel", "BackstageLiveInfoFormat"),
        new(FreeXBackstageInfoDetailId.FileSize, "Backstage_LiveInfo_SizeLabel", "BackstageLiveInfoSize"),
        new(FreeXBackstageInfoDetailId.LastModified, "Backstage_LiveInfo_LastModifiedLabel", "BackstageLiveInfoLastModified"),
        new(FreeXBackstageInfoDetailId.SheetCount, "Backstage_LiveInfo_SheetsLabel", "BackstageLiveInfoSheets"),
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
        new(FreeXBackstageInfoDetailId.WorkbookStatistics, "MainWindow_Text_WorkbookStatistics", "BackstageInfoWorkbookStatistics"),
        new(FreeXBackstageInfoDetailId.Accessibility, "MainWindow_Text_Accessibility", "BackstageInfoAccessibility"),
        new(FreeXBackstageInfoDetailId.FormulaErrors, "MainWindow_Text_FormulaErrors", "BackstageInfoFormulaErrors"),
    ];

    private static readonly FreeXBackstageInfoDetailDefinition[] ParityInfoDetails =
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

    // Mirrors the File > Account page ("Local account information"): local app/OS identity,
    // version, and local workbook/sharing/export readiness rows (no cloud account). Interactive
    // and parity-capture renderers share this row set through the catalog.
    private static readonly FreeXBackstageAccountDetailDefinition[] AccountDetails =
    [
        new(FreeXBackstageAccountDetailId.FreeXUserName, "Backstage_Account_FreeXUserNameLabel", "BackstageAccountFreeXUserName"),
        new(FreeXBackstageAccountDetailId.LocalOsAccount, "Backstage_Account_LocalOSAccountLabel", "BackstageAccountLocalOsAccount"),
        new(FreeXBackstageAccountDetailId.Device, "Backstage_Account_DeviceRowLabel", "BackstageAccountDevice"),
        new(FreeXBackstageAccountDetailId.AppVersion, "Backstage_Account_AppVersionLabel", "BackstageAccountAppVersion"),
        new(FreeXBackstageAccountDetailId.OptionsFile, "Backstage_Account_OptionsFileLabel", "BackstageAccountOptionsFile"),
        new(FreeXBackstageAccountDetailId.CurrentWorkbook, "Backstage_Account_CurrentWorkbookLabel", "BackstageAccountCurrentWorkbook"),
        new(FreeXBackstageAccountDetailId.Sharing, "Backstage_Account_SharingLabel", "BackstageAccountSharing"),
        new(FreeXBackstageAccountDetailId.Export, "Backstage_Account_ExportLabel", "BackstageAccountExport"),
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
