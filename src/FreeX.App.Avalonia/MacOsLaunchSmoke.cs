using Avalonia.Controls.ApplicationLifetimes;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

internal sealed record MacOsLaunchSmokeOptions(string ReportPath, bool VerifyImageClipboardPaste)
{
    public const string Argument = "--macos-launch-smoke";
    public const string VerifyImageClipboardPasteArgument = "--macos-launch-smoke-verify-image-clipboard";

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
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, VerifyImageClipboardPasteArgument, StringComparison.OrdinalIgnoreCase))
            {
                verifyImageClipboardPaste = true;
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
            options = new MacOsLaunchSmokeOptions(reportPath, verifyImageClipboardPaste);

        startupArguments = filteredArguments.ToArray();
        return true;
    }
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
    string? OpenedSourcePath,
    bool IsOpening,
    bool HasNewSheetButton,
    bool HasFormatPainterButton,
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
    bool HasNativeQuitMenuItem)
{
    public bool IsPassed =>
        WindowShown &&
        !IsOpening &&
        !string.IsNullOrWhiteSpace(OpenedSourcePath) &&
        SheetTabCount > 0 &&
        ViewportRowCount > 0 &&
        ViewportColumnCount > 0 &&
        HasNewSheetButton &&
        HasFormatPainterButton &&
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
}

internal static class MacOsLaunchSmokeCoordinator
{
    private const int MaxWaitMilliseconds = 15000;
    private const int PollDelayMilliseconds = 250;

    public static void Start(MainWindow mainWindow, MacOsLaunchSmokeOptions options)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(options);

        mainWindow.Opened += async (_, _) => await RunAsync(mainWindow, options);
    }

    private static async Task RunAsync(MainWindow mainWindow, MacOsLaunchSmokeOptions options)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(MaxWaitMilliseconds);
        var snapshot = mainWindow.CreateLaunchSmokeSnapshot();
        var initialExternalImageClipboardPictureCount = snapshot.ExternalImageClipboardPictureCount;
        var attemptedImageClipboardPaste = false;
        try
        {
            while (!IsPassed(snapshot, options, initialExternalImageClipboardPictureCount) &&
                DateTimeOffset.UtcNow < deadline)
            {
                if (snapshot.IsPassed &&
                    options.VerifyImageClipboardPaste &&
                    !attemptedImageClipboardPaste)
                {
                    attemptedImageClipboardPaste = true;
                    await mainWindow.TryPasteLaunchSmokeClipboardImageAsync();
                    snapshot = mainWindow.CreateLaunchSmokeSnapshot();
                    continue;
                }

                await Task.Delay(PollDelayMilliseconds);
                snapshot = mainWindow.CreateLaunchSmokeSnapshot();
            }

            WriteReport(options.ReportPath, snapshot, options, initialExternalImageClipboardPictureCount);
            Shutdown(IsPassed(snapshot, options, initialExternalImageClipboardPictureCount) ? 0 : 1);
        }
        catch (Exception ex)
        {
            WriteFailureReport(options.ReportPath, snapshot, options, initialExternalImageClipboardPictureCount, ex);
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

    private static bool HasExternalImageClipboardPasteEvidence(
        MacOsLaunchSmokeSnapshot snapshot,
        int initialExternalImageClipboardPictureCount) =>
        snapshot.ExternalImageClipboardPictureCount > initialExternalImageClipboardPictureCount &&
        snapshot.ExternalImageClipboardPicturePngByteCount > 0;

    private static void WriteReport(
        string reportPath,
        MacOsLaunchSmokeSnapshot snapshot,
        MacOsLaunchSmokeOptions options,
        int initialExternalImageClipboardPictureCount)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var imageClipboardPasteVerified = HasExternalImageClipboardPasteEvidence(
            snapshot,
            initialExternalImageClipboardPictureCount);

        File.WriteAllLines(
            reportPath,
            [
                $"macos_launch_smoke={(IsPassed(snapshot, options, initialExternalImageClipboardPictureCount) ? "passed" : "failed")}",
                $"window_shown={FormatBool(snapshot.WindowShown)}",
                $"window_title={snapshot.WindowTitle}",
                $"display_name={snapshot.DisplayName}",
                $"active_sheet={snapshot.ActiveSheetName}",
                $"sheet_tab_count={snapshot.SheetTabCount}",
                $"viewport_rows={snapshot.ViewportRowCount}",
                $"viewport_columns={snapshot.ViewportColumnCount}",
                $"external_image_clipboard_paste_required={FormatBool(options.VerifyImageClipboardPaste)}",
                $"external_image_clipboard_paste={FormatBool(imageClipboardPasteVerified)}",
                $"external_image_clipboard_picture_count={snapshot.ExternalImageClipboardPictureCount}",
                $"external_image_clipboard_picture_png_bytes={snapshot.ExternalImageClipboardPicturePngByteCount}",
                $"opened_source_path={snapshot.OpenedSourcePath ?? ""}",
                $"is_opening={FormatBool(snapshot.IsOpening)}",
                $"new_sheet_button={FormatBool(snapshot.HasNewSheetButton)}",
                $"toolbar_format_painter_button={FormatBool(snapshot.HasFormatPainterButton)}",
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
        MacOsLaunchSmokeOptions options,
        int initialExternalImageClipboardPictureCount,
        Exception exception)
    {
        WriteReport(reportPath, snapshot, options, initialExternalImageClipboardPictureCount);
        File.AppendAllLines(reportPath, [$"error={exception.GetType().Name}: {exception.Message}"]);
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
