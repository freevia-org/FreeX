using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

internal sealed record MacOsLaunchSmokeOptions(
    string ReportPath,
    bool VerifyImageClipboardPaste,
    bool VerifyLiveCommandKeys)
{
    public const string Argument = "--macos-launch-smoke";
    public const string VerifyImageClipboardPasteArgument = "--macos-launch-smoke-verify-image-clipboard";
    public const string VerifyLiveCommandKeysArgument = "--macos-launch-smoke-verify-live-command-keys";

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
        var verifyImageClipboardPaste = false;
        var verifyLiveCommandKeys = false;
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, VerifyImageClipboardPasteArgument, StringComparison.OrdinalIgnoreCase))
            {
                verifyImageClipboardPaste = true;
                continue;
            }

            if (string.Equals(argument, VerifyLiveCommandKeysArgument, StringComparison.OrdinalIgnoreCase))
            {
                verifyLiveCommandKeys = true;
                continue;
            }

            if (!string.Equals(argument, Argument, StringComparison.OrdinalIgnoreCase))
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

        if (reportPath is not null)
            options = new MacOsLaunchSmokeOptions(reportPath, verifyImageClipboardPaste, verifyLiveCommandKeys);

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
    bool HasFormatCellsDialogClosedWithoutAccept = false)
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
        HasFormatCellsDialogClosedWithoutAccept: false);

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
        HasFindDialogClosedWithoutAccept &&
        HasReplaceDialogClosedWithoutAccept &&
        HasGoToDialogClosedWithoutAccept &&
        HasGoToSpecialDialogClosedWithoutAccept &&
        HasFormatCellsDialogClosedWithoutAccept;
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
    bool HasUnderlineMenuGesture)
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
        HasUnderlineMenuGesture: false);

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
        HasUnderlineMenuGesture;
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
    bool HasFocusableSheetTab,
    bool HasFocusableActiveSheetTab,
    bool HasShellFocusCycleTargets,
    bool HasSheetTabContextKeyboardHelp,
    bool HasSheetTabContextRenameMenuItem,
    bool HasSheetTabContextTabColorMenuItem,
    bool HasSheetTabContextNoColorMenuItem,
    bool HasSheetTabContextSelectAllSheetsMenuItem,
    bool HasSheetTabContextUngroupSheetsMenuItem,
    bool HasNativeFileMenu,
    bool HasNativeEditMenu,
    bool HasNativeDataMenu,
    bool HasNativeReviewMenu,
    bool HasNativeFormatMenu,
    bool HasNativeViewMenu,
    bool HasNativeSheetMenu,
    bool HasNativeHelpMenu,
    bool HasNativeNewWorkbookMenuItem,
    bool HasNativeOpenMenuItem,
    bool HasNativeOpenRecentMenuItem,
    int NativeOpenRecentItemCount,
    bool HasNativeSaveMenuItem,
    bool HasNativeSaveAsMenuItem,
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
    bool HasNativeHelpOnlineMenuItem,
    bool HasNativeSendFeedbackMenuItem,
    bool HasNativeCheckForUpdatesMenuItem,
    bool HasNativeAboutMenuItem,
    bool HasNativeLegalNoticesMenuItem,
    bool HasNativeQuitMenuItem,
    bool HasNativeFormatCellsMenuItem = false)
{
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
        HasFocusableSheetTab &&
        HasFocusableActiveSheetTab &&
        HasShellFocusCycleTargets &&
        HasSheetTabContextKeyboardHelp &&
        HasSheetTabContextRenameMenuItem &&
        HasSheetTabContextTabColorMenuItem &&
        HasSheetTabContextNoColorMenuItem &&
        HasSheetTabContextSelectAllSheetsMenuItem &&
        HasSheetTabContextUngroupSheetsMenuItem &&
        HasNativeFileMenu &&
        HasNativeEditMenu &&
        HasNativeDataMenu &&
        HasNativeReviewMenu &&
        HasNativeFormatMenu &&
        HasNativeViewMenu &&
        HasNativeSheetMenu &&
        HasNativeHelpMenu &&
        HasNativeNewWorkbookMenuItem &&
        HasNativeOpenMenuItem &&
        HasNativeOpenRecentMenuItem &&
        NativeOpenRecentItemCount > 0 &&
        HasNativeSaveMenuItem &&
        HasNativeSaveAsMenuItem &&
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
    private const int MaxWaitMilliseconds = 15000;
    private const int LiveCommandKeyWaitMilliseconds = 30000;
    private const int PollDelayMilliseconds = 250;

    public static void Start(MainWindow mainWindow, MacOsLaunchSmokeOptions options)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(options);

        mainWindow.Opened += async (_, _) => await RunAsync(mainWindow, options);
    }

    private static async Task RunAsync(MainWindow mainWindow, MacOsLaunchSmokeOptions options)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(
            MaxWaitMilliseconds + (options.VerifyLiveCommandKeys ? LiveCommandKeyWaitMilliseconds : 0));
        var snapshot = mainWindow.CreateLaunchSmokeSnapshot();
        var commandKeyEvidence = MacOsLaunchSmokeCommandKeySnapshot.Empty;
        var liveCommandKeyEvidence = MacOsLaunchSmokeLiveCommandKeySnapshot.Empty;
        var initialExternalImageClipboardPictureCount = snapshot.ExternalImageClipboardPictureCount;
        var attemptedCommandKeyEvidence = false;
        var attemptedImageClipboardPaste = false;
        var attemptedDialogEvidence = false;
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
                    commandKeyEvidence = CaptureCommandKeyEvidence(mainWindow);
                    continue;
                }

                if (snapshot.HasShellEvidence &&
                    !snapshot.DialogEvidence.IsPassed &&
                    !attemptedDialogEvidence)
                {
                    attemptedDialogEvidence = true;
                    await mainWindow.CaptureLaunchSmokeDialogEvidenceAsync();
                    snapshot = mainWindow.CreateLaunchSmokeSnapshot();
                    continue;
                }

                if (snapshot.HasShellEvidence &&
                    snapshot.DialogEvidence.IsPassed &&
                    options.VerifyImageClipboardPaste &&
                    !attemptedImageClipboardPaste)
                {
                    attemptedImageClipboardPaste = true;
                    await mainWindow.TryPasteLaunchSmokeClipboardImageAsync();
                    snapshot = mainWindow.CreateLaunchSmokeSnapshot();
                    continue;
                }

                if (IsReadyForLiveCommandKeys(
                        snapshot,
                        options,
                        initialExternalImageClipboardPictureCount,
                        commandKeyEvidence,
                        liveCommandKeyEvidence))
                {
                    liveCommandKeyEvidence = mainWindow.BeginLaunchSmokeLiveCommandKeyProbe();
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
                snapshot = mainWindow.CreateLaunchSmokeSnapshot();
                liveCommandKeyEvidence = mainWindow.CreateLaunchSmokeLiveCommandKeySnapshot();
            }

            liveCommandKeyEvidence = mainWindow.CreateLaunchSmokeLiveCommandKeySnapshot();
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
            Shutdown(IsPassedWithCommandKeyEvidence(
                snapshot,
                options,
                initialExternalImageClipboardPictureCount,
                commandKeyEvidence,
                liveCommandKeyEvidence) ? 0 : 1);
        }
        catch (Exception ex)
        {
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

    private static MacOsLaunchSmokeCommandKeySnapshot CaptureCommandKeyEvidence(MainWindow mainWindow) =>
        new(
            HasNewWorkbookMenuGesture: HasNativeMenuItemGesture(mainWindow, "_newWorkbookMenuItem", Key.N, KeyModifiers.Meta),
            HasOpenMenuGesture: HasNativeMenuItemGesture(mainWindow, "_openMenuItem", Key.O, KeyModifiers.Meta),
            HasSaveMenuGesture: HasNativeMenuItemGesture(mainWindow, "_saveMenuItem", Key.S, KeyModifiers.Meta),
            HasSaveAsMenuGesture: HasNativeMenuItemGesture(mainWindow, "_saveAsMenuItem", Key.S, KeyModifiers.Meta | KeyModifiers.Shift),
            HasCloseWorkbookMenuGesture: HasNativeMenuItemGesture(mainWindow, "_closeWorkbookMenuItem", Key.W, KeyModifiers.Meta),
            HasQuitMenuGesture: HasNativeMenuItemGesture(mainWindow, "_quitMenuItem", Key.Q, KeyModifiers.Meta),
            HasSelectAllMenuGesture: HasNativeMenuItemGesture(mainWindow, "_selectAllMenuItem", Key.A, KeyModifiers.Meta),
            HasFindMenuGesture: HasNativeMenuItemGesture(mainWindow, "_findMenuItem", Key.F, KeyModifiers.Meta),
            HasBoldMenuGesture: HasNativeMenuItemGesture(mainWindow, "_boldMenuItem", Key.B, KeyModifiers.Meta),
            HasItalicMenuGesture: HasNativeMenuItemGesture(mainWindow, "_italicMenuItem", Key.I, KeyModifiers.Meta),
            HasUnderlineMenuGesture: HasNativeMenuItemGesture(mainWindow, "_underlineMenuItem", Key.U, KeyModifiers.Meta));

    private static bool HasNativeMenuItemGesture(
        MainWindow mainWindow,
        string fieldName,
        Key expectedKey,
        KeyModifiers expectedModifiers) =>
        typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(mainWindow) is NativeMenuItem { Gesture: { } gesture } &&
        gesture.Key == expectedKey &&
        gesture.KeyModifiers == expectedModifiers;

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

        File.WriteAllLines(
            reportPath,
            [
                $"macos_launch_smoke={(IsPassedWithCommandKeyEvidence(snapshot, options, initialExternalImageClipboardPictureCount, commandKeyEvidence, liveCommandKeyEvidence) ? "passed" : "failed")}",
                $"window_shown={FormatBool(snapshot.WindowShown)}",
                $"window_title={snapshot.WindowTitle}",
                $"display_name={snapshot.DisplayName}",
                $"active_sheet={snapshot.ActiveSheetName}",
                $"sheet_tab_count={snapshot.SheetTabCount}",
                $"viewport_rows={snapshot.ViewportRowCount}",
                $"viewport_columns={snapshot.ViewportColumnCount}",
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
                $"native_file_menu={FormatBool(snapshot.HasNativeFileMenu)}",
                $"native_edit_menu={FormatBool(snapshot.HasNativeEditMenu)}",
                $"native_data_menu={FormatBool(snapshot.HasNativeDataMenu)}",
                $"native_review_menu={FormatBool(snapshot.HasNativeReviewMenu)}",
                $"native_format_menu={FormatBool(snapshot.HasNativeFormatMenu)}",
                $"native_view_menu={FormatBool(snapshot.HasNativeViewMenu)}",
                $"native_sheet_menu={FormatBool(snapshot.HasNativeSheetMenu)}",
                $"native_help_menu={FormatBool(snapshot.HasNativeHelpMenu)}",
                $"native_new_workbook_menu_item={FormatBool(snapshot.HasNativeNewWorkbookMenuItem)}",
                $"native_open_menu_item={FormatBool(snapshot.HasNativeOpenMenuItem)}",
                $"native_open_recent_menu_item={FormatBool(snapshot.HasNativeOpenRecentMenuItem)}",
                $"native_open_recent_item_count={snapshot.NativeOpenRecentItemCount}",
                $"native_save_menu_item={FormatBool(snapshot.HasNativeSaveMenuItem)}",
                $"native_save_as_menu_item={FormatBool(snapshot.HasNativeSaveAsMenuItem)}",
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
