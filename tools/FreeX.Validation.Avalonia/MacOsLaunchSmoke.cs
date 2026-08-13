// Launch-smoke orchestration belongs to the external validation host, not the product binary.
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Automation;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Free.Shared.AppServices;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.DataTools;
using FreeX.App.Presentation.Dialogs;
using FreeX.App.Presentation.FormulaBar;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using UiText = FreeX.App.Localization.LocalizedUiText;
using AvaloniaGrid = Avalonia.Controls.Grid;

namespace FreeX.App.Avalonia;

#if FREEX_VALIDATION_HOST
internal sealed record MacOsLaunchSmokeOptions(
    string ReportPath,
    bool VerifyImageClipboardPaste,
    bool VerifyLiveCommandKeys,
    string? DiagnosticsDirectory)
{
    public const string Argument = "--macos-launch-smoke";
    public const string DiagnosticsDirectoryArgument = "--macos-launch-smoke-diagnostics-dir";
    public const string VerifyImageClipboardPasteArgument = "--macos-launch-smoke-verify-image-clipboard";
    public const string VerifyLiveCommandKeysArgument = "--macos-launch-smoke-verify-live-command-keys";

    // Platform-neutral aliases used by the Linux preview lane. They drive the same
    // headless launch-smoke coordinator and report contract as the macOS arguments;
    // the macOS spellings stay for the existing hosted macOS workflow and guards.
    public const string NeutralArgument = "--launch-smoke";
    public const string NeutralDiagnosticsDirectoryArgument = "--launch-smoke-diagnostics-dir";
    public const string NeutralVerifyImageClipboardPasteArgument = "--launch-smoke-verify-image-clipboard";
    public const string NeutralVerifyLiveCommandKeysArgument = "--launch-smoke-verify-live-command-keys";

    private static bool IsReportArgument(string argument) =>
        string.Equals(argument, Argument, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(argument, NeutralArgument, StringComparison.OrdinalIgnoreCase);

    private static bool IsDiagnosticsDirectoryArgument(string argument) =>
        string.Equals(argument, DiagnosticsDirectoryArgument, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(argument, NeutralDiagnosticsDirectoryArgument, StringComparison.OrdinalIgnoreCase);

    private static bool IsVerifyImageClipboardPasteArgument(string argument) =>
        string.Equals(argument, VerifyImageClipboardPasteArgument, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(argument, NeutralVerifyImageClipboardPasteArgument, StringComparison.OrdinalIgnoreCase);

    private static bool IsVerifyLiveCommandKeysArgument(string argument) =>
        string.Equals(argument, VerifyLiveCommandKeysArgument, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(argument, NeutralVerifyLiveCommandKeysArgument, StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(
        IReadOnlyList<string> args,
        out MacOsLaunchSmokeOptions? options,
        out string[] startupArguments,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(args);

        options = null;
        error = "";
        var filteredArguments = new List<string>();
        string? reportPath = null;
        string? diagnosticsDirectory = null;
        var verifyImageClipboardPaste = false;
        var verifyLiveCommandKeys = false;
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (IsDiagnosticsDirectoryArgument(argument))
            {
                if (diagnosticsDirectory is not null)
                {
                    startupArguments = [];
                    error = $"{DiagnosticsDirectoryArgument} was specified more than once.";
                    return false;
                }

                if (index + 1 >= args.Count)
                {
                    startupArguments = [];
                    error = $"{DiagnosticsDirectoryArgument} requires a directory path.";
                    return false;
                }

                diagnosticsDirectory = args[++index];
                if (string.IsNullOrWhiteSpace(diagnosticsDirectory))
                {
                    startupArguments = [];
                    error = $"{DiagnosticsDirectoryArgument} requires a non-empty directory path.";
                    return false;
                }

                continue;
            }

            if (IsVerifyImageClipboardPasteArgument(argument))
            {
                verifyImageClipboardPaste = true;
                continue;
            }

            if (IsVerifyLiveCommandKeysArgument(argument))
            {
                verifyLiveCommandKeys = true;
                continue;
            }

            if (!IsReportArgument(argument))
            {
                filteredArguments.Add(argument);
                continue;
            }

            if (reportPath is not null)
            {
                startupArguments = [];
                error = $"{Argument} was specified more than once.";
                return false;
            }

            if (index + 1 >= args.Count)
            {
                startupArguments = [];
                error = $"{Argument} requires a report path.";
                return false;
            }

            reportPath = args[++index];
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                startupArguments = [];
                error = $"{Argument} requires a non-empty report path.";
                return false;
            }
        }

        if (diagnosticsDirectory is not null && reportPath is null)
        {
            startupArguments = [];
            error = $"{DiagnosticsDirectoryArgument} requires {Argument}.";
            return false;
        }

        if (reportPath is not null)
            options = new MacOsLaunchSmokeOptions(
                reportPath,
                verifyImageClipboardPaste,
                verifyLiveCommandKeys,
                diagnosticsDirectory);

        startupArguments = filteredArguments.ToArray();
        return true;
    }
}

internal sealed record MacOsLaunchSmokeDialogSnapshot(
    bool HasFindDialog,
    bool HasFindDialogTextBox,
    bool HasFindDialogActionButtons,
    bool HasFindDialogOptions,
    bool HasFindDialogFormatControls,
    bool HasFindDialogCompactLayout,
    bool HasReplaceDialog,
    bool HasReplaceDialogTextBoxes,
    bool HasReplaceDialogActionButtons,
    bool HasReplaceDialogOptions,
    bool HasReplaceDialogFormatControls,
    bool HasReplaceDialogCompactLayout,
    bool HasGoToDialog,
    bool HasGoToDialogReferenceControls,
    bool HasGoToDialogHistoryControls,
    bool HasGoToDialogSpecialControl,
    bool HasGoToDialogCompactLayout,
    bool HasGoToSpecialDialog,
    bool HasGoToSpecialKindControls,
    bool HasGoToSpecialValueTypeControls,
    bool HasGoToSpecialDialogCompactLayout,
    bool HasFindDialogClosedWithoutAccept,
    bool HasReplaceDialogClosedWithoutAccept,
    bool HasGoToDialogClosedWithoutAccept,
    bool HasGoToSpecialDialogClosedWithoutAccept,
    bool HasFormatCellsDialog = false,
    bool HasFormatCellsDialogTabStrip = false,
    bool HasFormatCellsDialogDefaultNumberTab = false,
    bool HasFormatCellsDialogNumberControls = false,
    bool HasFormatCellsDialogActionButtons = false,
    bool HasFormatCellsDialogCompactLayout = false,
    bool HasFormatCellsDialogClosedWithoutAccept = false,
    bool HasSortDialog = false,
    bool HasSortDialogSortOnControls = false,
    bool HasSortDialogColorControls = false,
    bool HasSortDialogActionButtons = false,
    bool HasSortDialogCompactLayout = false,
    bool HasSortDialogClosedWithoutAccept = false,
    bool HasDataValidationDropdownControl = false,
    bool HasDataValidationDropdownItems = false,
    bool HasDataValidationDialog = false,
    bool HasDataValidationDialogCriteriaControls = false,
    bool HasDataValidationDialogMessageControls = false,
    bool HasDataValidationDialogActionButtons = false,
    bool HasDataValidationDialogCompactLayout = false,
    bool HasDataValidationDialogClosedWithoutAccept = false,
    bool HasConditionalFormatRuleDialog = false,
    bool HasConditionalFormatRuleTypeControls = false,
    bool HasConditionalFormatRulePresetControls = false,
    bool HasConditionalFormatRuleValueControls = false,
    bool HasConditionalFormatRuleActionButtons = false,
    bool HasConditionalFormatRuleCompactLayout = false,
    bool HasConditionalFormatRuleDialogClosedWithoutAccept = false,
    bool HasManageConditionalFormatsDialog = false,
    bool HasManageConditionalFormatsListControls = false,
    bool HasManageConditionalFormatsReorderControls = false,
    bool HasManageConditionalFormatsAppliesToControls = false,
    bool HasManageConditionalFormatsActionButtons = false,
    bool HasManageConditionalFormatsCompactLayout = false,
    bool HasManageConditionalFormatsDialogClosedWithoutAccept = false)
{
    public static MacOsLaunchSmokeDialogSnapshot Empty { get; } = new(
        HasFindDialog: false,
        HasFindDialogTextBox: false,
        HasFindDialogActionButtons: false,
        HasFindDialogOptions: false,
        HasFindDialogFormatControls: false,
        HasFindDialogCompactLayout: false,
        HasReplaceDialog: false,
        HasReplaceDialogTextBoxes: false,
        HasReplaceDialogActionButtons: false,
        HasReplaceDialogOptions: false,
        HasReplaceDialogFormatControls: false,
        HasReplaceDialogCompactLayout: false,
        HasGoToDialog: false,
        HasGoToDialogReferenceControls: false,
        HasGoToDialogHistoryControls: false,
        HasGoToDialogSpecialControl: false,
        HasGoToDialogCompactLayout: false,
        HasGoToSpecialDialog: false,
        HasGoToSpecialKindControls: false,
        HasGoToSpecialValueTypeControls: false,
        HasGoToSpecialDialogCompactLayout: false,
        HasFindDialogClosedWithoutAccept: false,
        HasReplaceDialogClosedWithoutAccept: false,
        HasGoToDialogClosedWithoutAccept: false,
        HasGoToSpecialDialogClosedWithoutAccept: false,
        HasFormatCellsDialog: false,
        HasFormatCellsDialogTabStrip: false,
        HasFormatCellsDialogDefaultNumberTab: false,
        HasFormatCellsDialogNumberControls: false,
        HasFormatCellsDialogActionButtons: false,
        HasFormatCellsDialogCompactLayout: false,
        HasFormatCellsDialogClosedWithoutAccept: false,
        HasSortDialog: false,
        HasSortDialogSortOnControls: false,
        HasSortDialogColorControls: false,
        HasSortDialogActionButtons: false,
        HasSortDialogCompactLayout: false,
        HasSortDialogClosedWithoutAccept: false,
        HasDataValidationDropdownControl: false,
        HasDataValidationDropdownItems: false,
        HasDataValidationDialog: false,
        HasDataValidationDialogCriteriaControls: false,
        HasDataValidationDialogMessageControls: false,
        HasDataValidationDialogActionButtons: false,
        HasDataValidationDialogCompactLayout: false,
        HasDataValidationDialogClosedWithoutAccept: false,
        HasConditionalFormatRuleDialog: false,
        HasConditionalFormatRuleTypeControls: false,
        HasConditionalFormatRulePresetControls: false,
        HasConditionalFormatRuleValueControls: false,
        HasConditionalFormatRuleActionButtons: false,
        HasConditionalFormatRuleCompactLayout: false,
        HasConditionalFormatRuleDialogClosedWithoutAccept: false,
        HasManageConditionalFormatsDialog: false,
        HasManageConditionalFormatsListControls: false,
        HasManageConditionalFormatsReorderControls: false,
        HasManageConditionalFormatsAppliesToControls: false,
        HasManageConditionalFormatsActionButtons: false,
        HasManageConditionalFormatsCompactLayout: false,
        HasManageConditionalFormatsDialogClosedWithoutAccept: false);

    public bool IsPassed =>
        HasFindDialog &&
        HasFindDialogTextBox &&
        HasFindDialogActionButtons &&
        HasFindDialogOptions &&
        HasFindDialogFormatControls &&
        HasFindDialogCompactLayout &&
        HasReplaceDialog &&
        HasReplaceDialogTextBoxes &&
        HasReplaceDialogActionButtons &&
        HasReplaceDialogOptions &&
        HasReplaceDialogFormatControls &&
        HasReplaceDialogCompactLayout &&
        HasGoToDialog &&
        HasGoToDialogReferenceControls &&
        HasGoToDialogHistoryControls &&
        HasGoToDialogSpecialControl &&
        HasGoToDialogCompactLayout &&
        HasGoToSpecialDialog &&
        HasGoToSpecialKindControls &&
        HasGoToSpecialValueTypeControls &&
        HasGoToSpecialDialogCompactLayout &&
        HasFormatCellsDialog &&
        HasFormatCellsDialogTabStrip &&
        HasFormatCellsDialogDefaultNumberTab &&
        HasFormatCellsDialogNumberControls &&
        HasFormatCellsDialogActionButtons &&
        HasFormatCellsDialogCompactLayout &&
        HasSortDialog &&
        HasSortDialogSortOnControls &&
        HasSortDialogColorControls &&
        HasSortDialogActionButtons &&
        HasSortDialogCompactLayout &&
        HasDataValidationDropdownControl &&
        HasDataValidationDropdownItems &&
        HasDataValidationDialog &&
        HasDataValidationDialogCriteriaControls &&
        HasDataValidationDialogMessageControls &&
        HasDataValidationDialogActionButtons &&
        HasDataValidationDialogCompactLayout &&
        HasFindDialogClosedWithoutAccept &&
        HasReplaceDialogClosedWithoutAccept &&
        HasGoToDialogClosedWithoutAccept &&
        HasGoToSpecialDialogClosedWithoutAccept &&
        HasFormatCellsDialogClosedWithoutAccept &&
        HasSortDialogClosedWithoutAccept &&
        HasDataValidationDialogClosedWithoutAccept &&
        HasConditionalFormatRuleDialog &&
        HasConditionalFormatRuleTypeControls &&
        HasConditionalFormatRulePresetControls &&
        HasConditionalFormatRuleValueControls &&
        HasConditionalFormatRuleActionButtons &&
        HasConditionalFormatRuleCompactLayout &&
        HasConditionalFormatRuleDialogClosedWithoutAccept &&
        HasManageConditionalFormatsDialog &&
        HasManageConditionalFormatsListControls &&
        HasManageConditionalFormatsReorderControls &&
        HasManageConditionalFormatsAppliesToControls &&
        HasManageConditionalFormatsActionButtons &&
        HasManageConditionalFormatsCompactLayout &&
        HasManageConditionalFormatsDialogClosedWithoutAccept;
}

internal sealed record MacOsLaunchSmokeCommandKeySnapshot(
    bool HasNewWorkbookMenuGesture,
    bool HasOpenMenuGesture,
    bool HasSaveMenuGesture,
    bool HasSaveAsMenuGesture,
    bool HasCloseWorkbookMenuGesture,
    bool HasQuitMenuGesture,
    bool HasSelectAllMenuGesture,
    bool HasFindMenuGesture,
    bool HasBoldMenuGesture,
    bool HasItalicMenuGesture,
    bool HasUnderlineMenuGesture,
    bool HasFindDirectRouteSourceGuard,
    bool HasPageUpDirectRouteSourceGuard,
    bool HasPageDownDirectRouteSourceGuard)
{
    public static MacOsLaunchSmokeCommandKeySnapshot Empty { get; } = new(
        HasNewWorkbookMenuGesture: false,
        HasOpenMenuGesture: false,
        HasSaveMenuGesture: false,
        HasSaveAsMenuGesture: false,
        HasCloseWorkbookMenuGesture: false,
        HasQuitMenuGesture: false,
        HasSelectAllMenuGesture: false,
        HasFindMenuGesture: false,
        HasBoldMenuGesture: false,
        HasItalicMenuGesture: false,
        HasUnderlineMenuGesture: false,
        HasFindDirectRouteSourceGuard: false,
        HasPageUpDirectRouteSourceGuard: false,
        HasPageDownDirectRouteSourceGuard: false);

    public bool IsPassed =>
        HasNewWorkbookMenuGesture &&
        HasOpenMenuGesture &&
        HasSaveMenuGesture &&
        HasSaveAsMenuGesture &&
        HasCloseWorkbookMenuGesture &&
        HasQuitMenuGesture &&
        HasSelectAllMenuGesture &&
        HasFindMenuGesture &&
        HasBoldMenuGesture &&
        HasItalicMenuGesture &&
        HasUnderlineMenuGesture &&
        HasFindDirectRouteSourceGuard &&
        HasPageUpDirectRouteSourceGuard &&
        HasPageDownDirectRouteSourceGuard;
}

internal sealed record MacOsLaunchSmokeLiveCommandKeySnapshot(
    bool IsReady,
    bool HasSelectAllCommandKey,
    bool HasSelectAllStateChange,
    bool HasBoldCommandKey,
    bool HasBoldStateChange,
    bool InitialBoldState,
    bool CurrentBoldState,
    bool HasItalicCommandKey,
    bool HasItalicStateChange,
    bool InitialItalicState,
    bool CurrentItalicState,
    bool HasUnderlineCommandKey,
    bool HasUnderlineStateChange,
    bool InitialUnderlineState,
    bool CurrentUnderlineState)
{
    public static MacOsLaunchSmokeLiveCommandKeySnapshot Empty { get; } = new(
        IsReady: false,
        HasSelectAllCommandKey: false,
        HasSelectAllStateChange: false,
        HasBoldCommandKey: false,
        HasBoldStateChange: false,
        InitialBoldState: false,
        CurrentBoldState: false,
        HasItalicCommandKey: false,
        HasItalicStateChange: false,
        InitialItalicState: false,
        CurrentItalicState: false,
        HasUnderlineCommandKey: false,
        HasUnderlineStateChange: false,
        InitialUnderlineState: false,
        CurrentUnderlineState: false);

    public static MacOsLaunchSmokeLiveCommandKeySnapshot Ready(
        bool boldState,
        bool italicState,
        bool underlineState) =>
        new(
            IsReady: true,
            HasSelectAllCommandKey: false,
            HasSelectAllStateChange: false,
            HasBoldCommandKey: false,
            HasBoldStateChange: false,
            InitialBoldState: boldState,
            CurrentBoldState: boldState,
            HasItalicCommandKey: false,
            HasItalicStateChange: false,
            InitialItalicState: italicState,
            CurrentItalicState: italicState,
            HasUnderlineCommandKey: false,
            HasUnderlineStateChange: false,
            InitialUnderlineState: underlineState,
            CurrentUnderlineState: underlineState);

    public bool HasAnyCommandKey =>
        HasSelectAllCommandKey ||
        HasBoldCommandKey ||
        HasItalicCommandKey ||
        HasUnderlineCommandKey;

    public bool IsPassed =>
        IsReady &&
        HasSelectAllCommandKey &&
        HasSelectAllStateChange &&
        HasBoldCommandKey &&
        HasBoldStateChange &&
        HasItalicCommandKey &&
        HasItalicStateChange &&
        HasUnderlineCommandKey &&
        HasUnderlineStateChange;
}

internal sealed record MacOsLaunchSmokeSnapshot(
    bool WindowShown,
    string WindowTitle,
    string DisplayName,
    string ActiveSheetName,
    int SheetTabCount,
    int ViewportRowCount,
    int ViewportColumnCount,
    int ExternalImageClipboardPictureCount,
    int ExternalImageClipboardPicturePngByteCount,
    MacOsLaunchSmokeDialogSnapshot DialogEvidence,
    string? OpenedSourcePath,
    bool IsOpening,
    bool HasNewSheetButton,
    bool HasFormatPainterButton,
    bool HasAutoSumButton,
    bool HasAutoSumSumMenuItem,
    bool HasAutoSumAverageMenuItem,
    bool HasAutoSumCountNumbersMenuItem,
    bool HasAutoSumCountAllMenuItem,
    bool HasAutoSumMaxMenuItem,
    bool HasAutoSumMinMenuItem,
    bool HasFillCellsButton,
    bool HasFillDownMenuItem,
    bool HasFillRightMenuItem,
    bool HasFillUpMenuItem,
    bool HasFillLeftMenuItem,
    bool HasClearButton,
    bool HasClearAllMenuItem,
    bool HasClearFormatsMenuItem,
    bool HasClearContentsMenuItem,
    bool HasClearCommentsMenuItem,
    bool HasClearHyperlinksMenuItem,
    bool HasBordersButton,
    bool HasWrapTextButton,
    bool HasMergeAndCenterButton,
    bool HasFormulaBoxAutomationName,
    bool HasFormulaBoxAutomationHelp,
    bool HasFormulaBoxAutomationId,
    bool HasStatusTextAutomationName,
    bool HasStatusTextAutomationHelp,
    bool HasStatusTextAutomationId,
    bool HasStatusTextValue,
    bool HasCellAddressAutomationName,
    bool HasCellAddressAutomationHelp,
    bool HasCellAddressAutomationId,
    bool HasSelectionStatsAutomationName,
    bool HasSelectionStatsAutomationHelp,
    bool HasSelectionStatsAutomationId,
    bool HasFocusableSheetTab,
    bool HasFocusableActiveSheetTab,
    bool HasShellFocusCycleTargets,
    bool HasSheetTabContextKeyboardHelp,
    bool HasSheetTabContextRenameMenuItem,
    bool HasSheetTabContextTabColorMenuItem,
    bool HasSheetTabContextNoColorMenuItem,
    bool HasSheetTabContextSelectAllSheetsMenuItem,
    bool HasSheetTabContextUngroupSheetsMenuItem,
    string NativeTopLevelMenuOrder,
    string NativeDockTopLevelMenuOrder,
    bool HasNativeDockMenu,
    bool HasNativeDockFileMenu,
    int NativeDockFileMenuItemCount,
    bool HasNativeFileMenu,
    bool HasNativeHomeMenu,
    bool HasNativeInsertMenu,
    bool HasNativePageLayoutMenu,
    bool HasNativeFormulasMenu,
    bool HasNativeDataMenu,
    bool HasNativeReviewMenu,
    bool HasNativeViewMenu,
    bool HasNativeSheetMenu,
    bool HasNativeWindowMenu,
    bool HasNativeHelpMenu,
    bool HasNativeNewWorkbookMenuItem,
    bool HasNativeOpenMenuItem,
    bool HasNativeOpenRecentMenuItem,
    int NativeOpenRecentItemCount,
    bool HasNativeSaveMenuItem,
    bool HasNativeSaveAsMenuItem,
    bool HasNativeExportPdfMenuItem,
    bool HasNativeShareWorkbookMenuItem,
    bool HasNativeWorkbookStatisticsMenuItem,
    bool HasNativeCloseWorkbookMenuItem,
    bool HasNativeNewSheetMenuItem,
    bool HasNativeRenameSheetMenuItem,
    bool HasNativeDuplicateSheetMenuItem,
    bool HasNativeMoveSheetLeftMenuItem,
    bool HasNativeMoveSheetRightMenuItem,
    bool HasNativeTabColorMenuItem,
    bool HasNativeClearTabColorMenuItem,
    int NativeTabColorSwatchCount,
    bool HasNativeSelectAllSheetsMenuItem,
    bool HasNativeUngroupSheetsMenuItem,
    bool HasNativeHideSheetMenuItem,
    bool HasNativeUnhideSheetMenuItem,
    bool HasNativeDeleteSheetMenuItem,
    bool HasNativeUndoMenuItem,
    bool HasNativeRedoMenuItem,
    bool HasNativeCutMenuItem,
    bool HasNativeCopyMenuItem,
    bool HasNativePasteMenuItem,
    bool HasNativePasteSpecialMenuItem,
    bool HasNativeFormatPainterMenuItem,
    bool HasNativePasteSpecialCommentsMenuItem,
    bool HasNativePasteSpecialValidationMenuItem,
    bool HasNativePasteSpecialAllExceptBordersMenuItem,
    bool HasNativePasteSpecialAllMergingConditionalFormatsMenuItem,
    bool HasNativePasteSpecialColumnWidthsMenuItem,
    bool HasNativePasteSpecialFormulasAndNumberFormatsMenuItem,
    bool HasNativePasteSpecialValuesAndNumberFormatsMenuItem,
    bool HasNativePasteSpecialValuesAndSourceFormattingMenuItem,
    bool HasNativePasteSpecialKeepSourceColumnWidthsMenuItem,
    bool HasNativePasteSpecialPasteLinkMenuItem,
    bool HasNativePasteSpecialTextMenuItem,
    bool HasNativePasteSpecialUnicodeTextMenuItem,
    bool HasNativePasteSpecialPictureMenuItem,
    bool HasNativePasteSpecialLinkedPictureMenuItem,
    bool HasNativeSelectAllMenuItem,
    bool HasNativeFindMenuItem,
    bool HasNativeFindNextMenuItem,
    bool HasNativeReplaceMenuItem,
    bool HasNativeGoToMenuItem,
    bool HasNativeGoToSpecialMenuItem,
    bool HasNativeSortAscendingMenuItem,
    bool HasNativeSortDescendingMenuItem,
    bool HasNativeFlashFillMenuItem,
    bool HasNativeAdvancedFilterMenuItem,
    bool HasNativeRemoveDuplicatesMenuItem,
    bool HasNativeSubtotalMenuItem,
    bool HasNativeDataValidationPreviewMenuItem,
    bool HasNativeDataValidationMenuItem,
    bool HasNativeWhatIfAnalysisMenuItem,
    bool HasNativeGoalSeekMenuItem,
    bool HasNativeDataTableMenuItem,
    bool HasNativeScenarioManagerMenuItem,
    bool HasNativeForecastSheetMenuItem,
    bool HasNativeReviewSummaryMenuItem,
    bool HasNativeCheckAccessibilityMenuItem,
    bool HasNativeNextNoteMenuItem,
    bool HasNativePreviousNoteMenuItem,
    bool HasNativeNextCommentMenuItem,
    bool HasNativePreviousCommentMenuItem,
    bool HasNativeAutoSumMenuItem,
    bool HasNativeAutoSumSumMenuItem,
    bool HasNativeAutoSumAverageMenuItem,
    bool HasNativeAutoSumCountNumbersMenuItem,
    bool HasNativeAutoSumCountAllMenuItem,
    bool HasNativeAutoSumMaxMenuItem,
    bool HasNativeAutoSumMinMenuItem,
    bool HasNativeFillCellsMenuItem,
    bool HasNativeFillDownMenuItem,
    bool HasNativeFillRightMenuItem,
    bool HasNativeFillUpMenuItem,
    bool HasNativeFillLeftMenuItem,
    bool HasNativeClearMenuItem,
    bool HasNativeClearAllMenuItem,
    bool HasNativeClearFormatsMenuItem,
    bool HasNativeClearContentsMenuItem,
    bool HasNativeClearCommentsMenuItem,
    bool HasNativeClearHyperlinksMenuItem,
    bool HasNativeBoldMenuItem,
    bool HasNativeItalicMenuItem,
    bool HasNativeUnderlineMenuItem,
    bool HasNativeDoubleUnderlineMenuItem,
    bool HasNativeStrikethroughMenuItem,
    bool HasNativeIncreaseFontSizeMenuItem,
    bool HasNativeDecreaseFontSizeMenuItem,
    bool HasNativeFillColorMenuItem,
    bool HasNativeClearFillMenuItem,
    bool HasNativeFontColorMenuItem,
    int NativeFillColorSwatchCount,
    int NativeFontColorSwatchCount,
    bool HasNativeBordersMenuItem,
    int NativeBordersPresetCount,
    bool HasNativeCellStylesMenuItem,
    int NativeCellStylesPresetCount,
    bool HasNativeHorizontalTextMenuItem,
    bool HasNativeAngleCounterclockwiseMenuItem,
    bool HasNativeAngleClockwiseMenuItem,
    bool HasNativeVerticalTextMenuItem,
    bool HasNativeRotateTextUpMenuItem,
    bool HasNativeRotateTextDownMenuItem,
    bool HasNativeCurrencyFormatMenuItem,
    bool HasNativePercentFormatMenuItem,
    bool HasNativeCommaStyleMenuItem,
    bool HasNativeIncreaseDecimalMenuItem,
    bool HasNativeDecreaseDecimalMenuItem,
    bool HasNativeAlignTopMenuItem,
    bool HasNativeAlignMiddleMenuItem,
    bool HasNativeAlignBottomMenuItem,
    bool HasNativeWrapTextMenuItem,
    bool HasNativeMergeAndCenterMenuItem,
    bool HasNativeUnmergeCellsMenuItem,
    bool HasNativeShowGridlinesMenuItem,
    bool HasNativeShowHeadingsMenuItem,
    bool HasNativeZoomInMenuItem,
    bool HasNativeZoomOutMenuItem,
    bool HasNativeZoom100MenuItem,
    bool HasNativeZoomToSelectionMenuItem,
    bool HasNativeFreezePanesMenuItem,
    bool HasNativeFreezeTopRowMenuItem,
    bool HasNativeFreezeFirstColumnMenuItem,
    bool HasNativeUnfreezePanesMenuItem,
    bool HasNativeDecreaseIndentMenuItem,
    bool HasNativeIncreaseIndentMenuItem,
    bool HasNativeAlignLeftMenuItem,
    bool HasNativeAlignCenterMenuItem,
    bool HasNativeAlignRightMenuItem,
    bool HasNativeShowFormulasMenuItem,
    bool HasNativeMinimizeWindowMenuItem,
    bool HasNativeZoomWindowMenuItem,
    bool HasNativeBringAllToFrontMenuItem,
    bool HasNativeHelpOnlineMenuItem,
    bool HasNativeSendFeedbackMenuItem,
    bool HasNativeCheckForUpdatesMenuItem,
    bool HasNativeAboutMenuItem,
    bool HasNativeLegalNoticesMenuItem,
    bool HasNativeQuitMenuItem,
    bool HasNativeFormatCellsMenuItem = false)
{
    public bool HasAccessibilitySmokeEvidence =>
        HasFormulaBoxAutomationName &&
        HasFormulaBoxAutomationHelp &&
        HasFormulaBoxAutomationId &&
        HasStatusTextAutomationName &&
        HasStatusTextAutomationHelp &&
        HasStatusTextAutomationId &&
        HasStatusTextValue &&
        HasCellAddressAutomationName &&
        HasCellAddressAutomationHelp &&
        HasCellAddressAutomationId &&
        HasSelectionStatsAutomationName &&
        HasSelectionStatsAutomationHelp &&
        HasSelectionStatsAutomationId;

    public bool HasShellEvidence =>
        WindowShown &&
        !IsOpening &&
        !string.IsNullOrWhiteSpace(OpenedSourcePath) &&
        SheetTabCount > 0 &&
        ViewportRowCount > 0 &&
        ViewportColumnCount > 0 &&
        HasNewSheetButton &&
        HasFormatPainterButton &&
        HasAutoSumButton &&
        HasAutoSumSumMenuItem &&
        HasAutoSumAverageMenuItem &&
        HasAutoSumCountNumbersMenuItem &&
        HasAutoSumCountAllMenuItem &&
        HasAutoSumMaxMenuItem &&
        HasAutoSumMinMenuItem &&
        HasFillCellsButton &&
        HasFillDownMenuItem &&
        HasFillRightMenuItem &&
        HasFillUpMenuItem &&
        HasFillLeftMenuItem &&
        HasClearButton &&
        HasClearAllMenuItem &&
        HasClearFormatsMenuItem &&
        HasClearContentsMenuItem &&
        HasClearCommentsMenuItem &&
        HasClearHyperlinksMenuItem &&
        HasBordersButton &&
        HasWrapTextButton &&
        HasMergeAndCenterButton &&
        HasAccessibilitySmokeEvidence &&
        HasFocusableSheetTab &&
        HasFocusableActiveSheetTab &&
        HasShellFocusCycleTargets &&
        HasSheetTabContextKeyboardHelp &&
        HasSheetTabContextRenameMenuItem &&
        HasSheetTabContextTabColorMenuItem &&
        HasSheetTabContextNoColorMenuItem &&
        HasSheetTabContextSelectAllSheetsMenuItem &&
        HasSheetTabContextUngroupSheetsMenuItem &&
        string.Equals(NativeTopLevelMenuOrder, "File|Home|Insert|Page Layout|Formulas|Data|Review|View|Sheet|Window|Help", StringComparison.Ordinal) &&
        string.Equals(NativeDockTopLevelMenuOrder, "File|Home|Insert|Page Layout|Formulas|Data|Review|View|Sheet|Window|Help", StringComparison.Ordinal) &&
        HasNativeDockMenu &&
        HasNativeDockFileMenu &&
        NativeDockFileMenuItemCount > 0 &&
        HasNativeFileMenu &&
        HasNativeHomeMenu &&
        HasNativeInsertMenu &&
        HasNativePageLayoutMenu &&
        HasNativeFormulasMenu &&
        HasNativeDataMenu &&
        HasNativeReviewMenu &&
        HasNativeViewMenu &&
        HasNativeSheetMenu &&
        HasNativeWindowMenu &&
        HasNativeHelpMenu &&
        HasNativeNewWorkbookMenuItem &&
        HasNativeOpenMenuItem &&
        HasNativeOpenRecentMenuItem &&
        NativeOpenRecentItemCount > 0 &&
        HasNativeSaveMenuItem &&
        HasNativeSaveAsMenuItem &&
        HasNativeExportPdfMenuItem &&
        HasNativeShareWorkbookMenuItem &&
        HasNativeWorkbookStatisticsMenuItem &&
        HasNativeCloseWorkbookMenuItem &&
        HasNativeNewSheetMenuItem &&
        HasNativeRenameSheetMenuItem &&
        HasNativeDuplicateSheetMenuItem &&
        HasNativeMoveSheetLeftMenuItem &&
        HasNativeMoveSheetRightMenuItem &&
        HasNativeTabColorMenuItem &&
        HasNativeClearTabColorMenuItem &&
        NativeTabColorSwatchCount == CellColorPalettePlanner.BuildDefaultSwatches().Count &&
        HasNativeSelectAllSheetsMenuItem &&
        HasNativeUngroupSheetsMenuItem &&
        HasNativeHideSheetMenuItem &&
        HasNativeUnhideSheetMenuItem &&
        HasNativeDeleteSheetMenuItem &&
        HasNativeUndoMenuItem &&
        HasNativeRedoMenuItem &&
        HasNativeCutMenuItem &&
        HasNativeCopyMenuItem &&
        HasNativePasteMenuItem &&
        HasNativePasteSpecialMenuItem &&
        HasNativeFormatPainterMenuItem &&
        HasNativePasteSpecialCommentsMenuItem &&
        HasNativePasteSpecialValidationMenuItem &&
        HasNativePasteSpecialAllExceptBordersMenuItem &&
        HasNativePasteSpecialAllMergingConditionalFormatsMenuItem &&
        HasNativePasteSpecialColumnWidthsMenuItem &&
        HasNativePasteSpecialFormulasAndNumberFormatsMenuItem &&
        HasNativePasteSpecialValuesAndNumberFormatsMenuItem &&
        HasNativePasteSpecialValuesAndSourceFormattingMenuItem &&
        HasNativePasteSpecialKeepSourceColumnWidthsMenuItem &&
        HasNativePasteSpecialPasteLinkMenuItem &&
        HasNativePasteSpecialTextMenuItem &&
        HasNativePasteSpecialUnicodeTextMenuItem &&
        HasNativePasteSpecialPictureMenuItem &&
        HasNativePasteSpecialLinkedPictureMenuItem &&
        HasNativeSelectAllMenuItem &&
        HasNativeFindMenuItem &&
        HasNativeFindNextMenuItem &&
        HasNativeReplaceMenuItem &&
        HasNativeGoToMenuItem &&
        HasNativeGoToSpecialMenuItem &&
        HasNativeSortAscendingMenuItem &&
        HasNativeSortDescendingMenuItem &&
        HasNativeFlashFillMenuItem &&
        HasNativeAdvancedFilterMenuItem &&
        HasNativeRemoveDuplicatesMenuItem &&
        HasNativeSubtotalMenuItem &&
        HasNativeDataValidationPreviewMenuItem &&
        HasNativeDataValidationMenuItem &&
        HasNativeWhatIfAnalysisMenuItem &&
        HasNativeGoalSeekMenuItem &&
        HasNativeDataTableMenuItem &&
        HasNativeScenarioManagerMenuItem &&
        HasNativeForecastSheetMenuItem &&
        HasNativeReviewSummaryMenuItem &&
        HasNativeCheckAccessibilityMenuItem &&
        HasNativeNextNoteMenuItem &&
        HasNativePreviousNoteMenuItem &&
        HasNativeNextCommentMenuItem &&
        HasNativePreviousCommentMenuItem &&
        HasNativeFormatCellsMenuItem &&
        HasNativeAutoSumMenuItem &&
        HasNativeAutoSumSumMenuItem &&
        HasNativeAutoSumAverageMenuItem &&
        HasNativeAutoSumCountNumbersMenuItem &&
        HasNativeAutoSumCountAllMenuItem &&
        HasNativeAutoSumMaxMenuItem &&
        HasNativeAutoSumMinMenuItem &&
        HasNativeFillCellsMenuItem &&
        HasNativeFillDownMenuItem &&
        HasNativeFillRightMenuItem &&
        HasNativeFillUpMenuItem &&
        HasNativeFillLeftMenuItem &&
        HasNativeClearMenuItem &&
        HasNativeClearAllMenuItem &&
        HasNativeClearFormatsMenuItem &&
        HasNativeClearContentsMenuItem &&
        HasNativeClearCommentsMenuItem &&
        HasNativeClearHyperlinksMenuItem &&
        HasNativeBoldMenuItem &&
        HasNativeItalicMenuItem &&
        HasNativeUnderlineMenuItem &&
        HasNativeDoubleUnderlineMenuItem &&
        HasNativeStrikethroughMenuItem &&
        HasNativeIncreaseFontSizeMenuItem &&
        HasNativeDecreaseFontSizeMenuItem &&
        HasNativeFillColorMenuItem &&
        HasNativeClearFillMenuItem &&
        HasNativeFontColorMenuItem &&
        NativeFillColorSwatchCount == CellColorPalettePlanner.BuildDefaultSwatches().Count &&
        NativeFontColorSwatchCount == CellColorPalettePlanner.BuildDefaultSwatches().Count &&
        HasNativeBordersMenuItem &&
        NativeBordersPresetCount == Enum.GetValues<CellBorderPreset>().Length &&
        HasNativeCellStylesMenuItem &&
        NativeCellStylesPresetCount == Enum.GetValues<CellStylePreset>().Length &&
        HasNativeHorizontalTextMenuItem &&
        HasNativeAngleCounterclockwiseMenuItem &&
        HasNativeAngleClockwiseMenuItem &&
        HasNativeVerticalTextMenuItem &&
        HasNativeRotateTextUpMenuItem &&
        HasNativeRotateTextDownMenuItem &&
        HasNativeCurrencyFormatMenuItem &&
        HasNativePercentFormatMenuItem &&
        HasNativeCommaStyleMenuItem &&
        HasNativeIncreaseDecimalMenuItem &&
        HasNativeDecreaseDecimalMenuItem &&
        HasNativeAlignTopMenuItem &&
        HasNativeAlignMiddleMenuItem &&
        HasNativeAlignBottomMenuItem &&
        HasNativeWrapTextMenuItem &&
        HasNativeMergeAndCenterMenuItem &&
        HasNativeUnmergeCellsMenuItem &&
        HasNativeShowGridlinesMenuItem &&
        HasNativeShowHeadingsMenuItem &&
        HasNativeZoomInMenuItem &&
        HasNativeZoomOutMenuItem &&
        HasNativeZoom100MenuItem &&
        HasNativeZoomToSelectionMenuItem &&
        HasNativeFreezePanesMenuItem &&
        HasNativeFreezeTopRowMenuItem &&
        HasNativeFreezeFirstColumnMenuItem &&
        HasNativeUnfreezePanesMenuItem &&
        HasNativeDecreaseIndentMenuItem &&
        HasNativeIncreaseIndentMenuItem &&
        HasNativeAlignLeftMenuItem &&
        HasNativeAlignCenterMenuItem &&
        HasNativeAlignRightMenuItem &&
        HasNativeShowFormulasMenuItem &&
        HasNativeMinimizeWindowMenuItem &&
        HasNativeZoomWindowMenuItem &&
        HasNativeBringAllToFrontMenuItem &&
        HasNativeHelpOnlineMenuItem &&
        HasNativeSendFeedbackMenuItem &&
        HasNativeCheckForUpdatesMenuItem &&
        HasNativeAboutMenuItem &&
        HasNativeLegalNoticesMenuItem &&
        HasNativeQuitMenuItem;

    public bool IsPassed => HasShellEvidence && DialogEvidence.IsPassed;
}
internal static class MacOsLaunchSmokeCoordinator
{

    private static async Task<MacOsLaunchSmokeDialogSnapshot> CaptureDialogEvidenceAsync(
        MainWindow.RendererValidationAccess access)
    {
        var hasFindDialog = false;
        var hasFindDialogTextBox = false;
        var hasFindDialogActionButtons = false;
        var hasFindDialogOptions = false;
        var hasFindDialogFormatControls = false;
        var hasFindDialogCompactLayout = false;
        var findDialogClosedWithoutAccept = await access.InspectFindDialogAsync(probe =>
        {
            hasFindDialog = HasDialog(probe.Dialog, "Find");
            hasFindDialogTextBox = HasAutomationId(probe.FindBox, "FindTextBox") &&
                probe.FindBox.MinWidth >= 300;
            hasFindDialogActionButtons =
                HasButton(probe.FindNextButton, "FindNextButton", "Find Next") &&
                HasButton(probe.FindAllButton, "FindAllButton", "Find All") &&
                HasButton(probe.CancelButton, "FindCancelButton", "Cancel");
            hasFindDialogOptions = HasFindOptions(probe.OptionsControls, "Find", defaultLookInIndex: 0);
            hasFindDialogFormatControls =
                HasButton(probe.ChooseFormatButton, "FindChooseFormatFromCellButton", "Choose From Cell") &&
                HasButton(probe.ClearFormatButton, "FindClearFormatButton", "Clear Format") &&
                !probe.ClearFormatButton.IsVisible;
            hasFindDialogCompactLayout = HasCompactDialog(probe.Dialog, width: 420, height: 430, minWidth: 360, minHeight: 390);
        });

        var hasReplaceDialog = false;
        var hasReplaceDialogTextBoxes = false;
        var hasReplaceDialogActionButtons = false;
        var hasReplaceDialogOptions = false;
        var hasReplaceDialogFormatControls = false;
        var hasReplaceDialogCompactLayout = false;
        var replaceDialogClosedWithoutAccept = await access.InspectReplaceDialogAsync(probe =>
        {
            hasReplaceDialog = HasDialog(probe.Dialog, "Replace");
            hasReplaceDialogTextBoxes =
                HasAutomationId(probe.FindBox, "ReplaceFindTextBox") &&
                HasAutomationId(probe.ReplaceBox, "ReplaceWithTextBox") &&
                probe.FindBox.MinWidth >= 300 &&
                probe.ReplaceBox.MinWidth >= 300;
            hasReplaceDialogActionButtons =
                HasButton(probe.ReplaceButton, "ReplaceButton", "Replace") &&
                HasButton(probe.ReplaceAllButton, "ReplaceAllButton", "Replace All") &&
                HasButton(probe.CancelButton, "ReplaceCancelButton", "Cancel");
            hasReplaceDialogOptions = HasFindOptions(probe.OptionsControls, "Replace", defaultLookInIndex: 1);
            hasReplaceDialogFormatControls =
                HasButton(probe.ChooseFindFormatButton, "ReplaceFindChooseFormatFromCellButton", "Choose From Cell") &&
                HasButton(probe.ClearFindFormatButton, "ReplaceFindClearFormatButton", "Clear Format") &&
                !probe.ClearFindFormatButton.IsVisible &&
                HasButton(probe.ChooseReplaceFormatButton, "ReplaceWithChooseFormatFromCellButton", "Choose From Cell") &&
                HasButton(probe.ClearReplaceFormatButton, "ReplaceWithClearFormatButton", "Clear Format") &&
                !probe.ClearReplaceFormatButton.IsVisible;
            hasReplaceDialogCompactLayout = HasCompactDialog(probe.Dialog, width: 420, height: 520, minWidth: 360, minHeight: 480);
        });

        var hasGoToDialog = false;
        var hasGoToDialogReferenceControls = false;
        var hasGoToDialogHistoryControls = false;
        var hasGoToDialogSpecialControl = false;
        var hasGoToDialogCompactLayout = false;
        var goToDialogClosedWithoutAccept = await access.InspectGoToDialogAsync(
            probe =>
            {
                hasGoToDialog = HasDialog(probe.Dialog, "Go To");
                hasGoToDialogReferenceControls =
                    HasAutomationId(probe.InputBox, "GoToReferenceBox") &&
                    HasButton(probe.AcceptButton, "GoToReferenceBoxAcceptButton", "OK") &&
                    HasButton(probe.CancelButton, "GoToReferenceBoxCancelButton", "Cancel");
                hasGoToDialogHistoryControls =
                    HasAutomationId(probe.HistoryList, "GoToHistoryList") &&
                    string.Equals(AutomationProperties.GetName(probe.HistoryList), "Go To", StringComparison.Ordinal) &&
                    probe.HistoryList.ItemCount > 0;
                hasGoToDialogSpecialControl =
                    HasButton(probe.SpecialButton, "GoToSpecialButton", "Special...");
                hasGoToDialogCompactLayout = HasCompactDialog(probe.Dialog, width: 420, height: 320, minWidth: 420, minHeight: 320);
            });

        var hasGoToSpecialDialog = false;
        var hasGoToSpecialKindControls = false;
        var hasGoToSpecialValueTypeControls = false;
        var hasGoToSpecialDialogCompactLayout = false;
        var goToSpecialDialogClosedWithoutAccept = await access.InspectGoToSpecialDialogAsync(probe =>
        {
            hasGoToSpecialDialog = HasDialog(probe.Dialog, "Go To Special");
            var hasSelectedKind =
                probe.KindBox is AvaloniaGrid kindGrid &&
                kindGrid.Children.OfType<RadioButton>().Any(button => button.IsChecked == true);
            hasGoToSpecialKindControls =
                HasAutomationId(probe.KindBox, "GoToSpecialKindBox") &&
                hasSelectedKind &&
                HasButton(probe.OkButton, "GoToSpecialOkButton", "OK") &&
                HasButton(probe.CancelButton, "GoToSpecialCancelButton", "Cancel");
            hasGoToSpecialValueTypeControls =
                HasCheckBox(probe.NumbersBox, "GoToSpecialNumbersBox", "Numbers") &&
                HasCheckBox(probe.TextBox, "GoToSpecialTextBox", "Text") &&
                HasCheckBox(probe.LogicalsBox, "GoToSpecialLogicalsBox", "Logicals") &&
                HasCheckBox(probe.ErrorsBox, "GoToSpecialErrorsBox", "Errors");
            hasGoToSpecialDialogCompactLayout = HasCompactDialog(
                probe.Dialog,
                width: GoToSpecialDialogPlanner.Width,
                height: GoToSpecialDialogPlanner.Height,
                minWidth: GoToSpecialDialogPlanner.Width,
                minHeight: GoToSpecialDialogPlanner.Height);
        });

        var hasFormatCellsDialog = false;
        var hasFormatCellsDialogTabStrip = false;
        var hasFormatCellsDialogDefaultNumberTab = false;
        var hasFormatCellsDialogNumberControls = false;
        var hasFormatCellsDialogActionButtons = false;
        var hasFormatCellsDialogCompactLayout = false;
        var formatCellsDialogClosedWithoutAccept = await access.InspectFormatCellsDialogAsync(probe =>
        {
            hasFormatCellsDialog = HasDialog(probe.Dialog, "Format Cells");
            hasFormatCellsDialogTabStrip =
                HasAutomationId(probe.TabStrip, "FormatCellsTabStrip") &&
                HasAutomationId(probe.NumberTab, "FormatCellsNumberTab") &&
                HasAutomationId(probe.AlignmentTab, "FormatCellsAlignmentTab") &&
                HasAutomationId(probe.FontTab, "FormatCellsFontTab") &&
                HasAutomationId(probe.FillTab, "FormatCellsFillTab") &&
                HasAutomationId(probe.BorderTab, "FormatCellsBorderTab") &&
                HasAutomationId(probe.ProtectionTab, "FormatCellsProtectionTab");
            hasFormatCellsDialogDefaultNumberTab =
                probe.TabStrip.SelectedIndex == 0 &&
                HasAutomationId(probe.NumberTab, "FormatCellsNumberTab");
            hasFormatCellsDialogNumberControls =
                HasAutomationId(probe.NumberCategoryList, "FormatCellsNumberCategoryList") &&
                HasAutomationId(probe.NumberFormatBox, "FormatCellsNumberFormatBox") &&
                HasAutomationId(probe.NumberPreview, "FormatCellsNumberPreview") &&
                probe.NumberFormatBox.MinWidth >= 260;
            hasFormatCellsDialogActionButtons =
                HasButton(probe.OkButton, "FormatCellsOkButton", "OK") &&
                HasButton(probe.CancelButton, "FormatCellsCancelButton", "Cancel");
            hasFormatCellsDialogCompactLayout = HasCompactDialog(probe.Dialog, width: 690, height: 660, minWidth: 620, minHeight: 560);
        });

        var hasSortDialog = false;
        var hasSortDialogSortOnControls = false;
        var hasSortDialogColorControls = false;
        var hasSortDialogActionButtons = false;
        var hasSortDialogCompactLayout = false;
        var sortDialogClosedWithoutAccept = await access.InspectSortDialogAsync(probe =>
        {
            hasSortDialog = HasDialog(probe.Dialog, "Custom Sort");
            hasSortDialogSortOnControls =
                HasComboBox(probe.SortOnBox, "SortLevel1SortOnBox", "Sort On") &&
                probe.SortOnBox.MinWidth >= 120 &&
                string.Equals(probe.SortOnBox.SelectedItem?.ToString(), SortDialogPlannerText.Default.SortOnCellValues, StringComparison.Ordinal);
            hasSortDialogColorControls =
                HasComboBox(probe.ColorBox, "SortLevel1ColorBox", "Color") &&
                probe.ColorBox.MinWidth >= 105 &&
                !probe.ColorBox.IsEnabled &&
                string.Equals(probe.ColorBox.SelectedItem?.ToString(), "None", StringComparison.Ordinal);
            hasSortDialogActionButtons =
                HasCheckBox(probe.HeadersCheckBox, "SortHeadersCheckBox", "My data has headers") &&
                HasAutomationId(probe.LevelsGrid, "SortLevelsGrid") &&
                HasButton(probe.AddLevelButton, "SortAddLevelButton", "Add Level") &&
                HasButton(probe.DeleteLevelButton, "SortDeleteLevelButton", "Delete Level") &&
                HasButton(probe.CopyLevelButton, "SortCopyLevelButton", "Copy Level") &&
                HasButton(probe.MoveUpButton, "SortMoveUpButton", "Move Up") &&
                HasButton(probe.MoveDownButton, "SortMoveDownButton", "Move Down") &&
                HasButton(probe.OptionsButton, "SortOptionsButton", "Options...") &&
                HasButton(probe.OkButton, "SortOkButton", "OK") &&
                HasButton(probe.CancelButton, "SortCancelButton", "Cancel");
            hasSortDialogCompactLayout = HasCompactDialog(probe.Dialog, width: 760, height: 500, minWidth: 680, minHeight: 420);
        });

        var dvPlan = new DataValidationDropdownPlan(
            ["Yes", "No"],
            "Yes",
            new DataValidationDropdownBounds(12, 16, 48, 18));
        var dropdown = access.CreateDataValidationDropdown(
            dvPlan,
            DataValidationAffordancePlanner.ArrowButtonWidth,
            Math.Max(DataValidationDropdownPlanner.MinimumHeight, dvPlan.Bounds.Height));
        var hasDataValidationDropdownControl =
            HasComboBox(dropdown, "WorksheetDataValidationDropdown", "Data validation list") &&
            string.Equals(
                AutomationProperties.GetHelpText(dropdown),
                "Pick a permitted value for the active cell.",
                StringComparison.Ordinal) &&
            dropdown.Width == DataValidationAffordancePlanner.ArrowButtonWidth &&
            dropdown.Height == Math.Max(DataValidationDropdownPlanner.MinimumHeight, dvPlan.Bounds.Height) &&
            dropdown.MinWidth == DataValidationDropdownPlanner.MinimumWidth &&
            dropdown.MinHeight == DataValidationDropdownPlanner.MinimumHeight;
        var hasDataValidationDropdownItems =
            dropdown.ItemsSource is IEnumerable<string> dropdownItems &&
            dropdownItems.SequenceEqual(["Yes", "No"], StringComparer.Ordinal) &&
            string.Equals(dropdown.SelectedItem?.ToString(), "Yes", StringComparison.Ordinal);

        var hasDataValidationDialog = false;
        var hasDataValidationDialogCriteriaControls = false;
        var hasDataValidationDialogMessageControls = false;
        var hasDataValidationDialogActionButtons = false;
        var hasDataValidationDialogCompactLayout = false;
        var dataValidationDialogClosedWithoutAccept = await access.InspectDataValidationDialogAsync(probe =>
        {
            hasDataValidationDialog = HasDialog(probe.Dialog, "Data Validation");
            hasDataValidationDialogCriteriaControls =
                HasAutomationId(probe.SummaryText, "DataValidationSelectionSummaryText") &&
                HasComboBox(probe.TypeBox, "DataValidationTypeBox", "Allow") &&
                HasComboBox(probe.OperatorBox, "DataValidationOperatorBox", "Data") &&
                HasAutomationId(probe.Formula1Box, "DataValidationFormula1Box") &&
                HasAutomationId(probe.Formula2Box, "DataValidationFormula2Box") &&
                HasCheckBox(probe.AllowBlankBox, "DataValidationAllowBlankBox", UiText.Get("DataValidation_IgnoreBlank")) &&
                HasCheckBox(probe.ShowDropdownBox, "DataValidationShowDropdownBox", UiText.Get("DataValidation_InCellDropdown")) &&
                !probe.ShowDropdownBox.IsVisible &&
                string.Equals(probe.TypeBox.SelectedItem?.ToString(), "Whole number", StringComparison.Ordinal) &&
                string.Equals(probe.Formula1Box.Text, "1", StringComparison.Ordinal) &&
                string.Equals(probe.Formula2Box.Text, "100", StringComparison.Ordinal);
            hasDataValidationDialogMessageControls =
                HasCheckBox(probe.ShowInputMessageBox, "DataValidationShowInputMessageBox", UiText.Get("DataValidation_ShowInputMessageWhenCellIsSelected")) &&
                HasAutomationId(probe.PromptTitleBox, "DataValidationPromptTitleBox") &&
                HasAutomationId(probe.PromptMessageBox, "DataValidationPromptMessageBox") &&
                HasCheckBox(probe.ShowErrorMessageBox, "DataValidationShowErrorMessageBox", UiText.Get("DataValidation_ShowErrorAlertAfterInvalidDataIsEntered")) &&
                HasComboBox(probe.AlertStyleBox, "DataValidationAlertStyleBox", "Style") &&
                HasAutomationId(probe.ErrorTitleBox, "DataValidationErrorTitleBox") &&
                HasAutomationId(probe.ErrorMessageBox, "DataValidationErrorMessageBox") &&
                string.Equals(probe.AlertStyleBox.SelectedItem?.ToString(), "Stop", StringComparison.Ordinal);
            hasDataValidationDialogActionButtons =
                HasButton(probe.ApplyButton, "DataValidationApplyButton", UiText.Get("Common_Ok")) &&
                HasButton(probe.ClearButton, "DataValidationClearButton", UiText.Get("DataValidation_ClearAll")) &&
                HasButton(probe.CancelButton, "DataValidationCancelButton", UiText.Get("Common_Cancel"));
            hasDataValidationDialogCompactLayout = HasCompactDialog(probe.Dialog, width: 520, height: 560, minWidth: 480, minHeight: 460);
        });

        var hasConditionalFormatRuleDialog = false;
        var hasConditionalFormatRuleTypeControls = false;
        var hasConditionalFormatRulePresetControls = false;
        var hasConditionalFormatRuleValueControls = false;
        var hasConditionalFormatRuleActionButtons = false;
        var hasConditionalFormatRuleCompactLayout = false;
        var conditionalFormatRuleDialogClosedWithoutAccept = await access.InspectConditionalFormatRuleDialogAsync(
            probe =>
            {
                hasConditionalFormatRuleDialog = HasDialog(probe.Dialog, "New Formatting Rule");
                hasConditionalFormatRuleTypeControls =
                    HasComboBox(probe.RuleTypeBox, "ConditionalFormatRuleTypeBox", "Rule type") &&
                    probe.RuleTypeBox.SelectedIndex == 0 &&
                    HasComboBox(probe.TopBottomBox, "ConditionalFormatTopBottomBox", "Top or bottom") &&
                    HasAutomationId(probe.IconSetBox, "ConditionalFormatIconSetBox");
                hasConditionalFormatRulePresetControls =
                    HasComboBox(probe.PresetBox, "ConditionalFormatPresetBox", "Preset") &&
                    probe.PresetBox.ItemCount > 0 &&
                    HasAutomationId(probe.HighlightBox, "ConditionalFormatHighlightBox") &&
                    probe.HighlightBox.SelectedIndex == 0;
                hasConditionalFormatRuleValueControls =
                    HasAutomationId(probe.OperatorBox, "ConditionalFormatOperatorBox") &&
                    HasAutomationId(probe.Value1Box, "ConditionalFormatValue1Box") &&
                    HasAutomationId(probe.FormulaBox, "ConditionalFormatFormulaBox") &&
                    HasAutomationId(probe.TextBox, "ConditionalFormatTextBox") &&
                    HasAutomationId(probe.RankBox, "ConditionalFormatRankBox") &&
                    HasAutomationId(probe.MinColorBox, "ConditionalFormatMinColorBox") &&
                    HasAutomationId(probe.MaxColorBox, "ConditionalFormatMaxColorBox");
                hasConditionalFormatRuleActionButtons =
                    HasButton(probe.OkButton, "ConditionalFormatOkButton", "OK") &&
                    HasButton(probe.CancelButton, "ConditionalFormatCancelButton", "Cancel");
                hasConditionalFormatRuleCompactLayout = HasCompactDialog(
                    probe.Dialog,
                    width: ConditionalFormatDialogCatalog.RuleEditorCaptureWidth,
                    height: ConditionalFormatDialogCatalog.RuleEditorCaptureHeight,
                    minWidth: ConditionalFormatDialogCatalog.RuleEditorMinWidth,
                    minHeight: ConditionalFormatDialogCatalog.RuleEditorMinHeight);
            });

        var hasManageConditionalFormatsDialog = false;
        var hasManageConditionalFormatsListControls = false;
        var hasManageConditionalFormatsReorderControls = false;
        var hasManageConditionalFormatsAppliesToControls = false;
        var hasManageConditionalFormatsActionButtons = false;
        var hasManageConditionalFormatsCompactLayout = false;
        var manageConditionalFormatsClosedWithoutAccept = false;
        await access.InspectManageConditionalFormatsDialogAsync(probe =>
        {
            hasManageConditionalFormatsDialog = HasDialog(probe.Dialog, UiText.Get("ManageConditionalFormats_ConditionalFormattingRulesManager"));
            hasManageConditionalFormatsListControls =
                HasAutomationId(probe.ListBox, "ManageConditionalFormatsListBox") &&
                HasText(AutomationProperties.GetName(probe.ListBox), UiText.Get("ManageConditionalFormats_ConditionalFormattingRules"));
            hasManageConditionalFormatsReorderControls =
                HasNamedButton(probe.MoveUpButton, "ManageConditionalFormatsMoveUpButton", UiText.Get("ManageConditionalFormats_MoveUp")) &&
                HasNamedButton(probe.MoveDownButton, "ManageConditionalFormatsMoveDownButton", UiText.Get("ManageConditionalFormats_MoveDown"));
            hasManageConditionalFormatsAppliesToControls =
                HasAutomationId(probe.AppliesToBox, "ManageConditionalFormatsAppliesToBox") &&
                HasButton(probe.ApplyAppliesToButton, "ManageConditionalFormatsApplyAppliesToButton", UiText.Get("ManageConditionalFormats_Apply"));
            hasManageConditionalFormatsActionButtons =
                HasButton(probe.NewButton, "ManageConditionalFormatsNewButton", UiText.Get("ManageConditionalFormats_NewRule")) &&
                HasButton(probe.EditButton, "ManageConditionalFormatsEditButton", UiText.Get("ManageConditionalFormats_EditRule")) &&
                HasButton(probe.DeleteButton, "ManageConditionalFormatsDeleteButton", UiText.Get("ManageConditionalFormats_DeleteRule")) &&
                HasButton(probe.CloseButton, "ManageConditionalFormatsCloseButton", UiText.Get("Common_Ok"));
            hasManageConditionalFormatsCompactLayout = HasCompactDialog(probe.Dialog, width: 560, height: 460, minWidth: 480, minHeight: 360);
            manageConditionalFormatsClosedWithoutAccept = true;
        });

        return new MacOsLaunchSmokeDialogSnapshot(
            hasFindDialog,
            hasFindDialogTextBox,
            hasFindDialogActionButtons,
            hasFindDialogOptions,
            hasFindDialogFormatControls,
            hasFindDialogCompactLayout,
            hasReplaceDialog,
            hasReplaceDialogTextBoxes,
            hasReplaceDialogActionButtons,
            hasReplaceDialogOptions,
            hasReplaceDialogFormatControls,
            hasReplaceDialogCompactLayout,
            hasGoToDialog,
            hasGoToDialogReferenceControls,
            hasGoToDialogHistoryControls,
            hasGoToDialogSpecialControl,
            hasGoToDialogCompactLayout,
            hasGoToSpecialDialog,
            hasGoToSpecialKindControls,
            hasGoToSpecialValueTypeControls,
            hasGoToSpecialDialogCompactLayout,
            findDialogClosedWithoutAccept,
            replaceDialogClosedWithoutAccept,
            goToDialogClosedWithoutAccept,
            goToSpecialDialogClosedWithoutAccept,
            hasFormatCellsDialog,
            hasFormatCellsDialogTabStrip,
            hasFormatCellsDialogDefaultNumberTab,
            hasFormatCellsDialogNumberControls,
            hasFormatCellsDialogActionButtons,
            hasFormatCellsDialogCompactLayout,
            formatCellsDialogClosedWithoutAccept,
            hasSortDialog,
            hasSortDialogSortOnControls,
            hasSortDialogColorControls,
            hasSortDialogActionButtons,
            hasSortDialogCompactLayout,
            sortDialogClosedWithoutAccept,
            hasDataValidationDropdownControl,
            hasDataValidationDropdownItems,
            hasDataValidationDialog,
            hasDataValidationDialogCriteriaControls,
            hasDataValidationDialogMessageControls,
            hasDataValidationDialogActionButtons,
            hasDataValidationDialogCompactLayout,
            dataValidationDialogClosedWithoutAccept,
            hasConditionalFormatRuleDialog,
            hasConditionalFormatRuleTypeControls,
            hasConditionalFormatRulePresetControls,
            hasConditionalFormatRuleValueControls,
            hasConditionalFormatRuleActionButtons,
            hasConditionalFormatRuleCompactLayout,
            conditionalFormatRuleDialogClosedWithoutAccept,
            hasManageConditionalFormatsDialog,
            hasManageConditionalFormatsListControls,
            hasManageConditionalFormatsReorderControls,
            hasManageConditionalFormatsAppliesToControls,
            hasManageConditionalFormatsActionButtons,
            hasManageConditionalFormatsCompactLayout,
            manageConditionalFormatsClosedWithoutAccept);
    }


    private static bool HasDialog(Window dialog, string title) =>
        dialog.IsVisible &&
        string.Equals(dialog.Title, title, StringComparison.Ordinal);

    private static bool HasCompactDialog(
        Window dialog,
        double width,
        double height,
        double minWidth,
        double minHeight) =>
        dialog.Width <= width &&
        dialog.Height <= height &&
        dialog.MinWidth <= minWidth &&
        dialog.MinHeight <= minHeight;

    private static bool HasButton(Button button, string automationId, string content) =>
        HasAutomationId(button, automationId) &&
        HasText(button.Content?.ToString(), content);

    private static bool HasNamedButton(Button button, string automationId, string name) =>
        HasAutomationId(button, automationId) &&
        HasText(AutomationProperties.GetName(button), name);

    private static bool HasCheckBox(CheckBox checkBox, string automationId, string content) =>
        HasAutomationId(checkBox, automationId) &&
        HasText(checkBox.Content?.ToString(), content);

    private static bool HasComboBox(ComboBox comboBox, string automationId, string name) =>
        HasAutomationId(comboBox, automationId) &&
        HasText(AutomationProperties.GetName(comboBox), name);

    private static bool HasText(string? actual, string expected) =>
        string.Equals(NormalizeText(actual), NormalizeText(expected), StringComparison.Ordinal);

    private static string NormalizeText(string? text) =>
        (text ?? string.Empty).Replace("_", string.Empty, StringComparison.Ordinal);

    private static bool HasAutomationId(Control control, string automationId) =>
        string.Equals(AutomationProperties.GetAutomationId(control), automationId, StringComparison.Ordinal);

    private static bool HasFindOptions(
        MainWindow.FindOptionsControls controls,
        string automationPrefix,
        int defaultLookInIndex) =>
        HasAutomationId(controls.Panel, $"{automationPrefix}OptionsPanel") &&
        HasAutomationId(controls.WithinBox, $"{automationPrefix}WithinBox") &&
        HasAutomationId(controls.SearchBox, $"{automationPrefix}SearchBox") &&
        HasAutomationId(controls.LookInBox, $"{automationPrefix}LookInBox") &&
        HasAutomationId(controls.MatchCaseBox, $"{automationPrefix}MatchCaseBox") &&
        HasAutomationId(controls.MatchEntireCellBox, $"{automationPrefix}MatchEntireCellBox") &&
        controls.WithinBox.SelectedIndex == 0 &&
        controls.SearchBox.SelectedIndex == 0 &&
        controls.LookInBox.SelectedIndex == defaultLookInIndex;

    private static bool HasStatusBarAccessibleValue(TextBlock statusText, TextBlock selectionStatsText) =>
        !string.IsNullOrWhiteSpace(statusText.Text) ||
        !string.IsNullOrWhiteSpace(selectionStatsText.Text);

    private static bool HasToolbarMenuItem(MenuItem item, string expectedHeader) =>
        string.Equals(item.Header?.ToString(), expectedHeader, StringComparison.Ordinal);

    private static bool HasNativeTopLevelMenu(NativeMenu? menu, NativeMenuTopLevelId id) =>
        FindNativeTopLevelSubmenu(menu, GetNativeTopLevelHeader(id)) is not null;

    private static string GetNativeTopLevelMenuOrder(NativeMenu? menu) =>
        string.Join("|", menu?.Items.OfType<NativeMenuItem>()
            .Select(static item => item.Header?.ToString())
            .Where(static header => !string.IsNullOrWhiteSpace(header)) ?? []);

    private static NativeMenu? FindNativeTopLevelSubmenu(NativeMenu? menu, string expectedHeader) =>
        menu?.Items.OfType<NativeMenuItem>().FirstOrDefault(item =>
            string.Equals(item.Header?.ToString(), expectedHeader, StringComparison.Ordinal) &&
            item.Menu is not null)?.Menu;

    private static int CountNativeTopLevelMenuItems(NativeMenu? menu, NativeMenuTopLevelId id) =>
        FindNativeTopLevelSubmenu(menu, GetNativeTopLevelHeader(id))?.Items.OfType<NativeMenuItem>()
            .Count(static item => !string.IsNullOrWhiteSpace(item.Header?.ToString())) ?? 0;

    private static string GetNativeTopLevelHeader(NativeMenuTopLevelId id) =>
        NativeMenuCatalog.TopLevelMenus.First(plan => plan.Id == id).Header;

    private static bool HasNativeMenuItem(
        NativeMenuItem item,
        string expectedHeader,
        bool requireGesture = true) =>
        string.Equals(item.Header?.ToString(), expectedHeader, StringComparison.Ordinal) &&
        (!requireGesture || item.Gesture is not null);

    private static bool HasNativeMenuItem(NativeMenuItem item, NativeMenuItemId id)
    {
        var plan = NativeMenuCatalog.GetMenuItem(id);
        return HasNativeMenuItem(item, GetNativeMenuItemHeader(plan), plan.RequiresGestureInSmoke);
    }

    private static bool HasNativeFileMenuItem(NativeMenuItem item, NativeFileMenuItemId id)
    {
        var plan = NativeMenuCatalog.GetFileMenuItem(id);
        return HasNativeMenuItem(item, GetNativeFileMenuItemHeader(plan), plan.RequiresGestureInSmoke);
    }

    private static bool HasEnabledNativeMenuItem(
        NativeMenuItem item,
        string expectedHeader,
        bool requireGesture = true) =>
        item.IsEnabled && HasNativeMenuItem(item, expectedHeader, requireGesture);

    private static bool HasEnabledNativeFileMenuItem(NativeMenuItem item, NativeFileMenuItemId id) =>
        item.IsEnabled && HasNativeFileMenuItem(item, id);

    private static bool HasNativeSubmenuItem(NativeMenu? menu, string expectedHeader) =>
        menu?.Items.OfType<NativeMenuItem>().Any(item =>
            string.Equals(item.Header?.ToString(), expectedHeader, StringComparison.Ordinal)) == true;

    private static bool HasNativeSubmenuItem(NativeMenu? menu, NativeMenuItemId id) =>
        HasNativeSubmenuItem(menu, GetNativeMenuItemHeader(NativeMenuCatalog.GetMenuItem(id)));

    private static int CountNativeColorPaletteSwatches(NativeMenu? menu) =>
        menu?.Items.OfType<NativeMenuItem>().Count(item =>
            item.Header?.ToString()?.StartsWith("#", StringComparison.Ordinal) == true) ?? 0;

    private static int CountNativeOpenRecentItems(NativeMenu? menu) =>
        menu?.Items.OfType<NativeMenuItem>().Count(item =>
            item.IsEnabled &&
            !string.Equals(item.Header?.ToString(), "(No Recent Workbooks)", StringComparison.Ordinal)) ?? 0;

    private static string GetNativeFileMenuItemHeader(NativeFileMenuItemPlan plan) =>
        plan.UsesResourceKey ? UiText.Get(plan.Label) : plan.Label;

    private static string GetNativeMenuItemHeader(NativeMenuItemPlan plan) =>
        plan.UsesResourceKey ? UiText.Get(plan.Label) : plan.Label;

    private static string FormulaBarText(string resourceKey) =>
        string.IsNullOrEmpty(resourceKey) ? string.Empty : UiText.Get(resourceKey);


    private static MacOsLaunchSmokeSnapshot CaptureSnapshot(
        MainWindow.RendererValidationAccess access,
        MacOsLaunchSmokeDialogSnapshot dialogEvidence)
    {

        var shell = access.ObserveShell();
        var nativeMenu = access.NativeMenu;
        var _aboutMenuItem = access.GetNativeMenuItem("_aboutMenuItem");
        var _advancedFilterMenuItem = access.GetNativeMenuItem("_advancedFilterMenuItem");
        var _alignBottomMenuItem = access.GetNativeMenuItem("_alignBottomMenuItem");
        var _alignCenterMenuItem = access.GetNativeMenuItem("_alignCenterMenuItem");
        var _alignLeftMenuItem = access.GetNativeMenuItem("_alignLeftMenuItem");
        var _alignMiddleMenuItem = access.GetNativeMenuItem("_alignMiddleMenuItem");
        var _alignRightMenuItem = access.GetNativeMenuItem("_alignRightMenuItem");
        var _alignTopMenuItem = access.GetNativeMenuItem("_alignTopMenuItem");
        var _angleClockwiseMenuItem = access.GetNativeMenuItem("_angleClockwiseMenuItem");
        var _angleCounterclockwiseMenuItem = access.GetNativeMenuItem("_angleCounterclockwiseMenuItem");
        var _autoSumAverageFlyoutItem = access.GetControl<MenuItem>("_autoSumAverageFlyoutItem");
        var _autoSumButton = access.GetControl<DropDownButton>("_autoSumButton");
        var _autoSumCountAllFlyoutItem = access.GetControl<MenuItem>("_autoSumCountAllFlyoutItem");
        var _autoSumCountNumbersFlyoutItem = access.GetControl<MenuItem>("_autoSumCountNumbersFlyoutItem");
        var _autoSumMaxFlyoutItem = access.GetControl<MenuItem>("_autoSumMaxFlyoutItem");
        var _autoSumMenuItem = access.GetNativeMenuItem("_autoSumMenuItem");
        var _autoSumMinFlyoutItem = access.GetControl<MenuItem>("_autoSumMinFlyoutItem");
        var _autoSumSumFlyoutItem = access.GetControl<MenuItem>("_autoSumSumFlyoutItem");
        var _boldMenuItem = access.GetNativeMenuItem("_boldMenuItem");
        var _bordersButton = access.GetControl<DropDownButton>("_bordersButton");
        var _bordersMenuItem = access.GetNativeMenuItem("_bordersMenuItem");
        var _bringAllToFrontMenuItem = access.GetNativeMenuItem("_bringAllToFrontMenuItem");
        var _cellAddressText = access.GetControl<TextBox>("_cellAddressText");
        var _cellStylesMenuItem = access.GetNativeMenuItem("_cellStylesMenuItem");
        var _checkAccessibilityMenuItem = access.GetNativeMenuItem("_checkAccessibilityMenuItem");
        var _checkForUpdatesMenuItem = access.GetNativeMenuItem("_checkForUpdatesMenuItem");
        var _clearAllFlyoutItem = access.GetControl<MenuItem>("_clearAllFlyoutItem");
        var _clearButton = access.GetControl<DropDownButton>("_clearButton");
        var _clearCommentsFlyoutItem = access.GetControl<MenuItem>("_clearCommentsFlyoutItem");
        var _clearContentsFlyoutItem = access.GetControl<MenuItem>("_clearContentsFlyoutItem");
        var _clearFillMenuItem = access.GetNativeMenuItem("_clearFillMenuItem");
        var _clearFormatsFlyoutItem = access.GetControl<MenuItem>("_clearFormatsFlyoutItem");
        var _clearHyperlinksFlyoutItem = access.GetControl<MenuItem>("_clearHyperlinksFlyoutItem");
        var _clearMenuItem = access.GetNativeMenuItem("_clearMenuItem");
        var _closeWorkbookMenuItem = access.GetNativeMenuItem("_closeWorkbookMenuItem");
        var _commaStyleMenuItem = access.GetNativeMenuItem("_commaStyleMenuItem");
        var _copyMenuItem = access.GetNativeMenuItem("_copyMenuItem");
        var _currencyFormatMenuItem = access.GetNativeMenuItem("_currencyFormatMenuItem");
        var _cutMenuItem = access.GetNativeMenuItem("_cutMenuItem");
        var _dataValidationMenuItem = access.GetNativeMenuItem("_dataValidationMenuItem");
        var _dataValidationPreviewMenuItem = access.GetNativeMenuItem("_dataValidationPreviewMenuItem");
        var _decreaseDecimalMenuItem = access.GetNativeMenuItem("_decreaseDecimalMenuItem");
        var _decreaseFontSizeMenuItem = access.GetNativeMenuItem("_decreaseFontSizeMenuItem");
        var _decreaseIndentMenuItem = access.GetNativeMenuItem("_decreaseIndentMenuItem");
        var _deleteSheetMenuItem = access.GetNativeMenuItem("_deleteSheetMenuItem");
        var _doubleUnderlineMenuItem = access.GetNativeMenuItem("_doubleUnderlineMenuItem");
        var _duplicateSheetMenuItem = access.GetNativeMenuItem("_duplicateSheetMenuItem");
        var _exportPdfMenuItem = access.GetNativeMenuItem("_exportPdfMenuItem");
        var _fillCellsButton = access.GetControl<DropDownButton>("_fillCellsButton");
        var _fillCellsMenuItem = access.GetNativeMenuItem("_fillCellsMenuItem");
        var _fillColorMenuItem = access.GetNativeMenuItem("_fillColorMenuItem");
        var _fillDownFlyoutItem = access.GetControl<MenuItem>("_fillDownFlyoutItem");
        var _fillLeftFlyoutItem = access.GetControl<MenuItem>("_fillLeftFlyoutItem");
        var _fillRightFlyoutItem = access.GetControl<MenuItem>("_fillRightFlyoutItem");
        var _fillUpFlyoutItem = access.GetControl<MenuItem>("_fillUpFlyoutItem");
        var _findMenuItem = access.GetNativeMenuItem("_findMenuItem");
        var _findNextMenuItem = access.GetNativeMenuItem("_findNextMenuItem");
        var _flashFillMenuItem = access.GetNativeMenuItem("_flashFillMenuItem");
        var _fontColorMenuItem = access.GetNativeMenuItem("_fontColorMenuItem");
        var _forecastSheetMenuItem = access.GetNativeMenuItem("_forecastSheetMenuItem");
        var _formatCellsMenuItem = access.GetNativeMenuItem("_formatCellsMenuItem");
        var _formatPainterButton = access.GetControl<Button>("_formatPainterButton");
        var _formatPainterMenuItem = access.GetNativeMenuItem("_formatPainterMenuItem");
        var _formulaBox = access.GetControl<TextBox>("_formulaBox");
        var _freezeFirstColumnMenuItem = access.GetNativeMenuItem("_freezeFirstColumnMenuItem");
        var _freezePanesMenuItem = access.GetNativeMenuItem("_freezePanesMenuItem");
        var _freezeTopRowMenuItem = access.GetNativeMenuItem("_freezeTopRowMenuItem");
        var _goToMenuItem = access.GetNativeMenuItem("_goToMenuItem");
        var _goToSpecialMenuItem = access.GetNativeMenuItem("_goToSpecialMenuItem");
        var _helpOnlineMenuItem = access.GetNativeMenuItem("_helpOnlineMenuItem");
        var _hideSheetMenuItem = access.GetNativeMenuItem("_hideSheetMenuItem");
        var _horizontalTextMenuItem = access.GetNativeMenuItem("_horizontalTextMenuItem");
        var _increaseDecimalMenuItem = access.GetNativeMenuItem("_increaseDecimalMenuItem");
        var _increaseFontSizeMenuItem = access.GetNativeMenuItem("_increaseFontSizeMenuItem");
        var _increaseIndentMenuItem = access.GetNativeMenuItem("_increaseIndentMenuItem");
        var _italicMenuItem = access.GetNativeMenuItem("_italicMenuItem");
        var _legalNoticesMenuItem = access.GetNativeMenuItem("_legalNoticesMenuItem");
        var _mergeAndCenterButton = access.GetControl<Button>("_mergeAndCenterButton");
        var _mergeAndCenterMenuItem = access.GetNativeMenuItem("_mergeAndCenterMenuItem");
        var _minimizeWindowMenuItem = access.GetNativeMenuItem("_minimizeWindowMenuItem");
        var _moveSheetLeftMenuItem = access.GetNativeMenuItem("_moveSheetLeftMenuItem");
        var _moveSheetRightMenuItem = access.GetNativeMenuItem("_moveSheetRightMenuItem");
        var _newSheetButton = access.GetControl<Button>("_newSheetButton");
        var _newSheetMenuItem = access.GetNativeMenuItem("_newSheetMenuItem");
        var _newWorkbookMenuItem = access.GetNativeMenuItem("_newWorkbookMenuItem");
        var _nextCommentMenuItem = access.GetNativeMenuItem("_nextCommentMenuItem");
        var _nextNoteMenuItem = access.GetNativeMenuItem("_nextNoteMenuItem");
        var _openMenuItem = access.GetNativeMenuItem("_openMenuItem");
        var _openRecentMenuItem = access.GetNativeMenuItem("_openRecentMenuItem");
        var _pasteMenuItem = access.GetNativeMenuItem("_pasteMenuItem");
        var _pasteSpecialMenuItem = access.GetNativeMenuItem("_pasteSpecialMenuItem");
        var _percentFormatMenuItem = access.GetNativeMenuItem("_percentFormatMenuItem");
        var _previousCommentMenuItem = access.GetNativeMenuItem("_previousCommentMenuItem");
        var _previousNoteMenuItem = access.GetNativeMenuItem("_previousNoteMenuItem");
        var _quitMenuItem = access.GetNativeMenuItem("_quitMenuItem");
        var _redoMenuItem = access.GetNativeMenuItem("_redoMenuItem");
        var _removeDuplicatesMenuItem = access.GetNativeMenuItem("_removeDuplicatesMenuItem");
        var _renameSheetMenuItem = access.GetNativeMenuItem("_renameSheetMenuItem");
        var _replaceMenuItem = access.GetNativeMenuItem("_replaceMenuItem");
        var _reviewSummaryMenuItem = access.GetNativeMenuItem("_reviewSummaryMenuItem");
        var _rotateTextDownMenuItem = access.GetNativeMenuItem("_rotateTextDownMenuItem");
        var _rotateTextUpMenuItem = access.GetNativeMenuItem("_rotateTextUpMenuItem");
        var _saveAsMenuItem = access.GetNativeMenuItem("_saveAsMenuItem");
        var _saveMenuItem = access.GetNativeMenuItem("_saveMenuItem");
        var _selectAllMenuItem = access.GetNativeMenuItem("_selectAllMenuItem");
        var _selectAllSheetsMenuItem = access.GetNativeMenuItem("_selectAllSheetsMenuItem");
        var _selectionStatsText = access.GetControl<TextBlock>("_selectionStatsText");
        var _sendFeedbackMenuItem = access.GetNativeMenuItem("_sendFeedbackMenuItem");
        var _shareWorkbookMenuItem = access.GetNativeMenuItem("_shareWorkbookMenuItem");
        var _sheetGridHost = access.GetControl<ContentControl>("_sheetGridHost");
        var _showFormulasMenuItem = access.GetNativeMenuItem("_showFormulasMenuItem");
        var _showGridlinesMenuItem = access.GetNativeMenuItem("_showGridlinesMenuItem");
        var _showHeadingsMenuItem = access.GetNativeMenuItem("_showHeadingsMenuItem");
        var _sortAscendingMenuItem = access.GetNativeMenuItem("_sortAscendingMenuItem");
        var _sortDescendingMenuItem = access.GetNativeMenuItem("_sortDescendingMenuItem");
        var _statusText = access.GetControl<TextBlock>("_statusText");
        var _strikethroughMenuItem = access.GetNativeMenuItem("_strikethroughMenuItem");
        var _subtotalMenuItem = access.GetNativeMenuItem("_subtotalMenuItem");
        var _tabColorMenuItem = access.GetNativeMenuItem("_tabColorMenuItem");
        var _underlineMenuItem = access.GetNativeMenuItem("_underlineMenuItem");
        var _undoMenuItem = access.GetNativeMenuItem("_undoMenuItem");
        var _unfreezePanesMenuItem = access.GetNativeMenuItem("_unfreezePanesMenuItem");
        var _ungroupSheetsMenuItem = access.GetNativeMenuItem("_ungroupSheetsMenuItem");
        var _unhideSheetMenuItem = access.GetNativeMenuItem("_unhideSheetMenuItem");
        var _unmergeCellsMenuItem = access.GetNativeMenuItem("_unmergeCellsMenuItem");
        var _verticalTextMenuItem = access.GetNativeMenuItem("_verticalTextMenuItem");
        var _whatIfAnalysisMenuItem = access.GetNativeMenuItem("_whatIfAnalysisMenuItem");
        var _workbookStatisticsMenuItem = access.GetNativeMenuItem("_workbookStatisticsMenuItem");
        var _wrapTextButton = access.GetControl<ToggleButton>("_wrapTextButton");
        var _wrapTextMenuItem = access.GetNativeMenuItem("_wrapTextMenuItem");
        var _zoom100MenuItem = access.GetNativeMenuItem("_zoom100MenuItem");
        var _zoomInMenuItem = access.GetNativeMenuItem("_zoomInMenuItem");
        var _zoomOutMenuItem = access.GetNativeMenuItem("_zoomOutMenuItem");
        var _zoomText = access.GetControl<TextBlock>("_zoomText");
        var _zoomToSelectionMenuItem = access.GetNativeMenuItem("_zoomToSelectionMenuItem");
        var _zoomWindowMenuItem = access.GetNativeMenuItem("_zoomWindowMenuItem");

        var nativeTopLevelMenuOrder = GetNativeTopLevelMenuOrder(nativeMenu);
        var nativeDockMenu = access.NativeDockMenu;
        var nativeDockTopLevelMenuOrder = GetNativeTopLevelMenuOrder(nativeDockMenu);
        var hasNativeDockMenu = nativeDockMenu is not null;
        var hasNativeDockFileMenu = HasNativeTopLevelMenu(nativeDockMenu, NativeMenuTopLevelId.File);
        var nativeDockFileMenuItemCount = CountNativeTopLevelMenuItems(nativeDockMenu, NativeMenuTopLevelId.File);
        var hasNativeFileMenu = HasNativeTopLevelMenu(nativeMenu, NativeMenuTopLevelId.File);
        var hasNativeHomeMenu = HasNativeTopLevelMenu(nativeMenu, NativeMenuTopLevelId.Home);
        var hasNativeInsertMenu = HasNativeTopLevelMenu(nativeMenu, NativeMenuTopLevelId.Insert);
        var hasNativePageLayoutMenu = HasNativeTopLevelMenu(nativeMenu, NativeMenuTopLevelId.PageLayout);
        var hasNativeFormulasMenu = HasNativeTopLevelMenu(nativeMenu, NativeMenuTopLevelId.Formulas);
        var hasNativeDataMenu = HasNativeTopLevelMenu(nativeMenu, NativeMenuTopLevelId.Data);
        var hasNativeReviewMenu = HasNativeTopLevelMenu(nativeMenu, NativeMenuTopLevelId.Review);
        var hasNativeViewMenu = HasNativeTopLevelMenu(nativeMenu, NativeMenuTopLevelId.View);
        var hasNativeSheetMenu = HasNativeTopLevelMenu(nativeMenu, NativeMenuTopLevelId.Sheet);
        var hasNativeWindowMenu = HasNativeTopLevelMenu(nativeMenu, NativeMenuTopLevelId.Window);
        var hasNativeHelpMenu = HasNativeTopLevelMenu(nativeMenu, NativeMenuTopLevelId.Help);
        var nativeCellStylesPresetCount = _cellStylesMenuItem.Menu?
            .Items
            .OfType<NativeMenuItem>()
            .Count(item => item.Header is not null) ?? 0;
        var nativeOpenRecentItemCount = CountNativeOpenRecentItems(_openRecentMenuItem.Menu);
        var nativeFillColorSwatchCount = CountNativeColorPaletteSwatches(_fillColorMenuItem.Menu);
        var nativeFontColorSwatchCount = CountNativeColorPaletteSwatches(_fontColorMenuItem.Menu);
        var nativeBordersPresetCount = _bordersMenuItem.Menu?
            .Items
            .OfType<NativeMenuItem>()
            .Count(item => item.Header is not null) ?? 0;
        var nativeTabColorSwatchCount = CountNativeColorPaletteSwatches(_tabColorMenuItem.Menu);

        return new MacOsLaunchSmokeSnapshot(
            WindowShown: shell.WindowShown,
            WindowTitle: shell.WindowTitle,
            DisplayName: shell.DisplayName,
            ActiveSheetName: shell.ActiveSheetName,
            SheetTabCount: shell.SheetTabCount,
            ViewportRowCount: shell.ViewportRowCount,
            ViewportColumnCount: shell.ViewportColumnCount,
            ExternalImageClipboardPictureCount: shell.ExternalImageClipboardPictureCount,
            ExternalImageClipboardPicturePngByteCount: shell.ExternalImageClipboardPicturePngByteCount,
            DialogEvidence: dialogEvidence,
            OpenedSourcePath: shell.OpenedSourcePath,
            IsOpening: shell.IsOpening,
            HasNewSheetButton: _newSheetButton.Content?.ToString() == "+",
            HasFormatPainterButton: _formatPainterButton.Content?.ToString() == UiText.Get("MainWindow_TooltipTitle_FormatPainter") &&
                string.Equals(AutomationProperties.GetAutomationId(_formatPainterButton), "HomeFormatPainterButton", StringComparison.Ordinal) &&
                string.Equals(
                    AutomationProperties.GetHelpText(_formatPainterButton),
                    UiText.Get("MainWindow_TooltipDescription_CopyFormattingFromOnePlaceAndApplyItToAnother"),
                    StringComparison.Ordinal),
            HasAutoSumButton: _autoSumButton.Content?.ToString() == "AutoSum" &&
                _autoSumButton.Flyout is MenuFlyout &&
                string.Equals(AutomationProperties.GetAutomationId(_autoSumButton), "HomeAutoSumButton", StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetHelpText(_autoSumButton), "Insert a formula using nearby numeric cells.", StringComparison.Ordinal),
            HasAutoSumSumMenuItem: HasToolbarMenuItem(_autoSumSumFlyoutItem, "Sum"),
            HasAutoSumAverageMenuItem: HasToolbarMenuItem(_autoSumAverageFlyoutItem, "Average"),
            HasAutoSumCountNumbersMenuItem: HasToolbarMenuItem(_autoSumCountNumbersFlyoutItem, "Count Numbers"),
            HasAutoSumCountAllMenuItem: HasToolbarMenuItem(_autoSumCountAllFlyoutItem, "Count All"),
            HasAutoSumMaxMenuItem: HasToolbarMenuItem(_autoSumMaxFlyoutItem, "Max"),
            HasAutoSumMinMenuItem: HasToolbarMenuItem(_autoSumMinFlyoutItem, "Min"),
            HasFillCellsButton: _fillCellsButton.Content?.ToString() == "Fill Cells" &&
                _fillCellsButton.Flyout is MenuFlyout &&
                string.Equals(AutomationProperties.GetAutomationId(_fillCellsButton), "HomeFillCellsButton", StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetHelpText(_fillCellsButton), "Copy the edge cells across the selected range.", StringComparison.Ordinal),
            HasFillDownMenuItem: HasToolbarMenuItem(_fillDownFlyoutItem, "Down"),
            HasFillRightMenuItem: HasToolbarMenuItem(_fillRightFlyoutItem, "Right"),
            HasFillUpMenuItem: HasToolbarMenuItem(_fillUpFlyoutItem, "Up"),
            HasFillLeftMenuItem: HasToolbarMenuItem(_fillLeftFlyoutItem, "Left"),
            HasClearButton: _clearButton.Content?.ToString() == "Clear" &&
                _clearButton.Flyout is MenuFlyout &&
                string.Equals(AutomationProperties.GetAutomationId(_clearButton), "HomeClearButton", StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetHelpText(_clearButton), "Clear contents, formatting, comments, hyperlinks, or all cell state from the selected range.", StringComparison.Ordinal),
            HasClearAllMenuItem: HasToolbarMenuItem(_clearAllFlyoutItem, "Clear All"),
            HasClearFormatsMenuItem: HasToolbarMenuItem(_clearFormatsFlyoutItem, "Clear Formats"),
            HasClearContentsMenuItem: HasToolbarMenuItem(_clearContentsFlyoutItem, "Clear Contents"),
            HasClearCommentsMenuItem: HasToolbarMenuItem(_clearCommentsFlyoutItem, "Clear Comments and Notes"),
            HasClearHyperlinksMenuItem: HasToolbarMenuItem(_clearHyperlinksFlyoutItem, "Clear Hyperlinks"),
            HasBordersButton: _bordersButton.Content?.ToString() == "Borders" &&
                string.Equals(AutomationProperties.GetAutomationId(_bordersButton), "HomeBordersButton", StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetHelpText(_bordersButton), "Apply or change borders on the selected cells.", StringComparison.Ordinal),
            HasWrapTextButton: _wrapTextButton.Content?.ToString() == "Wrap" &&
                string.Equals(AutomationProperties.GetAutomationId(_wrapTextButton), "HomeWrapTextButton", StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetHelpText(_wrapTextButton), "Wrap text within the selected cells.", StringComparison.Ordinal),
            HasMergeAndCenterButton: _mergeAndCenterButton.Content?.ToString() == "Merge & Center" &&
                string.Equals(AutomationProperties.GetAutomationId(_mergeAndCenterButton), "HomeMergeAndCenterButton", StringComparison.Ordinal) &&
                string.Equals(AutomationProperties.GetHelpText(_mergeAndCenterButton), "Merge and center the selected cells.", StringComparison.Ordinal),
            HasFormulaBoxAutomationName: string.Equals(
                AutomationProperties.GetName(_formulaBox),
                FormulaBarText(FormulaBarChromePlanner.FormulaBox.AutomationNameResourceKey),
                StringComparison.Ordinal),
            HasFormulaBoxAutomationHelp: string.Equals(
                AutomationProperties.GetHelpText(_formulaBox),
                FormulaBarText(FormulaBarChromePlanner.FormulaBox.HelpTextResourceKey),
                StringComparison.Ordinal),
            HasFormulaBoxAutomationId: string.Equals(AutomationProperties.GetAutomationId(_formulaBox), "FormulaBox", StringComparison.Ordinal),
            HasStatusTextAutomationName: string.Equals(AutomationProperties.GetName(_statusText), "Status", StringComparison.Ordinal),
            HasStatusTextAutomationHelp: string.Equals(AutomationProperties.GetHelpText(_statusText), "Shows the current workbook status.", StringComparison.Ordinal),
            HasStatusTextAutomationId: string.Equals(AutomationProperties.GetAutomationId(_statusText), "StatusText", StringComparison.Ordinal),
            HasStatusTextValue: HasStatusBarAccessibleValue(_statusText, _selectionStatsText),
            HasCellAddressAutomationName: string.Equals(AutomationProperties.GetName(_cellAddressText), "Cell address", StringComparison.Ordinal),
            HasCellAddressAutomationHelp: string.Equals(AutomationProperties.GetHelpText(_cellAddressText), "Shows the active cell address.", StringComparison.Ordinal),
            HasCellAddressAutomationId: string.Equals(AutomationProperties.GetAutomationId(_cellAddressText), "CellAddressText", StringComparison.Ordinal),
            HasSelectionStatsAutomationName: string.Equals(AutomationProperties.GetName(_selectionStatsText), "Selection statistics", StringComparison.Ordinal),
            HasSelectionStatsAutomationHelp: string.Equals(AutomationProperties.GetHelpText(_selectionStatsText), "Shows statistics for the current selection.", StringComparison.Ordinal),
            HasSelectionStatsAutomationId: string.Equals(AutomationProperties.GetAutomationId(_selectionStatsText), "SelectionStatsText", StringComparison.Ordinal),
            HasFocusableSheetTab: access.HasSheetTab(button => button.Focusable),
            HasFocusableActiveSheetTab: access.ActiveSheetTab?.Focusable == true,
            HasShellFocusCycleTargets: _sheetGridHost.Focusable &&
                access.ToolbarFocusTargets.Any(control => control.Focusable) &&
                _formulaBox.Focusable &&
                access.ActiveSheetTab?.Focusable == true &&
                _zoomText.Focusable,
            HasSheetTabContextKeyboardHelp: access.HasSheetTab(button =>
                string.Equals(
                    AutomationProperties.GetHelpText(button),
                    UiText.Get("SheetTabs_ContextHelpText"),
                    StringComparison.Ordinal)),
            HasSheetTabContextRenameMenuItem: access.HasSheetTabContextMenuItem(UiText.Get("MainWindow_Header_Rename")),
            HasSheetTabContextTabColorMenuItem: access.HasSheetTabContextMenuItem(UiText.Get("MainWindow_Header_TabColor")),
            HasSheetTabContextNoColorMenuItem: access.HasSheetTabContextSubmenuItem(
                UiText.Get("MainWindow_Header_TabColor"),
                UiText.Get("RibbonWire_TabColorNone")),
            HasSheetTabContextSelectAllSheetsMenuItem: access.HasSheetTabContextMenuItem(UiText.Get("MainWindow_Header_SelectAllSheets")),
            HasSheetTabContextUngroupSheetsMenuItem: access.HasSheetTabContextMenuItem(UiText.Get("MainWindow_Header_UngroupSheets")),
            NativeTopLevelMenuOrder: nativeTopLevelMenuOrder,
            NativeDockTopLevelMenuOrder: nativeDockTopLevelMenuOrder,
            HasNativeDockMenu: hasNativeDockMenu,
            HasNativeDockFileMenu: hasNativeDockFileMenu,
            NativeDockFileMenuItemCount: nativeDockFileMenuItemCount,
            HasNativeFileMenu: hasNativeFileMenu,
            HasNativeHomeMenu: hasNativeHomeMenu,
            HasNativeInsertMenu: hasNativeInsertMenu,
            HasNativePageLayoutMenu: hasNativePageLayoutMenu,
            HasNativeFormulasMenu: hasNativeFormulasMenu,
            HasNativeDataMenu: hasNativeDataMenu,
            HasNativeReviewMenu: hasNativeReviewMenu,
            HasNativeViewMenu: hasNativeViewMenu,
            HasNativeSheetMenu: hasNativeSheetMenu,
            HasNativeWindowMenu: hasNativeWindowMenu,
            HasNativeHelpMenu: hasNativeHelpMenu,
            HasNativeNewWorkbookMenuItem: HasNativeFileMenuItem(_newWorkbookMenuItem, NativeFileMenuItemId.NewWorkbook),
            HasNativeOpenMenuItem: HasNativeFileMenuItem(_openMenuItem, NativeFileMenuItemId.Open),
            HasNativeOpenRecentMenuItem: HasNativeFileMenuItem(_openRecentMenuItem, NativeFileMenuItemId.OpenRecent),
            NativeOpenRecentItemCount: nativeOpenRecentItemCount,
            HasNativeSaveMenuItem: HasNativeFileMenuItem(_saveMenuItem, NativeFileMenuItemId.Save),
            HasNativeSaveAsMenuItem: HasNativeFileMenuItem(_saveAsMenuItem, NativeFileMenuItemId.SaveAs),
            HasNativeExportPdfMenuItem: HasNativeFileMenuItem(_exportPdfMenuItem, NativeFileMenuItemId.ExportPdf),
            HasNativeShareWorkbookMenuItem: HasEnabledNativeFileMenuItem(_shareWorkbookMenuItem, NativeFileMenuItemId.ShareWorkbook),
            HasNativeWorkbookStatisticsMenuItem: HasNativeFileMenuItem(_workbookStatisticsMenuItem, NativeFileMenuItemId.WorkbookStatistics),
            HasNativeCloseWorkbookMenuItem: HasNativeFileMenuItem(_closeWorkbookMenuItem, NativeFileMenuItemId.CloseWorkbook),
            HasNativeNewSheetMenuItem: HasNativeMenuItem(_newSheetMenuItem, NativeMenuItemId.NewSheet),
            HasNativeRenameSheetMenuItem: HasNativeMenuItem(_renameSheetMenuItem, NativeMenuItemId.RenameSheet),
            HasNativeDuplicateSheetMenuItem: HasNativeMenuItem(_duplicateSheetMenuItem, NativeMenuItemId.DuplicateSheet),
            HasNativeMoveSheetLeftMenuItem: HasNativeMenuItem(_moveSheetLeftMenuItem, NativeMenuItemId.MoveSheetLeft),
            HasNativeMoveSheetRightMenuItem: HasNativeMenuItem(_moveSheetRightMenuItem, NativeMenuItemId.MoveSheetRight),
            HasNativeTabColorMenuItem: HasNativeMenuItem(_tabColorMenuItem, NativeMenuItemId.TabColor),
            HasNativeClearTabColorMenuItem: HasNativeSubmenuItem(_tabColorMenuItem.Menu, "No Color"),
            NativeTabColorSwatchCount: nativeTabColorSwatchCount,
            HasNativeSelectAllSheetsMenuItem: HasNativeMenuItem(_selectAllSheetsMenuItem, NativeMenuItemId.SelectAllSheets),
            HasNativeUngroupSheetsMenuItem: HasNativeMenuItem(_ungroupSheetsMenuItem, NativeMenuItemId.UngroupSheets),
            HasNativeHideSheetMenuItem: HasNativeMenuItem(_hideSheetMenuItem, NativeMenuItemId.HideSheet),
            HasNativeUnhideSheetMenuItem: HasNativeMenuItem(_unhideSheetMenuItem, NativeMenuItemId.UnhideSheet),
            HasNativeDeleteSheetMenuItem: HasNativeMenuItem(_deleteSheetMenuItem, NativeMenuItemId.DeleteSheet),
            HasNativeUndoMenuItem: HasNativeMenuItem(_undoMenuItem, NativeMenuItemId.Undo),
            HasNativeRedoMenuItem: HasNativeMenuItem(_redoMenuItem, NativeMenuItemId.Redo),
            HasNativeCutMenuItem: HasNativeMenuItem(_cutMenuItem, NativeMenuItemId.Cut),
            HasNativeCopyMenuItem: HasNativeMenuItem(_copyMenuItem, NativeMenuItemId.Copy),
            HasNativePasteMenuItem: HasNativeMenuItem(_pasteMenuItem, NativeMenuItemId.Paste),
            HasNativePasteSpecialMenuItem: HasNativeMenuItem(_pasteSpecialMenuItem, NativeMenuItemId.PasteSpecial),
            HasNativeFormatPainterMenuItem: HasNativeMenuItem(_formatPainterMenuItem, NativeMenuItemId.FormatPainter),
            HasNativePasteSpecialCommentsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Comments and Notes"),
            HasNativePasteSpecialValidationMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Validation"),
            HasNativePasteSpecialAllExceptBordersMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "All Except Borders"),
            HasNativePasteSpecialAllMergingConditionalFormatsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "All Merging Conditional Formats"),
            HasNativePasteSpecialColumnWidthsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Column Widths"),
            HasNativePasteSpecialFormulasAndNumberFormatsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Formulas and Number Formats"),
            HasNativePasteSpecialValuesAndNumberFormatsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Values and Number Formats"),
            HasNativePasteSpecialValuesAndSourceFormattingMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Values and Source Formatting"),
            HasNativePasteSpecialKeepSourceColumnWidthsMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Keep Source Column Widths"),
            HasNativePasteSpecialPasteLinkMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Paste Link"),
            HasNativePasteSpecialTextMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Text"),
            HasNativePasteSpecialUnicodeTextMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Unicode Text"),
            HasNativePasteSpecialPictureMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Picture"),
            HasNativePasteSpecialLinkedPictureMenuItem: HasNativeSubmenuItem(_pasteSpecialMenuItem.Menu, "Linked Picture"),
            HasNativeSelectAllMenuItem: HasNativeMenuItem(_selectAllMenuItem, NativeMenuItemId.SelectAll),
            HasNativeFindMenuItem: HasNativeMenuItem(_findMenuItem, NativeMenuItemId.Find),
            HasNativeFindNextMenuItem: HasNativeMenuItem(_findNextMenuItem, NativeMenuItemId.FindNext),
            HasNativeReplaceMenuItem: HasNativeMenuItem(_replaceMenuItem, NativeMenuItemId.Replace),
            HasNativeGoToMenuItem: HasNativeMenuItem(_goToMenuItem, NativeMenuItemId.GoTo),
            HasNativeGoToSpecialMenuItem: HasNativeMenuItem(_goToSpecialMenuItem, NativeMenuItemId.GoToSpecial),
            HasNativeSortAscendingMenuItem: HasNativeMenuItem(_sortAscendingMenuItem, NativeMenuItemId.SortAscending),
            HasNativeSortDescendingMenuItem: HasNativeMenuItem(_sortDescendingMenuItem, NativeMenuItemId.SortDescending),
            HasNativeFlashFillMenuItem: HasNativeMenuItem(_flashFillMenuItem, NativeMenuItemId.FlashFill),
            HasNativeAdvancedFilterMenuItem: HasNativeMenuItem(_advancedFilterMenuItem, NativeMenuItemId.AdvancedFilter),
            HasNativeRemoveDuplicatesMenuItem: HasNativeMenuItem(_removeDuplicatesMenuItem, NativeMenuItemId.RemoveDuplicates),
            HasNativeSubtotalMenuItem: HasNativeMenuItem(_subtotalMenuItem, NativeMenuItemId.Subtotal),
            HasNativeDataValidationPreviewMenuItem: HasNativeMenuItem(_dataValidationPreviewMenuItem, NativeMenuItemId.DataValidationPreview),
            HasNativeDataValidationMenuItem: HasNativeMenuItem(_dataValidationMenuItem, NativeMenuItemId.DataValidation),
            HasNativeWhatIfAnalysisMenuItem: HasNativeMenuItem(_whatIfAnalysisMenuItem, NativeMenuItemId.WhatIfAnalysis),
            HasNativeGoalSeekMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, NativeMenuItemId.GoalSeek),
            HasNativeDataTableMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, NativeMenuItemId.DataTable),
            HasNativeScenarioManagerMenuItem: HasNativeSubmenuItem(_whatIfAnalysisMenuItem.Menu, NativeMenuItemId.ScenarioManager),
            HasNativeForecastSheetMenuItem: HasNativeMenuItem(_forecastSheetMenuItem, NativeMenuItemId.ForecastSheet),
            HasNativeReviewSummaryMenuItem: HasNativeMenuItem(_reviewSummaryMenuItem, NativeMenuItemId.ReviewSummary),
            HasNativeCheckAccessibilityMenuItem: HasNativeMenuItem(_checkAccessibilityMenuItem, NativeMenuItemId.CheckAccessibility),
            HasNativeNextNoteMenuItem: HasNativeMenuItem(_nextNoteMenuItem, NativeMenuItemId.NextNote),
            HasNativePreviousNoteMenuItem: HasNativeMenuItem(_previousNoteMenuItem, NativeMenuItemId.PreviousNote),
            HasNativeNextCommentMenuItem: HasNativeMenuItem(_nextCommentMenuItem, NativeMenuItemId.NextComment),
            HasNativePreviousCommentMenuItem: HasNativeMenuItem(_previousCommentMenuItem, NativeMenuItemId.PreviousComment),
            HasNativeFormatCellsMenuItem: HasNativeMenuItem(_formatCellsMenuItem, NativeMenuItemId.FormatCells),
            HasNativeAutoSumMenuItem: HasNativeMenuItem(_autoSumMenuItem, NativeMenuItemId.AutoSum),
            HasNativeAutoSumSumMenuItem: HasNativeSubmenuItem(_autoSumMenuItem.Menu, NativeMenuItemId.AutoSumSum),
            HasNativeAutoSumAverageMenuItem: HasNativeSubmenuItem(_autoSumMenuItem.Menu, NativeMenuItemId.AutoSumAverage),
            HasNativeAutoSumCountNumbersMenuItem: HasNativeSubmenuItem(_autoSumMenuItem.Menu, NativeMenuItemId.AutoSumCountNumbers),
            HasNativeAutoSumCountAllMenuItem: HasNativeSubmenuItem(_autoSumMenuItem.Menu, NativeMenuItemId.AutoSumCountAll),
            HasNativeAutoSumMaxMenuItem: HasNativeSubmenuItem(_autoSumMenuItem.Menu, NativeMenuItemId.AutoSumMax),
            HasNativeAutoSumMinMenuItem: HasNativeSubmenuItem(_autoSumMenuItem.Menu, NativeMenuItemId.AutoSumMin),
            HasNativeFillCellsMenuItem: HasNativeMenuItem(_fillCellsMenuItem, NativeMenuItemId.FillCells),
            HasNativeFillDownMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, NativeMenuItemId.FillDown),
            HasNativeFillRightMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, NativeMenuItemId.FillRight),
            HasNativeFillUpMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, NativeMenuItemId.FillUp),
            HasNativeFillLeftMenuItem: HasNativeSubmenuItem(_fillCellsMenuItem.Menu, NativeMenuItemId.FillLeft),
            HasNativeClearMenuItem: HasNativeMenuItem(_clearMenuItem, NativeMenuItemId.Clear),
            HasNativeClearAllMenuItem: HasNativeSubmenuItem(_clearMenuItem.Menu, NativeMenuItemId.ClearAll),
            HasNativeClearFormatsMenuItem: HasNativeSubmenuItem(_clearMenuItem.Menu, NativeMenuItemId.ClearFormats),
            HasNativeClearContentsMenuItem: HasNativeSubmenuItem(_clearMenuItem.Menu, NativeMenuItemId.ClearContents),
            HasNativeClearCommentsMenuItem: HasNativeSubmenuItem(_clearMenuItem.Menu, NativeMenuItemId.ClearComments),
            HasNativeClearHyperlinksMenuItem: HasNativeSubmenuItem(_clearMenuItem.Menu, NativeMenuItemId.ClearHyperlinks),
            HasNativeBoldMenuItem: HasNativeMenuItem(_boldMenuItem, NativeMenuItemId.Bold),
            HasNativeItalicMenuItem: HasNativeMenuItem(_italicMenuItem, NativeMenuItemId.Italic),
            HasNativeUnderlineMenuItem: HasNativeMenuItem(_underlineMenuItem, NativeMenuItemId.Underline),
            HasNativeDoubleUnderlineMenuItem: HasNativeMenuItem(_doubleUnderlineMenuItem, NativeMenuItemId.DoubleUnderline),
            HasNativeStrikethroughMenuItem: HasNativeMenuItem(_strikethroughMenuItem, NativeMenuItemId.Strikethrough),
            HasNativeIncreaseFontSizeMenuItem: HasNativeMenuItem(_increaseFontSizeMenuItem, NativeMenuItemId.IncreaseFontSize),
            HasNativeDecreaseFontSizeMenuItem: HasNativeMenuItem(_decreaseFontSizeMenuItem, NativeMenuItemId.DecreaseFontSize),
            HasNativeFillColorMenuItem: HasNativeMenuItem(_fillColorMenuItem, NativeMenuItemId.FillColor),
            HasNativeClearFillMenuItem: HasNativeMenuItem(_clearFillMenuItem, NativeMenuItemId.ClearFill),
            HasNativeFontColorMenuItem: HasNativeMenuItem(_fontColorMenuItem, NativeMenuItemId.FontColor),
            NativeFillColorSwatchCount: nativeFillColorSwatchCount,
            NativeFontColorSwatchCount: nativeFontColorSwatchCount,
            HasNativeBordersMenuItem: HasNativeMenuItem(_bordersMenuItem, NativeMenuItemId.Borders),
            NativeBordersPresetCount: nativeBordersPresetCount,
            HasNativeCellStylesMenuItem: HasNativeMenuItem(_cellStylesMenuItem, NativeMenuItemId.CellStyles),
            NativeCellStylesPresetCount: nativeCellStylesPresetCount,
            HasNativeHorizontalTextMenuItem: HasNativeMenuItem(_horizontalTextMenuItem, NativeMenuItemId.HorizontalText),
            HasNativeAngleCounterclockwiseMenuItem: HasNativeMenuItem(_angleCounterclockwiseMenuItem, NativeMenuItemId.AngleCounterclockwise),
            HasNativeAngleClockwiseMenuItem: HasNativeMenuItem(_angleClockwiseMenuItem, NativeMenuItemId.AngleClockwise),
            HasNativeVerticalTextMenuItem: HasNativeMenuItem(_verticalTextMenuItem, NativeMenuItemId.VerticalText),
            HasNativeRotateTextUpMenuItem: HasNativeMenuItem(_rotateTextUpMenuItem, NativeMenuItemId.RotateTextUp),
            HasNativeRotateTextDownMenuItem: HasNativeMenuItem(_rotateTextDownMenuItem, NativeMenuItemId.RotateTextDown),
            HasNativeCurrencyFormatMenuItem: HasNativeMenuItem(_currencyFormatMenuItem, NativeMenuItemId.CurrencyFormat),
            HasNativePercentFormatMenuItem: HasNativeMenuItem(_percentFormatMenuItem, NativeMenuItemId.PercentFormat),
            HasNativeCommaStyleMenuItem: HasNativeMenuItem(_commaStyleMenuItem, NativeMenuItemId.CommaStyle),
            HasNativeIncreaseDecimalMenuItem: HasNativeMenuItem(_increaseDecimalMenuItem, NativeMenuItemId.IncreaseDecimal),
            HasNativeDecreaseDecimalMenuItem: HasNativeMenuItem(_decreaseDecimalMenuItem, NativeMenuItemId.DecreaseDecimal),
            HasNativeAlignTopMenuItem: HasNativeMenuItem(_alignTopMenuItem, NativeMenuItemId.AlignTop),
            HasNativeAlignMiddleMenuItem: HasNativeMenuItem(_alignMiddleMenuItem, NativeMenuItemId.AlignMiddle),
            HasNativeAlignBottomMenuItem: HasNativeMenuItem(_alignBottomMenuItem, NativeMenuItemId.AlignBottom),
            HasNativeWrapTextMenuItem: HasNativeMenuItem(_wrapTextMenuItem, NativeMenuItemId.WrapText),
            HasNativeMergeAndCenterMenuItem: HasNativeMenuItem(_mergeAndCenterMenuItem, NativeMenuItemId.MergeAndCenter),
            HasNativeUnmergeCellsMenuItem: HasNativeMenuItem(_unmergeCellsMenuItem, NativeMenuItemId.UnmergeCells),
            HasNativeShowGridlinesMenuItem: HasNativeMenuItem(_showGridlinesMenuItem, NativeMenuItemId.ShowGridlines),
            HasNativeShowHeadingsMenuItem: HasNativeMenuItem(_showHeadingsMenuItem, NativeMenuItemId.ShowHeadings),
            HasNativeZoomInMenuItem: HasNativeMenuItem(_zoomInMenuItem, NativeMenuItemId.ZoomIn),
            HasNativeZoomOutMenuItem: HasNativeMenuItem(_zoomOutMenuItem, NativeMenuItemId.ZoomOut),
            HasNativeZoom100MenuItem: HasNativeMenuItem(_zoom100MenuItem, NativeMenuItemId.Zoom100),
            HasNativeZoomToSelectionMenuItem: HasNativeMenuItem(_zoomToSelectionMenuItem, NativeMenuItemId.ZoomToSelection),
            HasNativeFreezePanesMenuItem: HasNativeMenuItem(_freezePanesMenuItem, NativeMenuItemId.FreezePanes),
            HasNativeFreezeTopRowMenuItem: HasNativeMenuItem(_freezeTopRowMenuItem, NativeMenuItemId.FreezeTopRow),
            HasNativeFreezeFirstColumnMenuItem: HasNativeMenuItem(_freezeFirstColumnMenuItem, NativeMenuItemId.FreezeFirstColumn),
            HasNativeUnfreezePanesMenuItem: HasNativeMenuItem(_unfreezePanesMenuItem, NativeMenuItemId.UnfreezePanes),
            HasNativeDecreaseIndentMenuItem: HasNativeMenuItem(_decreaseIndentMenuItem, NativeMenuItemId.DecreaseIndent),
            HasNativeIncreaseIndentMenuItem: HasNativeMenuItem(_increaseIndentMenuItem, NativeMenuItemId.IncreaseIndent),
            HasNativeAlignLeftMenuItem: HasNativeMenuItem(_alignLeftMenuItem, NativeMenuItemId.AlignLeft),
            HasNativeAlignCenterMenuItem: HasNativeMenuItem(_alignCenterMenuItem, NativeMenuItemId.AlignCenter),
            HasNativeAlignRightMenuItem: HasNativeMenuItem(_alignRightMenuItem, NativeMenuItemId.AlignRight),
            HasNativeShowFormulasMenuItem: HasNativeMenuItem(_showFormulasMenuItem, NativeMenuItemId.ShowFormulas),
            HasNativeMinimizeWindowMenuItem: HasNativeMenuItem(_minimizeWindowMenuItem, NativeMenuItemId.MinimizeWindow),
            HasNativeZoomWindowMenuItem: HasNativeMenuItem(_zoomWindowMenuItem, NativeMenuItemId.ZoomWindow),
            HasNativeBringAllToFrontMenuItem: HasNativeMenuItem(_bringAllToFrontMenuItem, NativeMenuItemId.BringAllToFront),
            HasNativeHelpOnlineMenuItem: HasNativeMenuItem(_helpOnlineMenuItem, NativeMenuItemId.HelpOnline),
            HasNativeSendFeedbackMenuItem: HasNativeMenuItem(_sendFeedbackMenuItem, NativeMenuItemId.SendFeedback),
            HasNativeCheckForUpdatesMenuItem: HasNativeMenuItem(_checkForUpdatesMenuItem, NativeMenuItemId.CheckForUpdates),
            HasNativeAboutMenuItem: HasNativeMenuItem(_aboutMenuItem, NativeMenuItemId.About),
            HasNativeLegalNoticesMenuItem: HasNativeMenuItem(_legalNoticesMenuItem, NativeMenuItemId.LegalNotices),
            HasNativeQuitMenuItem: HasNativeFileMenuItem(_quitMenuItem, NativeFileMenuItemId.Quit));
    }


    private const int MaxWaitMilliseconds = 15000;
    private const int LiveCommandKeyWaitMilliseconds = 30000;
    private const int PollDelayMilliseconds = 250;

    public static void Start(
        MainWindow.RendererValidationAccess access,
        MacOsLaunchSmokeOptions options,
        LocalAppDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(options);

        access.StartWhenOpened(() => RunAsync(access, options, diagnostics));
    }

    private static async Task RunAsync(
        MainWindow.RendererValidationAccess access,
        MacOsLaunchSmokeOptions options,
        LocalAppDiagnostics? diagnostics)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(
            MaxWaitMilliseconds + (options.VerifyLiveCommandKeys ? LiveCommandKeyWaitMilliseconds : 0));
        var dialogEvidence = MacOsLaunchSmokeDialogSnapshot.Empty;
        var snapshot = CaptureSnapshot(access, dialogEvidence);
        var commandKeyEvidence = MacOsLaunchSmokeCommandKeySnapshot.Empty;
        var liveCommandKeyEvidence = MacOsLaunchSmokeLiveCommandKeySnapshot.Empty;
        var initialExternalImageClipboardPictureCount = snapshot.ExternalImageClipboardPictureCount;
        var attemptedCommandKeyEvidence = false;
        var attemptedImageClipboardPaste = false;
        var attemptedDialogEvidence = false;
        diagnostics?.RecordEvent("macos_launch_smoke", new Dictionary<string, string?>
        {
            ["source"] = "macos_launch_smoke",
            ["scope"] = "launch",
            ["status"] = "starting"
        });
        try
        {
            while (!IsPassedWithCommandKeyEvidence(
                    snapshot,
                    options,
                    initialExternalImageClipboardPictureCount,
                    commandKeyEvidence,
                    liveCommandKeyEvidence) &&
                DateTimeOffset.UtcNow < deadline)
            {
                if (snapshot.HasShellEvidence &&
                    !commandKeyEvidence.IsPassed &&
                    !attemptedCommandKeyEvidence)
                {
                    attemptedCommandKeyEvidence = true;
                    commandKeyEvidence = CaptureCommandKeyEvidence(access);
                    continue;
                }

                if (snapshot.HasShellEvidence &&
                    !snapshot.DialogEvidence.IsPassed &&
                    !attemptedDialogEvidence)
                {
                    attemptedDialogEvidence = true;
                    dialogEvidence = await CaptureDialogEvidenceAsync(access);
                    snapshot = CaptureSnapshot(access, dialogEvidence);
                    continue;
                }

                if (snapshot.HasShellEvidence &&
                    snapshot.DialogEvidence.IsPassed &&
                    options.VerifyImageClipboardPaste &&
                    !attemptedImageClipboardPaste)
                {
                    attemptedImageClipboardPaste = true;
                    await access.TryPasteExternalClipboardImageAsync();
                    snapshot = CaptureSnapshot(access, dialogEvidence);
                    continue;
                }

                if (IsReadyForLiveCommandKeys(
                        snapshot,
                        options,
                        initialExternalImageClipboardPictureCount,
                        commandKeyEvidence,
                        liveCommandKeyEvidence))
                {
                    var initialFormatting = access.BeginCommandObservation(observation =>
                        liveCommandKeyEvidence = RecordCommandObservation(liveCommandKeyEvidence, observation));
                    liveCommandKeyEvidence = MacOsLaunchSmokeLiveCommandKeySnapshot.Ready(
                        initialFormatting.Bold,
                        initialFormatting.Italic,
                        initialFormatting.Underline);
                    WriteReport(
                        options.ReportPath,
                        snapshot,
                        commandKeyEvidence,
                        liveCommandKeyEvidence,
                        options,
                        initialExternalImageClipboardPictureCount,
                        attemptedCommandKeyEvidence,
                        attemptedDialogEvidence,
                        finalReport: false);
                    continue;
                }

                await Task.Delay(PollDelayMilliseconds);
                snapshot = CaptureSnapshot(access, dialogEvidence);
            }

            WriteReport(
                options.ReportPath,
                snapshot,
                commandKeyEvidence,
                liveCommandKeyEvidence,
                options,
                initialExternalImageClipboardPictureCount,
                attemptedCommandKeyEvidence,
                attemptedDialogEvidence,
                finalReport: true);
            var isPassed = IsPassedWithCommandKeyEvidence(
                snapshot,
                options,
                initialExternalImageClipboardPictureCount,
                commandKeyEvidence,
                liveCommandKeyEvidence);
            diagnostics?.RecordEvent("macos_launch_smoke", new Dictionary<string, string?>
            {
                ["source"] = "macos_launch_smoke",
                ["scope"] = "launch",
                ["status"] = isPassed ? "passed" : "failed"
            });
            Shutdown(isPassed ? 0 : 1);
        }
        catch (Exception ex)
        {
            diagnostics?.RecordCrash(ex, "macos_launch_smoke");
            WriteFailureReport(
                options.ReportPath,
                snapshot,
                commandKeyEvidence,
                liveCommandKeyEvidence,
                options,
                initialExternalImageClipboardPictureCount,
                attemptedCommandKeyEvidence,
                attemptedDialogEvidence,
                ex);
            Shutdown(1);
        }
    }

    private static bool IsPassed(
        MacOsLaunchSmokeSnapshot snapshot,
        MacOsLaunchSmokeOptions options,
        int initialExternalImageClipboardPictureCount) =>
        snapshot.IsPassed &&
        (!options.VerifyImageClipboardPaste || HasExternalImageClipboardPasteEvidence(
            snapshot,
            initialExternalImageClipboardPictureCount));

    private static bool IsPassedWithCommandKeyEvidence(
        MacOsLaunchSmokeSnapshot snapshot,
        MacOsLaunchSmokeOptions options,
        int initialExternalImageClipboardPictureCount,
        MacOsLaunchSmokeCommandKeySnapshot commandKeyEvidence,
        MacOsLaunchSmokeLiveCommandKeySnapshot liveCommandKeyEvidence) =>
        IsPassed(snapshot, options, initialExternalImageClipboardPictureCount) &&
        commandKeyEvidence.IsPassed &&
        (!options.VerifyLiveCommandKeys || liveCommandKeyEvidence.IsPassed);

    private static bool IsReadyForLiveCommandKeys(
        MacOsLaunchSmokeSnapshot snapshot,
        MacOsLaunchSmokeOptions options,
        int initialExternalImageClipboardPictureCount,
        MacOsLaunchSmokeCommandKeySnapshot commandKeyEvidence,
        MacOsLaunchSmokeLiveCommandKeySnapshot liveCommandKeyEvidence) =>
        options.VerifyLiveCommandKeys &&
        !liveCommandKeyEvidence.IsReady &&
        IsPassed(snapshot, options, initialExternalImageClipboardPictureCount) &&
        commandKeyEvidence.IsPassed;

    private static MacOsLaunchSmokeCommandKeySnapshot CaptureCommandKeyEvidence(
        MainWindow.RendererValidationAccess access) =>
        new(
            HasNewWorkbookMenuGesture: access.HasNativeMenuItemGesture("_newWorkbookMenuItem", Key.N, KeyModifiers.Meta),
            HasOpenMenuGesture: access.HasNativeMenuItemGesture("_openMenuItem", Key.O, KeyModifiers.Meta),
            HasSaveMenuGesture: access.HasNativeMenuItemGesture("_saveMenuItem", Key.S, KeyModifiers.Meta),
            HasSaveAsMenuGesture: access.HasNativeMenuItemGesture("_saveAsMenuItem", Key.S, KeyModifiers.Meta | KeyModifiers.Shift),
            HasCloseWorkbookMenuGesture: access.HasNativeMenuItemGesture("_closeWorkbookMenuItem", Key.W, KeyModifiers.Meta),
            HasQuitMenuGesture: access.HasNativeMenuItemGesture("_quitMenuItem", Key.Q, KeyModifiers.Meta),
            HasSelectAllMenuGesture: access.HasNativeMenuItemGesture("_selectAllMenuItem", Key.A, KeyModifiers.Meta),
            HasFindMenuGesture: access.HasNativeMenuItemGesture("_findMenuItem", Key.F, KeyModifiers.Meta),
            HasBoldMenuGesture: access.HasNativeMenuItemGesture("_boldMenuItem", Key.B, KeyModifiers.Meta),
            HasItalicMenuGesture: access.HasNativeMenuItemGesture("_italicMenuItem", Key.I, KeyModifiers.Meta),
            HasUnderlineMenuGesture: access.HasNativeMenuItemGesture("_underlineMenuItem", Key.U, KeyModifiers.Meta),
            HasFindDirectRouteSourceGuard: MainWindow.RendererValidationAccess.HasMethods(
                "MainWindow_KeyDown",
                "HasOnlyCommandModifier",
                "ShowFindDialogAsync"),
            HasPageUpDirectRouteSourceGuard: MainWindow.RendererValidationAccess.HasMethods(
                "MainWindow_KeyDown",
                "HasOnlyCommandModifier",
                "SelectAdjacentVisibleSheetFromKeyboard"),
            HasPageDownDirectRouteSourceGuard: MainWindow.RendererValidationAccess.HasMethods(
                "MainWindow_KeyDown",
                "HasOnlyCommandModifier",
                "SelectAdjacentVisibleSheetFromKeyboard"));

    private static MacOsLaunchSmokeLiveCommandKeySnapshot RecordCommandObservation(
        MacOsLaunchSmokeLiveCommandKeySnapshot snapshot,
        RendererCommandObservation observation)
    {
        if (!snapshot.IsReady)
            return snapshot;

        var changed = observation.Before != observation.After;
        return observation.Command switch
        {
            RendererObservedCommand.SelectAll => snapshot with
            {
                HasSelectAllCommandKey = true,
                HasSelectAllStateChange = snapshot.HasSelectAllStateChange || changed
            },
            RendererObservedCommand.Bold => snapshot with
            {
                HasBoldCommandKey = true,
                HasBoldStateChange = snapshot.HasBoldStateChange || changed,
                CurrentBoldState = observation.After
            },
            RendererObservedCommand.Italic => snapshot with
            {
                HasItalicCommandKey = true,
                HasItalicStateChange = snapshot.HasItalicStateChange || changed,
                CurrentItalicState = observation.After
            },
            RendererObservedCommand.Underline => snapshot with
            {
                HasUnderlineCommandKey = true,
                HasUnderlineStateChange = snapshot.HasUnderlineStateChange || changed,
                CurrentUnderlineState = observation.After
            },
            _ => snapshot
        };
    }

    private static bool HasExternalImageClipboardPasteEvidence(
        MacOsLaunchSmokeSnapshot snapshot,
        int initialExternalImageClipboardPictureCount) =>
        snapshot.ExternalImageClipboardPictureCount > initialExternalImageClipboardPictureCount &&
        snapshot.ExternalImageClipboardPicturePngByteCount > 0;

    private static void WriteReport(
        string reportPath,
        MacOsLaunchSmokeSnapshot snapshot,
        MacOsLaunchSmokeCommandKeySnapshot commandKeyEvidence,
        MacOsLaunchSmokeLiveCommandKeySnapshot liveCommandKeyEvidence,
        MacOsLaunchSmokeOptions options,
        int initialExternalImageClipboardPictureCount,
        bool attemptedCommandKeyEvidence,
        bool attemptedDialogEvidence,
        bool finalReport)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var imageClipboardPasteVerified = HasExternalImageClipboardPasteEvidence(
            snapshot,
            initialExternalImageClipboardPictureCount);
        var dialogSmokeStatus = GetDialogSmokeStatus(snapshot, attemptedDialogEvidence);
        var liveCommandKeySmokeStatus = GetLiveCommandKeySmokeStatus(options, liveCommandKeyEvidence, finalReport);
        var appDiagnosticsConfigured = !string.IsNullOrWhiteSpace(options.DiagnosticsDirectory);

        File.WriteAllLines(
            reportPath,
            [
                $"macos_launch_smoke={(IsPassedWithCommandKeyEvidence(snapshot, options, initialExternalImageClipboardPictureCount, commandKeyEvidence, liveCommandKeyEvidence) ? "passed" : "failed")}",
                $"app_diagnostics_directory_configured={FormatBool(appDiagnosticsConfigured)}",
                $"window_shown={FormatBool(snapshot.WindowShown)}",
                $"window_title={snapshot.WindowTitle}",
                $"display_name={snapshot.DisplayName}",
                $"active_sheet={snapshot.ActiveSheetName}",
                $"sheet_tab_count={snapshot.SheetTabCount}",
                $"viewport_rows={snapshot.ViewportRowCount}",
                $"viewport_columns={snapshot.ViewportColumnCount}",
                $"macos_accessibility_smoke={(snapshot.HasAccessibilitySmokeEvidence ? "passed" : "failed")}",
                $"a11y_formula_box_name={FormatBool(snapshot.HasFormulaBoxAutomationName)}",
                $"a11y_formula_box_help={FormatBool(snapshot.HasFormulaBoxAutomationHelp)}",
                $"a11y_formula_box_id={FormatBool(snapshot.HasFormulaBoxAutomationId)}",
                $"a11y_status_text_name={FormatBool(snapshot.HasStatusTextAutomationName)}",
                $"a11y_status_text_help={FormatBool(snapshot.HasStatusTextAutomationHelp)}",
                $"a11y_status_text_id={FormatBool(snapshot.HasStatusTextAutomationId)}",
                $"a11y_status_text_value={FormatBool(snapshot.HasStatusTextValue)}",
                $"a11y_cell_address_name={FormatBool(snapshot.HasCellAddressAutomationName)}",
                $"a11y_cell_address_help={FormatBool(snapshot.HasCellAddressAutomationHelp)}",
                $"a11y_cell_address_id={FormatBool(snapshot.HasCellAddressAutomationId)}",
                $"a11y_selection_stats_name={FormatBool(snapshot.HasSelectionStatsAutomationName)}",
                $"a11y_selection_stats_help={FormatBool(snapshot.HasSelectionStatsAutomationHelp)}",
                $"a11y_selection_stats_id={FormatBool(snapshot.HasSelectionStatsAutomationId)}",
                $"command_key_smoke={(commandKeyEvidence.IsPassed ? "passed" : "failed")}",
                $"command_key_smoke_attempted={FormatBool(attemptedCommandKeyEvidence)}",
                $"cmd_new_workbook_menu_gesture={FormatBool(commandKeyEvidence.HasNewWorkbookMenuGesture)}",
                $"cmd_open_menu_gesture={FormatBool(commandKeyEvidence.HasOpenMenuGesture)}",
                $"cmd_save_menu_gesture={FormatBool(commandKeyEvidence.HasSaveMenuGesture)}",
                $"cmd_save_as_menu_gesture={FormatBool(commandKeyEvidence.HasSaveAsMenuGesture)}",
                $"cmd_close_workbook_menu_gesture={FormatBool(commandKeyEvidence.HasCloseWorkbookMenuGesture)}",
                $"cmd_quit_menu_gesture={FormatBool(commandKeyEvidence.HasQuitMenuGesture)}",
                $"cmd_select_all_menu_gesture={FormatBool(commandKeyEvidence.HasSelectAllMenuGesture)}",
                $"cmd_find_menu_gesture={FormatBool(commandKeyEvidence.HasFindMenuGesture)}",
                $"cmd_find_direct_route_source_guard={FormatBool(commandKeyEvidence.HasFindDirectRouteSourceGuard)}",
                $"cmd_page_up_direct_route_source_guard={FormatBool(commandKeyEvidence.HasPageUpDirectRouteSourceGuard)}",
                $"cmd_page_down_direct_route_source_guard={FormatBool(commandKeyEvidence.HasPageDownDirectRouteSourceGuard)}",
                $"cmd_bold_menu_gesture={FormatBool(commandKeyEvidence.HasBoldMenuGesture)}",
                $"cmd_italic_menu_gesture={FormatBool(commandKeyEvidence.HasItalicMenuGesture)}",
                $"cmd_underline_menu_gesture={FormatBool(commandKeyEvidence.HasUnderlineMenuGesture)}",
                $"live_command_key_smoke_required={FormatBool(options.VerifyLiveCommandKeys)}",
                $"live_command_key_smoke={liveCommandKeySmokeStatus}",
                $"live_command_key_smoke_attempted={FormatBool(liveCommandKeyEvidence.IsReady)}",
                $"live_command_key_smoke_ready={FormatBool(liveCommandKeyEvidence.IsReady)}",
                $"live_cmd_select_all_received={FormatBool(liveCommandKeyEvidence.HasSelectAllCommandKey)}",
                $"live_cmd_select_all_state_changed={FormatBool(liveCommandKeyEvidence.HasSelectAllStateChange)}",
                $"live_cmd_bold_received={FormatBool(liveCommandKeyEvidence.HasBoldCommandKey)}",
                $"live_cmd_bold_state_changed={FormatBool(liveCommandKeyEvidence.HasBoldStateChange)}",
                $"live_cmd_italic_received={FormatBool(liveCommandKeyEvidence.HasItalicCommandKey)}",
                $"live_cmd_italic_state_changed={FormatBool(liveCommandKeyEvidence.HasItalicStateChange)}",
                $"live_cmd_underline_received={FormatBool(liveCommandKeyEvidence.HasUnderlineCommandKey)}",
                $"live_cmd_underline_state_changed={FormatBool(liveCommandKeyEvidence.HasUnderlineStateChange)}",
                $"external_image_clipboard_paste_required={FormatBool(options.VerifyImageClipboardPaste)}",
                $"external_image_clipboard_paste={FormatBool(imageClipboardPasteVerified)}",
                $"external_image_clipboard_picture_count={snapshot.ExternalImageClipboardPictureCount}",
                $"external_image_clipboard_picture_png_bytes={snapshot.ExternalImageClipboardPicturePngByteCount}",
                $"macos_dialog_smoke={(snapshot.DialogEvidence.IsPassed ? "passed" : "failed")}",
                $"macos_dialog_smoke_attempted={FormatBool(attemptedDialogEvidence)}",
                $"macos_dialog_smoke_status={dialogSmokeStatus}",
                $"macos_dialog_activation_completed={FormatBool(attemptedDialogEvidence && snapshot.DialogEvidence.IsPassed)}",
                $"find_dialog={FormatBool(snapshot.DialogEvidence.HasFindDialog)}",
                $"find_dialog_text_box={FormatBool(snapshot.DialogEvidence.HasFindDialogTextBox)}",
                $"find_dialog_action_buttons={FormatBool(snapshot.DialogEvidence.HasFindDialogActionButtons)}",
                $"find_dialog_options={FormatBool(snapshot.DialogEvidence.HasFindDialogOptions)}",
                $"find_dialog_format_controls={FormatBool(snapshot.DialogEvidence.HasFindDialogFormatControls)}",
                $"find_dialog_compact_layout={FormatBool(snapshot.DialogEvidence.HasFindDialogCompactLayout)}",
                $"find_dialog_result_closed_without_accept={FormatBool(snapshot.DialogEvidence.HasFindDialogClosedWithoutAccept)}",
                $"replace_dialog={FormatBool(snapshot.DialogEvidence.HasReplaceDialog)}",
                $"replace_dialog_text_boxes={FormatBool(snapshot.DialogEvidence.HasReplaceDialogTextBoxes)}",
                $"replace_dialog_action_buttons={FormatBool(snapshot.DialogEvidence.HasReplaceDialogActionButtons)}",
                $"replace_dialog_options={FormatBool(snapshot.DialogEvidence.HasReplaceDialogOptions)}",
                $"replace_dialog_format_controls={FormatBool(snapshot.DialogEvidence.HasReplaceDialogFormatControls)}",
                $"replace_dialog_compact_layout={FormatBool(snapshot.DialogEvidence.HasReplaceDialogCompactLayout)}",
                $"replace_dialog_result_closed_without_accept={FormatBool(snapshot.DialogEvidence.HasReplaceDialogClosedWithoutAccept)}",
                $"go_to_dialog={FormatBool(snapshot.DialogEvidence.HasGoToDialog)}",
                $"go_to_dialog_reference_controls={FormatBool(snapshot.DialogEvidence.HasGoToDialogReferenceControls)}",
                $"go_to_dialog_history_controls={FormatBool(snapshot.DialogEvidence.HasGoToDialogHistoryControls)}",
                $"go_to_dialog_special_control={FormatBool(snapshot.DialogEvidence.HasGoToDialogSpecialControl)}",
                $"go_to_dialog_compact_layout={FormatBool(snapshot.DialogEvidence.HasGoToDialogCompactLayout)}",
                $"go_to_dialog_result_closed_without_accept={FormatBool(snapshot.DialogEvidence.HasGoToDialogClosedWithoutAccept)}",
                $"go_to_special_dialog={FormatBool(snapshot.DialogEvidence.HasGoToSpecialDialog)}",
                $"go_to_special_dialog_kind_controls={FormatBool(snapshot.DialogEvidence.HasGoToSpecialKindControls)}",
                $"go_to_special_dialog_value_type_controls={FormatBool(snapshot.DialogEvidence.HasGoToSpecialValueTypeControls)}",
                $"go_to_special_dialog_compact_layout={FormatBool(snapshot.DialogEvidence.HasGoToSpecialDialogCompactLayout)}",
                $"go_to_special_dialog_result_closed_without_accept={FormatBool(snapshot.DialogEvidence.HasGoToSpecialDialogClosedWithoutAccept)}",
                $"format_cells_dialog={FormatBool(snapshot.DialogEvidence.HasFormatCellsDialog)}",
                $"format_cells_dialog_tab_strip={FormatBool(snapshot.DialogEvidence.HasFormatCellsDialogTabStrip)}",
                $"format_cells_dialog_default_number_tab={FormatBool(snapshot.DialogEvidence.HasFormatCellsDialogDefaultNumberTab)}",
                $"format_cells_dialog_number_controls={FormatBool(snapshot.DialogEvidence.HasFormatCellsDialogNumberControls)}",
                $"format_cells_dialog_action_buttons={FormatBool(snapshot.DialogEvidence.HasFormatCellsDialogActionButtons)}",
                $"format_cells_dialog_compact_layout={FormatBool(snapshot.DialogEvidence.HasFormatCellsDialogCompactLayout)}",
                $"format_cells_dialog_result_closed_without_accept={FormatBool(snapshot.DialogEvidence.HasFormatCellsDialogClosedWithoutAccept)}",
                $"sort_dialog={FormatBool(snapshot.DialogEvidence.HasSortDialog)}",
                $"sort_dialog_sort_on_controls={FormatBool(snapshot.DialogEvidence.HasSortDialogSortOnControls)}",
                $"sort_dialog_color_controls={FormatBool(snapshot.DialogEvidence.HasSortDialogColorControls)}",
                $"sort_dialog_action_buttons={FormatBool(snapshot.DialogEvidence.HasSortDialogActionButtons)}",
                $"sort_dialog_compact_layout={FormatBool(snapshot.DialogEvidence.HasSortDialogCompactLayout)}",
                $"sort_dialog_result_closed_without_accept={FormatBool(snapshot.DialogEvidence.HasSortDialogClosedWithoutAccept)}",
                $"data_validation_dropdown_control={FormatBool(snapshot.DialogEvidence.HasDataValidationDropdownControl)}",
                $"data_validation_dropdown_items={FormatBool(snapshot.DialogEvidence.HasDataValidationDropdownItems)}",
                $"data_validation_dialog={FormatBool(snapshot.DialogEvidence.HasDataValidationDialog)}",
                $"data_validation_dialog_criteria_controls={FormatBool(snapshot.DialogEvidence.HasDataValidationDialogCriteriaControls)}",
                $"data_validation_dialog_message_controls={FormatBool(snapshot.DialogEvidence.HasDataValidationDialogMessageControls)}",
                $"data_validation_dialog_action_buttons={FormatBool(snapshot.DialogEvidence.HasDataValidationDialogActionButtons)}",
                $"data_validation_dialog_compact_layout={FormatBool(snapshot.DialogEvidence.HasDataValidationDialogCompactLayout)}",
                $"data_validation_dialog_result_closed_without_accept={FormatBool(snapshot.DialogEvidence.HasDataValidationDialogClosedWithoutAccept)}",
                $"conditional_format_rule_dialog={FormatBool(snapshot.DialogEvidence.HasConditionalFormatRuleDialog)}",
                $"conditional_format_rule_dialog_type_controls={FormatBool(snapshot.DialogEvidence.HasConditionalFormatRuleTypeControls)}",
                $"conditional_format_rule_dialog_preset_controls={FormatBool(snapshot.DialogEvidence.HasConditionalFormatRulePresetControls)}",
                $"conditional_format_rule_dialog_value_controls={FormatBool(snapshot.DialogEvidence.HasConditionalFormatRuleValueControls)}",
                $"conditional_format_rule_dialog_action_buttons={FormatBool(snapshot.DialogEvidence.HasConditionalFormatRuleActionButtons)}",
                $"conditional_format_rule_dialog_compact_layout={FormatBool(snapshot.DialogEvidence.HasConditionalFormatRuleCompactLayout)}",
                $"conditional_format_rule_dialog_result_closed_without_accept={FormatBool(snapshot.DialogEvidence.HasConditionalFormatRuleDialogClosedWithoutAccept)}",
                $"manage_conditional_formats_dialog={FormatBool(snapshot.DialogEvidence.HasManageConditionalFormatsDialog)}",
                $"manage_conditional_formats_dialog_list_controls={FormatBool(snapshot.DialogEvidence.HasManageConditionalFormatsListControls)}",
                $"manage_conditional_formats_dialog_reorder_controls={FormatBool(snapshot.DialogEvidence.HasManageConditionalFormatsReorderControls)}",
                $"manage_conditional_formats_dialog_applies_to_controls={FormatBool(snapshot.DialogEvidence.HasManageConditionalFormatsAppliesToControls)}",
                $"manage_conditional_formats_dialog_action_buttons={FormatBool(snapshot.DialogEvidence.HasManageConditionalFormatsActionButtons)}",
                $"manage_conditional_formats_dialog_compact_layout={FormatBool(snapshot.DialogEvidence.HasManageConditionalFormatsCompactLayout)}",
                $"manage_conditional_formats_dialog_result_closed_without_accept={FormatBool(snapshot.DialogEvidence.HasManageConditionalFormatsDialogClosedWithoutAccept)}",
                $"opened_source_path={snapshot.OpenedSourcePath ?? ""}",
                $"is_opening={FormatBool(snapshot.IsOpening)}",
                $"new_sheet_button={FormatBool(snapshot.HasNewSheetButton)}",
                $"toolbar_format_painter_button={FormatBool(snapshot.HasFormatPainterButton)}",
                $"toolbar_autosum_button={FormatBool(snapshot.HasAutoSumButton)}",
                $"toolbar_autosum_sum_menu_item={FormatBool(snapshot.HasAutoSumSumMenuItem)}",
                $"toolbar_autosum_average_menu_item={FormatBool(snapshot.HasAutoSumAverageMenuItem)}",
                $"toolbar_autosum_count_numbers_menu_item={FormatBool(snapshot.HasAutoSumCountNumbersMenuItem)}",
                $"toolbar_autosum_count_all_menu_item={FormatBool(snapshot.HasAutoSumCountAllMenuItem)}",
                $"toolbar_autosum_max_menu_item={FormatBool(snapshot.HasAutoSumMaxMenuItem)}",
                $"toolbar_autosum_min_menu_item={FormatBool(snapshot.HasAutoSumMinMenuItem)}",
                $"toolbar_fill_cells_button={FormatBool(snapshot.HasFillCellsButton)}",
                $"toolbar_fill_down_menu_item={FormatBool(snapshot.HasFillDownMenuItem)}",
                $"toolbar_fill_right_menu_item={FormatBool(snapshot.HasFillRightMenuItem)}",
                $"toolbar_fill_up_menu_item={FormatBool(snapshot.HasFillUpMenuItem)}",
                $"toolbar_fill_left_menu_item={FormatBool(snapshot.HasFillLeftMenuItem)}",
                $"toolbar_clear_button={FormatBool(snapshot.HasClearButton)}",
                $"toolbar_clear_all_menu_item={FormatBool(snapshot.HasClearAllMenuItem)}",
                $"toolbar_clear_formats_menu_item={FormatBool(snapshot.HasClearFormatsMenuItem)}",
                $"toolbar_clear_contents_menu_item={FormatBool(snapshot.HasClearContentsMenuItem)}",
                $"toolbar_clear_comments_menu_item={FormatBool(snapshot.HasClearCommentsMenuItem)}",
                $"toolbar_clear_hyperlinks_menu_item={FormatBool(snapshot.HasClearHyperlinksMenuItem)}",
                $"toolbar_borders_button={FormatBool(snapshot.HasBordersButton)}",
                $"toolbar_wrap_text_button={FormatBool(snapshot.HasWrapTextButton)}",
                $"toolbar_merge_and_center_button={FormatBool(snapshot.HasMergeAndCenterButton)}",
                $"focusable_sheet_tab={FormatBool(snapshot.HasFocusableSheetTab)}",
                $"focusable_active_sheet_tab={FormatBool(snapshot.HasFocusableActiveSheetTab)}",
                $"shell_focus_cycle_targets={FormatBool(snapshot.HasShellFocusCycleTargets)}",
                $"sheet_tab_context_keyboard_help={FormatBool(snapshot.HasSheetTabContextKeyboardHelp)}",
                $"sheet_tab_context_rename_menu_item={FormatBool(snapshot.HasSheetTabContextRenameMenuItem)}",
                $"sheet_tab_context_tab_color_menu_item={FormatBool(snapshot.HasSheetTabContextTabColorMenuItem)}",
                $"sheet_tab_context_no_color_menu_item={FormatBool(snapshot.HasSheetTabContextNoColorMenuItem)}",
                $"sheet_tab_context_select_all_sheets_menu_item={FormatBool(snapshot.HasSheetTabContextSelectAllSheetsMenuItem)}",
                $"sheet_tab_context_ungroup_sheets_menu_item={FormatBool(snapshot.HasSheetTabContextUngroupSheetsMenuItem)}",
                $"native_top_level_menu_order={snapshot.NativeTopLevelMenuOrder}",
                $"native_dock_top_level_menu_order={snapshot.NativeDockTopLevelMenuOrder}",
                $"native_dock_menu_installed={FormatBool(snapshot.HasNativeDockMenu)}",
                $"native_dock_file_menu={FormatBool(snapshot.HasNativeDockFileMenu)}",
                $"native_dock_file_menu_item_count={snapshot.NativeDockFileMenuItemCount}",
                $"native_file_menu={FormatBool(snapshot.HasNativeFileMenu)}",
                $"native_home_menu={FormatBool(snapshot.HasNativeHomeMenu)}",
                $"native_insert_menu={FormatBool(snapshot.HasNativeInsertMenu)}",
                $"native_page_layout_menu={FormatBool(snapshot.HasNativePageLayoutMenu)}",
                $"native_formulas_menu={FormatBool(snapshot.HasNativeFormulasMenu)}",
                $"native_data_menu={FormatBool(snapshot.HasNativeDataMenu)}",
                $"native_review_menu={FormatBool(snapshot.HasNativeReviewMenu)}",
                $"native_view_menu={FormatBool(snapshot.HasNativeViewMenu)}",
                $"native_sheet_menu={FormatBool(snapshot.HasNativeSheetMenu)}",
                $"native_window_menu={FormatBool(snapshot.HasNativeWindowMenu)}",
                $"native_help_menu={FormatBool(snapshot.HasNativeHelpMenu)}",
                $"native_new_workbook_menu_item={FormatBool(snapshot.HasNativeNewWorkbookMenuItem)}",
                $"native_open_menu_item={FormatBool(snapshot.HasNativeOpenMenuItem)}",
                $"native_open_recent_menu_item={FormatBool(snapshot.HasNativeOpenRecentMenuItem)}",
                $"native_open_recent_item_count={snapshot.NativeOpenRecentItemCount}",
                $"native_save_menu_item={FormatBool(snapshot.HasNativeSaveMenuItem)}",
                $"native_save_as_menu_item={FormatBool(snapshot.HasNativeSaveAsMenuItem)}",
                $"native_export_pdf_menu_item={FormatBool(snapshot.HasNativeExportPdfMenuItem)}",
                $"native_share_workbook_menu_item={FormatBool(snapshot.HasNativeShareWorkbookMenuItem)}",
                $"native_workbook_statistics_menu_item={FormatBool(snapshot.HasNativeWorkbookStatisticsMenuItem)}",
                $"native_close_workbook_menu_item={FormatBool(snapshot.HasNativeCloseWorkbookMenuItem)}",
                $"native_new_sheet_menu_item={FormatBool(snapshot.HasNativeNewSheetMenuItem)}",
                $"native_rename_sheet_menu_item={FormatBool(snapshot.HasNativeRenameSheetMenuItem)}",
                $"native_duplicate_sheet_menu_item={FormatBool(snapshot.HasNativeDuplicateSheetMenuItem)}",
                $"native_move_sheet_left_menu_item={FormatBool(snapshot.HasNativeMoveSheetLeftMenuItem)}",
                $"native_move_sheet_right_menu_item={FormatBool(snapshot.HasNativeMoveSheetRightMenuItem)}",
                $"native_tab_color_menu_item={FormatBool(snapshot.HasNativeTabColorMenuItem)}",
                $"native_tab_color_clear_item={FormatBool(snapshot.HasNativeClearTabColorMenuItem)}",
                $"native_tab_color_swatch_count={snapshot.NativeTabColorSwatchCount}",
                $"native_select_all_sheets_menu_item={FormatBool(snapshot.HasNativeSelectAllSheetsMenuItem)}",
                $"native_ungroup_sheets_menu_item={FormatBool(snapshot.HasNativeUngroupSheetsMenuItem)}",
                $"native_hide_sheet_menu_item={FormatBool(snapshot.HasNativeHideSheetMenuItem)}",
                $"native_unhide_sheet_menu_item={FormatBool(snapshot.HasNativeUnhideSheetMenuItem)}",
                $"native_delete_sheet_menu_item={FormatBool(snapshot.HasNativeDeleteSheetMenuItem)}",
                $"native_undo_menu_item={FormatBool(snapshot.HasNativeUndoMenuItem)}",
                $"native_redo_menu_item={FormatBool(snapshot.HasNativeRedoMenuItem)}",
                $"native_cut_menu_item={FormatBool(snapshot.HasNativeCutMenuItem)}",
                $"native_copy_menu_item={FormatBool(snapshot.HasNativeCopyMenuItem)}",
                $"native_paste_menu_item={FormatBool(snapshot.HasNativePasteMenuItem)}",
                $"native_paste_special_menu_item={FormatBool(snapshot.HasNativePasteSpecialMenuItem)}",
                $"native_format_painter_menu_item={FormatBool(snapshot.HasNativeFormatPainterMenuItem)}",
                $"native_paste_special_comments_menu_item={FormatBool(snapshot.HasNativePasteSpecialCommentsMenuItem)}",
                $"native_paste_special_validation_menu_item={FormatBool(snapshot.HasNativePasteSpecialValidationMenuItem)}",
                $"native_paste_special_all_except_borders_menu_item={FormatBool(snapshot.HasNativePasteSpecialAllExceptBordersMenuItem)}",
                $"native_paste_special_all_merging_conditional_formats_menu_item={FormatBool(snapshot.HasNativePasteSpecialAllMergingConditionalFormatsMenuItem)}",
                $"native_paste_special_column_widths_menu_item={FormatBool(snapshot.HasNativePasteSpecialColumnWidthsMenuItem)}",
                $"native_paste_special_formulas_and_number_formats_menu_item={FormatBool(snapshot.HasNativePasteSpecialFormulasAndNumberFormatsMenuItem)}",
                $"native_paste_special_values_and_number_formats_menu_item={FormatBool(snapshot.HasNativePasteSpecialValuesAndNumberFormatsMenuItem)}",
                $"native_paste_special_values_and_source_formatting_menu_item={FormatBool(snapshot.HasNativePasteSpecialValuesAndSourceFormattingMenuItem)}",
                $"native_paste_special_keep_source_column_widths_menu_item={FormatBool(snapshot.HasNativePasteSpecialKeepSourceColumnWidthsMenuItem)}",
                $"native_paste_special_paste_link_menu_item={FormatBool(snapshot.HasNativePasteSpecialPasteLinkMenuItem)}",
                $"native_paste_special_text_menu_item={FormatBool(snapshot.HasNativePasteSpecialTextMenuItem)}",
                $"native_paste_special_unicode_text_menu_item={FormatBool(snapshot.HasNativePasteSpecialUnicodeTextMenuItem)}",
                $"native_paste_special_picture_menu_item={FormatBool(snapshot.HasNativePasteSpecialPictureMenuItem)}",
                $"native_paste_special_linked_picture_menu_item={FormatBool(snapshot.HasNativePasteSpecialLinkedPictureMenuItem)}",
                $"native_select_all_menu_item={FormatBool(snapshot.HasNativeSelectAllMenuItem)}",
                $"native_find_menu_item={FormatBool(snapshot.HasNativeFindMenuItem)}",
                $"native_find_next_menu_item={FormatBool(snapshot.HasNativeFindNextMenuItem)}",
                $"native_replace_menu_item={FormatBool(snapshot.HasNativeReplaceMenuItem)}",
                $"native_go_to_menu_item={FormatBool(snapshot.HasNativeGoToMenuItem)}",
                $"native_go_to_special_menu_item={FormatBool(snapshot.HasNativeGoToSpecialMenuItem)}",
                $"native_sort_ascending_menu_item={FormatBool(snapshot.HasNativeSortAscendingMenuItem)}",
                $"native_sort_descending_menu_item={FormatBool(snapshot.HasNativeSortDescendingMenuItem)}",
                $"native_flash_fill_menu_item={FormatBool(snapshot.HasNativeFlashFillMenuItem)}",
                $"native_advanced_filter_menu_item={FormatBool(snapshot.HasNativeAdvancedFilterMenuItem)}",
                $"native_remove_duplicates_menu_item={FormatBool(snapshot.HasNativeRemoveDuplicatesMenuItem)}",
                $"native_subtotal_menu_item={FormatBool(snapshot.HasNativeSubtotalMenuItem)}",
                $"native_data_validation_preview_menu_item={FormatBool(snapshot.HasNativeDataValidationPreviewMenuItem)}",
                $"native_data_validation_menu_item={FormatBool(snapshot.HasNativeDataValidationMenuItem)}",
                $"native_what_if_analysis_menu_item={FormatBool(snapshot.HasNativeWhatIfAnalysisMenuItem)}",
                $"native_goal_seek_menu_item={FormatBool(snapshot.HasNativeGoalSeekMenuItem)}",
                $"native_data_table_menu_item={FormatBool(snapshot.HasNativeDataTableMenuItem)}",
                $"native_scenario_manager_menu_item={FormatBool(snapshot.HasNativeScenarioManagerMenuItem)}",
                $"native_forecast_sheet_menu_item={FormatBool(snapshot.HasNativeForecastSheetMenuItem)}",
                $"native_review_summary_menu_item={FormatBool(snapshot.HasNativeReviewSummaryMenuItem)}",
                $"native_check_accessibility_menu_item={FormatBool(snapshot.HasNativeCheckAccessibilityMenuItem)}",
                $"native_next_note_menu_item={FormatBool(snapshot.HasNativeNextNoteMenuItem)}",
                $"native_previous_note_menu_item={FormatBool(snapshot.HasNativePreviousNoteMenuItem)}",
                $"native_next_comment_menu_item={FormatBool(snapshot.HasNativeNextCommentMenuItem)}",
                $"native_previous_comment_menu_item={FormatBool(snapshot.HasNativePreviousCommentMenuItem)}",
                $"native_format_cells_menu_item={FormatBool(snapshot.HasNativeFormatCellsMenuItem)}",
                $"native_autosum_menu_item={FormatBool(snapshot.HasNativeAutoSumMenuItem)}",
                $"native_autosum_sum_menu_item={FormatBool(snapshot.HasNativeAutoSumSumMenuItem)}",
                $"native_autosum_average_menu_item={FormatBool(snapshot.HasNativeAutoSumAverageMenuItem)}",
                $"native_autosum_count_numbers_menu_item={FormatBool(snapshot.HasNativeAutoSumCountNumbersMenuItem)}",
                $"native_autosum_count_all_menu_item={FormatBool(snapshot.HasNativeAutoSumCountAllMenuItem)}",
                $"native_autosum_max_menu_item={FormatBool(snapshot.HasNativeAutoSumMaxMenuItem)}",
                $"native_autosum_min_menu_item={FormatBool(snapshot.HasNativeAutoSumMinMenuItem)}",
                $"native_fill_cells_menu_item={FormatBool(snapshot.HasNativeFillCellsMenuItem)}",
                $"native_fill_down_menu_item={FormatBool(snapshot.HasNativeFillDownMenuItem)}",
                $"native_fill_right_menu_item={FormatBool(snapshot.HasNativeFillRightMenuItem)}",
                $"native_fill_up_menu_item={FormatBool(snapshot.HasNativeFillUpMenuItem)}",
                $"native_fill_left_menu_item={FormatBool(snapshot.HasNativeFillLeftMenuItem)}",
                $"native_clear_menu_item={FormatBool(snapshot.HasNativeClearMenuItem)}",
                $"native_clear_all_menu_item={FormatBool(snapshot.HasNativeClearAllMenuItem)}",
                $"native_clear_formats_menu_item={FormatBool(snapshot.HasNativeClearFormatsMenuItem)}",
                $"native_clear_contents_menu_item={FormatBool(snapshot.HasNativeClearContentsMenuItem)}",
                $"native_clear_comments_menu_item={FormatBool(snapshot.HasNativeClearCommentsMenuItem)}",
                $"native_clear_hyperlinks_menu_item={FormatBool(snapshot.HasNativeClearHyperlinksMenuItem)}",
                $"native_bold_menu_item={FormatBool(snapshot.HasNativeBoldMenuItem)}",
                $"native_italic_menu_item={FormatBool(snapshot.HasNativeItalicMenuItem)}",
                $"native_underline_menu_item={FormatBool(snapshot.HasNativeUnderlineMenuItem)}",
                $"native_double_underline_menu_item={FormatBool(snapshot.HasNativeDoubleUnderlineMenuItem)}",
                $"native_strikethrough_menu_item={FormatBool(snapshot.HasNativeStrikethroughMenuItem)}",
                $"native_increase_font_size_menu_item={FormatBool(snapshot.HasNativeIncreaseFontSizeMenuItem)}",
                $"native_decrease_font_size_menu_item={FormatBool(snapshot.HasNativeDecreaseFontSizeMenuItem)}",
                $"native_fill_color_menu_item={FormatBool(snapshot.HasNativeFillColorMenuItem)}",
                $"native_clear_fill_menu_item={FormatBool(snapshot.HasNativeClearFillMenuItem)}",
                $"native_font_color_menu_item={FormatBool(snapshot.HasNativeFontColorMenuItem)}",
                $"native_fill_color_swatch_count={snapshot.NativeFillColorSwatchCount}",
                $"native_font_color_swatch_count={snapshot.NativeFontColorSwatchCount}",
                $"native_borders_menu_item={FormatBool(snapshot.HasNativeBordersMenuItem)}",
                $"native_borders_preset_count={snapshot.NativeBordersPresetCount}",
                $"native_cell_styles_menu_item={FormatBool(snapshot.HasNativeCellStylesMenuItem)}",
                $"native_cell_styles_preset_count={snapshot.NativeCellStylesPresetCount}",
                $"native_horizontal_text_menu_item={FormatBool(snapshot.HasNativeHorizontalTextMenuItem)}",
                $"native_angle_counterclockwise_menu_item={FormatBool(snapshot.HasNativeAngleCounterclockwiseMenuItem)}",
                $"native_angle_clockwise_menu_item={FormatBool(snapshot.HasNativeAngleClockwiseMenuItem)}",
                $"native_vertical_text_menu_item={FormatBool(snapshot.HasNativeVerticalTextMenuItem)}",
                $"native_rotate_text_up_menu_item={FormatBool(snapshot.HasNativeRotateTextUpMenuItem)}",
                $"native_rotate_text_down_menu_item={FormatBool(snapshot.HasNativeRotateTextDownMenuItem)}",
                $"native_currency_format_menu_item={FormatBool(snapshot.HasNativeCurrencyFormatMenuItem)}",
                $"native_percent_format_menu_item={FormatBool(snapshot.HasNativePercentFormatMenuItem)}",
                $"native_comma_style_menu_item={FormatBool(snapshot.HasNativeCommaStyleMenuItem)}",
                $"native_increase_decimal_menu_item={FormatBool(snapshot.HasNativeIncreaseDecimalMenuItem)}",
                $"native_decrease_decimal_menu_item={FormatBool(snapshot.HasNativeDecreaseDecimalMenuItem)}",
                $"native_align_top_menu_item={FormatBool(snapshot.HasNativeAlignTopMenuItem)}",
                $"native_align_middle_menu_item={FormatBool(snapshot.HasNativeAlignMiddleMenuItem)}",
                $"native_align_bottom_menu_item={FormatBool(snapshot.HasNativeAlignBottomMenuItem)}",
                $"native_wrap_text_menu_item={FormatBool(snapshot.HasNativeWrapTextMenuItem)}",
                $"native_merge_and_center_menu_item={FormatBool(snapshot.HasNativeMergeAndCenterMenuItem)}",
                $"native_unmerge_cells_menu_item={FormatBool(snapshot.HasNativeUnmergeCellsMenuItem)}",
                $"native_show_gridlines_menu_item={FormatBool(snapshot.HasNativeShowGridlinesMenuItem)}",
                $"native_show_headings_menu_item={FormatBool(snapshot.HasNativeShowHeadingsMenuItem)}",
                $"native_zoom_in_menu_item={FormatBool(snapshot.HasNativeZoomInMenuItem)}",
                $"native_zoom_out_menu_item={FormatBool(snapshot.HasNativeZoomOutMenuItem)}",
                $"native_zoom_100_menu_item={FormatBool(snapshot.HasNativeZoom100MenuItem)}",
                $"native_zoom_to_selection_menu_item={FormatBool(snapshot.HasNativeZoomToSelectionMenuItem)}",
                $"native_freeze_panes_menu_item={FormatBool(snapshot.HasNativeFreezePanesMenuItem)}",
                $"native_freeze_top_row_menu_item={FormatBool(snapshot.HasNativeFreezeTopRowMenuItem)}",
                $"native_freeze_first_column_menu_item={FormatBool(snapshot.HasNativeFreezeFirstColumnMenuItem)}",
                $"native_unfreeze_panes_menu_item={FormatBool(snapshot.HasNativeUnfreezePanesMenuItem)}",
                $"native_decrease_indent_menu_item={FormatBool(snapshot.HasNativeDecreaseIndentMenuItem)}",
                $"native_increase_indent_menu_item={FormatBool(snapshot.HasNativeIncreaseIndentMenuItem)}",
                $"native_align_left_menu_item={FormatBool(snapshot.HasNativeAlignLeftMenuItem)}",
                $"native_align_center_menu_item={FormatBool(snapshot.HasNativeAlignCenterMenuItem)}",
                $"native_align_right_menu_item={FormatBool(snapshot.HasNativeAlignRightMenuItem)}",
                $"native_show_formulas_menu_item={FormatBool(snapshot.HasNativeShowFormulasMenuItem)}",
                $"native_minimize_window_menu_item={FormatBool(snapshot.HasNativeMinimizeWindowMenuItem)}",
                $"native_zoom_window_menu_item={FormatBool(snapshot.HasNativeZoomWindowMenuItem)}",
                $"native_bring_all_to_front_menu_item={FormatBool(snapshot.HasNativeBringAllToFrontMenuItem)}",
                $"native_help_online_menu_item={FormatBool(snapshot.HasNativeHelpOnlineMenuItem)}",
                $"native_send_feedback_menu_item={FormatBool(snapshot.HasNativeSendFeedbackMenuItem)}",
                $"native_check_for_updates_menu_item={FormatBool(snapshot.HasNativeCheckForUpdatesMenuItem)}",
                $"native_about_menu_item={FormatBool(snapshot.HasNativeAboutMenuItem)}",
                $"native_legal_notices_menu_item={FormatBool(snapshot.HasNativeLegalNoticesMenuItem)}",
                $"native_quit_menu_item={FormatBool(snapshot.HasNativeQuitMenuItem)}",
            ]);
    }

    private static void WriteFailureReport(
        string reportPath,
        MacOsLaunchSmokeSnapshot snapshot,
        MacOsLaunchSmokeCommandKeySnapshot commandKeyEvidence,
        MacOsLaunchSmokeLiveCommandKeySnapshot liveCommandKeyEvidence,
        MacOsLaunchSmokeOptions options,
        int initialExternalImageClipboardPictureCount,
        bool attemptedCommandKeyEvidence,
        bool attemptedDialogEvidence,
        Exception exception)
    {
        WriteReport(
            reportPath,
            snapshot,
            commandKeyEvidence,
            liveCommandKeyEvidence,
            options,
            initialExternalImageClipboardPictureCount,
            attemptedCommandKeyEvidence,
            attemptedDialogEvidence,
            finalReport: true);
        File.AppendAllLines(reportPath, [$"error={exception.GetType().Name}: {exception.Message}"]);
    }

    private static string GetLiveCommandKeySmokeStatus(
        MacOsLaunchSmokeOptions options,
        MacOsLaunchSmokeLiveCommandKeySnapshot liveCommandKeyEvidence,
        bool finalReport)
    {
        if (!options.VerifyLiveCommandKeys)
            return "not_required";

        if (liveCommandKeyEvidence.IsPassed)
            return "passed";

        if (!liveCommandKeyEvidence.IsReady)
            return "not_ready";

        if (!finalReport)
            return "waiting_for_system_events";

        if (!liveCommandKeyEvidence.HasAnyCommandKey)
            return "blocked_or_not_received";

        return "failed_missing_state_change";
    }

    private static string GetDialogSmokeStatus(
        MacOsLaunchSmokeSnapshot snapshot,
        bool attemptedDialogEvidence)
    {
        if (snapshot.DialogEvidence.IsPassed)
            return "passed";

        if (!snapshot.HasShellEvidence)
            return "not_attempted_shell_evidence_incomplete";

        if (!attemptedDialogEvidence)
            return "not_attempted";

        return "failed_missing_dialog_evidence";
    }

    private static void Shutdown(int exitCode)
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.TryShutdown(exitCode);
        }
    }

    private static string FormatBool(bool value) => value ? "true" : "false";
}
#endif
